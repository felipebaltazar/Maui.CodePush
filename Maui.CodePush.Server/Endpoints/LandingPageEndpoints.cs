using Maui.CodePush.Server.Data;
using Maui.CodePush.Server.Data.Entities;
using Maui.CodePush.Server.Services;
using MongoDB.Driver;

namespace Maui.CodePush.Server.Endpoints;

public static class LandingPageEndpoints
{
    public static RouteGroupBuilder MapLandingPageEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/lp").WithTags("Landing Page");

        group.MapPost("/register", Register);
        group.MapGet("/plans", GetPlans);

        return group;
    }

    private static async Task<IResult> Register(
        LandingPageRegisterRequest request,
        MongoDbContext db)
    {
        // Validate inputs
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { error = "Email, password, and name are required." });
        }

        if (request.Password.Length < 6)
            return Results.BadRequest(new { error = "Password must be at least 6 characters." });

        // Parse plan tier
        if (!Enum.TryParse<PlanTier>(request.Plan, ignoreCase: true, out var planTier))
            return Results.BadRequest(new { error = "Invalid plan. Must be free, pro, business, or enterprise." });

        if (planTier == PlanTier.Enterprise)
            return Results.BadRequest(new { error = "Enterprise plan requires contacting sales." });

        // Check email not taken
        var exists = await db.Accounts.Find(a => a.Email == request.Email).AnyAsync();
        if (exists)
            return Results.Conflict(new { error = "An account with this email already exists." });

        var planInfo = PlanDefinitions.Plans[planTier];

        // Create account
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Name = request.Name,
            Company = request.Company,
            ApiKey = TokenService.GenerateRandomToken(),
            CreatedAt = DateTime.UtcNow
        };

        // Create subscription
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Status = planTier == PlanTier.Free ? SubscriptionStatus.Active : SubscriptionStatus.Inactive,
            Plan = planInfo.Name,
            PlanTier = planTier,
            PatchInstallsLimit = planInfo.PatchInstallsIncluded,
            PatchInstallsUsed = 0,
            CurrentPeriodStart = DateTime.UtcNow,
            CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1),
            CreatedAt = DateTime.UtcNow
        };

        string? checkoutUrl = null;

        // For paid plans, create Stripe Checkout session
        if (planTier is PlanTier.Pro or PlanTier.Business)
        {
            var stripeKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
            if (!string.IsNullOrEmpty(stripeKey) && !string.IsNullOrEmpty(planInfo.StripePriceId))
            {
                try
                {
                    Stripe.StripeConfiguration.ApiKey = stripeKey;

                    // Create Stripe customer
                    var customerService = new Stripe.CustomerService();
                    var customer = await customerService.CreateAsync(new Stripe.CustomerCreateOptions
                    {
                        Email = request.Email,
                        Name = request.Name,
                        Metadata = new Dictionary<string, string>
                        {
                            ["accountId"] = account.Id.ToString(),
                            ["company"] = request.Company ?? ""
                        }
                    });

                    subscription.StripeCustomerId = customer.Id;

                    // Create Checkout session with coupon support
                    var sessionService = new Stripe.Checkout.SessionService();
                    var sessionOptions = new Stripe.Checkout.SessionCreateOptions
                    {
                        Customer = customer.Id,
                        Mode = "subscription",
                        AllowPromotionCodes = true,
                        LineItems = new List<Stripe.Checkout.SessionLineItemOptions>
                        {
                            new() { Price = planInfo.StripePriceId, Quantity = 1 }
                        },
                        SuccessUrl = "https://codepush.monkseal.dev/success?session_id={CHECKOUT_SESSION_ID}",
                        CancelUrl = "https://codepush.monkseal.dev/pricing"
                    };

                    // Apply coupon if provided
                    if (!string.IsNullOrWhiteSpace(request.Coupon))
                    {
                        sessionOptions.Discounts = new List<Stripe.Checkout.SessionDiscountOptions>
                        {
                            new() { Coupon = request.Coupon }
                        };
                        // Can't use AllowPromotionCodes with Discounts simultaneously
                        sessionOptions.AllowPromotionCodes = null;
                    }

                    var session = await sessionService.CreateAsync(sessionOptions);
                    checkoutUrl = session.Url;

                    // Mark as inactive until payment completes
                    subscription.Status = SubscriptionStatus.Inactive;
                }
                catch (Stripe.StripeException ex)
                {
                    // Log but don't block registration - they can pay later
                    Console.Error.WriteLine($"Stripe error during registration: {ex.Message}");
                }
            }
        }

        await db.Accounts.InsertOneAsync(account);
        await db.Subscriptions.InsertOneAsync(subscription);

        return Results.Ok(new
        {
            accountId = account.Id,
            email = account.Email,
            plan = planInfo.Name,
            checkoutUrl,
            message = checkoutUrl is not null
                ? "Account created. Complete payment to activate your subscription."
                : "Account created successfully."
        });
    }

    private static IResult GetPlans()
    {
        var plans = PlanDefinitions.Plans.Select(kv => new
        {
            tier = kv.Key.ToString().ToLowerInvariant(),
            name = kv.Value.Name,
            priceMonthly = kv.Value.PriceMonthly,
            priceMonthlyDisplay = kv.Value.PriceMonthly > 0
                ? $"${kv.Value.PriceMonthly / 100}/mo"
                : "Free",
            patchInstallsIncluded = kv.Value.PatchInstallsIncluded,
            overagePricePer2500 = kv.Value.OveragePricePer2500,
            features = kv.Value.Features
        });

        return Results.Ok(new { plans });
    }
}

public record LandingPageRegisterRequest(
    string Email,
    string Password,
    string Name,
    string? Company,
    string Plan,
    string? Coupon = null
);
