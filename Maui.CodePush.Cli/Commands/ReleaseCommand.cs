using System.CommandLine;
using System.Text.Json;
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

        var command = new Command("release", "Manage releases and deploy module updates")
        {
            pathsArgument, deviceOption, packageNameOption, platformOption,
            outputOption, noBuildOption, configOption, restartOption,
            versionOption, channelOption, localOption
        };

        // Subcommands
        command.Add(CreateCreateSubcommand());
        command.Add(CreateListSubcommand());

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

    // ── Subcommand: release create ──────────────────────────────

    private static Command CreateCreateSubcommand()
    {
        var pathsArg = new Argument<string[]>("paths") { Arity = ArgumentArity.ZeroOrMore, Description = "Module project (.csproj) or DLL paths" };
        var versionOpt = new Option<string>("--version", "-v") { Description = "Release version for app store", Required = true };
        var platformOpt = new Option<string?>("--platform") { Description = "Target platform" };
        var channelOpt = new Option<string>("--channel") { Description = "Release channel", DefaultValueFactory = _ => "production" };
        var configOpt = new Option<string>("--configuration", "-c") { Description = "Build configuration", DefaultValueFactory = _ => "Release" };
        var noGitTagOpt = new Option<bool>("--no-git-tag") { Description = "Skip git tag" };
        var appProjectOpt = new Option<string?>("--app-project") { Description = "App .csproj path (for dotnet publish)" };

        var cmd = new Command("create", "Create a new release (app store version) with dependency snapshot")
        {
            pathsArg, versionOpt, platformOpt, channelOpt, configOpt, noGitTagOpt, appProjectOpt
        };

        cmd.SetAction(async (parseResult, _) =>
        {
            try
            {
                var paths = parseResult.GetValue(pathsArg) ?? [];
                var version = parseResult.GetValue(versionOpt)!;
                var platform = parseResult.GetValue(platformOpt);
                var channel = parseResult.GetValue(channelOpt)!;
                var configuration = parseResult.GetValue(configOpt)!;
                var noGitTag = parseResult.GetValue(noGitTagOpt);
                var appProject = parseResult.GetValue(appProjectOpt);

                var configManager = new ConfigManager();
                var loaded = configManager.TryLoadConfig();
                var config = loaded?.Config;
                var projectDir = loaded?.ProjectDir ?? Directory.GetCurrentDirectory();

                var serverUrl = config?.ServerUrl ?? CliSettings.DefaultServerUrl;
                if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(config?.AppId))
                {
                    ConsoleUI.Error("Not configured. Run 'codepush login' and 'codepush apps add --set-default' first.");
                    return;
                }

                platform ??= config?.Platform ?? "android";

                var client = new ServerClient(serverUrl, token: config.Token, apiKey: config.ApiKey);
                var projBuilder = new ProjectBuilder();
                var analyzer = new DependencyAnalyzer();

                // Optionally build the full app via dotnet publish
                if (!string.IsNullOrEmpty(appProject))
                {
                    var tfm = platform == "ios" ? "net9.0-ios" : "net9.0-android";
                    await ConsoleUI.SpinnerAsync($"Publishing app ({platform}, {configuration})", async () =>
                    {
                        var startInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "dotnet",
                            Arguments = $"publish \"{appProject}\" -f {tfm} -c {configuration}",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = System.Diagnostics.Process.Start(startInfo)!;
                        await proc.WaitForExitAsync();
                        if (proc.ExitCode != 0)
                        {
                            var err = await proc.StandardError.ReadToEndAsync();
                            throw new InvalidOperationException($"Publish failed:\n{err}");
                        }
                    });
                }

                // Resolve and build modules
                var modulePaths = ResolveModulePaths(paths, config, projectDir);
                if (modulePaths.Count == 0)
                {
                    ConsoleUI.Error("No modules specified.");
                    return;
                }

                var modules = new List<(string Name, string DllPath)>();
                var snapshots = new List<ModuleDependencySnapshotDto>();

                foreach (var (name, path) in modulePaths)
                {
                    string dllPath;
                    if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                    {
                        dllPath = await ConsoleUI.SpinnerAsync($"Building {name}",
                            () => projBuilder.BuildModuleAsync(path, platform, configuration));
                    }
                    else
                    {
                        dllPath = System.IO.Path.GetFullPath(path);
                    }

                    var snapshot = await ConsoleUI.SpinnerAsync($"Analyzing {name} dependencies",
                        () => Task.FromResult(analyzer.CreateSnapshot(name, dllPath)));

                    ConsoleUI.Detail($"  {name}", $"{snapshot.AssemblyReferences.Count} refs, {snapshot.DllSize} bytes");
                    modules.Add((name, dllPath));
                    snapshots.Add(snapshot);
                }

                // Upload to server
                var snapshotJson = JsonSerializer.Serialize(snapshots);

                ConsoleUI.Separator();

                var result = await ConsoleUI.SpinnerAsync($"Creating release v{version}",
                    () => client.CreateAppReleaseAsync(config.AppId, version, platform, channel, modules, snapshotJson));

                var releaseId = result.GetProperty("releaseId").GetString();
                var gitTag = result.GetProperty("gitTag").GetString();

                ConsoleUI.Blank();
                ConsoleUI.Success($"Release v{version} created!");
                ConsoleUI.Detail("Release ID", releaseId ?? "");
                ConsoleUI.Detail("Platform", platform);
                ConsoleUI.Detail("Channel", channel);
                ConsoleUI.Detail("Modules", modules.Count.ToString());

                // Git tag
                if (!noGitTag && !string.IsNullOrEmpty(gitTag))
                {
                    var git = new GitService();
                    if (await git.IsGitRepoAsync())
                    {
                        var tagged = await git.CreateAndPushTagAsync(gitTag, $"CodePush release v{version}");
                        if (tagged)
                            ConsoleUI.Detail("Git tag", gitTag);
                    }
                }

                ConsoleUI.Blank();
                ConsoleUI.Info("Submit the built APK/IPA to the app store.");
                ConsoleUI.Info($"Then push patches with: codepush patch --release {version}");
                ConsoleUI.Blank();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ConsoleUI.Error(ex.Message);
            }
        });

        return cmd;
    }

    // ── Subcommand: release list ────────────────────────────────

    private static Command CreateListSubcommand()
    {
        var cmd = new Command("list", "List releases for the current app");

        cmd.SetAction(async (_, _) =>
        {
            try
            {
                var configManager = new ConfigManager();
                var loaded = configManager.TryLoadConfig();
                var config = loaded?.Config;

                var serverUrl = config?.ServerUrl ?? CliSettings.DefaultServerUrl;
                if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(config?.AppId))
                {
                    ConsoleUI.Error("Not configured. Run 'codepush login' first.");
                    return;
                }

                var client = new ServerClient(serverUrl, token: config.Token, apiKey: config.ApiKey);

                var releases = await ConsoleUI.SpinnerAsync("Fetching releases",
                    () => client.ListAppReleasesAsync(config.AppId));

                var rows = new List<string[]>();
                foreach (var r in releases.EnumerateArray())
                {
                    rows.Add([
                        r.GetProperty("version").GetString() ?? "",
                        r.GetProperty("platform").GetString() ?? "",
                        r.GetProperty("channel").GetString() ?? "",
                        r.GetProperty("moduleCount").GetInt32().ToString(),
                        r.GetProperty("createdAt").GetString()?[..10] ?? ""
                    ]);
                }

                ConsoleUI.PrintTable(["Version", "Platform", "Channel", "Modules", "Created"], rows);
            }
            catch (Exception ex)
            {
                ConsoleUI.Error(ex.Message);
            }
        });

        return cmd;
    }
}
