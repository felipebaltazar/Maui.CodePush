using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace Maui.CodePush.Cli.Services;

public class AssemblyReferenceDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

public class ModuleDependencySnapshotDto
{
    [JsonPropertyName("moduleName")]
    public string ModuleName { get; set; } = string.Empty;

    [JsonPropertyName("dllHash")]
    public string DllHash { get; set; } = string.Empty;

    [JsonPropertyName("dllSize")]
    public long DllSize { get; set; }

    [JsonPropertyName("assemblyReferences")]
    public List<AssemblyReferenceDto> AssemblyReferences { get; set; } = [];
}

public record CompatibilityResult(bool IsCompatible, List<string> Violations);

public class DependencyAnalyzer
{
    // .NET runtime assemblies excluded from compatibility checks
    private static readonly HashSet<string> RuntimeAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Runtime", "System.Private.CoreLib", "netstandard", "mscorlib",
        "System.Collections", "System.Linq", "System.Threading", "System.IO",
        "System.Text.Json", "System.Memory", "System.Buffers",
        "System.Runtime.InteropServices", "System.Threading.Tasks",
        "System.ComponentModel", "System.ObjectModel", "System.Reflection",
        "System.Resources.ResourceManager", "System.Diagnostics.Debug",
        "System.Runtime.Extensions", "System.Net.Http"
    };

    public List<AssemblyReferenceDto> GetAssemblyReferences(string dllPath)
    {
        var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
        var runtimeDlls = Directory.GetFiles(runtimeDir, "*.dll");

        var dllDir = Path.GetDirectoryName(dllPath) ?? ".";
        var localDlls = Directory.GetFiles(dllDir, "*.dll");

        var allPaths = runtimeDlls.Concat(localDlls).Distinct().ToList();
        if (!allPaths.Contains(dllPath))
            allPaths.Add(dllPath);

        var resolver = new PathAssemblyResolver(allPaths);
        using var mlc = new MetadataLoadContext(resolver);

        var assembly = mlc.LoadFromAssemblyPath(dllPath);
        var references = assembly.GetReferencedAssemblies();

        return references.Select(r => new AssemblyReferenceDto
        {
            Name = r.Name ?? "",
            Version = r.Version?.ToString() ?? "0.0.0.0"
        }).ToList();
    }

    public ModuleDependencySnapshotDto CreateSnapshot(string moduleName, string dllPath)
    {
        var fileInfo = new FileInfo(dllPath);
        var hash = ComputeHash(dllPath);
        var refs = GetAssemblyReferences(dllPath);

        return new ModuleDependencySnapshotDto
        {
            ModuleName = moduleName,
            DllHash = hash,
            DllSize = fileInfo.Length,
            AssemblyReferences = refs
        };
    }

    public CompatibilityResult CheckCompatibility(
        List<AssemblyReferenceDto> releaseRefs,
        List<AssemblyReferenceDto> patchRefs)
    {
        var violations = new List<string>();
        var releaseMap = releaseRefs.ToDictionary(r => r.Name, r => r.Version, StringComparer.OrdinalIgnoreCase);

        foreach (var patchRef in patchRefs)
        {
            if (RuntimeAssemblies.Contains(patchRef.Name))
                continue;

            if (!releaseMap.TryGetValue(patchRef.Name, out var releaseVersion))
            {
                violations.Add($"NEW: {patchRef.Name} v{patchRef.Version} (not in release)");
                continue;
            }

            if (!System.Version.TryParse(patchRef.Version, out var patchVer) ||
                !System.Version.TryParse(releaseVersion, out var relVer))
                continue;

            if (patchVer > relVer)
            {
                violations.Add($"CHANGED: {patchRef.Name} {releaseVersion} -> {patchRef.Version} (version increased)");
            }
        }

        return new CompatibilityResult(violations.Count == 0, violations);
    }

    private static string ComputeHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = System.Security.Cryptography.SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }
}
