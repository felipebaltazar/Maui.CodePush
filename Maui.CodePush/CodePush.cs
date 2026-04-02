using System.Diagnostics;
using System.Reflection;

namespace Maui.CodePush;

public static class CodePush
{
    private static string _basePath = string.Empty;
    private static string _tempPath = string.Empty;
    private static CodePushOptions? _options;
    private static ModuleManager? _moduleManager;
    private static UpdateClient? _updateClient;
    private static bool _initialized;

    internal static AssemblyRegister Register => _options?.Register ?? new AssemblyRegister();
    internal static ModuleManager? Manager => _moduleManager;

    internal static void Initialize(CodePushOptions options)
    {
        if (_initialized)
            return;

        _options = options;
        _basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Modules");
        _tempPath = Path.Combine(Path.GetTempPath(), "Modules");

        if (!Directory.Exists(_basePath))
            Directory.CreateDirectory(_basePath);

        if (!Directory.Exists(_tempPath))
            Directory.CreateDirectory(_tempPath);

        _moduleManager = new ModuleManager(_basePath);
        _updateClient = new UpdateClient(options, _tempPath);

        AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolve;

        DeleteDebugAssemblies();

        _initialized = true;

        Debug.WriteLine("[CodePush] Initialized successfully.");
    }

    public static Assembly? AssemblyResolve(object? sender, ResolveEventArgs args)
    {
        var embeddedAssembly = new AssemblyName(args.Name);
        var moduleName = $"{embeddedAssembly.Name}.dll";

        if (!Register.Assemblies.Any(n => n == moduleName))
            return null;

        var dllLocalPath = ResolveAssembly(moduleName);

        if (string.IsNullOrWhiteSpace(dllLocalPath) || !File.Exists(dllLocalPath))
            return null;

        try
        {
            var assembly = Assembly.LoadFrom(dllLocalPath);
            _moduleManager?.MarkApplied(Path.GetFileNameWithoutExtension(moduleName));
            Debug.WriteLine($"[CodePush] Loaded module: {moduleName}");
            return assembly;
        }
        catch (BadImageFormatException)
        {
            Debug.WriteLine($"[CodePush] BadImageFormat for {moduleName}, attempting unpack from embedded...");

            try
            {
                var localDllFile = Path.Combine(_basePath, moduleName);
                UnpackEmbeddedReference(moduleName, localDllFile, deleteIfExists: true);

                if (File.Exists(localDllFile))
                    return Assembly.LoadFrom(localDllFile);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CodePush] Failed to recover {moduleName}: {ex.Message}");
            }
        }

        return null;
    }

    public static async Task CheckUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (_updateClient is null || _options is null || _moduleManager is null)
        {
            Debug.WriteLine("[CodePush] Not initialized. Call UseCodePush() first.");
            return;
        }

        try
        {
            var result = await _updateClient.CheckForUpdatesAsync(cancellationToken);

            if (result?.UpdateAvailable == true)
            {
                // Use patches (new model) or modules (legacy)
                var updates = result.Patches.Count > 0 ? result.Patches : result.Modules;

                foreach (var patch in updates)
                {
                    var dllName = $"{patch.Name}.dll";
                    if (!Register.Assemblies.Any(n => n == dllName))
                        continue;

                    // Skip if already on this version (compare hash)
                    var currentInfo = _moduleManager.GetModuleInfo(patch.Name);
                    if (currentInfo?.Hash == patch.Hash)
                        continue;

                    var downloadedPath = await _updateClient.DownloadModuleAsync(patch, cancellationToken);
                    if (downloadedPath != null)
                    {
                        _moduleManager.MarkUpdated(patch.Name, patch.Version, downloadedPath);
                        Debug.WriteLine($"[CodePush] Patch downloaded for {patch.Name} v{patch.Version}");
                    }
                }
            }

            _moduleManager.UpdateLastChecked();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CodePush] Update check failed: {ex.Message}");

            if (Debugger.IsAttached)
                Debugger.Break();
        }
    }

    public static void Rollback()
    {
        if (Directory.Exists(_basePath))
        {
            Directory.Delete(_basePath, true);
            Directory.CreateDirectory(_basePath);
        }

        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, true);
            Directory.CreateDirectory(_tempPath);
        }

        _moduleManager?.Reset();

        Debug.WriteLine("[CodePush] Rolled back to embedded modules.");
    }

    public static void Rollback(string moduleName)
    {
        var dllName = moduleName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? moduleName
            : $"{moduleName}.dll";

        var baseDll = Path.Combine(_basePath, dllName);
        var tempDll = Path.Combine(_tempPath, dllName);

        if (File.Exists(tempDll))
            File.Delete(tempDll);

        if (File.Exists(baseDll))
            File.Delete(baseDll);

        _moduleManager?.MarkRolledBack(Path.GetFileNameWithoutExtension(dllName));

        Debug.WriteLine($"[CodePush] Rolled back module: {moduleName}");
    }

    private static string ResolveAssembly(string moduleName)
    {
        var tempLocalDll = Path.Combine(_tempPath, moduleName);
        var localDllFile = Path.Combine(_basePath, moduleName);

        // Priority 1: temp folder (pending updates)
        if (File.Exists(tempLocalDll))
        {
            File.Copy(tempLocalDll, localDllFile, true);
            File.Delete(tempLocalDll);
            Debug.WriteLine($"[CodePush] Applied pending update for {moduleName}");
        }

        // Priority 2: base folder (persisted)
        if (File.Exists(localDllFile))
            return localDllFile;

        // Priority 3: embedded resource (fallback)
        UnpackEmbeddedReference(moduleName, localDllFile);

        return localDllFile;
    }

    private static void UnpackEmbeddedReference(string moduleName, string localDllFile, bool deleteIfExists = false)
    {
        try
        {
            if (deleteIfExists && File.Exists(localDllFile))
                File.Delete(localDllFile);

#if ANDROID
            using var embedded = Platform.AppContext.Assets!.Open(moduleName);
            using var fileStream = File.Create(localDllFile);
            embedded.CopyTo(fileStream);
#else
            var assemblyName = CodePushAppDelegate.AssemblyName;
            var assembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == assemblyName);

            if (assembly is null)
            {
                Debug.WriteLine($"[CodePush] Host assembly '{assemblyName}' not found for embedded resource extraction.");
                return;
            }

            using var stream = assembly.GetManifestResourceStream(moduleName);
            if (stream is null)
            {
                Debug.WriteLine($"[CodePush] Embedded resource '{moduleName}' not found in assembly '{assemblyName}'.");
                return;
            }

            using var fileStream = File.Create(localDllFile);
            stream.CopyTo(fileStream);
#endif
            Debug.WriteLine($"[CodePush] Unpacked embedded resource: {moduleName}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CodePush] Failed to unpack {moduleName}: {ex.Message}");
        }
    }

    internal static void DeleteDebugAssemblies()
    {
#if ANDROID
        // Fast deployment directory: /data/user/0/<package>/files/.__override__/
        var filesDir = Platform.AppContext.FilesDir!.AbsolutePath;
        var overrideDir = Path.Combine(filesDir, ".__override__");

        if (!Directory.Exists(overrideDir))
            return;

        foreach (var assembly in Register.Assemblies)
        {
            // Check root override directory
            var filePath = Path.Combine(overrideDir, assembly);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Debug.WriteLine($"[CodePush] Deleted debug assembly: {filePath}");
            }

            // Check arch-specific subdirectories (arm64-v8a, armeabi-v7a, x86_64, etc.)
            try
            {
                foreach (var subDir in Directory.GetDirectories(overrideDir))
                {
                    var archFilePath = Path.Combine(subDir, assembly);
                    if (File.Exists(archFilePath))
                    {
                        File.Delete(archFilePath);
                        Debug.WriteLine($"[CodePush] Deleted debug assembly: {archFilePath}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CodePush] Error scanning override dirs: {ex.Message}");
            }
        }
#elif IOS
        var assemblyName = CodePushAppDelegate.AssemblyName;
        var applicationDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            $"{assemblyName}.content");

        if (!Directory.Exists(applicationDataFolder))
            return;

        foreach (var assembly in Register.Assemblies)
        {
            var finalFilePath = Path.Combine(applicationDataFolder, assembly);
            if (File.Exists(finalFilePath))
            {
                File.Delete(finalFilePath);
                Debug.WriteLine($"[CodePush] Deleted debug assembly: {finalFilePath}");
            }
        }
#endif
    }
}
