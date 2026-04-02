# Maui.CodePush (Biblioteca Principal)

NuGet package que fornece o mecanismo de code push para .NET MAUI.
Pacote: `Maui.CodePush` versao `0.1.0-pre`. TFM: `net9.0-android;net9.0-ios`.

## Arquivos Raiz

| Arquivo | Papel |
|---------|-------|
| `CodePush.cs` | **Core engine**. Classe estatica. `Initialize()` configura paths e registra `AssemblyResolve`. `ResolveAssembly()` implementa cadeia de prioridade: temp -> base -> embedded. `UnpackEmbeddedReference()` extrai DLL dos assets (Android) ou manifest resources (iOS). `CheckUpdatesAsync()` consulta servidor. `Rollback()` reverte para embedded. `DeleteDebugAssemblies()` limpa fast deployment. |
| `AppBuilderExtensions.cs` | Extension method `UseCodePush(Action<CodePushOptions>)` no `MauiAppBuilder`. Ponto de entrada para o consumidor. |
| `AssemblyRegister.cs` | `HashSet<string>` interno de nomes de DLLs. Impede duplicatas. Adiciona `.dll` automaticamente. |
| `Maui.CodePush.csproj` | Gera NuGet on build. Inclui `build/*.props` e `build/*.targets` em `build/` e `buildTransitive/`. |

## Pastas

### Abstractions/
- `IAssemblyCollection.cs` — Interface interna (`void AddAssembly(string)`). Implementada por `AssemblyRegister`.

### Models/
- `CodePushOptions.cs` — Config do consumidor: `ServerUrl`, `AppKey`, `Channel`, `UpdatePolicy`, `AddModule()`. O `AssemblyRegister` interno eh populado via `AddModule()`.
- `ModuleManifest.cs` — JSON persistido em `Modules/codepush-manifest.json`. Contem `appVersion`, `platform`, `lastChecked`, dicionario de `ModuleInfo`.
- `ModuleInfo.cs` — Estado de um modulo: `Version`, `Hash` (SHA-256), `DownloadedAt`, `AppliedAt`, `PreviousHash`, `Status` (Embedded/Active/Pending/RolledBack).
- `UpdateCheckResult.cs` — Resposta do servidor: `UpdateAvailable` bool + lista de `ModuleUpdateInfo` (name, version, downloadUrl, hash, size, isMandatory).

### Services/
- `ModuleManager.cs` — Gerencia manifesto JSON. `ComputeHash()`/`VerifyHash()` para SHA-256. Transicoes de estado: `MarkUpdated()` -> `MarkApplied()` -> `MarkRolledBack()`. `Reset()` para rollback total.
- `UpdateClient.cs` — `HttpClient` para servidor. `CheckForUpdatesAsync()` envia GET com app/module/version/platform/channel. `DownloadModuleAsync()` baixa DLL, verifica hash, salva em temp. `DownloadModuleFromUrlAsync()` para testes sem servidor.

### Platforms/
- `Android/CodePushApplication.cs` — Base class abstrata: `MauiApplication`. O consumidor herda no `MainApplication`.
- `iOS/CodePushAppDelegate.cs` — Base class abstrata: `MauiUIApplicationDelegate`. Captura `AssemblyName` do host no construtor (usado por `UnpackEmbeddedReference` para localizar embedded resources no iOS).

### build/
- `Maui.CodePush.props` — Importado cedo. Adiciona `<Using Include="Maui.CodePush" />`.
- `Maui.CodePush.targets` — Importado tarde. 5 targets MSBuild:
  1. `CodePush_ConfigureiOSInterpreter` — Seta `MtouchInterpreter=-all,NomeDoModulo`
  2. `CodePush_RemoveModulesFromAndroid` — AfterTargets=`_PrepareAssemblies`. Remove de `_ShrunkUserAssemblies`/`_ShrunkFrameworkAssemblies` via matching por nome
  3. `CodePush_RemoveModulesFromiOS` — BeforeTargets=`_CompileNativeExecutable`. Deleta DLLs de refint/output
  4. `CodePush_RemoveModulesAfterBuild` — Cleanup iOS pos-build
  5. `CodePush_RemoveModulesFromHotRestart` — Remove de `_HotRestartContentDirContents`

## Regras para Contribuicao

- Namespace unico: `Maui.CodePush`
- Logs com prefixo `[CodePush]` via `Debug.WriteLine`
- MSBuild targets com prefixo `CodePush_`, item groups internos com `_CodePush`
- Nao adicionar dependencias alem de `Microsoft.Maui.Controls` e `Microsoft.Maui.Controls.Compatibility`
- Codigo platform-specific usa `#if ANDROID` / `#elif IOS` / `#endif`
