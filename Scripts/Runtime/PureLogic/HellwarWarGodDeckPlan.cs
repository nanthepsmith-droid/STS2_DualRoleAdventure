#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace LocalMultiControl.Scripts.Runtime.PureLogic;

/// <summary>
/// 战灵召唤 · **手工预设卡组**的一条（纯数据）：
/// <paramref name="Entry"/> = 卡牌裸 entry（游戏 <c>ModelId.Entry</c>，如 <c>STRIKE_IRONCLAD</c>），
/// <paramref name="Upgraded"/> = 这一张是否升级（配置文本里末尾那个 <c>+</c>），
/// <paramref name="EnchantEntry"/> = 这一张带的**附魔**裸 entry（<c>null</c> = 不带；r277 新增），
/// <paramref name="EnchantAmount"/> = 附魔的"数量 / 强度"（游戏 <c>CardCmd.Enchant</c> 的 amount，默认 1）。
/// </summary>
internal sealed record HellwarWarGodDeckEntry(
    string Entry,
    bool Upgraded,
    string? EnchantEntry = null,
    int EnchantAmount = HellwarWarGodDeckPlan.DefaultEnchantAmount);

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
/// <item>末尾加 <c>[附魔]</c> = 这张带某个**附魔**（<c>ModelId.Entry</c>，如 <c>SHARP</c>）；
///   可写数量 <c>[SHARP:3]</c>（不写 = <b>1</b>）；前缀 <c>ENCHANTMENT.</c> / <c>ENCHANT.</c> 可有可无；
///   <c>+</c> 与 <c>[附魔]</c> **顺序随便**（<c>BASH+[SHARP]</c> 与 <c>BASH[SHARP]+</c> 等价）——
///   两者都从末尾剥，剥干净为止；</item>
/// <item>写几次就是几张（要 3 张就写 3 遍 <c>STRIKE+</c>），**顺序即抽牌堆初始顺序**（洗牌前）；</item>
/// <item><b>整串空白 = 用内置默认卡组</b>（不是"空卡组"）—— 缺省天然如此，所以不改配置也有好卡组。</item>
/// </list>
/// ⚠ 认不出的 entry 由**运行时**跳过并打 WARN（不崩、不阻塞召唤）；若一条都没认出来，回落"角色初始卡组"。
/// ⚠ 附魔同样由运行时校验（<c>EnchantmentModel.CanEnchant</c>）：认不出 / 这张牌不能带 ⇒ 只跳过**那个附魔**，卡照给。
/// 本类只做"文本 → 条目表"，**不碰游戏类型**（<c>ModelDb</c> 查表在 <c>HellwarWarGodDecks</c>）。
/// </summary>
internal static class HellwarWarGodDeckPlan
{
    /// <summary>升级标记（写在条目末尾）。</summary>
    internal const char UpgradeMarker = '+';

    /// <summary>附魔段的左右括号（写在条目末尾，如 <c>BASH[SHARP]</c>）。</summary>
    internal const char EnchantOpenMarker = '[';

    /// <inheritdoc cref="EnchantOpenMarker"/>
    internal const char EnchantCloseMarker = ']';

    /// <summary>附魔数量分隔符（<c>[SHARP:3]</c> 里的冒号）。</summary>
    internal const char EnchantAmountSeparator = ':';

    /// <summary>不写数量时的默认值（游戏侧多数附魔就是传 <c>1m</c>，见 <c>CardCmd.Enchant</c> 的调用点）。</summary>
    internal const int DefaultEnchantAmount = 1;

    /// <summary>卡牌 id 的 category 前缀（<c>ModelId</c> 的拼法；可选，大小写不敏感）。</summary>
    internal const string CardIdPrefix = "CARD.";

    /// <summary>附魔 id 的 category 前缀（可选，大小写不敏感；两种都收，实机见 <c>ModelId.SlugifyCategory</c>）。</summary>
    internal static readonly string[] EnchantIdPrefixes = { "ENCHANTMENT.", "ENCHANT." };

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
            string? enchant = null;
            int enchantAmount = DefaultEnchantAmount;

            // `+`（升级）与 `[附魔]` 都从**末尾**剥，交替剥到剥不动为止 ⇒ 两种顺序都能写。
            // 只认最右边那一段附魔（多写 `[A][B]` 时后一个会被剥掉但忽略，见下面的说明）。
            bool changed = true;
            while (changed)
            {
                changed = false;
                token = token.Trim();

                if (token.EndsWith(EnchantCloseMarker) && TrySplitEnchant(token, out string head, out string inner))
                {
                    token = head;
                    if (enchant == null)
                    {
                        ParseEnchant(inner, out string enchantEntry, out int amount);
                        if (enchantEntry.Length > 0)
                        {
                            enchant = enchantEntry;
                            enchantAmount = amount;
                        }
                    }

                    changed = true;
                    continue;
                }

                if (token.EndsWith(UpgradeMarker))
                {
                    upgraded = true;
                    token = token[..^1].Trim();
                    changed = true;
                }
            }

            if (token.StartsWith(CardIdPrefix, StringComparison.OrdinalIgnoreCase))
            {
                token = token[CardIdPrefix.Length..];
            }

            if (token.Length == 0)
            {
                continue;
            }

            entries.Add(new HellwarWarGodDeckEntry(token.ToUpperInvariant(), upgraded, enchant, enchantAmount));
        }

        return entries.Count == 0 ? null : entries;
    }

    /// <summary>
    /// 末尾是一段 <c>[...]</c> 时把它切成"前面那截 + 括号里的内容"。
    /// ⚠ 括号**不配对**（如 <c>BASH[</c>）⇒ 返回 false，整串原样留着 ⇒ 运行时按"认不出的卡"WARN，
    /// 不会静默把卡名吃掉。
    /// </summary>
    private static bool TrySplitEnchant(string token, out string head, out string inner)
    {
        head = token;
        inner = string.Empty;

        int open = token.LastIndexOf(EnchantOpenMarker);
        if (open < 0 || open == token.Length - 1)
        {
            return false;
        }

        head = token[..open].Trim();
        inner = token[(open + 1)..^1].Trim();
        return head.Length > 0;
    }

    /// <summary>解析附魔段内容：<c>SHARP</c> / <c>SHARP:3</c> / <c>ENCHANTMENT.SHARP</c>。</summary>
    private static void ParseEnchant(string inner, out string enchantEntry, out int amount)
    {
        enchantEntry = string.Empty;
        amount = DefaultEnchantAmount;

        if (inner.Length == 0)
        {
            return;
        }

        string entry = inner;
        int separator = inner.IndexOf(EnchantAmountSeparator);
        if (separator >= 0)
        {
            entry = inner[..separator].Trim();
            string amountText = inner[(separator + 1)..].Trim();
            if (int.TryParse(amountText, out int parsed) && parsed > 0)
            {
                amount = parsed;
            }
        }

        foreach (string prefix in EnchantIdPrefixes)
        {
            if (entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                entry = entry[prefix.Length..];
                break;
            }
        }

        enchantEntry = entry.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// 把条目表**格式化回配置文本**（逗号分隔、升级带 <c>+</c>、附魔带 <c>[附魔]</c>）——
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

            if (!string.IsNullOrEmpty(entries[i].EnchantEntry))
            {
                builder.Append(EnchantOpenMarker).Append(entries[i].EnchantEntry);
                if (entries[i].EnchantAmount != DefaultEnchantAmount)
                {
                    builder.Append(EnchantAmountSeparator).Append(entries[i].EnchantAmount);
                }

                builder.Append(EnchantCloseMarker);
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
