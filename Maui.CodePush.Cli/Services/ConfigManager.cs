using System.Text.Json;
using System.Xml.Linq;
using Maui.CodePush.Cli.Models;

namespace Maui.CodePush.Cli.Services;

public class ConfigManager
{
    public const string ConfigFileName = ".codepush.json";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public (CodePushConfig Config, string ProjectDir) LoadConfig(string? startDir = null)
    {
        var dir = startDir ?? Directory.GetCurrentDirectory();

        while (dir != null)
        {
            var configPath = Path.Combine(dir, ConfigFileName);
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<CodePushConfig>(json, _jsonOptions)
                    ?? new CodePushConfig();
                return (config, dir);
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new FileNotFoundException(
            $"{ConfigFileName} not found. Run 'codepush init' to create one.");
    }

    public (CodePushConfig Config, string ProjectDir)? TryLoadConfig(string? startDir = null)
    {
        try { return LoadConfig(startDir); }
        catch (FileNotFoundException) { return null; }
    }

    public void CreateConfig(string directory, CodePushConfig config)
    {
        var configPath = Path.Combine(directory, ConfigFileName);
        var json = JsonSerializer.Serialize(config, _jsonOptions);
        File.WriteAllText(configPath, json);
    }

    public CodePushConfig AutoDetectFromProject(string directory)
    {
        var config = new CodePushConfig();

        // Find the main app csproj (has OutputType=Exe)
        var csprojFiles = Directory.GetFiles(directory, "*.csproj", SearchOption.AllDirectories);

        foreach (var csproj in csprojFiles)
        {
            try
            {
                var doc = XDocument.Load(csproj);
                var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

                // Check if it's the app project (OutputType=Exe)
                var outputType = doc.Descendants(ns + "OutputType").FirstOrDefault()?.Value;
                if (string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract ApplicationId
                    var appId = doc.Descendants(ns + "ApplicationId").FirstOrDefault()?.Value;
                    if (!string.IsNullOrEmpty(appId))
                        config.PackageName = appId;

                    // Extract CodePushModule items
                    var modules = doc.Descendants(ns + "CodePushModule");
                    foreach (var module in modules)
                    {
                        var name = module.Attribute("Include")?.Value;
                        if (string.IsNullOrEmpty(name))
                            continue;

                        // Try to find the module's project file
                        var moduleProject = csprojFiles.FirstOrDefault(f =>
                            Path.GetFileNameWithoutExtension(f)
                                .Equals(name, StringComparison.OrdinalIgnoreCase));

                        var relativePath = moduleProject != null
                            ? Path.GetRelativePath(directory, moduleProject)
                            : null;

                        config.Modules.Add(new ModuleConfig
                        {
                            Name = name,
                            ProjectPath = relativePath
                        });
                    }

                    break;
                }
            }
            catch
            {
                // Skip unparseable csproj files
            }
        }

        return config;
    }
}
