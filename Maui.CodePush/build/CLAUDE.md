# build/

MSBuild props e targets distribuidos via NuGet package.
Incluidos em `build/` e `buildTransitive/` no .nupkg.
Para ProjectReference, o consumidor deve importar manualmente no csproj.

## Maui.CodePush.props
Importado CEDO no build (antes dos items do projeto).
- Adiciona `<Using Include="Maui.CodePush" />` para implicit using

## Maui.CodePush.targets
Importado TARDE no build (apos items do projeto e compilacao).
O consumidor declara `<CodePushModule Include="NomeDoModulo" />` no csproj.

### Targets Android
- `CodePush_RemoveModulesFromAndroid` (AfterTargets=`_PrepareAssemblies`): Remove modulos de `_ShrunkUserAssemblies` e `_ShrunkFrameworkAssemblies` ANTES da criacao do assembly store (`libxamarin-app.so`). Usa matching por nome via propriedade delimitada por ponto-e-virgula.

### Targets iOS
- `CodePush_ConfigureiOSInterpreter` (BeforeTargets="Build"): Seta `MtouchInterpreter=-all,Modulo1,Modulo2` para habilitar interpreter apenas nos modulos CodePush (hibrido AOT + interpreter)
- `CodePush_RemoveModulesFromiOS` (BeforeTargets=`_CompileNativeExecutable`): Deleta DLLs de refint/, resourcestamps/, output/
- `CodePush_RemoveModulesAfterBuild` (AfterTargets="AfterBuild"): Cleanup adicional
- `CodePush_RemoveModulesFromHotRestart` (AfterTargets=`_CopyFilesToHotRestartContentDir`): Remove para dev com Hot Restart

### Hook points criticos (descobertos via investigacao do SDK)
- Android: `_PrepareAssemblies` -> `_CollectAssembliesToCompress` -> `_ProcessAssemblyCompressionFailures` -> `CreateAssemblyStore`
- iOS: `_CompileNativeExecutable` eh o ultimo ponto antes da compilacao nativa
