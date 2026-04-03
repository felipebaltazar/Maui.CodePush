using System.Security.Claims;
using System.Security.Cryptography;
using Maui.CodePush.Server.Data;
using Maui.CodePush.Server.Data.Entities;
using Maui.CodePush.Server.Services;
using MongoDB.Driver;

namespace Maui.CodePush.Server.Endpoints;

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", Login);
        group.MapGet("/me", GetMe).RequireAuthorization();

        // Device authorization flow (CLI browser login)
        group.MapPost("/device", CreateDeviceCode);
        group.MapPost("/device/token", PollDeviceToken);
        group.MapPost("/device/authorize", AuthorizeDevice);

        return group;
    }

    // ── Standard login ──────────────────────────────────────────

    private static async Task<IResult> Login(
        LoginRequest request,
        MongoDbContext db,
        TokenService tokenService)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Results.BadRequest(new { error = "Email and password are required." });

        var account = await db.Accounts.Find(a => a.Email == request.Email).FirstOrDefaultAsync();
        if (account is null || !BCrypt.Net.BCrypt.Verify(request.Password, account.PasswordHash))
            return Results.Unauthorized();

        var token = tokenService.GenerateJwtToken(account.Id, account.Email, account.Name);
        var expiresAt = DateTime.UtcNow.AddDays(7);

        return Results.Ok(new { token, expiresAt });
    }

    private static async Task<IResult> GetMe(ClaimsPrincipal user, MongoDbContext db)
    {
        var accountId = GetAccountId(user);
        if (accountId is null) return Results.Unauthorized();

        var account = await db.Accounts.Find(a => a.Id == accountId.Value).FirstOrDefaultAsync();
        if (account is null) return Results.NotFound();

        var subscription = await db.Subscriptions.Find(s => s.AccountId == accountId.Value).FirstOrDefaultAsync();

        return Results.Ok(new
        {
            accountId = account.Id,
            email = account.Email,
            name = account.Name,
            apiKey = account.ApiKey,
            createdAt = account.CreatedAt,
            subscription = subscription is null ? null : new
            {
                status = subscription.Status.ToString(),
                plan = subscription.Plan,
                expiresAt = subscription.ExpiresAt
            }
        });
    }

    // ── Device authorization flow ───────────────────────────────

    private static async Task<IResult> CreateDeviceCode(MongoDbContext db)
    {
        var deviceCode = GenerateSecureCode(32);
        var userCode = GenerateUserCode();

        var device = new DeviceCode
        {
            Id = Guid.NewGuid(),
            Code = deviceCode,
            UserCode = userCode,
            Status = DeviceCodeStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddMinutes(15),
            CreatedAt = DateTime.UtcNow
        };

        await db.DeviceCodes.InsertOneAsync(device);

        return Results.Ok(new
        {
            deviceCode,
            userCode,
            verificationUrl = $"https://monkseal.dev/auth/device?code={userCode}",
            expiresIn = 900,
            interval = 3
        });
    }

    private static async Task<IResult> PollDeviceToken(
        DeviceTokenRequest request,
        MongoDbContext db,
        TokenService tokenService)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceCode))
            return Results.BadRequest(new { error = "deviceCode is required." });

        var device = await db.DeviceCodes
            .Find(d => d.Code == request.DeviceCode)
            .FirstOrDefaultAsync();

        if (device is null)
            return Results.BadRequest(new { error = "invalid_device_code" });

        if (device.ExpiresAt < DateTime.UtcNow)
            return Results.BadRequest(new { error = "expired_token" });

        if (device.Status == DeviceCodeStatus.Pending)
            return Results.Ok(new { error = "authorization_pending" });

        if (device.Status != DeviceCodeStatus.Authorized || device.AccountId is null)
            return Results.BadRequest(new { error = "expired_token" });

        var account = await db.Accounts
            .Find(a => a.Id == device.AccountId.Value)
            .FirstOrDefaultAsync();

        if (account is null)
            return Results.BadRequest(new { error = "account_not_found" });

        var token = tokenService.GenerateJwtToken(account.Id, account.Email, account.Name);

        await db.DeviceCodes.DeleteOneAsync(d => d.Id == device.Id);

        return Results.Ok(new
        {
            token,
            apiKey = account.ApiKey,
            email = account.Email,
            name = account.Name,
            expiresAt = DateTime.UtcNow.AddDays(7)
        });
    }

    private static async Task<IResult> AuthorizeDevice(
        AuthorizeDeviceRequest request,
        MongoDbContext db)
    {
        if (string.IsNullOrWhiteSpace(request.UserCode) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.BadRequest(new { error = "userCode, email, and password are required." });
        }

        var device = await db.DeviceCodes
            .Find(d => d.UserCode == request.UserCode && d.Status == DeviceCodeStatus.Pending)
            .FirstOrDefaultAsync();

        if (device is null || device.ExpiresAt < DateTime.UtcNow)
            return Results.BadRequest(new { error = "Invalid or expired code." });

        var account = await db.Accounts.Find(a => a.Email == request.Email).FirstOrDefaultAsync();
        if (account is null || !BCrypt.Net.BCrypt.Verify(request.Password, account.PasswordHash))
            return Results.Unauthorized();

        var update = Builders<DeviceCode>.Update
            .Set(d => d.Status, DeviceCodeStatus.Authorized)
            .Set(d => d.AccountId, account.Id);

        await db.DeviceCodes.UpdateOneAsync(d => d.Id == device.Id, update);

        return Results.Ok(new { authorized = true, email = account.Email });
    }

    // ── Helpers ─────────────────────────────────────────────────

    internal static Guid? GetAccountId(ClaimsPrincipal user)
    {
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    private static string GenerateSecureCode(int bytes) =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(bytes));

    private static string GenerateUserCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var code = new char[8];
        var rng = RandomNumberGenerator.GetBytes(8);
        for (var i = 0; i < 8; i++)
            code[i] = chars[rng[i] % chars.Length];
        return $"{new string(code, 0, 4)}-{new string(code, 4, 4)}";
    }
}

public record LoginRequest(string Email, string Password);
public record DeviceTokenRequest(string DeviceCode);
public record AuthorizeDeviceRequest(string UserCode, string Email, string Password);
