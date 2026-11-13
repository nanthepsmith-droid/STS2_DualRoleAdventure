using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 选择器栈的唯一访问入口（0.113 适配）。
///
/// **为什么要有这一层**：0.112 及更早版本的选择器栈是 <c>CardSelectCmd</c> 里的**全局单栈**
/// （私有静态字段 <c>_selectorStack</c>）；0.113 把它抽成 <see cref="CardSelectorStack"/> 实例，
/// 由 <c>RunManager.Instance.CardSelectorStack</c> 持有，并且**按玩家槽位分成多个栈**
/// （<c>_selectorStacks</c>，公开 API 只有 <see cref="CardSelectorStack.GetSelector"/> /
/// <see cref="CardSelectorStack.PushSelector"/> / <see cref="CardSelectorStack.UseSelector"/>）。
///
/// 于是：
/// <list type="bullet">
/// <item>「栈顶是谁」只能用 <c>GetSelector(player)</c> 问，玩家身份由调用方给；</item>
/// <item>「整栈快照 / 按引用摘除 / 整体清理」上游**没有公开 API**，仍需反射（字段名换成
///   <c>CardSelectorStack._selectorStacks</c>）—— 这是 r332 之后又一次"反射面整体搬迁"，
///   旧的 <c>CardSelectCmd._selectorStack</c> 在 0.113 已不存在（反射会静默返回 null）；</item>
/// <item><c>GetSelector(player)</c> 对**结构外成员**（战神这类临时席位：不在 <c>RunState.Players</c> 里）
///   **直接抛 <c>InvalidOperationException</c>**，与 r264 的 <c>ReserveChoiceId</c> 返回 -1 同型。
///   因此所有入口都先判槽位、再 try/catch：宁可返回 null（= 没有自动选择器），也不让异常冒穿
///   原版选牌链（那会变成 <c>turn loop died</c>）。</item>
/// </list>
///
/// 口径：**改道 &gt; 跳过 &gt; 不管**。这里选择"跳过并留痕"，且每个失败族只打一次 WARN，避免热路径刷屏。
/// </summary>
internal static class WakuuSelectorStackAccess
{
    private static readonly FieldInfo? SelectorStacksField =
        AccessTools.Field(typeof(CardSelectorStack), "_selectorStacks");

    private static int _warnedGetTop;
    private static int _warnedPush;
    private static int _warnedEnterList;

    /// <summary>反射面是否就绪（供启动自检打印一条锚点，避免"静默空转"）。</summary>
    internal static bool IsReflectionReady => SelectorStacksField != null;

    /// <summary>
    /// 启动自检（<see cref="RuntimeCompatibilityCheck.Run"/> 调一次）：把"这个版本用的是哪套选择器栈"
    /// 写成一条日志。0.113 起栈实例挂在 RunManager 上、且要到进局才创建，所以这里只验**类型与字段**
    /// 是否解析得到，栈实例是否存在留给运行时探针。
    /// </summary>
    internal static void SelfCheck()
    {
        if (IsReflectionReady)
        {
            LocalMultiControlLogger.Info(
                "选择器栈反射面已就绪（0.113 按玩家分栈）：RunManager.CardSelectorStack._selectorStacks"
                + "（公开 API GetSelector/PushSelector 可用）");
        }
        else
        {
            LocalMultiControlLogger.Warn(
                "选择器栈反射面缺失：未找到 CardSelectorStack._selectorStacks —— "
                + "栈探针/按引用摘栈/自恢复清理会空转（托选出牌本身仍走公开 API）");
        }
    }

    private static CardSelectorStack? CurrentStack => RunManager.Instance?.CardSelectorStack;

    /// <summary>玩家所在槽位；不在本局玩家集合里（含无局、战神这类结构外成员）返回 -1。</summary>
    internal static int GetSlotIndex(Player? player)
    {
        if (player == null)
        {
            return -1;
        }

        try
        {
            return RunManager.Instance?.DebugOnlyGetState()?.GetPlayerSlotIndex(player) ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>是否为「结构外成员」（不在 runState.Players ⇒ 没有自己的选择器栈）。</summary>
    internal static bool IsStructuralOutsider(Player? player) => GetSlotIndex(player) < 0;

    /// <summary>
    /// 该玩家当前生效（栈顶）的选择器。结构外成员 / 反射面缺失 / 上游抛异常一律返回 **null**
    /// （null = "没有自动选择器"，调用方按"真人 UI 或既有兜底"处理）。
    /// </summary>
    internal static ICardSelector? GetTop(Player? player)
    {
        if (player == null || IsStructuralOutsider(player))
        {
            return null;
        }

        try
        {
            return CurrentStack?.GetSelector(player);
        }
        catch (Exception exception)
        {
            WarnOnce(ref _warnedGetTop, $"选择器栈 GetTop 失败，按「无选择器」处理: player={player.NetId}, error={exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// 给该玩家压入托管选择器。结构外成员 / 上限抛异常 ⇒ 返回 null（= 没有压栈，调用方需自己决定降级）。
    /// </summary>
    internal static IDisposable? Push(Player player, ICardSelector selector)
    {
        if (player == null || selector == null)
        {
            return null;
        }

        if (IsStructuralOutsider(player))
        {
            WarnOnce(ref _warnedPush, $"结构外成员没有选择器栈，跳过压栈（托管选牌对其不生效）: player={player.NetId}, selector={selector.GetType().Name}");
            return null;
        }

        try
        {
            return CurrentStack?.PushSelector(player, selector);
        }
        catch (Exception exception)
        {
            WarnOnce(ref _warnedPush, $"压入选择器栈失败，按「未压栈」处理: player={player.NetId}, selector={selector.GetType().Name}, error={exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// 全部玩家选择器栈的条目快照（**栈顶 → 栈底**逐栈拼接）。反射面不可用时返回空列表。
    /// 语义变化说明：0.112 及更早是"一个全局栈"，本方法读取的是同一批数据；0.113 起是"每玩家一栈"，
    /// 因此这里的 Count 是**所有槽位的条目总和**，调用方（探针/日志）仍可用"是否为 0"判断残留。
    /// </summary>
    internal static IReadOnlyList<object> SnapshotEntries()
    {
        IList? lists = TryGetStacks();
        if (lists == null)
        {
            return Array.Empty<object>();
        }

        List<object> entries = new();
        foreach (object? item in lists)
        {
            if (item is not IEnumerable enumerable)
            {
                continue;
            }

            foreach (object? selector in enumerable)
            {
                if (selector != null)
                {
                    entries.Add(selector);
                }
            }
        }

        return entries;
    }

    /// <summary>最后一个非空槽位的栈顶类型名（无可读栈时返回 "none"）。</summary>
    internal static string DescribeTop()
    {
        IList? lists = TryGetStacks();
        if (lists == null)
        {
            return "none";
        }

        string topType = "none";
        foreach (object? item in lists)
        {
            if (item is not Stack<ICardSelector> stack || stack.Count == 0)
            {
                continue;
            }

            topType = stack.Peek()?.GetType().Name ?? "none";
        }

        return topType;
    }

    /// <summary>
    /// 把某个选择器按**引用**从任意玩家栈里摘掉。不在任何栈里 = 空操作，返回是否真的摘掉了。
    ///
    /// 用途同旧版：并发出牌档下，先释放的作用域已不在栈顶 ⇒ 原版 scope 不会弹栈 ⇒ 会永久残留。
    /// </summary>
    internal static bool RemoveByReference(object selector)
    {
        if (selector == null)
        {
            return false;
        }

        IList? lists = TryGetStacks();
        if (lists == null)
        {
            return false;
        }

        bool removed = false;
        foreach (object? item in lists)
        {
            if (item is not Stack<ICardSelector> stack || stack.Count == 0)
            {
                continue;
            }

            // Stack<T> 的枚举顺序就是「栈顶 → 栈底」，与 WakuuSelectorStackSurgery 的入参约定一致
            List<object> topToBottom = new(stack.Count);
            foreach (ICardSelector? stacked in stack)
            {
                if (stacked != null)
                {
                    topToBottom.Add(stacked);
                }
            }

            List<object> remaining = WakuuSelectorStackSurgery.RemoveByReference(topToBottom, selector);
            if (remaining.Count == topToBottom.Count)
            {
                continue;
            }

            stack.Clear();
            for (int i = remaining.Count - 1; i >= 0; i--)
            {
                stack.Push((ICardSelector)remaining[i]);
            }

            removed = true;
        }

        return removed;
    }

    /// <summary>
    /// 清理"全是托管选择器"的栈（自恢复用）。只要有一个栈里混着非托管选择器就整体不动 ——
    /// 与旧版"栈里只要混入非瓦库选择器就不清"的保守口径一致。返回被清掉的条目数。
    /// </summary>
    internal static int ClearVakuuStacks()
    {
        IList? lists = TryGetStacks();
        if (lists == null)
        {
            return 0;
        }

        List<Stack<ICardSelector>> targets = new();
        int total = 0;
        foreach (object? item in lists)
        {
            if (item is not Stack<ICardSelector> stack || stack.Count == 0)
            {
                continue;
            }

            bool allVakuu = true;
            foreach (ICardSelector? stacked in stack)
            {
                if (stacked is not VakuuCardSelector and not LocalWakuuStrategySelector)
                {
                    allVakuu = false;
                    break;
                }
            }

            if (!allVakuu)
            {
                continue;
            }

            targets.Add(stack);
            total += stack.Count;
        }

        foreach (Stack<ICardSelector> stack in targets)
        {
            stack.Clear();
        }

        return total;
    }

    private static IList? TryGetStacks()
    {
        CardSelectorStack? stackOwner = CurrentStack;
        if (stackOwner == null || SelectorStacksField == null)
        {
            return null;
        }

        try
        {
            return SelectorStacksField.GetValue(stackOwner) as IList;
        }
        catch (Exception exception)
        {
            WarnOnce(ref _warnedEnterList, $"读取选择器栈列表失败（探针/自恢复将空转）: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    private static void WarnOnce(ref int flag, string message)
    {
        if (System.Threading.Interlocked.Exchange(ref flag, 1) == 0)
        {
            LocalMultiControlLogger.Warn(message);
        }
    }
}
