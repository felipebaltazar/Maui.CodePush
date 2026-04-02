using Microsoft.Extensions.Configuration;

namespace Maui.CodePush.Cli.Services;

public static class CliSettings
{
    private static readonly Lazy<string?> _serverUrl = new(() =>
    {
        var appDir = AppContext.BaseDirectory;
        var configPath = Path.Combine(appDir, "appsettings.json");

        if (!File.Exists(configPath))
            return null;

        var config = new ConfigurationBuilder()
            .AddJsonFile(configPath, optional: true)
            .Build();

        var url = config["CodePush:ServerUrl"];

        // Skip placeholder value
        if (url != null && url.StartsWith("#{"))
            return null;

        return url;
    });

    public static string? DefaultServerUrl => _serverUrl.Value;
}
