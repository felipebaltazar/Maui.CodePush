using System.CommandLine;
using Maui.CodePush.Cli.Models;
using Maui.CodePush.Cli.Services;
using static Maui.CodePush.Cli.Commands.InitCommand;

namespace Maui.CodePush.Cli.Commands;

public static class ReleaseCommand
{
    public static Command Create()
    {
        var pathsArgument = new Argument<string[]>("paths") { Arity = ArgumentArity.ZeroOrMore, Description = "Module project (.csproj) or DLL (.dll) paths" };

        var deviceOption = new Option<string?>("--device", "-d") { Description = "Target device serial" };
        var packageNameOption = new Option<string?>("--package-name", "-p") { Description = "Android application ID" };
        var platformOption = new Option<string?>("--platform") { Description = "Target platform (default: from config or android)" };
        var outputOption = new Option<string?>("--output", "-o") { Description = "Output directory instead of deploying to device" };
        var noBuildOption = new Option<bool>("--no-build") { Description = "Skip build, treat paths as pre-built DLLs" };
        var configOption = new Option<string>("--configuration", "-c") { Description = "Build configuration", DefaultValueFactory = _ => "Release" };
        var restartOption = new Option<bool>("--restart") { Description = "Force-stop and restart the app after deployment" };

        var command = new Command("release", "Build and deploy module updates to a connected device")
        {
            pathsArgument,
            deviceOption,
            packageNameOption,
            platformOption,
            outputOption,
            noBuildOption,
            configOption,
            restartOption
        };

        command.SetAction(async (parseResult, _) =>
        {
            var paths = parseResult.GetValue(pathsArgument) ?? [];
            var device = parseResult.GetValue(deviceOption);
            var packageName = parseResult.GetValue(packageNameOption);
            var platform = parseResult.GetValue(platformOption);
            var output = parseResult.GetValue(outputOption);
            var noBuild = parseResult.GetValue(noBuildOption);
            var configuration = parseResult.GetValue(configOption)!;
            var restart = parseResult.GetValue(restartOption);

            try
            {
                await ExecuteAsync(paths, device, packageName, platform, output, noBuild, configuration, restart);
            }
            catch (Exception ex) when (ex is FileNotFoundException or AdbException or InvalidOperationException or ArgumentException)
            {
                WriteError(ex.Message);
            }
        });

        return command;
    }

    private static async Task ExecuteAsync(string[] paths, string? device, string? packageName,
        string? platform, string? output, bool noBuild, string configuration, bool restart)
    {
        var configManager = new ConfigManager();
        var builder = new ProjectBuilder();

        var loaded = configManager.TryLoadConfig();
        var config = loaded?.Config;
        var projectDir = loaded?.ProjectDir ?? Directory.GetCurrentDirectory();

        packageName ??= config?.PackageName;
        platform ??= config?.Platform ?? "android";

        var modulePaths = ResolveModulePaths(paths, config, projectDir);

        if (modulePaths.Count == 0)
        {
            WriteError("No modules specified. Pass project/DLL paths or configure modules in .codepush.json");
            return;
        }

        var deployments = new List<(string Name, string DllPath)>();

        foreach (var (name, path) in modulePaths)
        {
            if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && !noBuild)
            {
                WriteInfo($"Building {name} ({platform}, {configuration})...");
                var dllPath = await builder.BuildModuleAsync(path, platform, configuration);
                WriteSuccess($"Built {name} -> {Path.GetFileName(dllPath)}");
                deployments.Add((name, dllPath));
            }
            else if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || noBuild)
            {
                if (!File.Exists(path))
                    throw new FileNotFoundException($"DLL not found: {path}");
                deployments.Add((name, Path.GetFullPath(path)));
            }
            else
            {
                throw new ArgumentException($"Unknown file type: {path}. Expected .csproj or .dll");
            }
        }

        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            foreach (var (name, dllPath) in deployments)
            {
                var dest = Path.Combine(output, $"{name}.dll");
                File.Copy(dllPath, dest, true);
                WriteSuccess($"Copied {name} -> {dest}");
            }
            return;
        }

        if (string.IsNullOrEmpty(packageName))
            throw new InvalidOperationException("Package name required. Use --package-name or configure in .codepush.json");

        var adb = new AdbService();
        adb.FindAdb(config?.AdbPath);
        var deviceSerial = await adb.ResolveDeviceAsync(device);

        WriteInfo($"Deploying to {deviceSerial}...");

        foreach (var (name, dllPath) in deployments)
        {
            await adb.DeployModuleAsync(deviceSerial, packageName, dllPath, name);
            WriteSuccess($"Deployed {name} to {deviceSerial}");
        }

        if (restart)
        {
            WriteInfo("Restarting app...");
            await adb.ForceStopAppAsync(deviceSerial, packageName);
            await Task.Delay(1000);
            await adb.StartAppAsync(deviceSerial, packageName);
            WriteSuccess("App restarted. Update will be applied.");
        }
        else
        {
            Console.WriteLine();
            WriteInfo("Restart the app to apply the update.");
        }
    }

    private static List<(string Name, string Path)> ResolveModulePaths(
        string[] paths, CodePushConfig? config, string projectDir)
    {
        var result = new List<(string Name, string Path)>();

        if (paths.Length > 0)
        {
            foreach (var p in paths)
            {
                var fullPath = System.IO.Path.GetFullPath(p);
                var name = System.IO.Path.GetFileNameWithoutExtension(fullPath);
                result.Add((name, fullPath));
            }
            return result;
        }

        if (config?.Modules != null)
        {
            foreach (var module in config.Modules)
            {
                if (!string.IsNullOrEmpty(module.ProjectPath))
                {
                    var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(projectDir, module.ProjectPath));
                    result.Add((module.Name, fullPath));
                }
            }
        }

        return result;
    }
}
