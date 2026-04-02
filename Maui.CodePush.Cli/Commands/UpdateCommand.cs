using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using Maui.CodePush.Cli.Services;

namespace Maui.CodePush.Cli.Commands;

public static class UpdateCommand
{
    public static Command Create()
    {
        var preOption = new Option<bool>("--pre") { Description = "Include pre-release versions" };

        var command = new Command("update", "Update the CodePush CLI to the latest version")
        {
            preOption
        };

        command.SetAction(async (parseResult, _) =>
        {
            var pre = parseResult.GetValue(preOption);

            try
            {
                var current = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
                ConsoleUI.Info($"Current version: {current}");

                var args = pre
                    ? "tool update -g dotnet-codepush --prerelease"
                    : "tool update -g dotnet-codepush";

                ConsoleUI.Blank();

                var result = await ConsoleUI.SpinnerAsync("Checking for updates", async () =>
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = args,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo)
                        ?? throw new InvalidOperationException("Failed to start dotnet process.");

                    var stdout = await process.StandardOutput.ReadToEndAsync();
                    var stderr = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    return (ExitCode: process.ExitCode, Output: stdout + stderr);
                });

                if (result.Output.Contains("is the latest version", StringComparison.OrdinalIgnoreCase)
                    || result.Output.Contains("was already installed", StringComparison.OrdinalIgnoreCase))
                {
                    ConsoleUI.Success("Already on the latest version.");
                }
                else if (result.ExitCode == 0)
                {
                    ConsoleUI.Success("Updated successfully!");
                    ConsoleUI.Detail("Output", result.Output.Trim());
                }
                else
                {
                    ConsoleUI.Error($"Update failed: {result.Output.Trim()}");
                }

                ConsoleUI.Blank();
            }
            catch (Exception ex)
            {
                ConsoleUI.Error(ex.Message);
            }
        });

        return command;
    }
}
