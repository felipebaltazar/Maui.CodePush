# Models/

Data classes para configuracao, estado e comunicacao com servidor.
Todos usam `System.Text.Json` com `[JsonPropertyName]` para serialização.

| Arquivo | Papel |
|---------|-------|
| `CodePushOptions.cs` | Configuracao do consumidor passada via `UseCodePush()`. Contem `AssemblyRegister` interno populado por `AddModule()`. `UpdatePolicy` enum define quando aplicar (OnNextRestart, Immediate, Prompt). |
| `ModuleManifest.cs` | Raiz do manifesto JSON. Persistido em `Modules/codepush-manifest.json`. |
| `ModuleInfo.cs` | Estado de um modulo individual. `ModuleStatus` enum: Embedded -> Pending -> Active -> RolledBack. Hash SHA-256 para verificacao de integridade. `PreviousHash` permite rollback. |
| `UpdateCheckResult.cs` | Resposta do endpoint `GET /api/updates/check`. `ModuleUpdateInfo` contem URL de download, hash esperado, tamanho. |

## Ciclo de Vida do ModuleStatus

```
Embedded  -->  Pending  -->  Active
   ^              |
   |              v
   +--- RolledBack
```

- **Embedded**: DLL original do APK/IPA (fallback)
- **Pending**: Nova DLL baixada, aguardando restart
- **Active**: DLL aplicada e em uso
- **RolledBack**: Revertido para Embedded
