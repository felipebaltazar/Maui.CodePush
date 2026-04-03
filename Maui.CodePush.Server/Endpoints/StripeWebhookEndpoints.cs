using Maui.CodePush.Server.Data;
using Maui.CodePush.Server.Data.Entities;
using Maui.CodePush.Server.Services;
using MongoDB.Driver;

namespace Maui.CodePush.Server.Endpoints;

public static class StripeWebhookEndpoints
{
    public static RouteGroupBuilder MapStripeWebhookEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/webhook").WithTags("Webhook");

        group.MapPost("/stripe", HandleStripeWebhook);

        return group;
    }

    private static async Task<IResult> HandleStripeWebhook(
        HttpContext httpContext,
        MongoDbContext db,
        SubscriptionService subscriptionService)
    {
        var webhookSecret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET");
        if (string.IsNullOrEmpty(webhookSecret))
            return Results.StatusCode(500);

        string body;
        using (var reader = new StreamReader(httpContext.Request.Body))
        {
            body = await reader.ReadToEndAsync();
        }

        var sigHeader = httpContext.Request.Headers["Stripe-Signature"].FirstOrDefault();
        if (string.IsNullOrEmpty(sigHeader))
            return Results.BadRequest(new { error = "Missing Stripe-Signature header." });

        Stripe.Event stripeEvent;
        try
        {
            stripeEvent = Stripe.EventUtility.ConstructEvent(body, sigHeader, webhookSecret);
        }
        catch (Stripe.StripeException)
        {
            return Results.BadRequest(new { error = "Invalid webhook signature." });
        }

        switch (stripeEvent.Type)
        {
            case Stripe.EventTypes.CheckoutSessionCompleted:
                await HandleCheckoutCompleted(stripeEvent, db);
                break;

            case Stripe.EventTypes.CustomerSubscriptionUpdated:
                await HandleSubscriptionUpdated(stripeEvent, subscriptionService);
                break;

            case Stripe.EventTypes.CustomerSubscriptionDeleted:
                await HandleSubscriptionDeleted(stripeEvent, subscriptionService);
                break;

            case Stripe.EventTypes.InvoicePaymentSucceeded:
                await HandleInvoicePaymentSucceeded(stripeEvent, db);
                break;

            case Stripe.EventTypes.InvoicePaymentFailed:
                await HandleInvoicePaymentFailed(stripeEvent, subscriptionService);
                break;
        }

        return Results.Ok(new { received = true });
    }

    private static async Task HandleCheckoutCompleted(Stripe.Event stripeEvent, MongoDbContext db)
    {
        if (stripeEvent.Data.Object is not Stripe.Checkout.Session session)
            return;

        var customerId = session.CustomerId;
        var stripeSubscriptionId = session.SubscriptionId;

        if (string.IsNullOrEmpty(customerId))
            return;

        var filter = Builders<Subscription>.Filter.Eq(s => s.StripeCustomerId, customerId);
        var update = Builders<Subscription>.Update
            .Set(s => s.Status, SubscriptionStatus.Active)
            .Set(s => s.StripeSubscriptionId, stripeSubscriptionId);

        await db.Subscriptions.UpdateOneAsync(filter, update);
    }

    private static async Task HandleSubscriptionUpdated(Stripe.Event stripeEvent, SubscriptionService subscriptionService)
    {
        if (stripeEvent.Data.Object is not Stripe.Subscription stripeSub)
            return;

        var customerId = stripeSub.CustomerId;
        if (string.IsNullOrEmpty(customerId))
            return;

        var status = stripeSub.Status switch
        {
            "active" => SubscriptionStatus.Active,
            "trialing" => SubscriptionStatus.Trial,
            _ => SubscriptionStatus.Inactive
        };

        // Determine plan tier from the price ID
        var priceId = stripeSub.Items?.Data?.FirstOrDefault()?.Price?.Id;
        var tier = ResolvePlanTier(priceId);

        var periodEnd = stripeSub.CurrentPeriodEnd;

        await subscriptionService.UpdateFromStripeAsync(customerId, status, tier, periodEnd);
    }

    private static async Task HandleSubscriptionDeleted(Stripe.Event stripeEvent, SubscriptionService subscriptionService)
    {
        if (stripeEvent.Data.Object is not Stripe.Subscription stripeSub)
            return;

        var customerId = stripeSub.CustomerId;
        if (string.IsNullOrEmpty(customerId))
            return;

        await subscriptionService.UpdateFromStripeAsync(
            customerId,
            SubscriptionStatus.Inactive,
            PlanTier.Free,
            null);
    }

    private static async Task HandleInvoicePaymentSucceeded(Stripe.Event stripeEvent, MongoDbContext db)
    {
        if (stripeEvent.Data.Object is not Stripe.Invoice invoice)
            return;

        var customerId = invoice.CustomerId;
        if (string.IsNullOrEmpty(customerId))
            return;

        // Reset patch installs on new billing period — use Stripe's dates, not local clock
        var periodStart = invoice.PeriodStart;
        var periodEnd = invoice.PeriodEnd;

        var filter = Builders<Subscription>.Filter.Eq(s => s.StripeCustomerId, customerId);
        var update = Builders<Subscription>.Update
            .Set(s => s.PatchInstallsUsed, 0L)
            .Set(s => s.Status, SubscriptionStatus.Active)
            .Set(s => s.CurrentPeriodStart, periodStart)
            .Set(s => s.CurrentPeriodEnd, periodEnd);

        await db.Subscriptions.UpdateOneAsync(filter, update);
    }

    private static async Task HandleInvoicePaymentFailed(Stripe.Event stripeEvent, SubscriptionService subscriptionService)
    {
        if (stripeEvent.Data.Object is not Stripe.Invoice invoice)
            return;

        var customerId = invoice.CustomerId;
        if (string.IsNullOrEmpty(customerId))
            return;

        // Determine current tier from existing subscription or fallback to Free
        await subscriptionService.UpdateFromStripeAsync(
            customerId,
            SubscriptionStatus.Inactive,
            PlanTier.Free,
            null);
    }

    private static PlanTier ResolvePlanTier(string? priceId)
    {
        if (string.IsNullOrEmpty(priceId))
            return PlanTier.Free;

        foreach (var (tier, plan) in PlanDefinitions.Plans)
        {
            if (plan.StripePriceId == priceId)
                return tier;
        }

        return PlanTier.Free;
    }
}
