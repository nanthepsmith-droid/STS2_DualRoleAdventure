using System;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 极速模式（Blitz）商店的条目类型。
///
/// <para>⚠ **顺序必须与游戏 `BlitzMerchantEntryType` 逐值一致**（运行层按 <c>(int)</c> 直接映射），
/// 由单测 `极速商店条目枚举顺序与游戏一致` 钉住 —— 上游增删条目时先改这里再跑测试。</para>
/// </summary>
internal enum BlitzShopEntryKind
{
    CardUpgrade = 0,
    UpgradedCardReward = 1,
    RandomUpgrades = 2,
    CardTransform = 3,
    CardRemoval = 4,
    CardRewards = 5,
    ColorlessCardReward = 6,
    Potions = 7,
    CardEnchant = 8,
    EnchantedCardReward = 9,
    PrismaticCardReward = 10,
    RareCardReward = 11,
    UpgradedRareCard = 12,
    Relic = 13,
    Glam = 14,
    TwoMoreChoices = 15,
    CardRewardOfEachRarity = 16,
    UpgradedRareCardReward = 17,
    TwoRelics = 18,
    FullHealAndMaxHp = 19,
}

/// <summary>一次「买 / 不买」判定 + 稳定原因码（原因码进日志，便于事后判读为什么没买）。</summary>
internal readonly struct BlitzShopDecision
{
    private BlitzShopDecision(bool buy, string reason)
    {
        Buy = buy;
        Reason = reason;
    }

    public bool Buy { get; }

    /// <summary>稳定原因码（中文短句，直接进日志；同一条件永远同一句话）。</summary>
    public string Reason { get; }

    public static BlitzShopDecision Yes(string reason) => new(buy: true, reason);

    public static BlitzShopDecision No(string reason) => new(buy: false, reason);
}

/// <summary>
/// 极速商店判定时用到的**该席位**状态快照（纯数据，无游戏类型）。
/// 由运行层 <c>LocalWakuuBlitzShopAuto</c> 每次判定前重算。
/// </summary>
internal sealed class BlitzShopContext
{
    /// <summary>本店还剩几次购买机会（<c>MerchantInventory.BlitzChoicesRemaining</c>）。</summary>
    public int RemainingChoices { get; set; }

    public int Hp { get; set; }

    public int MaxHp { get; set; }

    public int DeckSize { get; set; }

    /// <summary>该档位的牌组上限（小 30 / 中 40 / 大 = <see cref="int.MaxValue"/>）。</summary>
    public int DeckLimit { get; set; } = 40;

    public int CurseCount { get; set; }

    /// <summary>
    /// 牌组里**值得为它买删牌**的诅咒数（r401）：只算可删的普通诅咒 ——
    /// 带【永恒】词条的不可删诅咒（如【进阶之灾】）与会自己消失的【愧疚】都不算。
    /// 与 <see cref="CurseCount"/>（用于诅咒上限 <see cref="CanTakeCurse"/>，必须算**全部**诅咒）分开。
    /// </summary>
    public int RemovableCurseCount { get; set; }

    /// <summary>可升级的「非打击 / 非防御 / 非诅咒 / 非状态」牌数（随机升级与升级服务都看它）。</summary>
    public int UpgradableNonBasicCount { get; set; }

    /// <summary>可变化的基础牌数（打击 + 防御）。</summary>
    public int ChangeableBasicCount { get; set; }

    /// <summary>可删的垃圾牌数（诅咒 / 状态 / 打击 / 防御）。</summary>
    public int RemovableJunkCount { get; set; }

    /// <summary>可被附魔的「非打击 / 非防御」牌数（探测不到具体附魔时的兜底判据）。</summary>
    public int NonBasicEnchantableCount { get; set; }

    /// <summary>该附魔（条目自带那一条）能附的牌数；<c>-1</c> = 运行层没探测到（改用兜底判据）。</summary>
    public int EnchantTargetCount { get; set; } = -1;

    /// <summary>【华彩】的目标牌数（能力牌 / 3 费及以上 / X 费）。</summary>
    public int GlamTargetCount { get; set; }

    public bool HasOpenPotionSlot { get; set; }

    /// <summary>药水栏里有【鲜血药水】（满栏时可先喝掉腾位）。</summary>
    public bool HasBloodPotion { get; set; }

    /// <summary>栏内最低药水稀有度（<c>PotionRarity</c> 枚举值；<c>-1</c> = 栏内没药水）。</summary>
    public int LowestPotionRarity { get; set; } = -1;

    /// <summary>除本条之外、当前仍「符合规则且没买」的条目数（供「诅咒选择」判断值不值得换次数）。</summary>
    public int DesirableUnboughtCount { get; set; }

    /// <summary>本局已买过几次「诅咒遗物」。</summary>
    public int CursedRelicBuysThisRun { get; set; }

    public double HpPercent => MaxHp <= 0 ? 1.0 : (double)Hp / MaxHp;

    /// <summary>浅拷贝（运行层给单条附魔补 <see cref="EnchantTargetCount"/> 时不污染基础快照）。</summary>
    public BlitzShopContext Clone()
    {
        return (BlitzShopContext)MemberwiseClone();
    }
}

/// <summary>
/// 极速模式（Blitz）商店**自动购买**的判定表（用户 2026-11-23 填表口径，见
/// <c>maintenance-docs/极速商店自动购买-规则表（待填）.md</c>）。
///
/// <para><b>硬机制</b>（规则必须在这套约束下成立）：每次进店 6 格 = 5 个非诅咒档 + 1 个诅咒档；
/// <b>只有 3 次购买机会</b>，买一次 -1，「诅咒选择」+2，减到 0 全店置灰；
/// 所有条目 <c>Cost = 0</c>（不花金币），代价只有诅咒 / 牌组被改动。</para>
///
/// <para><b>决策顺序</b>：按 <see cref="Priority"/> 由小到大买（同档按条目顺序），
/// 每买一次重算上下文（诅咒数与剩余次数都会变）。</para>
///
/// <para><b>「次数用不完」口径</b>（用户原话「允许买非诅咒」）：候选里只要还有非诅咒档可买，
/// 就一直买到次数用完；诅咒档只按各自规则买，不拿来凑次数。</para>
/// </summary>
internal static class BlitzShopBuyPolicy
{
    /// <summary>牌组诅咒数上限（用户口径 N = 5）：买完诅咒档后诅咒数不得超过它。</summary>
    public const int CurseCap = 5;

    /// <summary>卡牌奖励类条目的牌组余量（牌组 ≥ 档位上限 + 5 就不再往里塞牌）。</summary>
    public const int DeckSizeBuffer = 5;

    /// <summary>「诅咒回复」考虑档（HP ≤ 40%）。</summary>
    public const double LowHpThreshold = 0.40;

    /// <summary>「诅咒回复」无条件必买档（HP &lt; 20%，无视诅咒数）。</summary>
    public const double CriticalHpThreshold = 0.20;

    /// <summary>「诅咒遗物」一局最多买几次（防诅咒堆积）。</summary>
    public const int MaxCursedRelicBuys = 2;

    /// <summary><c>PotionRarity.Rare</c> 的枚举值（低于它才值得为奖励换掉栏内最低那瓶）。</summary>
    public const int PotionRarityRare = 2;

    /// <summary>诅咒档（`Glam` 及之后的 6 种，都会先塞 1 张随机诅咒）。</summary>
    public static bool IsCursed(BlitzShopEntryKind kind)
    {
        return (int)kind >= (int)BlitzShopEntryKind.Glam;
    }

    /// <summary>稀有档（抽卡权重只有 0.5 的三种：棱彩 / 稀有卡 / 升级稀有）。</summary>
    public static bool IsRare(BlitzShopEntryKind kind)
    {
        return kind is BlitzShopEntryKind.PrismaticCardReward
            or BlitzShopEntryKind.RareCardReward
            or BlitzShopEntryKind.UpgradedRareCard;
    }

    /// <summary>日志/规则表用的中文名。</summary>
    public static string Describe(BlitzShopEntryKind kind)
    {
        return kind switch
        {
            BlitzShopEntryKind.CardUpgrade => "卡牌升级服务",
            BlitzShopEntryKind.UpgradedCardReward => "升级卡牌奖励",
            BlitzShopEntryKind.RandomUpgrades => "随机升级",
            BlitzShopEntryKind.CardTransform => "卡牌变化服务",
            BlitzShopEntryKind.CardRemoval => "卡牌移除服务",
            BlitzShopEntryKind.CardRewards => "卡牌奖励",
            BlitzShopEntryKind.ColorlessCardReward => "无色卡牌奖励",
            BlitzShopEntryKind.Potions => "药水",
            BlitzShopEntryKind.CardEnchant => "附魔服务",
            BlitzShopEntryKind.EnchantedCardReward => "附魔卡牌奖励",
            BlitzShopEntryKind.PrismaticCardReward => "棱彩卡牌奖励",
            BlitzShopEntryKind.RareCardReward => "稀有卡牌奖励",
            BlitzShopEntryKind.UpgradedRareCard => "升级稀有卡牌",
            BlitzShopEntryKind.Relic => "遗物",
            BlitzShopEntryKind.Glam => "诅咒华彩",
            BlitzShopEntryKind.TwoMoreChoices => "诅咒选择",
            BlitzShopEntryKind.CardRewardOfEachRarity => "诅咒卡牌奖励",
            BlitzShopEntryKind.UpgradedRareCardReward => "诅咒升级稀有卡牌奖励",
            BlitzShopEntryKind.TwoRelics => "诅咒遗物",
            BlitzShopEntryKind.FullHealAndMaxHp => "诅咒回复",
            _ => kind.ToString(),
        };
    }

    /// <summary>买完这条后牌组诅咒数仍在上限内。</summary>
    public static bool CanTakeCurse(BlitzShopContext context)
    {
        return context.CurseCount < CurseCap;
    }

    /// <summary>牌组还有余量（≥ 档位上限 + <see cref="DeckSizeBuffer"/> 就不再塞牌）。</summary>
    public static bool CanGrowDeck(BlitzShopContext context)
    {
        // ⚠ 大卡组档的 DeckLimit = int.MaxValue：直接 +5 会整型溢出成负数 ⇒ 全部卡牌奖励都不买（单测兜住）。
        if (context.DeckLimit >= int.MaxValue - DeckSizeBuffer)
        {
            return true;
        }

        return context.DeckSize < context.DeckLimit + DeckSizeBuffer;
    }

    /// <summary>
    /// 这条值不值得买。返回 <see cref="BlitzShopDecision.No"/> 时 <c>Reason</c> 是稳定原因码。
    /// </summary>
    public static BlitzShopDecision Evaluate(BlitzShopEntryKind kind, BlitzShopContext context)
    {
        switch (kind)
        {
            case BlitzShopEntryKind.CardUpgrade:
                return context.UpgradableNonBasicCount >= 1
                    ? BlitzShopDecision.Yes("有非基础牌可升级")
                    : BlitzShopDecision.No("没有非打击/防御的牌可升级");

            case BlitzShopEntryKind.UpgradedCardReward:
            case BlitzShopEntryKind.CardRewards:
            case BlitzShopEntryKind.ColorlessCardReward:
            case BlitzShopEntryKind.EnchantedCardReward:
            case BlitzShopEntryKind.PrismaticCardReward:
            case BlitzShopEntryKind.RareCardReward:
            case BlitzShopEntryKind.UpgradedRareCard:
                return CanGrowDeck(context)
                    ? BlitzShopDecision.Yes("卡牌奖励（极速里机会稀缺）")
                    : BlitzShopDecision.No($"牌组已达上限+{DeckSizeBuffer}");

            case BlitzShopEntryKind.RandomUpgrades:
                if (context.UpgradableNonBasicCount < 2)
                {
                    return BlitzShopDecision.No("可升级的非基础牌不足 2 张");
                }

                return context.DeckSize < context.DeckLimit
                    ? BlitzShopDecision.Yes("随机升级 2 张")
                    : BlitzShopDecision.No("牌组已达档位上限");

            case BlitzShopEntryKind.CardTransform:
                if (context.ChangeableBasicCount <= 0)
                {
                    return BlitzShopDecision.No("没有可变化的基础牌");
                }

                return context.DeckSize < context.DeckLimit
                    ? BlitzShopDecision.Yes("有基础牌可变化")
                    : BlitzShopDecision.No("牌组已达档位上限");

            case BlitzShopEntryKind.CardRemoval:
                if (context.RemovableJunkCount <= 0)
                {
                    return BlitzShopDecision.No("没有可删的垃圾牌");
                }

                // r401：判据用**可删的**诅咒数（不可删的【永恒】诅咒 / 会自己消失的【愧疚】都不算）。
                if (context.RemovableCurseCount <= 0 && context.DeckSize <= context.DeckLimit)
                {
                    return BlitzShopDecision.No("没可删诅咒且牌组没超上限");
                }

                return BlitzShopDecision.Yes(context.RemovableCurseCount > 0 ? "有诅咒牌可删" : "牌组超上限");

            case BlitzShopEntryKind.Potions:
                if (context.HasOpenPotionSlot || context.HasBloodPotion)
                {
                    return BlitzShopDecision.Yes("药水栏有空间（或可喝鲜血腾位）");
                }

                if (context.LowestPotionRarity >= 0 && context.LowestPotionRarity < PotionRarityRare)
                {
                    return BlitzShopDecision.Yes("可换掉栏内低稀有度药水");
                }

                return BlitzShopDecision.No("药水栏满且无可换");

            case BlitzShopEntryKind.CardEnchant:
            {
                int targets = context.EnchantTargetCount >= 0
                    ? context.EnchantTargetCount
                    : context.NonBasicEnchantableCount;
                return targets >= 1
                    ? BlitzShopDecision.Yes("有符合该附魔的牌")
                    : BlitzShopDecision.No("没有符合该附魔的牌");
            }

            case BlitzShopEntryKind.Relic:
                return BlitzShopDecision.Yes("遗物永久收益");

            case BlitzShopEntryKind.Glam:
                if (!CanTakeCurse(context))
                {
                    return BlitzShopDecision.No($"牌组诅咒数已达上限 {CurseCap}");
                }

                return context.GlamTargetCount >= 1
                    ? BlitzShopDecision.Yes("有能力牌/高费牌可附华彩")
                    : BlitzShopDecision.No("没有能力牌或 3 费以上的牌");

            case BlitzShopEntryKind.TwoMoreChoices:
            {
                if (!CanTakeCurse(context))
                {
                    return BlitzShopDecision.No($"牌组诅咒数已达上限 {CurseCap}");
                }

                // 「1 次换 2 次」只在次数不够用时才有价值：剩 3 次时买它等于白花 1 次。
                int needed = context.RemainingChoices <= 1 ? 2 : 3;
                if (context.RemainingChoices >= 3)
                {
                    return BlitzShopDecision.No("剩余次数充裕（3 次），不拿次数换诅咒");
                }

                return context.DesirableUnboughtCount >= needed
                    ? BlitzShopDecision.Yes($"剩 {context.RemainingChoices} 次且还有 {context.DesirableUnboughtCount} 个想买的")
                    : BlitzShopDecision.No($"想买的条目不足（{context.DesirableUnboughtCount} < {needed}）");
            }

            case BlitzShopEntryKind.CardRewardOfEachRarity:
                if (!CanTakeCurse(context))
                {
                    return BlitzShopDecision.No($"牌组诅咒数已达上限 {CurseCap}");
                }

                return CanGrowDeck(context)
                    ? BlitzShopDecision.Yes("1 次机会换 3 次卡牌奖励")
                    : BlitzShopDecision.No($"牌组已达上限+{DeckSizeBuffer}");

            case BlitzShopEntryKind.UpgradedRareCardReward:
                return CanTakeCurse(context)
                    ? BlitzShopDecision.Yes("升级稀有卡牌奖励（极速最强单卡来源）")
                    : BlitzShopDecision.No($"牌组诅咒数已达上限 {CurseCap}");

            case BlitzShopEntryKind.TwoRelics:
                if (!CanTakeCurse(context))
                {
                    return BlitzShopDecision.No($"牌组诅咒数已达上限 {CurseCap}");
                }

                return context.CursedRelicBuysThisRun < MaxCursedRelicBuys
                    ? BlitzShopDecision.Yes("1 次机会换 2 件遗物")
                    : BlitzShopDecision.No($"本局已买过 {MaxCursedRelicBuys} 次诅咒遗物");

            case BlitzShopEntryKind.FullHealAndMaxHp:
            {
                // 商店后面必是精英（BlitzActMap 固定单线），低血在这里回满的收益远高于平时。
                if (context.Hp >= context.MaxHp)
                {
                    return BlitzShopDecision.No("满血，回满无收益");
                }

                if (context.HpPercent < CriticalHpThreshold)
                {
                    return BlitzShopDecision.Yes("HP<20%：无视诅咒数必买（下一格是精英）");
                }

                if (context.HpPercent > LowHpThreshold)
                {
                    return BlitzShopDecision.No($"HP>{LowHpThreshold:P0}，不需要回复");
                }

                return CanTakeCurse(context)
                    ? BlitzShopDecision.Yes($"HP≤{LowHpThreshold:P0} 且诅咒未达上限")
                    : BlitzShopDecision.No($"牌组诅咒数已达上限 {CurseCap}");
            }

            default:
                return BlitzShopDecision.No("规则未覆盖该条目");
        }
    }

    /// <summary>
    /// 购买优先级（小 = 先买）。用户 2026-11-23 拍板顺序：
    /// 保命回复 → 诅咒选择（补次数）→ 卡牌奖励类 → 遗物 → 升级稀有卡 → 删牌 → 升级/附魔/变化 → 药水。
    /// </summary>
    public static int Priority(BlitzShopEntryKind kind, BlitzShopContext context)
    {
        switch (kind)
        {
            case BlitzShopEntryKind.FullHealAndMaxHp:
                return context.HpPercent < CriticalHpThreshold ? 0 : 1;

            case BlitzShopEntryKind.TwoMoreChoices:
                return 2;

            case BlitzShopEntryKind.UpgradedCardReward:
            case BlitzShopEntryKind.CardRewards:
            case BlitzShopEntryKind.ColorlessCardReward:
            case BlitzShopEntryKind.EnchantedCardReward:
            case BlitzShopEntryKind.PrismaticCardReward:
            case BlitzShopEntryKind.RareCardReward:
            case BlitzShopEntryKind.CardRewardOfEachRarity:
            case BlitzShopEntryKind.UpgradedRareCardReward:
                return 3;

            case BlitzShopEntryKind.Relic:
            case BlitzShopEntryKind.TwoRelics:
                return 4;

            case BlitzShopEntryKind.UpgradedRareCard:
                return 5;

            case BlitzShopEntryKind.CardRemoval:
                return 6;

            case BlitzShopEntryKind.CardUpgrade:
            case BlitzShopEntryKind.CardEnchant:
            case BlitzShopEntryKind.RandomUpgrades:
            case BlitzShopEntryKind.CardTransform:
            case BlitzShopEntryKind.Glam:
                return 7;

            case BlitzShopEntryKind.Potions:
                return 8;

            default:
                return 9;
        }
    }
}
