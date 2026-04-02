using System.Diagnostics;

namespace Maui.CodePush.Cli.Services;

public class GitService
{
    public async Task<bool> IsGitRepoAsync()
    {
        try
        {
            var result = await RunAsync("rev-parse", "--is-inside-work-tree");
            return result.ExitCode == 0 && result.Output.Trim() == "true";
        }
        catch { return false; }
    }

    public async Task<bool> CreateAndPushTagAsync(string tagName, string? message = null)
    {
        message ??= tagName;

        var tag = await RunAsync("tag", $"-a \"{tagName}\" -m \"{message}\"");
        if (tag.ExitCode != 0)
        {
            ConsoleUI.Warning($"Git tag failed: {tag.Output.Trim()}");
            return false;
        }

        var push = await RunAsync("push", $"origin \"{tagName}\"");
        if (push.ExitCode != 0)
        {
            ConsoleUI.Warning($"Git push tag failed: {push.Output.Trim()}");
            return false;
        }

        return true;
    }

    public async Task<string?> GetLatestTagAsync(string prefix)
    {
        var result = await RunAsync("tag", $"-l \"{prefix}*\" --sort=-version:refname");
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
            return null;

        return result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string command, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = $"{command} {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start git.");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout + stderr);
    }
}
