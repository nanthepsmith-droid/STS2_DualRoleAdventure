# Changelog

Notable versions and key changes of `LocalMultiControl` / `DualRoleAdventure`. Entries up to v1.30 are translated from the original author's Chinese changelog; the fuller day-by-day history lives in `docs/archive/player-update-history.zh.md`.

## [v1.37] - 2026-08-26

> 分支 `feat/vakuu-config-menu`（2026-08-25 → 08-26，marker r1–r22）。所有新功能集中在
> 设置 → 常规页新增的「瓦库托管」子菜单中开关（即时写回 `%APPDATA%\SlayTheSpire2\vakuu_autopilot.json`），
> 总开关仍为 `useVakuuForm`（默认关 = 与 v1.36 行为一致）。

### Added
- **设置页子菜单**：设置 → 常规注入「瓦库托管」按钮行，推入原生子菜单栈（原生观感，
  无黑幕浮窗），勾选框复刻游戏原生外观；策略类配置用循环切换按钮行。
- **战后奖励自动领取**：卡牌奖励领最左（`autoClaimCards`）、金币与遗物自动领
  （`autoClaimGoldRelics`）、药水奖励自动领（`autoClaimPotions`）——有空位直接领；
  满栏且栏内有鲜血药水先喝掉腾位；否则奖励稀有度高于栏内最低才丢弃栏内最低者换领。
- **非共享事件自动选择**（`autoChooseEvents`）：按策略 first/last/random（`eventChoiceMode`）
  逐页选择，直调 `EventOption.Chosen()` 绕开消息层；复刻原版联机死亡拦截
  （致死选项剔除、整页死路停住等真人）；触发战斗/小游戏弹层即停；水晶球绝对排除、涅奥默认关。
- **事件/遗物自定义奖励后台直接结算**：后台瓦库的 `RewardsCmd.OfferCustom` 奖励
  （未来药水/药水快递/坩埚/召唤铃等）不再依赖无人能点的弹屏，按开关逐件结算或跳过放行。
- **火堆自动选择**（`autoRestChoice`）：血量 <50% 睡觉；有遗物选项在睡觉以外随机；
  否则锻造升级"打击/防御以外"的最后一张牌（不足用打击/防御补齐），全升完则睡觉；
  未满血且没得锻时愈合队友；全员满血时锻造候选放宽到含打击/防御；帐篷多选全拿。
- **战斗内自动用药引擎**（`autoUsePotions` 默认关）：65 种原版药水逐条规则表驱动，
  支持回合开始/回合结束前两个评估相位、敌人意图伤害估算、定向作答选择器：
  - 治疗/资源类：血液/再生低血自用；果汁到手立刻喝；混沌药水（EntropicBrew）栏内只剩它
    且有空位时喝；迅捷/异蛇之油剩能量抽牌；能量药水救"仅因能量不足打不出"的高费牌；
    稳定血清保留能力牌手；龙涎香 Boss 残血吃额外回合；
  - 精英/Boss 首回合：力量/敏捷/集中/异鱼之油/流动铜液/马萨雷斯赠礼/明耀酊剂/宇宙药剂/
    精炼混沌/明晰提取物自用，攻击技能能力无色药水使用，火焰/毒素/灾厄/易伤/虚弱/
    消亡粉末对第一个敌人，爆炸安瓿敌数≥3 或首回合全体；
  - 意图触发：甲虫汁/镣铐=有敌人意图攻击；铁心=敌人攻击意图或已有覆甲；速度=有技能牌；
  - 回合结束前防御兜底：格挡（≥敌伤+10 或致死）、固化（格挡×3<敌伤）、罐装幽灵/
    幸运补剂（≥+30 或致死）、瓶中船（精英/Boss ≥+15 或致死）；
  - 角色路由：集中/扩容/黑暗精华→故障机器人队友，星星/王之勇气→储君，骨头酿/尸鬼瓮→
    亡灵契约师，士兵炖汤→铁甲战士（仅Boss）；自己是该职业则自用；
  - 优先给真人：复制、超巨化；
  - 手牌构成条件：灰水只消耗状态/诅咒、赌徒特酿/瓶装潜能/发光水只洗坏牌、
    癫狂之触免费最高费牌、预知之滴取最便宜能力/稀有牌、液态记忆取弃牌堆能力牌；
  - **昏眩**（RingingPower）时不使用抽牌/能量类药水；
  - **污浊药水**绝不在战斗中使用，遇商人自动投掷换 100 金币；果汁经领取链路即刻入队饮用；
  - mod 药水（非游戏内置）：普通战斗随机回合（预掷 1~3）消耗。

### Fixed
- 火堆后出发永久黑屏：多控下一方未选完火堆就出发，`MoveToMapCoordAction` 在
  `RestSiteSynchronizer.AfterAllRestSitesCompleted` 处永久挂起（原版注释明确警告过该挂起）。
  出发前把未完成的休息区选择按跳过补完（与断线处理同款语义）；被跳过方拿不到火堆收益。
- 地图点节点不跟投/不出发：击杀最后一敌的卡牌动作仍在 Executing 时立即胜利结算翻转战斗状态，
  战斗结束清理跳过执行中的动作，其随后暂停等待玩家选择、恢复后已不在战斗而**永久卡死队列头部**，
  堵死后续全部投票动作。新增战斗外残留战斗动作清道夫（CombatEnded 后延迟多轮扫描，
  按游戏原语义 Cancel+移除非执行中的战斗类残留；新战斗开始即停止）。
- 后台瓦库的事件/遗物自定义奖励挂起：`OfferCustom` 在 `IsMe=false` 时不弹屏但完成任务
  无人满足，事件选项 await 它会永久卡住（SetEventFinished 排在其后）。改为后台直接结算放行。
- 托管上下文窗口吞掉前台玩家自己的弃牌/抽牌演出（切人后自愈）：`CardPileCmd.Add` 大重载
  执行期间把 NetId 钉扎到前台角色。
- 合成/抽牌视觉节点串进前台玩家手牌（数据层一直正确）：进手牌演出改按前台角色判定。
- 火堆选择成功但角色头顶不显示气泡：等 UI 房间节点就绪后再决策；显式驱动头顶气泡。

### Changed
- 托管效果选牌改用自建策略选择器（`cardPickMode`: first/last/random，默认 last，
  解决酒狐合成永远拿到排最前的牌）；作用域外选牌一律不代答（回滚验证：代答会导致开局黑屏）。
- 【瓦库形态】旧"永久低语耳环"路径完整保留，`useVakuuForm=false` 时行为与 v1.35 一致。

## [Unreleased]

### Changed
- Hardened `LocalLoopbackHostGameService.GetVersionInfoForPeer` to return the local version info
  instead of null (same as upstream's v1.32): the game's three lobby join handlers call
  `GetVersionInfoForPeer(senderId).Value.IsModded()` unguarded. These messages are unreachable in
  local self-coop today, but future game changes could start routing through them.
- READMEs: GuyGinat's community Workshop item ([3772900244](https://steamcommunity.com/sharedfiles/filedetails/?id=3772900244))
  has resumed updating, so it is now recommended alongside the original item (3747538947).

## [v1.36] - 2026-08-24

### Added
- 瓦库形态托管（默认关闭）：新增独立遗物【瓦库形态】（+1 能量 + 接管所有回合自动出牌），
  与旧的"永久低语耳环"路径并存。总开关集中在 `%APPDATA%\SlayTheSpire2\vakuu_autopilot.json`
  （`useVakuuForm` 默认 false = 完全保持原有瓦库行为；每次开局重新加载）：
  - `playAllCards`：打光所有手牌（60 张护栏防死循环）；
  - `backgroundMode`：后台托管——回合钩子/自动出牌不再把前台切给瓦库角色，
    选牌在自动出牌作用域内自动作答免切换，作用域外保留切换兜底；
  - `suppressVanillaEarring`：压制原版低语耳环的自动出牌钩子（+1 能量保留）；
  - 交互安全网：后台瓦库被弹层卡住（战斗滞留 12 秒 / 事件滞留 8 秒）时自动切前台
    并全屏提示，交由人工处理。

### Fixed
- 读档后奖励界面永久黑屏（v1.34 起）：读档链路 `StartRun: FadeOut → LoadRun（内部进入
  PreFinished 房间并弹出战后奖励）→ FadeIn` 中，合并奖励补丁同步等待玩家领奖，阻塞了配对
  的 FadeIn——全屏转场黑幕（NTransition SimpleA=1）永不撤除。现读档重放期间奖励改为后台
  弹出、入口立即放行，与原版 fire-and-forget 语义一致；实时战斗路径行为不变。
- 自定义遗物描述页异常（显示未解锁且无法退出/打开）：未加入任何遗物池的遗物在
  `RelicModel.Pool` 处抛 `InvalidOperationException`，中断悬停提示构建。托管遗物现经
  `ModHelper.AddModelToPool` 注册进事件遗物池（该池无随机奖励引用）。
- 真人选牌被托管抢答：瓦库自动出牌进行中（全局选择器在栈上）时，真人打出的需要选牌的卡
  （如酒狐合成）会被瞬间自动应答为第一张。现在 `CardSelectCmd.Selector` 加守卫——选牌归属者
  非瓦库形态角色时强制走正常选牌 UI；另加"任何弹层打开即暂停自动出牌/看门狗"护栏。

### Changed
- 【瓦库形态】文案贴合实际效果（接管每回合从左到右出牌 + 最大能量 +1），flavor 为
  "让瓦库玩算你赢了。"
- 新增诊断日志三件套（可见性链/转场黑幕扫描/奖励遮挡检查），便于未来排查同类黑屏。

## [v1.35] - 2026-08-23

### Fixed
- Fake Merchant (商人？？？) event: purchases were always charged to and granted to the character
  that entered the room (usually character 1), no matter who was browsing the rug. The shop UI
  binds to that instance's `MerchantInventory`, whose entries hard-wire their buyer at creation.
  Shared custom-layout events are now rebuilt per foreground character on switch
  (`RefreshEventRoomForControlledPlayer`), so each character browses and buys from their own
  stock with their own gold, matching vanilla multiplayer semantics.
- Fake Merchant event: only the first character could throw the Foul Potion (浑浊药水)
  to start the fight; with the second character controlled, the potion popup's throw button stayed
  disabled and a forced use would consume the potion with no effect. Root cause: character
  switching re-points `EventSynchronizer._localPlayerId` at the foreground player, but this shared
  custom-layout event attaches its `NFakeMerchant` UI node only to the event instance of whoever
  entered the room, so `EventRoom.LocalMutableEvent.Node` was null for the other character.
  New `FoulPotionPatch` falls back to the live custom-event screen / sibling instances when
  resolving the merchant button (`GetFoulPotionMerchantTarget`) and completes the throw on behalf
  of the vanilla branch in `FoulPotion.OnUse`; combat and real-shop branches are untouched.
  Character switching is also ignored while a throw settlement is in flight so the fight can
  never lose a combat-ready signal.

## [v1.34] - 2026-08-22

### Fixed
- Relic "Silken Tress" (华美发束): the promised Glam enchantment never reached the card rewards
  you actually see, while the relic itself was already marked as used up after the first combat.
  Root cause: vanilla `CombatRoom.OfferRoomEndRewards` pre-generates a reward set per character,
  and one-shot relic hooks (Silken Tress's `IsUsed`, egg upgrade counters, Silver Crucible, …) were
  consumed there — enchanting cards that were then thrown away. The mod's merged-rewards screen
  regenerated fresh sets afterwards with every relic already spent. A new intercept on
  `CombatRoom.OfferRoomEndRewards` (`CombatRoomOfferRoomEndRewardsPatch`) now generates each
  character's rewards exactly once in local self-coop and preserves the vanilla
  `Hook.BeforeCombatRewardOffered` step, so Silken Tress enchants the displayed rewards and other
  modify-once relics apply correctly too.
- Crystal Sphere event: only the first character could complete their divination — finishing it
  ended the event for everyone. Two causes: the divination overlay stayed on the overlay stack
  after completion (the event auto-switch chain defers while overlays are open), and the overlay's
  own PROCEED button called `ProceedFromTerminalRewardsScreen`, opening the map directly. The event
  option handlers now close finished divination overlays when they complete so the existing
  auto-switch chain walks to the next character, and the minigame PROCEED is intercepted while any
  character still has an unfinished Crystal Sphere (switching to them instead of leaving the room).

### Changed
- Crystal Sphere settlement is now strictly per-character: paying "Uncover Future", the Payment
  Plan Debt curse, and revealed gold/relic/potion/card rewards stay with the revealing character.
  This supersedes the original author's mirror-settlement design (everyone pays together, loot is
  copied to everyone); all cross-player mirroring for this event is disabled behind the
  `CrystalSphereMirrorRuntime.CrossPlayerMirroringEnabled` switch (= false) if shared settlement
  is ever wanted back.
- Character hotkey switching (Tab / Shift+Tab / legacy keys) is ignored while a divination
  minigame is in progress. Switching away previously left the minigame completing under another
  character's context, failing its owner check in `DoLocalCrystalSphereRewards`, so the event could
  never finish (softlock).

## [v1.33] - 2026-08-21

### Added
- Built-in Oddmelt compatibility: Oddmelt's hidden Gauge input cards (GaugeSummonActionCard /
  GaugeUltimateActionCard / GaugeBurstActionCard) are deliberately registered in no card pool, so
  rebuilding the combat hand UI on character switch called `NCard.Create` on them and hit
  "is not in any card pool!" (`InvalidProgramException`), rolling the switch back. A guard prefix on
  the game's `NCard.Create` (`NCardCreateHiddenCardGuardPatch`) now returns null for cards whose
  pool cannot be resolved; every caller already null-checks, so such cards are skipped exactly as
  Oddmelt intends. Without Oddmelt installed the guard is a no-op. This supersedes the separate
  `OddmeltGaugeCardRenderFix` mod, which is no longer needed.

## [v1.32] - 2026-08-21

### Fixed
- Local multiplayer: `[HarmonyPatch]` attributes placed only on methods (inside a class without a
  class-level `[HarmonyPatch]`) were silently skipped by `PatchAll`, so several patches never ran —
  including the combat card-selection foreground switch and the serialized hand selection. All patch
  classes now carry a class-level `[HarmonyPatch]` marker/target (`CardSelectForegroundSwitchPatch`,
  `NPlayerHandSelectCardsSerializationPatch`). Init now logs the patched-method census.
- Local multiplayer: `NPlayerHand.SelectCards` prefix no longer uses `ref bool __runOriginal`
  (Harmony 2.4.2 generated invalid wrapper IL for this method, crashing mod init with
  `InvalidProgramException`); it now uses the proven bool-return + `ref Task<...> __result` pattern.
- Local multiplayer: when two locally-controlled characters both need a hand-card choice at the same
  sync point (e.g. both characters holding the GensokyoSpire boss Utsuho's "Meltdown" buff select a
  card to exhaust at turn start), the second `NPlayerHand.SelectCards` overwrote the single shared
  `_selectionCompletionSource`, permanently orphaning the first choice and softlocking that character
  (unable to play cards or end turn). Combat hand selections are now serialized: only one selection is
  active at a time, the later one asynchronously waits its turn, and the foreground is switched to the
  selecting character right before its prompt is shown. The selection's owner is tracked per async
  chain from the `FromHand`/`FromSimpleGrid` entry (an `AsyncLocal` in
  `CardSelectForegroundSwitchPatch`), falling back to the triggering model's owner, so interleaved
  selections from both characters are each shown against the right hand. The wrapper's re-entry guard
  also uses an `AsyncLocal` so sibling `SelectCards` calls from the action executor are still serialized
  while the first prompt is on screen (`NPlayerHandSelectCardsSerializationPatch`).
- Local multiplayer: turn-end / turn-start card-choice effects belonging to a backgrounded
  character no longer hang or get skipped. The game builds `HookPlayerChoiceContext` with
  `LocalContext.NetId` as `_localPlayerId`, so when the last-visible character differs from the
  character whose choice is running, `_gameAction.OwnerId != _localPlayerId` and the hook action is
  never enqueued locally (`HookPlayerChoiceContext.cs:194/206`). The mod now:
  - Forces `_localPlayerId` to the choice owner inside every `HookPlayerChoiceContext` constructor
    (`HookPlayerChoiceContextLocalPatch`), so the choice is enqueued and runs on the loopback.
  - Auto-aligns the controlled foreground (hand / local context / top bar) to each character in the
    turn loop before `SetupPlayerTurn`, `DoTurnEnd` and `FlushPlayerHand`, and synchronously before
    `CardSelectCmd.FromHand`/`FromHandForUpgrade`/`FromSimpleGrid`/`FromChooseACardScreen`/
    `FromCombatPile`. The previous deferred (`CallDeferred`) foreground switch could miss the
    synchronous wait for the choice, so it has been replaced with a synchronous switch guarded by the
    same in-play/in-selection/target-selection checks (`CardSelectForegroundSwitchPatch`,
    `CombatManagerTurnHookForegroundPatch`, `LocalMultiControlRuntime.TryEnsureForegroundForPlayer`).
- Character switching during an in-progress card play / card selection no longer rolls back the
  control context; the combat UI refresh is deferred until the flow finishes (bounded retries), and a
  stuck off-screen end-turn button is re-animated in (`LocalMultiControlRuntime`).
- Hook actions (e.g. Mini-Hakkero's turn-end hand selection for a backgrounded character) switch the
  foreground to their owner at enqueue time, before the selection UI appears
  (`HookEnqueueForegroundPatch`).

### Adapted
- Game v0.111.0 (beta111, 2026-08-13). The mod previously failed to load with
  `ReflectionTypeLoadException`. Changes:
  - `INetGameService`/`INetHostGameService` gained net-new members in v0.111.0. The local
    loopback host service (`LocalLoopbackHostGameService`) now implements `LocalVersion`,
    `ClientConnectionFailed` and `GetVersionInfoForPeer`.
  - `LoadRunLobbyPlayer` replaced its `versionInfo` field with a flat `isModded` bool; the
    load-lobby auto-ready patch now writes `isModded` from the net service's local version.
  - `StartRunLobby.MaxPlayers` was replaced by a constructor-injected readonly `_maxPlayers`
    (no auto-property backing field). Local self-coop lobbies are now created at the full 12-player
    capacity up front and the reflection-based `EnsureLobbyMaxCapacity` resize was removed.
  - `CombatManager`'s ready-to-begin-enemy-turn set moved onto the turn state; the ready-set lookup
    tries the new location first and falls back to the pre-beta110 field.

## [v1.31] - 2026-07-27

First community-maintained release. The original author (liwenhao0427) discontinued
maintenance at v1.30 and gave written permission (2026-07-27, email) for this fork to
take over maintenance and distribution; they will cross-link this version from the
original Workshop item and video.

> Note (2026-08-21): the paragraph above is preserved from the v1.31 release notes written by
> the previous maintainer (GuyGinat). The current maintainer has not verified that
> authorization and makes no such claim.

### Added
- Optional "ghost hands" combat overlay: shows every backgrounded character's current hand as rows of non-interactive cards behind and above the active character's hand. Toggle with `F8`; move the display at runtime with `Ctrl+Arrows` (`Ctrl+Shift+Arrows` for fine 4px steps). State, position and scale persist to `user://dual_role_adventure_settings.json` (edit `ghostHandsScale` there to resize; default 0.5). Card nodes are borrowed from and returned to the game's own `NodePool`.

### Changed
- Character switching is now bound to `Tab` (next) and `Shift+Tab` (previous). The legacy keys — `]` / `R` / `/` for next, `[` / `T` for previous — still work as aliases.
- Documentation translated to English; original Chinese documents archived under `docs/archive/`.

### Fixed
- Adapted to game v0.109.0 (2026-07-17); five compile-level API breaks since the v1.30 baseline:
  - `VoteToMoveToNextActAction` gained a required `currentActIndex` parameter; the act-change auto-ready patch now passes `RunState.CurrentActIndex`, mirroring the game's own call site.
  - `PotionFactory.CreateRandomPotionsOutOfCombat` now returns `IEnumerable<PotionModel>`; the local merchant inventory rebuild materializes it with `.ToList()` like the game does.
  - `Controller` input constants were renamed (`joystick*` → `lStick*`, `dPad{East,West,North,South}` → `dPad{Right,Left,Up,Down}`); updated the gamepad axis router and the character-select hotkey hint icons.
  - `EventModel.GenerateInternalCombatState(runState)` was removed; rebuilding a non-shared combat-layout event room on character switch now calls `EventSynchronizer.GenerateInternalCombatStateIfNecessary(event)` instead.
- Local Multi-Control entry unreachable on fresh profiles: since v0.109.0, pressing Host with `Progress.NumberOfRuns == 0` skips the host submenu and immediately hosts a Standard online game, so the injected card never appeared. A new prefix on `NMultiplayerSubmenu.OnHostPressed` routes fresh profiles through the host submenu (its Standard card keeps the one-click hosting behavior).

### Notes
- `NRestSiteRoom.UpdateNavigation` no longer exists in v0.109.0; the rest-site controller focus recovery already guarded that lookup with a null-conditional call, so it degrades to the mod's own focus-grab fallback.
- Static sweep of all string-based Harmony/reflection targets against decompiled v0.109.0 source: everything else resolves (the two `BeginRunIfAllPlayersReady` misses are the intentional legacy-name fallback legs).

## [v1.30] - 2026-06-19

Final release by the original author. (Summarized from the archived player-update history.)

### Fixed
- Adapted to the game's 1.0 release: treasure rooms no longer black-screen (the removed `IsSinglePlayerOrFakeMultiplayer` API is no longer called; the chest gesture layer works with the new vote structure, with a focus guard for first-frame relic nodes).
- Shop inventory switching adapted to the new `Inventories` structure; ESC quick-restart moved to the release version's load method.
- Loading a multi-character save resumes correctly (`BeginRunForAllPlayersIfAllReady` with a reflection fallback to the legacy `BeginRunIfAllPlayersReady`).
- Reward synchronizer local-player checks (`RewardsSetSynchronizer`) are re-synced on character switch, fixing "clicked a relic reward, nothing happened" in the beta.

## [v1.17] - 2026-03-28

### Changed
- Reworked the loot phase: rewards are generated independently per character, then presented as one combined list with a per-character prefix label.
- Relic-doubling effects, shop-quality factors, and hunt-style card effects now apply per owning character.
- Removed the old resource-mirroring logic; relic/potion/gold mirroring in loot scenes is suppressed by the aggregated flow.

## [v1.10] - 2026-03-22

### Fixed
- Vakuu auto-switch could miss its trigger: added a per-frame fallback that switches to the next controllable non-Vakuu character when no Vakuu character has a playable card.
- Standardized the English spelling `Vakuu`.

### Changed
- Vakuu toggle hint text unified across mouse and controller paths, localized in Chinese and English (single: "Vakuu will control Player X"; all: "Vakuu will control all characters").

## [v1.09] - 2026-03-21

### Added
- Controller combos on the character-select screen: `Y` toggles Vakuu for the highlighted character, `LT + Y` toggles all.
- Controller hotkey hint icons on the select screen (`LT + D-pad`, `Y`, `LT + Y`), shown only in controller mode like the base game.
- Full LT-combo input chain: while LT is held, native controller input is intercepted; on release, the original LT action replays only if no combo was used.

## [v1.06] - 2026-03-20

### Changed
- Potions are now maintained per character instead of being pinned to slot 1.
- Removed the special "+2 initial potion slots" rule; potion slots match online multiplayer behavior.
- The top potion bar follows the currently controlled character after switching and stays directly usable.

## [v1.05] - 2026-03-20

### Fixed
- Removed a duplicate copy path in the treasure chest patch that could double-copy in a single settlement.
- Chest relic copying now de-duplicates against already-owned relics.
- Treasure-map settlement handled once for the whole party, with automatic view switching during Vakuu flows.

## [v0.1.9] - 2026-03-15

### Fixed
- Rest sites no longer occasionally auto-upgrade a random card and end without manual selection; manual card selection outside combat restored.
- Rest sites run strictly serially: each character chooses once before the site ends — no more skipped characters.
- Event branches that upgrade/remove cards process every unfinished character with the same option, one by one.
- Mouse visibility restored after switching characters at a treasure chest.

## [v0.1.3] - 2026-03-15

### Changed
- Combat character-switch buttons redesigned as pure icons with scaling and right-side mirroring.
- Character-select 2x2 button group rearranged to match combat-screen interaction style.
- Multiple rounds of screenshot-driven tuning: arrow positions, margins, horizontal spacing, readability.

## [v0.1.2] - 2026-03-14

### Changed
- Release process upgraded to support "fast releases" (publish directly from existing artifacts in the project root).

## [v0.1.1-clearable] - 2026-03-14

### Fixed
- Combat top-bar display glitch on entering combat.
- Shared-event auto-voting flow blockage.

## [v0.1.0-initial-usable] - 2026-03-13

### Added
- Minimal usable loop for local multi-control.
- Basic input switching, key synchronization chains, and the core patch framework.

[Unreleased]: https://github.com/nanthepsmith-droid/STS2_DualRoleAdventure/compare/v1.32...HEAD
[v1.32]: https://github.com/nanthepsmith-droid/STS2_DualRoleAdventure/releases/tag/v1.32
[v1.31]: https://github.com/GuyGinat/STS2_DualRoleAdventure/releases/tag/v1.31
[v1.30]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v1.30
[v1.17]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v1.17
[v1.10]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v1.10
[v1.09]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v1.09
[v1.06]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v1.06
[v1.05]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v1.05
[v0.1.9]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v0.1.9
[v0.1.3]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v0.1.3
[v0.1.2]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v0.1.2
[v0.1.1-clearable]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v0.1.1-clearable
[v0.1.0-initial-usable]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v0.1.0-initial-usable
