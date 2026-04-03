using Maui.CodePush.Server.Data.Entities;

namespace Maui.CodePush.Server.Services;

public static class PlanDefinitions
{
    public static readonly Dictionary<PlanTier, PlanInfo> Plans = new()
    {
        [PlanTier.Free] = new("Free", 0, 5_000, 0, null, new[]
        {
            "5,000 Patch Installs", "Unlimited Apps & Releases", "Community Support"
        }),
        [PlanTier.Pro] = new("Pro", 2000, 50_000, 40, "price_pro_monthly_id", new[]
        {
            "50,000 Patch Installs", "$1 per 2,500 overage", "CI for private repos (100h)",
            "Console", "Collaboration", "Patch Rollbacks", "Signed Patches",
            "Usage Notifications", "Staging", "Email Support", "Admin + Developer roles"
        }),
        [PlanTier.Business] = new("Business", 40000, 1_000_000, 40, "price_business_monthly_id", new[]
        {
            "1,000,000 Patch Installs", "$1 per 2,500 overage", "CI for private repos (100h)",
            "Analytics", "Invoice Billing", "+Viewer role", "Private Discord Support"
        }),
        [PlanTier.Enterprise] = new("Enterprise", 0, 0, 0, null, new[]
        {
            "Custom Patch Installs", "Custom CI hours", "SAML", "Audit Logs",
            "+App Manager role", "Private Discord/Slack Support"
        })
    };
}

public record PlanInfo(
    string Name,
    int PriceMonthly,
    long PatchInstallsIncluded,
    int OveragePricePer2500,
    string? StripePriceId,
    string[] Features
);
