using System.CommandLine;
using Maui.CodePush.Cli.Services;

namespace Maui.CodePush.Cli.Commands;

public static class InitCommand
{
    public static Command Create()
    {
        var packageNameOption = new Option<string?>("--package-name", "-p") { Description = "Android application ID (auto-detected from csproj if omitted)" };
        var platformOption = new Option<string>("--platform") { Description = "Target platform", DefaultValueFactory = _ => "android" };
        var forceOption = new Option<bool>("--force") { Description = "Overwrite existing .codepush.json" };

        var command = new Command("init", "Initialize CodePush configuration in the current directory")
        {
            packageNameOption,
            platformOption,
            forceOption
        };

        command.SetAction((parseResult, _) =>
        {
            var packageName = parseResult.GetValue(packageNameOption);
            var platform = parseResult.GetValue(platformOption)!;
            var force = parseResult.GetValue(forceOption);

            var configManager = new ConfigManager();
            var dir = Directory.GetCurrentDirectory();
            var configPath = Path.Combine(dir, ConfigManager.ConfigFileName);

            if (File.Exists(configPath) && !force)
            {
                WriteError($"{ConfigManager.ConfigFileName} already exists. Use --force to overwrite.");
                return Task.CompletedTask;
            }

            WriteInfo("Scanning project files...");
            var config = configManager.AutoDetectFromProject(dir);

            if (!string.IsNullOrEmpty(packageName))
                config.PackageName = packageName;

            config.Platform = platform;
            configManager.CreateConfig(dir, config);

            WriteSuccess($"Created {ConfigManager.ConfigFileName}");
            Console.WriteLine();

            if (!string.IsNullOrEmpty(config.PackageName))
                Console.WriteLine($"  Package: {config.PackageName}");

            Console.WriteLine($"  Platform: {config.Platform}");
            Console.WriteLine($"  Modules:  {config.Modules.Count}");

            foreach (var module in config.Modules)
            {
                Console.WriteLine($"    - {module.Name}");
                if (module.ProjectPath != null)
                    Console.WriteLine($"      Project: {module.ProjectPath}");
            }

            if (string.IsNullOrEmpty(config.PackageName))
                WriteWarning("Package name not detected. Set it with: codepush init --package-name com.yourapp");

            if (config.Modules.Count == 0)
                WriteWarning("No CodePushModule items found. Add <CodePushModule Include=\"...\"/> to your app csproj.");

            return Task.CompletedTask;
        });

        return command;
    }

    internal static void WriteInfo(string msg) => Console.WriteLine($"[CodePush] {msg}");

    internal static void WriteSuccess(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[CodePush] {msg}");
        Console.ResetColor();
    }

    internal static void WriteWarning(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[CodePush] Warning: {msg}");
        Console.ResetColor();
    }

    internal static void WriteError(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[CodePush] Error: {msg}");
        Console.ResetColor();
    }
}
