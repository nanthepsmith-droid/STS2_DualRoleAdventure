# Player Guide — DualRoleAdventure (LocalMultiControl)

Control 2–12 characters by yourself in Slay the Spire 2's multiplayer mode, on one machine, with no network.

> 中文版指南：[PLAYER_GUIDE.zh-CN.md](PLAYER_GUIDE.zh-CN.md)

## Install & enable

1. Subscribe on the Steam Workshop (or place `DualRoleAdventure.dll` + `DualRoleAdventure.json` in `<game>\mods\DualRoleAdventure\`).
2. Launch the game → `Settings → Mods` → enable **多角色冒险 (DualRoleAdventure)**. Restart if prompted.

## Starting a run

1. Main menu → **Multiplayer → Host**.
2. Pick the **Local Multi-Control (单人多角色)** card (it sits next to Standard / Daily / Custom).
3. In character select:
   - **`+` / `-`** — add/remove local characters (2–12; duplicates allowed)
   - **`Tab` / `Shift+Tab`** — switch which character you're editing
   - Pick a character and ready-up for each slot, then start as usual.
4. **Custom mode** also works: entering `Custom Mode` from the multiplayer menu keeps the local multi-control flow, so you can use seeds and custom rules with multiple characters.

## Vakuu (AI auto-play)

Any character can be handed to the built-in autoplayer ("Vakuu"):

- On the character-select screen: toggle per character, or toggle **all** at once.
- Controller: `Y` toggles the highlighted character, `LT + Y` toggles everyone.
- Vakuu characters play their turns automatically; when no Vakuu character can act, control returns to you.

### Vakuu Form (new in v1.36, off by default)

Optional upgraded hosting mode. When enabled, checked characters receive a separate relic
【瓦库形态 / Vakuu Form】("Letting Vakuu play counts as you winning") instead of the
permanent earring, and Vakuu plays **in the background** — the game no longer switches
to them, and they play their entire hand every turn.

Configure via `%APPDATA%\SlayTheSpire2\vakuu_autopilot.json`
(reloaded at the start of each run), or in game via **Settings → General → 瓦库托管**
(a native-looking submenu; changes apply immediately):

| Key | Default | Effect |
|---|---|---|
| `useVakuuForm` | `false` | Master switch. `false` = classic permanent-earring behavior |
| `playAllCards` | `true` | Play the whole hand (hard cap 60 cards as a safety fuse) |
| `backgroundMode` | `true` | Never switch foreground to Vakuu; a safety net hands control back if a dialog stalls >12s (combat) / >8s (event) |
| `suppressVanillaEarring` | `true` | Suppress the vanilla Whispering Earring auto-play hook for Form holders (+1 energy kept) |
| `autoClaimCards` | `true` | Auto-claim Vakuu's post-combat card rewards (leftmost), gold and relics |
| `autoClaimGoldRelics` | `true` | Auto-claim gold & relic rewards |
| `autoClaimPotions` | `true` | Auto-claim potion rewards. If the belt is full: drink a Blood Potion first to free a slot; otherwise take the reward only if its rarity beats the lowest potion on the belt (discarding it) |
| `autoChooseEvents` | `true` | Auto-pick options for non-shared events (first/last/random via `eventChoiceMode`). Lethal options are rejected; combat/minigame/unknown situations stop and wait for you |
| `eventChoiceMode` | `first` | Event option strategy: `first` / `last` / `random` |
| `cardPickMode` | `last` | In-combat effect card picks (synthesis, choose-N, …): `first` / `last` / `random`. Card rewards always claim leftmost |
| `autoRestChoice` | `true` | Auto-pick rest sites: low HP → rest; relic options → random among non-rest; otherwise smith the last non-Strike/Defend upgradable card (all done → rest; not full HP and nothing to smith → heal ally); tents pick everything |
| `neowAutoChoose` | `false` | Also auto-pick Neow bonuses |
| `autoUsePotions` | `false` | Auto-use potions in combat by a per-potion rule table: heals at <50% HP, Fruit Juice on pickup, buffs/debuffs/card potions on round 1 of Elite/Boss fights, defensive potions before end turn when enemy intent damage is high, character-specific potions thrown to the matching teammate, Duplicator/Gigantification to the human player first, targeted picks for Ashwater/Gambler's Brew/etc., Foul Potion only thrown at merchants for gold, mod potions consumed at a random round in normal fights |

### What Vakuu handles automatically (v1.37)

With the switches above on, a backgrounded Vakuu also: claims their combat/event rewards,
resolves non-shared events, picks rest-site options, uses potions per the rule table, and
throws Foul Potions at merchants. Everything else (shops, shared events, crystal sphere)
stays manual.

## During a run

- **Switch characters:** `Tab` (next) / `Shift+Tab` (previous). Legacy keys `]` `R` `/` (next) and `[` `T` (previous) still work.
- Each character owns their deck, hand, energy, gold, potions, relics, and choices. The UI (hand, energy, potion bar, status strip) follows whoever you control.
- **Combat:** play each character's turn, switching freely; end turn per character.
- **Rewards:** loot is generated per character and shown as one combined list, each entry prefixed with its owner (e.g. `[Player 2]`). Claim with the matching character.
- **Events:** by default each character resolves the event independently — the mod walks you through them one by one. Shared-event votes are auto-completed where the game requires everyone to vote.
- **Crystal Sphere:** each character pays for and divines on their own turn; revealed rewards (and the Payment Plan curse) belong only to the revealing character. Character switching is locked while a divination is in progress — finish your divinations first, the mod switches automatically.
- **Rest sites:** each character chooses in sequence (rest, upgrade, etc.).
- **Shops:** purchases and card removal are billed to the character currently in control. The Fake Merchant event (商人？？？) works the same way — each character browses and buys from their own stock with their own gold, and either character can throw the Foul Potion (浑浊药水) at the merchant to start the fight.
- **Map:** picking the next node auto-completes the "everyone must vote" step.
- **Save & continue:** quit normally; `Multiplayer → Load` resumes the run and auto-readies all local characters.

## Ghost hands overlay (optional)

Shows your backgrounded characters' hands behind and above your active hand, so you can plan across the whole team:

- **`F8`** — toggle on/off (off by default; remembered between sessions)
- **`Ctrl+Arrows`** — move the display (hold to glide); **`Ctrl+Shift+Arrows`** — fine 4px steps
- Cards are semi-transparent, click-through, and update as characters draw/play.
- Settings persist to `%APPDATA%\SlayTheSpire2\dual_role_adventure_settings.json`; edit `ghostHandsScale` (default `0.5`) there to resize the cards.

## Hotkey reference

| Key | Context | Action |
|---|---|---|
| `Tab` / `Shift+Tab` | anywhere | switch controlled character (next / previous) |
| `[` `T` / `]` `R` `/` | anywhere | legacy switch aliases (previous / next) |
| `+` / `-` | lobby | change local character count (2–12) |
| `F8` | combat | toggle ghost hands overlay |
| `Ctrl+Arrows` (+`Shift`) | combat, overlay on | move ghost hands (fine steps with Shift) |
| `Y` / `LT+Y` | character select, controller | toggle Vakuu for one / all characters |
| `LT + D-pad` | character select, controller | count and edit-slot controls |

## Troubleshooting & feedback

- Log file: `%APPDATA%\SlayTheSpire2\logs\godot.log` — mod lines are prefixed `[LocalMultiControl]`.
- If something breaks, note the **act, room/screen, and exact steps**, then open a [GitHub issue](https://github.com/nanthepsmith-droid/STS2_DualRoleAdventure/issues) with the log attached.
- Known issues under investigation are tracked in [TODO.md](TODO.md).
