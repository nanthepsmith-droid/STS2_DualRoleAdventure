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
3. **部署槽唯一性检查**：已加跨槽位 json id 重复检测（WARN，含备份槽 DUPLICATE_ID 隐患）；
   下一步「槽位 dll 与 json id 不匹配 = FAIL」仍需按各槽实际命名规则定制。
4. **标准化回归验证契约**：把「改完给复现步骤」规范成
   `BUG / EXPECTED / SETUP / ACTION / OBSERVE / PASS CONDITION / FAIL CONDITION / LOG ANCHORS`，
   配合上面的固定 token 做机器可校验。
5. **ExpectedPatchTargets 升级到完整签名**：现在兼容 `Type.Method`、`Namespace.Type.Method`、
   `Type.Method/argc` 三种写法，但清单仍以简单名为主；逐步替换为 FullName 以消除同名类型/重载歧义。
