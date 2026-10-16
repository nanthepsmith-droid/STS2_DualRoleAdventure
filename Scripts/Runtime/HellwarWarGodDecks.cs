#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 预设卡组一条的**落地形态**：canonical 卡模板 + 这一张是否升级
/// + 这一张要带的**附魔**（裸 entry；r277 新增，<c>null</c> = 不带）与附魔数量。
/// </summary>
internal readonly record struct HellwarWarGodDeckCard(
    CardModel Canonical,
    bool Upgraded,
    string? EnchantEntry = null,
    int EnchantAmount = HellwarWarGodDeckPlan.DefaultEnchantAmount);

/// <summary>
/// 预设卡组的解析结果。
/// <paramref name="Cards"/> = <c>null</c> 表示"不替换卡组"（用角色初始卡组）；
/// <paramref name="FromConfig"/> = 这一份是不是来自配置文件（日志 / 排查用）；
/// <paramref name="MissingEntries"/> = 配置里认不出来的 entry（只打 WARN，不阻塞召唤）。
/// </summary>
internal sealed record HellwarWarGodDeckResolution(
    IReadOnlyList<HellwarWarGodDeckCard>? Cards,
    bool FromConfig,
    IReadOnlyList<string> MissingEntries);

/// <summary>
/// 战灵召唤 · **预设卡组**的内容层（手工挑的卡）。
///
/// 两级来源（2026-10-10 用户拍板：手工 id 列表 + 开放到配置文件）：
/// <list type="number">
/// <item><b>内置默认</b>：按档位的"角色皮"各配一副**手工挑过、AI 好驾驭**的卡组，用
///   <c>ModelDb.Card&lt;T&gt;()</c> **类型引用**写死 —— 编译期可校验，改卡池 / 加 mod 都不会静默失效
///   （比字符串 id 安全；字符串只留给玩家在配置里写）；</item>
/// <item><b>配置覆盖</b>：<c>hellwarDeckCommon/Rare/Epic/Ancient</c> 任一非空 ⇒ 用玩家那份（字符串 id，
///   见 <see cref="HellwarWarGodDeckPlan"/>）；空 = 用内置默认。</item>
/// </list>
///
/// ⚠ **普通档内置默认 = 不替换**（<c>null</c>）= 继续用"召唤者自身角色的初始卡组"——
/// 那是唯一一档要用 **mod 角色**内容的（自动兼容），也是"最便宜 ⇒ 最弱"的平衡锚点；
/// 想给它换卡就在配置里写 <c>hellwarDeckCommon</c>。
///
/// ⚠ 挑卡口径（AI 托管）：只挑**线性、只吃能量**的牌（攻击 / 格挡 / 直接增益）；
/// 避开"必须选牌 / 依赖手牌构成 / 依赖出牌顺序"的牌（托管大脑用 <c>CanPlay()</c> 过滤，
/// 但复杂前置牌会让评分与收益脱节，见 references thirdparty/本地多控坑）。
/// </summary>
internal static class HellwarWarGodDecks
{
    /// <summary>entry → canonical 卡模板的懒查表（<c>ModelDb.AllCards</c> 忽略解锁/纪元状态，全量可见）。</summary>
    private static Dictionary<string, CardModel>? _byEntry;

    /// <summary>entry → canonical 附魔的懒查表（<c>ModelDb.DebugEnchantments</c> 去掉 mock，r277）。</summary>
    private static Dictionary<string, EnchantmentModel>? _enchantByEntry;

    /// <summary>
    /// 解析"这一档该用什么卡组"。
    /// <paramref name="configOverride"/> 传该档位的配置文本（空 = 用内置默认）。
    /// </summary>
    internal static HellwarWarGodDeckResolution Resolve(HellwarWarGodTier tier, string? configOverride)
    {
        try
        {
            IReadOnlyList<HellwarWarGodDeckEntry>? configured = HellwarWarGodDeckPlan.Parse(configOverride);
            if (configured != null)
            {
                List<HellwarWarGodDeckCard> cards = new(configured.Count);
                List<string> missing = new();
                foreach (HellwarWarGodDeckEntry entry in configured)
                {
                    CardModel? canonical = LookupByEntry(entry.Entry);
                    if (canonical == null)
                    {
                        missing.Add(entry.Entry);
                        continue;
                    }

                    cards.Add(new HellwarWarGodDeckCard(canonical, entry.Upgraded, entry.EnchantEntry, entry.EnchantAmount));
                }

                if (missing.Count > 0)
                {
                    LocalMultiControlLogger.Warn(
                        $"预设卡组配置里有认不出的卡牌（已跳过，不阻塞召唤）: 档位={tier}, "
                        + $"认不出={missing.Count} 条[{string.Join(", ", missing)}], 采用={cards.Count} 张。"
                        + "（entry 要写卡牌的 ModelId.Entry，如 STRIKE_IRONCLAD；前缀 CARD. 可有可无）");
                }

                if (cards.Count == 0)
                {
                    LocalMultiControlLogger.Warn(
                        $"预设卡组配置一条都没认出来 ⇒ 本档回落角色初始卡组: 档位={tier}, 配置={configOverride}");
                    return new HellwarWarGodDeckResolution(null, FromConfig: true, missing);
                }

                return new HellwarWarGodDeckResolution(cards, FromConfig: true, missing);
            }

            IReadOnlyList<CardModel>? builtin = DefaultDeckOf(tier);
            if (builtin == null)
            {
                return new HellwarWarGodDeckResolution(null, FromConfig: false, Array.Empty<string>());
            }

            List<HellwarWarGodDeckCard> list = new(builtin.Count);
            foreach (CardModel card in builtin)
            {
                list.Add(new HellwarWarGodDeckCard(card, Upgraded: false));
            }

            return new HellwarWarGodDeckResolution(list, FromConfig: false, Array.Empty<string>());
        }
        catch (Exception exception)
        {
            // 配置再脏也不该把召唤链带崩（卡组解析失败 ⇒ 回落角色初始卡组）。
            LocalMultiControlLogger.Warn($"预设卡组解析异常（回落角色初始卡组）: 档位={tier}, error={exception.Message}");
            return new HellwarWarGodDeckResolution(null, FromConfig: false, Array.Empty<string>());
        }
    }

    /// <summary>某档的**内置默认卡组**（<c>null</c> = 不替换 = 用角色初始卡组）。</summary>
    internal static IReadOnlyList<CardModel>? DefaultDeckOf(HellwarWarGodTier tier)
    {
        return tier switch
        {
            HellwarWarGodTier.Rare => SilentDeck(),
            HellwarWarGodTier.Epic => IroncladDeck(),
            HellwarWarGodTier.Ancient => RegentDeck(),
            _ => null,
        };
    }

    /// <summary>
    /// 某档内置默认卡组的**配置文本**（逗号分隔的 entry）—— 设置页说明行与日志用它给玩家一份模板。
    /// 没有内置默认（普通档）时返回空串。
    /// </summary>
    internal static string DescribeBuiltinDeck(HellwarWarGodTier tier)
    {
        IReadOnlyList<CardModel>? deck = DefaultDeckOf(tier);
        if (deck == null)
        {
            return string.Empty;
        }

        return HellwarWarGodDeckPlan.Format(HellwarWarGodDeckPlan.Wrap(deck.Select((card) => card.Id.Entry)));
    }

    /// <summary>按 entry（<c>ModelId.Entry</c>，大小写不敏感）查 canonical 卡模板；查不到返回 null。</summary>
    internal static CardModel? LookupByEntry(string entry)
    {
        _byEntry ??= BuildEntryMap();
        return _byEntry.TryGetValue(entry, out CardModel? card) ? card : null;
    }

    private static Dictionary<string, CardModel> BuildEntryMap()
    {
        // `ModelDb.AllCards` 的官方注释："ignores Unlocks/Epoch state" ⇒ 全量、且**不去重也会 Distinct**，
        // 正是"配置里能写任意卡"需要的口径（含 token / 事件牌 / 无色牌）。
        Dictionary<string, CardModel> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (CardModel card in ModelDb.AllCards)
        {
            map[card.Id.Entry] = card;
        }

        return map;
    }

    /// <summary>
    /// 按 entry（<c>ModelId.Entry</c>，大小写不敏感）查 **canonical 附魔**模板；查不到返回 null。
    ///
    /// <para>数据源 = <c>ModelDb.DebugEnchantments</c> —— 那是全游戏**唯一**的附魔全量入口
    /// （注释原文 *"including mock ones for testing"*），所以**按 <c>IsMock</c> 过滤掉测试用附魔**；
    /// 它基于 <c>AllAbstractModelSubtypes</c> ⇒ **mod 附魔也在里面**。</para>
    /// </summary>
    internal static EnchantmentModel? LookupEnchantmentByEntry(string entry)
    {
        _enchantByEntry ??= BuildEnchantEntryMap();
        return _enchantByEntry.TryGetValue(entry, out EnchantmentModel? enchantment) ? enchantment : null;
    }

    /// <summary>全部可用的附魔 entry（内容目录导出 / 排查用；已排除 mock）。</summary>
    internal static IReadOnlyCollection<string> AllEnchantmentEntries()
    {
        _enchantByEntry ??= BuildEnchantEntryMap();
        return _enchantByEntry.Keys;
    }

    private static Dictionary<string, EnchantmentModel> BuildEnchantEntryMap()
    {
        Dictionary<string, EnchantmentModel> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (EnchantmentModel enchantment in ModelDb.DebugEnchantments)
        {
            if (enchantment.IsMock)
            {
                continue;
            }

            map[enchantment.Id.Entry] = enchantment;
        }

        return map;
    }

    // ------------------------------------------------------------------
    // 内置默认卡组（手工挑，14 张；顺序 = 抽牌堆初始顺序）
    // ------------------------------------------------------------------

    /// <summary>
    /// 罕见档（静默猎手皮，成本 2）：**毒 + 格挡 + 敏捷**的线性组合。
    /// 全部是"只吃能量"的牌，托管 AI 不会用废（弃牌 / Shiv / 连锁类刻意不进）。
    /// </summary>
    private static CardModel[] SilentDeck() => new CardModel[]
    {
        ModelDb.Card<Neutralize>(),      // 0 费：3 伤 + 1 虚弱
        ModelDb.Card<PoisonedStab>(),    // 1 费：伤害 + 中毒
        ModelDb.Card<PoisonedStab>(),
        ModelDb.Card<DeadlyPoison>(),    // 1 费：直接上毒
        ModelDb.Card<DeadlyPoison>(),
        ModelDb.Card<Backflip>(),        // 1 费：格挡 + 抽 2
        ModelDb.Card<Backflip>(),
        ModelDb.Card<Dash>(),            // 2 费：格挡 + 伤害
        ModelDb.Card<LegSweep>(),        // 2 费：格挡 + 虚弱
        ModelDb.Card<Footwork>(),        // 1 费：+敏捷（能力牌，收益线性）
        ModelDb.Card<NoxiousFumes>(),    // 1 费：每回合全体上毒（能力牌）
        ModelDb.Card<Adrenaline>(),      // 0 费：+能量 + 抽牌
        ModelDb.Card<Predator>(),        // 2 费：高伤 + 下回合多抽
        ModelDb.Card<Afterimage>(),      // 1 费：每打一张牌得格挡（能力牌）
    };

    /// <summary>
    /// 稀有档（铁甲战士皮，成本 3）：**易伤 + 力量 + 大格挡**的正面交换。
    /// 全是"打就完了"的牌（消耗类只留 <c>Impervious</c>，它自己一张就值）。
    /// </summary>
    private static CardModel[] IroncladDeck() => new CardModel[]
    {
        ModelDb.Card<Bash>(),            // 2 费：伤害 + 易伤
        ModelDb.Card<Bash>(),
        ModelDb.Card<TwinStrike>(),      // 1 费：两段伤害
        ModelDb.Card<TwinStrike>(),
        ModelDb.Card<PommelStrike>(),    // 1 费：伤害 + 抽 1
        ModelDb.Card<Uppercut>(),        // 2 费：伤害 + 虚弱 + 易伤
        ModelDb.Card<Bludgeon>(),        // 3 费：大单体伤害
        ModelDb.Card<IronWave>(),        // 1 费：格挡 + 伤害
        ModelDb.Card<IronWave>(),
        ModelDb.Card<ShrugItOff>(),      // 1 费：格挡 + 抽 1
        ModelDb.Card<ShrugItOff>(),
        ModelDb.Card<Inflame>(),         // 1 费：+力量（能力牌）
        ModelDb.Card<DemonForm>(),       // 3 费：每回合 +力量（能力牌，滚雪球）
        ModelDb.Card<Impervious>(),      // 2 费：30 格挡（消耗）
    };

    /// <summary>
    /// 先古档（摄政王皮，成本 4）：摄政王有独立资源"**星**"，所以卡组按"攒星 + 花星"配，
    /// **两边都只放线性牌**：
    /// <list type="bullet">
    /// <item>攒星牌同时给伤害 / 格挡（<c>SolarStrike</c> / <c>GatherLight</c> / <c>ShiningStrike</c> / <c>Glow</c>）
    ///   ⇒ 就算星一直没花掉也不亏；</item>
    /// <item>花星牌只放"有星就纯赚"的（<c>Alignment</c> 0 费换 2 能量 / <c>ParticleWall</c> 0 费 9 格挡）——
    ///   星不够时它们**不可打出**，而托管大脑两个档都先过 <c>CanPlay()</c> 过滤 ⇒ 不会被硬打空放；</item>
    /// <item>刻意**不放** <c>Stardust</c>（X 星费 + 随机目标）与 <c>CrescentSpear</c>（收益依赖"带星费的牌"张数）
    ///   这类"要 AI 做资源规划 / 依赖构成"的牌。</item>
    /// </list>
    /// </summary>
    private static CardModel[] RegentDeck() => new CardModel[]
    {
        ModelDb.Card<StrikeRegent>(),    // 1 费：伤害
        ModelDb.Card<StrikeRegent>(),
        ModelDb.Card<DefendRegent>(),    // 1 费：格挡
        ModelDb.Card<DefendRegent>(),
        ModelDb.Card<SolarStrike>(),     // 1 费：伤害 + 1 星
        ModelDb.Card<SolarStrike>(),
        ModelDb.Card<GatherLight>(),     // 1 费：格挡 + 1 星
        ModelDb.Card<GatherLight>(),
        ModelDb.Card<ShiningStrike>(),   // 1 费：伤害 + 2 星
        ModelDb.Card<Glow>(),            // 1 费：1 星 + 抽 1 + 下回合抽 1
        ModelDb.Card<Alignment>(),       // 0 费 + 2 星：换 2 能量（有星就纯赚）
        ModelDb.Card<ParticleWall>(),    // 0 费 + 2 星：9 格挡且回手牌
        ModelDb.Card<KinglyPunch>(),     // 1 费：伤害，反复抽到会成长
        ModelDb.Card<PillarOfCreation>(),// 1 费：能力牌，每回合给格挡
    };
}
