namespace ThirdPartyHotPathFix.Scripts.Gates;

/// <summary>
/// 战斗内「谁带着压制类能力」快照（纯逻辑，不碰 Godot / 游戏类型，可单测）。
///
/// 失效键 = (战斗实例, 能力版本号, 我方生物数, 敌方生物数)。
/// 版本号由 <c>Creature.ApplyPowerInternal</c> / <c>RemovePowerInternal</c> 的后缀递增
/// —— 因此这个缓存**不会过期**（不像按帧 / 按毫秒分桶那样有 ≤N ms 的滞后窗口）：
/// 新能力挂上的下一次 hook 分发就能被看见，能力移除同理。
///
/// 并发：hook 分发是 async 链，续体可能落在其它线程上 ⇒ 快照必须**整体发布**
/// （一个不可变 Entry 引用，`volatile` 读写），不能散在多个字段里 ——
/// 否则读者可能拼出「新键 + 旧判定」= 在机制在场时错误地跳过第三方过滤链。
/// </summary>
internal sealed class CombatSnapshotCache
{
    private sealed class Entry
    {
        public required object Owner { get; init; }

        public required long Version { get; init; }

        public required int Allies { get; init; }

        public required int Enemies { get; init; }

        public required bool Koishi { get; init; }

        public required bool Saya { get; init; }

        public required bool IronCavalry { get; init; }
    }

    private volatile Entry? _entry;

    /// <summary>命中缓存返回 true（三个 armed 结果一并取出）。</summary>
    public bool TryGet(
        object owner,
        long version,
        int allies,
        int enemies,
        out bool koishi,
        out bool saya,
        out bool ironCavalry)
    {
        Entry? entry = _entry;
        if (entry != null
            && ReferenceEquals(entry.Owner, owner)
            && entry.Version == version
            && entry.Allies == allies
            && entry.Enemies == enemies)
        {
            koishi = entry.Koishi;
            saya = entry.Saya;
            ironCavalry = entry.IronCavalry;
            return true;
        }

        koishi = false;
        saya = false;
        ironCavalry = false;
        return false;
    }

    public void Commit(
        object owner,
        long version,
        int allies,
        int enemies,
        bool koishi,
        bool saya,
        bool ironCavalry)
    {
        _entry = new Entry
        {
            Owner = owner,
            Version = version,
            Allies = allies,
            Enemies = enemies,
            Koishi = koishi,
            Saya = saya,
            IronCavalry = ironCavalry,
        };
    }

    public void Reset() => _entry = null;
}
