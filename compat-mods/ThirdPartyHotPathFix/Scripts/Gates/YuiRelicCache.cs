namespace ThirdPartyHotPathFix.Scripts.Gates;

/// <summary>
/// YUICore 遗物触发复读链的「有没有相关遗物」快照（纯逻辑，可单测）。
///
/// 失效键 = (运行态/战斗实例, 所有玩家遗物总数)。
/// 遗物只能通过「获得 / 失去」改变数量，所以这个键足以覆盖
/// <c>IRelicTriggerRepeater</c> / <c>IAfterRelicTriggeredHook</c> 的出现与消失；
/// 而 `IsMelted` / `IsUsedUp` 之类只影响 <c>CanReceiveCustomHook</c>（决定谁收到通知），
/// 不改变「有没有这类遗物」这个类型级判定 —— 因此不需要进键。
///
/// 并发：与 <see cref="CombatSnapshotCache"/> 同理，键与值放进同一个不可变对象整体发布，
/// 避免读到「新键 + 旧结论」。
/// </summary>
internal sealed class YuiRelicCache
{
    private sealed class Entry
    {
        public required object Owner { get; init; }

        public required int RelicCount { get; init; }

        public required bool Armed { get; init; }
    }

    private volatile Entry? _entry;

    /// <summary>命中缓存返回 true（<paramref name="armed"/> = 场上有人持有相关遗物）。</summary>
    public bool TryGet(object owner, int relicCount, out bool armed)
    {
        Entry? entry = _entry;
        if (entry != null && ReferenceEquals(entry.Owner, owner) && entry.RelicCount == relicCount)
        {
            armed = entry.Armed;
            return true;
        }

        armed = false;
        return false;
    }

    public void Commit(object owner, int relicCount, bool armed)
    {
        _entry = new Entry { Owner = owner, RelicCount = relicCount, Armed = armed };
    }

    public void Reset() => _entry = null;
}
