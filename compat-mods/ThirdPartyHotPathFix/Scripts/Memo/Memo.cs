using System;
using System.Collections.Generic;

namespace ThirdPartyHotPathFix.Scripts.Memo;

/// <summary>
/// 纯函数记忆化（纯逻辑，可单测）。
///
/// 用途：把第三方 / 原版里**每次调用都重算的纯函数**（字符串归一化、类型名解析）只算一次。
/// 12 席规模下这些函数每秒被调用数千次，实测占主线程 5.2%（RitsuLib 的
/// `NormalizePublicStem` 里 4 个 `Regex.Replace`）+ 4.3%（YUI 的 `FindType` 每次
/// `AppDomain.GetAssemblies()` + `Assembly.GetType`）+ 1.2%（原版 `StringHelper.Slugify`）。
///
/// 三条不许忘的规则：
/// 1. **只缓存成功结果**（null / 抛异常一律不缓存）——否则「类型还没加载」「参数非法」这类
///    一次性失败会被永久钉死；
/// 2. 键为 null 时不缓存也不命中（原函数对 null 的行为各异，别替它下结论）；
/// 3. 有容量上限：到顶后只更新已有键、不再新增，避免长时间存档把字典撑大。
/// </summary>
internal sealed class Memo<TValue>
    where TValue : class
{
    private readonly Dictionary<string, TValue> _cache;
    private readonly int _maxEntries;

    /// <param name="maxEntries">最多缓存多少条（到顶后只更新不新增）。</param>
    /// <param name="comparer">键比较器；默认序数比较（id / 类型名都是 ASCII）。</param>
    public Memo(int maxEntries = 20000, IEqualityComparer<string>? comparer = null)
    {
        _maxEntries = maxEntries;
        _cache = new Dictionary<string, TValue>(comparer ?? StringComparer.Ordinal);
    }

    /// <summary>命中次数（用来确认补丁真的生效）。</summary>
    public long Hits { get; private set; }

    /// <summary>未命中次数。</summary>
    public long Misses { get; private set; }

    public int Count => _cache.Count;

    public bool TryGet(string? key, out TValue value)
    {
        if (key != null && _cache.TryGetValue(key, out TValue? found))
        {
            Hits++;
            value = found;
            return true;
        }

        Misses++;
        value = null!;
        return false;
    }

    /// <summary>缓存一次成功结果；null 键 / null 值直接忽略。</summary>
    public void Store(string? key, TValue? value)
    {
        if (key == null || value == null)
        {
            return;
        }

        if (_cache.Count >= _maxEntries && !_cache.ContainsKey(key))
        {
            return;
        }

        _cache[key] = value;
    }
}
