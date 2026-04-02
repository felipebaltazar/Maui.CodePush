# Platforms/

Classes base platform-specific que o app consumidor deve herdar.

## Android/
- `CodePushApplication.cs` — Herda `MauiApplication`. O consumidor cria `MainApplication : CodePushApplication`. Atualmente placeholder; logica de inicializacao movida para `CodePush.Initialize()`.

## iOS/
- `CodePushAppDelegate.cs` — Herda `MauiUIApplicationDelegate`. Captura `GetType().Assembly.GetName().Name` no construtor e expoe via `AssemblyName` static. Esse nome eh usado por `CodePush.UnpackEmbeddedReference()` para localizar o assembly host ao extrair embedded resources via `GetManifestResourceStream()`.

## Por que classes base?
O consumidor precisa herdar para que o CodePush tenha acesso ao contexto da aplicacao. No iOS especificamente, o nome do assembly host eh necessario para extrair embedded resources. No Android, `Platform.AppContext` ja esta disponivel globalmente.
