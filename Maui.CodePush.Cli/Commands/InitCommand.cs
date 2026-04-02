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
                ConsoleUI.Error($"{ConfigManager.ConfigFileName} already exists. Use --force to overwrite.");
                return Task.CompletedTask;
            }

            ConsoleUI.Info("Scanning project files...");
            var config = configManager.AutoDetectFromProject(dir);

            if (!string.IsNullOrEmpty(packageName))
                config.PackageName = packageName;

            config.Platform = platform;
            configManager.CreateConfig(dir, config);

            ConsoleUI.Success($"Created {ConfigManager.ConfigFileName}");
            ConsoleUI.Blank();

            if (!string.IsNullOrEmpty(config.PackageName))
                ConsoleUI.Detail("Package", config.PackageName);

            ConsoleUI.Detail("Platform", config.Platform);
            ConsoleUI.Detail("Modules", config.Modules.Count.ToString());

            foreach (var module in config.Modules)
            {
                ConsoleUI.Detail("  Module", module.Name);
                if (module.ProjectPath != null)
                    ConsoleUI.Detail("  Project", module.ProjectPath);
            }

            ConsoleUI.Blank();

            if (string.IsNullOrEmpty(config.PackageName))
                ConsoleUI.Warning("Package name not detected. Use: codepush init --package-name com.yourapp");

            if (config.Modules.Count == 0)
                ConsoleUI.Warning("No CodePushModule items found. Add <CodePushModule Include=\"...\"/> to your csproj.");

            return Task.CompletedTask;
        });

        return command;
    }
}
