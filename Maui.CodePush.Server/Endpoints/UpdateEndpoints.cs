using Maui.CodePush.Server.Data;
using Maui.CodePush.Server.Services;
using MongoDB.Driver;

namespace Maui.CodePush.Server.Endpoints;

public static class UpdateEndpoints
{
    private static string GetCdnBaseUrl()
    {
        return Environment.GetEnvironmentVariable("CODEPUSH_CDN_URL")
            ?? "https://cdn.monkseal.dev";
    }

    public static RouteGroupBuilder MapUpdateEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/updates").WithTags("Updates");

        group.MapGet("/check", CheckForUpdate);

        return group;
    }

    private static async Task<IResult> CheckForUpdate(
        HttpRequest request,
        MongoDbContext db,
        string app,
        string platform,
        string? releaseVersion = null,
        string? module = null,
        string? version = null,
        string? channel = "production")
    {
        var appToken = request.Headers["X-CodePush-Token"].ToString();
        if (string.IsNullOrWhiteSpace(appToken))
            return Results.Unauthorized();

        if (!Guid.TryParse(app, out var appId))
            return Results.BadRequest(new { error = "Invalid app ID." });

        var appEntity = await db.Apps.Find(a => a.Id == appId && a.AppToken == appToken).FirstOrDefaultAsync();
        if (appEntity is null)
            return Results.Unauthorized();

        channel ??= "production";

        // New flow: releaseVersion present → AppRelease + Patches
        if (!string.IsNullOrWhiteSpace(releaseVersion))
        {
            return await CheckForUpdateV2(db, appId, appToken, platform, releaseVersion, channel);
        }

        // Legacy flow: module + version → Release collection
        if (string.IsNullOrWhiteSpace(module) || string.IsNullOrWhiteSpace(version))
            return Results.BadRequest(new { error = "Either releaseVersion or both module and version are required." });

        var cdnBase = GetCdnBaseUrl();

        var latestRelease = await db.Releases
            .Find(r => r.AppId == appId
                && r.ModuleName == module
                && r.Platform == platform
                && r.Channel == channel)
            .SortByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();

        if (latestRelease is null || latestRelease.Version == version)
        {
            return Results.Ok(new
            {
                updateAvailable = false,
                modules = Array.Empty<object>()
            });
        }

        return Results.Ok(new
        {
            updateAvailable = true,
            modules = new[]
            {
                new
                {
                    name = latestRelease.ModuleName,
                    version = latestRelease.Version,
                    downloadUrl = $"{cdnBase}/patches/{appId}/patches/{latestRelease.Id}.dll?token={appToken}",
                    hash = latestRelease.DllHash,
                    size = latestRelease.DllSize,
                    isMandatory = latestRelease.IsMandatory
                }
            }
        });
    }

    private static async Task<IResult> CheckForUpdateV2(
        MongoDbContext db,
        Guid appId,
        string appToken,
        string platform,
        string releaseVersion,
        string channel)
    {
        var appRelease = await db.AppReleases
            .Find(r => r.AppId == appId
                && r.Version == releaseVersion
                && r.Platform == platform
                && r.Channel == channel)
            .FirstOrDefaultAsync();

        if (appRelease is null)
        {
            return Results.Ok(new
            {
                updateAvailable = false,
                patches = Array.Empty<object>()
            });
        }

        var activePatches = await db.Patches
            .Find(p => p.ReleaseId == appRelease.Id && p.IsActive)
            .SortByDescending(p => p.PatchNumber)
            .ToListAsync();

        var latestPerModule = activePatches
            .GroupBy(p => p.ModuleName)
            .Select(g => g.First())
            .ToList();

        if (latestPerModule.Count == 0)
        {
            return Results.Ok(new
            {
                updateAvailable = false,
                patches = Array.Empty<object>()
            });
        }

        var cdnBase = GetCdnBaseUrl();

        var patches = latestPerModule.Select(p => new
        {
            name = p.ModuleName,
            patchNumber = p.PatchNumber,
            version = p.Version,
            downloadUrl = $"{cdnBase}/patches/{p.AppId}/patches/{p.Id}.dll?token={appToken}",
            hash = p.DllHash,
            size = p.DllSize,
            isMandatory = p.IsMandatory
        }).ToList();

        return Results.Ok(new
        {
            updateAvailable = true,
            releaseVersion = appRelease.Version,
            patches
        });
    }
}
