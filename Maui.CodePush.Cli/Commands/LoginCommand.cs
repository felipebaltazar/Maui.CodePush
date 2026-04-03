using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using Maui.CodePush.Cli.Services;

namespace Maui.CodePush.Cli.Commands;

public static class LoginCommand
{
    public static Command Create()
    {
        var serverOption = new Option<string?>("--server", "-s") { Description = "Server URL (uses built-in default if omitted)" };

        var command = new Command("login", "Authenticate via browser (opens monkseal.dev)")
        {
            serverOption
        };

        command.SetAction(async (parseResult, ct) =>
        {
            var server = parseResult.GetValue(serverOption);

            try
            {
                var configManager = new ConfigManager();
                var loaded = configManager.TryLoadConfig();

                server ??= loaded?.Config.ServerUrl ?? CliSettings.DefaultServerUrl;
                if (string.IsNullOrEmpty(server))
                {
                    ConsoleUI.Error("Server URL required. Use --server or set serverUrl in .codepush.json");
                    return;
                }

                var client = new ServerClient(server);

                // Step 1: Get device code
                var deviceResult = await ConsoleUI.SpinnerAsync("Requesting login code",
                    () => client.CreateDeviceCodeAsync());

                var deviceCode = deviceResult.GetProperty("deviceCode").GetString()!;
                var userCode = deviceResult.GetProperty("userCode").GetString()!;
                var verificationUrl = deviceResult.GetProperty("verificationUrl").GetString()!;
                var interval = deviceResult.GetProperty("interval").GetInt32();

                // Step 2: Open browser
                ConsoleUI.Blank();
                ConsoleUI.Info($"Your login code: {userCode}");
                ConsoleUI.Blank();
                ConsoleUI.Info("Opening browser to complete authentication...");

                try
                {
                    Process.Start(new ProcessStartInfo(verificationUrl) { UseShellExecute = true });
                }
                catch
                {
                    ConsoleUI.Info($"Open this URL in your browser: {verificationUrl}");
                }

                ConsoleUI.Blank();
                ConsoleUI.Info("Waiting for authorization...");
                ConsoleUI.Blank();

                // Step 3: Poll for token
                JsonElement? tokenResult = null;
                var maxAttempts = 300 / interval; // 5 minutes max

                for (var i = 0; i < maxAttempts; i++)
                {
                    await Task.Delay(interval * 1000, ct);

                    try
                    {
                        var poll = await client.PollDeviceTokenAsync(deviceCode);
                        var pollStr = poll.GetRawText();

                        if (pollStr.Contains("authorization_pending"))
                            continue;

                        if (pollStr.Contains("expired_token"))
                        {
                            ConsoleUI.Error("Login code expired. Run 'codepush login' again.");
                            return;
                        }

                        if (poll.TryGetProperty("token", out _))
                        {
                            tokenResult = poll;
                            break;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (tokenResult is null)
                {
                    ConsoleUI.Error("Login timed out. Run 'codepush login' again.");
                    return;
                }

                var token = tokenResult.Value.GetProperty("token").GetString();
                var apiKey = tokenResult.Value.GetProperty("apiKey").GetString();
                var email = tokenResult.Value.GetProperty("email").GetString();
                var name = tokenResult.Value.GetProperty("name").GetString();

                // Save to config
                var config = loaded?.Config ?? new Models.CodePushConfig();
                var dir = loaded?.ProjectDir ?? Directory.GetCurrentDirectory();

                config.ServerUrl = server;
                config.Token = token;
                config.ApiKey = apiKey;
                configManager.CreateConfig(dir, config);

                ConsoleUI.Success("Authenticated successfully!");
                ConsoleUI.Detail("Email", email ?? "");
                ConsoleUI.Detail("Name", name ?? "");
                if (apiKey?.Length > 16)
                    ConsoleUI.Detail("API Key", $"{apiKey[..16]}...");
                ConsoleUI.Blank();
            }
            catch (Exception ex)
            {
                ConsoleUI.Error(ex.Message);
            }
        });

        return command;
    }

    public static Command CreateRegisterCommand()
    {
        var command = new Command("register", "Create an account on the Monkseal website");

        command.SetAction((_, _) =>
        {
            const string url = "https://monkseal.dev/register";
            ConsoleUI.Info($"Opening {url}...");

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                ConsoleUI.Success("Browser opened. Complete registration, then run: codepush login");
            }
            catch
            {
                ConsoleUI.Info($"Open this URL in your browser: {url}");
            }

            ConsoleUI.Blank();
            return Task.CompletedTask;
        });

        return command;
    }
}
