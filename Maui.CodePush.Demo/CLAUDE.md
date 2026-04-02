# Maui.CodePush.Demo

App de demonstracao que usa o CodePush para carregar o modulo Feature dinamicamente.
Serve como referencia de integracao e campo de testes.

## Configuracao Chave (csproj)
- `<CodePushModule Include="Maui.CodePush.Demo.Feature" />` — Declara o modulo para os MSBuild targets
- `<EmbeddedResource Include="Maui.CodePush.Demo.Feature.dll" />` — Embedda a DLL como fallback
- Importa `Maui.CodePush.props` e `Maui.CodePush.targets` manualmente (ProjectReference, nao NuGet)
- iOS signing: `Apple Development: Created via API (XNG86Q8UW7)`

## Arquivos

| Arquivo | Papel |
|---------|-------|
| `MauiProgram.cs` | Chama `UseCodePush()` registrando `Maui.CodePush.Demo.Feature` como modulo. |
| `App.xaml.cs` | `OnStart()` chama `CodePush.CheckUpdatesAsync()` em background. |
| `AppShell.xaml` | Referencia `Maui.CodePush.Demo.Feature.MainPage` via `clr-namespace` — o assembly eh carregado dinamicamente pelo CodePush quando o Shell resolve o tipo. |
| `Platforms/Android/MainApplication.cs` | Herda `CodePushApplication`. |
| `Platforms/iOS/AppDelegate.cs` | Herda `CodePushAppDelegate`. |

## Como Testar Update

```bash
# 1. Instalar app (com Feature v1 embeddada)
dotnet build -f net9.0-android -p:EmbedAssembliesIntoApk=true -t:Install

# 2. Modificar Feature (ex: mudar MainPage.xaml)
# 3. Buildar so a DLL
dotnet build Maui.CodePush.Demo.Feature -f net9.0-android

# 4. Pushear para o device
adb push Feature.dll /data/local/tmp/Feature.dll
adb shell "run-as com.companyname.maui.codepush.demo cp /data/local/tmp/Feature.dll \
  /data/user/0/com.companyname.maui.codepush.demo/cache/Modules/Maui.CodePush.Demo.Feature.dll"

# 5. Reiniciar app — CodePush aplica automaticamente
```
