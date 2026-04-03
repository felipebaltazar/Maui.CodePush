# CodePush Documentation

Welcome to the CodePush documentation — the OTA update system for .NET MAUI by [Monkseal](https://monkseal.dev).

Ship hotfixes to your Android and iOS apps instantly, without waiting for app store review.

---

## What is CodePush?

CodePush lets you push code changes directly to your users' devices. When you fix a bug or tweak your UI, your users get the update on their next app launch — no reinstall, no store review.

It works by loading feature modules dynamically at runtime via `Assembly.LoadFrom`. At build time, CodePush removes these modules from the app package and embeds them as extractable resources. Updates replace these modules with new versions downloaded from the server.

## How it works

```
  You                          Your Users
  ───                          ──────────
  1. codepush patch             3. App checks for updates
     --release 1.0.0            4. Downloads new module
                                5. Applies on next restart
  2. Patch published ─────>     6. Users see the fix ✔
```

## Quick links

| Topic | Description |
|-------|-------------|
| [Getting Started](getting-started.md) | Install, register, and push your first update in 5 minutes |
| [CLI Reference](cli-reference.md) | All commands, options, and examples |
| [App Configuration](configuration.md) | Set up your .NET MAUI project for CodePush |
| [Releases & Patches](releases.md) | How the release/patch model works |
| [Self-hosted Server](self-hosted.md) | Host your own CodePush server |
| [Pricing](pricing.md) | Plans, limits, and billing |
| [Security](security.md) | Authentication, signing, and app store compliance |
| [FAQ](faq.md) | Common questions and troubleshooting |

## Supported Platforms

| Platform | Status |
|----------|--------|
| Android | ✔ Fully supported |
| iOS | ✔ Supported (hybrid AOT + interpreter) |

## Requirements

- .NET 9.0+
- .NET MAUI workloads
- A Monkseal account ([register here](https://monkseal.dev/register))
