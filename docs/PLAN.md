# Plano de Implementacao — Maui.CodePush

Documento vivo com o historico de decisoes, planejamento original e roadmap tecnico.
Criado em 2026-04-01 durante a sessao de planejamento inicial.

---

## Contexto e Motivacao

O .NET MAUI nao possui uma solucao de code push / OTA updates como outros frameworks moveis:
- **Flutter**: Shorebird (fork do Dart VM para hot-patching de codigo AOT)
- **React Native**: CodePush (Microsoft, substituicao de JS bundles)
- **Xamarin**: Existia um mecanismo experimental via `Assembly.Load` (base deste projeto)

O Maui.CodePush visa preencher essa lacuna como **produto comercial**, permitindo que desenvolvedores .NET MAUI publiquem hotfixes sem passar pela revisao das app stores.

### Historico
O projeto nasceu de experimentos feitos em Xamarin por Felipe Baltazar, onde:
- No Android, `Assembly.Load` funcionava via `AppDomain.CurrentDomain.AssemblyResolve`
- DLLs eram removidas do APK via MSBuild tasks customizadas e carregadas de assets
- No iOS, `Assembly.Load` era bloqueado (Xamarin nao tinha interpreter mode)
- Limitacoes: nao podia usar custom renderers, AOT pesado, nem evoluiu para iOS

O projeto foi migrado para .NET MAUI como proof-of-concept e em 2026-04-01 passou por uma reestruturacao completa.

---

## Decisoes Tecnicas (2026-04-01)

### 1. iOS Strategy
**Opcoes avaliadas:**
- (A) Full interpreter (`UseInterpreter=true`): Performance ruim para o app inteiro
- (B) **Hibrido AOT + Interpreter (`MtouchInterpreter`)**: App principal AOT, so modulos CodePush interpretados — **ESCOLHIDO para MVP**
- (C) Fork do .NET Runtime (como Shorebird fez com Dart VM): Maximo poder, mas esforco de anos — **ESCOLHIDO como evolucao futura**
- (D) XAML-only updates: Muito limitado
- (E) MetadataLoadContext: So reflection, nao executa codigo

**Resultado**: MVP com MtouchInterpreter, investigacao do fork MonoVM (`dotnet/runtime` -> `src/mono/`) como Fase 3.

### 2. Target Framework
**Escolha**: net9.0 apenas (nao net8.0). .NET 9 tem melhorias no interpreter e AOT relevantes para o CodePush.

### 3. Escopo de Update
**Escolha**: DLLs completas de modulos isolados (nao assemblies arbitrarios do app). O desenvolvedor escolhe quais projetos sao "modulos CodePush".

### 4. Quando Aplicar Updates
**Escolha**: `OnNextRestart` como padrao. Uma vez que um assembly eh carregado via `Assembly.Load`, ele NAO pode ser descarregado do processo .NET. Updates so tomam efeito no proximo cold start.

### 5. MSBuild Hook Points (descoberta critica)
Em .NET 9 Android, assemblies sao empacotados em `libxamarin-app.so` (assembly store). O hook `BeforeTargets="_GenerateAndroidAssetsDir"` que funcionava no .NET 8 nao funciona mais.

**Hook correto**: `AfterTargets="_PrepareAssemblies"` — remove de `_ShrunkUserAssemblies` e `_ShrunkFrameworkAssemblies` ANTES do assembly store ser criado.

Cadeia completa descoberta no SDK:
```
_ResolveAssemblies -> _PrepareAssemblies -> _CollectAssembliesToCompress
-> _ProcessAssemblyCompressionFailures -> CreateAssemblyStore (em _BuildApkEmbed)
```

---

## Fase 1 — MVP Local (Status: Em Progresso)

### Completado
- [x] Remocao de paths hardcoded do demo
- [x] Correcao de `async void` em `UnpackEmbeddedReference`
- [x] `CodePushOptions` com options pattern
- [x] `ModuleManifest` / `ModuleInfo` para version tracking
- [x] `ModuleManager` com SHA-256 e rollback
- [x] `UpdateClient` com HTTP + hash verification
- [x] MSBuild targets genericos via `<CodePushModule>`
- [x] Target Android: `AfterTargets="_PrepareAssemblies"` funcional
- [x] Target iOS: `MtouchInterpreter` configurado automaticamente
- [x] Migracao para net9.0
- [x] Validacao em device Android fisico (Samsung via USB)
- [x] Teste de code push: DLL v2 pushada via adb, app carregou UI nova no restart

### Pendente
- [ ] Validacao em device iOS fisico (MtouchInterpreter + Assembly.Load)
- [ ] Teste end-to-end com servidor HTTP simples (nao adb push)
- [ ] Teste de rollback em device
- [ ] Publicacao do NuGet package (preview)

---

## Fase 2 — Servidor + CLI

### Backend API (ASP.NET Core Minimal API + EF Core + PostgreSQL)
Endpoints:
- `POST /api/apps` — Registrar app, retorna API key
- `GET /api/apps/{key}/updates/check` — Client chama para verificar updates
- `POST /api/apps/{key}/releases` — CLI faz upload de release
- `DELETE /api/apps/{key}/releases/{id}` — Rollback
- `GET /api/apps/{key}/releases` — Listar releases
- `PATCH /api/apps/{key}/releases/{id}` — Ajustar rollout %

### CLI Tool (`dotnet-codepush`, .NET global tool)
```
codepush init                    — Setup interativo, cria .codepush config
codepush login                   — Autenticar com servidor
codepush apps create <name>      — Registrar app
codepush release <path>          — Upload de release (--app, --channel, --version, --platform)
codepush rollback <app-key>      — Rollback da release mais recente
codepush releases list <app-key> — Listar releases
codepush releases promote        — Promover release entre channels
```

### Code Signing
- CLI gera par RSA-2048 no `codepush init`
- Chave privada fica com o dev (`.codepush/signing-key.pem`, git-ignored)
- Chave publica embutida no app
- CLI assina hash SHA-256 da DLL ao fazer release
- Client verifica assinatura antes de `Assembly.Load`

### Updates Diferenciais
- Algoritmo `bsdiff` para patches binarios
- Servidor armazena diffs das ultimas N versoes
- Client baixa diff se disponivel, aplica com `bspatch`, valida hash
- Fallback para DLL completa sempre disponivel

---

## Fase 3 — Producao + Fork MonoVM

### Producao
- CDN (Azure Blob + CDN ou Cloudflare)
- Staged rollouts (% baseado em hash do device_id)
- Analytics (adoption rate, crash rate por versao)
- Dashboard web (Blazor ou React)
- Multi-tenant (Organization > App > Channel > Release)

### Fork do MonoVM (Investigacao)
Objetivo: otimizar execucao de modulos dinamicos no iOS.

Areas do `dotnet/runtime`:
- `src/mono/mono/mini/aot-runtime.c` — Carregamento de codigo AOT
- `src/mono/mono/metadata/assembly.c` — Carregamento de assemblies
- `src/mono/mono/mini/interp/` — Interpreter Mono (otimizacoes)
- Possibilidade de pre-gerar "traces otimizados" no servidor

Restricao Apple: nao eh possivel carregar .dylib novo em runtime. O interpreter (embutido) eh o unico caminho. Fork focaria em OTIMIZAR o interpreter.

---

## Riscos e Mitigacoes

| Risco | Impacto | Mitigacao |
|-------|---------|-----------|
| Assembly.Load falha no iOS com interpreter | Bloqueia iOS | Testar cedo. Fallback: `Assembly.Load(byte[])` |
| Apple rejeita apps | Bloqueia distribuicao | Precedente: React Native CodePush, Shorebird. Posicionar como bug-fix |
| Performance interpreter ruim | Degrada UX | Modulos UI-light. Computacao pesada no host AOT |
| MSBuild targets quebram | Quebra build | CI matrix contra .NET 9.x releases |
| Conflitos de versao | Crashes | Incluir versao .NET no manifesto. Rejeitar incompativeis |

---

## Compliance App Store

### Apple iOS (Section 3.3.2)
O Mono interpreter eh parte do binario do app (dentro do .NET runtime no IPA). Interpretar IL bytecode eh analogo a JavaScript no WebKit. React Native CodePush e Shorebird operam sob o mesmo principio ha anos.

### Google Play (Deceptive Behavior)
Apps podem executar codigo em VMs (.NET runtime) com acesso limitado a APIs Android. Assemblies .NET rodando no Mono qualificam sob esta excecao.

### Recomendacao
Posicionar como ferramenta de hotfix/bug fix. Nao usar para adicionar features grandes que precisariam de review.
