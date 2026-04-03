using System.Security.Claims;
using System.Security.Cryptography;
using Maui.CodePush.Server.Data;
using Maui.CodePush.Server.Data.Entities;
using Maui.CodePush.Server.Services;
using MongoDB.Driver;
using static Maui.CodePush.Server.Endpoints.AuthEndpoints;

namespace Maui.CodePush.Server.Endpoints;

public static class ReleaseEndpoints
{
    public static RouteGroupBuilder MapReleaseEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/apps/{appId:guid}/releases")
            .WithTags("Releases");

        group.MapPost("/", CreateRelease)
            .RequireAuthorization()
            .DisableAntiforgery();

        group.MapGet("/", ListReleases)
            .RequireAuthorization();

        group.MapDelete("/{releaseId:guid}", DeleteRelease)
            .RequireAuthorization();

        return group;
    }

    private static async Task<IResult> CreateRelease(
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
        var file = form.Files.GetFile("file");
        var moduleName = form["moduleName"].ToString();
        var version = form["version"].ToString();
        var platform = form["platform"].ToString();
        var channel = form["channel"].ToString();
        var isMandatoryStr = form["isMandatory"].ToString();
        var rolloutPercentageStr = form["rolloutPercentage"].ToString();

        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "A DLL file is required." });

        if (file.Length > 50 * 1024 * 1024)
            return Results.BadRequest(new { error = "File size exceeds the 50MB limit." });

        if (string.IsNullOrWhiteSpace(moduleName) ||
            string.IsNullOrWhiteSpace(version) ||
            string.IsNullOrWhiteSpace(platform))
            return Results.BadRequest(new { error = "moduleName, version, and platform are required." });

        if (string.IsNullOrWhiteSpace(channel))
            channel = "production";

        var isMandatory = bool.TryParse(isMandatoryStr, out var mandatory) && mandatory;
        var rolloutPercentage = int.TryParse(rolloutPercentageStr, out var rollout) ? rollout : 100;

        var releaseId = Guid.NewGuid();

        byte[] fileBytes;
        using (var memoryStream = new MemoryStream())
        {
            await file.CopyToAsync(memoryStream);
            fileBytes = memoryStream.ToArray();
        }

        var hash = SHA256.HashData(fileBytes);
        var dllHash = Convert.ToHexStringLower(hash);

        await blobStorage.UploadPatchAsync(appId, releaseId, fileBytes);

        var release = new Release
        {
            Id = releaseId,
            AppId = appId,
            ModuleName = moduleName,
            Version = version,
            Platform = platform,
            Channel = channel,
            DllHash = dllHash,
            DllSize = fileBytes.Length,
            FileName = file.FileName,
            IsMandatory = isMandatory,
            RolloutPercentage = rolloutPercentage,
            CreatedAt = DateTime.UtcNow
        };

        await db.Releases.InsertOneAsync(release);

        return Results.Ok(new
        {
            releaseId = release.Id,
            moduleName = release.ModuleName,
            version = release.Version,
            platform = release.Platform,
            channel = release.Channel,
            dllHash = release.DllHash,
            dllSize = release.DllSize
        });
    }

    private static async Task<IResult> ListReleases(Guid appId, ClaimsPrincipal user, MongoDbContext db)
    {
        var accountId = GetAccountId(user);
        if (accountId is null) return Results.Unauthorized();

        var app = await db.Apps.Find(a => a.Id == appId && a.AccountId == accountId.Value).FirstOrDefaultAsync();
        if (app is null) return Results.NotFound();

        var releases = await db.Releases
            .Find(r => r.AppId == appId)
            .SortByDescending(r => r.CreatedAt)
            .Project(r => new
            {
                releaseId = r.Id,
                moduleName = r.ModuleName,
                version = r.Version,
                platform = r.Platform,
                channel = r.Channel,
                dllHash = r.DllHash,
                dllSize = r.DllSize,
                fileName = r.FileName,
                isMandatory = r.IsMandatory,
                rolloutPercentage = r.RolloutPercentage,
                createdAt = r.CreatedAt
            })
            .ToListAsync();

        return Results.Ok(releases);
    }

    private static async Task<IResult> DeleteRelease(
        Guid appId, Guid releaseId, ClaimsPrincipal user, MongoDbContext db, BlobStorageService blobStorage)
    {
        var accountId = GetAccountId(user);
        if (accountId is null) return Results.Unauthorized();

        var app = await db.Apps.Find(a => a.Id == appId && a.AccountId == accountId.Value).FirstOrDefaultAsync();
        if (app is null) return Results.NotFound();

        var release = await db.Releases.Find(r => r.Id == releaseId && r.AppId == appId).FirstOrDefaultAsync();
        if (release is null) return Results.NotFound();

        await blobStorage.DeleteAsync("patches", $"{appId}/patches/{releaseId}.dll");

        await db.Releases.DeleteOneAsync(r => r.Id == releaseId);

        return Results.NoContent();
    }
}
