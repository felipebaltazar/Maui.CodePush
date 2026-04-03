using System.Security.Claims;
using Maui.CodePush.Server.Data;
using Maui.CodePush.Server.Data.Entities;
using Maui.CodePush.Server.Services;
using MongoDB.Driver;
using static Maui.CodePush.Server.Endpoints.AuthEndpoints;

namespace Maui.CodePush.Server.Endpoints;

public static class AppEndpoints
{
    public static RouteGroupBuilder MapAppEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/apps")
            .WithTags("Apps")
            .RequireAuthorization();

        group.MapPost("/", CreateApp);
        group.MapGet("/", ListApps);
        group.MapGet("/{appId:guid}", GetApp);
        group.MapDelete("/{appId:guid}", DeleteApp);

        return group;
    }

    private static async Task<IResult> CreateApp(
        CreateAppRequest request,
        ClaimsPrincipal user,
        MongoDbContext db,
        SubscriptionService subscriptionService)
    {
        var accountId = GetAccountId(user);
        if (accountId is null) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.PackageName) ||
            string.IsNullOrWhiteSpace(request.DisplayName))
            return Results.BadRequest(new { error = "PackageName and DisplayName are required." });

        var isActive = await subscriptionService.IsSubscriptionActiveAsync(accountId.Value);
        if (!isActive)
            return Results.StatusCode(403);

        var packageExists = await db.Apps.Find(a => a.PackageName == request.PackageName).AnyAsync();
        if (packageExists)
            return Results.Conflict(new { error = "Package name is already taken." });

        var appEntity = new App
        {
            Id = Guid.NewGuid(),
            AccountId = accountId.Value,
            PackageName = request.PackageName,
            DisplayName = request.DisplayName,
            AppToken = TokenService.GenerateRandomToken(),
            CreatedAt = DateTime.UtcNow
        };

        await db.Apps.InsertOneAsync(appEntity);

        return Results.Ok(new
        {
            appId = appEntity.Id,
            packageName = appEntity.PackageName,
            displayName = appEntity.DisplayName,
            appToken = appEntity.AppToken
        });
    }

    private static async Task<IResult> ListApps(ClaimsPrincipal user, MongoDbContext db)
    {
        var accountId = GetAccountId(user);
        if (accountId is null) return Results.Unauthorized();

        var apps = await db.Apps.Find(a => a.AccountId == accountId.Value)
            .Project(a => new
            {
                appId = a.Id,
                packageName = a.PackageName,
                displayName = a.DisplayName,
                appToken = a.AppToken,
                createdAt = a.CreatedAt
            })
            .ToListAsync();

        return Results.Ok(apps);
    }

    private static async Task<IResult> GetApp(Guid appId, ClaimsPrincipal user, MongoDbContext db)
    {
        var accountId = GetAccountId(user);
        if (accountId is null) return Results.Unauthorized();

        var app = await db.Apps.Find(a => a.Id == appId && a.AccountId == accountId.Value).FirstOrDefaultAsync();
        if (app is null) return Results.NotFound();

        return Results.Ok(new
        {
            appId = app.Id,
            packageName = app.PackageName,
            displayName = app.DisplayName,
            appToken = app.AppToken,
            createdAt = app.CreatedAt
        });
    }

    private static async Task<IResult> DeleteApp(
        Guid appId, ClaimsPrincipal user, MongoDbContext db, BlobStorageService blobStorage)
    {
        var accountId = GetAccountId(user);
        if (accountId is null) return Results.Unauthorized();

        var app = await db.Apps.Find(a => a.Id == appId && a.AccountId == accountId.Value).FirstOrDefaultAsync();
        if (app is null) return Results.NotFound();

        // Delete releases and uploaded files
        await blobStorage.DeleteAllForAppAsync(appId);

        await db.AppReleases.DeleteManyAsync(r => r.AppId == appId);
        await db.Patches.DeleteManyAsync(p => p.AppId == appId);
        await db.Releases.DeleteManyAsync(r => r.AppId == appId);
        await db.Apps.DeleteOneAsync(a => a.Id == appId);

        return Results.NoContent();
    }
}

public record CreateAppRequest(string PackageName, string DisplayName);
