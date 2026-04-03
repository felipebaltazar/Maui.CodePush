using Maui.CodePush.Server.Data.Entities;

namespace Maui.CodePush.Server.Services;

public static class PlanDefinitions
{
    public static readonly Dictionary<PlanTier, PlanInfo> Plans = new()
    {
        [PlanTier.Free] = new("Free", 0, 5_000, 0, null, [
            "5,000 Patch Installs", "Unlimited Apps & Releases", "Community Support"
        ]),
        [PlanTier.Pro] = new("Pro", 2000, 50_000, 40,
            Environment.GetEnvironmentVariable("STRIPE_PRICE_PRO") ?? "price_1TI131DuJYKBBQ2UtDxVjTqC",
            [
                "50,000 Patch Installs", "$1 per 2,500 overage", "CI for private repos (100h)",
                "Console", "Collaboration", "Patch Rollbacks", "Signed Patches",
                "Usage Notifications", "Staging", "Email Support", "Admin + Developer roles"
            ]),
        [PlanTier.Business] = new("Business", 40000, 1_000_000, 40,
            Environment.GetEnvironmentVariable("STRIPE_PRICE_BUSINESS") ?? "price_1TI133DuJYKBBQ2UVPUjHwm9",
            [
                "1,000,000 Patch Installs", "$1 per 2,500 overage", "CI for private repos (100h)",
                "Analytics", "Invoice Billing", "+Viewer role", "Private Discord Support"
            ]),
        [PlanTier.Enterprise] = new("Enterprise", 0, 0, 0, null, [
            "Custom Patch Installs", "Custom CI hours", "SAML", "Audit Logs",
            "+App Manager role", "Private Discord/Slack Support"
        ])
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
