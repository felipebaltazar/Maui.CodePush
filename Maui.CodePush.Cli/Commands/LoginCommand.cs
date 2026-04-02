using System.CommandLine;
using Maui.CodePush.Cli.Services;

namespace Maui.CodePush.Cli.Commands;

public static class LoginCommand
{
    public static Command Create()
    {
        var serverOption = new Option<string?>("--server", "-s") { Description = "Server URL (uses built-in default if omitted)" };
        var emailOption = new Option<string>("--email", "-e") { Description = "Account email", Required = true };
        var passwordOption = new Option<string>("--password") { Description = "Account password", Required = true };
        var registerOption = new Option<bool>("--register") { Description = "Create a new account instead of logging in" };
        var nameOption = new Option<string?>("--name") { Description = "Display name (required for --register)" };

        var command = new Command("login", "Authenticate with a CodePush server and save credentials")
        {
            serverOption, emailOption, passwordOption, registerOption, nameOption
        };

        command.SetAction(async (parseResult, _) =>
        {
            var server = parseResult.GetValue(serverOption);
            var email = parseResult.GetValue(emailOption)!;
            var password = parseResult.GetValue(passwordOption)!;
            var register = parseResult.GetValue(registerOption);
            var name = parseResult.GetValue(nameOption);

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
                string? token = null;
                string? apiKey = null;

                if (register)
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        ConsoleUI.Error("--name is required for registration.");
                        return;
                    }

                    var result = await ConsoleUI.SpinnerAsync("Creating account",
                        async () => await client.RegisterAsync(email, password, name));

                    apiKey = result.GetProperty("apiKey").GetString();

                    var loginResult = await ConsoleUI.SpinnerAsync("Logging in",
                        async () => await client.LoginAsync(email, password));

                    token = loginResult.GetProperty("token").GetString();
                }
                else
                {
                    var result = await ConsoleUI.SpinnerAsync("Logging in",
                        async () => await client.LoginAsync(email, password));

                    token = result.GetProperty("token").GetString();

                    var authedClient = new ServerClient(server, token: token);
                    var me = await authedClient.GetMeAsync();
                    apiKey = me.GetProperty("apiKey").GetString();
                }

                var config = loaded?.Config ?? new Models.CodePushConfig();
                var dir = loaded?.ProjectDir ?? Directory.GetCurrentDirectory();

                config.ServerUrl = server;
                config.Token = token;
                config.ApiKey = apiKey;
                configManager.CreateConfig(dir, config);

                ConsoleUI.Blank();
                ConsoleUI.Success("Authenticated successfully");
                ConsoleUI.Detail("Server", server);
                ConsoleUI.Detail("Email", email);
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
}
