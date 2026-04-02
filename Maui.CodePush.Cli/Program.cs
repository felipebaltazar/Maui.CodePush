using System.CommandLine;
using Maui.CodePush.Cli.Commands;

var rootCommand = new RootCommand("Maui.CodePush CLI — deploy OTA updates to .NET MAUI apps");

rootCommand.Add(InitCommand.Create());
rootCommand.Add(DevicesCommand.Create());
rootCommand.Add(ReleaseCommand.Create());
rootCommand.Add(RollbackCommand.Create());

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync(parseResult.InvocationConfiguration);
