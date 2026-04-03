# Getting Started

Push your first update in 5 minutes.

---

## 1. Install the tools

```bash
# Install the NuGet package in your MAUI app
dotnet add package CodePush.Maui --prerelease

# Install the CLI
dotnet tool install -g dotnet-codepush --prerelease
```

## 2. Create an account

```bash
codepush register
```

This opens [monkseal.dev/register](https://monkseal.dev/register) in your browser. Create your account and choose a plan.

## 3. Log in

```bash
codepush login
```

Opens your browser for authentication. Once you approve, the CLI saves your credentials automatically.

## 4. Create a Feature module

Create a standard .NET MAUI class library for the code you want to update via CodePush:

```bash
dotnet new mauilib -n MyApp.Feature
```

Add your pages, ViewModels, and services to this project. This module will be loaded dynamically at runtime.

## 5. Configure your app

### MyApp.csproj

```xml
<!-- Declare which modules are managed by CodePush -->
<ItemGroup>
  <CodePushModule Include="MyApp.Feature" />
</ItemGroup>

<!-- Embed the DLL as fallback for first launch -->
<ItemGroup>
  <EmbeddedResource Include="MyApp.Feature.dll" />
</ItemGroup>

<!-- Import CodePush build targets (auto for NuGet, manual for ProjectReference) -->
<Import Project="..\path\to\Maui.CodePush\build\Maui.CodePush.props" />
<Import Project="..\path\to\Maui.CodePush\build\Maui.CodePush.targets" />
```

### MauiProgram.cs

```csharp
builder.UseCodePush(options =>
{
    options.AddModule("MyApp.Feature");
    options.ReleaseVersion = "1.0.0";
    options.ServerUrl = "https://codepush.monkseal.dev";
    options.AppId = "your-app-id";
    options.AppToken = "your-app-token";
});
```

### Platform entry points

```csharp
// Android: Platforms/Android/MainApplication.cs
[Application]
public class MainApplication : CodePushApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership) { }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

// iOS: Platforms/iOS/AppDelegate.cs
[Register("AppDelegate")]
public class AppDelegate : CodePushAppDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
```

## 6. Register your app

```bash
codepush init
codepush apps add --package-name com.yourcompany.myapp --name "My App" --set-default
```

## 7. Create your first release

```bash
codepush release create --version 1.0.0 --app-project MyApp/MyApp.csproj
```

This builds your app, captures dependency snapshots, uploads to the server, and creates a git tag `v1.0.0`.

Submit the generated APK/IPA to the app stores.

## 8. Push a patch

Make a change to your Feature module, then:

```bash
codepush patch --release 1.0.0
```

CodePush checks dependency compatibility, uploads the new DLL, and creates a git tag. Your users receive the update on their next app launch.

## Next steps

- [CLI Reference](cli-reference.md) — all commands and options
- [Releases & Patches](releases.md) — how the model works
- [Configuration](configuration.md) — advanced setup options
