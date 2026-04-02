using System.CommandLine;
using Maui.CodePush.Cli.Services;

namespace Maui.CodePush.Cli.Commands;

public static class RollbackCommand
{
    public static Command Create()
    {
        var modulesArgument = new Argument<string[]>("modules") { Arity = ArgumentArity.ZeroOrMore, Description = "Module names to rollback" };

        var deviceOption = new Option<string?>("--device", "-d") { Description = "Target device serial" };
        var packageNameOption = new Option<string?>("--package-name", "-p") { Description = "Android application ID" };
        var allOption = new Option<bool>("--all") { Description = "Rollback all modules" };
        var restartOption = new Option<bool>("--restart") { Description = "Restart the app after rollback" };

        var command = new Command("rollback", "Remove deployed updates from a device (reverts to embedded version)")
        {
            modulesArgument, deviceOption, packageNameOption, allOption, restartOption
        };

        command.SetAction(async (parseResult, _) =>
        {
            var modules = parseResult.GetValue(modulesArgument) ?? [];
            var device = parseResult.GetValue(deviceOption);
            var packageName = parseResult.GetValue(packageNameOption);
            var all = parseResult.GetValue(allOption);
            var restart = parseResult.GetValue(restartOption);

            try
            {
                var configManager = new ConfigManager();
                var loaded = configManager.TryLoadConfig();
                var config = loaded?.Config;

                packageName ??= config?.PackageName;
                if (string.IsNullOrEmpty(packageName))
                {
                    ConsoleUI.Error("Package name required. Use --package-name or .codepush.json");
                    return;
                }

                var adb = new AdbService();
                adb.FindAdb(config?.AdbPath);
                var deviceSerial = await adb.ResolveDeviceAsync(device);

                ConsoleUI.Separator();
                ConsoleUI.Info($"Rolling back on {deviceSerial}");
                ConsoleUI.Blank();

                if (all)
                {
                    await ConsoleUI.SpinnerAsync("Removing all modules",
                        async () => await adb.RemoveAllModulesAsync(deviceSerial, packageName));
                }
                else
                {
                    var targetModules = modules.Length > 0
                        ? modules
                        : config?.Modules.Select(m => m.Name).ToArray() ?? [];

                    if (targetModules.Length == 0)
                    {
                        ConsoleUI.Error("No modules specified. Pass module names or use --all.");
                        return;
                    }

                    foreach (var module in targetModules)
                    {
                        await ConsoleUI.SpinnerAsync($"Rolling back {module}",
                            async () => await adb.RemoveModuleAsync(deviceSerial, packageName, module));
                    }
                }

                if (restart)
                {
                    await ConsoleUI.SpinnerAsync("Restarting app", async () =>
                    {
                        await adb.ForceStopAppAsync(deviceSerial, packageName);
                        await Task.Delay(1000);
                        await adb.StartAppAsync(deviceSerial, packageName);
                    });
                }

                ConsoleUI.Blank();
                ConsoleUI.Success("Rolled back to embedded modules.");
                ConsoleUI.Blank();
            }
            catch (Exception ex) when (ex is FileNotFoundException or AdbException or InvalidOperationException)
            {
                ConsoleUI.Error(ex.Message);
            }
        });

        return command;
    }
}
