using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>血条面板里一行的来源（决定显示的文案前缀）。</summary>
internal enum HpBadgeEntryKind
{
    /// <summary>召唤物（`PlayerCombatState.Pets`），文案保持 r86 以来的原样：`名字 当前/上限`。</summary>
    Pet = 0,

    /// <summary>战神（临时席位玩家），文案带 `战神·档位` 前缀，便于与召唤物区分。</summary>
    WarGod = 1,
}

/// <summary>
/// 血条面板一行的**数据快照**（纯值类型，不含 Godot 类型 ⇒ 可直接单测）。
/// </summary>
internal readonly struct HpBadgeEntry
{
    internal HpBadgeEntry(HpBadgeEntryKind kind, string? name, int currentHp, int maxHp, bool alive)
    {
        Kind = kind;
        Name = string.IsNullOrWhiteSpace(name) ? "Pet" : name!;

        // 防御：上限至少 1（除零），当前血量不取负值
        MaxHp = maxHp < 1 ? 1 : maxHp;
        CurrentHp = currentHp < 0 ? 0 : currentHp;
        Alive = alive;
    }

    internal HpBadgeEntryKind Kind { get; }

    internal string Name { get; }

    internal int CurrentHp { get; }

    internal int MaxHp { get; }

    internal bool Alive { get; }

    /// <summary>血条填充比例（死亡一律 0 —— 显示成空条 + 置灰，与 r86 一致）。</summary>
    internal float Ratio => Alive ? Math.Clamp((float)CurrentHp / MaxHp, 0f, 1f) : 0f;
}

/// <summary>血条面板的一行**渲染指令**（`IsOverflow` = 折叠提示行，不画血条）。</summary>
internal readonly struct HpBadgeRow
{
    internal HpBadgeRow(string label, float ratio, bool alive, bool isOverflow)
    {
        Label = label ?? string.Empty;
        Ratio = ratio;
        Alive = alive;
        IsOverflow = isOverflow;
    }

    internal string Label { get; }

    internal float Ratio { get; }

    internal bool Alive { get; }

    internal bool IsOverflow { get; }
}

/// <summary>
/// 「玩家状态条旁边的血条面板」的行规划纯逻辑（⑱ 战神血条，2026-10-22）。
///
/// 背景：r86 的面板只画 `PlayerCombatState.Pets`；战神**不是 pet**
/// （它是 `CombatState` 里的临时 Player，有自己的立绘 + 立绘上方血条）⇒ 面板里看不到它。
/// 本策略把「召唤物 ∪ 某绑主名下的在册战神」拼成统一的行表：
/// <list type="bullet">
/// <item>顺序：**召唤物在前、战神在后**（r86 的既有观感不变，只是往下多几行）；</item>
/// <item>文案：召唤物 `名字 当前/上限`（逐字保持不变）、战神 `战神·档位 当前/上限`；</item>
/// <item>行数上限：超出时**压成「还有 N 个」一行**（12 席 + 宠物全画会把面板拉出屏幕）。</item>
/// </list>
/// </summary>
internal static class WarGodHpBadgePolicy
{
    /// <summary>面板最多显示几行（含折叠行）。12 席全画会拉出屏幕，所以必须有上限。</summary>
    internal const int DefaultMaxRows = 8;

    /// <summary>战神行前缀。</summary>
    internal const string WarGodPrefix = "战神·";

    /// <summary>
    /// 档位的中文名（⚠ 与预设表同口径：`Rare` = 罕见、`Epic` = 稀有 —— 游戏侧就这么命名）。
    /// </summary>
    internal static string DescribeTier(HellwarWarGodTier tier)
    {
        return tier switch
        {
            HellwarWarGodTier.Common => "普通",
            HellwarWarGodTier.Rare => "罕见",
            HellwarWarGodTier.Epic => "稀有",
            HellwarWarGodTier.Ancient => "先古",
            _ => "未知",
        };
    }

    /// <summary>战神行用的显示名（不含血量）。</summary>
    internal static string DescribeWarGod(HellwarWarGodTier tier) => WarGodPrefix + DescribeTier(tier);

    /// <summary>
    /// 拼行表。<paramref name="maxRows"/> &lt; 2 时按 2 处理（至少要留 1 行内容 + 1 行折叠提示）。
    /// </summary>
    internal static List<HpBadgeRow> BuildRows(
        IReadOnlyList<HpBadgeEntry> pets,
        IReadOnlyList<HpBadgeEntry> warGods,
        int maxRows = DefaultMaxRows)
    {
        List<HpBadgeEntry> all = new();
        if (pets != null)
        {
            all.AddRange(pets);
        }

        if (warGods != null)
        {
            all.AddRange(warGods);
        }

        List<HpBadgeRow> rows = new(all.Count);
        if (all.Count == 0)
        {
            return rows;
        }

        int limit = Math.Max(2, maxRows);
        bool overflow = all.Count > limit;
        int shown = overflow ? limit - 1 : all.Count;

        for (int i = 0; i < shown; i++)
        {
            HpBadgeEntry entry = all[i];
            rows.Add(new HpBadgeRow(
                $"{entry.Name} {entry.CurrentHp}/{entry.MaxHp}",
                entry.Ratio,
                entry.Alive,
                isOverflow: false));
        }

        if (overflow)
        {
            int hidden = all.Count - shown;
            rows.Add(new HpBadgeRow($"还有 {hidden} 个（已折叠）", 0f, alive: true, isOverflow: true));
        }

        return rows;
    }
}
