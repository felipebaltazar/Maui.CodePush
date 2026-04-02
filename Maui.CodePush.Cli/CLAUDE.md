# Maui.CodePush.Cli

CLI tool para deploy de OTA updates em apps .NET MAUI.
Instalavel como .NET global tool: `dotnet tool install -g dotnet-codepush`.
TFM: `net9.0` (desktop, nao MAUI). Comando: `codepush`.

## Comandos

| Comando | Descricao |
|---------|-----------|
| `codepush init` | Cria `.codepush.json` auto-detectando `ApplicationId` e `<CodePushModule>` do csproj |
| `codepush devices` | Lista devices Android conectados via adb |
| `codepush release [paths]` | Builda modulos e deploya no device via adb. Aceita `.csproj` ou `.dll`. `--restart` reinicia o app. `--output` copia para diretorio |
| `codepush rollback [modules]` | Remove DLLs de updates do device (reverte para embedded). `--all` limpa tudo |

## Estrutura

```
Program.cs                      — Entry point, registra os 4 subcommands
Commands/
  InitCommand.cs                — Cria .codepush.json, auto-detect de csproj
  DevicesCommand.cs             — Lista devices via AdbService
  ReleaseCommand.cs             — Build + deploy. Core do CLI
  RollbackCommand.cs            — Remove updates do device
Services/
  AdbService.cs                 — Encontra adb, lista devices, push/remove files via run-as
  ProjectBuilder.cs             — Wrapper de dotnet build, infere assembly name de csproj
  ConfigManager.cs              — Leitura/escrita de .codepush.json, auto-detect de csproj
Models/
  CodePushConfig.cs             — Modelo do .codepush.json (packageName, platform, modules)
```

## Fluxo do Release

```
1. Carrega .codepush.json (defaults)
2. Resolve modulos: args > config > erro
3. Para cada modulo:
   - Se .csproj: dotnet build -f net9.0-android -c Release
   - Se .dll: usa direto
4. Se --output: copia para diretorio
5. Senao: adb push + run-as cp para cache/Modules/
6. Se --restart: force-stop + monkey launch
```

## Paths no Device (AdbService)

Deploy vai para `cache/Modules/` (= `Path.GetTempPath()/Modules/` no runtime).
Rollback limpa `cache/Modules/` e `files/Documents/Modules/`.

## Notas Tecnicas

- `AdbService.RunAdbAsync()` seta `MSYS_NO_PATHCONV=1` para evitar conversao de paths no Git Bash Windows
- `ProjectBuilder` infere assembly name de `<AssemblyName>` > `<RootNamespace>` > filename do csproj
- `ConfigManager.AutoDetectFromProject()` busca csproj com `<OutputType>Exe</OutputType>` e extrai `<ApplicationId>` e `<CodePushModule>`
- Usa `System.CommandLine` 2.0.5 (API: `SetAction`, `ParseResult.GetValue`, `Command.Add`)
