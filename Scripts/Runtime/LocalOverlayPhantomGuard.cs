using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 幽灵弹层守卫（r147）。
///
/// 背景（2026-09-25 第一幕实机，marker r146）：`NOverlayStack.Push` 的顺序是
/// <c>AddChildSafely(节点) → _overlays.Add(screen) → _backstop.MouseFilter = Stop → ShowBackstop()</c>。
/// 当 `AddChildSafely` 走的是"直接 AddChild"分支、而父节点正处于 Godot 的
/// "Parent node is busy setting up children" 状态时，**节点根本没进树，但名单照加、背板照暗、输入照吞**：
/// 表现为屏幕变暗 + 整屏点击无效（战斗里出不了牌、商人点不开、火堆锻造点不动），
/// 且因为节点不在树里，它自己的关闭逻辑永远不会跑 ⇒ **只能整局重开**。
///
/// 本类只做两件事：
/// 1. <see cref="TryEvictPhantom"/>：把"名单里有、节点没进树"的条目从弹层栈里清掉，并恢复共享背板
///    （变亮 + 不再吞输入），把硬软锁降级成一条日志；
/// 2. <see cref="NoteOverlayBlocked"/>：弹层阻挡自动流程时只记一次「栈里有谁、节点在不在树」，
///    给"非幽灵原因"的停滞留下可判读的锚点。
///
/// ⚠ 判定必须**延迟一帧**（见 <see cref="SchedulePhantomCheck"/>）：`AddChildSafely` 在
/// "非主线程 / 父节点未 ready" 时会改走 `CallDeferred(AddChild)`，那次添加发生在下一帧，
/// 立刻判定会把**正常的延迟入栈**误判成幽灵。
/// </summary>
internal static class LocalOverlayPhantomGuard
{
    /// <summary>周期巡检间隔（推入路径已经逐次兜底，巡检只是补漏）。</summary>
    private const long SweepIntervalMs = 1000L;

    /// <summary>`NOverlayStack._overlays`（私有 List&lt;IOverlayScreen&gt;）：判断"名单里到底有没有它"。</summary>
    private static readonly FieldInfo? OverlaysField = AccessTools.Field(typeof(NOverlayStack), "_overlays");

    private static bool _fieldMissingLogged;
    private static long _lastSweepMs;

    /// <summary>同一次「被弹层挡住」的阻塞只记一条日志；ScreenCount 回到 0 视为结束。</summary>
    private static bool _blockedEpisodeActive;

    /// <summary>
    /// 把本次 Push 的弹层排到下一帧再判定（避免误伤 `CallDeferred(AddChild)` 的延迟入栈）。
    /// </summary>
    internal static void SchedulePhantomCheck(NOverlayStack? stack, IOverlayScreen? screen, string source)
    {
        if (stack == null || screen == null || !LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        Callable.From(delegate
        {
            TryEvictPhantom(stack, screen, source);
        }).CallDeferred();
    }

    /// <summary>周期巡检：清掉名单里所有"节点不在树"的幽灵条目。</summary>
    internal static void Tick()
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress)
        {
            _blockedEpisodeActive = false;
            return;
        }

        long nowMs = (long)Time.GetTicksMsec();
        if (_lastSweepMs != 0 && nowMs - _lastSweepMs < SweepIntervalMs)
        {
            return;
        }

        _lastSweepMs = nowMs;

        NOverlayStack? stack = NOverlayStack.Instance;
        if (stack == null || !GodotObject.IsInstanceValid(stack))
        {
            _blockedEpisodeActive = false;
            return;
        }

        if (stack.ScreenCount <= 0)
        {
            _blockedEpisodeActive = false;
            return;
        }

        List<IOverlayScreen> entries = SnapshotEntries(stack);
        foreach (IOverlayScreen entry in entries)
        {
            TryEvictPhantom(stack, entry, "sweep");
        }
    }

    /// <summary>
    /// 弹层挡住了自动流程（看门狗 / 自动选择）时调用：**同一次阻塞只记一条**，
    /// 内容包含栈顶类型与"节点在不在树"——`inTree=False` 就是幽灵弹层的铁证。
    /// </summary>
    internal static void NoteOverlayBlocked(string source)
    {
        try
        {
            NOverlayStack? stack = NOverlayStack.Instance;
            int screenCount = stack?.ScreenCount ?? 0;
            if (screenCount <= 0)
            {
                _blockedEpisodeActive = false;
                return;
            }

            if (_blockedEpisodeActive)
            {
                return;
            }

            _blockedEpisodeActive = true;
            LocalMultiControlLogger.Warn(
                $"弹层阻挡自动流程（同一次阻塞只记一条）: source={source}, screenCount={screenCount}, "
                + $"top={Describe(stack?.Peek())}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"弹层阻挡日志异常(已忽略): {exception.Message}");
        }
    }

    /// <summary>
    /// 给停滞类诊断用的弹层栈快照：`screenCount=N, top=Type[inTree=True|False]`。
    /// `inTree=False` = 幽灵弹层（r147 自愈目标）；`inTree=True` = 真的有个界面开着（大概率在等人点）。
    /// </summary>
    internal static string DescribeOverlayStack()
    {
        try
        {
            NOverlayStack? stack = NOverlayStack.Instance;
            if (stack == null || !GodotObject.IsInstanceValid(stack))
            {
                return "screenCount=none";
            }

            return $"screenCount={stack.ScreenCount}, top={Describe(stack.Peek())}";
        }
        catch (Exception exception)
        {
            return $"overlay-desc-err:{exception.GetType().Name}";
        }
    }

    /// <summary>清掉一个"名单里有、节点不在树"的幽灵条目；不是幽灵就什么都不做。</summary>
    internal static void TryEvictPhantom(NOverlayStack? stack, IOverlayScreen? screen, string source)
    {
        try
        {
            if (stack == null || screen == null || !GodotObject.IsInstanceValid(stack))
            {
                return;
            }

            if (!IsRegistered(stack, screen))
            {
                // 名单里没有它：要么 Push 还没登记（不可能走到这）、要么已被正常移除 —— 不动。
                return;
            }

            if (!IsPhantom(screen))
            {
                return;
            }

            string described = Describe(screen);
            bool normalPathOk = RemoveByGamePath(stack, screen);
            if (!normalPathOk)
            {
                ForceRemoveFromList(stack, screen);
                RestoreStackAfterForceRemove(stack);
            }

            LocalMultiControlLogger.Warn(
                $"幽灵弹层已自愈（名单里有、节点没进树：会变暗并吞掉全部输入）: type={described}, "
                + $"source={source}, normalPath={normalPathOk}, screenCount={stack.ScreenCount}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"幽灵弹层自愈异常(已忽略): {exception.Message}");
        }
    }

    /// <summary>
    /// 幽灵判定：实现不是 Node / 对象已失效 / 不在场景树里。
    /// `Push` 成功时 `AddChildSafely` 会把它挂到弹层栈下，`IsInsideTree()` 必为真，所以这里不会误伤。
    /// </summary>
    private static bool IsPhantom(IOverlayScreen screen)
    {
        if (screen is not Node node)
        {
            return true;
        }

        if (!GodotObject.IsInstanceValid(node))
        {
            return true;
        }

        return !node.IsInsideTree();
    }

    private static bool IsRegistered(NOverlayStack stack, IOverlayScreen screen)
    {
        List<IOverlayScreen>? entries = ReadEntries(stack);
        if (entries == null)
        {
            if (!_fieldMissingLogged)
            {
                _fieldMissingLogged = true;
                LocalMultiControlLogger.Warn(
                    "幽灵弹层守卫不可用：NOverlayStack._overlays 字段未找到（游戏结构可能已变动），本次不做任何干预。");
            }

            return false;
        }

        return entries.Contains(screen);
    }

    /// <summary>走游戏自己的 `Remove`（含背板/焦点/信号的完整收口），返回"名单里已经没有了"。</summary>
    private static bool RemoveByGamePath(NOverlayStack stack, IOverlayScreen screen)
    {
        try
        {
            stack.Remove(screen);
        }
        catch (Exception exception)
        {
            // AfterOverlayClosed 抛异常会中断 Remove 内部后半段（含 _overlays.Remove）——继续走强抠。
            LocalMultiControlLogger.Warn(
                $"幽灵弹层正常移除路径抛异常（继续强制清理）: type={Describe(screen)}, "
                + $"err={exception.GetType().Name}: {exception.Message}");
        }

        return !IsRegistered(stack, screen);
    }

    private static void ForceRemoveFromList(NOverlayStack stack, IOverlayScreen screen)
    {
        List<IOverlayScreen>? entries = ReadEntries(stack);
        if (entries == null)
        {
            return;
        }

        entries.Remove(screen);
    }

    /// <summary>强抠名单后补上 `Remove` 被中断掉的那几件事（背板变亮、恢复栈顶、刷新焦点上下文）。</summary>
    private static void RestoreStackAfterForceRemove(NOverlayStack stack)
    {
        try
        {
            stack.HideBackstop();

            IOverlayScreen? top = stack.Peek();
            if (top != null)
            {
                top.AfterOverlayShown();
            }

            ActiveScreenContext.Instance.Update();
            stack.EmitSignal(NOverlayStack.SignalName.Changed);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"幽灵弹层清理后恢复弹层栈状态失败(已忽略): {exception.Message}");
        }
    }

    private static List<IOverlayScreen>? ReadEntries(NOverlayStack stack)
    {
        if (OverlaysField?.GetValue(stack) is IEnumerable raw)
        {
            List<IOverlayScreen> entries = new();
            foreach (object? item in raw)
            {
                if (item is IOverlayScreen overlay)
                {
                    entries.Add(overlay);
                }
            }

            return entries;
        }

        return null;
    }

    private static List<IOverlayScreen> SnapshotEntries(NOverlayStack stack)
    {
        return ReadEntries(stack) ?? new List<IOverlayScreen>();
    }

    private static string Describe(IOverlayScreen? screen)
    {
        if (screen == null)
        {
            return "none";
        }

        string typeName = screen.GetType().Name;
        if (screen is not Node node)
        {
            return $"{typeName}[not-node]";
        }

        if (!GodotObject.IsInstanceValid(node))
        {
            return $"{typeName}[invalid]";
        }

        return $"{typeName}[inTree={node.IsInsideTree()}]";
    }
}
