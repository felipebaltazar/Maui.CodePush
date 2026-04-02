using Maui.CodePush.Server.Data;
using MongoDB.Driver;

namespace Maui.CodePush.Server.Endpoints;

public static class UpdateEndpoints
{
    public static RouteGroupBuilder MapUpdateEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/updates").WithTags("Updates");

        group.MapGet("/check", CheckForUpdate);
        group.MapGet("/download/{releaseId:guid}", DownloadRelease);

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

        var baseUrl = $"{request.Scheme}://{request.Host}";

        return Results.Ok(new
        {
            updateAvailable = true,
            modules = new[]
            {
                new
                {
                    name = latestRelease.ModuleName,
                    version = latestRelease.Version,
                    downloadUrl = $"{baseUrl}/api/updates/download/{latestRelease.Id}",
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

        // Find all active patches for this release
        var activePatches = await db.Patches
            .Find(p => p.ReleaseId == appRelease.Id && p.IsActive)
            .SortByDescending(p => p.PatchNumber)
            .ToListAsync();

        // Group by moduleName, take latest per module
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

        var baseUrl = $"{request.Scheme}://{request.Host}";

        var patches = latestPerModule.Select(p => new
        {
            name = p.ModuleName,
            patchNumber = p.PatchNumber,
            version = p.Version,
            downloadUrl = $"{baseUrl}/api/updates/download/{p.Id}",
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

    private static async Task<IResult> DownloadRelease(
        Guid releaseId,
        HttpRequest request,
        MongoDbContext db,
        IConfiguration configuration)
    {
        var appToken = request.Headers["X-CodePush-Token"].ToString();
        if (string.IsNullOrWhiteSpace(appToken))
            return Results.Unauthorized();

        var uploadsPath = configuration["Uploads:Path"] ?? "uploads";

        // Try Patches collection first (new model)
        var patch = await db.Patches.Find(p => p.Id == releaseId).FirstOrDefaultAsync();
        if (patch is not null)
        {
            var patchApp = await db.Apps.Find(a => a.Id == patch.AppId && a.AppToken == appToken).FirstOrDefaultAsync();
            if (patchApp is null)
                return Results.Unauthorized();

            var patchFilePath = Path.Combine(uploadsPath, patch.AppId.ToString(), "patches", $"{releaseId}.dll");
            if (!File.Exists(patchFilePath))
                return Results.NotFound(new { error = "Patch file not found on disk." });

            var patchBytes = await File.ReadAllBytesAsync(patchFilePath);
            return Results.File(patchBytes, "application/octet-stream", patch.FileName);
        }

        // Fallback to legacy Releases collection
        var release = await db.Releases.Find(r => r.Id == releaseId).FirstOrDefaultAsync();
        if (release is null)
            return Results.NotFound();

        var appEntity = await db.Apps.Find(a => a.Id == release.AppId && a.AppToken == appToken).FirstOrDefaultAsync();
        if (appEntity is null)
            return Results.Unauthorized();

        var filePath = Path.Combine(uploadsPath, release.AppId.ToString(), $"{releaseId}.dll");

        if (!File.Exists(filePath))
            return Results.NotFound(new { error = "Release file not found on disk." });

        var fileBytes = await File.ReadAllBytesAsync(filePath);
        return Results.File(fileBytes, "application/octet-stream", release.FileName);
    }
}
