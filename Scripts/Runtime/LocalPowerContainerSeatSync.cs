using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// BUG-41（2026-11-04）：切前台后重建 creature 的 <see cref="NPowerContainer"/>，
/// 让「按玩家各算一份的实例型 power」显示成**当前玩家那一份**。
///
/// **为什么需要**：`SandpitPower`（无厌沙虫的沙坑）是 `PowerInstanceType.Instanced`，
/// `Target` = 某个玩家 ⇒ **每个玩家一份实例**；而 `PowerModel.IsVisible` 的判据是
/// `Target == null || LocalContext.IsMe(Target) || Target.IsEnemy` ⇒ 每份实例只对它自己的玩家可见。
/// 但 `NPowerContainer.Add(power)` 里那句 `if (power.IsVisible)` **只在加图标时判一次**，
/// `SetCreature` 之后再也不重算 ⇒ 容器是在第一个玩家做前台时建的，
/// 之后切人不会重建，boss 血条下的数字永远绑在第一个玩家那份实例上
/// （tooltip 是悬停时现算的，所以"悬停对、血条下错"）。
///
/// **做法**：只在「前台席位 / 战斗场次」真的变了时，对场上每个 creature 的容器做一次重建
/// （反射把 `_creature` 置空 → 回收旧 `NPower` 节点 → 清列表 → 再 `SetCreature`），
/// 由 `LocalMultiControlRuntime.ApplyControlContext` 统一调用。纯视觉、不碰任何数据。
/// </summary>
internal static class LocalPowerContainerSeatSync
{
    private static ulong _lastSeatId;
    private static int _lastCombatIdentity;

    /// <summary>同一个 WARN 只刷一次（避免切人时反复刷屏）。</summary>
    private static string? _lastWarn;

    /// <summary>累计重建次数（判读锚点：应约等于"切人次数"）。</summary>
    internal static int RebuildCount { get; private set; }

    /// <summary>归属已切到 <paramref name="seatId"/> 之后调用（由 ApplyControlContext 收口）。</summary>
    internal static void OnContextApplied(ulong seatId, string source)
    {
        try
        {
            if (!LocalSelfCoopContext.IsEnabled || seatId == 0UL)
            {
                return;
            }

            if (!CombatManager.Instance.IsInProgress)
            {
                return;
            }

            CombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
            if (combatState == null)
            {
                return;
            }

            int combatIdentity = RuntimeHelpers.GetHashCode(combatState);
            if (seatId == _lastSeatId && combatIdentity == _lastCombatIdentity)
            {
                return;
            }

            _lastSeatId = seatId;
            _lastCombatIdentity = combatIdentity;

            int rebuilt = RebuildAll(combatState);
            if (rebuilt > 0)
            {
                RebuildCount += rebuilt;
                LocalMultiControlLogger.Info(
                    $"敌方/我方 power 图标已按当前前台重建: seat={seatId}, 容器={rebuilt}, "
                    + $"累计={RebuildCount}, source={source}");
            }
        }
        catch (Exception exception)
        {
            Warn($"power 容器按前台重建失败: {exception.Message}");
        }
    }

    private static int RebuildAll(CombatState combatState)
    {
        NCombatRoom? room = NCombatRoom.Instance;
        if (room == null)
        {
            return 0;
        }

        int rebuilt = 0;
        // 玩家侧也要重建：某些第三方/原版 power 的 Target 是"别的玩家"，切换后可见性同样会变。
        foreach (Creature creature in combatState.Creatures)
        {
            if (creature == null || !creature.IsAlive)
            {
                continue;
            }

            NCreature? node = room.GetCreatureNode(creature);
            if (node == null || !GodotObject.IsInstanceValid(node))
            {
                continue;
            }

            NPowerContainer? container = ResolveContainer(node);
            if (container != null && GodotObject.IsInstanceValid(container) && RebuildOne(container, creature))
            {
                rebuilt++;
            }
        }

        return rebuilt;
    }

    /// <summary>从 creature 节点上取状态显示里的 power 容器（节点名与原版 `NCreature._Ready` 一致：`%HealthBar`）。</summary>
    private static NPowerContainer? ResolveContainer(NCreature node)
    {
        try
        {
            NCreatureStateDisplay? display = node.GetNodeOrNull<NCreatureStateDisplay>("%HealthBar");
            if (display == null)
            {
                return null;
            }

            return AccessTools.Field(typeof(NCreatureStateDisplay), "_powerContainer")?.GetValue(display)
                as NPowerContainer;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool RebuildOne(NPowerContainer container, Creature creature)
    {
        FieldInfo? creatureField = AccessTools.Field(typeof(NPowerContainer), "_creature");
        FieldInfo? nodesField = AccessTools.Field(typeof(NPowerContainer), "_powerNodes");
        if (creatureField == null || nodesField == null)
        {
            Warn("power 容器反射面缺失（_creature / _powerNodes）");
            return false;
        }

        // 还没 SetCreature 过（读不到 creature）⇒ 交给原版初始化，别抢。
        if (creatureField.GetValue(container) is not Creature)
        {
            return false;
        }

        if (nodesField.GetValue(container) is List<NPower> nodes)
        {
            foreach (NPower powerNode in nodes.ToArray())
            {
                if (powerNode != null && GodotObject.IsInstanceValid(powerNode))
                {
                    powerNode.QueueFree();
                }
            }

            nodes.Clear();
        }

        // SetCreature 里有 `if (_creature != null) throw` ⇒ 必须先反射置空。
        creatureField.SetValue(container, null);
        container.SetCreature(creature);
        return true;
    }

    private static void Warn(string message)
    {
        if (string.Equals(_lastWarn, message, StringComparison.Ordinal))
        {
            return;
        }

        _lastWarn = message;
        LocalMultiControlLogger.Warn(message);
    }
}
