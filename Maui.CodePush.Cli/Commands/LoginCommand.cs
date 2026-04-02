using System.CommandLine;
using Maui.CodePush.Cli.Services;
using static Maui.CodePush.Cli.Commands.InitCommand;

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
            serverOption,
            emailOption,
            passwordOption,
            registerOption,
            nameOption
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

                // Resolve server: arg > config > built-in default
                server ??= loaded?.Config.ServerUrl ?? CliSettings.DefaultServerUrl;
                if (string.IsNullOrEmpty(server))
                {
                    WriteError("Server URL required. Use --server or set serverUrl in .codepush.json");
                    return;
                }

                var client = new ServerClient(server);

                string? token = null;
                string? apiKey = null;

                if (register)
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        WriteError("--name is required for registration.");
                        return;
                    }

                    WriteInfo($"Registering {email}...");
                    var result = await client.RegisterAsync(email, password, name);
                    apiKey = result.GetProperty("apiKey").GetString();
                    WriteSuccess($"Account created.");

                    var loginResult = await client.LoginAsync(email, password);
                    token = loginResult.GetProperty("token").GetString();
                }
                else
                {
                    WriteInfo("Logging in...");
                    var result = await client.LoginAsync(email, password);
                    token = result.GetProperty("token").GetString();

                    var authedClient = new ServerClient(server, token: token);
                    var me = await authedClient.GetMeAsync();
                    apiKey = me.GetProperty("apiKey").GetString();
                }

                // Save to config
                var config = loaded?.Config ?? new Models.CodePushConfig();
                var dir = loaded?.ProjectDir ?? Directory.GetCurrentDirectory();

                config.ServerUrl = server;
                config.Token = token;
                config.ApiKey = apiKey;

                configManager.CreateConfig(dir, config);

                WriteSuccess("Credentials saved to .codepush.json");
                Console.WriteLine($"  Server:  {server}");
                Console.WriteLine($"  Email:   {email}");
                if (apiKey?.Length > 16)
                    Console.WriteLine($"  API Key: {apiKey[..16]}...");
            }
            catch (Exception ex)
            {
                WriteError(ex.Message);
            }
        });

        return command;
    }
}
