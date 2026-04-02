using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Maui.CodePush.Cli.Services;

public record AdbDevice(string Serial, string State, string? Model);

public class AdbException(string message) : Exception(message);

public class AdbService
{
    private string? _adbPath;

    public string FindAdb(string? explicitPath = null)
    {
        if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath))
        {
            _adbPath = explicitPath;
            return _adbPath;
        }

        // Check PATH
        var pathAdb = FindInPath("adb");
        if (pathAdb != null)
        {
            _adbPath = pathAdb;
            return _adbPath;
        }

        // Check well-known locations
        var candidates = GetAdbCandidates();
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                _adbPath = candidate;
                return _adbPath;
            }
        }

        throw new FileNotFoundException(
            "adb not found. Install Android SDK Platform Tools or set adbPath in .codepush.json");
    }

    public async Task<List<AdbDevice>> GetDevicesAsync()
    {
        var output = await RunAdbAsync("devices -l");
        var devices = new List<AdbDevice>();

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("List") || line.StartsWith("*"))
                continue;

            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                continue;

            var serial = parts[0];
            var state = parts[1];

            if (state is not "device")
                continue;

            string? model = null;
            foreach (var part in parts)
            {
                if (part.StartsWith("model:"))
                    model = part["model:".Length..];
            }

            devices.Add(new AdbDevice(serial, state, model));
        }

        return devices;
    }

    public async Task<string> ResolveDeviceAsync(string? deviceSerial = null)
    {
        var devices = await GetDevicesAsync();

        if (devices.Count == 0)
            throw new AdbException("No connected devices found. Enable USB debugging and connect a device.");

        if (!string.IsNullOrEmpty(deviceSerial))
        {
            if (devices.All(d => d.Serial != deviceSerial))
                throw new AdbException($"Device '{deviceSerial}' not found. Connected: {string.Join(", ", devices.Select(d => d.Serial))}");
            return deviceSerial;
        }

        if (devices.Count > 1)
        {
            var list = string.Join("\n  ", devices.Select(d => $"{d.Serial} ({d.Model ?? "unknown"})"));
            throw new AdbException($"Multiple devices connected. Use --device to specify:\n  {list}");
        }

        return devices[0].Serial;
    }

    public async Task DeployModuleAsync(string deviceSerial, string packageName, string localDllPath, string moduleName)
    {
        var dllName = EnsureDllExtension(moduleName);
        var tmpPath = $"/data/local/tmp/{dllName}";
        var targetPath = $"/data/user/0/{packageName}/cache/Modules/{dllName}";

        await RunAdbAsync($"push \"{localDllPath}\" {tmpPath}", deviceSerial);
        await RunAdbAsync($"shell \"run-as {packageName} mkdir -p /data/user/0/{packageName}/cache/Modules\"", deviceSerial);
        await RunAdbAsync($"shell \"run-as {packageName} cp {tmpPath} {targetPath}\"", deviceSerial);
        await RunAdbAsync($"shell rm -f {tmpPath}", deviceSerial);
    }

    public async Task RemoveModuleAsync(string deviceSerial, string packageName, string moduleName)
    {
        var dllName = EnsureDllExtension(moduleName);
        var tempPath = $"/data/user/0/{packageName}/cache/Modules/{dllName}";
        var basePath = $"/data/user/0/{packageName}/files/Documents/Modules/{dllName}";

        await RunAdbAsync($"shell \"run-as {packageName} rm -f {tempPath}\"", deviceSerial);
        await RunAdbAsync($"shell \"run-as {packageName} rm -f {basePath}\"", deviceSerial);
    }

    public async Task RemoveAllModulesAsync(string deviceSerial, string packageName)
    {
        await RunAdbAsync($"shell \"run-as {packageName} rm -rf /data/user/0/{packageName}/cache/Modules\"", deviceSerial);
        await RunAdbAsync($"shell \"run-as {packageName} rm -rf /data/user/0/{packageName}/files/Documents/Modules\"", deviceSerial);
    }

    public async Task ForceStopAppAsync(string deviceSerial, string packageName)
    {
        await RunAdbAsync($"shell am force-stop {packageName}", deviceSerial);
    }

    public async Task StartAppAsync(string deviceSerial, string packageName)
    {
        await RunAdbAsync($"shell monkey -p {packageName} -c android.intent.category.LAUNCHER 1", deviceSerial);
    }

    private async Task<string> RunAdbAsync(string arguments, string? deviceSerial = null)
    {
        var adbPath = _adbPath ?? throw new InvalidOperationException("adb not initialized. Call FindAdb() first.");

        var fullArgs = deviceSerial != null ? $"-s {deviceSerial} {arguments}" : arguments;

        var startInfo = new ProcessStartInfo
        {
            FileName = adbPath,
            Arguments = fullArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // Fix MSYS path conversion on Windows Git Bash
        startInfo.Environment["MSYS_NO_PATHCONV"] = "1";
        startInfo.Environment["MSYS2_ARG_CONV_EXCL"] = "*";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start adb process.");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        // adb push writes progress to stderr even on success
        if (process.ExitCode != 0 && !stderr.Contains("pushed") && !stderr.Contains("file pushed"))
        {
            throw new AdbException($"adb failed (exit {process.ExitCode}): {stderr.Trim()}");
        }

        return stdout;
    }

    private static string EnsureDllExtension(string name) =>
        name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.dll";

    private static string? FindInPath(string executable)
    {
        var ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            var fullPath = Path.Combine(dir, executable + ext);
            if (File.Exists(fullPath))
                return fullPath;
        }

        return null;
    }

    private static List<string> GetAdbCandidates()
    {
        var candidates = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Android", "android-sdk", "platform-tools", "adb.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Android", "android-sdk", "platform-tools", "adb.exe"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            candidates.Add(Path.Combine(home, "Library", "Android", "sdk", "platform-tools", "adb"));
            candidates.Add("/usr/local/bin/adb");
        }
        else
        {
            candidates.Add(Path.Combine(home, "Android", "Sdk", "platform-tools", "adb"));
            candidates.Add("/usr/local/bin/adb");
        }

        // ANDROID_HOME / ANDROID_SDK_ROOT
        var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME")
                       ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        if (!string.IsNullOrEmpty(androidHome))
        {
            var ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
            candidates.Insert(0, Path.Combine(androidHome, "platform-tools", $"adb{ext}"));
        }

        return candidates;
    }
}
