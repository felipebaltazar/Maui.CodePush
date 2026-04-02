# Maui.CodePush.Demo.Feature

Modulo carregado dinamicamente pelo CodePush. Class library .NET MAUI padrao.
Este projeto simula um "feature module" que pode ser atualizado via code push sem reinstalar o app.

## Arquivos

| Arquivo | Papel |
|---------|-------|
| `MainPage.xaml` | UI do modulo. Pagina referenciada pelo `AppShell.xaml` do Demo via `clr-namespace`. Ao modificar este arquivo e pushear a DLL nova, o app mostra a UI atualizada no proximo restart. |
| `MainPage.xaml.cs` | Code-behind. Logica de interacao (contador, animacoes). |
| `.csproj` | Class library `net9.0-android;net9.0-ios`. Referencia `Maui.CodePush` (para base classes). |

## Regras
- NAO deve conter nada que inicialize com a aplicacao (custom handlers, startup services)
- NAO deve ter dependencias pesadas que exigam AOT
- Pode conter XAML pages, ViewModels, services, conversores — qualquer coisa que o runtime consiga carregar via Assembly.Load
- A DLL compilada eh ~10KB. Updates sao rapidos mesmo em redes lentas
