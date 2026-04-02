using System.CommandLine;
using Maui.CodePush.Cli.Models;
using Maui.CodePush.Cli.Services;

namespace Maui.CodePush.Cli.Commands;

public static class ReleaseCommand
{
    public static Command Create()
    {
        var pathsArgument = new Argument<string[]>("paths") { Arity = ArgumentArity.ZeroOrMore, Description = "Module project (.csproj) or DLL (.dll) paths" };

        var deviceOption = new Option<string?>("--device", "-d") { Description = "Target device serial (adb mode)" };
        var packageNameOption = new Option<string?>("--package-name", "-p") { Description = "Android application ID" };
        var platformOption = new Option<string?>("--platform") { Description = "Target platform (default: from config or android)" };
        var outputOption = new Option<string?>("--output", "-o") { Description = "Output directory instead of deploying" };
        var noBuildOption = new Option<bool>("--no-build") { Description = "Skip build, treat paths as pre-built DLLs" };
        var configOption = new Option<string>("--configuration", "-c") { Description = "Build configuration", DefaultValueFactory = _ => "Release" };
        var restartOption = new Option<bool>("--restart") { Description = "Force-stop and restart the app (adb mode)" };
        var versionOption = new Option<string>("--version", "-v") { Description = "Release version (server mode)", DefaultValueFactory = _ => "1.0.0" };
        var channelOption = new Option<string>("--channel") { Description = "Release channel", DefaultValueFactory = _ => "production" };
        var localOption = new Option<bool>("--local") { Description = "Force deploy via adb instead of server" };

        var command = new Command("release", "Build and deploy module updates (via server or adb)")
        {
            pathsArgument, deviceOption, packageNameOption, platformOption,
            outputOption, noBuildOption, configOption, restartOption,
            versionOption, channelOption, localOption
        };

        command.SetAction(async (parseResult, _) =>
        {
            try
            {
                await ExecuteAsync(
                    parseResult.GetValue(pathsArgument) ?? [],
                    parseResult.GetValue(deviceOption),
                    parseResult.GetValue(packageNameOption),
                    parseResult.GetValue(platformOption),
                    parseResult.GetValue(outputOption),
                    parseResult.GetValue(noBuildOption),
                    parseResult.GetValue(configOption)!,
                    parseResult.GetValue(restartOption),
                    parseResult.GetValue(versionOption)!,
                    parseResult.GetValue(channelOption)!,
                    parseResult.GetValue(localOption));
            }
            catch (Exception ex) when (ex is FileNotFoundException or AdbException or InvalidOperationException or ArgumentException)
            {
                ConsoleUI.Error(ex.Message);
            }
        });

        return command;
    }

    private static async Task ExecuteAsync(string[] paths, string? device, string? packageName,
        string? platform, string? output, bool noBuild, string configuration, bool restart,
        string version, string channel, bool local)
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
            ConsoleUI.Error("No modules specified. Pass project/DLL paths or configure in .codepush.json");
            return;
        }

        // Build modules
        var deployments = new List<(string Name, string DllPath)>();
        foreach (var (name, path) in modulePaths)
        {
            if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && !noBuild)
            {
                var dllPath = await ConsoleUI.SpinnerAsync($"Building {name} ({platform}, {configuration})",
                    async () => await builder.BuildModuleAsync(path, platform, configuration));
                deployments.Add((name, dllPath));
            }
            else
            {
                var resolved = Path.GetFullPath(path);
                if (!File.Exists(resolved))
                    throw new FileNotFoundException($"File not found: {resolved}");
                ConsoleUI.Success($"Using {Path.GetFileName(resolved)}");
                deployments.Add((name, resolved));
            }
        }

        // Output to directory
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            foreach (var (name, dllPath) in deployments)
            {
                var dest = Path.Combine(output, $"{name}.dll");
                File.Copy(dllPath, dest, true);
                ConsoleUI.Success($"Copied {name} -> {dest}");
            }
            return;
        }

        // Decide: server or adb
        var serverUrl = config?.ServerUrl ?? CliSettings.DefaultServerUrl;
        var hasServer = !string.IsNullOrEmpty(serverUrl) && !string.IsNullOrEmpty(config?.AppId) && !local;

        if (hasServer)
            await DeployViaServer(deployments, config!, serverUrl!, platform, version, channel);
        else
            await DeployViaAdb(deployments, config, packageName, device, restart);
    }

    private static async Task DeployViaServer(
        List<(string Name, string DllPath)> deployments,
        CodePushConfig config, string serverUrl, string platform, string version, string channel)
    {
        var client = new ServerClient(serverUrl, token: config.Token, apiKey: config.ApiKey);

        ConsoleUI.Separator();
        ConsoleUI.Info($"Releasing to server ({channel})");
        ConsoleUI.Blank();

        foreach (var (name, dllPath) in deployments)
        {
            var result = await ConsoleUI.SpinnerAsync($"Uploading {name} v{version}",
                async () => await client.UploadReleaseAsync(config.AppId!, dllPath, name, version, platform, channel));

            var releaseId = result.GetProperty("releaseId").GetString();
            var hash = result.GetProperty("dllHash").GetString();
            var size = result.GetProperty("dllSize").GetInt64();

            ConsoleUI.Detail("Release ID", releaseId ?? "");
            ConsoleUI.Detail("Hash", hash ?? "");
            ConsoleUI.Detail("Size", $"{size:N0} bytes");
            ConsoleUI.Detail("Channel", channel);
            ConsoleUI.Detail("Platform", platform);
        }

        ConsoleUI.Blank();
        ConsoleUI.Success("Release published! Apps will receive the update on next check.");
        ConsoleUI.Blank();
    }

    private static async Task DeployViaAdb(
        List<(string Name, string DllPath)> deployments,
        CodePushConfig? config, string? packageName, string? device, bool restart)
    {
        if (string.IsNullOrEmpty(packageName))
            throw new InvalidOperationException("Package name required for adb deploy. Use --package-name or .codepush.json");

        var adb = new AdbService();
        adb.FindAdb(config?.AdbPath);
        var deviceSerial = await adb.ResolveDeviceAsync(device);

        ConsoleUI.Separator();
        ConsoleUI.Info($"Deploying via adb to {deviceSerial}");
        ConsoleUI.Blank();

        foreach (var (name, dllPath) in deployments)
        {
            await ConsoleUI.SpinnerAsync($"Deploying {name}",
                async () => await adb.DeployModuleAsync(deviceSerial, packageName, dllPath, name));
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
        else
        {
            ConsoleUI.Blank();
            ConsoleUI.Info("Restart the app to apply the update.");
        }

        ConsoleUI.Blank();
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
