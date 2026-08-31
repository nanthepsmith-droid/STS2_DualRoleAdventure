using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 智能选牌优先级纯函数测试（可行性分析 §9.2 规则表）。
/// 覆盖：卡牌类别归类、删除/复制/变化三场景的优先级排序、同权重保持原序、硬排除。
/// </summary>
[TestFixture]
public class PriorityPickingTests
{
    private static WakuuCardKind K(string? id, int cardType) => WakuuPriorityPicking.ClassifyCard(id, cardType);

    private const int ATK = WakuuPriorityPicking.CardTypeAttack;
    private const int SKL = WakuuPriorityPicking.CardTypeSkill;
    private const int STA = WakuuPriorityPicking.CardTypeStatus;
    private const int CUR = WakuuPriorityPicking.CardTypeCurse;
    private const int QST = WakuuPriorityPicking.CardTypeQuest;

    // ---------------------------------------------------------------
    // ClassifyCard：id + CardType 枚举值 → 优先级类别
    // ---------------------------------------------------------------

    [TestCase("CURSE_OF_THE_BELL", CUR, 5)] // WakuuCardKind.Curse
    [TestCase("BURN", STA, 4)]              // Status
    [TestCase("STRIKE_IRONCLAD", ATK, 2)]   // BasicStrike
    [TestCase("DEFEND_SILENT", SKL, 1)]     // BasicDefend
    [TestCase("FLAME_BARRIER", SKL, 0)]     // Other
    [TestCase("UPPERCUT", ATK, 0)]          // Other
    [TestCase("NEOW_QUEST", QST, 3)]        // Quest
    [TestCase(null, ATK, 0)]                // Other
    public void ClassifyCard_按类型与id归类(string? id, int cardType, int expectedKind)
    {
        // WakuuCardKind 为 internal，公开签名用 int 传参（与枚举取值一致）避免可访问性不一致
        Assert.That(K(id, cardType), Is.EqualTo((WakuuCardKind)expectedKind));
    }

    [TestCase(ATK, "STRIKE", 2)]
    [TestCase(ATK, "strike+", 2)]
    [TestCase(SKL, "DEFEND", 1)]
    [TestCase(ATK, "SOMETHING_STRIKE_LIKE", 2)]
    public void ClassifyCard_大小写不敏感且id含关键字即命中(int cardType, string id, int expectedKind)
    {
        Assert.That(K(id, cardType), Is.EqualTo((WakuuCardKind)expectedKind));
    }

    // ---------------------------------------------------------------
    // Remove（消耗/删除）：Curse > Status > Quest > Strike > 基础防御 > 其余
    // ---------------------------------------------------------------

    [Test]
    public void Remove_诅咒优先于一切()
    {
        List<WakuuCardKind> kinds = new()
        {
            K("FLAME_BARRIER", SKL),
            K("CURSE_OF_THE_BELL", CUR),
            K("STRIKE_IRONCLAD", ATK),
        };

        List<int> ranked = WakuuPriorityPicking.RankIndicesByScenario(WakuuPickScenario.Remove, kinds);
        Assert.That(ranked[0], Is.EqualTo(1)); // 诅咒
    }

    [Test]
    public void Remove_全类型按规则表排序()
    {
        List<WakuuCardKind> kinds = new()
        {
            K("FLAME_BARRIER", SKL),     // Other → 最低
            K("CURSE_OF_THE_BELL", CUR), // Curse → 最高
            K("BURN", STA),              // Status
            K("DEFEND_SILENT", SKL),     // BasicDefend
            K("STRIKE_IRONCLAD", ATK),   // BasicStrike
            K("NEOW_QUEST", QST),        // Quest
        };

        List<int> ranked = WakuuPriorityPicking.RankIndicesByScenario(WakuuPickScenario.Remove, kinds);
        Assert.That(ranked, Is.EqualTo(new[] { 1, 2, 5, 4, 3, 0 }));
    }

    [Test]
    public void Remove_同优先级保持原序_从左到右()
    {
        List<WakuuCardKind> kinds = new()
        {
            K("FLAME_BARRIER", SKL),
            K("BLOCK_UP", SKL),
        };

        List<int> ranked = WakuuPriorityPicking.RankIndicesByScenario(WakuuPickScenario.Remove, kinds);
        Assert.That(ranked, Is.EqualTo(new[] { 0, 1 }));
    }

    // ---------------------------------------------------------------
    // Copy（复制）：首选非坏牌；候选全坏时倒序 防御>打击>任务>状态>诅咒
    // ---------------------------------------------------------------

    [Test]
    public void Copy_首选非坏牌()
    {
        List<WakuuCardKind> kinds = new()
        {
            K("STRIKE_IRONCLAD", ATK),   // 坏牌
            K("FLAME_BARRIER", SKL),     // 好牌 → 应首选
            K("DEFEND_SILENT", SKL),     // 坏牌
        };

        List<int> ranked = WakuuPriorityPicking.RankIndicesByScenario(WakuuPickScenario.Copy, kinds);
        Assert.That(ranked[0], Is.EqualTo(1));
    }

    [Test]
    public void Copy_候选全坏时倒序_防御打击任务状态诅咒()
    {
        List<WakuuCardKind> kinds = new()
        {
            K("BURN", STA),              // Status
            K("CURSE_OF_THE_BELL", CUR), // Curse
            K("DEFEND_SILENT", SKL),     // BasicDefend → 倒序最高
            K("STRIKE_IRONCLAD", ATK),   // BasicStrike
            K("NEOW_QUEST", QST),        // Quest
        };

        List<int> ranked = WakuuPriorityPicking.RankIndicesByScenario(WakuuPickScenario.Copy, kinds);
        // Defend(2) > Strike(3) > Quest(4) > Status(0) > Curse(1)
        Assert.That(ranked, Is.EqualTo(new[] { 2, 3, 4, 0, 1 }));
    }

    [Test]
    public void Copy_混编时坏牌全部排在好牌之后()
    {
        List<WakuuCardKind> kinds = new()
        {
            K("STRIKE_IRONCLAD", ATK),
            K("FLAME_BARRIER", SKL),
            K("CURSE_OF_THE_BELL", CUR),
            K("BLOCK_UP", SKL),
        };

        List<int> ranked = WakuuPriorityPicking.RankIndicesByScenario(WakuuPickScenario.Copy, kinds);
        Assert.That(ranked, Is.EqualTo(new[] { 1, 3, 0, 2 }));
    }

    // ---------------------------------------------------------------
    // Transform（变化）：Strike > Defend > 其余；硬排除 Curse/Status/Quest
    // ---------------------------------------------------------------

    [Test]
    public void Transform_优先变掉打击与防御()
    {
        List<WakuuCardKind> kinds = new()
        {
            K("FLAME_BARRIER", SKL),     // Other
            K("STRIKE_IRONCLAD", ATK),   // Strike → 最高
            K("DEFEND_SILENT", SKL),     // Defend
        };

        List<int> ranked = WakuuPriorityPicking.RankIndicesByScenario(WakuuPickScenario.Transform, kinds);
        Assert.That(ranked, Is.EqualTo(new[] { 1, 2, 0 }));
    }

    [Test]
    public void Transform_硬排除类别垫底_但存在可选项时不会被选中()
    {
        List<WakuuCardKind> kinds = new()
        {
            K("CURSE_OF_THE_BELL", CUR),
            K("FLAME_BARRIER", SKL),
            K("BURN", STA),
        };

        List<int> ranked = WakuuPriorityPicking.RankIndicesByScenario(WakuuPickScenario.Transform, kinds);
        Assert.That(ranked[0], Is.EqualTo(1)); // 只选 Other
    }

    // ---------------------------------------------------------------
    // Unknown：原序返回
    // ---------------------------------------------------------------

    [Test]
    public void Unknown_返回原序()
    {
        List<WakuuCardKind> kinds = new()
        {
            K("CURSE_OF_THE_BELL", CUR),
            K("FLAME_BARRIER", SKL),
        };

        List<int> ranked = WakuuPriorityPicking.RankIndicesByScenario(WakuuPickScenario.Unknown, kinds);
        Assert.That(ranked, Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void RankIndicesByScenario_空集合返回空()
    {
        Assert.That(WakuuPriorityPicking.RankIndicesByScenario(WakuuPickScenario.Remove, new List<WakuuCardKind>()), Is.Empty);
        Assert.That(WakuuPriorityPicking.RankIndicesByScenario(WakuuPickScenario.Remove, null!), Is.Empty);
    }

    [Test]
    public void 排序结果覆盖全部下标无缺失无重复()
    {
        List<WakuuCardKind> kinds = new()
        {
            K("STRIKE_IRONCLAD", ATK),
            K("DEFEND_SILENT", SKL),
            K("FLAME_BARRIER", SKL),
            K("CURSE_OF_THE_BELL", CUR),
            K("BURN", STA),
        };

        foreach (WakuuPickScenario scenario in Enum.GetValues<WakuuPickScenario>())
        {
            List<int> ranked = WakuuPriorityPicking.RankIndicesByScenario(scenario, kinds);
            Assert.That(ranked, Is.Unique, $"scenario={scenario}");
            Assert.That(ranked.Order(), Is.EqualTo(Enumerable.Range(0, kinds.Count)), $"scenario={scenario}");
        }
    }

    // ---------------------------------------------------------------
    // ClassifyHandScenario（§9.1 ★★）：FromHand 的 source 类型名 + prefs 标题 loc key → 场景
    // ---------------------------------------------------------------

    [TestCase("card_selection/TO_EXHAUST", "Brand", (int)WakuuPickScenario.Remove)]
    [TestCase("card_selection/TO_REMOVE", "SomeRemoval", (int)WakuuPickScenario.Remove)]
    [TestCase("card_selection/TO_TRANSFORM", "EntropyPower", (int)WakuuPickScenario.Transform)]
    [TestCase(null, "DualWield", (int)WakuuPickScenario.Copy)]      // 自定义标题：靠 source 类型名识别
    [TestCase("card_selection/TO_DISCARD", "DualWield", (int)WakuuPickScenario.Copy)] // 非消耗/变换标题下靠类型名
    [TestCase("custom/COPY_ONE", "GreedyCopy", (int)WakuuPickScenario.Copy)]
    [TestCase(null, "SonicEcho", (int)WakuuPickScenario.Copy)]
    [TestCase(null, "MirrorImage", (int)WakuuPickScenario.Copy)]
    [TestCase(null, "ExhaustRitual", (int)WakuuPickScenario.Remove)] // 类型名含 Exhaust
    [TestCase("card_selection/TO_DISCARD", "Acrobatics", (int)WakuuPickScenario.Unknown)]
    [TestCase(null, null, (int)WakuuPickScenario.Unknown)]
    [TestCase(null, "Purity", (int)WakuuPickScenario.Unknown)]
    public void ClassifyHandScenario_按prefs标题与source类型名判定场景(string? prefsLocKey, string? sourceTypeName, int expectedScenario)
    {
        Assert.That(
            WakuuPriorityPicking.ClassifyHandScenario(sourceTypeName, prefsLocKey),
            Is.EqualTo((WakuuPickScenario)expectedScenario));
    }

    [Test]
    public void ClassifyHandScenario_prefs标题优先于source类型名()
    {
        // 预设标题（TO_EXHAUST/TO_TRANSFORM）确定性最高，优先于 source 类型名推断。
        Assert.That(WakuuPriorityPicking.ClassifyHandScenario("DualWield", "card_selection/TO_EXHAUST"),
            Is.EqualTo(WakuuPickScenario.Remove));
        Assert.That(WakuuPriorityPicking.ClassifyHandScenario("Brand", "card_selection/TO_TRANSFORM"),
            Is.EqualTo(WakuuPickScenario.Transform));
    }
}
