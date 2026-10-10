#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace LocalMultiControl.Scripts.Runtime.PureLogic;

/// <summary>
/// 战灵召唤 · **手工预设卡组**的一条（纯数据）：
/// <paramref name="Entry"/> = 卡牌裸 entry（游戏 <c>ModelId.Entry</c>，如 <c>STRIKE_IRONCLAD</c>），
/// <paramref name="Upgraded"/> = 这一张是否升级（配置文本里末尾那个 <c>+</c>）。
/// </summary>
internal sealed record HellwarWarGodDeckEntry(string Entry, bool Upgraded);

/// <summary>
/// 战灵召唤 · **手工预设卡组**的「配置文本 ⇄ 条目表」解析层（纯逻辑、无游戏类型、可单测）。
///
/// 背景（2026-10-10 用户拍板）：战神用什么卡组不再走"原版角色初始卡组 + 全升级"，
/// 改成**手工挑的 id 列表**，并且**开放到配置文件**（用户："自己想不出好卡组" ⇒ 我们出好默认、他能改）。
///
/// 文本格式（写进 <c>vakuu_autopilot.json</c> 的 <c>hellwarDeck*</c> 键，例如 <c>hellwarDeckRare</c>）：
/// <code>
/// "hellwarDeckRare": "NEUTRALIZE, POISONED_STAB, POISONED_STAB+, DEADLY_POISON, BACKFLIP, ..."
/// </code>
/// <list type="bullet">
/// <item>分隔符：逗号 / 分号 / 竖线 / 任意空白（含换行）都可以 —— 一行一张也行；</item>
/// <item>每项 = 卡牌的**裸 entry**（<c>ModelId.Entry</c>，如 <c>STRIKE_IRONCLAD</c>）；
///   前缀 <c>CARD.</c> 可有可无（大小写不敏感）；</item>
/// <item>末尾加 <c>+</c> = 这张**单独升级**（如 <c>BACKFLIP+</c>）；不加则按该档位的"全升级"开关走；
///   <c>+</c> 可以叠写（<c>BACKFLIP++</c> 等价于 <c>BACKFLIP+</c>）；</item>
/// <item>写几次就是几张（要 3 张就写 3 遍 <c>STRIKE+</c>），**顺序即抽牌堆初始顺序**（洗牌前）；</item>
/// <item><b>整串空白 = 用内置默认卡组</b>（不是"空卡组"）—— 缺省天然如此，所以不改配置也有好卡组。</item>
/// </list>
/// ⚠ 认不出的 entry 由**运行时**跳过并打 WARN（不崩、不阻塞召唤）；若一条都没认出来，回落"角色初始卡组"。
/// 本类只做"文本 → 条目表"，**不碰游戏类型**（<c>ModelDb</c> 查表在 <c>HellwarWarGodDecks</c>）。
/// </summary>
internal static class HellwarWarGodDeckPlan
{
    /// <summary>升级标记（写在条目末尾）。</summary>
    internal const char UpgradeMarker = '+';

    /// <summary>卡牌 id 的 category 前缀（<c>ModelId</c> 的拼法；可选，大小写不敏感）。</summary>
    internal const string CardIdPrefix = "CARD.";

    private static readonly char[] Separators = { ',', ';', '|', ' ', '\t', '\r', '\n' };

    /// <summary>
    /// 解析配置文本。
    /// 返回 <c>null</c> = "没配置"（空串 / 全空白 / 全是分隔符）⇒ 调用方用**内置默认卡组**；
    /// 返回非空列表 = 玩家自定义的条目表（可能含认不出的 entry，交给运行时过滤）。
    /// </summary>
    internal static IReadOnlyList<HellwarWarGodDeckEntry>? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        List<HellwarWarGodDeckEntry> entries = new();
        foreach (string chunk in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            string token = chunk.Trim();

            bool upgraded = false;
            while (token.EndsWith(UpgradeMarker))
            {
                upgraded = true;
                token = token[..^1].Trim();
            }

            if (token.StartsWith(CardIdPrefix, StringComparison.OrdinalIgnoreCase))
            {
                token = token[CardIdPrefix.Length..];
            }

            if (token.Length == 0)
            {
                continue;
            }

            entries.Add(new HellwarWarGodDeckEntry(token.ToUpperInvariant(), upgraded));
        }

        return entries.Count == 0 ? null : entries;
    }

    /// <summary>
    /// 把条目表**格式化回配置文本**（逗号分隔、升级带 <c>+</c>）——
    /// 日志里直接给玩家一份"复制粘贴就能改"的模板（内置默认卡组也用它打印）。
    /// </summary>
    internal static string Format(IReadOnlyList<HellwarWarGodDeckEntry>? entries)
    {
        if (entries == null || entries.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        for (int i = 0; i < entries.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(entries[i].Entry);
            if (entries[i].Upgraded)
            {
                builder.Append(UpgradeMarker);
            }
        }

        return builder.ToString();
    }

    /// <summary>日志用一行摘要（张数 + 配置文本）。</summary>
    internal static string Describe(IReadOnlyList<HellwarWarGodDeckEntry>? entries)
        => entries == null ? "（无）" : $"{entries.Count} 张: {Format(entries)}";

    /// <summary>把裸 entry 列表包成"都不升级"的条目表（内置默认卡组的 typed 列表用）。</summary>
    internal static IReadOnlyList<HellwarWarGodDeckEntry> Wrap(IEnumerable<string> entries)
    {
        List<HellwarWarGodDeckEntry> list = new();
        foreach (string entry in entries)
        {
            list.Add(new HellwarWarGodDeckEntry(entry, Upgraded: false));
        }

        return list;
    }
}
