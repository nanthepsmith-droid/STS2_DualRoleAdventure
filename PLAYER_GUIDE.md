# Player Guide — DualRoleAdventure (LocalMultiControl)

> 中文版指南：[PLAYER_GUIDE.zh-CN.md](PLAYER_GUIDE.zh-CN.md)
>
> This page only covers **how to use the current version**. For what changed in each version, see [CHANGELOG.md](CHANGELOG.md).

Control 2–12 characters by yourself in Slay the Spire 2's multiplayer mode, on one machine, with no network.

## Install & enable

1. Subscribe on the Steam Workshop (or place `DualRoleAdventure.dll` + `DualRoleAdventure.json` in `<game>\mods\DualRoleAdventure\`).
2. Launch the game → `Settings → Mods` → enable **多角色冒险 (DualRoleAdventure)**. Restart if prompted.

## Starting a run

1. Main menu → **Multiplayer → Host**. This page has 4 cards: **Local Multi-Control (单人多角色)** /
   Standard / Daily / Custom. The last three are the **official online modes** and still work exactly as
   vanilla when the mod is installed (the mod coexists — it never takes over an official entry).
2. Three ways to start a local multi-character run:
   - **"Local Multi-Control" (单人多角色) card** — a normal local run (2–12 characters);
   - **"Local Custom Run" (本地·自定义模式) small button** below the card row — local run with seeds and custom rules;
   - **"Local Daily Run" (本地·每日挑战) small button** — local multi-character Daily Climb (max 4 seats, see below).
3. In character select:
   - **`+` / `-`** — add/remove local characters (2–12; duplicates allowed)
   - **`Tab` / `Shift+Tab`** — switch which character you're editing
   - Pick a character and ready-up for each slot, then start as usual.
   - ⚠ **More seats cost more performance**: with third-party "content-heavy" mods installed (YUI series /
     figure_Saya / Koishi / newsanguo — mods that hook into combat dispatch), a 5–12 seat run may only reach
     10–20 fps. With those mods, **stay at 4 seats or fewer**; see the *Performance* section of the
     [README](README.md) for the criterion. Hextech Runes is a different case (it pushes the whole-fight
     listener list past vanilla's capacity ⇒ a resize on every dispatch) and is **already fixed** by
     `ThirdPartyHotPathFix` — with it installed, try 12 seats again.

### Local Daily Climb

Enter via the **"Local Daily Run"** button under the card row (the official "Daily" card above it keeps the
vanilla online flow):

- **Max 4 seats** — the Daily multiplayer mode itself caps at 4 players; extra seats are clamped back to 4.
- **Characters come from the date** — Daily characters are assigned from a "date + player count" seed and
  **cannot be picked or randomized** (same rule as vanilla).
- **Never uploads to the Daily leaderboard** — local seats are fake local players and would pollute the
  board, so the mod **skips the upload** in local multi-character Daily runs only (log anchor
  `DAILY_SCORE_SKIP`); official online Daily and solo Daily behave exactly as before.
- **Works online and offline** — online it uses the game's time server; offline it falls back to local
  time (entering takes a little longer, which is normal).
- The Daily screen supports Vakuu hosting checkboxes, the `-/+` player-count panel and seat switching.

## Vakuu (AI auto-play)

Any character can be handed to the built-in autoplayer ("Vakuu"):

- On the character-select screen: toggle per character, or toggle **all** at once.
- Controller: `Y` toggles the highlighted character, `LT + Y` toggles everyone.
- Vakuu characters play their turns automatically; when no Vakuu character can act, control returns to you.

### Vakuu Form (off by default)

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

### What Vakuu handles automatically

With the switches above on, a backgrounded Vakuu also: claims their combat/event rewards,
resolves non-shared events, picks rest-site options, uses potions per the rule table, and
throws Foul Potions at merchants. Shared events and the crystal sphere stay manual;
shopping can optionally be handed to Vakuu via "Auto-buy cards" (below).

**Keep the Vakuu Form relic** (on by default): third-party "devour relics" effects (e.g. TouhouAncients'
Bottomless Stomach) can no longer remove the Vakuu Form relic; if it is ever lost, Vakuu keeps autopiloting
via the configured Vakuu players and re-grants the relic. Turning this off restores the old behavior.

> Features not tied to Vakuu autopilot (cross-character card rewards, stat badges, skipping draw
> animations, …) live at the end of the settings page under their own "Other settings" section and work
> even when Vakuu autopilot is off.

### In combat: which card, at whom, how fast

- **"Combat Decision Brain"** (inside "Vakuu autopilot", config key `vakuuBrain`, default **Heuristic**)
  decides **which card Vakuu plays and at whom** in combat.
  - **Heuristic (default)**: plays the leftmost playable card — the mod's long-standing behavior, most
    stable and predictable.
  - **Scored**: scores every playable card and takes the best — Powers / 0-cost / "this one kills an
    enemy" cards rank higher; when below half HP or **facing a lethal hit this turn** it prioritizes
    Block (and then **only picks among Block cards**, so a higher-scoring attack cannot push the
    life-saving card aside); AoE cards are worth more against multiple enemies; **X-cost cards are saved
    for last as long as any other card can be played** (to maximize X); ties go to the leftmost card.
    For single-enemy cards it **prefers a killable target, otherwise the enemy with the lowest effective
    HP** (focus fire); ally buffs go to human players first.
  - **Auto**: not wired to an external solver yet (that direction is shelved); the mode is kept and
    behaves exactly like Heuristic.
  - ⚠ Scoring uses a **rough estimate** (card values + Strength/Dexterity, no Vulnerable/Weak modifiers)
    and has **no cross-turn planning**; switch back to Heuristic anytime if it feels off. All three modes
    only affect in-combat plays — never card picks, events or shops.
- **Faster Vakuu card plays** (on by default): skips the card-pile animations during Vakuu's automatic
  plays — measured roughly 3x faster per card, data unchanged. The queue mode and concurrent mode below
  also benefit (queue-mode plays skip the card's travel animation into its pile).
- **Vakuu plays via action queue** (on by default): Vakuu's plays enter the game's multiplayer action
  queue instead of the legacy in-place autoplay, so with multiple Vakuus their plays interleave instead
  of running strictly one-by-one. Queue plays still skip the trailing wait and the result-pile tweens
  (the "card flies out of hand" animation cannot be skipped), so they remain a little slower per card
  than the in-place path.
- **Concurrent Vakuu plays** (on by default, requires queue mode): multiple Vakuus no longer wait for
  each other, and when you play a card or end your turn your action jumps the queue immediately
  (yielded Vakuu plays are simply replayed next round — nothing is lost).
- **Vakuu view mode** (three modes, default "never follow"): Vakuu no longer steals your camera while
  autopiloting. "Key moments only" peeks at Vakuu's turn start for about a second and returns;
  "always follow" restores the old feel. The safety nets that hand control back to you are unaffected.

### Card picks, events and stats

- **Personal preference recorder** (on by default): records your own card-reward/event choices to
  `personal_stats.json` (only human decisions; Vakuu's automated picks are excluded; only finished runs count).
- **Personal-stats decision assist** (off by default): Vakuu prefers your own stats when picking cards/events,
  falling back to community stats when samples are thin. The "preference tier" cycles
  Character-first / Volume-first / Character-only. With it enabled, Vakuu also picks event options by your
  own option pick-rate + win-rate (same policy as card picks) instead of first/last/random.
- **Personal stat badge** (off by default): shows your own pick/win percentages (XX%) with hover details on
  card rewards / shop / event buttons; data source has three modes (personal only / personal+community fallback /
  blended) and the badge corner is selectable (default bottom-left to avoid overlapping community-stat labels).

### Shop automation

> ⚠ **Only applies to the seat you have switched to**: auto-buying is triggered when the shop UI binds to a
> character (shops are settled per controlled character). To let a Vakuu seat buy, **switch to it first**
> (`Tab` / `Shift+Tab`) — then it picks, pays and buys card removal on its own. While you stand on another
> character it buys nothing. Switch to each Vakuu seat once; **each seat auto-buys only once per shop**.

- **Auto-buy cards in shops** (experimental, off): buys cards with ≥20% community win rate while keeping
  ≥50 gold; "also buy without data" lets mod cards buy on the gold floor alone.
- **Auto-buy relics** / **auto-buy potions** (off by default, requiring the "Auto-buy cards" master switch):
  relics/potions have no community ratings, so these only check "affordable + still ≥ 50 gold afterwards";
  potions additionally require a free potion slot.
- **Auto card removal** (off by default, same master switch): when the gold floor allows it, Vakuu buys the
  shop's card-removal service once and removes one card by pick priority (with "smart pick priority" on:
  Curse > Status > Quest > Strike > basic Defend > the rest).
- With "Personal-stats decision assist" on and at least 3 finished runs that bought something, relics and
  potions also consult **your own** win-rate delta (runs where you bought it vs. runs where you did not):
  a **negative delta vetoes the purchase** (veto only — it never proactively picks what to buy).

### The four Vakuu features (inside "Vakuu autopilot"; **all off by default**, and they need "Vakuu Form Autopilot (Master Toggle)" on)

- **Purify at rest sites** (`purifyVakuu`): rest sites give you (the human) an extra "Purify" option —
  pick a Vakuu in a small "choose player" panel, then the **vanilla card-removal screen** opens to remove up
  to 5 cards from its deck (you tick them one by one). Canceling, or picking none, leaves the option
  unconsumed so you can try again.
- **Unite in combat** (`uniteVakuu`): combat gets a "Unite" button (above the end-turn button), **once per
  combat** — first **copy** one card from your deck into a chosen Vakuu's combat hand, then **copy** one
  card from a chosen Vakuu's deck into your own hand. Both directions are copies and **decks are unchanged**
  (the copies vanish when the combat ends); either step can be canceled, and **canceling both means the
  chance is not used** (the button stays).
- **Refine at rest sites** (`refineVakuu`): rest sites give you an extra "Refine" option — pick a Vakuu, then:
  ① take cards from its deck (limit: Unlimited / 20 / 5, **you may take none**) → ② multi-select relics to
  take (limit: Unlimited / 5 / 3 / 1; the Whispering Earring and Vakuu Form are excluded; these are
  **really taken**) → ③ take **as many of its potions as you want** (cap = your free potion slots; the ones
  you leave are removed too) → ④ absorb a share of its HP and Max HP (All / Half / Quarter) →
  ⑤ **melt it down** (HP and Max HP zeroed, it leaves the fight). The seat stays in the roster and the party
  isn't shrunk (if its Max HP is ever restored it can come back). ⚠ **Refining is irreversible.**
  Four extra settings: HP ratio (default All), card limit (default Unlimited), relic limit (default
  Unlimited), and whether to take its cards & relics / potions (both on by default).
- **Vakuu's Daddy** (`vakuuDaddy`): **your human seats start the run with the relic Vakuu's Daddy** — each
  combat starts with one【I Block】【You Attack】【Merge】in hand (0-cost skills; upgraded versions gain
  Retain; they never show up in rewards or shops):
  - **【I Block】** (choose an ally): attack damage it takes this turn is redirected to you, but you take
    **only half** of it, and **your own Block still applies**; expires at the end of the enemy turn.
  - **【You Attack】** (choose an enemy): **every Vakuu prioritizes attacking it this turn** (your own
    targeting is unaffected).
  - **【Merge】** (choose a Vakuu ally): its **hand and energy become yours**; this turn your draws come from
    either draw pile at random and your discards go to either discard pile (the other deck is not stolen —
    this only affects this combat's copies).
  - All three draw 1 card; the toggle takes effect at the **start of the next run**; Vakuu seats never get
    this relic.

### War God Summon (inside "Vakuu autopilot", **off by default**; formerly "Hell God")

Turn on **"War God Summon"** (`hellwarGod`; takes effect at the **start of the next run**):

- **Relic Hell Earring**: granted to **human seats only** (Vakuu seats don't get it). The number on the relic = your current **charges** (the summon budget).
- **Rest site option "Split Spirit"**: spends a share of your **current HP** (No cost / 10% / 15% (default) / 20%, never below 1 HP) for **+1 charge**;
  by default it **doesn't use up the whole rest** (you can still rest / smith afterwards).
- **"Gain charges each new act" (off by default)**: +1~3 charges (default 2) per act; granted once per act, never re-granted on reload.
- **Summoning in combat**: at the **start of every combat turn**, if you can afford it, the summon panel appears:
  - **four single tiers**: **Common 1 / Rare 2 / Epic 3 / Ancient 4** charges — each brings its own **preset deck + preset relics**
    (Ancient also gets +25 max HP); you then **pick a "character preset"** ("Auto / Random / a specific preset";
    for Common the candidates are **every playable character on your machine**, mod characters included);
  - **【Endless War Gods】** (12 charges, once per combat): fills every free seat, rolling a tier per seat
    (Common / Rare / Epic by default; there's an "may roll Ancient" toggle);
  - **【Ultimate Nine-Chain】** (requires a charge **cap** of 12 and enough charges left this combat, once per combat):
    summons **9 Ancients** and **permanently** lowers the cap by the number actually summoned;
  - **Revive**: a war god that died stays on the field; its owner can revive it at the start of their turn for the same tier's cost.
- **A war god is a "temporary player"**: it **plays its own cards** (autopilot), has its own portrait and HP bar, **takes no party slot and gets no combat rewards**;
  it **shares damage** with its owner (intangible on the turn it appears; from the next turn it can tank half and pass it on) and dies with its owner.
- **War god HP bars and portraits**: a summoned war god is drawn as one extra row `战神·<tier> 75/75` after the summons in
  the summon panel beside your status strip (the bar tracks HP; dead = greyed out; returns on revive; disappears when combat
  ends or you leave the run; more than 8 rows collapse into "还有 N 个（已折叠）"); portraits stand **above their own owner**
  (4 per row, stacked upward, centered by count, clamped to the screen edge), and crowded human portraits are spread apart
  **only** when too close (moving a portrait **carries its pets along**; after another mod reflows the formation, the next
  human turn start settles it back).
- **Preset war gods (v2)**: preset table = `%APPDATA%\SlayTheSpire2\hellwar_presets.json` (our "War God Forge" editor can build it visually);
  the mod **ships 18 presets** (Rare 5 / Epic 5 / Ancient 8) so you don't need to place the file yourself — your own file **overrides same-`id` entries**.
  A preset referencing content **missing on your machine** (character / card / relic) is **skipped as a whole** (named in the log); the rest still work.
  Decks support per-card upgrades `+` and **enchantments** `[enchant]` / `[enchant:count]` (an unknown enchant is skipped, the card is still given);
  the **bonus HP may be negative** (= lower max HP, never below 1).
- **Export content catalog** (for the external editor): the settings page has a one-shot button that writes
  `%APPDATA%\SlayTheSpire2\content_catalog.json` (includes third-party mod content, tagged by source); click it again after installing/removing mods.

⚠ Two things to know: ① "Auto" per tier = **the first usable preset of that tier** (mark one as its "default" in the editor to pin it);
② war gods **never** receive combat rewards — only human seats do.

### Co-op Bots compatibility (optional)

When the third-party **Co-op Bots** mod (online AI teammates) is installed, this mod coexists with it:

- **Not installed / not wanted**: zero impact — the mod probes for it and degrades gracefully.
- **Handing seats to the online bots**: set `coopBotsSeats` in
  `%APPDATA%\SlayTheSpire2\vakuu_autopilot.json` (comma-separated seat numbers, e.g. `"2,3"`). If a seat is
  checked for both Vakuu and an online bot, **the online bot wins** and the log prints a WARN.
- **If you run Co-op Bots, keep this mod enabled**: its own bots rely on our shop-ACK bypass to leave the shop.
- Known issues (root cause still being investigated, see `TODO.md` BUG-17 / BUG-18): occasionally
  "an event's last page card-pick cannot advance" and "your card clicks are ignored while Vakuu plays" —
  diagnostics only so far.

## During a run

- **Switch characters:** `Tab` (next) / `Shift+Tab` (previous). Legacy keys `]` `R` `/` (next) and `[` `T` (previous) still work.
- Each character owns their deck, hand, energy, gold, potions, relics, and choices. The UI (hand, energy, potion bar, status strip) follows whoever you control.
- **Combat:** play each character's turn, switching freely; end turn per character.
- **Turn-start animations:** with many players, "Other settings" offers **"Skip other players' turn-start draw animations"** (off by default) —
  only the character you are viewing plays its draw animation, the rest resolve instantly (data is unaffected).
- **Rewards:** loot is generated per character and shown as one combined list, each entry prefixed with its owner (e.g. `[Player 2]`). Claim with the matching character.
- **Events:** by default each character resolves the event independently — the mod walks you through them one by one. Shared-event votes are auto-completed where the game requires everyone to vote.
- **Crystal Sphere:** each character pays for and divines on their own turn; revealed rewards (and the Payment Plan curse) belong only to the revealing character. Character switching is locked while a divination is in progress — finish your divinations first, the mod switches automatically.
- **Rest sites:** each character chooses in sequence (rest, upgrade, etc.). **With 5+ players everyone's portrait and speech bubble show up**
  (the vanilla scene hard-codes four character containers; they are created per headcount, and layouts with ≤4 players are unchanged).
- **Treasure rooms:** each character picks their own relic. **With 5~12 players the slots are created per relic count in a grid layout**
  (the vanilla scene only has four relic slots, so the 5th onward was invisible and unclickable), no longer spilling under the HP bar on the left.
- **Shops:** purchases and card removal are billed to the character currently in control. The Fake Merchant event (商人？？？) works the same way — each character browses and buys from their own stock with their own gold, and either character can throw the Foul Potion (浑浊药水) at the merchant to start the fight.
- **Overlay ownership:** with multiple players on one screen, the mod's summon / choice overlays are titled "—— 角色N" so you can tell whose they are.
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

- Log file: `%APPDATA%\SlayTheSpire2\logs\godot.log` — mod lines are prefixed `[LocalMultiControl]`, and a batch of
  diagnostic lines (`[性能探针]` / `[StartupProf]` etc.) is always on; include them when reporting.
- If something breaks, note the **act, room/screen, and exact steps**, then open a [GitHub issue](https://github.com/nanthepsmith-droid/STS2_DualRoleAdventure/issues) with the log attached.
- Known issues under investigation are tracked in [TODO.md](TODO.md); per-version changes are in [CHANGELOG.md](CHANGELOG.md).
