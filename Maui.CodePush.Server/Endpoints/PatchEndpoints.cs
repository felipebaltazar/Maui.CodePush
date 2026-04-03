using System.Security.Claims;
using System.Security.Cryptography;
using Maui.CodePush.Server.Data;
using Maui.CodePush.Server.Data.Entities;
using Maui.CodePush.Server.Services;
using MongoDB.Driver;
using static Maui.CodePush.Server.Endpoints.AuthEndpoints;

namespace Maui.CodePush.Server.Endpoints;

public static class PatchEndpoints
{
    public static RouteGroupBuilder MapPatchEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/apps/{appId:guid}/releases/{releaseId:guid}/patches")
            .WithTags("Patches")
            .RequireAuthorization();

        group.MapPost("/", CreatePatch).DisableAntiforgery();
        group.MapGet("/", ListPatches);
        group.MapDelete("/{patchId:guid}", DeletePatch);

        return group;
    }

    private static async Task<IResult> CreatePatch(
        Guid appId,
        Guid releaseId,
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

        var release = await db.AppReleases.Find(r => r.Id == releaseId && r.AppId == appId).FirstOrDefaultAsync();
        if (release is null)
            return Results.NotFound(new { error = "Release not found." });

        if (!request.HasFormContentType)
            return Results.BadRequest(new { error = "Multipart form data is required." });

        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        var moduleName = form["moduleName"].ToString();
        var channel = form["channel"].ToString();
        var isMandatoryStr = form["isMandatory"].ToString();
        var rolloutPercentageStr = form["rolloutPercentage"].ToString();

        if (file is null || file.Length == 0)
            return Results.BadRequest(new { error = "A DLL file is required." });

        if (string.IsNullOrWhiteSpace(moduleName))
            return Results.BadRequest(new { error = "moduleName is required." });

        if (string.IsNullOrWhiteSpace(channel))
            channel = release.Channel;

        var isMandatory = bool.TryParse(isMandatoryStr, out var mandatory) && mandatory;
        var rolloutPercentage = int.TryParse(rolloutPercentageStr, out var rollout) ? rollout : 100;

        // Find max patch number for (ReleaseId, ModuleName) and increment
        var latestPatch = await db.Patches
            .Find(p => p.ReleaseId == releaseId && p.ModuleName == moduleName)
            .SortByDescending(p => p.PatchNumber)
            .FirstOrDefaultAsync();

        var patchNumber = (latestPatch?.PatchNumber ?? 0) + 1;

        // Deactivate previous active patches for same (ReleaseId, ModuleName)
        var deactivateFilter = Builders<Patch>.Filter.And(
            Builders<Patch>.Filter.Eq(p => p.ReleaseId, releaseId),
            Builders<Patch>.Filter.Eq(p => p.ModuleName, moduleName),
            Builders<Patch>.Filter.Eq(p => p.IsActive, true));
        var deactivateUpdate = Builders<Patch>.Update.Set(p => p.IsActive, false);
        await db.Patches.UpdateManyAsync(deactivateFilter, deactivateUpdate);

        // Read file and compute hash
        byte[] fileBytes;
        using (var memoryStream = new MemoryStream())
        {
            await file.CopyToAsync(memoryStream);
            fileBytes = memoryStream.ToArray();
        }

        var hash = SHA256.HashData(fileBytes);
        var dllHash = Convert.ToHexStringLower(hash);

        var patchId = Guid.NewGuid();
        await blobStorage.UploadPatchAsync(appId, patchId, fileBytes);

        var patch = new Patch
        {
            Id = patchId,
            AppId = appId,
            ReleaseId = releaseId,
            PatchNumber = patchNumber,
            ModuleName = moduleName,
            Version = $"{release.Version}+{patchNumber}",
            Platform = release.Platform,
            Channel = channel,
            DllHash = dllHash,
            DllSize = fileBytes.Length,
            FileName = file.FileName,
            IsMandatory = isMandatory,
            RolloutPercentage = rolloutPercentage,
            GitTag = $"patch-v{release.Version}-{patchNumber}",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await db.Patches.InsertOneAsync(patch);

        return Results.Ok(new
        {
            patchId = patch.Id,
            releaseId = patch.ReleaseId,
            patchNumber = patch.PatchNumber,
            moduleName = patch.ModuleName,
            version = patch.Version,
            dllHash = patch.DllHash,
            dllSize = patch.DllSize,
            gitTag = patch.GitTag
        });
    }

    private static async Task<IResult> ListPatches(
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

        var patches = await db.Patches
            .Find(p => p.ReleaseId == releaseId)
            .SortByDescending(p => p.PatchNumber)
            .Project(p => new
            {
                patchId = p.Id,
                patchNumber = p.PatchNumber,
                moduleName = p.ModuleName,
                version = p.Version,
                dllHash = p.DllHash,
                dllSize = p.DllSize,
                isActive = p.IsActive,
                createdAt = p.CreatedAt
            })
            .ToListAsync();

        return Results.Ok(patches);
    }

    private static async Task<IResult> DeletePatch(
        Guid appId,
        Guid releaseId,
        Guid patchId,
        ClaimsPrincipal user,
        MongoDbContext db,
        BlobStorageService blobStorage)
    {
        var accountId = GetAccountId(user);
        if (accountId is null) return Results.Unauthorized();

        var app = await db.Apps.Find(a => a.Id == appId && a.AccountId == accountId.Value).FirstOrDefaultAsync();
        if (app is null) return Results.NotFound();

        var patch = await db.Patches.Find(p => p.Id == patchId && p.ReleaseId == releaseId && p.AppId == appId).FirstOrDefaultAsync();
        if (patch is null) return Results.NotFound();

        await blobStorage.DeleteAsync("patches", $"{appId}/patches/{patchId}.dll");

        await db.Patches.DeleteOneAsync(p => p.Id == patchId);

        return Results.NoContent();
    }
}
