using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Maui.CodePush.Server.Data;
using Maui.CodePush.Server.Data.Entities;
using Maui.CodePush.Server.Services;
using MongoDB.Driver;
using static Maui.CodePush.Server.Endpoints.AuthEndpoints;

namespace Maui.CodePush.Server.Endpoints;

public static class AppReleaseEndpoints
{
    public static RouteGroupBuilder MapAppReleaseEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/apps/{appId:guid}/releases")
            .WithTags("AppReleases")
            .RequireAuthorization();

        group.MapPost("/v2", CreateAppRelease).DisableAntiforgery();
        group.MapGet("/v2", ListAppReleases);
        group.MapGet("/v2/{releaseId:guid}", GetAppRelease);
        group.MapDelete("/v2/{releaseId:guid}", DeleteAppRelease);

        return group;
    }

    private static async Task<IResult> CreateAppRelease(
        Guid appId,
        HttpRequest request,
        ClaimsPrincipal user,
        MongoDbContext db,
        SubscriptionService subscriptionService,
        BlobStorageService blobStorage)
    {
        var accountId = GetAccountId(user);
        if (accountId is null) return Results.Unauthorized();

        var app = await db.Apps.Find(a => a.Id == appId && a.AccountId == accountId.Value).FirstOrDefaultAsync();
        if (app is null)
            return Results.NotFound(new { error = "App not found or not owned by this account." });

        var isActive = await subscriptionService.IsSubscriptionActiveAsync(accountId.Value);
        if (!isActive)
            return Results.StatusCode(403);

        if (!request.HasFormContentType)
            return Results.BadRequest(new { error = "Multipart form data is required." });

        var form = await request.ReadFormAsync();
        var version = form["version"].ToString();
        var platform = form["platform"].ToString();
        var channel = form["channel"].ToString();
        var dependencySnapshotJson = form["dependencySnapshot"].ToString();
        var moduleNamesStr = form["moduleNames"].ToString();
        var files = form.Files.GetFiles("files");

        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(platform))
            return Results.BadRequest(new { error = "version and platform are required." });

        if (string.IsNullOrWhiteSpace(channel))
            channel = "production";

        // Check for existing release with same (AppId, Version, Platform, Channel)
        var exists = await db.AppReleases
            .Find(r => r.AppId == appId && r.Version == version && r.Platform == platform && r.Channel == channel)
            .AnyAsync();
        if (exists)
            return Results.Conflict(new { error = "A release with the same version, platform, and channel already exists." });

        // Parse dependency snapshot
        List<ModuleDependencySnapshot> dependencySnapshot;
        try
        {
            dependencySnapshot = string.IsNullOrWhiteSpace(dependencySnapshotJson)
                ? []
                : JsonSerializer.Deserialize<List<ModuleDependencySnapshot>>(dependencySnapshotJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? [];
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { error = "Invalid dependencySnapshot JSON." });
        }

        var moduleNames = string.IsNullOrWhiteSpace(moduleNamesStr)
            ? []
            : moduleNamesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        if (files.Count > 0 && moduleNames.Count != files.Count)
            return Results.BadRequest(new { error = "moduleNames count must match the number of uploaded files." });

        var releaseId = Guid.NewGuid();

        var modules = new List<object>();

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var moduleName = moduleNames[i];

            byte[] fileBytes;
            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);
                fileBytes = memoryStream.ToArray();
            }

            var hash = SHA256.HashData(fileBytes);
            var dllHash = Convert.ToHexStringLower(hash);

            await blobStorage.UploadReleaseAsync(appId, releaseId, moduleName, fileBytes);

            modules.Add(new { moduleName, dllHash, dllSize = fileBytes.Length });
        }

        var appRelease = new AppRelease
        {
            Id = releaseId,
            AppId = appId,
            Version = version,
            Platform = platform,
            Channel = channel,
            DependencySnapshot = dependencySnapshot,
            GitTag = $"v{version}",
            CreatedAt = DateTime.UtcNow
        };

        await db.AppReleases.InsertOneAsync(appRelease);

        return Results.Ok(new
        {
            releaseId = appRelease.Id,
            version = appRelease.Version,
            platform = appRelease.Platform,
            channel = appRelease.Channel,
            gitTag = appRelease.GitTag,
            modules
        });
    }

    private static async Task<IResult> ListAppReleases(
        Guid appId,
        ClaimsPrincipal user,
        MongoDbContext db)
    {
        var accountId = GetAccountId(user);
        if (accountId is null) return Results.Unauthorized();

        var app = await db.Apps.Find(a => a.Id == appId && a.AccountId == accountId.Value).FirstOrDefaultAsync();
        if (app is null) return Results.NotFound();

        var releases = await db.AppReleases
            .Find(r => r.AppId == appId)
            .SortByDescending(r => r.CreatedAt)
            .ToListAsync();

        var result = releases.Select(r => new
        {
            releaseId = r.Id,
            version = r.Version,
            platform = r.Platform,
            channel = r.Channel,
            gitTag = r.GitTag,
            createdAt = r.CreatedAt,
            moduleCount = r.DependencySnapshot.Count
        });

        return Results.Ok(result);
    }

    private static async Task<IResult> GetAppRelease(
        Guid appId,
        Guid releaseId,
        ClaimsPrincipal user,
        MongoDbContext db)
    {
        var accountId = GetAccountId(user);
        if (accountId is null) return Results.Unauthorized();

        var app = await db.Apps.Find(a => a.Id == appId && a.AccountId == accountId.Value).FirstOrDefaultAsync();
        if (app is null) return Results.NotFound();

        var release = await db.AppReleases.Find(r => r.Id == releaseId && r.AppId == appId).FirstOrDefaultAsync();
        if (release is null) return Results.NotFound();

        return Results.Ok(new
        {
            releaseId = release.Id,
            version = release.Version,
            platform = release.Platform,
            channel = release.Channel,
            gitTag = release.GitTag,
            createdAt = release.CreatedAt,
            dependencySnapshot = release.DependencySnapshot.Select(d => new
            {
                moduleName = d.ModuleName,
                dllHash = d.DllHash,
                dllSize = d.DllSize,
                assemblyReferences = d.AssemblyReferences.Select(a => new
                {
                    name = a.Name,
                    version = a.Version
                })
            })
        });
    }

    private static async Task<IResult> DeleteAppRelease(
        Guid appId,
        Guid releaseId,
        ClaimsPrincipal user,
        MongoDbContext db,
        BlobStorageService blobStorage)
    {
        var accountId = GetAccountId(user);
        if (accountId is null) return Results.Unauthorized();

        var app = await db.Apps.Find(a => a.Id == appId && a.AccountId == accountId.Value).FirstOrDefaultAsync();
        if (app is null) return Results.NotFound();

        var release = await db.AppReleases.Find(r => r.Id == releaseId && r.AppId == appId).FirstOrDefaultAsync();
        if (release is null) return Results.NotFound();

        // Delete uploaded release files
        foreach (var module in release.DependencySnapshot)
        {
            await blobStorage.DeleteAsync("releases", $"{appId}/releases/{releaseId}/{module.ModuleName}.dll");
        }

        // Delete all patches for this release and their files
        var patches = await db.Patches.Find(p => p.ReleaseId == releaseId).ToListAsync();
        foreach (var patch in patches)
        {
            await blobStorage.DeleteAsync("patches", $"{appId}/patches/{patch.Id}.dll");
        }

        await db.Patches.DeleteManyAsync(p => p.ReleaseId == releaseId);
        await db.AppReleases.DeleteOneAsync(r => r.Id == releaseId);

        return Results.NoContent();
    }
}
