using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 附魔选牌规则引擎测试（原版附魔一览表.md 规则表的数据驱动逻辑）。
/// 覆盖：阶段式取第一个非空条目、精确牌名/升级要求、谓词组合、遗物/牌组条件、
/// 各排序键（含 X 费折算）、规则表注册完整性、Clone 分支。
/// </summary>
[TestFixture]
public class WakuuEnchantPickingTests
{
    private static WakuuEnchantCardInfo C(
        string id,
        int cardType = WakuuEnchantPicking.CardTypeSkill,
        int cost = 1,
        bool costsX = false,
        int damage = 0,
        int hits = 1,
        int block = 0,
        int draw = 0,
        int rarity = 0,
        bool exhaust = false,
        bool retain = false,
        bool upgraded = false)
    {
        return new WakuuEnchantCardInfo(id, cardType, cost, costsX, damage, hits, block, draw, rarity, exhaust, retain, upgraded);
    }

    private static List<int> Rank(
        List<WakuuEnchantCardInfo> cards,
        IReadOnlyList<WakuuEnchantRuleEntry>? rule,
        List<string>? relics = null,
        List<string>? deck = null)
    {
        return WakuuEnchantPicking.RankIndices(cards, rule, relics, deck);
    }

    // ---------------------------------------------------------------
    // 基础：无规则 / 空候选 / 规则全不命中
    // ---------------------------------------------------------------

    [Test]
    public void 无规则返回原序()
    {
        List<WakuuEnchantCardInfo> cards = new() { C("A"), C("B"), C("C") };
        Assert.That(Rank(cards, null), Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void 空候选返回空()
    {
        Assert.That(Rank(new List<WakuuEnchantCardInfo>(), new List<WakuuEnchantRuleEntry>()), Is.Empty);
        Assert.That(Rank(null!, null), Is.Empty);
    }

    [Test]
    public void 规则全不命中返回原序()
    {
        List<WakuuEnchantCardInfo> cards = new() { C("A"), C("B") };
        List<WakuuEnchantRuleEntry> rule = new()
        {
            new WakuuEnchantRuleEntry { CardId = "NEVER_EXISTS" },
        };
        Assert.That(Rank(cards, rule), Is.EqualTo(new[] { 0, 1 }));
    }

    // ---------------------------------------------------------------
    // 阶段式：取第一个非空条目
    // ---------------------------------------------------------------

    [Test]
    public void 第一阶段优先_能力牌优先于费用最低()
    {
        List<WakuuEnchantCardInfo> cards = new()
        {
            C("SKILL_LOW", WakuuEnchantPicking.CardTypeSkill, cost: 0),
            C("POWER_A", WakuuEnchantPicking.CardTypePower, cost: 3),
        };
        List<WakuuEnchantRuleEntry> rule = new()
        {
            new WakuuEnchantRuleEntry { Predicate = new WakuuEnchantPredicate { CardTypeMask = WakuuEnchantPicking.MaskPower } },
            new WakuuEnchantRuleEntry { Sort = WakuuEnchantSort.CostAsc },
        };
        Assert.That(Rank(cards, rule)[0], Is.EqualTo(1)); // 能力牌优先
    }

    [Test]
    public void 第一阶段为空则进入第二阶段()
    {
        List<WakuuEnchantCardInfo> cards = new()
        {
            C("SKILL_1", WakuuEnchantPicking.CardTypeSkill, cost: 1),
            C("SKILL_0", WakuuEnchantPicking.CardTypeSkill, cost: 0),
        };
        List<WakuuEnchantRuleEntry> rule = new()
        {
            new WakuuEnchantRuleEntry { Predicate = new WakuuEnchantPredicate { CardTypeMask = WakuuEnchantPicking.MaskPower } },
            new WakuuEnchantRuleEntry { Sort = WakuuEnchantSort.CostAsc },
        };
        Assert.That(Rank(cards, rule), Is.EqualTo(new[] { 1, 0 })); // 第二阶段费用最低
    }

    // ---------------------------------------------------------------
    // 精确牌名 / 升级要求
    // ---------------------------------------------------------------

    [Test]
    public void 精确牌名大小写不敏感()
    {
        List<WakuuEnchantCardInfo> cards = new() { C("strike_ironclad"), C("ANGER") };
        List<WakuuEnchantRuleEntry> rule = new() { new WakuuEnchantRuleEntry { CardId = "anger" } };
        Assert.That(Rank(cards, rule)[0], Is.EqualTo(1));
    }

    [Test]
    public void 牌名要求已升级()
    {
        List<WakuuEnchantCardInfo> cards = new() { C("ANGER", upgraded: false), C("ANGER", upgraded: true) };
        List<WakuuEnchantRuleEntry> rule = new()
        {
            new WakuuEnchantRuleEntry { CardId = "ANGER", Predicate = new WakuuEnchantPredicate { RequireUpgraded = true } },
        };
        Assert.That(Rank(cards, rule)[0], Is.EqualTo(1)); // 只命中已升级那张
    }

    // ---------------------------------------------------------------
    // 谓词：类型 / 消耗 / 多段 / 抽牌 / 格挡 / 费用
    // ---------------------------------------------------------------

    [Test]
    public void 多段攻击筛选_3段以上优先()
    {
        List<WakuuEnchantCardInfo> cards = new()
        {
            C("SINGLE", WakuuEnchantPicking.CardTypeAttack, hits: 1),
            C("TRIPLE", WakuuEnchantPicking.CardTypeAttack, hits: 3),
            C("DOUBLE", WakuuEnchantPicking.CardTypeAttack, hits: 2),
        };
        List<WakuuEnchantRuleEntry> rule = new()
        {
            new WakuuEnchantRuleEntry { Predicate = new WakuuEnchantPredicate { CardTypeMask = WakuuEnchantPicking.MaskAttack, MinHitCount = 3, Exhaust = false } },
        };
        Assert.That(Rank(cards, rule), Is.EqualTo(new[] { 1 })); // 只有 3 段那张命中
    }

    [Test]
    public void 多段攻击筛选_3段为空时回退2段()
    {
        List<WakuuEnchantCardInfo> cards = new()
        {
            C("SINGLE", WakuuEnchantPicking.CardTypeAttack, hits: 1),
            C("DOUBLE", WakuuEnchantPicking.CardTypeAttack, hits: 2),
        };
        List<WakuuEnchantRuleEntry> rule = new()
        {
            new WakuuEnchantRuleEntry { Predicate = new WakuuEnchantPredicate { CardTypeMask = WakuuEnchantPicking.MaskAttack, MinHitCount = 3, Exhaust = false } },
            new WakuuEnchantRuleEntry { Predicate = new WakuuEnchantPredicate { CardTypeMask = WakuuEnchantPicking.MaskAttack, ExactHitCount = 2, Exhaust = false } },
        };
        Assert.That(Rank(cards, rule), Is.EqualTo(new[] { 1 })); // 3 段为空，回退 2 段
    }

    [Test]
    public void 消耗筛选()
    {
        List<WakuuEnchantCardInfo> cards = new() { C("EX", exhaust: false), C("EX2", exhaust: true) };
        List<WakuuEnchantRuleEntry> rule = new()
        {
            new WakuuEnchantRuleEntry { Predicate = new WakuuEnchantPredicate { Exhaust = true } },
        };
        Assert.That(Rank(cards, rule)[0], Is.EqualTo(1));
    }

    [Test]
    public void 抽牌筛选_3张以上()
    {
        List<WakuuEnchantCardInfo> cards = new() { C("DRAW2", draw: 2), C("DRAW3", draw: 3) };
        List<WakuuEnchantRuleEntry> rule = new()
        {
            new WakuuEnchantRuleEntry { Predicate = new WakuuEnchantPredicate { MinDrawCount = 3 } },
        };
        Assert.That(Rank(cards, rule)[0], Is.EqualTo(1));
    }

    [Test]
    public void 格挡大于20筛选()
    {
        List<WakuuEnchantCardInfo> cards = new() { C("BLK15", block: 15), C("BLK25", block: 25) };
        List<WakuuEnchantRuleEntry> rule = new()
        {
            new WakuuEnchantRuleEntry { Predicate = new WakuuEnchantPredicate { MinBlock = 20 }, Sort = WakuuEnchantSort.BlockDesc },
        };
        Assert.That(Rank(cards, rule), Is.EqualTo(new[] { 1 })); // 只有 25 格挡那张命中
    }

    [Test]
    public void 费用筛选_精确3费能力牌()
    {
        List<WakuuEnchantCardInfo> cards = new()
        {
            C("PWR3", WakuuEnchantPicking.CardTypePower, cost: 3),
            C("PWR2", WakuuEnchantPicking.CardTypePower, cost: 2),
        };
        List<WakuuEnchantRuleEntry> rule = new()
        {
            new WakuuEnchantRuleEntry { Predicate = new WakuuEnchantPredicate { CardTypeMask = WakuuEnchantPicking.MaskPower, ExactCost = 3 } },
        };
        Assert.That(Rank(cards, rule), Is.EqualTo(new[] { 0 }));
    }

    // ---------------------------------------------------------------
    // 遗物 / 牌组条件
    // ---------------------------------------------------------------

    [Test]
    public void 遗物条件_持有任一才命中()
    {
        List<WakuuEnchantCardInfo> cards = new() { C("A"), C("B") };
        List<WakuuEnchantRuleEntry> rule = new()
        {
            new WakuuEnchantRuleEntry { CardId = "A", RequiredRelicAny = new[] { "CHEMICAL_X" } },
            new WakuuEnchantRuleEntry { CardId = "B" },
        };
        // 没持有化学物X → A 不命中，走 B
        Assert.That(Rank(cards, rule, relics: new List<string> { "LANTERN" })[0], Is.EqualTo(1));
        // 持有化学物X → A 命中
        Assert.That(Rank(cards, rule, relics: new List<string> { "CHEMICAL_X" })[0], Is.EqualTo(0));
    }

    [Test]
    public void 牌组条件_含任一牌才命中()
    {
        List<WakuuEnchantCardInfo> cards = new() { C("PROLONG"), C("FALLBACK") };
        List<WakuuEnchantRuleEntry> rule = new()
        {
            new WakuuEnchantRuleEntry { CardId = "PROLONG", RequiredDeckCardAny = new[] { "BOOT_SEQUENCE" } },
            new WakuuEnchantRuleEntry { CardId = "FALLBACK" },
        };
        Assert.That(Rank(cards, rule, deck: new List<string> { "TRASH_TO_TREASURE" })[0], Is.EqualTo(1));
        Assert.That(Rank(cards, rule, deck: new List<string> { "BOOT_SEQUENCE" })[0], Is.EqualTo(0));
    }

    [Test]
    public void 没有XX是降级位置而非禁止持有()
    {
        // 用户口径：「没有 XX」= 与「有 XX」相比优先级降低（无条件降级位置），不是禁止持有。
        // 有能量遗物时上面的「吊杀(有)」先命中；没有时走到降级位置照样能选到吊杀。
        List<WakuuEnchantCardInfo> cards = new() { C("HANG") };
        List<WakuuEnchantRuleEntry> rule = new()
        {
            new WakuuEnchantRuleEntry { CardId = "HANG", RequiredRelicAny = new[] { "LANTERN" } }, // 吊杀（有热可可/灯笼/古茶具套装）
            new WakuuEnchantRuleEntry { CardId = "REBOOT" },
            new WakuuEnchantRuleEntry { CardId = "HANG" }, // 吊杀（没有……的降级位置，无条件）
            new WakuuEnchantRuleEntry { CardId = "OTHER" },
        };
        // 持有能量遗物：第一个条目命中 HANG
        Assert.That(Rank(cards, rule, relics: new List<string> { "LANTERN" })[0], Is.EqualTo(0));
        // 无能量遗物：第一个 HANG 不命中、REBOOT 不在候选 → 降级位置仍能选到 HANG（而非彻底跳过）
        Assert.That(Rank(cards, rule, relics: new List<string> { "ANCHOR" })[0], Is.EqualTo(0));
    }

    [Test]
    public void 降级位置低于前置的其它精确牌()
    {
        // 真实表里「吊杀(没有)」排在 重启 之后：牌组同时有重启和吊杀且无能量遗物时选重启。
        List<WakuuEnchantCardInfo> cards = new() { C("REBOOT"), C("HANG") };
        List<WakuuEnchantRuleEntry> rule = new()
        {
            new WakuuEnchantRuleEntry { CardId = "HANG", RequiredRelicAny = new[] { "LANTERN" } },
            new WakuuEnchantRuleEntry { CardId = "REBOOT" },
            new WakuuEnchantRuleEntry { CardId = "HANG" }, // 无条件降级位置
            new WakuuEnchantRuleEntry { CardId = "OTHER" },
        };
        Assert.That(Rank(cards, rule, relics: new List<string> { "ANCHOR" })[0], Is.EqualTo(0)); // REBOOT 优先
    }

    // ---------------------------------------------------------------
    // 排序键
    // ---------------------------------------------------------------

    [Test]
    public void 费用最高排序()
    {
        List<WakuuEnchantCardInfo> cards = new() { C("C1", cost: 1), C("C3", cost: 3), C("C2", cost: 2) };
        List<WakuuEnchantRuleEntry> rule = new() { new WakuuEnchantRuleEntry { Sort = WakuuEnchantSort.CostDesc } };
        Assert.That(Rank(cards, rule), Is.EqualTo(new[] { 1, 2, 0 }));
    }

    [Test]
    public void 费用最低排序()
    {
        List<WakuuEnchantCardInfo> cards = new() { C("C1", cost: 1), C("C0", cost: 0), C("C2", cost: 2) };
        List<WakuuEnchantRuleEntry> rule = new() { new WakuuEnchantRuleEntry { Sort = WakuuEnchantSort.CostAsc } };
        Assert.That(Rank(cards, rule), Is.EqualTo(new[] { 1, 0, 2 }));
    }

    [Test]
    public void 伤害最高排序_按伤害乘段数()
    {
        List<WakuuEnchantCardInfo> cards = new()
        {
            C("D6", WakuuEnchantPicking.CardTypeAttack, damage: 6, hits: 1),
            C("D3X2", WakuuEnchantPicking.CardTypeAttack, damage: 3, hits: 2), // 总伤 6
            C("D8", WakuuEnchantPicking.CardTypeAttack, damage: 8, hits: 1),
        };
        List<WakuuEnchantRuleEntry> rule = new() { new WakuuEnchantRuleEntry { Sort = WakuuEnchantSort.DamageDesc } };
        Assert.That(Rank(cards, rule), Is.EqualTo(new[] { 2, 0, 1 })); // 8 > 6(6x1) = 6(3x2)
    }

    [Test]
    public void 伤害排序_X费按段数3折算()
    {
        List<WakuuEnchantCardInfo> cards = new()
        {
            C("X2", WakuuEnchantPicking.CardTypeAttack, costsX: true, damage: 2),   // 2×3=6
            C("D5", WakuuEnchantPicking.CardTypeAttack, damage: 5, hits: 1),        // 5
            C("D7", WakuuEnchantPicking.CardTypeAttack, damage: 7, hits: 1),        // 7
        };
        List<WakuuEnchantRuleEntry> rule = new() { new WakuuEnchantRuleEntry { Sort = WakuuEnchantSort.DamageDesc } };
        Assert.That(Rank(cards, rule), Is.EqualTo(new[] { 2, 0, 1 })); // 7 > 6(X) > 5
    }

    [Test]
    public void 费用排序_X费按3参与比较()
    {
        List<WakuuEnchantCardInfo> cards = new() { C("X", costsX: true), C("C1", cost: 1), C("C3", cost: 3) };
        List<WakuuEnchantRuleEntry> rule = new() { new WakuuEnchantRuleEntry { Sort = WakuuEnchantSort.CostDesc } };
        // X=3 与 C3=3 并列，稳定排序保持原序（X 下标 0 在前）
        Assert.That(Rank(cards, rule), Is.EqualTo(new[] { 0, 2, 1 }));
    }

    [Test]
    public void 稀有度最高排序()
    {
        List<WakuuEnchantCardInfo> cards = new() { C("R1", rarity: 1), C("R3", rarity: 3), C("R2", rarity: 2) };
        List<WakuuEnchantRuleEntry> rule = new() { new WakuuEnchantRuleEntry { Sort = WakuuEnchantSort.RarityDesc } };
        Assert.That(Rank(cards, rule), Is.EqualTo(new[] { 1, 2, 0 }));
    }

    // ---------------------------------------------------------------
    // 规则表注册完整性
    // ---------------------------------------------------------------

    [Test]
    public void 有规则的附魔均返回非空()
    {
        string[] withRule = { "Adroit", "Corrupted", "Glam", "Imbued", "Inky", "Instinct", "Momentum",
            "Nimble", "PerfectFit", "RoyallyApproved", "Sharp", "Slither", "SlumberingEssence",
            "SoulsPower", "Sown", "Steady", "Swift", "Vigorous" };
        foreach (string name in withRule)
        {
            Assert.That(WakuuEnchantRules.ForEnchantment(name), Is.Not.Null, $"附魔 {name} 应有规则");
        }
    }

    [Test]
    public void 维持现状的附魔返回null()
    {
        Assert.That(WakuuEnchantRules.ForEnchantment("Goopy"), Is.Null);
        Assert.That(WakuuEnchantRules.ForEnchantment("Spiral"), Is.Null);
        Assert.That(WakuuEnchantRules.ForEnchantment("TezcatarasEmber"), Is.Null);
        Assert.That(WakuuEnchantRules.ForEnchantment("UnknownEnchant"), Is.Null);
    }

    [Test]
    public void Clone_无陀螺分支首项为心灵震慑()
    {
        IReadOnlyList<WakuuEnchantRuleEntry> rule = WakuuEnchantRules.ForClone(hasUnceasingTop: false);
        Assert.That(rule, Is.Not.Null);
        Assert.That(rule![0].CardId, Is.EqualTo("MIND_BLAST"));
    }

    [Test]
    public void Clone_有陀螺分支首项为欺凌()
    {
        IReadOnlyList<WakuuEnchantRuleEntry> rule = WakuuEnchantRules.ForClone(hasUnceasingTop: true);
        Assert.That(rule, Is.Not.Null);
        Assert.That(rule![0].CardId, Is.EqualTo("BULLY"));
    }

    [Test]
    public void Clone_有陀螺分支末尾接无陀螺分支()
    {
        IReadOnlyList<WakuuEnchantRuleEntry> withTop = WakuuEnchantRules.ForClone(hasUnceasingTop: true);
        IReadOnlyList<WakuuEnchantRuleEntry> withoutTop = WakuuEnchantRules.ForClone(hasUnceasingTop: false);
        // 有陀螺分支包含无陀螺分支的全部条目（"无不休陀螺考虑的牌"）
        Assert.That(withTop.Count, Is.GreaterThan(withoutTop.Count));
        Assert.That(withTop[withTop.Count - withoutTop.Count].CardId, Is.EqualTo(withoutTop[0].CardId));
    }

    [Test]
    public void Resolve_Clone按持有陀螺选分支()
    {
        Assert.That(WakuuEnchantRules.Resolve("Clone", new List<string> { "UNCEASING_TOP" })![0].CardId, Is.EqualTo("BULLY"));
        Assert.That(WakuuEnchantRules.Resolve("Clone", new List<string> { "LANTERN" })![0].CardId, Is.EqualTo("MIND_BLAST"));
        Assert.That(WakuuEnchantRules.Resolve("Clone", null)![0].CardId, Is.EqualTo("MIND_BLAST"));
    }

    [Test]
    public void Resolve_非Clone按类型名查()
    {
        Assert.That(WakuuEnchantRules.Resolve("Adroit", null), Is.Not.Null);
        Assert.That(WakuuEnchantRules.Resolve("Goopy", null), Is.Null);
    }

    // ---------------------------------------------------------------
    // 端到端：用户表语义抽查
    // ---------------------------------------------------------------

    [Test]
    public void Adroit_选费用最低()
    {
        List<WakuuEnchantCardInfo> cards = new() { C("C2", cost: 2), C("C0", cost: 0), C("C1", cost: 1) };
        Assert.That(Rank(cards, WakuuEnchantRules.ForEnchantment("Adroit")), Is.EqualTo(new[] { 1, 2, 0 }));
    }

    [Test]
    public void Imbued_抽3张优先于费用最高()
    {
        List<WakuuEnchantCardInfo> cards = new()
        {
            C("DRAW2", WakuuEnchantPicking.CardTypeSkill, cost: 0, draw: 2),
            C("DRAW3", WakuuEnchantPicking.CardTypeSkill, cost: 2, draw: 3),
            C("EXPENSIVE", WakuuEnchantPicking.CardTypeSkill, cost: 3),
        };
        // 阶段 1（能抽 3 张以上）命中 DRAW3，直接返回它，不走费用最高阶段
        Assert.That(Rank(cards, WakuuEnchantRules.ForEnchantment("Imbued")), Is.EqualTo(new[] { 1 }));
    }

    [Test]
    public void Imbued_无抽3张时回退费用最高()
    {
        List<WakuuEnchantCardInfo> cards = new()
        {
            C("DRAW2", WakuuEnchantPicking.CardTypeSkill, cost: 0, draw: 2),
            C("EXPENSIVE", WakuuEnchantPicking.CardTypeSkill, cost: 3),
        };
        // 无抽 3 张以上 → 阶段 2 按费用最高排序（返回完整排序列表，调用方 Take）
        Assert.That(Rank(cards, WakuuEnchantRules.ForEnchantment("Imbued")), Is.EqualTo(new[] { 1, 0 }));
    }

    [Test]
    public void SoulsPower_精确牌名链优先于稀有度兜底()
    {
        List<WakuuEnchantCardInfo> cards = new()
        {
            C("RARE_X", rarity: 4),
            C("WHISTLE", rarity: 0),
        };
        List<int> ranked = Rank(cards, WakuuEnchantRules.ForEnchantment("SoulsPower"));
        Assert.That(ranked[0], Is.EqualTo(1)); // 吹哨精确命中，即便稀有度低
    }
}
