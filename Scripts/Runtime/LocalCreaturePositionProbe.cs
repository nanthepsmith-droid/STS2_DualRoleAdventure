using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「角色立绘站位」诊断探针（r215，2026-10-06 用户实机报：**打一半我和瓦库的立绘左右站位对调了**，
/// 不影响战斗）。
///
/// 为什么要探：原版站位只在**战斗房间创建那一刻**算一次 ——
/// <c>NCombatRoom.CreateAllyNodes</c> → <c>NCombatRoom.PositionPlayersAndPets</c>，
/// 而那句 <c>PositionPlayersAndPets</c> 的排序键是 <c>LocalContext.IsMe(entity)</c>
/// （"本地玩家"那一项 <c>list.Insert(0, …)</c> ⇒ 排在**最左**，其余按入参顺序排）。
/// 本地多控下 <c>LocalContext</c> 会随「切人 / 后台托管出牌」漂移，所以站位"该以谁为『我』"本身就脆；
/// 而**中途**想换位就得有人重新摆过节点 —— 光看现有日志无法判断是"一开始就摆反了"还是"中途被谁挪了"。
///
/// 因此本探针做两件事（都只在本地多控 + 进行中的战斗里生效）：
/// <list type="number">
/// <item><b>入战快照</b>（<c>NCombatRoom._Ready</c> 后缀，即站位刚算完之后）打一行
///   `[立绘站位快照] … 从左到右=[…]` + 当时的受控位 / 上下文位 ⇒ 一眼看出"布局时『我』是谁、谁在左"；</item>
/// <item><b>位移探针</b>（战斗 UI 逐帧跟踪器，内部 250ms 节流）：任一玩家立绘的 **X 或父节点索引**变了就打一行
///   （含 变化前→后 / 当前左右顺序 / 受控位 / 上下文位 / 开战以来毫秒）⇒ 定位"是谁在什么时候挪的"。</item>
/// </list>
/// 两条都**不改变任何行为**（只读 + 打日志）；正常战斗中位移为 0 ⇒ 平时一条都不会打。
/// </summary>
internal static class LocalCreaturePositionProbe
{
    /// <summary>位移探针的采样节流（站位不需要逐帧精度）。</summary>
    private const long SampleIntervalMs = 250L;

    /// <summary>小于这个像素差当成抖动（浮点/缩放误差），不算"位移"。</summary>
    private const float MoveThresholdPx = 2f;

    private static long _lastSampleMs;

    /// <summary>上一次采样到的每个席位的立绘状态（X + 父节点索引）。</summary>
    private static readonly Dictionary<ulong, CrewSnapshot> LastSnapshots = new();

    private readonly struct CrewSnapshot
    {
        internal CrewSnapshot(float x, int parentIndex)
        {
            X = x;
            ParentIndex = parentIndex;
        }

        internal float X { get; }

        internal int ParentIndex { get; }
    }

    /// <summary>退局 / 换战斗时复位（由运行时清理链调用）。</summary>
    internal static void Reset(string source)
    {
        LastSnapshots.Clear();
        _lastSampleMs = 0L;
        _ = source;
    }

    /// <summary>
    /// 入战快照：由 <c>NCombatRoom._Ready</c> 后缀调用（此刻 <c>CreateAllyNodes</c> 刚算完站位）。
    /// </summary>
    internal static void LogCombatLayoutSnapshot(NCombatRoom room, string source)
    {
        try
        {
            if (room == null || room.Mode != CombatRoomMode.ActiveCombat || !IsLocalCoopCombat())
            {
                return;
            }

            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            CombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
            if (runState?.Players == null || combatState?.Players == null)
            {
                return;
            }

            List<string> placements = new();
            LastSnapshots.Clear();

            // 按 X 从小到大 = 屏幕上**从左到右**的实际站位。
            IEnumerable<Player> byLeftToRight = combatState.Players
                .Where(player => player?.Creature != null)
                .OrderBy(player => room.GetCreatureNode(player.Creature)?.GlobalPosition.X ?? float.MaxValue);

            foreach (Player player in byLeftToRight)
            {
                NCreature? node = room.GetCreatureNode(player.Creature);
                if (node == null)
                {
                    continue;
                }

                LastSnapshots[player.NetId] = new CrewSnapshot(node.GlobalPosition.X, node.GetIndex());
                placements.Add(
                    $"{DescribeSeat(player.NetId)}(x={node.GlobalPosition.X:0.#}, 节点索引={node.GetIndex()}"
                    + (node.GetIndex() == 0 ? ", 最前" : string.Empty) + ")");
            }

            if (placements.Count == 0)
            {
                return;
            }

            LocalMultiControlLogger.Info(
                $"[立绘站位快照] source={source}, 从左到右=[{string.Join(" | ", placements)}], "
                + $"受控位={DescribeSeatOrNone(LocalSeatSource.CurrentSeats().ControlledSeatId)}, "
                + $"上下文位={DescribeSeatOrNone(LocalSeatSource.CurrentSeats().ContextSeatId)}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[立绘站位快照] 采样失败: {exception.Message}");
        }
    }

    /// <summary>位移探针：由战斗 UI 逐帧跟踪器调用（内部节流，只在真的变了时打日志）。</summary>
    internal static void Sample(string source)
    {
        try
        {
            long now = System.Environment.TickCount64;
            if (_lastSampleMs != 0L && now - _lastSampleMs < SampleIntervalMs)
            {
                return;
            }

            _lastSampleMs = now;

            if (!IsLocalCoopCombat())
            {
                if (LastSnapshots.Count > 0)
                {
                    LastSnapshots.Clear();
                }

                return;
            }

            NCombatRoom? room = NCombatRoom.Instance;
            CombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
            if (room == null || combatState?.Players == null)
            {
                return;
            }

            bool moved = false;
            foreach (Player player in combatState.Players)
            {
                if (player?.Creature == null)
                {
                    continue;
                }

                NCreature? node = room.GetCreatureNode(player.Creature);
                if (node == null)
                {
                    continue;
                }

                CrewSnapshot current = new(node.GlobalPosition.X, node.GetIndex());
                if (!LastSnapshots.TryGetValue(player.NetId, out CrewSnapshot previous))
                {
                    LastSnapshots[player.NetId] = current;
                    continue;
                }

                bool xChanged = Math.Abs(current.X - previous.X) >= MoveThresholdPx;
                bool orderChanged = current.ParentIndex != previous.ParentIndex;
                if (!xChanged && !orderChanged)
                {
                    continue;
                }

                moved = true;
                LastSnapshots[player.NetId] = current;
                LocalMultiControlLogger.Info(
                    $"[立绘位移探针] {DescribeSeat(player.NetId)} 立绘移动: "
                    + $"x {previous.X:0.#} -> {current.X:0.#} (Δ{current.X - previous.X:+0.#;-0.#;0}), "
                    + $"节点索引 {previous.ParentIndex} -> {current.ParentIndex}, "
                    + $"战斗开始后={(long)(Time.GetTicksMsec() - room.CreatedMsec)}ms, source={source}");
            }

            if (moved)
            {
                LocalMultiControlLogger.Info(
                    $"[立绘位移探针] 当前从左到右=[{DescribeLayoutLeftToRight(room, combatState)}], "
                    + $"受控位={DescribeSeatOrNone(LocalSeatSource.CurrentSeats().ControlledSeatId)}, "
                    + $"上下文位={DescribeSeatOrNone(LocalSeatSource.CurrentSeats().ContextSeatId)}");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[立绘位移探针] 采样失败: {exception.Message}");
        }
    }

    private static string DescribeLayoutLeftToRight(NCombatRoom room, CombatState combatState)
    {
        List<string> placements = new();
        IEnumerable<Player> ordered = combatState.Players
            .Where(player => player?.Creature != null)
            .OrderBy(player => room.GetCreatureNode(player.Creature)?.GlobalPosition.X ?? float.MaxValue);

        foreach (Player player in ordered)
        {
            NCreature? node = room.GetCreatureNode(player.Creature);
            if (node != null)
            {
                placements.Add($"{DescribeSeat(player.NetId)}(x={node.GlobalPosition.X:0.#})");
            }
        }

        return string.Join(" | ", placements);
    }

    private static bool IsLocalCoopCombat()
    {
        return LocalSelfCoopContext.IsEnabled
               && LocalSelfCoopContext.UseSingleAdventureMode
               && CombatManager.Instance.IsInProgress;
    }

    private static string DescribeSeat(ulong netId)
    {
        return $"角色{LocalSelfCoopContext.GetSlotLabel(netId)}(…{netId})";
    }

    private static string DescribeSeatOrNone(ulong? netId)
    {
        return netId.HasValue ? DescribeSeat(netId.Value) : "无";
    }
}
