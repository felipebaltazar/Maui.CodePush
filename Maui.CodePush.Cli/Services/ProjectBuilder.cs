using System.Diagnostics;
using System.Xml.Linq;

namespace Maui.CodePush.Cli.Services;

public class ProjectBuilder
{
    public async Task<string> BuildModuleAsync(string projectPath, string platform, string configuration = "Release")
    {
        ValidateProject(projectPath, platform);

        var tfm = platform.ToLowerInvariant() switch
        {
            "android" => "net9.0-android",
            "ios" => "net9.0-ios",
            _ => throw new ArgumentException($"Unsupported platform: {platform}")
        };

        var assemblyName = GetAssemblyName(projectPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectPath}\" -f {tfm} -c {configuration} --no-restore",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start dotnet build.");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Build failed for {Path.GetFileName(projectPath)}:\n{stdout}\n{stderr}");
        }

        // Find output DLL
        var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))!;
        var dllPath = Path.Combine(projectDir, "bin", configuration, tfm, $"{assemblyName}.dll");

        if (!File.Exists(dllPath))
            throw new FileNotFoundException($"Build succeeded but DLL not found at: {dllPath}");

        return dllPath;
    }

    public string GetAssemblyName(string projectPath)
    {
        var doc = XDocument.Load(projectPath);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        var assemblyName = doc.Descendants(ns + "AssemblyName").FirstOrDefault()?.Value
                        ?? doc.Descendants(ns + "RootNamespace").FirstOrDefault()?.Value
                        ?? Path.GetFileNameWithoutExtension(projectPath);

        return assemblyName;
    }

    public void ValidateProject(string projectPath, string platform)
    {
        if (!File.Exists(projectPath))
            throw new FileNotFoundException($"Project not found: {projectPath}");

        if (!projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Not a .csproj file: {projectPath}");

        var content = File.ReadAllText(projectPath);
        var expectedTfm = platform.ToLowerInvariant() switch
        {
            "android" => "net9.0-android",
            "ios" => "net9.0-ios",
            _ => platform
        };

        if (!content.Contains(expectedTfm, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Project does not target {expectedTfm}. Check TargetFrameworks in {Path.GetFileName(projectPath)}.");
        }
    }
}
