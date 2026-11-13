using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库托管选择器的**归属者注册表**（改进-2 / Phase 1）。
///
/// 职责：把「压栈」与「登记归属者」绑定成一个不可分割的操作
/// （<see cref="Open"/> = <c>CardSelectorStack.PushSelector(player, …)</c> + <see cref="WakuuOwnerSelectorMap{TSelector}"/>.Register），
/// 于是 <c>CardSelectorStack.GetSelector</c> 的守卫可以按**归属者**而不是「栈顶」来分发选择器：
/// 两个瓦库的作用域同时存在时，各自的选择各归各，不再被晚压入的一方抢答。
///
/// 为什么需要它：0.112 及更早的选择器栈是全局静态、getter 只给栈顶、<c>ICardSelector</c> 不带归属者信息
/// （详见 <c>maintenance-docs/decision-records/多瓦库并行托管可行性与方案.md</c> §4）。
/// **0.113 起上游把栈按玩家槽位拆开**（每个 Player 一个栈），本注册表因此退化为两层用途：
/// ① 诊断/日志里的"谁压了什么"；② 守卫在"结构外成员没有自己的栈"时按 NetId 直接分发。
///
/// 与旧行为的兼容性：<see cref="TryGet"/> 未命中时守卫退回既有三条老路
/// （真人摘掉 / 瓦库放过 / 保持栈顶），因此**单瓦库、单作用域下行为与升级前完全等价**。
///
/// 清理：0.113 删掉了原版 <c>CardSelectCmd.Reset()</c>（改由各 scope 的 Dispose 清自己那个栈），
/// 因此改为由 <c>LocalMultiControlRuntime.OnRunCleanup</c> 直接调用 <see cref="Reset"/>，
/// 防止被卡住的异步链泄漏的条目跨局生效。
/// </summary>
internal static class WakuuSelectorRegistry
{
    private static readonly WakuuOwnerSelectorMap<ICardSelector> Map = new();

    /// <summary>
    /// 压入托管选择器并登记其归属者。返回的 scope 释放时**先退订、再弹栈**（顺序无关紧要，
    /// 但退订必须发生，否则条目会随卡住的异步链泄漏）。
    /// </summary>
    /// <param name="ownerId">本次选牌的归属玩家 NetId（瓦库形态角色）。</param>
    /// <param name="selector">要压栈并登记的托管选择器。</param>
    internal static IDisposable Open(ulong ownerId, ICardSelector selector)
    {
        if (selector == null)
        {
            throw new ArgumentNullException(nameof(selector));
        }

        // 0.113：选择器栈按玩家槽位拆分（`CardSelectorStack.PushSelector(player, selector)`），
        // 所以必须先把归属者 NetId 解析成 Player。解析不到（战神这类结构外成员 / 无局）时**不压栈**，
        // 只登记归属者 —— 此时选择器守卫仍可按 NetId 直接分发（见 CardSelectorStackGetSelectorGuardPatch），
        // 因此"托管选牌是否生效"取决于调用点能不能给出 Player，而不是静默失效。
        Player? owner = RunManager.Instance?.DebugOnlyGetState()?.GetPlayer(ownerId);
        IDisposable? stackScope = owner == null
            ? null
            : WakuuSelectorStackAccess.Push(owner, selector);

        IDisposable registration;
        try
        {
            registration = Map.Register(ownerId, selector);
        }
        catch
        {
            // 登记失败不能留下「已压栈但无归属」的选择器（那正是抢答的来源）
            stackScope?.Dispose();
            throw;
        }

        return new OpenScope(registration, stackScope, selector);
    }

    /// <summary>按归属者取其当前生效的托管选择器（最内层）。未登记返回 false。</summary>
    internal static bool TryGet(ulong ownerId, out ICardSelector? selector)
    {
        return Map.TryGet(ownerId, out selector);
    }

    /// <summary>
    /// 该选择器实例是否由本注册表登记（= 是"托管选择器"）。选择器守卫用它替代类型白名单，
    /// 避免新增托管选择器实现时被静默漏判（详见 <see cref="WakuuOwnerSelectorMap{TSelector}.IsRegistered"/>）。
    /// </summary>
    internal static bool IsRegistered(ICardSelector selector)
    {
        return Map.IsRegistered(selector);
    }

    /// <summary>登记表当前条目数（含嵌套），用于清理期日志。</summary>
    internal static int Count => Map.Count;

    /// <summary>被登记的归属者数量。</summary>
    internal static int OwnerCount => Map.OwnerCount;

    /// <summary>清空登记表，返回被清掉的条目数。</summary>
    internal static int Reset()
    {
        return Map.Reset();
    }

    /// <summary>诊断用：当前各归属者最内层选择器的「NetId:类型名」快照。</summary>
    internal static IReadOnlyList<string> Describe()
    {
        return Map.SnapshotTop()
            .Select(pair => $"{pair.Key}:{pair.Value.GetType().Name}")
            .ToList();
    }

    private sealed class OpenScope : IDisposable
    {
        private IDisposable? _registration;
        private IDisposable? _stackScope;
        private readonly ICardSelector _selector;

        internal OpenScope(IDisposable registration, IDisposable? stackScope, ICardSelector selector)
        {
            _registration = registration;
            _stackScope = stackScope;
            _selector = selector;
        }

        public void Dispose()
        {
            if (_stackScope == null && _registration == null)
            {
                return;
            }

            // 先退订归属者，再弹栈：即使弹栈抛异常，登记表也不会留下指向失效选择器的条目
            IDisposable? registration = _registration;
            _registration = null;
            registration?.Dispose();

            IDisposable? stackScope = _stackScope;
            _stackScope = null;
            stackScope?.Dispose();

            // 并发出牌档（方案 D 第二步）的安全网：两个作用域交错时，先压入的那个释放时已不在栈顶，
            // 原版 scope 不会弹栈 → 会永久残留在全局选择器栈里（之后"栈上无选择器"的判定全部失效）。
            // 按引用补摘一刀；不在栈里 = 空操作，因此单作用域的正常路径行为零变化。
            if (LocalWakuuRelicRuntime.RemoveSelectorFromStackIfPresent(_selector))
            {
                // 并发出牌档下这是**预期路径**（两个瓦库的作用域交错，先释放的那个已不在栈顶），
                // 所以只记 INFO：安全网命中 = 正常重叠，不是异常。
                LocalMultiControlLogger.Info(
                    $"并发出牌作用域释放时选择器仍在全局栈中，已按引用摘除防残留（并发出牌下的预期路径）: "
                    + $"selector={_selector.GetType().Name}, 栈快照={LocalWakuuRelicRuntime.SnapshotSelectorStack().Count} 项");
            }
        }
    }
}
