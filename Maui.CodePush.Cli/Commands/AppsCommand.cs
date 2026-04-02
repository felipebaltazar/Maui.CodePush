using System.CommandLine;
using System.Text.Json;
using Maui.CodePush.Cli.Services;

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

                var apps = await ConsoleUI.SpinnerAsync("Fetching apps",
                    async () => await client.ListAppsAsync());

                var rows = new List<string[]>();
                foreach (var app in apps.EnumerateArray())
                {
                    rows.Add([
                        app.GetProperty("appId").GetString() ?? "",
                        app.GetProperty("packageName").GetString() ?? "",
                        app.GetProperty("displayName").GetString() ?? ""
                    ]);
                }

                ConsoleUI.PrintTable(["App ID", "Package Name", "Display Name"], rows);
            }
            catch (Exception ex)
            {
                ConsoleUI.Error(ex.Message);
            }
        });

        return command;
    }

    private static Command CreateAddCommand()
    {
        var packageOption = new Option<string>("--package-name", "-p") { Description = "Android/iOS package name", Required = true };
        var nameOption = new Option<string>("--name", "-n") { Description = "Display name", Required = true };
        var setDefaultOption = new Option<bool>("--set-default") { Description = "Save as default appId in .codepush.json" };

        var command = new Command("add", "Register a new app") { packageOption, nameOption, setDefaultOption };

        command.SetAction(async (parseResult, _) =>
        {
            var packageName = parseResult.GetValue(packageOption)!;
            var displayName = parseResult.GetValue(nameOption)!;
            var setDefault = parseResult.GetValue(setDefaultOption);

            try
            {
                var (client, configManager) = GetAuthenticatedClient();

                var result = await ConsoleUI.SpinnerAsync($"Creating app {packageName}",
                    async () => await client.CreateAppAsync(packageName, displayName));

                var appId = result.GetProperty("appId").GetString()!;
                var appToken = result.GetProperty("appToken").GetString()!;

                ConsoleUI.Blank();
                ConsoleUI.Success("App created!");
                ConsoleUI.Detail("App ID", appId);
                ConsoleUI.Detail("Package", packageName);
                ConsoleUI.Detail("AppToken", $"{appToken[..16]}...");
                ConsoleUI.Blank();
                ConsoleUI.Info("Save the AppToken — your mobile app needs it for update checks.");

                if (setDefault)
                {
                    var loaded = configManager.TryLoadConfig();
                    var config = loaded?.Config ?? new Models.CodePushConfig();
                    var dir = loaded?.ProjectDir ?? Directory.GetCurrentDirectory();

                    config.AppId = appId;
                    config.PackageName = packageName;
                    configManager.CreateConfig(dir, config);

                    ConsoleUI.Success("Saved as default app in .codepush.json");
                }

                ConsoleUI.Blank();
            }
            catch (Exception ex)
            {
                ConsoleUI.Error(ex.Message);
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
