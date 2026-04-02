using System.CommandLine;
using Maui.CodePush.Cli.Services;
using static Maui.CodePush.Cli.Commands.InitCommand;

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
            modulesArgument,
            deviceOption,
            packageNameOption,
            allOption,
            restartOption
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
                    WriteError("Package name required. Use --package-name or configure in .codepush.json");
                    return;
                }

                var adb = new AdbService();
                adb.FindAdb(config?.AdbPath);
                var deviceSerial = await adb.ResolveDeviceAsync(device);

                if (all)
                {
                    WriteInfo($"Rolling back all modules on {deviceSerial}...");
                    await adb.RemoveAllModulesAsync(deviceSerial, packageName);
                    WriteSuccess("All modules rolled back to embedded version.");
                }
                else
                {
                    var targetModules = modules.Length > 0
                        ? modules
                        : config?.Modules.Select(m => m.Name).ToArray() ?? [];

                    if (targetModules.Length == 0)
                    {
                        WriteError("No modules specified. Pass module names or use --all.");
                        return;
                    }

                    foreach (var module in targetModules)
                    {
                        await adb.RemoveModuleAsync(deviceSerial, packageName, module);
                        WriteSuccess($"Rolled back {module}");
                    }
                }

                if (restart)
                {
                    WriteInfo("Restarting app...");
                    await adb.ForceStopAppAsync(deviceSerial, packageName);
                    await Task.Delay(1000);
                    await adb.StartAppAsync(deviceSerial, packageName);
                    WriteSuccess("App restarted with embedded modules.");
                }
                else
                {
                    Console.WriteLine();
                    WriteInfo("Restart the app to load the embedded modules.");
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException or AdbException or InvalidOperationException)
            {
                WriteError(ex.Message);
            }
        });

        return command;
    }
}
