# Steam Workshop Upload Workspace

Working folder for publishing this mod to the Steam Workshop (appid `2868840`).

> **Note (2026-08-21, current maintainer):** There are no plans to publish new versions to the Steam Workshop; distribution happens via [GitHub Releases](https://github.com/nanthepsmith-droid/STS2_DualRoleAdventure/releases). This folder is kept as-is for reference from the previous maintainer (GuyGinat, item 3772900244). Any "written permission" wording that appeared in these files referred to the previous maintainer's claim and is not asserted by the current maintainer.

## Layout

- `content/` — the files actually uploaded: `DualRoleAdventure.dll` + `DualRoleAdventure.json`. Overwrite with the latest release build before each upload.
- `preview.jpg` — the item's preview image.
- `steamcmd_item_fork.vdf` — upload config for **this fork's** Workshop item (`publishedfileid` 3772900244, first created by GuyGinat). After each upload SteamCMD writes the assigned `publishedfileid` back into this file — commit that change.
- `steamcmd_item.vdf` / `mod_id.txt` / `workshop.json` / `image.png` — the **original author's** upload workspace for item [3747538947](https://steamcommunity.com/sharedfiles/filedetails/?id=3747538947), kept for reference. Never upload with the original vdf.

## Upload procedure (maintainer)

1. Follow the release flow in `AGENTS.md` §7 (version bump, changelog, Release build, restage `content/`).
2. Update `changenote` in `steamcmd_item_fork.vdf`.
3. Run (Steam Guard prompt on first login):

```
steamcmd +login <steam_username> +workshop_build_item "<absolute path>\workshop\steamcmd_item_fork.vdf" +quit
```

4. First upload only: the vdf ships with `visibility "2"` (private) — verify the item page, accept the Workshop agreement if prompted, subscribe to confirm the game loads it, then set visibility to Public on the item page and change the vdf to `"0"` for future updates.
