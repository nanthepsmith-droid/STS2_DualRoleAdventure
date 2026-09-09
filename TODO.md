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

### BUG-2 真人先结束回合后，切到瓦库点结束回合无效（已备案，2026-09-08 用户再确认）

- 已记录于 `瓦库托管优化可行性分析.md` §16.2 第 1 条（结束按钮状态机绑定前台）；用户本轮反馈仍然存在。
- 现状绕法：切回自己 → 再切到瓦库 → 点结束回合才生效。

### 改进-1 每回合开始必须逐个看完所有真人玩家的抽牌演出（新增）

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
