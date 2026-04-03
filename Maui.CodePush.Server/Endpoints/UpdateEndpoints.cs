using Maui.CodePush.Server.Data;
using Maui.CodePush.Server.Services;
using MongoDB.Driver;

namespace Maui.CodePush.Server.Endpoints;

public static class UpdateEndpoints
{
    private static string? GetCdnBaseUrl()
    {
        return Environment.GetEnvironmentVariable("CODEPUSH_CDN_URL");
    }

    private static string GetDownloadUrl(HttpRequest request, string container, string blobPath, Guid id)
    {
        var cdnBase = GetCdnBaseUrl();
        if (!string.IsNullOrEmpty(cdnBase))
            return $"{cdnBase}/{container}/{blobPath}";

        // Fallback for self-hosted (no CDN configured)
        return $"{request.Scheme}://{request.Host}/api/updates/download/{id}";
    }

    public static RouteGroupBuilder MapUpdateEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/updates").WithTags("Updates");

        group.MapGet("/check", CheckForUpdate);
        group.MapGet("/download/{releaseId:guid}", DownloadFallback);

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
            return await CheckForUpdateV2(request, db, appId, platform, releaseVersion, channel);
        }

        // Legacy flow: module + version → Release collection
        if (string.IsNullOrWhiteSpace(module) || string.IsNullOrWhiteSpace(version))
            return Results.BadRequest(new { error = "Either releaseVersion or both module and version are required." });

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
                    downloadUrl = GetDownloadUrl(request, "patches", $"{appId}/patches/{latestRelease.Id}.dll", latestRelease.Id),
                    hash = latestRelease.DllHash,
                    size = latestRelease.DllSize,
                    isMandatory = latestRelease.IsMandatory
                }
            }
        });
    }

    private static async Task<IResult> CheckForUpdateV2(
        HttpRequest request,
        MongoDbContext db,
        Guid appId,
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

        var patches = latestPerModule.Select(p => new
        {
            name = p.ModuleName,
            patchNumber = p.PatchNumber,
            version = p.Version,
            downloadUrl = GetDownloadUrl(request, "patches", $"{p.AppId}/patches/{p.Id}.dll", p.Id),
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

    /// <summary>
    /// Fallback download for self-hosted deployments without CDN.
    /// Only used when CODEPUSH_CDN_URL is not configured.
    /// </summary>
    private static async Task<IResult> DownloadFallback(
        Guid releaseId,
        HttpRequest request,
        MongoDbContext db,
        BlobStorageService blobStorage)
    {
        var appToken = request.Headers["X-CodePush-Token"].ToString();
        if (string.IsNullOrWhiteSpace(appToken))
            return Results.Unauthorized();

        // Try patches first
        var patch = await db.Patches.Find(p => p.Id == releaseId).FirstOrDefaultAsync();
        if (patch is not null)
        {
            var app = await db.Apps.Find(a => a.Id == patch.AppId && a.AppToken == appToken).FirstOrDefaultAsync();
            if (app is null) return Results.Unauthorized();

            var data = await blobStorage.DownloadAsync("patches", $"{patch.AppId}/patches/{releaseId}.dll");
            if (data is null) return Results.NotFound();
            return Results.File(data, "application/octet-stream", $"{patch.ModuleName}.dll");
        }

        // Fallback: legacy releases
        var release = await db.Releases.Find(r => r.Id == releaseId).FirstOrDefaultAsync();
        if (release is null) return Results.NotFound();

        var releaseApp = await db.Apps.Find(a => a.Id == release.AppId && a.AppToken == appToken).FirstOrDefaultAsync();
        if (releaseApp is null) return Results.Unauthorized();

        var releaseData = await blobStorage.DownloadAsync("releases", $"{release.AppId}/{releaseId}.dll");
        if (releaseData is null) return Results.NotFound();
        return Results.File(releaseData, "application/octet-stream", release.FileName);
    }
}
