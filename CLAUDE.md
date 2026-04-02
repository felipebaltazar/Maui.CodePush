# Maui.CodePush

Ferramenta comercial de Code Push / OTA updates para .NET MAUI.
Permite publicar hotfixes diretamente em apps Android e iOS sem passar pela revisao das lojas.
Analogia: Shorebird (Flutter), CodePush (React Native).

## Documentacao Adicional

- `docs/PLAN.md` — Plano de implementacao completo com decisoes tecnicas, fases e roadmap
- `docs/HISTORY.md` — Historico cronologico do projeto (origens Xamarin, migracao, reestruturacao)
- Cada pasta tem seu proprio `CLAUDE.md` com descricao de arquivos e objetivos
- `README.md` — Documentacao publica para desenvolvedores/clientes

## REGRA OBRIGATORIA: Documentar Toda Evolucao

Qualquer mudanca no projeto DEVE ser acompanhada de atualizacao na documentacao. Isso NAO eh opcional.

Ao concluir qualquer tarefa, o Claude DEVE:
1. **`docs/HISTORY.md`**: Adicionar entrada cronologica descrevendo o que foi feito, decisoes tomadas e descobertas tecnicas
2. **`docs/PLAN.md`**: Atualizar status das tarefas (marcar completadas, adicionar novas, revisar riscos)
3. **`CLAUDE.md` da pasta afetada**: Atualizar se novos arquivos foram criados, renomeados ou removidos
4. **`CLAUDE.md` raiz**: Atualizar se houve mudanca arquitetural, novo fluxo, nova convencao ou nova pasta
5. **`README.md`**: Atualizar se houve mudanca na API publica, novos features, ou mudanca no roadmap
6. **Novo `CLAUDE.md`**: Criar se uma nova pasta foi adicionada ao projeto
7. **Memory (Claude)**: Salvar decisoes nao-obvias, feedback do usuario, e mudancas de direcao

Se uma tarefa cria/modifica/remove arquivos sem atualizar a documentacao correspondente, a tarefa NAO esta completa.

## Decisoes de Projeto

- **TFM**: net9.0 apenas (net9.0-android, net9.0-ios)
- **iOS**: MtouchInterpreter (hibrido AOT + interpreter) no MVP; fork do MonoVM como evolucao futura
- **Android**: Assembly.Load funciona nativamente
- **Escopo de update**: DLLs completas de modulos isolados
- **Quando aplicar**: Updates sao aplicados no proximo cold start (assembly carregado nao pode ser descarregado)
- **Build**: Assemblies devem ser embeddados com `EmbedAssembliesIntoApk=true` no Android para o mecanismo funcionar corretamente

## Como Funciona (Fluxo Completo)

### Build Time (MSBuild Targets)
1. O desenvolvedor declara `<CodePushModule Include="MeuApp.Feature" />` no csproj
2. **Android**: O target `CodePush_RemoveModulesFromAndroid` (AfterTargets=`_PrepareAssemblies`) remove a DLL do assembly store (libxamarin-app.so). Usa matching por nome em `_ShrunkUserAssemblies` e `_ShrunkFrameworkAssemblies`
3. **iOS**: O target `CodePush_ConfigureiOSInterpreter` configura `MtouchInterpreter=-all,NomeDoModulo` para habilitar o interpreter apenas para modulos CodePush. O target `CodePush_RemoveModulesFromiOS` remove a DLL dos outputs do build antes da compilacao nativa
4. A DLL do modulo permanece como `<EmbeddedResource>` no APK/IPA (fallback para primeira execucao)

### Runtime (Assembly Resolution)
1. `UseCodePush()` chama `CodePush.Initialize()` que registra `AppDomain.CurrentDomain.AssemblyResolve`
2. No Android debug (fast deployment), `DeleteDebugAssemblies()` remove a DLL de `.__override__/arm64-v8a/`
3. Quando o runtime tenta carregar o modulo (ex: Shell navega para uma pagina do Feature):
   - Runtime nao encontra no assembly store (foi removido) -> dispara `AssemblyResolve`
   - `ResolveAssembly()` segue prioridade: **temp** (updates pendentes) -> **base** (persistido) -> **embedded** (fallback)
   - Se encontra DLL no temp, copia para base e deleta do temp ("apply pending update")
   - Carrega via `Assembly.LoadFrom(path)`

### Update Flow
1. `CheckUpdatesAsync()` consulta o servidor: `GET /api/updates/check?app=X&module=Y&version=Z&platform=P`
2. Se update disponivel, `UpdateClient` baixa a DLL para o diretorio temp (`cache/Modules/`)
3. `ModuleManager` verifica hash SHA-256 e atualiza o manifesto
4. No proximo cold start, `ResolveAssembly` detecta a DLL no temp e aplica

### Simulacao de Update (sem servidor)
```bash
# 1. Buildar a nova DLL do modulo
dotnet build Feature/Feature.csproj -f net9.0-android

# 2. Pushear para o device via adb
adb push Feature.dll /data/local/tmp/Feature.dll
adb shell "run-as com.meuapp cp /data/local/tmp/Feature.dll /data/user/0/com.meuapp/cache/Modules/Feature.dll"

# 3. Reiniciar o app - CodePush aplica o update automaticamente
```

## Diretorios no Device

| Path | Conteudo |
|------|----------|
| `Personal/Modules/` (`files/Documents/Modules/`) | DLLs persistidas (versao ativa) + manifesto JSON |
| `TempPath/Modules/` (`cache/Modules/`) | DLLs de updates pendentes (staging area) |
| `files/.__override__/arm64-v8a/` | Fast deployment (debug only) - DLLs deletadas pelo CodePush |

## Estrutura do Projeto

```
Maui.CodePush/                          # Biblioteca principal (NuGet package)
  Maui.CodePush.csproj                  # net9.0-android;net9.0-ios, GeneratePackageOnBuild
  Maui.CodePush.sln                     # Solution com Demo e Feature

  CodePush.cs                           # Core engine (static class)
  AppBuilderExtensions.cs               # Extension method UseCodePush()
  AssemblyRegister.cs                   # Registro interno de modulos (HashSet<string>)

  Abstractions/
    IAssemblyCollection.cs              # Interface interna para registro de assemblies

  Models/
    CodePushOptions.cs                  # Configuracao (ServerUrl, AppKey, Channel, UpdatePolicy, Modules)
    ModuleManifest.cs                   # Manifesto JSON (appVersion, modules com hash/status/versao)
    ModuleInfo.cs                       # Info por modulo (version, hash, status, previousHash, timestamps)
    UpdateCheckResult.cs                # Resposta do servidor (updateAvailable, lista de ModuleUpdateInfo)

  Services/
    ModuleManager.cs                    # Gerencia manifesto, hash SHA-256, rollback, estado dos modulos
    UpdateClient.cs                     # HTTP client para check e download de updates

  Platforms/
    Android/
      CodePushApplication.cs            # Base class para Application Android (herdar no app)
    iOS/
      CodePushAppDelegate.cs            # Base class para AppDelegate iOS (captura AssemblyName do host)

  build/
    Maui.CodePush.props                 # Importado cedo: adiciona implicit using Maui.CodePush
    Maui.CodePush.targets               # Importado tarde: remove modulos do build, configura MtouchInterpreter

Maui.CodePush.Demo/                     # App de demonstracao
  Maui.CodePush.Demo.csproj            # Declara <CodePushModule>, importa .props/.targets da lib
  MauiProgram.cs                        # Chama UseCodePush() com configuracao
  App.xaml.cs                           # Chama CheckUpdatesAsync() no OnStart
  AppShell.xaml                         # Referencia Feature.MainPage via clr-namespace
  Platforms/Android/MainApplication.cs  # Herda CodePushApplication
  Platforms/iOS/AppDelegate.cs          # Herda CodePushAppDelegate

Maui.CodePush.Demo.Feature/            # Modulo carregado dinamicamente
  Maui.CodePush.Demo.Feature.csproj    # Class library net9.0-android;net9.0-ios
  MainPage.xaml                         # UI do modulo (substituivel via code push)
  MainPage.xaml.cs                      # Code-behind do modulo
```

## Arquivos Detalhados

### CodePush.cs
Engine principal. Classe estatica que gerencia todo o ciclo de vida.
- `Initialize(options)`: Configura paths, cria diretorios, registra AssemblyResolve, limpa debug assemblies
- `AssemblyResolve(sender, args)`: Handler que intercepta carregamento de modulos. Chama ResolveAssembly, carrega via Assembly.LoadFrom, marca como applied no manifesto
- `ResolveAssembly(moduleName)`: Cadeia de prioridade: temp -> base -> embedded. Move DLL do temp para base se existir
- `UnpackEmbeddedReference(moduleName, localDllFile)`: Extrai DLL dos assets (Android: `Platform.AppContext.Assets.Open`, iOS: `GetManifestResourceStream`)
- `CheckUpdatesAsync()`: Consulta servidor, baixa updates, verifica hash, salva no temp
- `Rollback()` / `Rollback(moduleName)`: Deleta DLLs baixadas, reseta manifesto. Proximo start usa embedded
- `DeleteDebugAssemblies()`: Android: limpa `.__override__/` (fast deployment). iOS: limpa `{assembly}.content/`

### AppBuilderExtensions.cs
Ponto de entrada para o consumidor. `UseCodePush(Action<CodePushOptions>)` cria options, invoca callback, chama `CodePush.Initialize()`.

### AssemblyRegister.cs
Classe interna que mantem um `HashSet<string>` de nomes de DLLs registradas. Adiciona extensao `.dll` automaticamente. Impede registro duplicado.

### CodePushOptions.cs
Configuracao do consumidor:
- `ServerUrl` / `AppKey` / `Channel`: Para comunicacao com servidor (Fase 2)
- `CheckOnStartup`: Se deve verificar updates ao iniciar
- `UpdatePolicy`: `OnNextRestart` (padrao), `Immediate`, `Prompt`
- `AddModule(name)`: Registra modulo para carregamento dinamico

### ModuleManifest.cs / ModuleInfo.cs
Manifesto JSON persistido em `Modules/codepush-manifest.json`:
- Rastreia versao, hash SHA-256, timestamps, status de cada modulo
- Status: `Embedded` (original), `Active` (update aplicado), `Pending` (baixado, aguardando restart), `RolledBack`

### ModuleManager.cs
Gerencia o manifesto e integridade:
- `ComputeHash()` / `VerifyHash()`: SHA-256 para verificar integridade de DLLs baixadas
- `MarkUpdated()` / `MarkApplied()` / `MarkRolledBack()`: Transicoes de estado
- `Reset()`: Limpa manifesto (usado no rollback total)

### UpdateClient.cs
HTTP client para comunicacao com servidor:
- `CheckForUpdatesAsync()`: GET com app key, module name, versao atual, plataforma, channel
- `DownloadModuleAsync()`: Baixa DLL, verifica hash, salva no temp
- `DownloadModuleFromUrlAsync()`: Baixa de URL direta (para testes sem servidor)

### CodePushApplication.cs (Android)
Base class que o consumidor herda no `MainApplication`. Placeholder para logica futura de platform Android.

### CodePushAppDelegate.cs (iOS)
Base class que o consumidor herda no `AppDelegate`. Captura `AssemblyName` do host assembly para usar em `GetManifestResourceStream` ao extrair embedded resources no iOS.

### Maui.CodePush.props (MSBuild)
Importado cedo no build. Adiciona `<Using Include="Maui.CodePush" />` para implicit using.

### Maui.CodePush.targets (MSBuild)
Importado tarde no build. Contem 5 targets:
1. **CodePush_ConfigureiOSInterpreter** (BeforeTargets="Build", iOS): Seta `MtouchInterpreter=-all,ModuleName` para habilitar interpreter apenas nos modulos
2. **CodePush_RemoveModulesFromAndroid** (AfterTargets="_PrepareAssemblies"): Remove modulos de `_ShrunkUserAssemblies` e `_ShrunkFrameworkAssemblies` antes da criacao do assembly store. Usa matching por nome com lista delimitada por ponto-e-virgula
3. **CodePush_RemoveModulesFromiOS** (BeforeTargets="_CompileNativeExecutable"): Deleta DLLs de refint/, resourcestamps/, e output do build iOS
4. **CodePush_RemoveModulesAfterBuild** (AfterTargets="AfterBuild", iOS): Cleanup adicional pos-build
5. **CodePush_RemoveModulesFromHotRestart** (AfterTargets="_CopyFilesToHotRestartContentDir"): Remove de `_HotRestartContentDirContents` para dev com Hot Restart

## API do Consumidor

### csproj
```xml
<!-- Declarar modulos CodePush -->
<ItemGroup>
  <CodePushModule Include="MeuApp.Feature" />
</ItemGroup>

<!-- Embeddar DLL como recurso (fallback) -->
<ItemGroup>
  <EmbeddedResource Include="MeuApp.Feature.dll" />
</ItemGroup>

<!-- Importar targets (automatico via NuGet, manual via ProjectReference) -->
<Import Project="..\Maui.CodePush\build\Maui.CodePush.props" />
<Import Project="..\Maui.CodePush\build\Maui.CodePush.targets" />
```

### MauiProgram.cs
```csharp
builder.UseCodePush(options =>
{
    options.AddModule("MeuApp.Feature");
    // Fase 2:
    // options.ServerUrl = "https://codepush.example.com";
    // options.AppKey = "my-key";
});
```

### Platform Entry Points
```csharp
// Android: MainApplication.cs
public class MainApplication : CodePushApplication { ... }

// iOS: AppDelegate.cs
public class AppDelegate : CodePushAppDelegate { ... }
```

## Compliance com App Stores

- **Apple iOS (Section 3.3.2)**: O Mono interpreter eh parte do binario do app. Interpretar IL bytecode eh analogo a JavaScript no WebKit - permitido. React Native CodePush e Shorebird operam sob o mesmo principio
- **Google Play**: Apps podem executar codigo em VMs (.NET runtime) com acesso limitado a APIs Android
- **Recomendacao**: Posicionar como ferramenta de hotfix/bug fix, nao para adicionar features grandes que precisariam de review

## Fases do Produto

### Fase 1 (Atual) - MVP Local
- [x] Core engine com AssemblyResolve
- [x] MSBuild targets genericos (Android + iOS)
- [x] Version tracking com manifesto JSON
- [x] Rollback
- [x] Hash verification (SHA-256)
- [x] Validado em device Android fisico
- [ ] Validar em device iOS fisico (MtouchInterpreter)
- [ ] Teste end-to-end com servidor HTTP simples

### Fase 2 - Servidor + CLI
- [ ] Backend API (ASP.NET Core): registro de apps, upload de releases, check de updates
- [ ] CLI Tool (`dotnet-codepush`): init, login, release, rollback
- [ ] Code signing (RSA-2048)
- [ ] Updates diferenciais (bsdiff)

### Fase 3 - Producao + Fork MonoVM
- [ ] CDN, staged rollouts, analytics
- [ ] Estudo do fork MonoVM (`dotnet/runtime` -> `src/mono/`) para otimizar interpreter
- [ ] Dashboard web

## Convencoes

- Todos os logs usam prefixo `[CodePush]` via `Debug.WriteLine`
- Namespace unico: `Maui.CodePush` para toda a biblioteca
- Modulos sao class libraries .NET MAUI padrao (podem conter XAML, ViewModels, services)
- O consumidor referencia o modulo via ProjectReference durante dev (preserva IntelliSense/Hot Reload)
- MSBuild targets usam prefixo `CodePush_` nos nomes e `_CodePush` nos item groups internos

## Troubleshooting

### Android: DLL nao esta sendo removida do APK
- Verificar que `<CodePushModule Include="NomeDoModulo" />` esta no csproj
- Verificar que os `.targets` estao importados (automatico via NuGet, manual via ProjectReference)
- Para debug, usar `EmbedAssembliesIntoApk=true` para embutir assemblies no APK
- Inspecionar APK: `unzip -l app-Signed.apk | grep Feature` - deve mostrar apenas `assets/Feature.dll`

### Android: App crasha apos clean install
- Em debug mode sem `EmbedAssembliesIntoApk=true`, assemblies dependem de fast deployment
- Clean install via `adb install` nao inclui fast deployment
- Usar `dotnet build -t:Install -p:EmbedAssembliesIntoApk=true` ou deploy via Visual Studio

### AssemblyResolve nao dispara
- A DLL ainda esta no assembly store. Verificar build output por mensagens `[Maui.CodePush]`
- No Android fast deployment (debug), verificar que `DeleteDebugAssemblies` esta limpando `.__override__/`
