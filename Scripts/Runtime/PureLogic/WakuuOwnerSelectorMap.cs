using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「选牌归属者 → 其当前生效的托管选择器」注册表（改进-2 / Phase 1 的纯逻辑核心，可单测）。
///
/// 背景：游戏的选择器栈（<c>CardSelectCmd._selectorStack</c>）是**全局静态**，getter 只返回栈顶，
/// 且 <c>ICardSelector</c> 不带任何归属者信息 —— 因此「谁是这次选牌的归属者」只能靠外部登记。
/// 单瓦库 / 单作用域时「栈顶 == 唯一那个」尚可工作；一旦两个瓦库的作用域同时存在（Phase 2' 准并行的目标），
/// 栈顶就会被晚压入的那一方抢走（抢答）。本表把「按归属者查表」这条路由作为唯一事实来源。
///
/// 语义约定：
/// 1. **同一归属者可以嵌套登记**（如同一瓦库的出牌作用域内又压了一个药水选择器），
///    <see cref="TryGet"/> 取**最内层**（最后登记）那个，与选择器栈的栈顶语义一致；
/// 2. 释放**按引用**移除，不依赖「自己是否仍是最后一个」——乱序释放也不会误删别人的条目；
/// 3. 纯内存结构、无游戏类型依赖（泛型承载任意选择器类型），供单测用普通对象驱动。
///
/// ⚠ r332（2026-10-31 采样）**读路径必须无锁**：`LocalContext.IsMe` 的放行补丁每被第三方问一次
/// 就要查一次本表，而 figure_Saya / Koishi / newsanguo 的过滤器链让它**每秒上万次** ⇒
/// 旧实现每次 `lock` 都进监视器（12 个托管席位并发压栈时还有真实竞争），
/// 采样里 `TryGet` 一个人占主线程 **5.6%**。现改为**写时复制快照**：
/// `_snapshot` 是一个不可变字典引用，写操作在锁内构造新字典再整体替换，读操作直接读引用（零锁、零分配）。
/// 登记/退订只发生在选牌窗口边缘（纳秒级频率），复制成本可忽略。
/// </summary>
internal sealed class WakuuOwnerSelectorMap<TSelector> where TSelector : class
{
    /// <summary>只保护"构造新快照并替换"这段写操作；读路径完全不碰它。</summary>
    private readonly object _writeLock = new();

    /// <summary>
    /// 不可变快照（写时复制）：字典与其内部的 <see cref="List{T}"/> 一旦发布就**不再修改**，
    /// 因此读线程拿到的永远是一份自洽视图。
    /// </summary>
    private volatile Dictionary<ulong, List<Entry>> _snapshot = new();

    private sealed class Entry
    {
        internal Entry(TSelector selector)
        {
            Selector = selector;
        }

        internal TSelector Selector { get; }
    }

    /// <summary>当前登记的条目总数（含嵌套），供诊断与清理日志使用。</summary>
    public int Count
    {
        get
        {
            int total = 0;
            foreach (List<Entry> list in _snapshot.Values)
            {
                total += list.Count;
            }

            return total;
        }
    }

    /// <summary>被登记的归属者数量（去重后的玩家数）。</summary>
    public int OwnerCount => _snapshot.Count;

    /// <summary>
    /// 登记一个归属者的托管选择器，返回的 scope 释放时退订本条（幂等，重复释放无副作用）。
    /// </summary>
    public IDisposable Register(ulong ownerId, TSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        Entry entry = new(selector);
        lock (_writeLock)
        {
            Dictionary<ulong, List<Entry>> next = new(_snapshot);
            if (next.TryGetValue(ownerId, out List<Entry>? existing))
            {
                List<Entry> grown = new(existing.Count + 1);
                grown.AddRange(existing);
                grown.Add(entry);
                next[ownerId] = grown;
            }
            else
            {
                next[ownerId] = new List<Entry>(1) { entry };
            }

            _snapshot = next;
        }

        return new Registration(this, ownerId, entry);
    }

    /// <summary>按归属者取其**最内层**（最后登记）的选择器；无登记返回 false。</summary>
    public bool TryGet(ulong ownerId, out TSelector? selector)
    {
        Dictionary<ulong, List<Entry>> snapshot = _snapshot;
        if (snapshot.TryGetValue(ownerId, out List<Entry>? list) && list.Count > 0)
        {
            selector = list[list.Count - 1].Selector;
            return true;
        }

        selector = null;
        return false;
    }

    /// <summary>该归属者当前是否有登记。</summary>
    public bool Contains(ulong ownerId)
    {
        return _snapshot.TryGetValue(ownerId, out List<Entry>? list) && list.Count > 0;
    }

    /// <summary>
    /// 某个选择器实例**是否由本表登记**（按引用比对，任意归属者/任意嵌套层级）。
    /// 用途：选择器守卫要判"栈顶是不是托管选择器" —— 用类型白名单会漏掉后加的实现
    /// （火堆锻造的 <c>LocalWakuuSmithSelector</c>、定向选择器 <c>LocalWakuuTargetedCardSelector</c>
    /// 都曾因此被漏掉），按登记身份判则与"谁压的栈"天然一致。
    /// </summary>
    public bool IsRegistered(TSelector selector)
    {
        if (selector == null)
        {
            return false;
        }

        foreach (List<Entry> list in _snapshot.Values)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i].Selector, selector))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>清空整表（运行清理时调用，防止泄漏条目跨局生效）。返回被清掉的条目数。</summary>
    public int Reset()
    {
        lock (_writeLock)
        {
            Dictionary<ulong, List<Entry>> current = _snapshot;
            int cleared = 0;
            foreach (List<Entry> list in current.Values)
            {
                cleared += list.Count;
            }

            _snapshot = new Dictionary<ulong, List<Entry>>();
            return cleared;
        }
    }

    /// <summary>当前各归属者的最内层选择器快照（仅供诊断日志，顺序按 NetId 升序）。</summary>
    public IReadOnlyList<KeyValuePair<ulong, TSelector>> SnapshotTop()
    {
        List<KeyValuePair<ulong, TSelector>> snapshot = new();
        foreach (KeyValuePair<ulong, List<Entry>> pair in _snapshot)
        {
            if (pair.Value.Count > 0)
            {
                snapshot.Add(new KeyValuePair<ulong, TSelector>(pair.Key, pair.Value[pair.Value.Count - 1].Selector));
            }
        }

        snapshot.Sort((a, b) => a.Key.CompareTo(b.Key));
        return snapshot;
    }

    private void Unregister(ulong ownerId, Entry entry)
    {
        lock (_writeLock)
        {
            if (!_snapshot.TryGetValue(ownerId, out List<Entry>? list))
            {
                return;
            }

            // 按引用移除：乱序释放（内层先于外层释放）也能正确定位自己的那一条
            int index = -1;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(list[i], entry))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return;
            }

            Dictionary<ulong, List<Entry>> next = new(_snapshot);
            if (list.Count == 1)
            {
                next.Remove(ownerId);
            }
            else
            {
                List<Entry> shrunk = new(list.Count - 1);
                for (int i = 0; i < list.Count; i++)
                {
                    if (i != index)
                    {
                        shrunk.Add(list[i]);
                    }
                }

                next[ownerId] = shrunk;
            }

            _snapshot = next;
        }
    }

    private sealed class Registration : IDisposable
    {
        private WakuuOwnerSelectorMap<TSelector>? _owner;
        private readonly ulong _ownerId;
        private readonly Entry _entry;

        internal Registration(WakuuOwnerSelectorMap<TSelector> owner, ulong ownerId, Entry entry)
        {
            _owner = owner;
            _ownerId = ownerId;
            _entry = entry;
        }

        public void Dispose()
        {
            WakuuOwnerSelectorMap<TSelector>? owner = _owner;
            if (owner == null)
            {
                return; // 幂等：重复释放直接返回
            }

            _owner = null;
            owner.Unregister(_ownerId, _entry);
        }
    }
}
