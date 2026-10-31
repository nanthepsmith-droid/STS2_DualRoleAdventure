# DualRoleAdventure (LocalMultiControl)

English | [简体中文](README.zh-CN.md)

A **Slay the Spire 2** mod that turns the official online multiplayer into a *local* multi-character experience: one player controls **2–12 characters** on a single machine (duplicate characters allowed), switching between them at any time, while the game still runs the real multiplayer flow underneath — no networking involved.

> **Maintained fork.** The original mod was created by [liwenhao0427](https://github.com/liwenhao0427) and discontinued at v1.30 (June 2026). This repository continues maintenance and distribution as an independent community continuation ([original Workshop item](https://steamcommunity.com/sharedfiles/filedetails/?id=3747538947)). Thank you for building this, 磁石战士Ω — and [GuyGinat](https://github.com/GuyGinat) for the first community continuation!

## Features

- Local multiplayer party of 2–12 characters, started from the normal multiplayer menu (`Multiplayer → Host → Local Multi-Control`)
- **Local multi-character Daily Climb** (v1.43): via the "Local Daily Run" button below the multiplayer card row — up to 4 seats, characters assigned from the date, never uploads to the Daily leaderboard
- **Co-op Bots compatibility** (v1.43, optional): coexists with the online-AI-teammates mod; choose which seats the online bots take
- Instant character switching in and out of combat: `Tab` / `Shift+Tab` (legacy `[` `]` `R` `T` `/` still work)
- Per-character everything: decks, energy, gold, potions, relics, event choices, reward claims
- Full run flow: lobby → combat → rewards → map → events → shops → rest sites → treasure → next act → save/continue
- **Vakuu (AI auto-play)**: hand any character over to the built-in autoplayer, per character or all at once
- Optional **ghost hands** overlay (`F8`): see your other characters' hands behind your own, position adjustable at runtime (`Ctrl+Arrows`)
- Pure code mod: `has_dll=true`, `has_pck=false` — no asset pack required

See the **[Player Guide](PLAYER_GUIDE.md)** ([简体中文](PLAYER_GUIDE.zh-CN.md)) for installation, controls, and gameplay details.

## Compatibility

- Currently built against game version **v0.111.0** (August 2026). When the game patches, expect a short turnaround for a compatibility release — that is this fork's primary job.
- **Oddmelt**: compatible since v1.33 — a built-in guard skips Oddmelt's hidden Gauge input cards during combat UI rebuilds (previously these broke character switching); no separate fix mod is needed.

### ⚠ Performance: with "content-heavy" third-party mods, **do not run large battles**

Local multi-player cost scales with the number of seats: every seat has its own cards, relics and powers, and
almost every combat hook (keywords, energy, damage, card picks, …) walks over **all hook listeners of the whole
fight**. Some third-party mods do per-card / per-relic / per-power work inside that chain, so the cost grows
multiplicatively with seats.

> **Measured** (12 seats, same machine, same fight, before/after; YUI series enabled in both runs): with
> **figure_Saya / Koishi / newsanguo** disabled **41–60 fps**; with all three enabled most windows sit at
> **10–20 fps** (an easy window can reach 35).

**Play these mods with few seats only** (examples, not an exhaustive list):

| mod | what it adds per combat |
|---|---|
| **YUI series** (`YUICore` / `YUISpireExpansion`, …) | two `AsyncLocal` writes per relic on **every** hook dispatch (12 seats ≈ 120 relics) |
| **figure_Saya** (沙耶之歌) | a combat hook that filters every listener for "suppressed visible debuffs" |
| **Koishi** (古明地恋) | a combat hook that checks `SealAll` for every relic |
| **newsanguo** (新三国) | a combat hook that checks every power for "Hanzhong / Iron Cavalry" |
| **Hextech Runes** (`HextechRunes` / `HextechRunesSponsorPack`) | different mechanism: it pushes the whole-fight listener list past vanilla's pre-allocated capacity (`Players.Count * 50`, only 600 slots at 12 seats) ⇒ **a resize + array copy on every dispatch**, measured at **28–30%** of the main thread; **already fixed** by this repo's `ThirdPartyHotPathFix` v1.2.0, see below |

> **About Hextech Runes**: it is not a "walk the whole fight every frame" mod like the four above — runes are relics
> and its hook implementations return after one or two checks. But **runes push the listener count past vanilla's
> hard-coded initial capacity** (`new List<AbstractModel>(Players.Count * 50)`, only 600 slots at 12 seats), so every
> hook dispatch resizes the list: measured `List<>.set_Capacity` at **28.08% / 30.45%** of the main thread, versus
> **0.15% / 0.29%** with the same setup and Hextech disabled.
> We **fixed** that: `compat-mods/ThirdPartyHotPathFix` v1.2.0 raises that multiplier (50 → 100, override with
> `HOT_PATH_FIX_LISTENER_CAPACITY`) — allocation size only, no behaviour change. With it installed Hextech's own
> overhead is ~2%, so 12 seats should be usable again — **if your machine still struggles, drop to 4 seats as advised above**.
>
> This repo also ships a **flow** compat patch for Hextech, `HextechRunesLocalCoopFix` (same `compat-mods-*.zip`),
> which fixes "seat #2's rune screen cannot be opened ⇒ softlock" in local co-op. It has nothing to do with performance.

General criterion (not just the four above): **if a mod adds cards/relics/powers per seat, or patches
`CombatState.IterateHookListeners`-style methods that are called many times per frame and iterate the whole
fight**, frame rate will fall linearly (or worse) with seat count.

**Recommendation: with these mods installed, stay at 4 seats or fewer** (the vanilla multiplayer seat limit) and
avoid 5–12 seat runs. This is a property of those mods' implementations; this repo **cannot remove their content
cost** — `compat-mods/ThirdPartyHotPathFix` only strips part of their "idle" overhead when the mechanic is not
present, it cannot make the content itself cheaper.

## Installation

**Steam Workshop:** several separate "local multi-control" items exist, each on its own release cadence: the original author's item ([3747538947](https://steamcommunity.com/sharedfiles/filedetails/?id=3747538947)), [GuyGinat's community continuation](https://steamcommunity.com/sharedfiles/filedetails/?id=3772900244), and [the other authorized fork](https://steamcommunity.com/sharedfiles/filedetails/?id=3810171053) described in the section above. Note: **this repository's builds are distributed only via this repository's Releases** (they are not shipped through any Workshop item).

**Manual (this fork's builds):** download `DualRoleAdventure.dll` + `DualRoleAdventure.json` from [Releases](https://github.com/nanthepsmith-droid/STS2_DualRoleAdventure/releases) and place both in:

```
<Slay the Spire 2 install>\mods\DualRoleAdventure\
```

## See also: another **authorized** fork (Workshop)

**[本地多角色 · 全盛瓦库版 | Local Multi-Control · Prime Vakuu](https://steamcommunity.com/sharedfiles/filedetails/?id=3810171053)**

- That fork describes itself as "**based on liwenhao0427's (磁石战士Ω) Local Multi-Control and GuyGinat's community-maintained version — with authorization**". **The authorization is theirs** (granted by the original author and the previous maintainer) — which is exactly why we are glad to recommend it.
  ⚠ To be clear about the boundary: **this repository itself has no such authorization** — it is simply a community continuation line kept alive after the original author stopped at v1.30; the two continuations are independent of each other.
- Its focus differs from ours: it adds a good deal of enhancement around **Vakuu (AI auto-play)**. This repository's maintainer has played it and found it noticeably better in that area — worth a try.
- ⚠ It is published **on the Steam Workshop only (no GitHub repository)**. This fork's builds are still distributed **only via this repository's Releases**. The two are independent — pick either one, or try both (note: installing two "local multi-control" mods at once will conflict; disable one first).

## Building from source

Requirements: .NET SDK 9, a Slay the Spire 2 install.

1. Point `<Sts2Dir>` in `LocalMultiControl.csproj` at your game install. Resolution order is
   `-p:Sts2Dir=<path>` > env var `STS2_DIR` > the built-in default (Windows `D:/SteamLibrary/...`).
   On Linux / WSL pass it explicitly, e.g.
   `dotnet build LocalMultiControl.csproj -c Release -p:Sts2Dir=/mnt/d/SteamLibrary/"steamapps/common/Slay the Spire 2"`.
   Every path in the csproj uses forward slashes, so the same file works on both platforms
   (backslashes are not separators on Linux).
2. Build:

```bash
dotnet restore LocalMultiControl.csproj
dotnet build LocalMultiControl.csproj -c Release
dotnet format LocalMultiControl.csproj --verify-no-changes   # style gate
```

3. The build copies `DualRoleAdventure.dll` to the repo root. Copy **only that dll** into your mod slot under the
game's `mods/` folder; the dll filename must match the slot json `id`. For a fresh slot, `DualRoleAdventure.json`
works as-is; **never drop a second json with a different id into a slot that already has one** (the game would
load the mod twice).

For game-API reference during development, decompile `sts2.dll` into `src/` (gitignored, read-only):

```bash
dotnet tool install -g ilspycmd --version 9.1.0.7988
ilspycmd -p --nested-directories -o /tmp/sts2-src "<game>/data_sts2_windows_x86_64/sts2.dll"
cp -r /tmp/sts2-src/MegaCrit/Sts2/. src/
```

## Reporting issues

Please open a [GitHub issue](https://github.com/nanthepsmith-droid/STS2_DualRoleAdventure/issues) and include: act number, screen/room, exact steps, and if possible the log file at `%APPDATA%\SlayTheSpire2\logs\godot.log` (mod entries are prefixed `[LocalMultiControl]`).

## Documentation

- [Player Guide](PLAYER_GUIDE.md) ([简体中文](PLAYER_GUIDE.zh-CN.md)) — install, controls, gameplay
- [CHANGELOG](CHANGELOG.md) — release history
- [TODO](TODO.md) — known issues under investigation
- [docs/architecture.md](docs/architecture.md) — how the mod works internally
- [docs/console-commands.md](docs/console-commands.md) — dev-console commands for testing
- [docs/design/](docs/design/) — original design documents (translated)
- [docs/archive/](docs/archive/) — original Chinese documents preserved as-is

## Naming (Vakuu vs Wakuu)

The game spells this character **Vakuu** (see `Vakuu` / `VakuuCardSelector` in the game assembly). This mod's early development wrote **"Wakuu"**, and that misspelling is still baked into **internal C# identifiers and file names** (`LocalWakuuAutopilotConfig`, `WakuuConfigData`, …).

Status:

- **Player-visible text is correct.** All UI strings, this README, the Player Guide and the Workshop page use **Vakuu**.
- **Config keys were unified** (`vakuu_autopilot.json`): `wakuuBrain` / `wakuuViewMode` / `wakuuPlayQueue` / `wakuuPlayOverlap` / `fastWakuuPlay` / `keepWakuuFormRelic` were renamed to their `vakuu*` spelling, with a **backward-compatible migration** — an existing config using the old keys keeps working and is rewritten with the new keys on the next save.
- **Internal identifiers keep the old spelling on purpose.** Renaming ~95 source files brings no gameplay benefit but churns the whole codebase, so there is **no plan to fix them**. If you see `Wakuu` in logs, source or stack traces, read it as `Vakuu`.

In short: only the config keys were worth changing, and that is done; the rest is cosmetic and intentionally left alone.

## Credits & license

- Original author: **liwenhao0427 (磁石战士Ω)** — design and the entire v0.1–v1.30 implementation. If this mod helps you, consider buying them a coffee:

  <img src="donate-original-author.jpeg" alt="Donate to the original author" width="200" />

- Maintainer (v1.31): [GuyGinat](https://github.com/GuyGinat) — first community-maintained release, English docs, Workshop item 3772900244
- Maintainer (v1.32+): [nanthepsmith-droid](https://github.com/nanthepsmith-droid) — game v0.110/0.111 adaptations, combat card-selection serialization & foreground fixes

### AI-assisted development

The v1.32+ maintenance is developed **with heavy AI assistance** (AI coding agents) under human direction: every change is reviewed, playtested and released by the human maintainer. The collaboration rules given to the agents live in [`AGENTS.md`](AGENTS.md); the v1.32 analysis that drove this release is documented in [`../maintenance-docs/维护现状分析.md`](../maintenance-docs/维护现状分析.md) (maintenance docs live outside this repo).

There is no formal open-source license yet. Until a LICENSE file lands, treat the source as *source-available for personal use* — ask before redistributing derivatives.
