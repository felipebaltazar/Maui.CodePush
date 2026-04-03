# Releases & Patches

CodePush uses a two-tier model inspired by [Shorebird](https://shorebird.dev): **releases** represent app store versions, and **patches** are OTA updates tied to a specific release.

---

## Concepts

### Release

A **release** is the version of your app submitted to the App Store or Google Play. When you create a release, CodePush captures:

- All module DLLs with their SHA-256 hashes
- A **dependency snapshot**: every assembly reference from each module (name + version)
- The target platform (Android/iOS)
- A git tag (`v1.0.0`)

Releases are created once per app store submission.

### Patch

A **patch** is a code push update applied on top of a release. It replaces one or more module DLLs with new versions. Patches:

- Are tied to a specific release version
- Are numbered sequentially (patch 1, 2, 3...)
- Only the latest active patch is delivered to users
- Must pass a **dependency compatibility check** before being accepted
- Create a git tag (`patch-v1.0.0-1`)

## Workflow

```
codepush release create --version 1.0.0
    │
    │  Submit to app store
    │
    ├── codepush patch --release 1.0.0    → patch #1
    ├── codepush patch --release 1.0.0    → patch #2 (replaces #1)
    └── codepush patch --release 1.0.0    → patch #3 (replaces #2)
    
codepush release create --version 1.1.0
    │
    │  Submit to app store
    │
    ├── codepush patch --release 1.1.0    → patch #1
    └── codepush patch --release 1.1.0    → patch #2
```

Users on version 1.0.0 only receive patches for 1.0.0. Users on 1.1.0 only receive patches for 1.1.0.

## Dependency Compatibility

When you create a patch, CodePush compares the new module's assembly references against the release snapshot. A patch is **rejected** if:

| Condition | Reason |
|-----------|--------|
| New assembly added | Not present in the app store binary |
| Assembly version increased | May require APIs not available |

A patch is **accepted** if all references are the same or lower version than the release.

### Example: compatible patch

```
Release 1.0.0:
  Microsoft.Maui.Controls 9.0.0
  MyLibrary 2.0.0

Patch:
  Microsoft.Maui.Controls 9.0.0  ✔ same version
  MyLibrary 2.0.0                ✔ same version
```

### Example: incompatible patch

```
Release 1.0.0:
  Microsoft.Maui.Controls 9.0.0

Patch:
  Microsoft.Maui.Controls 9.0.1  ✖ version increased
  CommunityToolkit.Maui 9.0.0    ✖ new assembly (not in release)
```

To fix: create a new release (`codepush release create --version 1.1.0`) that includes the updated dependencies.

## How the app receives updates

1. App starts with `ReleaseVersion = "1.0.0"` configured
2. `CheckUpdatesAsync()` calls the server: "I'm on release 1.0.0, any patches?"
3. Server returns the latest active patch for each module
4. App downloads the DLL, verifies SHA-256 hash
5. DLL saved to staging folder
6. On next cold start, `AssemblyResolve` loads the new DLL

### Resolution priority

When loading a module, CodePush checks in order:

1. **Staging folder** (pending patch) → moves to persistent folder
2. **Persistent folder** (current active version)
3. **Embedded resource** (original from app package, fallback)

## Rollback

### Server-side

Push a new patch that reverts the change. The old patch is deactivated.

### Client-side

```csharp
CodePush.Rollback();                    // all modules
CodePush.Rollback("MyApp.Feature");     // specific module
```

### Via CLI (local device)

```bash
codepush rollback --all --restart
```

## Channels

You can target different groups of users using channels:

```bash
codepush release create --version 1.0.0 --channel staging
codepush patch --release 1.0.0 --channel staging
```

```csharp
options.Channel = "staging";  // in MauiProgram.cs
```

Default channel is `production`.

## Git Tags

CodePush creates git tags automatically:

| Action | Tag | Example |
|--------|-----|---------|
| Release | `v{version}` | `v1.0.0` |
| Patch | `patch-v{release}-{number}` | `patch-v1.0.0-3` |

Use `--no-git-tag` to skip.
