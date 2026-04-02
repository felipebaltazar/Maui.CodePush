# Historico do Projeto

Registro cronologico de como o Maui.CodePush evoluiu, para contexto de novos contribuidores.

---

## Origens — Xamarin (pre-2026)

O conceito foi prototipado por Felipe Baltazar durante a era Xamarin:

- **Android**: `Assembly.Load` funcionava via hook em `AppDomain.CurrentDomain.AssemblyResolve`
- **Tecnica**: MSBuild task customizada removia a DLL do Feature apos mover para a pasta de assets do APK. Ao iniciar, `AssemblyNotFoundException` era interceptada e a DLL carregada dos assets
- **Limitacoes encontradas**:
  - Nao podia usar custom renderers ou qualquer coisa que inicializasse com a aplicacao
  - Nao podia usar AOT pesado
  - iOS bloqueava `Assembly.Load` completamente (Xamarin nao tinha interpreter mode)
- **Resultado**: Prova de conceito funcional no Android, iOS nao evoluiu

## Migracao para .NET MAUI (commits iniciais)

O projeto foi migrado do Xamarin para .NET MAUI:
- `8b62f38` — First commit: estrutura basica com CodePush.cs, AppBuilderExtensions, demo
- `e7fddc7` — Update README.md
- `6b32d9d` — Assembly load android: testes de carregamento no Android

Estado neste ponto:
- Paths hardcoded do demo espalhados pela library (`"Maui.CodePush.Demo.content"`)
- Bug `async void` em `UnpackEmbeddedReference` (race condition)
- `WebClient` obsoleto no `CheckUpdates()`
- `[ModuleInitializer]` criando diretorios antes do DI
- MSBuild targets hardcoded no demo (nao na library)
- iOS com MSBuild targets criados mas nao validado
- TFM: net8.0

## Reestruturacao Completa (2026-04-01)

Sessao de planejamento e implementacao com AI assistant (Claude):

### Planejamento
1. Explorado todo o codebase existente
2. Analisadas 5 opcoes para iOS (interpreter, fork runtime, XAML-only, etc.)
3. Decisoes: MtouchInterpreter para MVP, fork MonoVM futuro, net9.0 only, modulos isolados
4. Plano faseado: local MVP -> servidor + CLI -> producao + fork

### Implementacao Sprint 1
1. **Limpeza**: Removidos todos os paths hardcoded, `async void`, `[ModuleInitializer]`, `WebClient`
2. **Models**: Criados `CodePushOptions`, `ModuleManifest`, `ModuleInfo`, `UpdateCheckResult`
3. **Services**: Criados `ModuleManager` (manifesto + SHA-256) e `UpdateClient` (HTTP)
4. **API redesenhada**: `UseCodePush(Action<CodePushOptions>)` com options pattern
5. **MSBuild targets genericos**: `<CodePushModule>` item group, targets para Android e iOS
6. **Migracao net9.0**: Todos os csproj atualizados

### Descobertas Tecnicas Criticas
- **Android .NET 9**: `_ShrunkUserAssemblies` ainda funciona, mas o hook deve ser `AfterTargets="_PrepareAssemblies"` (nao `BeforeTargets="_GenerateAndroidAssetsDir"` como no .NET 8). Assemblies vao para `libxamarin-app.so` blob.
- **Android fast deployment**: Em debug mode, DLLs ficam em `files/.__override__/arm64-v8a/`, nao no APK. `Environment.SpecialFolder.LocalApplicationData` retorna path errado — deve usar `Platform.AppContext.FilesDir.AbsolutePath`.
- **Android `EmbedAssembliesIntoApk`**: Necessario `true` para testar o mecanismo completo. Sem isso, assemblies sao deployadas via fast deployment e o AssemblyResolve nao dispara.

### Validacao em Device Fisico (Android Samsung via USB)
1. Build com `EmbedAssembliesIntoApk=true` — Feature removida do assembly store
2. APK contem Feature apenas em `assets/` (EmbeddedResource)
3. Logcat confirmou: `[CodePush] Unpacked embedded resource` + `[CodePush] Loaded module`
4. Teste de update: DLL v2 pushada via adb para `cache/Modules/`
5. Restart: `[CodePush] Applied pending update` + `[CodePush] Loaded module`
6. UI mudou de template padrao (dotnet_bot) para tela customizada (fundo escuro, "CODE PUSH UPDATE!")

### Resultado
Mecanismo de code push **validado end-to-end** no Android fisico. iOS pendente de validacao em device.

## CLI Tool (2026-04-01, mesma sessao)

### Implementacao
Criado `Maui.CodePush.Cli/` como .NET global tool (`dotnet-codepush`) com 4 comandos:
- `codepush init` — Auto-detecta `ApplicationId` e `<CodePushModule>` do csproj, cria `.codepush.json`
- `codepush devices` — Lista devices Android conectados (wrapper de `adb devices -l`)
- `codepush release [paths] --restart` — Builda modulo + deploya no device via adb + reinicia
- `codepush rollback [--all] --restart` — Remove DLLs de updates, reverte para embedded

### Decisoes Tecnicas
- **System.CommandLine 2.0.5**: API estavel. `SetAction(Func<ParseResult, CancellationToken, Task>)`, `Command.Add()`, `ParseResult.GetValue()`. NAO usar `SetHandler/AddCommand/InvokeAsync` (API das betas).
- **AdbService**: Descobre adb automaticamente (PATH > well-known locations > ANDROID_HOME). Seta `MSYS_NO_PATHCONV=1` para evitar path mangling no Git Bash Windows. Deploy usa dois passos: `adb push` para `/data/local/tmp/` + `run-as cp` para `cache/Modules/`.
- **ConfigManager**: `.codepush.json` com `packageName`, `platform`, `modules[]`. Auto-detect busca csproj com `OutputType=Exe` e extrai `ApplicationId` e `CodePushModule`.
- **ProjectBuilder**: Infere assembly name de `<AssemblyName>` > `<RootNamespace>` > filename. Builda com `dotnet build -f net9.0-android -c Release`.

### Validacao
Testado end-to-end no Samsung fisico (RX8N90FT1LV):
```
codepush devices        -> listou device corretamente
codepush init --force   -> detectou package name e modulos do demo
codepush release Feature.csproj --restart  -> buildou, deployou, reiniciou
codepush rollback --all --restart          -> limpou tudo, reiniciou
```
