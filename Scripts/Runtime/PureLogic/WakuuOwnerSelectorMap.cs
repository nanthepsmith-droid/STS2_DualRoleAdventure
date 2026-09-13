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
/// </summary>
internal sealed class WakuuOwnerSelectorMap<TSelector> where TSelector : class
{
    private readonly object _lock = new();
    private readonly Dictionary<ulong, List<Entry>> _byOwner = new();

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
            lock (_lock)
            {
                int total = 0;
                foreach (List<Entry> list in _byOwner.Values)
                {
                    total += list.Count;
                }

                return total;
            }
        }
    }

    /// <summary>被登记的归属者数量（去重后的玩家数）。</summary>
    public int OwnerCount
    {
        get
        {
            lock (_lock)
            {
                return _byOwner.Count;
            }
        }
    }

    /// <summary>
    /// 登记一个归属者的托管选择器，返回的 scope 释放时退订本条（幂等，重复释放无副作用）。
    /// </summary>
    public IDisposable Register(ulong ownerId, TSelector selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        Entry entry = new(selector);
        lock (_lock)
        {
            if (!_byOwner.TryGetValue(ownerId, out List<Entry>? list))
            {
                list = new List<Entry>();
                _byOwner[ownerId] = list;
            }

            list.Add(entry);
        }

        return new Registration(this, ownerId, entry);
    }

    /// <summary>按归属者取其**最内层**（最后登记）的选择器；无登记返回 false。</summary>
    public bool TryGet(ulong ownerId, out TSelector? selector)
    {
        lock (_lock)
        {
            if (_byOwner.TryGetValue(ownerId, out List<Entry>? list) && list.Count > 0)
            {
                selector = list[list.Count - 1].Selector;
                return true;
            }
        }

        selector = null;
        return false;
    }

    /// <summary>该归属者当前是否有登记。</summary>
    public bool Contains(ulong ownerId)
    {
        lock (_lock)
        {
            return _byOwner.TryGetValue(ownerId, out List<Entry>? list) && list.Count > 0;
        }
    }

    /// <summary>清空整表（运行清理时调用，防止泄漏条目跨局生效）。返回被清掉的条目数。</summary>
    public int Reset()
    {
        lock (_lock)
        {
            int cleared = 0;
            foreach (List<Entry> list in _byOwner.Values)
            {
                cleared += list.Count;
            }

            _byOwner.Clear();
            return cleared;
        }
    }

    /// <summary>当前各归属者的最内层选择器快照（仅供诊断日志，顺序按 NetId 升序）。</summary>
    public IReadOnlyList<KeyValuePair<ulong, TSelector>> SnapshotTop()
    {
        List<KeyValuePair<ulong, TSelector>> snapshot = new();
        lock (_lock)
        {
            foreach (KeyValuePair<ulong, List<Entry>> pair in _byOwner)
            {
                if (pair.Value.Count > 0)
                {
                    snapshot.Add(new KeyValuePair<ulong, TSelector>(pair.Key, pair.Value[pair.Value.Count - 1].Selector));
                }
            }
        }

        snapshot.Sort((a, b) => a.Key.CompareTo(b.Key));
        return snapshot;
    }

    private void Unregister(ulong ownerId, Entry entry)
    {
        lock (_lock)
        {
            if (!_byOwner.TryGetValue(ownerId, out List<Entry>? list))
            {
                return;
            }

            // 按引用移除：乱序释放（内层先于外层释放）也能正确定位自己的那一条
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(list[i], entry))
                {
                    list.RemoveAt(i);
                    break;
                }
            }

            if (list.Count == 0)
            {
                _byOwner.Remove(ownerId);
            }
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
