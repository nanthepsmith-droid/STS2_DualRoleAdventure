# DualRoleAdventure (LocalMultiControl)

English | [简体中文](README.zh-CN.md)

A **Slay the Spire 2** mod that turns the official online multiplayer into a *local* multi-character experience: one player controls **2–12 characters** on a single machine (duplicate characters allowed), switching between them at any time, while the game still runs the real multiplayer flow underneath — no networking involved.

> **Maintained fork.** The original mod was created by [liwenhao0427](https://github.com/liwenhao0427) and discontinued at v1.30 (June 2026). This repository continues maintenance and distribution as an independent community continuation ([original Workshop item](https://steamcommunity.com/sharedfiles/filedetails/?id=3747538947)). Thank you for building this, 磁石战士Ω!

## Features

- Local multiplayer party of 2–12 characters, started from the normal multiplayer menu (`Multiplayer → Host → Local Multi-Control`)
- Instant character switching in and out of combat: `Tab` / `Shift+Tab` (legacy `[` `]` `R` `T` `/` still work)
- Per-character everything: decks, energy, gold, potions, relics, event choices, reward claims
- Full run flow: lobby → combat → rewards → map → events → shops → rest sites → treasure → next act → save/continue
- **Vakuu (AI auto-play)**: hand any character over to the built-in autoplayer, per character or all at once
- Optional **ghost hands** overlay (`F8`): see your other characters' hands behind your own, position adjustable at runtime (`Ctrl+Arrows`)
- Pure code mod: `has_dll=true`, `has_pck=false` — no asset pack required

See the **[Player Guide](PLAYER_GUIDE.md)** for installation, controls, and gameplay details.

## Compatibility

- Currently built against game version **v0.111.0** (August 2026). When the game patches, expect a short turnaround for a compatibility release — that is this fork's primary job.
- **Oddmelt**: compatible since v1.33 — a built-in guard skips Oddmelt's hidden Gauge input cards during combat UI rebuilds (previously these broke character switching); no separate fix mod is needed.

## Installation

**Steam Workshop:** watch the original author's Workshop item ([3747538947](https://steamcommunity.com/sharedfiles/filedetails/?id=3747538947)) — the original author may resume updating it when the game reaches its full release.

**Manual (this fork's builds):** download `DualRoleAdventure.dll` + `DualRoleAdventure.json` from [Releases](https://github.com/nanthepsmith-droid/STS2_DualRoleAdventure/releases) and place both in:

```
<Slay the Spire 2 install>\mods\DualRoleAdventure\
```

## Building from source

Requirements: .NET SDK 9, a Slay the Spire 2 install.

1. Point `<Sts2Dir>` in `LocalMultiControl.csproj` at your game install (it is OS-conditional: a Windows path and a WSL `/mnt/c/...` path are both preconfigured — edit yours).
2. Build:

```bash
dotnet restore LocalMultiControl.csproj
dotnet build LocalMultiControl.csproj -c Release
dotnet format LocalMultiControl.csproj --verify-no-changes   # style gate
```

3. The build copies `DualRoleAdventure.dll` to the repo root. Deploy it plus `DualRoleAdventure.json` to the game's `mods/DualRoleAdventure/` folder (`copy_pck_to_game.ps1` does this — adjust its target path to your install).

For game-API reference during development, decompile `sts2.dll` into `src/` (gitignored, read-only):

```bash
dotnet tool install -g ilspycmd --version 9.1.0.7988
ilspycmd -p --nested-directories -o /tmp/sts2-src "<game>/data_sts2_windows_x86_64/sts2.dll"
cp -r /tmp/sts2-src/MegaCrit/Sts2/. src/
```

## Reporting issues

Please open a [GitHub issue](https://github.com/nanthepsmith-droid/STS2_DualRoleAdventure/issues) and include: act number, screen/room, exact steps, and if possible the log file at `%APPDATA%\SlayTheSpire2\logs\godot.log` (mod entries are prefixed `[LocalMultiControl]`).

## Documentation

- [Player Guide](PLAYER_GUIDE.md) — install, controls, gameplay
- [CHANGELOG](CHANGELOG.md) — release history
- [TODO](TODO.md) — known issues under investigation
- [docs/architecture.md](docs/architecture.md) — how the mod works internally
- [docs/console-commands.md](docs/console-commands.md) — dev-console commands for testing
- [docs/design/](docs/design/) — original design documents (translated)
- [docs/archive/](docs/archive/) — original Chinese documents preserved as-is

## Credits & license

- Original author: **liwenhao0427 (磁石战士Ω)** — design and the entire v0.1–v1.30 implementation. If this mod helps you, consider buying them a coffee:

  <img src="donate-original-author.jpeg" alt="Donate to the original author" width="200" />

- Maintainer (v1.31): [GuyGinat](https://github.com/GuyGinat) — first community-maintained release, English docs, Workshop item 3772900244
- Maintainer (v1.32+): [nanthepsmith-droid](https://github.com/nanthepsmith-droid) — game v0.110/0.111 adaptations, combat card-selection serialization & foreground fixes

### AI-assisted development

The v1.32+ maintenance is developed **with heavy AI assistance** (AI coding agents) under human direction: every change is reviewed, playtested and released by the human maintainer. The collaboration rules given to the agents live in [`AGENTS.md`](AGENTS.md); the v1.32 analysis that drove this release is documented in [`docs/维护现状分析.md`](docs/维护现状分析.md).

There is no formal open-source license yet. Until a LICENSE file lands, treat the source as *source-available for personal use* — ask before redistributing derivatives.
