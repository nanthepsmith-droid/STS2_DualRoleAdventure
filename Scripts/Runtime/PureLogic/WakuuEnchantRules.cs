using System.Collections.Generic;
using System.Linq;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库附魔选牌规则表（数据驱动，来源：maintenance-docs/decision-records/原版附魔一览表.md 用户填写）。
///
/// 每种附魔一条有序规则：按顺序取第一个能筛出候选的条目，命中即按其排序方式输出。
/// 规则为 null 的附魔（维持现状）走既有 cardPickMode 策略。
///
/// 牌名 / 遗物名均为游戏内部 id（CardModel.Id.Entry / RelicModel.Id.Entry，大写），
/// 由中文本地化（zhs）反查得到；升级后缀 "+" 记为该条目的 RequireUpgraded。
/// </summary>
internal static class WakuuEnchantRules
{
    // ============ 条件组（遗物 / 牌组）============

    /// <summary>烫嘴可可（热可可）/ 灯笼 / 古茶具套装：能量类遗物，配合固有+保留起手。</summary>
    private static readonly string[] EnergyRelics = { "VERY_HOT_COCOA", "LANTERN", "VENERABLE_TEA_SET" };

    /// <summary>化学物 X。</summary>
    private static readonly string[] ChemicalX = { "CHEMICAL_X" };

    /// <summary>化废为宝 / 散射炮（牌组内持有）。</summary>
    private static readonly string[] TrashOrFlak = { "TRASH_TO_TREASURE", "FLAK_CANNON" };

    /// <summary>开信刀。</summary>
    private static readonly string[] LetterOpener = { "LETTER_OPENER" };

    /// <summary>锚（遗物）。</summary>
    private static readonly string[] AnchorRelic = { "ANCHOR" };

    /// <summary>启动流程（牌组内持有）。</summary>
    private static readonly string[] BootSequence = { "BOOT_SEQUENCE" };

    /// <summary>棋子。</summary>
    private static readonly string[] GamePiece = { "GAME_PIECE" };

    /// <summary>
    /// 不休陀螺（Clone 分支判定）。
    /// ⚠ 2026-09-01 用户要求停用不休陀螺分支（沙漏 BOSS 下是死路），此常量不再用于分支判定，
    /// 保留仅作文档记录。
    /// </summary>
    internal const string UnceasingTop = "UNCEASING_TOP";

    // 以下条件组仅被「不休陀螺」分支使用（卡戎之灰 / 抱抱先生 / 水银沙漏），分支已注释停用，
    // 定义一并移除以免未使用字段告警；日后恢复分支时需重新补回：
    //   CharonsAshes        = { "CHARONS_ASHES" }
    //   StrugglesOrHourglass = { "MR_STRUGGLES", "MERCURY_HOURGLASS" }

    // ============ 便捷构造 ============

    private static WakuuEnchantRuleEntry Card(
        string id,
        bool upgraded = false,
        string[]? relics = null,
        string[]? deck = null)
    {
        return new WakuuEnchantRuleEntry
        {
            CardId = id,
            Predicate = new WakuuEnchantPredicate { RequireUpgraded = upgraded },
            RequiredRelicAny = relics,
            RequiredDeckCardAny = deck,
        };
    }

    private static WakuuEnchantRuleEntry Any(
        WakuuEnchantSort sort = WakuuEnchantSort.Index,
        WakuuEnchantPredicate? predicate = null)
    {
        return new WakuuEnchantRuleEntry
        {
            Predicate = predicate ?? default,
            Sort = sort,
        };
    }

    private static WakuuEnchantPredicate MultiHit(int minHits, bool exhaust)
    {
        return new WakuuEnchantPredicate
        {
            CardTypeMask = WakuuEnchantPicking.MaskAttack,
            MinHitCount = minHits,
            Exhaust = exhaust,
        };
    }

    private static WakuuEnchantPredicate MultiHitExact(int hits, bool exhaust)
    {
        return new WakuuEnchantPredicate
        {
            CardTypeMask = WakuuEnchantPicking.MaskAttack,
            ExactHitCount = hits,
            Exhaust = exhaust,
        };
    }

    // ============ 各附魔规则 ============

    /// <summary>伶俐 Adroit：费用最低的牌。</summary>
    private static readonly List<WakuuEnchantRuleEntry> Adroit = new()
    {
        Any(WakuuEnchantSort.CostAsc),
    };

    /// <summary>腐化 Corrupted：伤害最高的牌（X 费按费用 3 折算）。</summary>
    private static readonly List<WakuuEnchantRuleEntry> Corrupted = new()
    {
        Any(WakuuEnchantSort.DamageDesc),
    };

    /// <summary>华彩 Glam：能力牌 &gt; X 费牌（用户追加）&gt; 费用最高（≥2）的牌 &gt; 愤怒 &gt; 不死 &gt; 适应打击 &gt; 其它牌。</summary>
    private static readonly List<WakuuEnchantRuleEntry> Glam = new()
    {
        Any(WakuuEnchantSort.Index, new WakuuEnchantPredicate { CardTypeMask = WakuuEnchantPicking.MaskPower }),
        Any(WakuuEnchantSort.Index, new WakuuEnchantPredicate { RequireCostsX = true }),
        Any(WakuuEnchantSort.CostDesc, new WakuuEnchantPredicate { MinCost = 2 }),
        Card("ANGER"),
        Card("UNDEATH"),
        Card("ADAPTIVE_STRIKE"),
        Any(),
    };

    /// <summary>黏糊 Goopy：维持现状。</summary>
    private static readonly List<WakuuEnchantRuleEntry>? Goopy = null;

    /// <summary>注能 Imbued：能抽牌（3 张以上）的卡牌 &gt; 费用最高的牌。</summary>
    private static readonly List<WakuuEnchantRuleEntry> Imbued = new()
    {
        Any(WakuuEnchantSort.Index, new WakuuEnchantPredicate { MinDrawCount = 3 }),
        Any(WakuuEnchantSort.CostDesc),
    };

    /// <summary>墨影 Inky：费用最低的牌。</summary>
    private static readonly List<WakuuEnchantRuleEntry> Inky = new()
    {
        Any(WakuuEnchantSort.CostAsc),
    };

    /// <summary>本能 Instinct：伤害最高的牌。</summary>
    private static readonly List<WakuuEnchantRuleEntry> Instinct = new()
    {
        Any(WakuuEnchantSort.DamageDesc),
    };

    /// <summary>
    /// 动量 Momentum：不消耗的多段（3 段以上）攻击牌 &gt; 不消耗的多段（2 段）攻击牌 &gt; 其它牌 &gt; 消耗的牌。
    /// </summary>
    private static readonly List<WakuuEnchantRuleEntry> Momentum = new()
    {
        Any(WakuuEnchantSort.Index, MultiHit(3, exhaust: false)),
        Any(WakuuEnchantSort.Index, MultiHitExact(2, exhaust: false)),
        Any(),
        Any(WakuuEnchantSort.Index, new WakuuEnchantPredicate { Exhaust = true }),
    };

    /// <summary>灵巧 Nimble：费用最低的牌。</summary>
    private static readonly List<WakuuEnchantRuleEntry> Nimble = new()
    {
        Any(WakuuEnchantSort.CostAsc),
    };

    /// <summary>完美契合 PerfectFit：不消耗的能抽牌（3 张以上）的卡牌 &gt; 其它牌。</summary>
    private static readonly List<WakuuEnchantRuleEntry> PerfectFit = new()
    {
        Any(WakuuEnchantSort.Index, new WakuuEnchantPredicate { Exhaust = false, MinDrawCount = 3 }),
        Any(),
    };

    /// <summary>
    /// 王室认证 RoyallyApproved：
    /// 粒子墙 &gt;（有烫嘴可可/灯笼/古茶具套装时）能力牌（3 费）&gt; 指定牌名列表 &gt;
    /// 能提供防御且格挡 &gt;20 的牌（取最高）&gt; 扫腿 &gt; 其它牌。
    ///
    /// 说明：第 2 条带遗物条件——不满足条件时不会跳过整段，而是继续往下走牌名列表，
    /// 避免出现"没有能量遗物就什么都不选"的空窗。
    /// </summary>
    private static readonly List<WakuuEnchantRuleEntry> RoyallyApproved = new()
    {
        Card("PARTICLE_WALL"),
        new WakuuEnchantRuleEntry
        {
            Predicate = new WakuuEnchantPredicate
            {
                CardTypeMask = WakuuEnchantPicking.MaskPower,
                ExactCost = 3,
            },
            RequiredRelicAny = EnergyRelics,
            Sort = WakuuEnchantSort.Index,
        },
        Card("BRIGHTEST_FLAME"),
        Card("OFFERING"),
        Card("NEUROSURGE"),
        Card("STOKE"),
        Card("BULLET_TIME"),
        Card("CORROSIVE_WAVE"),
        Card("DECISIONS_DECISIONS"),
        Card("SIGNAL_BOOST"),
        Card("SCRAWL"),
        Card("THE_GAMBIT"),
        Card("SALVO"),
        Card("EQUILIBRIUM"),
        Card("PRODUCTION"),
        Card("FASTEN"),
        Card("ROLLING_BOULDER"),
        Any(WakuuEnchantSort.BlockDesc, new WakuuEnchantPredicate { MinBlock = 20 }),
        Card("LEG_SWEEP"),
        Any(),
    };

    /// <summary>
    /// 锋利 Sharp：不消耗的多段（3 段以上）攻击牌 &gt; 消耗的多段（3 段以上）攻击牌 &gt;
    /// 不消耗的多段（2 段）攻击牌 &gt; 其它牌。
    /// </summary>
    private static readonly List<WakuuEnchantRuleEntry> Sharp = new()
    {
        Any(WakuuEnchantSort.Index, MultiHit(3, exhaust: false)),
        Any(WakuuEnchantSort.Index, MultiHit(3, exhaust: true)),
        Any(WakuuEnchantSort.Index, MultiHitExact(2, exhaust: false)),
        Any(),
    };

    /// <summary>蛇行 Slither：费用最高的牌。</summary>
    private static readonly List<WakuuEnchantRuleEntry> Slither = new()
    {
        Any(WakuuEnchantSort.CostDesc),
    };

    /// <summary>沉眠精华 SlumberingEssence：带保留且费用最高（≥3）的牌 &gt; 其它费用最高的牌。</summary>
    private static readonly List<WakuuEnchantRuleEntry> SlumberingEssence = new()
    {
        Any(WakuuEnchantSort.CostDesc, new WakuuEnchantPredicate { RequireRetain = true, MinCost = 3 }),
        Any(WakuuEnchantSort.CostDesc),
    };

    /// <summary>
    /// 灵魂之力 SoulsPower：吹哨 &gt; 遗传算法 &gt; 灵体 &gt; 肾上腺素 &gt; 抉择，抉择 &gt; 主宰 &gt;
    /// 炼制药水 &gt; 超临界态 &gt; 计算下注 &gt; 夜魇 &gt; 净化 &gt; 挽歌 &gt; 时候未到 &gt;
    /// 黑暗镣铐 &gt; 萎靡 &gt; 白噪声 &gt; 其它稀有度最高的牌。
    /// </summary>
    private static readonly List<WakuuEnchantRuleEntry> SoulsPower = new()
    {
        Card("WHISTLE"),
        Card("GENETIC_ALGORITHM"),
        Card("APPARITION"),
        Card("ADRENALINE"),
        Card("DECISIONS_DECISIONS"),
        Card("DOMINATE"),
        Card("ALCHEMIZE"),
        Card("SUPERCRITICAL"),
        Card("CALCULATED_GAMBLE"),
        Card("NIGHTMARE"),
        Card("PURITY"),
        Card("DIRGE"),
        Card("NOT_YET"),
        Card("DARK_SHACKLES"),
        Card("MALAISE"),
        Card("WHITE_NOISE"),
        Any(WakuuEnchantSort.RarityDesc),
    };

    /// <summary>
    /// 播种 Sown：能力牌（3 费）&gt; 能抽牌（3 张以上）的卡牌 &gt; 能力牌（2 费）&gt;
    /// 消耗的牌中费用最高的 &gt; 其它牌中费用最高的。
    /// </summary>
    private static readonly List<WakuuEnchantRuleEntry> Sown = new()
    {
        Any(WakuuEnchantSort.Index, new WakuuEnchantPredicate { CardTypeMask = WakuuEnchantPicking.MaskPower, ExactCost = 3 }),
        Any(WakuuEnchantSort.Index, new WakuuEnchantPredicate { MinDrawCount = 3 }),
        Any(WakuuEnchantSort.Index, new WakuuEnchantPredicate { CardTypeMask = WakuuEnchantPicking.MaskPower, ExactCost = 2 }),
        Any(WakuuEnchantSort.CostDesc, new WakuuEnchantPredicate { Exhaust = true }),
        Any(WakuuEnchantSort.CostDesc),
    };

    /// <summary>涡旋 Spiral：维持现状。</summary>
    private static readonly List<WakuuEnchantRuleEntry>? Spiral = null;

    /// <summary>稳定 Steady：费用最高的牌。</summary>
    private static readonly List<WakuuEnchantRuleEntry> Steady = new()
    {
        Any(WakuuEnchantSort.CostDesc),
    };

    /// <summary>迅速 Swift：愤怒 &gt; 不死 &gt; 适应打击 &gt; 能力牌 &gt; 消耗的能抽牌卡。</summary>
    private static readonly List<WakuuEnchantRuleEntry> Swift = new()
    {
        Card("ANGER"),
        Card("UNDEATH"),
        Card("ADAPTIVE_STRIKE"),
        Any(WakuuEnchantSort.Index, new WakuuEnchantPredicate { CardTypeMask = WakuuEnchantPicking.MaskPower }),
        Any(WakuuEnchantSort.Index, new WakuuEnchantPredicate { Exhaust = true, MinDrawCount = 1 }),
    };

    /// <summary>特兹卡塔拉的余烬 TezcatarasEmber：维持现状。</summary>
    private static readonly List<WakuuEnchantRuleEntry>? TezcatarasEmber = null;

    /// <summary>活力 Vigorous：同锋利（多段优先）。</summary>
    private static readonly List<WakuuEnchantRuleEntry> Vigorous = new()
    {
        Any(WakuuEnchantSort.Index, MultiHit(3, exhaust: false)),
        Any(WakuuEnchantSort.Index, MultiHit(3, exhaust: true)),
        Any(WakuuEnchantSort.Index, MultiHitExact(2, exhaust: false)),
        Any(),
    };

    /// <summary>
    /// 克隆 Clone —— 无「不休陀螺」时的优先级（先古之民遗物给牌附魔克隆，之后才能在休息处复制）。
    /// </summary>
    private static readonly List<WakuuEnchantRuleEntry> CloneWithoutTop = new()
    {
        Card("MIND_BLAST"),
        Card("ADRENALINE"),
        Card("CASCADE"),
        Card("BIG_BANG"),
        Card("AFTERIMAGE"),
        Card("PERFECTED_STRIKE"),
        Card("CRESCENT_SPEAR"),
        Card("ALCHEMIZE"),
        Card("CATASTROPHE"),
        Card("FLASH_OF_STEEL"),
        Card("FINESSE"),
        Card("MASTER_OF_STRATEGY"),
        Card("ETERNAL_ARMOR"),
        Card("CALCULATED_GAMBLE"),
        Card("ESCAPE_PLAN"),
        Card("FETCH"),
        Card("SQUEEZE"),
        Card("DARKNESS"),
        Card("UPROAR"),
        Card("STONE_ARMOR"),
        Card("DISTRACTION", upgraded: true),                       // 声东击西+
        Card("STORM"),
        Card("RESTLESSNESS"),
        Card("THINKING_AHEAD"),
        Card("THE_GAMBIT"),
        Card("DECISIONS_DECISIONS"),
        Card("HANG", relics: EnergyRelics),                        // 吊杀（有热可可/灯笼/古茶具套装）
        Card("MALAISE", relics: ChemicalX),                         // 萎靡（化学物 X）
        Card("HEAVENLY_DRILL", relics: ChemicalX),
        Card("VOLLEY", relics: ChemicalX),
        Card("ERADICATE", relics: ChemicalX),
        Card("DIRGE", relics: ChemicalX),
        new WakuuEnchantRuleEntry                                   // 延伸（有遗物【锚】或牌组有【启动流程】）
        {
            CardId = "PROLONG",
            RequiredRelicAny = AnchorRelic,
            RequiredDeckCardAny = BootSequence,
        },
        Card("WHITE_NOISE", upgraded: true, relics: GamePiece),      // 白噪声+（有棋子）
        Card("FIGHT_THROUGH", deck: TrashOrFlak),                    // 强撑（化废为宝/散射炮）
        Card("OVERCLOCK", deck: TrashOrFlak),
        Card("BOOST_AWAY", deck: TrashOrFlak),
        Card("SECRET_TECHNIQUE", relics: LetterOpener),              // 秘密技法（开信刀）
        Card("IMPATIENCE", relics: LetterOpener),
        Card("REBOOT"),
        // 「没有 XX」不是禁止持有，而是与「有 XX」相比优先级降低：无条件降级位置，
        // 有能量遗物时上面的 HANG(有) 先命中；没有时走到这里照样能选到吊杀。
        Card("HANG"),                                               // 吊杀（没有热可可/灯笼/古茶具套装的降级位置）
        Card("WHITE_NOISE", upgraded: true),                        // 白噪声+（没有棋子的降级位置）
        Any(),
    };

    /*
    /// <summary>
    /// 克隆 Clone —— 有「不休陀螺」时的优先级（2026-09-01 用户要求停用：沙漏 BOSS 下不休陀螺
    /// 相关牌是死路，克隆恒走无陀螺分支）。数据保留，日后如需恢复可解开本注释块。
    /// </summary>
    private static readonly List<WakuuEnchantRuleEntry> CloneWithTop = new List<WakuuEnchantRuleEntry>
    {
        Card("BULLY"),
        Card("BODY_SLAM", upgraded: true),                           // 全身撞击+
        Card("SPITE"),
        Card("WHIRLWIND"),
        Card("BEAM_CELL"),
        Card("GO_FOR_THE_EYES"),
        Card("CLAW"),
        Card("CHILL"),
        Card("DOUBLE_ENERGY"),
        Card("FTL"),
        Card("HELIX_DRILL"),
        Card("HOTFIX"),
        Card("SUPERCRITICAL"),
        Card("SUBROUTINE"),
        Card("TESLA_COIL"),
        Card("RAGE"),
        Card("UNRELENTING"),
        Card("ANTICIPATE"),
        Card("ASSASSINATE"),
        Card("BACKSTAB"),
        Card("FLATTEN"),
        Card("MISERY"),
        Card("POKE"),
        Card("OBLIVION"),
        Card("RIGHT_HAND_HAND"),
        Card("SHARED_FATE"),
        Card("DEFLECT"),
        Card("MALAISE", upgraded: true),                             // 萎靡+
        Card("NEUTRALIZE"),
        Card("PRECISE_CUT"),
        Card("SLICE"),
        Card("SHADOW_STEP", upgraded: true),                         // 暗影步+
        Card("SHADOWMELD", upgraded: true),                          // 融入暗影+
        Card("RADIATE"),
        Card("MONOLOGUE"),
        Card("MAKE_IT_SO"),
        Card("LUNAR_BLAST"),
        Card("AUTOMATION"),
        Card("BOLAS"),
        Card("OMNISLICE"),
        Card("DRAMATIC_ENTRANCE"),
        Card("PANACHE"),
        Card("PRODUCTION"),
        Card("TEMPEST", relics: ChemicalX),                          // 暴风雨（化学物 X）
        Card("TURBO", deck: TrashOrFlak),                            // 内核加速（化废为宝/散射炮）
        Card("ZAP", upgraded: true),                                 // 电击+
        Card("WHITE_NOISE", upgraded: true),                         // 白噪声+
        Card("DUALCAST", upgraded: true),                            // 双重释放+
        Card("PURITY", relics: CharonsAshes),                        // 净化（卡戎之灰）
        Card("KNOW_THY_PLACE", relics: LetterOpener),                // 何人僭越（开信刀）
        Card("EXPOSE", relics: LetterOpener),
        Card("STRATAGEM"),
        Card("SPLASH"),
        Card("ANGER"),
        Card("UNDEATH"),
        Card("SEANCE"),
        Card("BOOT_SEQUENCE", relics: StrugglesOrHourglass),         // 启动流程（抱抱先生/水银沙漏）
        Any(WakuuEnchantSort.Index, new WakuuEnchantPredicate { ExactCost = 0 }), // 0 费的牌
    };
    */

    static WakuuEnchantRules()
    {
        // 沙漏 BOSS 隐患：不休陀螺分支已注释停用，克隆恒走无陀螺分支（CloneWithoutTop）。
    }

    /// <summary>
    /// 按附魔类型名取规则；无规则（维持现状）返回 null。
    /// typeName 传 enchantment.GetType().Name（如 "Adroit"）。
    /// </summary>
    public static IReadOnlyList<WakuuEnchantRuleEntry>? ForEnchantment(string? typeName)
    {
        return typeName switch
        {
            "Adroit" => Adroit,
            "Corrupted" => Corrupted,
            "Glam" => Glam,
            "Goopy" => Goopy,
            "Imbued" => Imbued,
            "Inky" => Inky,
            "Instinct" => Instinct,
            "Momentum" => Momentum,
            "Nimble" => Nimble,
            "PerfectFit" => PerfectFit,
            "RoyallyApproved" => RoyallyApproved,
            "Sharp" => Sharp,
            "Slither" => Slither,
            "SlumberingEssence" => SlumberingEssence,
            "SoulsPower" => SoulsPower,
            "Sown" => Sown,
            "Spiral" => Spiral,
            "Steady" => Steady,
            "Swift" => Swift,
            "TezcatarasEmber" => TezcatarasEmber,
            "Vigorous" => Vigorous,
            _ => null,
        };
    }

    /// <summary>
    /// Clone 规则：恒走无陀螺分支（用户 2026-09-01 要求——沙漏 BOSS 下不休陀螺分支是死路）。
    /// hasUnceasingTop 参数保留仅兼容旧调用/测试；不休陀螺分支数据已注释停用。
    /// </summary>
    public static IReadOnlyList<WakuuEnchantRuleEntry> ForClone(bool hasUnceasingTop)
    {
        return CloneWithoutTop;
    }

    /// <summary>
    /// 统一入口：Clone 需要按遗物分支，其余按类型名。
    /// </summary>
    public static IReadOnlyList<WakuuEnchantRuleEntry>? Resolve(
        string? typeName,
        IReadOnlyList<string>? ownedRelics)
    {
        if (typeName == "Clone")
        {
            // 沙漏 BOSS 隐患：恒走无陀螺分支（不休陀螺分支已注释停用）
            return ForClone(hasUnceasingTop: false);
        }

        return ForEnchantment(typeName);
    }
}
