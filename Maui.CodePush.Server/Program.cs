using System.Security.Claims;
using System.Text;
using Maui.CodePush.Server.Auth;
using Maui.CodePush.Server.Data;
using Maui.CodePush.Server.Endpoints;
using Maui.CodePush.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// MongoDB - support env var override for connection string
var mongoConnStr = Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING");
if (!string.IsNullOrEmpty(mongoConnStr))
    builder.Configuration["MongoDB:ConnectionString"] = mongoConnStr;

var mongoDbName = Environment.GetEnvironmentVariable("MONGODB_DATABASE_NAME");
if (!string.IsNullOrEmpty(mongoDbName))
    builder.Configuration["MongoDB:DatabaseName"] = mongoDbName;

builder.Services.AddSingleton<MongoDbContext>();

// Authentication - support env var override for JWT secret
var jwtSecret = Environment.GetEnvironmentVariable("CODEPUSH_JWT_SECRET")
    ?? builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("JWT secret is not configured.");

// Sync the secret into configuration so TokenService reads the same value
builder.Configuration["Jwt:Secret"] = jwtSecret;

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = "MultiScheme";
    options.DefaultChallengeScheme = "MultiScheme";
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role
    };
})
.AddScheme<AuthenticationSchemeOptions, ApiKeyAuthHandler>(
    ApiKeyAuthDefaults.AuthenticationScheme, _ => { })
.AddPolicyScheme("MultiScheme", "JWT or API Key", options =>
{
    options.ForwardDefaultSelector = context =>
    {
        if (context.Request.Headers.ContainsKey(ApiKeyAuthDefaults.HeaderName))
            return ApiKeyAuthDefaults.AuthenticationScheme;

        return JwtBearerDefaults.AuthenticationScheme;
    };
});

builder.Services.AddAuthorization();

// Services
builder.Services.AddScoped<SubscriptionService>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<BlobStorageService>();

// Stripe — optional: features disabled when key is not set
var stripeSecretKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
if (!string.IsNullOrEmpty(stripeSecretKey))
    Stripe.StripeConfiguration.ApiKey = stripeSecretKey;

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Create MongoDB indexes
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
    await db.EnsureIndexesAsync();
}

// Create uploads directory
var uploadsPath = app.Configuration["Uploads:Path"] ?? "uploads";
Directory.CreateDirectory(uploadsPath);

// Middleware — UseHttpMetrics before auth so all requests (including 401/403) are measured
app.UseCors();
app.UseHttpMetrics();
app.UseAuthentication();
app.UseAuthorization();

// Health check with real MongoDB ping
app.MapGet("/health", async (MongoDbContext db) =>
{
    try
    {
        // Probe MongoDB connectivity
        await db.Accounts.Find(_ => true).Limit(1).FirstOrDefaultAsync();

        return Results.Ok(new
        {
            status = "healthy",
            service = "codepush-server",
            company = "Monkseal",
            timestamp = DateTime.UtcNow
        });
    }
    catch
    {
        return Results.Json(new
        {
            status = "unhealthy",
            service = "codepush-server",
            timestamp = DateTime.UtcNow
        }, statusCode: 503);
    }
}).WithTags("Health");

// Prometheus metrics — restricted via nginx (only internal docker network reaches 8080)
// RequireHost is spoofable; actual protection is iptables + nginx (port 8080 blocked externally)
app.MapMetrics();

// Map endpoints
app.MapAuthEndpoints();
app.MapAppEndpoints();
app.MapReleaseEndpoints();
app.MapUpdateEndpoints();
app.MapAppReleaseEndpoints();
app.MapPatchEndpoints();
app.MapLandingPageEndpoints();
app.MapStripeWebhookEndpoints();

app.Run();
