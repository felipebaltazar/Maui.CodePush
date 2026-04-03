using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Maui.CodePush.Server.Data.Entities;

public enum SubscriptionStatus
{
    Active,
    Inactive,
    Trial
}

public enum PlanTier
{
    Free,
    Pro,
    Business,
    Enterprise
}

public class Subscription
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; }

    [BsonElement("accountId")]
    [BsonRepresentation(BsonType.String)]
    public Guid AccountId { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public SubscriptionStatus Status { get; set; }

    [BsonElement("plan")]
    public string Plan { get; set; } = string.Empty;

    [BsonElement("planTier")]
    [BsonRepresentation(BsonType.String)]
    public PlanTier PlanTier { get; set; } = PlanTier.Free;

    [BsonElement("stripeCustomerId")]
    public string? StripeCustomerId { get; set; }

    [BsonElement("stripeSubscriptionId")]
    public string? StripeSubscriptionId { get; set; }

    [BsonElement("patchInstallsUsed")]
    public long PatchInstallsUsed { get; set; }

    [BsonElement("patchInstallsLimit")]
    public long PatchInstallsLimit { get; set; } = 5000;

    [BsonElement("currentPeriodStart")]
    public DateTime? CurrentPeriodStart { get; set; }

    [BsonElement("currentPeriodEnd")]
    public DateTime? CurrentPeriodEnd { get; set; }

    [BsonElement("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }
}
