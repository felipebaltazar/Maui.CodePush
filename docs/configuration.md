# App Configuration

How to set up your .NET MAUI project for CodePush.

---

## NuGet Package

```bash
dotnet add package CodePush.Maui --prerelease
```

## Project Structure

A typical CodePush project has:

```
MyApp/                          ← Your MAUI app
  MyApp.csproj
  MauiProgram.cs
  Platforms/
    Android/MainApplication.cs
    iOS/AppDelegate.cs

MyApp.Feature/                  ← Module managed by CodePush
  MyApp.Feature.csproj
  MainPage.xaml
  MainPage.xaml.cs
```

The Feature module is a standard .NET MAUI class library. You develop it normally with full IntelliSense and Hot Reload. CodePush handles the dynamic loading at runtime.

## csproj Configuration

### App Project (MyApp.csproj)

```xml
<!-- 1. Declare CodePush modules -->
<ItemGroup>
  <CodePushModule Include="MyApp.Feature" />
</ItemGroup>

<!-- 2. Embed the DLL as fallback -->
<ItemGroup>
  <EmbeddedResource Include="MyApp.Feature.dll" />
</ItemGroup>

<!-- 3. Reference your feature module normally -->
<ItemGroup>
  <ProjectReference Include="..\MyApp.Feature\MyApp.Feature.csproj" />
</ItemGroup>

<!-- 4. Import CodePush targets -->
<!-- Automatic if using NuGet PackageReference -->
<!-- Manual if using ProjectReference to the library: -->
<Import Project="..\Maui.CodePush\build\Maui.CodePush.props" />
<Import Project="..\Maui.CodePush\build\Maui.CodePush.targets" />
```

### What the MSBuild targets do

At build time, CodePush:

1. **Android**: Removes the Feature DLL from the assembly store (`libxamarin-app.so`) so it's not loaded normally
2. **iOS**: Configures `MtouchInterpreter` for hybrid AOT + interpreter mode, and removes the DLL from build outputs
3. The DLL remains as an `EmbeddedResource` (fallback for first launch)

### Feature Module (MyApp.Feature.csproj)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net9.0-android;net9.0-ios</TargetFrameworks>
    <UseMaui>true</UseMaui>
  </PropertyGroup>
</Project>
```

No special configuration needed. It's a regular MAUI class library.

## MauiProgram.cs

```csharp
builder.UseCodePush(options =>
{
    // Required: which modules to load dynamically
    options.AddModule("MyApp.Feature");

    // Required: your app store version (must match codepush release create --version)
    options.ReleaseVersion = "1.0.0";

    // Server connection (from codepush apps list)
    options.ServerUrl = "https://codepush.monkseal.dev";
    options.AppId = "your-app-id-guid";
    options.AppToken = "your-app-token";

    // Optional
    options.Channel = "production";           // default
    options.CheckOnStartup = true;            // default
    options.UpdatePolicy = UpdatePolicy.OnNextRestart;  // default
});
```

## Platform Entry Points

### Android

```csharp
// Platforms/Android/MainApplication.cs
[Application]
public class MainApplication : CodePushApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership) { }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
```

### iOS

```csharp
// Platforms/iOS/AppDelegate.cs
[Register("AppDelegate")]
public class AppDelegate : CodePushAppDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
```

## Update Check

Add to your `App.xaml.cs`:

```csharp
protected override async void OnStart()
{
    base.OnStart();
    await CodePush.CheckUpdatesAsync();
}
```

This checks the server for patches on every app launch. Updates download in the background and apply on the next cold start.

## CodePushOptions Reference

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `ServerUrl` | string? | null | CodePush server URL |
| `AppId` | string? | null | App GUID from `codepush apps add` |
| `AppToken` | string? | null | App token for update checks |
| `ReleaseVersion` | string? | null | App store version (e.g., "1.0.0") |
| `Channel` | string | "production" | Release channel |
| `CheckOnStartup` | bool | true | Check for updates when app starts |
| `UpdatePolicy` | UpdatePolicy | OnNextRestart | When to apply updates |

### UpdatePolicy

| Value | Behavior |
|-------|----------|
| `OnNextRestart` | Download now, apply on next cold start (default) |
| `Immediate` | Download and force restart |
| `Prompt` | Download then prompt user to restart |

## Module Limitations

Modules loaded via CodePush have some restrictions:

- **No custom handlers** or anything that initializes with the application
- **No heavy AOT** dependencies (iOS uses interpreter for modules)
- **Can contain**: XAML pages, ViewModels, services, converters, models
- **Cannot reference new assemblies** not present in the release (compatibility check prevents this)
