# FAQ

---

## General

### What can I update via CodePush?

Any code inside your CodePush modules: XAML pages, ViewModels, services, converters, models. Basically anything that lives in the dynamically loaded class library.

### What can't I update?

- Native code (Java/Kotlin on Android, Swift/ObjC on iOS)
- Custom handlers or renderers that initialize with the application
- New NuGet dependencies not present in the release
- App icons, splash screens, or other native resources

### Does it work on iOS?

Yes. CodePush uses `MtouchInterpreter` to enable hybrid AOT + interpreter mode. Your main app runs at full AOT speed, and only CodePush modules use the Mono interpreter for dynamic loading.

### Is this allowed by Apple/Google?

Yes. React Native CodePush and Shorebird (Flutter) have operated under the same principle for years. See [Security](security.md) for details on app store compliance.

---

## Technical

### How big are the updates?

Module DLLs are typically 10-50 KB. Downloads are fast even on slow networks.

### When do updates apply?

By default, updates apply on the next **cold start** of the app. Once an assembly is loaded into the .NET runtime, it cannot be unloaded.

### What happens if the server is down?

The app uses the last downloaded version (persisted on disk). If no updates were ever downloaded, it uses the embedded module (bundled in the app package at build time).

### Can I rollback a bad update?

Yes, three ways:
1. **Push a new patch** that fixes the issue
2. **Via CLI**: `codepush rollback --all --restart` (local device)
3. **Via code**: `CodePush.Rollback()` in your app

### How does version targeting work?

Each app has a `ReleaseVersion` baked in (e.g., "1.0.0"). The server only returns patches for that specific release. Users on v1.0.0 never receive patches meant for v1.1.0.

---

## CLI

### How do I log in?

```bash
codepush login
```

This opens your browser. No password is typed in the terminal.

### How do I create an account?

```bash
codepush register
```

This opens [monkseal.dev/register](https://monkseal.dev/register).

### The CLI can't find adb

The CLI searches common locations automatically. If it can't find adb, either:
- Add it to your PATH
- Set `"adbPath"` in `.codepush.json`
- Set the `ANDROID_HOME` environment variable

### Build fails with "Feature DLL not removed from APK"

Make sure:
1. `<CodePushModule Include="YourModule" />` is in your app csproj
2. The CodePush `.props` and `.targets` are imported
3. For Android, build with `EmbedAssembliesIntoApk=true` in debug mode

---

## Self-hosted

### Can I host my own server?

Yes. See [Self-hosted](self-hosted.md) for Docker setup instructions.

### Do I need Azure Blob Storage?

No. The server falls back to local filesystem storage if `AZURE_STORAGE_CONNECTION_STRING` is not configured.

### Do I need the CDN Worker?

No. If `CODEPUSH_CDN_URL` is not set, the server serves downloads directly via its own `/api/updates/download` endpoint.

---

## Billing

### What counts as a patch install?

Each time a device downloads and applies a patch. One push to 10,000 users = up to 10,000 patch installs.

### What happens when I exceed my limit?

On Free: updates stop being delivered until the next billing period.
On Pro/Business: overage billing at $1 per 2,500 installs.

### Can I use promotion codes?

Yes. Enter a code during Stripe checkout, or pass it via the registration API.
