using Maui.CodePush.Server.Data;
using Maui.CodePush.Server.Data.Entities;
using MongoDB.Driver;

namespace Maui.CodePush.Server.Services;

public class SubscriptionService
{
    private readonly MongoDbContext _db;

    public SubscriptionService(MongoDbContext db)
    {
        _db = db;
    }

    public async Task<Subscription?> GetSubscriptionAsync(Guid accountId)
    {
        return await _db.Subscriptions
            .Find(s => s.AccountId == accountId)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> IsSubscriptionActiveAsync(Guid accountId)
    {
        var sub = await GetSubscriptionAsync(accountId);
        return sub?.Status is SubscriptionStatus.Active or SubscriptionStatus.Trial;
    }

    public async Task<bool> CanDeployAsync(Guid accountId)
    {
        var sub = await GetSubscriptionAsync(accountId);
        if (sub is null)
            return false;

        if (sub.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Trial))
            return false;

        // Enterprise has no hard limit
        if (sub.PlanTier == PlanTier.Enterprise)
            return true;

        // Free tier: hard cap, no overage
        if (sub.PlanTier == PlanTier.Free)
            return sub.PatchInstallsUsed < sub.PatchInstallsLimit;

        // Pro and Business allow overage (billed separately via Stripe)
        return true;
    }

    public async Task IncrementPatchInstallsAsync(Guid accountId, int count)
    {
        var update = Builders<Subscription>.Update.Inc(s => s.PatchInstallsUsed, count);
        await _db.Subscriptions.UpdateOneAsync(s => s.AccountId == accountId, update);
    }

    public async Task ResetPatchInstallsAsync(Guid accountId)
    {
        var update = Builders<Subscription>.Update.Set(s => s.PatchInstallsUsed, 0);
        await _db.Subscriptions.UpdateOneAsync(s => s.AccountId == accountId, update);
    }

    public async Task UpdateFromStripeAsync(
        string stripeCustomerId,
        SubscriptionStatus status,
        PlanTier tier,
        DateTime? periodEnd)
    {
        var filter = Builders<Subscription>.Filter.Eq(s => s.StripeCustomerId, stripeCustomerId);

        var plan = PlanDefinitions.Plans[tier];
        var update = Builders<Subscription>.Update
            .Set(s => s.Status, status)
            .Set(s => s.PlanTier, tier)
            .Set(s => s.Plan, plan.Name)
            .Set(s => s.PatchInstallsLimit, plan.PatchInstallsIncluded)
            .Set(s => s.CurrentPeriodEnd, periodEnd);

        await _db.Subscriptions.UpdateOneAsync(filter, update);
    }
}
