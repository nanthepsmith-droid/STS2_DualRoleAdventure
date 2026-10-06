using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
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
/// <item><b>入战快照</b>（<c>NCombatRoom._Ready</c> 后缀，即站位刚算完之后；若那一刻还没有战斗态，
///   则由战斗 UI 跟踪器的**首次采样**补打一行 <c>source=first-tick</c>）打一行
///   `[立绘站位快照] … 从左到右=[…]` + 每席 `IsMe` 原判 + 当时的受控位 / 上下文位
///   ⇒ 一眼看出"布局时『我』是谁、谁在左"；</item>
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

    /// <summary>已打过"快照被跳过"日志的（source, reason）组合；只用于防刷屏，不参与判定。</summary>
    private static readonly HashSet<string> LoggedSnapshotSkips = new();

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
        LoggedSnapshotSkips.Clear();
        _lastSampleMs = 0L;
        _ = source;
    }

    /// <summary>
    /// 入战快照：由 <c>NCombatRoom._Ready</c> 后缀调用（此刻 <c>CreateAllyNodes</c> 刚算完站位），
    /// 以及战斗 UI 跟踪器的**首次 tick**（<c>source=first-tick</c>）兜底调用 ——
    /// r216 那一局 `_Ready` 这次调用**一行都没打**（该时刻 `CombatManager.IsInProgress` 还是 false，
    /// 旧实现直接静默 return），导致"开局站位"这一最关键的一项没采到；故：
    /// ① 判定放宽到"房间 mode + 本地多控会话 + 有战斗态玩家"（不再要求 `IsInProgress`）；
    /// ② 任何提前 return 都打一条**去重**的 `跳过` 日志（带 reason）
    ///    ⇒ 下次"快照没出现"能立刻分辨是"被跳过"还是"压根没被调用"。
    /// </summary>
    internal static void LogCombatLayoutSnapshot(NCombatRoom? room, string source)
    {
        try
        {
            if (room == null)
            {
                LogSnapshotSkip(source, "room=null");
                return;
            }

            if (room.Mode != CombatRoomMode.ActiveCombat)
            {
                LogSnapshotSkip(source, $"mode={room.Mode}");
                return;
            }

            if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
            {
                LogSnapshotSkip(source, "非本地多控回环局");
                return;
            }

            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            CombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
            if (runState?.Players == null || combatState?.Players == null)
            {
                LogSnapshotSkip(
                    source,
                    $"runState={(runState != null ? "有" : "null")}, combatState={(combatState != null ? "有" : "null")}");
                return;
            }

            List<string> placements = new();
            LastSnapshots.Clear();

            // 局部变量消掉可空告警（lambda 里用捕获的 room 会丢流分析）。
            NCombatRoom combatRoom = room;

            // 按 X 从小到大 = 屏幕上**从左到右**的实际站位。
            IEnumerable<Player> byLeftToRight = combatState.Players
                .Where(player => player?.Creature != null)
                .OrderBy(player => combatRoom.GetCreatureNode(player.Creature)?.GlobalPosition.X ?? float.MaxValue);

            foreach (Player player in byLeftToRight)
            {
                NCreature? node = combatRoom.GetCreatureNode(player.Creature);
                if (node == null)
                {
                    continue;
                }

                LastSnapshots[player.NetId] = new CrewSnapshot(node.GlobalPosition.X, node.GetIndex());
                // IsMe 原判（我们的放行口子只对第三方调用方生效，这里读到的是原值）
                // —— 站位由「谁是 me」决定，所以这一列是判读换位的关键。
                placements.Add(
                    $"{DescribeSeat(player.NetId)}(x={node.GlobalPosition.X:0.#}, 节点索引={node.GetIndex()}"
                    + (node.GetIndex() == 0 ? ", 最前" : string.Empty)
                    + $", IsMe={LocalContext.IsMe(player)})");
            }

            if (placements.Count == 0)
            {
                LogSnapshotSkip(source, "取不到任何玩家立绘节点");
                return;
            }

            LocalMultiControlLogger.Info(
                $"[立绘站位快照] source={source}, 从左到右=[{string.Join(" | ", placements)}], "
                + $"受控位={DescribeSeatOrNone(LocalSeatSource.CurrentSeats().ControlledSeatId)}, "
                + $"上下文位={DescribeSeatOrNone(LocalSeatSource.CurrentSeats().ContextSeatId)}, "
                + $"已死席位=[{DescribeDeadParticipants(combatState)}]");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[立绘站位快照] 采样失败: {exception.Message}");
        }
    }

    /// <summary>快照被提前跳过的原因（去重，便于"快照没出现"时立刻分辨是被跳过还是没被调用）。</summary>
    private static void LogSnapshotSkip(string source, string reason)
    {
        string key = $"{source}#{reason}";
        lock (LoggedSnapshotSkips)
        {
            if (LoggedSnapshotSkips.Count >= 16 || !LoggedSnapshotSkips.Add(key))
            {
                return;
            }
        }

        LocalMultiControlLogger.Info($"[立绘站位快照] 跳过: source={source}, reason={reason}");
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

            // 首次 tick 兜底：`NCombatRoom._Ready` 那一刻可能还没有战斗态（r216 实测"快照一行没打"），
            // 这里补采一次"开局站位"（同时填好 LastSnapshots，后面的对比才有基线）。
            if (LastSnapshots.Count == 0)
            {
                LogCombatLayoutSnapshot(room, "first-tick");
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

    /// <summary>
    /// 本场战斗里**已死**的玩家席位（r222：炼化把席位"死在战斗外"后，它们仍会被列进
    /// <c>CombatState.Players</c>、却永远轮不到开始回合 —— 这正是第三方（LexNinja2 的 LexKela 字典）
    /// 与一堆"按回合开始登记状态"的代码最容易踩的前提。快照里点出来，日后一眼可辨。
    /// </summary>
    private static string DescribeDeadParticipants(CombatState combatState)
    {
        try
        {
            List<string> dead = new();
            foreach (Player player in combatState.Players)
            {
                if (player?.Creature != null && player.Creature.IsDead)
                {
                    dead.Add(DescribeSeat(player.NetId));
                }
            }

            return dead.Count == 0 ? "无" : string.Join(", ", dead);
        }
        catch (Exception)
        {
            return "采样失败";
        }
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
