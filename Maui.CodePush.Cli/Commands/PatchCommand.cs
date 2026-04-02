using System.CommandLine;
using System.Text.Json;
using Maui.CodePush.Cli.Services;

namespace Maui.CodePush.Cli.Commands;

public static class PatchCommand
{
    public static Command Create()
    {
        var pathsArgument = new Argument<string[]>("paths") { Arity = ArgumentArity.ZeroOrMore, Description = "Module project (.csproj) or DLL paths" };

        var releaseOption = new Option<string>("--release", "-r") { Description = "Target release version (e.g. 1.0.0)", Required = true };
        var platformOption = new Option<string?>("--platform") { Description = "Target platform (default: from config or android)" };
        var channelOption = new Option<string>("--channel") { Description = "Release channel", DefaultValueFactory = _ => "production" };
        var configOption = new Option<string>("--configuration", "-c") { Description = "Build configuration", DefaultValueFactory = _ => "Release" };
        var noBuildOption = new Option<bool>("--no-build") { Description = "Skip build" };
        var noGitTagOption = new Option<bool>("--no-git-tag") { Description = "Skip git tag creation" };
        var mandatoryOption = new Option<bool>("--mandatory") { Description = "Mark as mandatory update" };

        var command = new Command("patch", "Create a code push patch for an existing release")
        {
            pathsArgument, releaseOption, platformOption, channelOption,
            configOption, noBuildOption, noGitTagOption, mandatoryOption
        };

        command.SetAction(async (parseResult, _) =>
        {
            try
            {
                var paths = parseResult.GetValue(pathsArgument) ?? [];
                var releaseVersion = parseResult.GetValue(releaseOption)!;
                var platform = parseResult.GetValue(platformOption);
                var channel = parseResult.GetValue(channelOption)!;
                var configuration = parseResult.GetValue(configOption)!;
                var noBuild = parseResult.GetValue(noBuildOption);
                var noGitTag = parseResult.GetValue(noGitTagOption);
                var mandatory = parseResult.GetValue(mandatoryOption);

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
                var builder = new ProjectBuilder();
                var analyzer = new DependencyAnalyzer();

                // Resolve and build modules
                var modulePaths = ResolveModulePaths(paths, config, projectDir);
                if (modulePaths.Count == 0)
                {
                    ConsoleUI.Error("No modules specified.");
                    return;
                }

                // Fetch release from server
                JsonElement releaseData;
                try
                {
                    releaseData = await ConsoleUI.SpinnerAsync($"Fetching release {releaseVersion}",
                        () => client.GetAppReleaseAsync(config.AppId, releaseVersion, platform, channel));
                }
                catch
                {
                    ConsoleUI.Error($"Release {releaseVersion} not found for {platform}/{channel}. Create it with: codepush release create --version {releaseVersion}");
                    return;
                }

                var releaseId = releaseData.GetProperty("releaseId").GetString()!;

                // Parse dependency snapshot from release
                var snapshotJson = releaseData.GetProperty("dependencySnapshot").GetRawText();
                var releaseSnapshots = JsonSerializer.Deserialize<List<ModuleDependencySnapshotDto>>(snapshotJson) ?? [];

                ConsoleUI.Separator();
                ConsoleUI.Info($"Patching release {releaseVersion} ({platform}/{channel})");
                ConsoleUI.Blank();

                foreach (var (name, path) in modulePaths)
                {
                    // Build
                    string dllPath;
                    if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && !noBuild)
                    {
                        dllPath = await ConsoleUI.SpinnerAsync($"Building {name}",
                            () => builder.BuildModuleAsync(path, platform, configuration));
                    }
                    else
                    {
                        dllPath = Path.GetFullPath(path);
                        if (!File.Exists(dllPath))
                            throw new FileNotFoundException($"File not found: {dllPath}");
                    }

                    // Check compatibility
                    var releaseSnapshot = releaseSnapshots.FirstOrDefault(s =>
                        s.ModuleName.Equals(name, StringComparison.OrdinalIgnoreCase));

                    if (releaseSnapshot != null)
                    {
                        var patchRefs = await ConsoleUI.SpinnerAsync("Analyzing dependencies",
                            () => Task.FromResult(analyzer.GetAssemblyReferences(dllPath)));

                        var releaseRefs = releaseSnapshot.AssemblyReferences
                            .Select(r => new AssemblyReferenceDto { Name = r.Name, Version = r.Version })
                            .ToList();

                        var compat = analyzer.CheckCompatibility(releaseRefs, patchRefs);

                        if (!compat.IsCompatible)
                        {
                            ConsoleUI.Blank();
                            ConsoleUI.Error("Dependency compatibility check failed");
                            ConsoleUI.Blank();
                            Console.WriteLine($"    Release {releaseVersion} vs patch:");
                            foreach (var v in compat.Violations)
                                ConsoleUI.Error($"  {v}");
                            ConsoleUI.Blank();
                            ConsoleUI.Info("Create a new release with: codepush release create");
                            return;
                        }

                        ConsoleUI.Success("Dependencies compatible");
                    }

                    // Upload patch
                    var result = await ConsoleUI.SpinnerAsync($"Uploading patch for {name}",
                        () => client.CreatePatchAsync(config.AppId, releaseId, dllPath, name, channel, mandatory));

                    var patchNumber = result.GetProperty("patchNumber").GetInt32();
                    var patchVersion = result.GetProperty("version").GetString();
                    var gitTag = result.GetProperty("gitTag").GetString();

                    ConsoleUI.Detail("Patch", $"#{patchNumber}");
                    ConsoleUI.Detail("Version", patchVersion ?? "");
                    ConsoleUI.Detail("Hash", result.GetProperty("dllHash").GetString()?[..12] + "...");

                    // Git tag
                    if (!noGitTag && !string.IsNullOrEmpty(gitTag))
                    {
                        var git = new GitService();
                        if (await git.IsGitRepoAsync())
                        {
                            var tagged = await git.CreateAndPushTagAsync(gitTag, $"CodePush patch {patchNumber} for release {releaseVersion}");
                            if (tagged)
                                ConsoleUI.Detail("Git tag", gitTag);
                        }
                    }
                }

                ConsoleUI.Blank();
                ConsoleUI.Success("Patch published! Apps will receive the update on next check.");
                ConsoleUI.Blank();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ConsoleUI.Error(ex.Message);
            }
        });

        return command;
    }

    private static List<(string Name, string Path)> ResolveModulePaths(
        string[] paths, Models.CodePushConfig? config, string projectDir)
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
