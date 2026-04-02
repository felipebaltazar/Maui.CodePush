using System.CommandLine;
using System.Text.Json;
using Maui.CodePush.Cli.Services;
using static Maui.CodePush.Cli.Commands.InitCommand;

namespace Maui.CodePush.Cli.Commands;

public static class AppsCommand
{
    public static Command Create()
    {
        var command = new Command("apps", "Manage apps on the CodePush server");

        command.Add(CreateListCommand());
        command.Add(CreateAddCommand());

        return command;
    }

    private static Command CreateListCommand()
    {
        var command = new Command("list", "List your apps");

        command.SetAction(async (_, _) =>
        {
            try
            {
                var (client, _) = GetAuthenticatedClient();
                var apps = await client.ListAppsAsync();

                Console.WriteLine();
                Console.WriteLine($"  {"App ID",-38} {"Package Name",-45} {"Display Name"}");
                Console.WriteLine($"  {"------",-38} {"------------",-45} {"------------"}");

                foreach (var app in apps.EnumerateArray())
                {
                    Console.WriteLine($"  {app.GetProperty("appId").GetString(),-38} {app.GetProperty("packageName").GetString(),-45} {app.GetProperty("displayName").GetString()}");
                }

                Console.WriteLine();
            }
            catch (Exception ex)
            {
                WriteError(ex.Message);
            }
        });

        return command;
    }

    private static Command CreateAddCommand()
    {
        var packageOption = new Option<string>("--package-name", "-p") { Description = "Android/iOS package name", Required = true };
        var nameOption = new Option<string>("--name", "-n") { Description = "Display name", Required = true };
        var setDefaultOption = new Option<bool>("--set-default") { Description = "Save as default appId in .codepush.json" };

        var command = new Command("add", "Register a new app")
        {
            packageOption,
            nameOption,
            setDefaultOption
        };

        command.SetAction(async (parseResult, _) =>
        {
            var packageName = parseResult.GetValue(packageOption)!;
            var displayName = parseResult.GetValue(nameOption)!;
            var setDefault = parseResult.GetValue(setDefaultOption);

            try
            {
                var (client, configManager) = GetAuthenticatedClient();

                WriteInfo($"Creating app {packageName}...");
                var result = await client.CreateAppAsync(packageName, displayName);

                var appId = result.GetProperty("appId").GetString()!;
                var appToken = result.GetProperty("appToken").GetString()!;

                WriteSuccess("App created!");
                Console.WriteLine($"  App ID:    {appId}");
                Console.WriteLine($"  Package:   {packageName}");
                Console.WriteLine($"  AppToken:  {appToken[..16]}...");
                Console.WriteLine();
                WriteInfo("Save the AppToken — your mobile app needs it for update checks.");

                if (setDefault)
                {
                    var loaded = configManager.TryLoadConfig();
                    var config = loaded?.Config ?? new Models.CodePushConfig();
                    var dir = loaded?.ProjectDir ?? Directory.GetCurrentDirectory();

                    config.AppId = appId;
                    config.PackageName = packageName;
                    configManager.CreateConfig(dir, config);

                    WriteSuccess("Saved as default app in .codepush.json");
                }
            }
            catch (Exception ex)
            {
                WriteError(ex.Message);
            }
        });

        return command;
    }

    private static (ServerClient Client, ConfigManager ConfigManager) GetAuthenticatedClient()
    {
        var configManager = new ConfigManager();
        var loaded = configManager.TryLoadConfig();
        var config = loaded?.Config;

        var serverUrl = config?.ServerUrl ?? CliSettings.DefaultServerUrl;
        if (string.IsNullOrEmpty(serverUrl))
            throw new InvalidOperationException("Not logged in. Run 'codepush login' first.");

        if (string.IsNullOrEmpty(config?.Token) && string.IsNullOrEmpty(config?.ApiKey))
            throw new InvalidOperationException("No credentials found. Run 'codepush login' first.");

        return (new ServerClient(serverUrl, token: config!.Token, apiKey: config.ApiKey), configManager);
    }
}
