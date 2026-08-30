> 本文件是 CodeBuddy skill `dualroleadventure-development/references/` 同名文件的仓库内只读副本，
> 供纯人工维护者阅读；请与 skills 目录对应文件保持同步更新。

# 本地多控特有的坑（战斗 UI / 奖励 / 设置页）

这些不是 Harmony 用法问题，而是「本地多控把原版联机流程改造后，原版事件/绑定路径变得不可靠」导致。

## 坑 A：`NEndTurnButton` 的事件回调在本地多控下**根本不触发**

**现象**：一名玩家死亡后，另一名存活玩家的「结束回合」按钮消失；切走再切回才恢复。

**排查结论（日志确认，2026-08-29 已修正）**：给 `NEndTurnButton.SetState` / `OnTurnStarted` /
`OnAboutToSwitchToEnemyTurn` 打的 Harmony 探针，在整份 godot.log 里**一次都没出现过**——
当时判断"补丁确实打上了（PatchAll 无异常），但这些方法从未被调用"。

> ⚠️ **2026-08-29 修正（r28 实机证实）**：上述"从未被调用"的结论有误——`NEndTurnButtonLifecyclePatch`
> 在 r28 之前是**方法级-only `[HarmonyPatch]`，整个类被 PatchAll 静默跳过（坑 1）**，探针根本没生效，
> 所以"一次都没出现过"是探针没挂上，而非事件不触发。r28 补上类级标记后实测：
> - `NCombatUi.Activate` 探针触发（`CombatUI Activate: ...`）；
> - `OnTurnStarted` 探针触发（日志出现 `回合开始兜底重评结束回合按钮: ...source=on-turn-started`，
>   来自 `NEndTurnButtonLifecyclePatch.OnTurnStartedPostfix`）；
> - `SetState` / `OnAboutToSwitchToEnemyTurn` 探针在 r28 会话仍未见，是否触发待继续观察。
>
> 结论修正：**本地多控下 `NEndTurnButton.OnTurnStarted` 是可触发的**；r26 当时判断"无效"是
> 因探针未生效所致。修复按钮状态的正确挂点仍是 `CombatManager.SetupPlayerTurn`（下方），
> 但"先确认事件回调真的触发"的教训反而被强化——排查时务必先排除"补丁被静默跳过"。

**修复要点**：不要挂 `NEndTurnButton.OnTurnStarted`。改挂**确认每次存活玩家回合开始都会触发**的
`CombatManager.SetupPlayerTurn`（已有 `CombatManagerSetupPlayerTurnForegroundPatch`），
调 `LocalMultiControlRuntime.ReevaluateEndTurnButtonForControlledPlayer` 按
「当前控制角色是否存活且未 ready」兜底重评按钮状态。

```csharp
// Scripts/Patch/CombatManagerTurnHookForegroundPatch.cs
[HarmonyPatch(typeof(CombatManager), "SetupPlayerTurn")]
internal static class CombatManagerSetupPlayerTurnForegroundPatch
{
    [HarmonyPrefix]
    private static void Prefix(Player player)
    {
        if (player?.Creature == null || !player.Creature.IsAlive) return;

        // 放在瓦库前台抑制判断之前，保证真人角色回合开始也必然重评
        LocalMultiControlRuntime.ReevaluateEndTurnButtonForControlledPlayer("turn-start-setup");

        if (LocalWakuuRelicRuntime.ShouldSuppressForegroundSwitch(player, onlyWhenSelectorActive: false)) return;
        LocalMultiControlRuntime.TryEnsureForegroundForPlayer(player, "turn-start-setup");
    }
}
```

**时序依据**：`SetupPlayerTurn` 在 `PlayersReadyToEndTurn.Clear()` 之后触发
（`CombatManager.cs:729` vs `:772`），此时被控角色 `IsPlayerReadyToEndTurn == false`，
兜底重评才能把按钮设成 Enabled。

**教训**：修 UI 状态类问题时，**先确认你挂的事件回调真的会触发**。加一条特征日志 grep 一下，
比对着源码推理快得多。

## 坑 B：奖励归属角色 ≠ 当前控制角色 → 事件软锁

**现象**：事件里弹卡牌奖励，此时若控制权被切换（自动或手动），领取永远无法完成，
事件 `await RewardsCmd.OfferCustom` 永久挂起。

**根因**：`RewardsSet(player).Offer()` 只为「奖励归属的 player」登记完成源；而真人点击领取走
`RewardsSetSynchronizer.SelectLocalReward`，它用 `_localPlayerId` 定位本地玩家并要求
`reward.Player == LocalPlayer`。本地多控把 `_localPlayerId` 同步成「当前控制角色」，
一旦切换就与奖励归属错位 → 领取抛异常或打进错误角色的奖励栈 → 完成源永不触发。

**修复**：`Scripts/Patch/RewardsSetSynchronizerSelectLocalRewardPatch.cs` —— 领取期间把
`RewardsSetSynchronizer._localPlayerId`、`LocalContext.NetId`、回环 sender **统一临时改绑到
奖励的归属角色**，领取后（含异常路径，用 `HarmonyFinalizer` 兜底）恢复。

**要点**：改绑必须**三处一起改**。`LocalLoopbackHostGameService.SendMessage` 会
`AlignSenderWithLocalContext()`，只改 `_localPlayerId` 不改 sender 会导致消息归属仍错。

## 坑 C：懒建缓存的 UI 首次显示时尺寸为 0

**现象**：瓦库托管设置面板首次点进去内容全空（只有返回按钮和滚动条），退出重进才显示。

**根因**：子菜单实例在 `NSubmenuStack` 下**懒建并缓存**，创建时 `Visible=false`；
`_Ready()` → `BuildContent()` 阶段 clipper 尚未完成布局（FullRect 尺寸为 0），
`OnScrollContentResized` 把滚动内容宽压成 `1px`，内容列被裁剪到看不见。
重进时缓存实例已带上一轮布局好的尺寸，所以正常。

**修复**：重写 `OnSubmenuShown()`，每次显示（含首次）用 `CallDeferred` **延迟一帧**重算尺寸。

**推广**：任何"隐藏创建 + 懒加载 + 缓存复用"的 UI，首次显示时都可能拿到 0 尺寸。
涉及按父容器尺寸计算布局的，一律延迟一帧再算，并挂 `Resized` 事件兜底。

## 坑 D：延迟结算的轮询粒度会变成用户可感知的卡顿

**现象**：打赢后要等一会才出奖励面板。

**根因**：`CreatureCmdKillWinCheckPatch` 的延迟结算按 `150ms × 60` 轮询（最坏 9 秒）。
即便在理想情况（击杀链几帧内收敛，或游戏自身 `ActionExecutor.ExecuteActions` 在每条动作后
已调 `CheckWinCondition` 自动结算）也要按固定 150ms 粒度白等。

**修复**：收紧为 `30ms × 20`（最坏 600ms），并在检测到"战斗已结算/敌人复活"时**立即提前返回**。

**要点**：写"等待某个条件"的轮询时，除了设上限，还要给**提前收敛**的出口；
轮询间隔直接决定用户感知延迟，别拍脑袋用 100ms+。

## 相关：切前台与托管的既有机制

- `TryEnsureForegroundForPlayer` / `SetupPlayerTurn` / `DoTurnEnd` / `FlushPlayerHand`
  四个钩子负责在回合各相位把前台切到对应角色（见 `CombatManagerTurnHookForegroundPatch.cs`）。
- 瓦库形态后台托管开启时**不为该角色切换前台**（`LocalWakuuRelicRuntime.ShouldSuppressForegroundSwitch`）。
  写兜底逻辑时注意这个开关会吞掉后续代码——需要无条件执行的逻辑要放在它**之前**。
