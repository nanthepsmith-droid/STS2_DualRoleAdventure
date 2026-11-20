namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库抓牌的**形态档位**（用户 2026-11-19 口径）。三档都只影响「瓦库自己怎么选」，
/// 不碰真人的领奖界面。
/// </summary>
internal enum WakuuCardPickTier
{
    /// <summary>
    /// 小卡组：**牌组到 30 张就收紧**（门槛提前一档抬起来），规则同中卡组，
    /// 但**抓取率门槛保持基准 30/40/50 不跟着下调**（用户 2026-11-20：「小卡组不变」）。
    /// </summary>
    Small = 0,

    /// <summary>
    /// 中卡组（默认）：正常抓牌 / 删牌；**三张都烂就不抓**；**卡组 ≥ 40 抬门槛**；
    /// **前三次卡牌奖励一律不跳**（开局牌太少时不许它自己把奖励跳过）；
    /// **每幕门槛比基准低 10 个点**（20/30/40，用户 2026-11-20）。
    /// </summary>
    Normal = 1,

    /// <summary>
    /// 大卡组（原版大盘形态）：**永不跳过卡牌奖励**；
    /// 删牌只删 诅咒 / 状态 / 打击 / 防御（其余牌一概不动）。
    /// </summary>
    Big = 2,
}

/// <summary>某一次卡牌奖励的处理方式。</summary>
internal enum WakuuCardTakeDecision
{
    /// <summary>照常抓（按既有统计/最左策略选一张）。</summary>
    Take = 0,

    /// <summary>不自动领，**把奖励留在奖励屏上让真人自己选**（2026-11-18 口径）。</summary>
    Skip = 1,

    /// <summary>【浮木】先重掷这张卡牌奖励，再看一次。</summary>
    Reroll = 2,

    /// <summary>【佩尔之翼】直接献祭这张卡牌奖励。</summary>
    Sacrifice = 3,
}

/// <summary>
/// 「这次卡牌奖励该抓还是该不抓」的纯判定（用户 2026-11-19 口径）。
///
/// <para><b>门槛怎么算</b>（用户原话：「二、三层过渡牌的抓取率本来就会变低，只要提高二、三层抓牌的
/// 抓取率要求就行」）：</para>
/// <list type="bullet">
/// <item>第一幕要求 <see cref="BaseTakePickRate"/>；</item>
/// <item>每往后一幕 +<see cref="ActStep"/>（二层 40%、三层 50%）—— 过渡牌在后两幕自然掉抓取率，
///   提高要求就等于"后期只拿真正的好牌"，不需要单独识别过渡牌；</item>
/// <item>**中卡组**每幕再减 <see cref="NormalActDownshift"/>（2026-11-20 用户口径 ⇒ 20% / 30% / 40%）；
///   小卡组保持原值（30% / 40% / 50%），一个字不动；</item>
/// <item>牌组张数到档位上限（小 30 / 中 40）再 +<see cref="DeckLimitBump"/>。</item>
/// </list>
///
/// <para><b>决策顺序</b>：大卡组永不跳 → 无任何数据不瞎跳（照抓最左）→ 中卡组前三次保护 →
/// 最高抓取率达标就抓 → 不达标时 <b>浮木重掷 &gt; 佩尔之翼献祭 &gt; 留给真人</b>。</para>
///
/// <para><b>为什么重掷在献祭之前</b>（2026-11-19 用户订正）：重掷只是"再抽一次机会"，
/// 抽到好牌就照抓；抽完还是烂，下一次评估里 <c>CanReroll</c> 已变 false ⇒ 自然落到献祭。
/// 反过来先献祭就等于放弃了这一抽。</para>
/// </summary>
internal static class WakuuCardPickPolicy
{
    public const string SmallTier = "small";
    public const string NormalTier = "normal";
    public const string BigTier = "big";

    /// <summary>小卡组档的门槛起始张数（用户：「限制降到 30 张牌」）。</summary>
    public const int SmallDeckLimit = 30;

    /// <summary>中卡组档的门槛起始张数（用户：「卡组 ≥ 40 抬门槛」）。</summary>
    public const int NormalDeckLimit = 40;

    /// <summary>第一幕的抓取率要求。</summary>
    public const double BaseTakePickRate = 0.30;

    /// <summary>每往后一幕加多少要求（二/三层抓取率本来就低 ⇒ 要求更高）。</summary>
    public const double ActStep = 0.10;

    /// <summary>牌组达到/超过档位上限后再抬多少。</summary>
    public const double DeckLimitBump = 0.15;

    /// <summary>
    /// **中卡组**每幕的向下修正（用户 2026-11-20 口径：「小卡组不变，中卡组每层都下调 10% 或者 20%」）。
    ///
    /// 取 10 个百分点 ⇒ 中卡组实际门槛 = 20% / 30% / 40%（原 30 / 40 / 50）。
    /// 20 个点会让一层只要 10%，等于"几乎什么都抓"、这套「不达标就不抓」就废了 ⇒ 先按 10 个点。
    /// **只作用于中卡组**：小卡组保持 30 / 40 / 50（用户明确"小卡组不变"），大卡组本来永不跳。
    /// </summary>
    public const double NormalActDownshift = 0.10;

    /// <summary>中卡组档：前 N 次卡牌奖励一律不跳。</summary>
    public const int NormalProtectFirstRewards = 3;

    /// <summary>把配置里的字符串归一成三档之一（未知 = 中卡组）。</summary>
    public static string NormalizeTier(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return NormalTier;
        }

        string value = raw.Trim().ToLowerInvariant();
        return value switch
        {
            SmallTier => SmallTier,
            BigTier => BigTier,
            _ => NormalTier,
        };
    }

    public static WakuuCardPickTier ParseTier(string? raw)
    {
        return NormalizeTier(raw) switch
        {
            SmallTier => WakuuCardPickTier.Small,
            BigTier => WakuuCardPickTier.Big,
            _ => WakuuCardPickTier.Normal,
        };
    }

    /// <summary>该档位的「牌组张数门槛」（超过就抬要求）；大卡组没有门槛（返回 <see cref="int.MaxValue"/>）。</summary>
    public static int DeckLimitOf(WakuuCardPickTier tier)
    {
        return tier switch
        {
            WakuuCardPickTier.Small => SmallDeckLimit,
            WakuuCardPickTier.Big => int.MaxValue,
            _ => NormalDeckLimit,
        };
    }

    /// <summary>
    /// 该档 / 该幕 / 该牌组张数下，一张牌要多高的抓取率才值得抓。
    /// 大卡组永不跳，返回 0（调用方据此直接 Take）。
    /// 中卡组每幕比基准低 <see cref="NormalActDownshift"/>（2026-11-20），小卡组保持基准。
    /// </summary>
    public static double RequiredPickRate(WakuuCardPickTier tier, int act, int deckSize)
    {
        if (tier == WakuuCardPickTier.Big)
        {
            return 0.0;
        }

        int effectiveAct = act < 1 ? 1 : act;
        double required = BaseTakePickRate + ActStep * (effectiveAct - 1);
        if (tier == WakuuCardPickTier.Normal)
        {
            required -= NormalActDownshift;
        }

        if (deckSize >= DeckLimitOf(tier))
        {
            required += DeckLimitBump;
        }

        // 下限 0 / 上限 1.0：阈值再怎么调也不会越界。
        if (required < 0.0)
        {
            return 0.0;
        }

        return required > 1.0 ? 1.0 : required;
    }

    /// <summary>
    /// 决策。
    /// </summary>
    /// <param name="tier">档位（<see cref="ParseTier"/> 归一后的枚举）。</param>
    /// <param name="act">当前幕（1 起）。</param>
    /// <param name="deckSize">该席位当前牌组张数。</param>
    /// <param name="rewardOrdinal">本局该席位的第几次卡牌奖励（1 起，用于中卡组前三次保护）。</param>
    /// <param name="bestPickRate">三张候选里最高的融合抓取率（0~1）。</param>
    /// <param name="hasAnySignal">三张里有没有任何一张查得到数据（个人或社区）。</param>
    /// <param name="canSacrifice">这次奖励有没有献祭选项（佩尔之翼）。</param>
    /// <param name="canReroll">这张奖励还能不能重掷（浮木 / <c>CardReward.CanReroll</c>）。</param>
    public static WakuuCardTakeDecision Decide(
        WakuuCardPickTier tier,
        int act,
        int deckSize,
        int rewardOrdinal,
        double bestPickRate,
        bool hasAnySignal,
        bool canSacrifice,
        bool canReroll)
    {
        if (tier == WakuuCardPickTier.Big)
        {
            return WakuuCardTakeDecision.Take; // 大卡组：不跳过卡牌奖励
        }

        if (!hasAnySignal)
        {
            // 没有任何数据时"烂"无从谈起 —— 瞎跳会把整局牌都跳光（新角色 / 没装统计 mod 时必现）。
            return WakuuCardTakeDecision.Take;
        }

        if (tier == WakuuCardPickTier.Normal && rewardOrdinal <= NormalProtectFirstRewards)
        {
            return WakuuCardTakeDecision.Take; // 前三次不跳（开局牌太少）
        }

        if (bestPickRate >= RequiredPickRate(tier, act, deckSize))
        {
            return WakuuCardTakeDecision.Take;
        }

        // 2026-11-19 用户订正：**先重掷**（抽一次机会），抽完还烂才献祭。
        // 重掷后 `CanReroll` 自动变 false ⇒ 下一次评估自然落到献祭/不抓。
        if (canReroll)
        {
            return WakuuCardTakeDecision.Reroll;
        }

        if (canSacrifice)
        {
            return WakuuCardTakeDecision.Sacrifice;
        }

        return WakuuCardTakeDecision.Skip;
    }

    /// <summary>大卡组档的删牌限制：只删 诅咒 / 状态 / 打击 / 防御。</summary>
    public static bool IsRemovalJunkOnly(WakuuCardPickTier tier)
    {
        return tier == WakuuCardPickTier.Big;
    }

    /// <summary>该类别在大卡组档里允不允许被删。</summary>
    public static bool IsRemovableInJunkOnly(WakuuCardKind kind)
    {
        return kind switch
        {
            WakuuCardKind.Curse => true,
            WakuuCardKind.Status => true,
            WakuuCardKind.BasicStrike => true,
            WakuuCardKind.BasicDefend => true,
            _ => false,
        };
    }
}
