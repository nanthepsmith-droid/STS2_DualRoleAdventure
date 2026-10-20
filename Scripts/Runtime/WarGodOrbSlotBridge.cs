#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 【战神球槽】读 <see cref="NOrbManager"/> 的**视觉球槽节点数**（反射读私有 `_orbs`）。
/// 只做读，不改变任何行为；读失败返回 <see cref="OrbVisualGuardPolicy.UnknownSlotCount"/>（调用方一律按"放行"处理）。
/// </summary>
internal static class OrbSlotVisualProbe
{
    private static readonly FieldInfo? OrbsField = AccessTools.Field(typeof(NOrbManager), "_orbs");

    /// <summary>视觉球槽节点数；-1 = 读不到（反射面变了）。</summary>
    internal static int ReadSlotCount(NOrbManager manager)
    {
        try
        {
            if (OrbsField?.GetValue(manager) is ICollection<NOrb> orbs)
            {
                return orbs.Count;
            }
        }
        catch (Exception)
        {
            // 读失败 ⇒ 交给调用方按"未知"处理（放行原版逻辑，绝不误吞动画）。
        }

        return OrbVisualGuardPolicy.UnknownSlotCount;
    }
}

/// <summary>
/// 【战神球槽】召唤时给**临时席位**补上视觉球槽节点（r290，2026-10-20 用户实机："故障机器人被召唤时不会显示充能球"）。
///
/// **根因**（`NOrbManager.cs:88-94`）：球槽**视觉节点**只在 `CombatManager.CombatSetUp` 那一帧，
/// 按当时的 `PlayerCombatState.OrbQueue.Capacity` 建（`OnCombatSetup → AddSlotAnim(Capacity)`）。
/// 战神是**战斗中途**加入的席位，`CombatSetUp` 早就过去了 ⇒ 它的 `NOrbManager._orbs` 永远是 **0 个节点**；
/// 而**数据层**是有槽的（`PlayerCombatState` 构造里 `OrbQueue.AddCapacity(player.BaseOrbSlotCount)`，
/// 故障机器人 = 3）⇒ 出牌效果（充能/激发）数值全对，只是**画面上一个球都看不到**。
/// 旁证：r283 的 `球槽视觉兜底` 就是为这件事打的补丁（那次的表现是"动画抛异常打死 turn loop"，
/// 现在动画被跳过 ⇒ 不再崩，但"看不见球"这层一直没补）。
///
/// **修法**：拿到该席位的 `NCreature.OrbManager`，把 `Capacity - 视觉节点数` 的差额补成空槽
/// （等价于原版 `OnCombatSetup` 那一刻该做的事）。幂等：已经有槽就什么都不做。
/// 纯表现层 —— 失败只 WARN，绝不影响召唤 / 出牌。
/// </summary>
internal static class WarGodOrbSlotBridge
{
    /// <summary>
    /// 把该玩家的**视觉球槽**对齐到数据层容量。返回本次补的槽数（0 = 无需补 / 非球槽角色）。
    /// </summary>
    internal static int SyncVisualSlots(Player player, string tag)
    {
        try
        {
            if (player.Creature == null)
            {
                return 0;
            }

            int capacity = player.PlayerCombatState?.OrbQueue.Capacity ?? 0;
            if (capacity <= 0)
            {
                return 0;   // 非球槽角色（或战斗态还没建好）⇒ 没有槽可画
            }

            NCreature? creatureNode = NCombatRoom.Instance?.GetCreatureNode(player.Creature);
            NOrbManager? manager = creatureNode?.OrbManager;
            if (manager == null)
            {
                // 节点还没建好（`AddChildSafely` 在非主线程会改成 deferred）⇒ 交给收尾阶段再调一次。
                LocalMultiControlLogger.Info(
                    $"{tag} ③c 球槽视觉节点暂未就绪（稍后收尾再补）: seat={player.NetId}, 数据层槽={capacity}");
                return 0;
            }

            int visual = OrbSlotVisualProbe.ReadSlotCount(manager);
            if (visual < 0)
            {
                LocalMultiControlLogger.Warn(
                    $"{tag} ③c 读不到视觉球槽数（反射面变了？本次不补，球的动画仍走 r283 的兜底）: seat={player.NetId}");
                return 0;
            }

            if (visual >= capacity)
            {
                return 0;   // 已经齐了（幂等）
            }

            manager.AddSlotAnim(capacity - visual);
            LocalMultiControlLogger.Info(
                $"{tag} ③c 已补球槽视觉节点: seat={player.NetId}, 数据层槽={capacity}, 视觉 {visual} → {visual + (capacity - visual)}"
                + "（战神是战斗中途加入的席位，原版只在开战那一帧建槽）");
            return capacity - visual;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"{tag} ③c 补球槽视觉节点失败（已忽略，不影响召唤与球的数值）: seat={player.NetId}, error={exception}");
            return 0;
        }
    }
}
