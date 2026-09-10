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

- **现象**：多个瓦库时只能「一个瓦库打完 → 切到下一个瓦库」串行进行，且真人视角会跟着切到
  瓦库正在操作的角色；瓦库多时同样很慢。
- **方向（未做）**：① 后台托管免切前台（类似单人双角色的后台模式）→ 多瓦库可并行 / 准并行推进；
  ② 视角策略可配置（不跟随 / 仅关键节点跟随）。
- **风险**：与选牌串行化、前台绑定类 UI（结束回合按钮，见 BUG-2）强耦合，
  需先解决「前台归属」的单一事实来源，否则会把 BUG-2 放大。

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
