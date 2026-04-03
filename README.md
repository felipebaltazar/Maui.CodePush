<p align="center">
  <img src="icon.png" alt="Monkseal CodePush" width="120" />
</p>

<h1 align="center">CodePush for .NET MAUI</h1>

<p align="center">
  <strong>Ship hotfixes to your .NET MAUI apps instantly. No app store review. No waiting.</strong>
  <br/>
  <em>A <a href="https://monkseal.dev">Monkseal</a> product</em>
</p>

<p align="center">
  <a href="docs/getting-started.md">Getting Started</a> •
  <a href="docs/cli-reference.md">CLI Reference</a> •
  <a href="docs/pricing.md">Pricing</a> •
  <a href="docs/README.md">Full Documentation</a>
</p>

---

CodePush is an over-the-air (OTA) update system for .NET MAUI, bringing the same capabilities that [Shorebird](https://shorebird.dev/) offers for Flutter and [CodePush](https://microsoft.github.io/code-push/) offered for React Native — now for the .NET ecosystem.

> Push bug fixes, UI changes, and minor updates directly to your users' devices on **Android** and **iOS**, without going through app store review cycles.

## Quick Start

```bash
# Install
dotnet add package CodePush.Maui --prerelease
dotnet tool install -g dotnet-codepush --prerelease

# Register & login
codepush register     # opens monkseal.dev in browser
codepush login        # authenticates via browser

# Create release & push a patch
codepush release create --version 1.0.0
codepush patch --release 1.0.0
```

See the full [Getting Started guide](docs/getting-started.md) for step-by-step setup.

## Features

- **Android + iOS** support
- **Release/Patch model** — create releases, push compatible patches (like Shorebird)
- **Dependency compatibility check** — prevents broken patches via assembly reference analysis
- **Zero config MSBuild integration** — automatically removes modules from the app package
- **SHA-256 hash verification** for download integrity
- **Git integration** — automatic tags for releases and patches
- **Browser-based login** — no passwords in the terminal
- **Self-hosted option** — [run your own server](docs/self-hosted.md) with Docker

## Documentation

| | |
|---|---|
| 📖 [Getting Started](docs/getting-started.md) | Install, register, and push your first update |
| 💻 [CLI Reference](docs/cli-reference.md) | All commands, options, and examples |
| ⚙️ [App Configuration](docs/configuration.md) | Set up your .NET MAUI project |
| 🔄 [Releases & Patches](docs/releases.md) | How the release/patch model works |
| 🖥️ [Self-hosted Server](docs/self-hosted.md) | Host your own CodePush server |
| 💰 [Pricing](docs/pricing.md) | Plans and limits |
| 🔒 [Security](docs/security.md) | Authentication and app store compliance |
| ❓ [FAQ](docs/faq.md) | Common questions and troubleshooting |

## Requirements

- .NET 9.0+
- .NET MAUI workloads
- A [Monkseal account](https://monkseal.dev/register)

## Contributing

See [CLAUDE.md](CLAUDE.md) for architecture documentation and [docs/PLAN.md](docs/PLAN.md) for technical decisions.

## License

**Monkseal CodePush License** — See [LICENSE](LICENSE).

Non-commercial and open source use is free with attribution. Commercial use requires a [paid plan](docs/pricing.md).

Copyright (c) 2026 Monkseal. All rights reserved.
