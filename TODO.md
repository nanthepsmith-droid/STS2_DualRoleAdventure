# Known Issues Under Investigation

Carried over from the original author's notes (translated); log excerpts are quoted verbatim from runtime output.

## Issue 1: Card rewards — extra card group after combat/scavenge

**Desired behavior:**
- The post-combat card reward page shows "Add a card to your deck"
- It should offer an *additional* group (a pick-1-of-3 from the other character's card pool)

**Current state:**
- The log reports "卡牌奖励已添加额外组" (extra card group added)
- But no extra group actually appears

**Root cause analysis:**
- The code uses `combatRoom.AddExtraReward(otherPlayer, extraReward)`
- But in `RewardsSet.cs` (~line 58), extra rewards are fetched **by player**:
  ```csharp
  if (Room is CombatRoom combatRoom && combatRoom.ExtraRewards.TryGetValue(Player, out List<Reward> value))
  {
      Rewards.AddRange(value);
  }
  ```
- Reward iteration runs as `currentPlayer`, but the extra reward was registered for `otherPlayer`
- So `currentPlayer`'s `ExtraRewards` never contains the extra group

**Fix direction:**
- Don't use `AddExtraReward`
- Either add cards from the other character's pool directly into the `CardReward`'s `_cards` list, or patch `RewardsSet` to support cross-player extra rewards

---

## Issue 2: Treasure chest deadlock

**Desired behavior:**
- In a chest room, after one character picks, the other should receive their reward directly without choosing

**Current state:**
- The `OnPicked` patch fires and logs "本地双人模式已自动补齐宝箱投票" (auto-completed chest vote)
- Then it errors: `Attempted to pick relic while relic picking is not active!`

**Root cause analysis:**
- After `OnPicked`, the mod calls `AwardRelics()` and `EndRelicVoting()`
- That sets `_currentRelics = null`, ending relic picking
- But the UI still lets the second player click another relic index — picking has already ended

**Fix direction:**
- Auto-vote for the other player on `OnPicked` (already done), but do **not** immediately call `AwardRelics`/`EndRelicVoting`
- Let the original logic run and award once all players have voted

---

## Issue 3: Potion bar — purchase and display problems

**Desired behavior:**
- Potions always belong to slot 1 (primary player)
- Potion bar gains 2 extra slots
- The bar is always usable and correctly shows slot 1's potions

**Current state:**
- Slot 2 buying a potion doesn't deduct gold, and the potion isn't actually acquired
- Slot 1's inventory gains a phantom potion occupying a slot
- The UI doesn't display it and it can't be used

**Root cause analysis:**
- `NPotionContainerPatch` is currently disabled, so the potion bar display is broken
- It needs to be restored and changed to always display the primary player's potions

**Fix direction:**
- Re-enable `NPotionContainerPatch`
- Bind the potion bar to the primary player; find the bar's initialization logic and pin it there

---

## Issue 4: Using potions in combat

**Desired behavior:**
- Multi-character mode should allow choosing a potion *target* (which character receives it)
- Buff potions should default to the currently controlled character, not always slot 1

**Current state:**
- Potions are pinned to slot 1
- No target selection in combat

**Fix direction:**
- Potion-use logic needs target selection support
- Buff potion effects should apply to the currently controlled character

---

## Related files

- `Scripts/Patch/CardRewardPatch.cs` — card reward patch
- `Scripts/Patch/TreasureRoomRelicSynchronizerPatch.cs` — treasure chest patch
- `Scripts/Patch/NPotionContainerPatch.cs` — potion bar patch (currently disabled)
- `Scripts/Patch/PlayerPotionMirrorPatch.cs` — potion ownership patch
- `src/Core/Rewards/RewardsSet.cs` — reward generation (decompiled reference)
- `src/Core/Rooms/CombatRoom.cs` — extra reward storage (decompiled reference)

## Log reference

```
// Card reward — added but not shown
[INFO] [LocalMultiControl] 卡牌奖励已添加额外组: currentPlayer=76561198388115947, otherPlayer=76561198388115946

// Treasure chest — deadlock after vote auto-completion
[DEBUG] [TreasureRoomRelicSynchronizer] Player ... picked relic at index 0: RELIC.PRAYER_WHEEL
[INFO] [LocalMultiControl] 本地双人模式已自动补齐宝箱投票（随机），按简化随机宝箱流程结算。
[DEBUG] [TreasureRoomRelicSynchronizer] Relic index 1 () is being picked by local player ...
ERROR: System.InvalidOperationException: Attempted to pick relic while relic picking is not active!

// Potions
[INFO] [LocalMultiControl] 药水已固定归属1号位: FIRE_POTION, from=76561198388115947, to=76561198388115946
[WARN] [LocalMultiControl] 跳过药水动画：当前视图不存在药水 FIRE_POTION
```

---

## 实机反馈待办（2026-09-08，用户拍板：先记录、暂不修）

> 都是「能用但别扭」的**多人规模化**体验问题，数据层未见错误；先留档排期，本轮不动代码。
> 同类既有备案：`maintenance-docs/decision-records/瓦库托管优化可行性分析.md` §16.2。

### BUG-1 战斗第一回合能量不同步（新增，2026-09-08）

- **现象**：进战斗前选中的是**瓦库托管角色**时，进入战斗后第一回合——手牌等内容显示的是真人玩家
  （同时也是战斗开始时的默认第一个玩家），**能量条却是瓦库的**；手动切换一下角色即恢复同步。
- **初判**：战斗开始时「前台 / 控制上下文」与「能量 UI 归属」不是同一份状态源——手牌按战斗开始的
  默认玩家渲染，能量按当前托管上下文渲染；切角色会重刷两者所以自愈。与 §16.2 同源（前台上下文切换层）。
- **排查入口**：`CombatManager.SetupPlayerTurn`、本 mod 的 `CombatManagerTurnHookForegroundPatch`
  （回合开始 hook 前切前台）、能量 UI 的归属刷新（战斗 UI 能量条 / `PlayerCombatState` 能量同步）。
- 待确认：能量**数值**本身是否也错（数据层），还是仅 UI 串了（表现层）。
- ✅ **2026-09-10 r96 已修并部署（待实机复测）**：根因 = 能量球与手牌分属两个玩家；
  按不变量「能量球必须与当前展示的手牌同属一个玩家」收口——新纯函数
  `CombatEnergyOwnership.TryResolveMismatch` + 手牌归属追踪 `_lastCombatUiPlayerId`
  （入战先记原版 `NCombatUi.Activate` 那一版）+ 入战延迟刷新改按手牌归属 +
  `SetupPlayerTurn` 前缀校正两次（切前台前后各一次）。校验日志：
  `战斗能量归属不一致已校正: 能量=…, 手牌=…, 受控=… → 重建为 …`、
  基准日志 `入战战斗UI归属已记录` / `战斗UI已刷新到当前角色`。marker **r96**，344 单测全绿。
- ⚠ **r96 实测未修复 → r97 再修（2026-09-10）**：r96 日志实证两处错误——
  ① 入战刷新的 `CombatManager.IsInProgress` 门禁在 `OnCombatSetUp` 时还是 false（战斗真正开始在其之后），
  三次调用全部空转，能量球从未重建（放宽为 `ActiveCombat` 亦可刷新）；
  ② 归属基准取了入战瞬间的 `LocalContext`（=瓦库），而真实手牌是开战后按**当前受控玩家**抽出来的，
  导致"基准=瓦库、能量=瓦库"被判成一致、一次校正都没触发 →
  改为**直接读手牌区实际卡牌的持有者**（`NCardHolder.CardNode.Model.Owner`），空手牌才退化到受控玩家，
  并删掉不可靠的 `_lastCombatUiPlayerId` 追踪。另加帧末延迟补校 + 每场一条
  `战斗能量归属核对: 能量=…, 手牌=…(cardOwner/emptyHand), 受控=…`。marker **r97**。
- ✅ **BUG-1 已闭环（2026-09-10，用户实机确认）**：r97 能量同步 + r99 辉星（储君第二资源）正常显示。
- ✅ **r97 能量已实机确认同步（2026-09-10）**；**辉星（储君/Regent 第二资源 Stars）不同步 → r98 已修（待复测）**：
  ① 入战切人提前到 `NCombatRoom._Ready` **前缀**（新 `NCombatRoomReadyForegroundPatch`，Combat 域已登记）——
  原版 `NCombatUi.Activate` 与第三方按 LocalContext 绑定本地玩家的 mod（本机装有
  RegentFX「万象辉星」，dll 内可见 `NCombatRoomReadyPatch`/`SetupStarRingForLocalPlayer`）
  都在旧时机之前就绑完了；前缀一定早于任何 mod 的后缀。只处理 `ActiveCombat`。
  ② 辉星纳入同一条不变量：`EnsureCombatEnergyMatchesHand` 同时核对辉星，能量一致时也会单独校正
  （新日志 `辉星归属不一致已校正`，核对行加 `辉星=` 字段）。
  ③ 修原版 `NStarCounter` 订阅死角：`Initialize` 只有 `!_isListeningToCombatState` 才订阅 `StarsChanged`，
  该标志无人复位 → 重绑改为「先退订旧玩家 + 复位标志」。marker **r98**。
- 🔧 **r98 实机证明归属本来就对 → 真根因是生命周期，r99 已修（待复测）**：r98 日志
  `战斗能量归属核对: 能量=326, 辉星=326, 手牌=326(cardOwner)` 全对、无校正 WARN；
  用户复测「**单人储君正常，本地多控下无论入战时是不是瓦库都不显示辉星**」。
  根因：原版把辉星计数器 `Reparent` 进能量球，而本 mod 每次切角色都**重建能量球并 QueueFree 旧的**
  → 辉星作为旧球子节点被一并带走（单人走原版路径，所以正常）。
  修法 `EnsureStarCounterDisplay`：辉星与能量球**解耦**——直接从 `star_counter.tscn` 实例化、
  挂在**战斗UI**下（场景原始父节点/设计位置），每次切人重绑 + 显式显隐 + 一条
  `辉星计数器就绪: player=…, visible=…, parent=…, pos=…` 诊断。marker **r99**。

### BUG-2 真人先结束回合后，切到瓦库点结束回合无效（2026-09-08 备案；2026-09-10 r104 已修，✅ 已实机确认）

- 已记录于 `瓦库托管优化可行性分析.md` §16.2 第 1 条（结束按钮状态机绑定前台）；用户 2026-09-08 再确认。
- 现状绕法：切回自己 → 再切到瓦库 → 点结束回合才生效。
- 🔧 **r104 修法（2026-09-10）**：根因 = **按钮归属取错来源**。原版 `NEndTurnButton.CallReleaseLogic`
  用 `LocalContext.GetMe(...)` 判定「结束谁的回合」，而本 mod 的 `LocalContext` 会为**瓦库后台出牌的动作归属**
  临时漂移；漂移到「已结束回合的角色」上时，点击被当成「撤销结束回合」（本 mod 补丁还会直接拦掉）→ 点了没反应。
  三条一起收口：
  ① **点击目标改取前台玩家**：新增 `LocalMultiControlRuntime.AlignLocalContextToForegroundForEndTurn()`，
  在点击瞬间把上下文校正到前台（`Session.CurrentControlledPlayerId`），后续原版逻辑即结算玩家正在看的角色；
  ② **按钮自愈**：新增 `ReconcileEndTurnButtonForForeground()`（战斗逐帧调用、内部 500ms 节流，判定用纯函数
  `EndTurnButtonReconcilePolicy.ShouldReconcile`）——前台角色可操作却按钮处于禁用/隐藏时重评一次
  （原版按钮只在 `TurnStarted`/`PlayerEndedTurn` 切状态，自动切人/瓦库自动结束回合会绕开它们）；
  ③ **文字与目标玩家对齐**：切到瓦库后不再残留「撤销结束回合」文案。
  新增诊断 `结束回合点击门禁: target=…, foreground=…, context=…, state=…, inputEnabled=…, focused=…, handMode=…, inPickFlow=…`。
- **验证步骤**：`marker=2026-09-10-r104` + `INIT_OK`；真人先结束回合 → 自动/手动切到瓦库（瓦库还有牌可出）→
  **第一次**点结束回合即生效；日志应有 `结束回合点击门禁` 或 `结束回合按钮自愈`（按钮曾被留在禁用态时）。
  回归：瓦库回合正常自动结束、真人回合结束按钮文字正确（未结束时 END TURN、结束后 UNDO）。
- ✅ **已闭环（2026-09-10，用户实机确认「测试过了确实没问题」）**。

### BUG-3 事件选项角标把「投票角色头像」挤到很右边（新增，2026-09-10，不影响游玩，先记录不修）

- **现象**：多人（本地多控）事件里选了某个选项后，该选项按钮上会出现**投票玩家的角色头像**
  （原版 `NEventOptionButton.PlayerVoteContainer` / `NMultiplayerVoteContainer`，头像= `player.Character.IconTexture`）；
  开着统计角标（`statBadge`）时，这些头像被「顶」到选项按钮很靠右的位置。
- **已核对的底层事实**（sts2src）：
  - 按钮场景 `scenes/events/event_option_button.tscn`：根 Control `custom_minimum_size = (800,100)`，
    `PlayerVoteContainer` 是**普通子节点**（`layout_mode = 0`，offset `532,75 → 781,105`，宽 249 高 30，`alignment = 2`=END 右对齐）。
  - `NMultiplayerVoteContainer.RefreshPlayerVotes` 给每个投票玩家 `AddChildSafely` 一个
    `ui/multiplayer_vote_icon`（`TextureRect`），由父容器排布；`alignment=END` → 图标从右往左排。
  - 我们的角标（`LocalStatBadgeUi.StatBadgeOverlay`）是**树根顶层 overlay，不是按钮子节点**，
    理论上不该参与按钮内部排布 —— 所以「顶开头像」的机理还不清楚，待复现确认。
- **待复现时收集**：① 角标位置档位 `statBadgeCorner`（默认左下；若当时在**右下**，角标矩形
  x≈728~792 / y≈75~95 与投票容器 x 532~781 / y 75~105 **正好重叠**，最可能就是这个）；
  ② 头像数量（玩家数越多、END 对齐越容易整体偏右溢出）；
  ③ 关掉 `statBadge` 后头像位置是否回正（用于确认因果，而不是「多人本来就这样」）。
- **修法备选**：① 角标档位落在右下时，对事件按钮目标做「避让投票容器」的横向偏移（纯函数
  `WakuuStatBadgeLayout.Resolve` 增参数，可单测）；② 事件角标改用按钮矩形**左侧**固定偏移，
  彻底避开右下投票区；③ 若确认是 overlay 每帧写 `Size/Position` 触发了按钮重排，则改为
  只画不写（CanvasItem `_Draw` 自绘）。

### BUG-4 第三方「次级资源」战斗UI切角色后仍显示（蕾克拉，2026-09-10 已修 r100，待复测）

- **现象**：LexNinja2 的**蕾克拉**在蕾忍角色上正常显示在能量旁边，但**切到其它角色后依旧显示**。
- **定性**：蕾克拉走 **RitsuLib 次级资源框架**（`[NodeAttachment] LEX_NINJA2_NODEATTACHMENT_LEX_KELA_COMBAT_COUNTER:
  NCombatUi -> NSecondaryResourceCounter`）。反编译 `SecondaryResourceCombatUiStateTracker` 实证：
  它**只在 `CombatStateChanged`** 时 `UpdateCombatUi(parent, LocalContext.GetMe(state))`——
  「本地玩家」在原版联机语义下是固定的；本 mod 切前台不触发该事件 → 计数器停在旧角色
  （`Refresh` 里的 `_hasBeenMaterial` 粘滞标记又让它不会自己消失；公共 `Bind(player)` 在玩家变化时会复位它）。
- **修法（r100）**：`Scripts/Runtime/LocalThirdPartySecondaryResourceBridge.cs`（**全反射 + 缓存 + try/catch**），
  每次重建战斗UI（切角色/入战）后调用 RitsuLib 公共入口
  `SecondaryResourceUiRuntime.UpdateCombatUi(NCombatUi, Player)`；未装 RitsuLib / 失败只记一次日志并跳过。
  成功日志 `第三方次级资源战斗UI已同步到当前玩家: player=…`。
- **验证**：切到非蕾忍角色 → 蕾克拉消失；切回 → 恢复。
- ⚠ **r100 残留场景 → r101 补强 → r102 改对根因（2026-09-10，待复测）**：蕾忍是瓦库 + 非 1 号位 +
  入战前停在瓦库视角时，进战斗后蕾克拉仍在，直到瓦库打完牌才自己消失。
  **r101 实机日志推翻粘滞假设**：入战时计数器本来就绑在 1 号位且已隐藏
  （`counters=1, bound=326, material=False, visible=False`）→ 是**之后**被改回瓦库的。
  真根因：RitsuLib 把「本地玩家」等同于 `LocalContext.GetMe`，而本 mod 的 `LocalContext.NetId`
  会为**瓦库后台出牌的动作归属**漂移 → 瓦库出牌期间 RitsuLib 自带刷新把计数器重绑到瓦库（显示），
  出牌结束上下文回真人 → 下次状态变化又隐藏。
  **r102**：新增 `SecondaryResourceCombatUiOwnerPatch`（ThirdParty 域，反射字符串目标
  `...SecondaryResourceUiRuntime:UpdateCombatUi`，前缀把归属强制为**前台玩家**；
  新增 `LocalMultiControlRuntime.TryGetForegroundPlayer()`，只认 `Session.CurrentControlledPlayerId`），
  r101 的桥也统一改用同一前台来源。marker **r102**。
- ✅ **BUG-4 已闭环（2026-09-10，用户实机确认）**：r102 起蕾克拉随前台正确显示/隐藏。

### BUG-5 进战斗后第一次切角色被弹回自己（2026-09-10 已修 r103，✅ 已确认）

- **现象**：战斗开始前停在瓦库视角 → 进战斗后**第一次**切角色只看到一次刷新动画、人还在自己身上，
  第二次才切到瓦库。
- **根因（r102 日志实证）**：不是"切换顺序差一位"，第一次热键确实切到了瓦库
  （`切换操控角色(指定): 326 -> 327` + `战斗UI已刷新到当前角色 327`），
  紧接着被 `source=wakuu-no-playable-cards-tick` 的自动化**弹回**（`TryAutoSwitchFromWakuuWhenAllWakuuNoPlayableCards`）。
  该自动化**每回合一次**（弹回后登记 `_wakuuToNonWakuuSwitchedRounds`），所以第一次弹回把名额用掉、第二次才停住。
- **修法（r103）**：`NoteManualSwitchToWakuu(source)` 挂在三个切人入口之后——手动来源
  （`hotkey*` / `player-state*`）切到瓦库、且该角色当前**确实没有可出的牌**时，本回合直接登记为已处理；
  有牌可出时不登记（不白占名额，免得削弱「瓦库打完把视图交回真人」的自动化）。
- **验证**：第一次切角色即停在瓦库；日志 `手动切到瓦库角色，本轮不再因「无牌可出」自动切走`。
- ✅ **已闭环（2026-09-10，用户实机确认）**。

### BUG-6 两个角色都带【工具箱】时，非前台那位被静默自动选卡（2026-09-10 r106 已修，✅ 已实机确认）

- **现象（2026-09-10 用户实机反馈）**：控制台给两个角色各塞了一堆「战斗开始/回合结束时选择卡牌」的遗物后，
  【工具箱】只有**前台那位**被问到、可以选；**另一位没被问，却照样自动拿到了工具箱该给的无色牌**。
- **日志实证（marker r105）**：
  `工具箱自动接管已命中: player=…327, reason=background-player` →
  `工具箱已自动选择首张卡: player=…327, card=RALLY`。
- **根因**：`Scripts/Patch/ToolboxPatch.cs` 的 `ShouldAutoPickFirstCard` 里有一条
  `isBackgroundPlayer = !LocalContext.IsMe(player)` → 非前台的玩家一律"自动选首张"。
  这是 **d2c2c31（2026-03-21「升级工具箱接管逻辑并绕过后台三选一阻塞」）** 加的临时手段；
  2026-08 之后后台选牌链路已经成熟（`CardSelectForegroundSwitchPatch` 切前台交真人 +
  `ShouldSelectLocalCard` 强制本地手选；b949dfa 实证「作用域外选牌必须切前台交真人，
  自动作答会导致进战斗黑屏」），这条自动选卡就变成**把真人的选择静默吃掉**了。
- **修法（r106）**：`ShouldAutoPickFirstCard` 只对**瓦库托管角色**接管（`IsWakuuEnabled`）；
  真人（包括此刻不在前台的另一位）走原版 `FromChooseACardScreen` → 自动切前台 → 真人自己选。
- **验证**：`marker=2026-09-10-r106`；两个角色都带工具箱进战斗 →
  两位都各自弹三选一（后台那位会先自动切前台，日志 `... source=combat-choice-FromChooseACardScreen`）；
  **不应**再出现 `工具箱自动接管已命中: reason=background-player`；瓦库角色的工具箱仍自动选首张
  （日志 `reason=wakuu-player`）。
- ✅ **已闭环（2026-09-10 实机日志）**：`FromChooseACardScreen` 对后台角色正常切前台（×2）；
  `工具箱自动接管已命中` / `工具箱已自动选择首张卡` 均为 **0 条**。
  用户拍板：**真人一律弹界面自己选，暂不做「非前台真人自动选」开关**。

### BUG-7 手牌「排序/动画」与数据不同步（2026-09-10 用户实测发现；**2026-09-11 r112 ✅ 已实机确认，关单**）

- **现象**：牌面内容都对，但**手牌顺序**与数据不一致（「数据链」只把**相邻**的牌变成数据链，
  所以错位非常明显）；**切换一下角色**（重建手牌 UI）就同步回来。
- **暴露时机**：r109 修好「瓦库打手牌变换牌会中断」之后才显形 —— 之前那一步直接抛异常中断整个出牌，
  现在数据层正常生效、只是视觉没跟上（r59/r109 都**故意**让后台角色的手牌变换跳过视觉：
  `IsMine=false`，因为前台手牌区根本没有它的牌）。
- **日志证据（marker r109，本局 3 次）**：
  `[手牌同步修复] 后台角色手牌变换：临时让开 NetId 以跳过前台动画查找: owner=…327, controlled=…326, netId=…327 -> …326`
- **机理（r111 已按源码核对修正）**：原假设「数据层把替换牌加到**手牌堆尾部**、顺序真的会变」**不成立** ——
  `sts2src/src/Core/Commands/CardCmd.cs:438-448` 对非 Deck 牌堆是
  `pile2.AddInternal(replacement2, item4)`，`item4` = 原牌索引；`CardPile.cs:83-97` 在 `index >= 0` 时
  `_cards.Insert(index, card)` → **数据层保序**。真正会脱节的是「屏幕上显示的手牌属于**非前台**角色」的窗口
  （切人后 `ScheduleDeferredCombatUiRefresh` 的延后重建窗口）：此时后台角色变换跳过视觉（r109
  `ShiftAwayFromOwner`）→ 这份手牌 UI 的顺序/内容不跟随数据，切一次角色（整表重建）就恢复。
- **修法（r111 → r112 收敛，按不变量收口）**：*显示的手牌 UI 顺序必须等于「前台角色手牌堆」的顺序*。
  ① 纯函数 `HandUiOrderPolicy.Decide(pileOrder, uiOrder)`（`Scripts/Runtime/PureLogic/HandUiOrderPolicy.cs`，
  多重集比较、同名重复牌按次数算）→ **只剩 `None` / `Reorder`**：只有「同一批牌、仅顺序不同」才重排，
  任何「多余 / 缺失」一律 `None`；② `LocalMultiControlRuntime.ReconcileDisplayedHandOrder(source)`：
  守卫「选牌/拖牌/出牌/有牌等待打出」一律不管；顺序不同 → `MoveChildSafely` 按数据重排 +
  `ForceRefreshCardIndices()`（**只动次序，不建节点、不删节点、不触发重建**）；陈旧显示/多余/缺失 → 纯诊断，
  同一差异持续 ≥1.5s 才记一条 WARN；③ 调用点 = `CardTransformNetIdPinPatch` 变换异步收尾（下一帧）+
  `LocalCombatSwitchTracker._Process`（战斗逐帧，250ms 节流）。+11 单测 → **399 全绿**，marker r112。
- ⚠ **r111 的教训（已修，务必别回退）**：r111 曾把「UI 多出陈旧节点 / 两边都缺」升级为「清多余节点 /
  整表重建」，实机立刻回归 —— **原版手牌变换的视觉更新故意延迟约 0.9s**
  （`NCardTransformShineVfx.PlayUntilCardUpdate` 先等 `0.75+0.125` 秒才 `UpdateCard`），
  这段窗口里「数据已是新牌、UI 还是旧牌」是**正常动画态**，据此整表重建会在出牌结算途中
  `hand.CancelAllCardPlay()` + 全量重建 → **打出的数据链停在屏幕中间不消耗**。
  所以本收口**只允许重排**这一种动作。
- **验证（请实机复测）**：`marker=2026-09-11-r112` + `INIT_OK`；
  **回归复测**：反复打「数据链」/「不等价交换」→ 牌正常结算消耗，**不再停在屏幕中间**，
  且**不应**再出现 `手牌UI与数据整体不一致，触发整表重建` / `战斗UI刷新顺延到选牌/出牌流程结束后`；
  **BUG-7 本体**：手牌顺序与数据一致，真出现顺序错位时应能看到
  `手牌UI顺序已按数据自愈: player=…, 重排=N, 前=[…], 后=[…]`。
  诊断（正常局几乎没有）：`手牌UI与数据存在差异但未处理（仅记录）: …多余=[…], 缺失=[…]` /
  `手牌显示归属与前台不一致（仅记录，重建由切人链路负责）` —— 有就把日志给我，它们指向真正该修的地方。
- ✅ **已闭环（2026-09-11，marker r112 实机确认）**：`手牌UI顺序已按数据自愈` 命中 **3 次**（均 `重排=1`），
  前后对比实证「数据把新牌插在原位、UI 把它排在末尾」——例如
  `前=[…,DEFEND_SILENT,DEFEND_SILENT,DATA_LINK] → 后=[…,DEFEND_SILENT,DATA_LINK,DEFEND_SILENT]`；
  同时 `整表重建` / `战斗UI刷新顺延` 均 **0 条**（r111 那局是 5+5，14 次数据链不再卡牌），
  诊断项 `存在差异但未处理` / `显示归属与前台不一致` 均 0 条（无误报）。
  用户结论：「我的视角和瓦库视角都没有什么问题」。**BUG-7 关单**。
- **优先级**：不影响游玩（不阻塞、数据正确）。

### BUG-8 瓦库自动出牌作用域被游戏侧 `OrbQueue is full` 打断（2026-09-11 实机日志发现；**暂不修，已记录**）

- **日志（marker r110，L9954 / L9957）**：
  ```
  [WARN] 瓦库选择器作用域异常退出: player=…327, round=2, error=OrbQueue is full
  [WARN] 瓦库看门狗重启失败:        player=…327, source=combat-watchdog, error=OrbQueue is full
  ```
- **根因：不是我们抛的，是游戏原生异常**。`sts2src/src/Core/Entities/Orbs/OrbQueue.cs:57-59`
  → `if (Orbs.Count >= Capacity) throw new InvalidOperationException("OrbQueue is full");`
  而 `OrbCmd.Channel`（`sts2src/src/Core/Commands/OrbCmd.cs:80-84`）的流程是「队列满了先 `await EvokeNext()`
  挤出一个腾位置，再 `TryEnqueue`」——**第二步仍会抛**，这就是那个窗口。
- **归因：由真人自己触发**。异常前约 20 行实锤：
  `9886 Enqueueing …CARD.IGNITION (38153845) … from owner …326`（**真人**在打一张充能 orb 的牌）、
  `9937 Asset not cached: res://scenes/orbs/orb_visuals/plasma_orb.tscn`
  → 异常冒泡到瓦库的自动出牌作用域，被我们的 catch 记下。
- **影响**：本次自动出牌 pass 中断；但 L9962 起看门狗已 `重新进入全量出牌模式 round=2` ——
  **自愈、未软锁**，整局 3 个回合仅 1 次。
- **决策（2026-09-11 用户拍板「先不管它」）**：**暂不改代码、也不降级日志**，避免掩盖真实的中场中断。
- **将来若要处理，可选路线**：① 归类为「必定自愈的可恢复游戏侧异常」→ 降到 INFO + 干净重启，
  但**保留计数**不丢信息；② 深挖上下文漂移：同局另有 **3 次**
  `检测到手动出牌上下文漂移，已强制校正: 327 -> 326, source=card-enqueue-manual-play`，
  怀疑 orb 归属可能受「上下文短暂钉在瓦库身上」影响 → 顺 `OrbCmd.Channel` 的 `player` 参数来源追。
- **优先级**：低（偶发、自愈、不影响数据）。

### BUG-9 SL（读档）后个人记录的抉择未回滚 → 选择率被污染（2026-09-11 用户反馈；**r118 已修，待实机**）

- **现象（用户 2026-09-11）**：「本地事件选项选择率记录好像有点问题？比如说我 **SL** 了它还是会记录我
  **SL 前的选项**」。
- **根因（代码级，成立）**：记录**不随游戏存档回滚** —— 真人一点事件选项就立即
  `_store.eventChoices.Add(...)` + `Save()` **落盘**（`LocalPersonalRecorder.RecordHumanEventPage`），
  而 `runKey = 种子 + 玩家数` 是**跨存档稳定**的（注释里就写着"跨存档稳定"）。
  只有 `RecordRunEnded(isAbandoned: true)` 才按 runKey 删数据，**SL 不触发**。于是：
  ① 同一事件页被**重复记多行** → `Offered`/`Chosen` 被放大（`CountEventOptionSlice` 是逐行累加）；
  ② SL 前点过、SL 后**改选**的选项仍残留 `chosen=true` → 继续参与选择率统计（用户看到的就是这条）。
  （注：`CountEventWinSlice` 按 runKey 去重，所以**胜率**没被放大，只有**选择率**被污染。）
- **修法（r118）**：**按存档点回滚**。
  1. 纯逻辑 `Scripts/Runtime/PureLogic/WakuuPersonalRollback.cs`：`RollbackAfter(store, runKey, cutoffUnixMs)`
     —— 丢弃该 runKey 中 `ts >` 存档点的行（四张表同一口径；边界取严格大于 = 存档当刻的视为已固化），
     返回各表丢弃数（+6 单测 `WakuuPersonalRollbackTests`）。
  2. 运行层 `LocalPersonalRecorder.RollbackToLastSavePoint(source)`：从
     `current_run_mp.save` 的**最后修改时间**取"上一次存档点"（`GodotFileIo.GetLastModifiedTime`，
     游戏自己也是这么读存档时间的），调上面那个纯函数；**取不到存档点就保守不删**（只 WARN）。
  3. 时机：`LocalMultiControlRuntime.OnRunLaunched`（进局/读档的公共入口）→ 日志
     `个人记录-读档回滚: source=run-launched, run=…, 存档点=…, 丢弃 events=N, …`（只在真丢弃时打）。
  4. 语义：**晚于存档点 = 会被重玩的那一段 → 丢弃；早于等于存档点 = 已随存档固化 → 保留**，
     所以「隔天继续游戏」不会误删真实记录；新开局时 runKey 不同（或存档已被清）→ 天然删 0 条。
- **已知边界**：若 SL 是**手工复制/覆盖存档文件**（而不是游戏内"退到主菜单→继续游戏"），
  存档文件 mtime 会变成复制时刻 → 我们的判据会偏晚、可能删不到东西。这种情况需要另加机制，
  **待用户确认 SL 方式**（见验证步骤第 3 条）。
- **历史污染**：已经写进 `personal_stats.json` 的重复行**无法自动区分**（与合法重复不可辨），
  如需干净重来可备份后删除该文件。
- **门禁**：0 警告 0 错误、444 单测全绿（+6）、`clr_compat_check` PASS、marker **r118** 已部署且 `dll_check` 字节一致。
- ⚠ **r118 实机发现回滚根本没执行（r119 修）**：r118 日志里 `source=run-launched` 出现过 2 次
  （第 2 次就是 SL 读档），但 `个人记录-读档回滚` **一条都没有** —— 回滚在 `runKey == null` 处**静默返回**了：
  原实现用 `RunManager.Instance.DebugOnlyGetState()` 取 runKey，而 `OnRunLaunched` 那一刻全局 state **还没就绪**。
  **r119 修法**：改为直接用调用方传入的 `RunState` 构造 runKey（新增 `BuildRunKey(RunState)` 供复用，
  `CurrentRunKeyOrNull` 一并改走它），并且**无论是否丢弃都打一条**
  `个人记录-读档回滚检查: source=…, run=…, 存档点=…, 丢弃 events=N, cards=N, shop=N, removals=N, total=N`
  （避免"没生效"与"没东西可删"再次无法区分）。marker **r119** 已部署（sha256 `c5aa4b27...`）。
- **抓牌（卡牌 offer）同样被 SL 污染，且已被同一机制覆盖**：`cardOffers` 与 `eventChoices` 一样是
  真人点选时**立即落盘**、`runKey` 跨存档稳定 → 抓取率 `PickRate = Picked / Offered` 会被放大。
  r118 的回滚本就是**四张表同一口径**（`eventChoices`/`cardOffers`/`shopPurchases`/`cardRemovals`），
  所以 r119 修好取 runKey 之后**事件与抓牌一起生效**，无需另做。
- ✅ **r120：思路修正（用户判断正确）—— 放弃"按存档点回滚"，改为"写时幂等"**。
  r119 实机日志显示回滚**跑起来了但 `total=0`**（一次都没删）：两次进局的存档点只差 13 秒，
  且恰等于**读档那一刻** → **读档/进局动作本身会重写存档文件**，存档文件 mtime 永远 ≥
  最近一次抉择的时间，`ts > mtime` 恒为假。**根因是"从外部猜出游戏回滚到了哪个状态"这件事本身不可靠。**
  **新思路（r120 已实现）**：不猜、改成**幂等** —— 每次写入前先删掉"同一抉择标识"的旧行，只保留最后一次。
  标识：事件页 `(runKey, eventId)`；卡牌批次 `(runKey, batchKey)`（batchKey = 该批卡 id 去重排序拼接，
  新增 `PersonalCardOfferRecord.batch` 字段；重 roll 出不同牌 = 新批次，不误合并）；
  商店 `(runKey, act, kind, item)`；删牌 `(runKey, card)`。
  纯逻辑 `WakuuPersonalDedupe`（+10 单测，含"SL 前选 A、SL 后改选 B → 旧页整页替换、A 不再残留 chosen"）。
  r118/r119 的 `WakuuPersonalRollback` + 回滚调用**已整体删除**（基于错误前提，`dll_check` 已确认产物里没有）。
  日志可验证：`个人记录-事件选择: …, 覆盖旧页=N` / `个人记录-卡牌奖励批次: …, 覆盖旧批次=N`。
  代价（刻意接受）：同一局内**合法地**重复遇到同一事件会被合并为一次（事件一局基本不重复；卡牌按卡集合区分，
  误合并概率很低，且"最终选了啥"比"重复计数"更符合直觉）。
  门禁：0 警告 0 错误、**448 单测全绿**、`clr_compat_check` PASS、marker **r120**（sha256 `9a4a1baf...`）。
- ⚠ **r120 残留不一致（已记录，本轮未改）**：四张表的去重键粒度分别是 —— 事件页 `(runKey,eventId)`、
  卡牌批次 `(runKey,batchKey)`、商店 `(runKey,act,kind,item)`、**删牌 `(runKey,card)`（唯一没带 `act` 的）**。
  而 `PersonalCardRemovalRecord` 本身有 `act` 字段、商店那版也带了 `act`，所以
  「同一局在不同幕（或同一幕）**合法地**删两张同名牌」会被合并成一行 → 删牌偏好被少算。
  加上 `act` 既修得掉它，又**不影响 SL 覆盖语义**（SL 不会跨幕）。属低风险小修，**另开一轮做**。
- **r120 验证方法**：进事件 → 点一个选项 → 退到主菜单 → 继续游戏 → 重新点（可改选）→
  期望 `个人记录-事件选择: … 覆盖旧页=1`（**改选也应为 1**，且 SL 前那一项的 `chosen` 行不再残留）；
  卡牌奖励同理看 `个人记录-卡牌奖励批次: … 覆盖旧批次=1`。
- ✅ **2026-09-12 实机确认通过（marker r120）**：用户按上述路径复测，**日志 + 落盘数据双向验证** ——
  - 该 `(runKey, eventId)` 在此之前已被历次 SL 累积成 **8 行**，本次抉择落盘时 `覆盖旧页=8`
    → 一步收敛为 1 页（本页 2 个选项 = 2 行）；**这 8 行就是用户所报 bug 的实物证据**。
  - SL 读档后**改选**（REST → TAKE_SCULPTURE）：`覆盖旧页=2` → 旧页整体替换。
    `personal_stats.json` 里该 `(runKey, eventId)` **恰好 2 行**：
    `TAKE_SCULPTURE chosen=true` / `REST chosen=false`
    ⇒ 用户原报的「SL 前点过的选项残留 `chosen=true`」**已彻底消失**（这是外部看不出、只能查盘的一条）。
  - 前后两次 `瓦库事件按个人统计选取` 的 `offered=3, withData=2` **完全一致**
    ⇒ 统计口径跨 SL **稳定、不再被放大**。
  - ⏳ 同口径的**卡牌批次去重（`覆盖旧批次=`）本轮未复现** —— 这局没走卡牌奖励；逻辑与事件同源、
    单测已覆盖（"SL 前选 A、SL 后改选 B → 旧页整页替换"），**下次走到卡牌奖励顺带看一眼即可**。
  - 本轮复测会话**无战斗**，故 r116/r117 的战斗路径未参与（r117 早前已单独实机确认）。

### BUG-10 瓦库在「回合被强行结束」后仍继续出牌（虚空形态，2026-09-13 用户实机发现；**r122 初修（✅ 实机确认已修）→ r123 收敛为"按结束来源归因"，待实机**）

- **现象（用户原话）**：瓦库打出**虚空形态**后，"本来虚空形态打出后强行结束回合无法出牌，
  但是瓦库结束回合了也能出牌"。**与两个新开关（出牌队列实验档 / 出牌加速）无关** —— 四种组合都能复现。
- **根因（代码级）**：`VoidForm.OnPlay` → `PlayerCmd.EndTurn(owner, canBackOut: false)`
  → `CombatManager.SetReadyToEndTurn` → 该玩家进入 `PlayersReadyToEndTurn`。
  原版拦人的手段是 **UI 层**：`PlayerCmd.EndTurn` 里 `if (LocalContext.IsMe(player)) OnEndedTurnLocally()`
  → `CombatManager.PlayerActionsDisabled = true` → `NPlayerHand.AreCardActionsAllowed()` 返回 false。
  而**我们的自动出牌链路完全绕开 UI**：`TryGetAutoplayUnsafeReason` 只看全局战斗状态
  （PlayPhase / `CurrentSide` / 弹层 / 拖牌 / 瞄准），**从未检查"这一位自己的回合是否已结束"**；
  `TryScheduleWatchdog` 也只按"手上还有可出牌"就调度 → 瓦库继续出牌。
  （同一根因也覆盖更隐蔽的一条：瓦库自动结束回合后，本回合内又因别人的效果拿到可出牌 → 继续打。）
- **为什么不能用 `PlayerActionsDisabled` 当判据**：它是**全局单值**，而且 mod 自己在
  `LocalMultiControlRuntime.ReevaluateEndTurnButtonState`（L2457）里按**前台玩家**反复重算并写回它
  （r104 的按钮自愈）—— 它只代表"当前前台那位"，代表不了别的瓦库。
- **修法（r122 初版 → r123 收敛）**：拦截位置不变（出牌循环 + 看门狗调度两处），
  但判据从"只要 ready 就停手"改为**按结束来源归因**（r122 那一刀切宽了，把用户想保留的行为一起否掉了）：
  ① **位置**：出牌循环 `TryGetAutoplayUnsafeReason` 熔断收手（**真正的拦截点**，覆盖"循环中途打出虚空形态、
     回合当场结束"的同帧情况）；`TryScheduleWatchdog` 同一条 → 干脆不调度，省掉每 300ms 空转与重复 WARN。
  ② **判据**（纯函数 `WakuuTurnEndOrigin.ShouldStopAutoplay`，+9 单测）：
     - 被**卡牌效果/原版强行结束**（`VoidForm.OnPlay` → `PlayerCmd.EndTurn`）**或来源不明** → **停手**；
     - 被**模组自己收口**（`TryEndAllPlayersWhenNoCards`）且 `!AllPlayersReadyToEndTurn()` → **放行**
       （即用户要求保留的"模组收口后，本回合内又因别人的效果拿到可出牌 → 继续打"）；
     - 一旦 `AllPlayersReadyToEndTurn()`（回合马上推进）→ 一律停手，不许打进攻城窗口。
  ③ **归因来源**：`CombatManager.SetReadyToEndTurn` 的前缀补丁（`CombatManagerPatch`，本就在）登记
     "这一位的 ready 是谁造成的"；模组自己发起时用 `WakuuTurnEndOrigin.BeginModIssuedEnd()` 包裹那一次
     `PlayerCmd.EndTurn`。**外部结束优先于模组收口**（同一回合先被模组收口、后又被打出虚空形态 → 仍停手）。
- **验证方法**：① 瓦库打出虚空形态 → 期望 `… reason=player-ready-to-end-turn`，之后该瓦库不再出牌；
  ② **模组收口后拿到新牌**：瓦库无牌 → 自动收口 →（别人给牌/给能量后）→ 期望它**继续出牌**
  （日志里**没有** `player-ready-to-end-turn` 熔断行）。
- **关于"加个瓦库不自动结束回合 / 等真人结束再结束"的开关（用户 2026-09-13 提议，本轮未做）**：
  排查后的结论是**当前架构下"瓦库不自动结束"会挂死** —— 推进到敌方回合要求**所有玩家都 ready**，
  而 mod 关闭瓦库的唯一时机就是 `TryEndAllPlayersWhenNoCards`（逐帧 tick 只切视角、**不**结束回合），
  且触发源只有"真人点结束回合"与"真人结束后的兜底"两条。瓦库自己不结束 → 没人再触发 → 敌方回合永不开始。
  "等真人全部结束再收口"同理需要**新增逐帧收口**（否则最后一个真人结束那一刻若瓦库还有牌就会漏收口）。
  鉴于 r123 已用归因把用户要的行为保住，**先不加开关**；要做的话应连带补"逐帧收口"，等用户拍板。
- 门禁：0 警告 0 错误、**467 单测全绿**（+9）、`clr_compat_check` PASS、marker **2026-09-13-r123**、
  `dll_check` 字节一致（`WakuuTurnEndOrigin` 在）。**r122 已由用户实机确认虚空形态已修**。
- ✅ **2026-09-13 实机确认（r123，日志实证）**：瓦库 `…329` 在 round 1 出 3 张牌
  （`STRIKE_REGENT` / `FALLING_STAR` / **`VOID_FORM`（actionId=15，栈里有 `VoidForm+<OnPlay>`）**）后
  立刻 `瓦库自动出牌已熔断跳过本次执行: player=…329, round=1, reason=player-ready-to-end-turn, played=3`，
  该回合再无出牌 ⇒ **虚空形态这条完全符合预期**；同局 43 张牌全走动作队列（实验档）且全部完成。
- ✅ **2026-09-13 用户在 r124 上做了"对照实验"（两条都通过）**：分别给**被自动结束回合**的瓦库和
  **被虚空形态结束回合**的瓦库塞牌 —— **前者依旧能打牌**、**后者不打牌**。
  与设计完全一致：r123 的归因判据（模组收口 → 放行 / 外部强行结束 → 停手）行为正确。
  同局 `动作队列UI入队触发空引用` **0 条** ⇒ BUG-11（r124）修复也实测生效。
- ⚠ **r124 首版的观察日志判据写错了（r125 修正）**：那条 `瓦库在模组收口后继续出牌（…）` 原先只看
  `IsPlayerReadyToEndTurn`，既不校验归因、又放在闸门**之前** ⇒ 实机日志出现误报：
  `8925 走动作队列完成 … VOID_FORM` → `8926 瓦库在模组收口后继续出牌`（**误报**，其实是虚空形态造成的结束）
  → `8927 熔断跳过 … reason=player-ready-to-end-turn`。
  **r125** 把它改为：判据用新增纯函数 `WakuuTurnEndOrigin.IsModIssuedExemption`（= 闸门放行的同一条件），
  并且挪到**闸门之后**调用 —— 即"闸门已放行、下一次迭代真的要去打牌"才记录。
  **行为零变化（只动日志）**，+2 单测（豁免状态真值表）。

### BUG-11 `NCardPlayQueue.OnActionEnqueued` 对「本地非前台玩家出牌」必然空引用（2026-09-13 实机日志发现；**r124 已修，待实机**）

- **现象（日志）**：`动作队列UI入队触发空引用，已拦截避免阻塞: action=PlayCardAction card: CARD.DEFEND_IRONCLAD index: 25 …,
  context=76561198422527326` ×6（既有 fail-safe 的 WARN，600ms 限流）。
  **出牌本身不受影响**（同局 43 张瓦库出牌全部完成），用户观感也正常。
- **根因（代码级，是我们自己的软肋被放大）**：原版 `NCardPlayQueue.OnActionEnqueued` 要把"别人打出的牌"
  画进出牌队列；非本地分支取 `creatureNode.PlayerIntentHandler.CardIntent` 当飞牌起点 —— 而
  **本 mod 主动关掉了远端意图 UI**（`NMultiplayerPlayerIntentHandlerPatch` 让
  `NMultiplayerPlayerIntentHandler.Create` 返回 null）⇒ `PlayerIntentHandler` 为 null ⇒ **必然空引用**。
- **为什么现在才明显**：以前只有"本地非前台玩家手动出牌"这种偶发情况会撞；方案 D 实验档（`wakuuPlayQueue`）
  让**瓦库的出牌也走 `PlayCardAction` 入队** ⇒ 变成"每次瓦库出牌都可能撞"。
- **修法（r124）**：新增前缀守卫 `NCardPlayQueueActionEnqueuedGuardPatch` —— **先判后跳**：
  本地分支要求 `NPlayerHand.Instance != null`、远端分支要求 `creatureNode.PlayerIntentHandler != null`，
  否则 `return false`（等价于"这张牌不进 UI 出牌队列"，与原本被 Finalizer 吞掉后的可见结果一致，
  但**没有异常、没有 WARN、也没有 `AlignContextForActionOwner` 那次无谓的上下文校正**）。
  原 Finalizer 保留作最后一道网。已在 `PatchDomainMap` 登记（否则 `PatchDomainMapTests` 直接红）。
- 门禁：0 警告 0 错误、467 单测全绿（含补丁域登记哨兵）、`clr_compat_check` PASS、marker **2026-09-13-r124**、`dll_check` 字节一致。
- **验证方法**：开实验档打一局战斗 → 日志里**不应再出现** `动作队列UI入队触发空引用`。

### 改进-1 每回合开始必须逐个看完所有真人玩家的抽牌演出（2026-09-10 r105 已实现，✅ 已实机确认）

> 🔧 **r105 实现**：设置页「其 它 设 置」新增开关 **「跳过他人回合开始抽牌演出」**（配置键
> `skipTurnStartDrawAnim`，**默认关**）。开启后回合开始只保留「当前正在看的那位」的抽牌演出，
> 其他人不切前台 → 其抽牌瞬时生效（依据原版 `CardPileCmd.GetTweenForCardsChangingPiles`：
> 非本地玩家的 Draw→Hand 本就不建节点、不做补间），数据照常，切过去即见完整手牌。
> 判定抽为纯函数 `TurnStartDrawAnimPolicy.ShouldSkipSwitch`（+5 单测）；日志
> `已跳过回合开始抽牌演出（非前台玩家，改进-1）: player=…, foreground=…, round=…`。
> 只在回合开始路径生效（回合结束/弃牌不受影响）。门禁：0 警告 0 错误、357 单测全绿、marker r105。
> **验证**：开开关进战斗 → 回合开始只应看到一位的抽牌动画，其余人的抽牌不播（日志有跳过行）；
> 切到其他角色时手牌应完整；关掉开关回到原观感。

- **现象**：回合开始会依次把前台切到每个真人玩家、播完其自动抽牌动画再切下一个；2 人还好，
  **满员 12 人时要等很久**。
- **方向（未做）**：提供「跳过 / 加速他人回合开始抽牌演出」开关——后台玩家不切前台、不建抽牌节点，
  或统一缩短动画时长。注意别误伤数据层（演出与数据分离，见 §11.4 的 `CardPileCmd.Add` 相关补丁）。

### 改进-2 多瓦库串行打牌 + 视角跟着切（新增）

> 📄 **2026-09-10 方案已产出（用户拍板：只做调研+方案，未动代码）**：
> `maintenance-docs/decision-records/多瓦库并行托管可行性与方案.md`（无 git，不进 github）。
> 结论要点：① 「后台托管免切前台」已落地（本节「现象」描述部分过时，`backgroundMode` 默认开、5 处切前台点有守卫）；
> ② 真正的串行源 = 全局 `SelectorScopeGate` + autoplay 被游戏回合循环 `await`；
> ③ `CardCmd.AutoPlay` **就地执行、不走全局动作泵**，故并行瓶颈是全局静态上下文而非动作队列；
> ④ 分期建议：Phase 0 视角策略（默认**不跟随**，低风险）→ Phase 1 选择器按归属分发 → Phase 2 autoplay 与回合循环解耦（准并行）。
> ⑤ **方案 §十一（用户追问后追加）**：原版 `ActionQueueSet` 设计注释即「某玩家等自己选择时，其他玩家可自由出牌」——
> 「多人不互相卡」由「每人一条队列 + 全局 action ID 排序 + 等选择的队列被跳过」实现，**单进程内即成立**；
> 瓦库当前串行是自研 `SelectorScopeGate` + inline `AutoPlay` 的产物。推荐 **Phase 2'（方案 D）**：
> 瓦库出牌改走 `PlayCardAction` 入自有队列，顺带**天然消除全局上下文并发风险**（全局单泵串行 ⇒ 无需 AsyncLocal 化）。
> ⑥ 用户拍板：`never` 档**保留兜底切换**（防软锁）、接受 Phase 0 独立交付、**慢慢来不着急**。
> ⑦ **Phase 0 已实现（r107，2026-09-10，已部署 ✅ 2026-09-11 实机确认）**：新增「瓦库托管视角」三档
> （`wakuuViewMode`，设置页「瓦库托管」区循环按钮，**默认不跟随**）+ 纯函数 `WakuuViewPolicy.ShouldSuppressSwitch`；
> 5 处切前台点（回合开始/结束、Hook 入队、出牌前、选牌兜底）改走它；**两处防软锁兜底不受档位影响**
> （作用域外真人交互选牌恒切、安全网救援恒切）。安全网候选甄别改用与档位无关的 `IsBackgroundHostedWakuu`。
> +12 单测 → **369 全绿**，构建 0 警告 0 错误、`clr_compat_check` PASS、marker r107 已部署且 `dll_check` 字节一致。
> ⑧ **r108（实机反馈修复，2026-09-10，已部署 ✅ 2026-09-11 实机确认）**：① 用户实测「不跟随」下**仍要真人替瓦库选**
> 战斗开始遗物给的无色牌三选一 —— 根因 `CardSelectWakuuTurnStartAutoAnswerPatch` 只覆盖
> `FromChooseACardScreen`，漏了 `FromSimpleGrid`；已补同款前缀（顺带用同一判据跳过无意义切前台）。
> ② 「仅关键节点」档位**实际无效**（被同一钩子里的「改进-1」开关再拦一次）→ 改为 **peek**
> （回合开始跳过去看一眼、约 1.2s 后自动切回真人），并让改进-1 不再管辖瓦库形态角色。
> ⑬ **Phase 1 已实现（r113，2026-09-11，已部署待实机）**：**选择器按归属者分发**（方案 §4）。
> 新增纯逻辑 `WakuuOwnerSelectorMap<TSelector>`（归属者→选择器登记表，支持同归属者嵌套、乱序释放、
> 幂等释放）+ `WakuuSelectorDispatch.Decide(hasChooser, registryHit, chooserIsWakuu)`（路由真值表）；
> 运行层 `WakuuSelectorRegistry.Open(ownerId, selector)` = `CardSelectCmd.PushSelector` + 登记，
> **6 处选择器压栈点全部迁移**（战斗出牌 / 遗物效果 / 事件选项 / 火堆 / 药水 / 卡牌奖励）；
> `CardSelectCmdSelectorGuardPatch` 改走路由（瓦库命中→用它自己的选择器；真人→摘掉走 UI；未知→保持栈顶）；
> 新增 `CardSelectCmdResetRegistryPatch`（run cleanup 同步清表）；新增启动自检 `SELECTOR_ROUTE`
> （反射枚举 `CardSelectCmd.From*` 全部入口并分类，出现未分类新入口即 WARN，便于游戏更新适配）。
> **行为零变化**：三条老路（真人摘掉 / 瓦库放过 / 保持栈顶）逐条保留，只有「瓦库 + 登记表命中」这一种
> 新组合走精确分发。+21 单测 → **420 全绿**（含「入口枚举_无未分类的新入口」这条游戏更新哨兵），
> 构建 0 警告 0 错误、`clr_compat_check` PASS、marker r113 已部署且 `dll_check` 字节一致。
> ⏭ Phase 1 的收益在于**铺好路由底座**（Phase 2' 准并行后两个瓦库作用域才能各归各），本轮单独交付无可见变化。
> ⑨ **r109（新 bug 修复，2026-09-10，已部署 ✅ 2026-09-11 实机确认）**：瓦库打「手牌变换」类牌（YUI「数据链」、
> 酒狐「不等价交换」）**中断**——`Couldn't get hand node for original card CARD.INJURY`，
> 牌停在屏幕中间、效果没跑完、没消耗（本局 18 次）。根因：原版 `CardCmd.Transform` 视觉分支按
> `LocalContext.IsMine` 决定是否到**前台手牌**找原卡节点，而瓦库自动出牌期间看门狗**已把 NetId 钉在瓦库身上**
> → 判成"我的牌" → 找不到 → 抛穿异步链；r59 的守卫只覆盖「要不要钉」，漏了「NetId 已经是牌主人」。
> 修法：纯函数 `CardTransformNetIdPolicy` + 新增 `ShiftAwayFromOwner`（后台变换时把 NetId 让到当前前台）。
> +6 单测 → **378 全绿**，marker r109。
> ⑩ **r110（历史小尾巴清理，2026-09-10，已部署 ✅ 2026-09-11 实机确认）**：① **清理期异常降级** —— 退出这一局时
> 自动出牌作用域仍在飞而抛出的 `Nullable object must have a value.` 不再打 WARN、不再上抛
> （纯函数 `WakuuTeardownPolicy.ShouldTreatAsExpectedAbort`，两条日志降级为 INFO）；
> ② **磁盘历史脏值自愈** —— 加载配置时把非法的字符串策略字段（已见 `wakuuBrain=bottomRight`）
> 归一后写回盘（`LocalWakuuAutopilotConfig.TryRepairHistoricalValues`），只改一次。
> +10 单测 → **388 全绿**，marker r110。
> ⑪ **2026-09-11 实机复核（marker r110 日志实证，✅ 全部闭环）**：
> ① r110 脏值自愈 WARN **仅 1 次**，随后两个配置快照均为 `wakuuBrain=heuristic`（原 `bottomRight` 已消失），
> 第二次加载不再告警 → 「只写一次」成立；② `Nullable` 全文仅剩 **2 处且均为 BaseLib 第三方**
> （`SavedProperty…System.Nullable\`1[System.Int32]`），我们那条清理期异常**已彻底消失**；
> ③ r108 作用域外自动作答命中 **3 次**（`FromChooseACardScreen`×1 + **`FromSimpleGrid`×2**）
> → 「不再要真人替瓦库选」成立；④ r109 手牌变换 **3 次** `[手牌同步修复] … NetId …327 -> …326`，
> 且**无** `Couldn't get hand node`；⑤ 视角档 `wakuuViewMode=never` 生效，多处
> `瓦库形态后台模式，跳过自动切换视角`；⑥ `BUILD_IDENTITY commit=5c295c3 state=clean`
> → 反证部署位二进制就是那份干净提交。
> ⑫ **2026-09-11 追加：「仅关键节点 peek」✅ 也已实机确认**（本轮最后一个未验证项出清）：
> 用户切到该档后，round 1 / round 2 各出现一次完整闭环 ——
> `检测到后台角色触发战斗效果/选牌，自动切换前台: 326 -> 327, source=turn-start-setup` →
> `控制上下文已更新: 326 -> 327, source=auto-foreground-turn-start-setup` → 约 1.2 秒后
> `仅关键节点：瓦库回合开始已看过，自动切回原视角: 327 -> 326, source=turn-start-setup` →
> `控制上下文已更新: 327 -> 326, source=wakuu-peek-return-turn-start-setup`。
> 即「跳过去看一眼再自动切回」与观察一致，符合设计预期（用户原话：跳过去看一眼然后自动切回）。
> **附带确认**：该会话**没有**再次出现脏值自愈 WARN（`wakuuBrain=heuristic` 保持）→
> 证明 r110 的「每刀只改一次」在**跨会话**同样成立。
> ⑭ **方案 D 可开关实验档已落地（r121，2026-09-12，已部署待实机）**：新增开关
> **「【实验】瓦库出牌走动作队列」（配置键 `wakuuPlayQueue`，默认关）** —— 开启后瓦库出牌不再用
> inline 的 `CardCmd.AutoPlay`，而是 `new PlayCardAction(card, target)` 经
> `ActionQueueSynchronizer.RequestEnqueue` 入**该瓦库自己的**动作队列（原版 `CardModel.EnqueueManualPlay`
> 就是这一行），并**逐张 `await action.CompletionTask`**（`GameAction` 公开的"等这个动作彻底跑完"）——
> 每张牌执行完（含扣费）才做下一次决策，能量读数准确，也不会堆出一串注定被 Cancel 的动作。
> 三条语义迁移（方案 §12.2）逐条处理：
> ① `isAutoPlay` true→false —— 实验档要观察的核心差异（`VoidFormPower` / `PaelsEye` / `UnceasingTop` 等）；
> ② 目标按**原版真人出牌口径**归一：**只有 `AnyEnemy` / `AnyAlly` 传大脑解析出的目标，其余一律 null**
>   —— `PlayCardAction` 用 `CardModel.IsValidTarget` 校验，而它对"非 Any 的牌 + 非空目标"返回 false ⇒
>   沿用 AutoPlay 口径（`AnyPlayer` 会被解析成自己）会让动作被 `Cancel()` 打不出牌；
>   目标解析不到时**不构造动作**，牌留在手牌；
> ③ **删掉外层 `await SpendResources()`**（动作自己会扣，否则双重扣能量）。
> 判定收敛为纯函数 `WakuuPlayQueuePolicy`（`DecidePath` / `NeedsExternalSpendResources` /
> `ShouldUseResolvedTarget`，+9 单测）。设置页「瓦库托管」区新增该项，文案写明三条语义变化。
> **阶梯式落地**：本步只换"出牌路径"，`SelectorScopeGate` 与选择器作用域**原样保留**（回滚面最小）；
> "去掉全局闸门、让多瓦库真正重叠"留作下一步，等本步实机数据（方案 §12.7「尚未做」）。
> 门禁：0 警告 0 错误、**458 单测全绿**（+10）、`clr_compat_check` PASS、marker **2026-09-12-r121**、
> `dll_check` 字节一致（`WakuuPlayQueuePolicy` 在、`__runOriginal`/`WakuuPersonalRollback` 不在）。
> **验证方法**：开开关进战斗 → 应出现 `瓦库出牌走动作队列完成（方案 D 实验档）: …, actionId=…, ms=…`
> 与游戏自带的 `Player <netId> playing card <卡>`；关掉开关应回到既有路径（**不再出现** `走动作队列` 行）。
> **观察点**：① 虚无形态 / 佩尔之眼 / 不歇之巅等牌对瓦库出牌的统计与触发是否变化；
> ② `AnyEnemy`/`AnyAlly` 牌是否照常打得出、解析不到时是否留在手牌；③ 能量有没有被扣两次（不应）；
> ④ `AnyPlayer` 类牌（按原版口径传 null）表现是否与旧路径一致；⑤ 与「瓦库出牌加速」叠加时的观感。
> **同一轮的顺手小修**：`WakuuPersonalDedupe.RemoveCardRemoval` 的去重键补上 `act`
> （原先四张表里唯一少一层 `act` 的，会把跨幕的两次合法同名删牌合并成一行；+1 单测「跨幕同名删牌不合并」）。
> ⑮ **r122（2026-09-13，用户实机反馈后收口）**：
> ① **修 BUG-10**（虚空形态强行结束回合后瓦库仍继续出牌，见该条目）—— 判据改用**逐玩家**的
> `IsPlayerReadyToEndTurn`，加在**出牌循环**与**看门狗调度**两处；
> ② **用户实测（r121）通过项**：**开实验档、不开加速**＝正常（**不会**错误触发佩尔之眼、
> **不会**无视限制打出华丽收场 —— 说明队列路径的 `CanPlay`/目标校验是有效的）；**都不开**＝正常；
> ③ **两个都开会"加速失效" —— 属设计使然**：队列路径走 `PlayCardAction`（`isAutoPlay: false`），
> 它没有 `skipCardPileVisuals` 参数，补间与两段固定等待都走「真人出牌」分支，因此**会盖过**
> 「瓦库出牌加速」，单张回到约 1 秒。设置页文案原先误写成"与加速同向"，r122 已改正为
> 「本项会盖过加速、二者取一」，并写明想两者兼得需再给 `CardModel.OnPlayWrapper` 打前缀补丁
> （且**只能**省两段固定等待，"牌从手牌飞出"仍走 `AddDuringManualCardPlay` 真人分支，收益有限）。
> 门禁：0 警告 0 错误、**458 单测全绿**、`clr_compat_check` PASS、marker **2026-09-13-r122**、
> `dll_check` 字节一致。
> ⑯ **r126（2026-09-13，方案 D 第二步：队列路径下去掉全局闸门）**：新增第二层实验开关
> **「【实验】瓦库并发出牌（不互相等）」（配置键 `wakuuPlayOverlap`，默认关，仅 `wakuuPlayQueue` 开时生效）**。
> 三个调用点共用同一真值 `WakuuPlayQueuePolicy.IsOverlappingQueuePlay(path, overlap)`：
> ① 出牌循环不再 `await SelectorScopeGate.WaitAsync()`（**这才是"一个打完才轮到下一个"的来源**）；
> ② `TryScheduleWatchdog` 不再因 `_selectorScopeInFlight > 0` 返回 `selector-scope-busy`；
> ③ `RunWatchdogAsync` 不再把 `LocalContext.NetId`/sender 钉在瓦库身上（出牌已交给游戏全局单泵
> `ActionExecutor.ExecuteActions`，归属走 r113 的 `WakuuSelectorRegistry` 按归属者分发；全局单值被两个
> 看门狗互相覆盖无意义，且会让原版把别人的出牌当"我的牌"走前台视觉 —— r109 那类异常的来源）。
> **顺带堵掉一个真坑**：原版 `StackedSelectorScope.Dispose` 只在"自己仍是栈顶"时弹栈 ⇒ 并发档下
> 先压入先释放的那个选择器会**永久残留在全局栈**（"栈上无选择器"的判定全部失效）。
> 新增纯逻辑 `WakuuSelectorStackSurgery.RemoveByReference`（按引用、只摘一份、未命中原样返回，+7 单测）
> + 运行层 `RemoveSelectorFromStackIfPresent`（反射读 `_selectorStack`，命中则 Clear + 逆序 Push 复原），
> 无条件挂在 `WakuuSelectorRegistry.OpenScope.Dispose`（正常路径未命中 = 空操作）。
> **明确不做**：`Selector` getter 守卫与登记表不动（Phase 1 底座已够用）；`NPlayerHandSelectCardsSerializationPatch`
> 保留（§12.3 判定不能退休）；inline 路径**永不**并发（就地执行会同步触发选牌链，没闸门保护会真抢答）。
> **代价（已写进设置页文案）**：瓦库出牌的前台视觉演出更少（更接近后台托管）；视角档位「全程跟随」下
> 多瓦库会来回抢视角（建议配默认「不跟随」）。
> 门禁：0 警告 0 错误、**478 单测全绿**（+11）、`clr_compat_check` PASS、marker **2026-09-13-r126**、
> `dll_check` 字节一致（`WakuuSelectorStackSurgery`/`IsOverlappingQueuePlay`/`wakuuPlayOverlap`/
> `RemoveSelectorFromStackIfPresent` 在，`__runOriginal` 不在）。详见方案 **§12.11**。
> **验证方法**：两个以上瓦库打一回合 → 开局应出现 `wakuuPlayOverlap=True` 与
> `瓦库并发出牌：跳过全局选择器闸门（方案 D 第二步）`/`本次不钉全局上下文`；各瓦库的
> `瓦库出牌耗时` 时间窗应**互相重叠**（不再是一个的 `delayMs` ≈ 前面所有瓦库耗时之和）；
> 某个瓦库等选牌时其他瓦库/真人应能继续出牌；**不应**出现
> `并发出牌作用域释放时选择器仍在全局栈中`（出现 = 安全网在救场，请发日志）、
> `Couldn't get hand node`、`瓦库选择器作用域异常退出`、选牌被抢答。
> 关掉本项 ⇒ 与 r121~r125 完全一致（`瓦库选择器闸门等待/已进入/已释放` 照旧）。
> ⑰ **r127（2026-09-13，r126 实机反馈的收口：「真人被瓦库插队」）**：用户实测 r126 后反馈
> 「瓦库之间不再必须打完一整轮才轮到下一个（✅ 并发出牌生效），但**我打了牌要等前面出牌的瓦库的牌
> 全部生效完**才轮到我的牌」。
> **日志定性（5 瓦库局，marker r126）**：并发出牌**完全生效** —— round 1 五个瓦库的
> `瓦库回合开始→出牌启动延迟` = **32/79/81/83/83 ms**（r116 串行时代 494/6297/9724）；
> `瓦库选择器闸门` **0 条**、`选牌选择器按归属分发` 2 条、`检测到真人选牌请求` 0 条、
> `Couldn't get hand node` / `瓦库选择器作用域异常退出` / `瓦库看门狗重启失败` 全 **0 条**。
> **真问题**：`GetReadyAction` 按**全局递增 action ID** 取下一个动作（多人模式原生语义），而并发档下
> 每个瓦库都用"逐张 `await`"占着一个**已入队、在排队**的动作 ⇒ 5 个瓦库 = 队列里常驻 5 张瓦库牌，
> 真人点出的牌必然排在它们全部之后（实测：行 8784 真人入队 → **行 9019** 才生效，其间 2 张瓦库牌；
> 另一张等了 3 张）。每张瓦库牌管线耗时 ~3.2~4.0s（5 瓦库平分单泵）。
> **修法（真人插队）**：`CardModel.EnqueueManualPlay` 前缀（真人**按下**出牌那一刻）调
> `LocalWakuuRelicRuntime.YieldPendingQueuePlaysToHuman()` → 把瓦库**还在排队、尚未开始执行**
> （`GameActionState.WaitingForExecution`）的入队动作 `GameAction.Cancel()` 撤掉，真人的牌随即成为
> 队列里 ID 最小的那个。判定抽为纯函数 `WakuuPlayQueuePolicy.ShouldCancelPendingPlayForHumanPlay`
> （+1 单测）；登记表 `_pendingQueuePlays` 在动作的 `finally` 里自我摘除，`ResetTakeoverFallbackState` 整体清空。
> **安全性**：被撤的动作**从未执行过** ⇒ 不扣能量、不结算、牌仍在手牌，只是回看门狗下一轮重新决策
> （`瓦库出牌入队后被取消（牌留在手牌，交看门狗下一轮）` 会变多，属预期）；`Cancel()` 走原版自己的取消通道，
> 并发档 `LocalContext.IsMe=false` ⇒ **不会动真人的手牌**。正在执行/正在等选择的动作一律不撤。
> **顺带修正一条判据**：`并发出牌作用域释放时选择器仍在全局栈中`（r126 一局 21 条）复核确认是**预期路径**
> （两个作用域交错、先释放的已不在栈顶），**r127 已从 WARN 降级为 INFO** —— 不是异常，不必再报。
> 门禁：0 警告 0 错误、**479 单测全绿**（+1）、`clr_compat_check` PASS、marker **2026-09-13-r127**、
> `dll_check` 字节一致（`ShouldCancelPendingPlayForHumanPlay`/`YieldPendingQueuePlaysToHuman`/
> `_pendingQueuePlays` 在、`__runOriginal` 不在）。详见方案 **§12.12**。
> **验证方法**：并发档下打一场 2+ 瓦库的战斗，真人点牌 → 期望日志
> `瓦库并发出牌：真人操作优先，撤掉尚未执行的瓦库入队动作让真人插队: actor=…, count=N, 详情=[…]`，
> 且**紧接着一两张之内**就出现 `Player …326 playing card <真人那张牌>`；瓦库的牌不应丢失。
> ⑱ **r128（2026-09-13，r127 实机反馈的收口：结束回合也得插队 + "能不能一次跑多个动作"的澄清）**：
> 用户实测 r127 后反馈「**这个大概没什么问题了**」（出牌插队 ✅），但
> 「**点结束回合按钮也必须等瓦库结束才看起来生效**」。
> **日志实证（同一局）**：行 8184 真人入队 `EndPlayerTurnAction`（id 24）→ 行 8197~8406 一直是 ready action
> 但被 id 更小的瓦库动作挡着（行 8409~8411 三张 `PlayCardAction` id 25/26/27 排在后面）→
> **行 8413 才 `Executing action`**。与出牌**完全同源**：`NEndTurnButton.CallReleaseLogic`
> （`sts2src/src/Core/Nodes/Combat/NEndTurnButton.cs:329-349`）同样是 `RequestEnqueue(new EndPlayerTurnAction(...))`。
> **修法**：`NEndTurnButtonPatch.Prefix`（r104 前台校正之后）调同一条
> `LocalWakuuRelicRuntime.YieldPendingQueuePlaysToHuman(me.NetId, "end-turn-button")`；
> 同时把 r127 里那条「瓦库形态直接跳过」的防御**移到出牌调用点**（瓦库走 `RequestEnqueue`，不经
> `EnqueueManualPlay`）—— 结束回合这条**必须**对瓦库形态也生效（真人把视角停在瓦库上点结束，
> 点击目标就是那个瓦库，正是要让它立刻停下）。日志统一为
> `瓦库并发出牌：真人操作优先，撤掉尚未执行的瓦库入队动作让真人插队: actor=…, count=N, 详情=[…], source=…`。
> **答用户问「不能做成原版多人游戏那样一次跑多个动作吗？」—— 不能，原版本身也不是**：
> `ActionExecutor.ExecuteActions()`（`ActionExecutor.cs:123-197`）是**全局单泵**（`await readyAction.Execute()`
> 等它彻底跑完再取下一个）；`ActionQueueSet.GetReadyAction()`（`:172-251`）跨**所有玩家**取 action ID 最小的那个，
> 仅对"正在等玩家选择"的队列**跳过**；设计注释（`:13-19`）说的就是「某人等自己选择时**其他人照样出牌**」，
> 而非"多个动作同时执行"。⇒ 原版多人**执行仍是一次一个**，"不互相卡"只体现在等选择时队列被跳过。
> 真并行要同时踩 `LocalContext`/`CurrentlyRunningAction`/`CheckWinCondition` 时机/动画节点/确定性校验和，
> 等于重写动作执行器，**不做**。现实里能做的是 ① 真人永远不排在瓦库后面（r127+r128 ✅）
> ② 缩短单个动作耗时（队列路径每张约 1s；`CardModel.OnPlayWrapper` 前缀强制 `skipCardPileVisuals`
> 可省 0.4~0.65s/张 ⇒ 预计 ~0.35~0.5s/张，**待用户拍板**）。
> 门禁：0 警告 0 错误、**479 单测全绿**、`clr_compat_check` PASS、marker **2026-09-13-r128**、
> `dll_check` 字节一致。详见方案 **§12.13**。
> **验证方法**：并发档下点结束回合 → 期望日志里同一次点击出现
> `…让真人插队: actor=…, source=end-turn-button`，且**紧接着**就 `Executing action: EndPlayerTurnAction`；
> 结束回合/撤销、瓦库照常自动收口都要正常。

- **现象**：多个瓦库时只能「一个瓦库打完 → 切到下一个瓦库」串行进行，且真人视角会跟着切到
  瓦库正在操作的角色；瓦库多时同样很慢。
- **方向（未做）**：① 后台托管免切前台（类似单人双角色的后台模式）→ 多瓦库可并行 / 准并行推进；
  ② 视角策略可配置（不跟随 / 仅关键节点跟随）。
- **风险**：与选牌串行化、前台绑定类 UI（结束回合按钮，见 BUG-2）强耦合，
  需先解决「前台归属」的单一事实来源，否则会把 BUG-2 放大。

### 改进-3 瓦库事件选项：接上「我们自己」的选择率 + 胜率统计（2026-09-11 用户反馈；**r114 / r115 ✅ 2026-09-11 实机核对通过**）

> 🔧 **r114 实现（2026-09-11）**：
> ① `WakuuEventSignal` 扩展出 **`ChosenRate`（选择率）** 与 `WinRateSkipped`（没选它的局胜率）、
> 派生 `WinRateGain` / `HasChosenRate`（社区链无选择率 → 保持 `null`，**不用 0 冒充**）；
> ② `WakuuPersonalQuery.TryGetEventDecisionSignal` **回填选择率**（原先整条丢弃），
> 且 `SkippedRuns == 0` 时**不回填** skipped 胜率（避免用 0% 当假基准把选项算成强正增益）；
> ③ 新增纯函数 `WakuuSignalPicking.PickBestEventOptionIndex`（**口径与卡牌 `PickBestCardIndex` 对齐**：
> 主信号 = 选择率，无选择率时退化为胜率；叠加因果增益加权；**负面信号直接出局**；同分保留更靠前）；
> ④ `LocalWakuuEventAutoChoice.SelectByPersonalStats` 改走该纯函数，日志补
> `chosenRate / winRate / gain / offered / tier`；⑤ 设置页「个人统计决策辅助」文案写明
> 「事件选项按你的选择率 + 胜率选，用稳定 loc key 查表、不受界面语言影响」。
> 门禁：0 警告 0 错误、**432 单测全绿**（+12）、`clr_compat_check` PASS、marker **r114** 已部署且 `dll_check` 字节一致。
>
> **前置条件（重要）**：这条链受开关 **「个人统计决策辅助」（`personalAssist`）** 管辖，**默认关** ——
> 想让它生效必须先在设置页打开；且样本门槛是 **≥3**（`WakuuPersonalQuery.DefaultMinPersonalCount`）、
> **只统计已打完（胜/负）的局**，所以早期样本少时会回退到社区统计 → first/last/random。
> **是否把 `personalAssist` 改成默认开，留给用户拍板**（会同时影响卡牌奖励的个人统计链）。
>
> **验证要点**：开开关后进事件 → 日志 `瓦库事件按个人统计选取: event=…, option=…, chosenRate=…, winRate=…, gain=…`；
> 样本不足/全为负面时是 `瓦库事件个人统计未采用，回退社区/原策略: …`（属正常回退，不是 bug）。
>
> 🔧 **r115 诊断补强（2026-09-11，r114 实机日志暴露的盲点）**：r114 第一条实机日志里
> `personalAssist=True`、`eventChoiceMode=random`、事件自动选择 2 次，但**两条统计链一行日志都没有** ——
> 因为两条链在「本页只有 0/1 个可选项」「记录器还没有任何事件样本」「社区适配器未就绪」这些早退路径上
> **全是静默 return**，导致"没生效"与"没数据"在日志里无法区分。r115 把这些路径全部留痕：
> `瓦库事件{个人统计|社区统计}跳过（本页仅 N 个可选，无选择余地）` /
> `瓦库事件个人统计无记录（记录器还没采到任何事件选项）` /
> `瓦库事件个人统计无可用样本…各选项展示次数=[KEY:n, …]`（可直接看出"该事件从未被记录"还是"样本不够"）/
> `瓦库事件社区统计无数据…, skadaReady=…`。marker **r115** 已部署。
> （事件选项选择率/胜率逻辑本身**没有改动**，只补日志。）
>
> ✅ **2026-09-11 实机核对通过（marker r119 会话日志）**：该局「个人统计决策辅助」为**开**，
> 两位瓦库各自动选一次（两局共 6 次）**全部命中个人统计链**，且数值合规：
> `瓦库事件按个人统计选取: event=…INTEGRATED_STRATEGY_EVENTS_EVENT_SECRET_ROOM_EVENT, char=IRONCLAD,
> index=0/2, option=…TAKE_SCULPTURE, chosenRate=1.000, winRate=0.000, gain=无, offered=3, withData=2,
> tier=characterFirst`
> —— ① `chosenRate` 有值 ⇒ `TryGetEventDecisionSignal` 的**选择率回填生效**（原先整条丢弃）；
> ② `offered=3, withData=2` 与 `tier=characterFirst` 正是 r115 补的回退诊断字段 ⇒ **诊断链同样生效**；
> ③ 结论：r114（选择率参与决策）+ r115（"没生效"与"没数据"可区分）**均已在实机跑通**，
> 只剩"玩法观感是否满意"这一主观项（用户未提异议）。
> 注：`winRate=0.000` 是真实切片结果（点过它的局都输了），**不是**占位 0 ——
> 「`SkippedRuns == 0` 不回填假基准」的设计未受影响。

- **现象（用户 2026-09-11）**：瓦库的事件选项现在体感「只能随机 / 按 first-last 乱选」，
  用户问「什么时候能按选择率或胜率选」，并指出**我们自己已经在 mod 里统计了事件选项的选择率与胜率**。
- **现状核查（结论：数据我们早就采到了，是决策链漏用了选择率）**：
  - **我们已经有的数据**：个人记录器 `personal_stats.json` → `eventChoices`（事件每页每选项一行，
    带 `chosen` 标记），足以算出 —— ① **选择率** `PersonalEventSlice.ChosenRate = Chosen / Offered`
    （`Scripts/Runtime/PureLogic/WakuuPersonalData.cs:179-192`）；② **胜率** `PersonalWinSlice.WinRateHeld`
    （同文件 `CountEventWinSlice`）。查询入口 `WakuuPersonalQuery.CountEventOptionSlice`（L508-554）与
    `TryGetEventDecisionSignal`（L471-505）。
  - **但决策链只用了胜率**：`TryGetEventDecisionSignal`（L486-502）算出了 `PersonalEventSlice slice`
    却**只拿 `slice.Offered` 当门槛**，返回的 `WakuuEventSignal(optionKey, win.WinRateHeld, slice.Offered)`
    把 `Chosen` / `ChosenRate` **整条丢掉**；上层 `LocalWakuuEventAutoChoice.SelectByPersonalStats`
    （L292-364）也只比较 `signal.Value.WinRate`。→ **选择率从未参与决策**。
    与卡牌侧明显不对称：卡牌的 `WakuuCardSignal.PickRate` 在 `WakuuSignalPicking.PickBestCardIndex`
    里是**主信号**（选择率 + 因果增益），事件侧没有对应物。
  - **且事件统计这条链默认是关的**：`personalAssist`（设置页「个人统计决策辅助」）**默认关**、
    `skadaAssist`（社区统计）也**默认关** → 默认落到 `SelectByStrategy` 的 first/last/random
    （`WakuuConfigData.eventChoiceMode` 默认 `First`）。这才是「只能乱选」的**直接原因**。
  - **个人数据链的优势（比社区链可靠）**：用**稳定 loc key**（`EventOption.TextKey`）查表，
    **不受界面语言影响**、也不依赖第三方 mod；而社区链是 SkadaHelper **文本模糊匹配**，
    中文界面下大概率整体 miss（r48 已记录、日志锚点 `瓦库事件社区统计匹配: … 命中=0`）。

- **修法（小改动，纯逻辑为主，建议作为 r114 候选）**：
  1. `WakuuEventSignal` 增加 `Chosen` / `ChosenRate`；**社区侧无此字段 → 置"无选择率"**
     （不要用 0 冒充，否则会污染口径）；
  2. `WakuuPersonalQuery.TryGetEventDecisionSignal` 把 `slice.Chosen` 一并回填（不再丢弃）；
  3. 新增纯函数 `WakuuSignalPicking.PickBestEventOptionIndex`（口径与卡牌一致）：
     有选择率时按「选择率优先 + 胜率不差」综合；无选择率时退回胜率；
     胜率明显低于"未点该选项"的局（负面信号）直接出局；同值保留更靠前的选项（与"最上"兜底同向）；
  4. `LocalWakuuEventAutoChoice.SelectByPersonalStats` 改走该纯函数，日志补
     `chosenRate` / `offered` / `winRate` 便于核对；
  5. 单测：`WakuuSignalPicking` 事件口径真值表 + `WakuuPersonalData` 回填断言。
- **做的时候一并定的开放问题**：
  - `personalAssist` 默认关是否合适？至少设置页文案要写明「事件选项想智能选必须打开这一项」；
  - 事件侧的样本量门槛（现 `DefaultMinPersonalCount = 3`）在"同一事件不常重复遇到"的现实下偏严，
    可能需要按事件统计覆盖率后再调整；
  - 社区侧若 SkadaHelper 的事件条目其实带选择率字段（当前只读了 `Text/WinRate/Count`），
    可一并接入 —— **待确认字段名，别猜**。

---

## 维护性改进 backlog（门禁体系 2026-09-08 之后的下一批）

已落地（见 `AGENTS.md` §9 门禁表 + `Scripts/Tools/clr_compat_check.py`）：
源码隔离 Guard、单元测试门禁、CLR/PE/Assembly 三层兼容检查、Critical/Optional 补丁分级、
`INIT_OK`/`INIT_FAILED` 终态收口 + 统一错误码、`dll_check` false-green 修复、marker 缺失 FAIL、
`log_parser --init-status`。

下一轮候选（按性价比排序，2026-09-08 更新）：

1. ~~**Harmony owner / 第三方 Patch 冲突检查**~~ ✅ 已做（Entry.cs `LogKeyPatchOwners`，r88）：
   `关键目标 NPlayerHand.SelectCards — sts2.dualroleadventure [P2Po0...]`，
   第三方 owner 单独 WARN。
2. ~~**BuildIdentity 增强**~~ ✅ 已做（csproj 注入 GitCommit/GitDirty/BuildTimeUtc，r89）：
   `BUILD_IDENTITY commit=<hash> state=clean|dirty built=<UTC>`。
3. ~~**部署槽唯一性检查**~~ ✅ 已做（r93）：跨槽位 json id 重复检测（WARN，既有）+ 新增
   `Test-SlotIdentity`（本仓库 mod 的「json id ≠ dll 主文件名 / id 对应 dll 缺失」= **FAIL**，
   `-List`/部署/`-CheckOnly` 三种模式都跑）与全槽位 WARN 扫描 `Find-SlotIdDllMismatch`。
   命名规则已按各槽实证确认：**目录名可与 id 不同，但 dll 必须与 id 同名**
   （`DualRoleAdventure` 槽 = `DualRoleAdventurefixed.dll` + id `DualRoleAdventurefixed`）。
4. ~~**标准化回归验证契约**~~ ✅ 已做（AGENTS.md §10）：
   `BUG / EXPECTED / SETUP / ACTION / OBSERVE / PASS CONDITION / FAIL CONDITION / LOG ANCHORS`，
   配合固定 token（`INIT_OK` / `PATCH_RESULT` 等）可机器校验；每轮交付随改动写明。
5. ~~**ExpectedPatchTargets 升级到完整签名**~~ ✅ 已做（r92）：Critical/Optional 清单全部换成
   FullName，重载目标钉死参数个数（`FromCombatPile/4`+`/5`、`TryToProcure/3`、`FromDeckForEnchantment/4`）；
   新增 `ExpectedPatchTargetsTests` 用 sts2.dll 元数据逐条核对（格式 + 可解析 + 参数个数 + 无重复），
   清单写错在游戏更新/手误时**单测先红**，不再等到实机误报 Critical 缺失。
6. **marker 解析的对齐坑**：`deploy_dll.ps1` 的 UTF-16 解码已修（r92，两种对齐都扫）。
   同类隐患：`dll_check.py` 早就是双对齐，其它自研脚本若从 dll 里抠字符串需同样处理。
