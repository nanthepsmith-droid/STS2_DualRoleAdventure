using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.TestSupport;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库托管选择器的**归属者注册表**（改进-2 / Phase 1）。
///
/// 职责：把「压栈」与「登记归属者」绑定成一个不可分割的操作
/// （<see cref="Open"/> = <c>CardSelectCmd.PushSelector</c> + <see cref="WakuuOwnerSelectorMap{TSelector}"/>.Register），
/// 于是 <c>CardSelectCmd.Selector</c> getter 的守卫可以按**归属者**而不是「栈顶」来分发选择器：
/// 两个瓦库的作用域同时存在时，各自的选择各归各，不再被晚压入的一方抢答。
///
/// 为什么需要它：游戏的选择器栈是全局静态、getter 只给栈顶、<c>ICardSelector</c> 不带归属者信息
/// （详见 <c>maintenance-docs/decision-records/多瓦库并行托管可行性与方案.md</c> §4）。
///
/// 与旧行为的兼容性：<see cref="TryGet"/> 未命中时守卫退回既有三条老路
/// （真人摘掉 / 瓦库放过 / 保持栈顶），因此**单瓦库、单作用域下行为与升级前完全等价**。
///
/// 清理：原版 <c>CardSelectCmd.Reset()</c>（run cleanup）会清掉游戏自己的选择器栈，
/// <c>CardSelectCmdResetRegistryPatch</c> 同步调用 <see cref="Reset"/>，
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

        IDisposable stackScope = CardSelectCmd.PushSelector(selector);

        IDisposable registration;
        try
        {
            registration = Map.Register(ownerId, selector);
        }
        catch
        {
            // 登记失败不能留下「已压栈但无归属」的选择器（那正是抢答的来源）
            stackScope.Dispose();
            throw;
        }

        return new OpenScope(registration, stackScope);
    }

    /// <summary>按归属者取其当前生效的托管选择器（最内层）。未登记返回 false。</summary>
    internal static bool TryGet(ulong ownerId, out ICardSelector? selector)
    {
        return Map.TryGet(ownerId, out selector);
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

        internal OpenScope(IDisposable registration, IDisposable stackScope)
        {
            _registration = registration;
            _stackScope = stackScope;
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
        }
    }
}
