# Services/

Servicos de infraestrutura para gerenciamento de modulos e comunicacao HTTP.

| Arquivo | Papel |
|---------|-------|
| `ModuleManager.cs` | Gerencia o manifesto JSON e integridade. Instanciado em `CodePush.Initialize()` com `_basePath`. `ComputeHash()` e `VerifyHash()` usam SHA-256. Transicoes: `MarkUpdated()` (salva hash anterior), `MarkApplied()` (seta timestamp), `MarkRolledBack()` (reverte hash). `Reset()` limpa tudo. |
| `UpdateClient.cs` | HTTP client para o servidor CodePush. Instanciado com `CodePushOptions` e `_tempPath`. `CheckForUpdatesAsync()` faz GET com query params. `DownloadModuleAsync()` baixa e verifica hash. Retorna `null` se hash nao bater. |

## Paths no Device

- `ModuleManager` opera em `_basePath` = `Personal/Modules/` (persistente)
- `UpdateClient` baixa para `_tempPath` = `TempPath/Modules/` (staging)
- No proximo restart, `CodePush.ResolveAssembly()` move de temp para base
