# CLI Reference

The CodePush CLI (`codepush`) manages your releases, patches, and deployments.

Install: `dotnet tool install -g dotnet-codepush --prerelease`

---

## Authentication

### `codepush register`

Opens [monkseal.dev/register](https://monkseal.dev/register) to create an account.

### `codepush login`

Authenticates via browser. Opens a verification page, you log in, and the CLI receives credentials automatically.

```bash
codepush login                    # uses built-in server URL
codepush login --server https://my-server.com  # self-hosted
```

| Option | Description |
|--------|-------------|
| `--server`, `-s` | Server URL (optional, uses built-in default) |

---

## Project Setup

### `codepush init`

Creates a `.codepush.json` configuration file by auto-detecting your project.

```bash
codepush init
codepush init --package-name com.myapp --force
```

| Option | Description |
|--------|-------------|
| `--package-name`, `-p` | Android/iOS application ID |
| `--platform` | Target platform (default: `android`) |
| `--force` | Overwrite existing config |

---

## App Management

### `codepush apps list`

Lists all apps registered to your account.

### `codepush apps add`

Registers a new app on the server.

```bash
codepush apps add --package-name com.myapp --name "My App" --set-default
```

| Option | Description |
|--------|-------------|
| `--package-name`, `-p` | Android/iOS package name (must be unique) |
| `--name`, `-n` | Display name |
| `--set-default` | Save as default appId in `.codepush.json` |

---

## Releases

### `codepush release create`

Creates a new release — the version you submit to the app store. Captures dependency snapshots for compatibility checking.

```bash
codepush release create --version 1.0.0
codepush release create --version 1.0.0 --app-project MyApp.csproj
codepush release create --version 1.0.0 --dotnet-args "/p:AndroidSigningKeyPass=secret"
```

| Option | Description |
|--------|-------------|
| `--version`, `-v` | Release version (required) |
| `--app-project` | App .csproj path for `dotnet publish` |
| `--platform` | Target platform |
| `--channel` | Release channel (default: `production`) |
| `--configuration`, `-c` | Build configuration (default: `Release`) |
| `--dotnet-args` | Extra arguments for `dotnet publish/build` |
| `--no-git-tag` | Skip git tag creation |

### `codepush release list`

Lists all releases for the current app.

### `codepush release [paths]`

Quick deploy via server or adb (legacy command, use `patch` for production).

```bash
codepush release Feature.csproj --local --restart  # deploy via adb
```

| Option | Description |
|--------|-------------|
| `--local` | Deploy via adb instead of server |
| `--device`, `-d` | Target device serial |
| `--restart` | Restart the app after deployment |
| `--output`, `-o` | Output directory instead of deploying |
| `--no-build` | Skip build |

---

## Patches

### `codepush patch`

Creates a code push patch for an existing release. Checks dependency compatibility before uploading.

```bash
codepush patch --release 1.0.0
codepush patch --release 1.0.0 Feature/Feature.csproj
codepush patch --release 1.0.0 --mandatory
codepush patch --release 1.0.0 --dotnet-args "/p:TrimMode=link"
```

| Option | Description |
|--------|-------------|
| `--release`, `-r` | Target release version (required) |
| `--platform` | Target platform |
| `--channel` | Release channel (default: `production`) |
| `--configuration`, `-c` | Build configuration (default: `Release`) |
| `--mandatory` | Mark as mandatory update |
| `--dotnet-args` | Extra arguments for `dotnet build` |
| `--no-build` | Skip build |
| `--no-git-tag` | Skip git tag creation |

If dependencies are incompatible, the CLI shows a detailed error:

```
✖ Dependency compatibility check failed

  Release 1.0.0 vs patch:
    ✖ NEW: CommunityToolkit.Maui v9.1.0 (not in release)
    ✖ CHANGED: Microsoft.Maui.Controls 9.0.0 -> 9.0.1 (version increased)

  Create a new release with: codepush release create
```

---

## Device Management

### `codepush devices`

Lists connected Android devices.

### `codepush rollback`

Removes deployed updates from a device (reverts to embedded version).

```bash
codepush rollback --all --restart
codepush rollback MyApp.Feature --restart
```

| Option | Description |
|--------|-------------|
| `--all` | Rollback all modules |
| `--device`, `-d` | Target device serial |
| `--restart` | Restart the app after rollback |

---

## Maintenance

### `codepush update`

Updates the CLI to the latest version.

```bash
codepush update          # stable version
codepush update --pre    # include pre-releases
```

---

## Configuration File

The `.codepush.json` file stores project settings. Created by `codepush init` and updated by `codepush login`.

```json
{
  "packageName": "com.myapp",
  "platform": "android",
  "modules": [
    {
      "name": "MyApp.Feature",
      "projectPath": "../MyApp.Feature/MyApp.Feature.csproj"
    }
  ],
  "serverUrl": "https://codepush.monkseal.dev",
  "appId": "guid",
  "token": "jwt-token",
  "apiKey": "hex-key"
}
```

> **Note:** `.codepush.json` contains credentials and should be added to `.gitignore`.

---

## Git Tags

CodePush automatically creates and pushes git tags:

| Action | Tag format | Example |
|--------|-----------|---------|
| Release | `v{version}` | `v1.0.0` |
| Patch | `patch-v{release}-{number}` | `patch-v1.0.0-1` |

Use `--no-git-tag` to disable.
