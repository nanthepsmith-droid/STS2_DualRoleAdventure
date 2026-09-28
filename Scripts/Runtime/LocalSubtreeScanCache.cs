using System;
using System.Collections.Generic;
using Godot;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 子树扫描的短期缓存（R2 去重复 + 消掉每帧遍历）。
///
/// 给挂在 `_Process` 上的调用点用：同一个根节点在一小段时间内重复扫描必然得到同一批节点，
/// 没必要每帧把整棵子树遍历一遍。TTL 内直接复用上次结果；超时、根被释放、或缓存里的节点失效
/// 都会触发重扫。
///
/// 换来的代价：节点树在 TTL 内新增/删除的节点最多晚 <see cref="RescanIntervalMsec"/> 毫秒被看到。
/// 三处调用点（战斗玩家状态条 / 大厅席位卡 / 每日页席位卡）本身都是逐帧幂等重试，
/// 且席位卡的增删带 UI 动画，这点延迟不影响任何实机契约（锚点与「期望 0」都不看时序）。
///
/// 返回的列表是缓存内部实例，**调用方只读**（需要排序/投影请自行生成新集合）。
/// </summary>
internal static class LocalSubtreeScanCache
{
    /// <summary>重扫间隔（毫秒）。见类注释里对代价的说明。</summary>
    private const long RescanIntervalMsec = 500;

    private sealed class Entry<T> where T : Node
    {
        internal Node Root = null!;
        internal IReadOnlyList<T> Nodes = Array.Empty<T>();
        internal long ScannedAtMsec;
    }

    private static readonly Dictionary<(ulong RootId, Type NodeType), object> Entries = new();

    /// <summary>扫描 <paramref name="root"/> 子树里的全部 <typeparamref name="T"/>（带 TTL 缓存）。</summary>
    internal static IReadOnlyList<T> Scan<T>(Node root) where T : Node
    {
        (ulong, Type) key = (root.GetInstanceId(), typeof(T));
        long now = (long)Time.GetTicksMsec();
        if (Entries.TryGetValue(key, out object? boxed) && boxed is Entry<T> cached && IsUsable(cached, root, now))
        {
            return cached.Nodes;
        }

        List<T> nodes = new();
        foreach (Node node in LocalNodeTree.EnumerateDescendants(root))
        {
            if (node is T typed)
            {
                nodes.Add(typed);
            }
        }

        Entries[key] = new Entry<T>
        {
            Root = root,
            Nodes = nodes,
            ScannedAtMsec = now
        };
        return nodes;
    }

    private static bool IsUsable<T>(Entry<T> entry, Node root, long now) where T : Node
    {
        if (!ReferenceEquals(entry.Root, root) || !GodotObject.IsInstanceValid(root))
        {
            return false;
        }

        if (now - entry.ScannedAtMsec > RescanIntervalMsec)
        {
            return false;
        }

        foreach (T node in entry.Nodes)
        {
            if (!GodotObject.IsInstanceValid(node))
            {
                return false;
            }
        }

        return true;
    }
}
