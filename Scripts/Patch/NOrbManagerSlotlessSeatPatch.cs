#nullable enable

using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using MegaCrit.Sts2.Core.Nodes.Orbs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 【战神球槽】没有球槽视觉节点的席位 ⇒ 跳过球的进出动画（r283，2026-10-19 实机「打到一半回合死了」）。
///
/// **事故原形**（日志 `godot__20261019-133808__r282.log`）：战神 4096 打出了 <c>CARD.SPINNER</c>
/// ⇒ 获得原版 `SpinnerPower`（每次能量重置时 <c>OrbCmd.Channel&lt;GlassOrb&gt;</c> 充 1 个球）。
/// 下一次**它的回合开始**时：
/// <code>
/// CombatManager.SetupPlayerTurn → Hook.AfterEnergyReset → SpinnerPower.AfterEnergyReset
///   → OrbCmd.Channel → NOrbManager.AddOrbAnim → _orbs.First(node => node.Model != null) ⇒ 抛
/// System.InvalidOperationException: Sequence contains no matching element
///   ⇒ Combat #9 turn loop died … the combat is stuck until the room is restarted
/// </code>
/// 表现为用户看到的「不能出牌、不能换人」。
///
/// **为什么 `_orbs` 会是空的**：`NOrbManager` 的球槽**视觉节点**只在 `OnCombatSetup`（战斗初始化那一刻）
/// 按当时的 `OrbQueue.Capacity` 建（<c>AddSlotAnim</c>）；而 `OrbCmd.Channel` 对"0 槽角色"会**战斗中**
/// 临时 `AddSlots(player, 1)` —— 数据层有 1 槽了，视觉层仍是 0 个节点 ⇒ `AddOrbAnim` 里的
/// `_orbs.First(...)` 必然抛。这是原版对"非故障角色中途获得球"的空白，战神只是**第一个踩到的人**
/// （战神是战斗中途加入的席位，`CombatSetUp` 早就过去了，任何充能牌/遗物都会踩）。
///
/// **修法**：动画是纯表现 ⇒ 没有视觉球槽就跳过动画，**数据层（OrbQueue / 充能与激发效果）照常**；
/// 有球槽的玩家（正常故障机器人）完全不受影响。两个调用点（`OrbCmd` 只有这两处）都补。
/// </summary>
[HarmonyPatch(typeof(NOrbManager))]
internal static class NOrbManagerSlotlessSeatPatch
{
    private static readonly FieldInfo? OrbsField = AccessTools.Field(typeof(NOrbManager), "_orbs");

    /// <summary>已打过"跳过球动画"日志的 manager（按实例 id 去重；每场战斗最多几条）。</summary>
    private static readonly HashSet<ulong> LoggedManagers = new();

    [HarmonyPrefix]
    [HarmonyPatch(nameof(NOrbManager.AddOrbAnim))]
    private static bool AddOrbPrefix(NOrbManager __instance)
        => !ShouldSkipOrbAnim(__instance, "AddOrbAnim");

    [HarmonyPrefix]
    [HarmonyPatch(nameof(NOrbManager.EvokeOrbAnim))]
    private static bool EvokeOrbPrefix(NOrbManager __instance)
        => !ShouldSkipOrbAnim(__instance, "EvokeOrbAnim");

    /// <summary>
    /// r288：**减球槽**的动画同族 —— `RemoveSlotAnim(amount)` 里 `amount > _orbs.Count` 会抛
    /// "There are not enough slots to remove."。临时席位（战神）本来就没有球槽节点（`_orbs` 空），
    /// 任何"减槽"效果的牌（实测 `CARD.BULK_UP`）一打就把托管出牌链打断（被 r262 黑名单兜住 = 这张牌本局托管不打）。
    /// 没有视觉球槽 ⇒ 减槽动画跳过即可（数据层照常，牌效果其余部分继续）。
    /// ⚠ 只在 `_orbs` 为空时跳过；有槽的玩家若真的 `amount > count`，仍旧让原版抛（不掩盖原版数据错）。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(nameof(NOrbManager.RemoveSlotAnim))]
    private static bool RemoveSlotPrefix(NOrbManager __instance)
        => !ShouldSkipOrbAnim(__instance, "RemoveSlotAnim");

    /// <summary>没有视觉球槽 ⇒ 返回 true（跳过原方法）；读取失败一律**放行**原逻辑，绝不把动画吞掉。</summary>
    private static bool ShouldSkipOrbAnim(NOrbManager manager, string method)
    {
        try
        {
            int slotCount = ReadVisualSlotCount(manager);
            if (!OrbVisualGuardPolicy.ShouldSkipOrbVisual(slotCount))
            {
                return false;
            }

            ulong key = manager.GetInstanceId();
            lock (LoggedManagers)
            {
                if (LoggedManagers.Count >= 32 || !LoggedManagers.Add(key))
                {
                    return true;
                }
            }

            LocalMultiControlLogger.Info(
                $"球槽视觉兜底：该席位没有球槽节点 ⇒ 跳过球的动画（数据层照常，第三方/临时席位常见）: "
                + $"method={method}, instance={key}");
            return true;
        }
        catch (System.Exception exception)
        {
            LocalMultiControlLogger.Warn($"球槽视觉兜底判定失败（放行原逻辑）: {exception.Message}");
            return false;
        }
    }

    private static int ReadVisualSlotCount(NOrbManager manager)
    {
        if (OrbsField?.GetValue(manager) is ICollection<NOrb> orbs)
        {
            return orbs.Count;
        }

        return -1;
    }
}
