using System;
using System.Collections.Generic;
using Godot;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「按玩家 id 找游戏原生 id 标签」的引用缓存（R2）。
///
/// ⚠ 这个缓存**只给「找稳定子节点引用」用**，不能拿去做写后读或每帧布局。
/// R2 第一版曾把它当通用子树扫描缓存，结果每日页席位卡补建（写后立刻读「已有哪些卡」）
/// 在 TTL 内看不到自己刚建的卡 ⇒ 每帧重复补建（实测一局 `席位卡已补建` **199 次**、
/// `added` 累计 **342 张**，玩家看到的正是「选人界面无限玩家」）；同一原因让席位卡集合每帧抖动，
/// 列布局反复重算 ⇒ 席位卡上的切人/瓦库按钮位置漂移。
/// **结论：写后读与布局计算必须实时遍历**（见 `LocalNodeTree.EnumerateDescendants`），
/// 只有「节点引用在会话内稳定」的场景（战斗玩家状态条的 id 标签、大厅席位卡的 id 标签）才允许缓存。
///
/// 命中条件：同一个根、同一个玩家 id 文本、标签仍有效且仍在该根子树内；
/// 未命中（确实找不到标签）时带 TTL 重扫，避免在"这页压根没有标签"时白白反复全树遍历。
/// </summary>
internal static class LocalIdLabelCache
{
    /// <summary>未命中时的重扫间隔（毫秒）。</summary>
    private const long MissRescanIntervalMsec = 500;

    private sealed class Entry
    {
        internal Node Root = null!;
        internal string PlayerIdText = string.Empty;
        internal Label? Label;
        internal long ScannedAtMsec;
    }

    private static readonly Dictionary<ulong, Entry> Entries = new();

    internal static Label? Find(Node root, string playerIdText, StringName[]? excludedNames = null)
    {
        long now = (long)Time.GetTicksMsec();
        if (Entries.TryGetValue(root.GetInstanceId(), out Entry? entry)
            && ReferenceEquals(entry.Root, root)
            && entry.PlayerIdText == playerIdText
            && IsUsable(entry, root, now))
        {
            return entry.Label;
        }

        Label? found = LocalNodeTree.FindIdLabel(root, playerIdText, excludedNames);
        Entries[root.GetInstanceId()] = new Entry
        {
            Root = root,
            PlayerIdText = playerIdText,
            Label = found,
            ScannedAtMsec = now
        };
        return found;
    }

    private static bool IsUsable(Entry entry, Node root, long now)
    {
        if (!GodotObject.IsInstanceValid(root))
        {
            return false;
        }

        if (entry.Label == null)
        {
            // 没找到：TTL 内沿用「没有」，超时后重扫（页面可能刚建好标签）
            return now - entry.ScannedAtMsec <= MissRescanIntervalMsec;
        }

        return GodotObject.IsInstanceValid(entry.Label) && IsUnder(entry.Label, root);
    }

    private static bool IsUnder(Node node, Node root)
    {
        Node? current = node;
        for (int depth = 0; depth < 64 && current != null; depth++)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }

            current = current.GetParent();
        }

        return false;
    }
}
