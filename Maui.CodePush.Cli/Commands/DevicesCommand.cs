using System.CommandLine;
using Maui.CodePush.Cli.Services;
using static Maui.CodePush.Cli.Commands.InitCommand;

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
                WriteInfo($"Using adb: {foundPath}");

                var devices = await adb.GetDevicesAsync();

                if (devices.Count == 0)
                {
                    WriteWarning("No devices connected. Enable USB debugging and connect a device.");
                    return;
                }

                Console.WriteLine();
                Console.WriteLine($"  {"Serial",-25} {"Model",-20} {"State"}");
                Console.WriteLine($"  {"------",-25} {"-----",-20} {"-----"}");

                foreach (var device in devices)
                {
                    Console.WriteLine($"  {device.Serial,-25} {device.Model ?? "unknown",-20} {device.State}");
                }

                Console.WriteLine();
                WriteSuccess($"{devices.Count} device(s) connected.");
            }
            catch (Exception ex) when (ex is FileNotFoundException or AdbException)
            {
                WriteError(ex.Message);
            }
        });

        return command;
    }
}
