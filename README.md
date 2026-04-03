<p align="center">
  <img src="icon.png" alt="Monkseal CodePush" width="120" />
</p>

<h1 align="center">CodePush for .NET MAUI</h1>

<p align="center">
  <strong>Ship hotfixes to your .NET MAUI apps instantly. No app store review. No waiting.</strong>
  <br/>
  <em>A <a href="https://github.com/felipebaltazar/Maui.CodePush">Monkseal</a> product</em>
</p>

CodePush is an over-the-air (OTA) update system for .NET MAUI, bringing the same capabilities that [Shorebird](https://shorebird.dev/) offers for Flutter and [CodePush](https://microsoft.github.io/code-push/) offered for React Native — now for the .NET ecosystem.

> Push bug fixes, UI changes, and minor updates directly to your users' devices on **Android** and **iOS**, without going through app store review cycles.

---

## How It Works

```
  Developer                    Device
  ─────────                    ──────
  1. Build Feature module
  2. Push DLL update    ───>   3. App detects update
                               4. Downloads new DLL
                               5. Applies on next restart
                               6. Users see the fix instantly
```

At build time, CodePush **removes your feature modules from the app package** and embeds them as extractable resources. At runtime, modules are loaded dynamically via `Assembly.LoadFrom`. When an update is available, the new DLL replaces the current one — no reinstall needed.

## Quick Start

### 1. Install the NuGet package

```
dotnet add package CodePush.Maui --prerelease
dotnet tool install -g dotnet-codepush --prerelease
```

### 2. Create a Feature module

Create a standard .NET MAUI class library with your pages, ViewModels, and services:

```
dotnet new mauiclass -n MyApp.Feature
```

### 3. Configure your app

**MyApp.csproj:**
```xml
<ItemGroup>
  <!-- Declare which modules are managed by CodePush -->
  <CodePushModule Include="MyApp.Feature" />
</ItemGroup>

<ItemGroup>
  <!-- Embed the DLL as fallback for first launch -->
  <EmbeddedResource Include="MyApp.Feature.dll" />
</ItemGroup>
```

**MauiProgram.cs:**
```csharp
builder.UseCodePush(options =>
{
    options.AddModule("MyApp.Feature");
    options.ReleaseVersion = "1.0.0";     // matches your app store version
    options.ServerUrl = "https://your-codepush-server.com";
    options.AppId = "your-app-id";
    options.AppToken = "your-app-token";
});
```

**Platform entry points:**
```csharp
// Android: MainApplication.cs
public class MainApplication : CodePushApplication { ... }

// iOS: AppDelegate.cs
public class AppDelegate : CodePushAppDelegate { ... }
```

### 4. Create a release and push patches

```bash
# Login to CodePush server
codepush login --email you@email.com --password yourpass

# Register your app
codepush apps add --package-name com.myapp --name "My App" --set-default

# Create a release (captures dependency snapshot + git tag)
codepush release create --version 1.0.0 --app-project MyApp/MyApp.csproj

# Submit APK/IPA to app store...

# Later, push a patch (checks dependency compatibility)
codepush patch --release 1.0.0

# Or deploy directly to a connected device for testing
codepush release MyApp.Feature.csproj --local --restart
```

## Features

- **Android + iOS** support out of the box
- **Release/Patch model** — like Shorebird: create releases, push compatible patches
- **Dependency compatibility check** — prevents broken patches via assembly reference analysis
- **Zero config MSBuild integration** — automatically removes modules from the app package
- **3-tier assembly resolution** — pending updates > persisted > embedded fallback
- **SHA-256 hash verification** for download integrity
- **Git integration** — automatic tags for releases (`v1.0.0`) and patches (`patch-v1.0.0-1`)
- **Rollback** to any previous version or the original embedded module
- **CLI tool** — `dotnet-codepush` with login, apps, release, patch, devices, rollback
- **iOS hybrid AOT + Interpreter** — main app runs full AOT, only CodePush modules use the Mono interpreter (no performance penalty for the host app)

## How iOS Works

Apple restricts loading native code at runtime, but the Mono interpreter — which ships inside your app binary — can execute IL bytecode dynamically. CodePush configures `MtouchInterpreter` to enable interpretation **only for your modules**, keeping the rest of your app at full AOT performance.

This is the same approach used by React Native CodePush and Shorebird — interpreting managed bytecode through a built-in interpreter that ships with the app.

## Architecture

```
┌─────────────────────────────────────────────┐
│                Your MAUI App                │
│                                             │
│  MauiProgram.cs                             │
│    └── UseCodePush(options => {             │
│            options.AddModule("Feature");    │
│        })                                   │
│                                             │
│  AppShell.xaml                              │
│    └── References Feature.MainPage          │
│         (loaded dynamically by CodePush)    │
│                                             │
├─────────────────────────────────────────────┤
│             Maui.CodePush Library           │
│                                             │
│  Build Time:                                │
│    MSBuild targets remove Feature DLL       │
│    from assembly store, embed as resource   │
│                                             │
│  Runtime:                                   │
│    AssemblyResolve intercepts loading       │
│    → Check temp/ (pending updates)          │
│    → Check base/ (persisted version)        │
│    → Extract from embedded (first launch)   │
│    → Assembly.LoadFrom(path)                │
│                                             │
├─────────────────────────────────────────────┤
│           CodePush Server (Phase 2)         │
│    Release management, CDN, analytics       │
└─────────────────────────────────────────────┘
```

## Self-hosted Server

Host your own CodePush server with Docker. Works on **Linux**, **macOS**, and **Windows**.

### Quick Install (Linux / macOS)

```bash
curl -fsSL https://raw.githubusercontent.com/felipebaltazar/Maui.CodePush/main/scripts/install-server.sh | bash
```

### Quick Install (Windows PowerShell)

```powershell
irm https://raw.githubusercontent.com/felipebaltazar/Maui.CodePush/main/scripts/install-server.ps1 | iex
```

### What the installer does

1. Checks that Docker is installed
2. Asks for install directory, MongoDB connection, and server port
3. Generates a secure JWT secret
4. Optionally adds a local MongoDB container (if using localhost)
5. Creates `.env` and `docker-compose.yml`
6. Pulls the Docker image from `ghcr.io/felipebaltazar/codepush-server:latest`
7. Starts the server and verifies it's running

### Manual Install

If you prefer to set up manually:

```bash
mkdir codepush && cd codepush

# Create .env
cat > .env <<EOF
MONGODB_CONNECTION_STRING=mongodb://localhost:27017
MONGODB_DATABASE_NAME=codepush
CODEPUSH_JWT_SECRET=$(openssl rand -base64 48)
CODEPUSH_PORT=8080
EOF

# Create docker-compose.yml
cat > docker-compose.yml <<'EOF'
services:
  codepush-server:
    image: ghcr.io/felipebaltazar/codepush-server:latest
    container_name: codepush-server
    restart: unless-stopped
    ports:
      - "${CODEPUSH_PORT:-8080}:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - MONGODB_CONNECTION_STRING=${MONGODB_CONNECTION_STRING}
      - MONGODB_DATABASE_NAME=${MONGODB_DATABASE_NAME:-codepush}
      - CODEPUSH_JWT_SECRET=${CODEPUSH_JWT_SECRET}
    volumes:
      - codepush-uploads:/app/uploads

  mongo:
    image: mongo:7
    container_name: codepush-mongo
    restart: unless-stopped
    volumes:
      - codepush-mongo-data:/data/db

volumes:
  codepush-uploads:
  codepush-mongo-data:
EOF

# Start
docker compose up -d
```

### After installation

```bash
# Install the CLI
dotnet tool install -g dotnet-codepush --prerelease

# Register and login
codepush login --server http://YOUR_SERVER:8080 \
  --email you@email.com --password yourpass --register --name "Your Name"

# Create your app
codepush apps add --package-name com.yourapp --name "My App" --set-default
```

### Managing the server

```bash
cd /opt/codepush  # or your install directory

docker compose up -d      # Start
docker compose down        # Stop
docker compose pull        # Update to latest version
docker compose up -d       # Restart with new version
docker logs -f codepush-server  # View logs
```

### Environment variables

| Variable | Required | Description |
|----------|----------|-------------|
| `MONGODB_CONNECTION_STRING` | Yes | MongoDB connection string |
| `MONGODB_DATABASE_NAME` | No | Database name (default: `codepush`) |
| `CODEPUSH_JWT_SECRET` | Yes | Secret key for JWT tokens (min 32 chars) |
| `CODEPUSH_PORT` | No | Server port (default: `8080`) |

---

## Roadmap

| Phase | Status | Description |
|-------|--------|-------------|
| **1 - Core Engine** | **Done** | Assembly loading, MSBuild targets, version tracking. Validated on Android device. |
| **2 - Server + CLI** | **Done** | REST API (MongoDB), CLI tool, release/patch model with dependency checks, git tags |
| **3 - Production** | Planned | Code signing (RSA-2048), differential updates (bsdiff), CDN, staged rollouts, analytics |

## App Store Compliance

- **Apple iOS** (Section 3.3.2): The Mono interpreter is part of the app binary. Interpreting IL bytecode is analogous to JavaScript in WebKit — this approach is used by React Native CodePush and Shorebird
- **Google Play**: .NET assemblies run on the Mono runtime VM, which qualifies under Google's policy for virtual machine execution

CodePush is designed for **hotfixes and minor updates**, not for circumventing app store review for major features.

## Requirements

- .NET 9.0+
- .NET MAUI workloads for Android and/or iOS
- For Android device testing: `EmbedAssembliesIntoApk=true` in debug builds

## Contributing

See [CLAUDE.md](CLAUDE.md) for architecture documentation and [docs/PLAN.md](docs/PLAN.md) for the implementation plan and technical decisions history.

## License

**Monkseal CodePush License** — See [LICENSE](LICENSE) for full terms.

- **Non-commercial use**: Free, with attribution
- **Open source projects**: Free, with attribution and no competing products
- **Commercial use**: Requires an active paid subscription from Monkseal
- **Restrictions**: Cannot create competing OTA update products or SaaS offerings

Copyright (c) 2026 Monkseal. All rights reserved.
