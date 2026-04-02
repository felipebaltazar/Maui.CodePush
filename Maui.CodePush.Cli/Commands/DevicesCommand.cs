using System.CommandLine;
using Maui.CodePush.Cli.Services;

namespace Maui.CodePush.Cli.Commands;

public static class DevicesCommand
{
    public static Command Create()
    {
        var command = new Command("devices", "List connected Android devices");

        command.SetAction(async (_, _) =>
        {
            try
            {
                var adb = new AdbService();
                var configManager = new ConfigManager();

                string? adbPath = null;
                var loaded = configManager.TryLoadConfig();
                if (loaded?.Config.AdbPath != null)
                    adbPath = loaded.Value.Config.AdbPath;

                var foundPath = adb.FindAdb(adbPath);
                ConsoleUI.Detail("adb", foundPath);

                var devices = await ConsoleUI.SpinnerAsync("Scanning devices",
                    async () => await adb.GetDevicesAsync());

                if (devices.Count == 0)
                {
                    ConsoleUI.Warning("No devices connected. Enable USB debugging and connect a device.");
                    return;
                }

                var rows = devices.Select(d => new[]
                {
                    d.Serial,
                    d.Model ?? "unknown",
                    d.State
                }).ToList();

                ConsoleUI.PrintTable(["Serial", "Model", "State"], rows);
                ConsoleUI.Success($"{devices.Count} device(s) connected.");
            }
            catch (Exception ex) when (ex is FileNotFoundException or AdbException)
            {
                ConsoleUI.Error(ex.Message);
            }
        });

        return command;
    }
}
