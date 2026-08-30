using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 药水规则表完整性测试。
/// 通过 GetRuleMetas() 导出的元数据校验：规则总数、名称唯一、Match 目标类型提取、
/// 关键规则的属性（范围/相位/首回合/昏眩/目标/条件/选牌器）。改乱任一条规则即可被抓住。
/// </summary>
[TestFixture]
public class PotionRuleTableTests
{
    private static IReadOnlyList<WakuuPotionRuleMeta> _metas = Array.Empty<WakuuPotionRuleMeta>();

    private const string PotionNs = "MegaCrit.Sts2.Core.Models.Potions.";

    [OneTimeSetUp]
    public void LoadRuleMetas()
    {
        _metas = LocalWakuuPotionAutoUse.GetRuleMetas();
    }

    private WakuuPotionRuleMeta? Find(string name)
    {
        return _metas.FirstOrDefault((m) => m.Name == name);
    }

    // ---------------------------------------------------------------
    // 规则表整体完整性
    // ---------------------------------------------------------------

    [Test]
    public void 规则表_非空且数量在预期范围()
    {
        // 当前 60 条；留上下缓冲以容忍"新增规则但忘改测试"也能被大致察觉
        Assert.That(_metas.Count, Is.InRange(55, 65));
    }

    [Test]
    public void 规则表_名称唯一()
    {
        IEnumerable<string> duplicates = _metas
            .GroupBy((m) => m.Name)
            .Where((g) => g.Count() > 1)
            .Select((g) => g.Key);

        Assert.That(duplicates, Is.Empty, "存在重复的规则名: " + string.Join(", ", duplicates));
    }

    [Test]
    public void 规则表_每条规则都能提取Match目标药水类型()
    {
        IEnumerable<WakuuPotionRuleMeta> unextractable = _metas
            .Where((m) => m.MatchedPotionTypeName == null);

        Assert.That(unextractable, Is.Empty,
            "Match 目标类型提取失败的规则: " + string.Join(", ", unextractable.Select((m) => m.Name)));
    }

    [Test]
    public void 规则表_每条规则的Match目标都是药水命名空间类型()
    {
        IEnumerable<WakuuPotionRuleMeta> badNs = _metas
            .Where((m) => m.MatchedPotionTypeName == null || !m.MatchedPotionTypeName.StartsWith(PotionNs, StringComparison.Ordinal));

        Assert.That(badNs, Is.Empty);
    }

    [Test]
    public void 规则表_没有污浊药水规则()
    {
        // 污浊药水（FoulPotion）在战斗内被硬跳过（含自己全场伤害），只允许商人处投掷
        Assert.That(_metas.Any((m) => m.MatchedPotionTypeName == PotionNs + "FoulPotion"), Is.False);
    }

    [Test]
    public void 规则表_每种药水类型至多一条规则()
    {
        IEnumerable<string> dupTypes = _metas
            .Where((m) => m.MatchedPotionTypeName != null)
            .GroupBy((m) => m.MatchedPotionTypeName)
            .Where((g) => g.Count() > 1)
            .Select((g) => g.Key!);

        Assert.That(dupTypes, Is.Empty, "同一种药水被多条规则匹配: " + string.Join(", ", dupTypes));
    }

    // ---------------------------------------------------------------
    // 特殊处理类规则
    // ---------------------------------------------------------------

    [Test]
    public void 废弃药水丢弃_丢弃替代使用_任意战斗任意相位()
    {
        WakuuPotionRuleMeta? meta = Find("废弃药水丢弃");
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.DiscardInsteadOfUse, Is.True);
            Assert.That(meta.Scope, Is.EqualTo(WakuuPotionFightScope.AnyCombat));
            Assert.That(meta.Phases, Is.EqualTo(WakuuPotionPhase.Both));
            Assert.That(meta.MatchedPotionTypeName, Is.EqualTo(PotionNs + "DeprecatedPotion"));
        });
    }

    [Test]
    public void 果汁随时喝_无条件任意战斗()
    {
        WakuuPotionRuleMeta? meta = Find("果汁随时喝");
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.MatchedPotionTypeName, Is.EqualTo(PotionNs + "FruitJuice"));
            Assert.That(meta.Scope, Is.EqualTo(WakuuPotionFightScope.AnyCombat));
            Assert.That(meta.HasCondition, Is.False);
            Assert.That(meta.DiscardInsteadOfUse, Is.False);
        });
    }

    // ---------------------------------------------------------------
    // 治疗类规则
    // ---------------------------------------------------------------

    [Test]
    public void 血液药水低血自用_带条件任意战斗()
    {
        WakuuPotionRuleMeta? meta = Find("血液药水低血自用");
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.MatchedPotionTypeName, Is.EqualTo(PotionNs + "BloodPotion"));
            Assert.That(meta.Scope, Is.EqualTo(WakuuPotionFightScope.AnyCombat));
            Assert.That(meta.Phases, Is.EqualTo(WakuuPotionPhase.Both));
            Assert.That(meta.HasCondition, Is.True); // 低血才自用
            Assert.That(meta.FirstRoundOnly, Is.False);
        });
    }

    [Test]
    public void 再生药水低血自用_带条件任意战斗()
    {
        WakuuPotionRuleMeta? meta = Find("再生药水低血自用");
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.MatchedPotionTypeName, Is.EqualTo(PotionNs + "RegenPotion"));
            Assert.That(meta.HasCondition, Is.True);
        });
    }

    [Test]
    public void 龙涎香Boss残血_仅Boss回合末低血()
    {
        WakuuPotionRuleMeta? meta = Find("龙涎香Boss残血");
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.MatchedPotionTypeName, Is.EqualTo(PotionNs + "Ambergris"));
            Assert.That(meta.Scope, Is.EqualTo(WakuuPotionFightScope.BossFight));
            Assert.That(meta.Phases, Is.EqualTo(WakuuPotionPhase.EndOfTurn));
            Assert.That(meta.HasCondition, Is.True);
        });
    }

    // ---------------------------------------------------------------
    // 精英/Boss 首回合增益类
    // ---------------------------------------------------------------

    [Test]
    public void 力量首回合_硬仗首回合自用()
    {
        WakuuPotionRuleMeta? meta = Find("力量首回合");
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.MatchedPotionTypeName, Is.EqualTo(PotionNs + "StrengthPotion"));
            Assert.That(meta.Scope, Is.EqualTo(WakuuPotionFightScope.HardFight));
            Assert.That(meta.FirstRoundOnly, Is.True);
            Assert.That(meta.Target, Is.EqualTo(WakuuPotionTargetKind.Default));
            Assert.That(meta.HasCondition, Is.False);
        });
    }

    [Test]
    public void 集中首回合_给故障机器人()
    {
        WakuuPotionRuleMeta? meta = Find("集中首回合");
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.MatchedPotionTypeName, Is.EqualTo(PotionNs + "FocusPotion"));
            Assert.That(meta.Scope, Is.EqualTo(WakuuPotionFightScope.HardFight));
            Assert.That(meta.FirstRoundOnly, Is.True);
            Assert.That(meta.Target, Is.EqualTo(WakuuPotionTargetKind.AllyCharacter));
            Assert.That(meta.AllyCharacterTypeName, Does.Contain("Defect"));
        });
    }

    [TestCase("明耀酊剂首回合", "RadiantTincture")]
    [TestCase("明晰提取物首回合", "Clarity")]
    public void 抽牌类增益药水_昏眩时跳过(string ruleName, string potionTypeName)
    {
        WakuuPotionRuleMeta? meta = Find(ruleName);
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.SkipWhenStunned, Is.True, $"{ruleName} 应在昏眩时跳过");
            Assert.That(meta.MatchedPotionTypeName, Is.EqualTo(PotionNs + potionTypeName));
            Assert.That(meta.Scope, Is.EqualTo(WakuuPotionFightScope.HardFight));
            Assert.That(meta.FirstRoundOnly, Is.True);
        });
    }

    [Test]
    public void 爆炸安瓿多敌或首回合_带敌人数量条件()
    {
        WakuuPotionRuleMeta? meta = Find("爆炸安瓿多敌或首回合");
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.MatchedPotionTypeName, Is.EqualTo(PotionNs + "ExplosiveAmpoule"));
            Assert.That(meta.Scope, Is.EqualTo(WakuuPotionFightScope.HardFight));
            Assert.That(meta.FirstRoundOnly, Is.True);
            Assert.That(meta.HasCondition, Is.True); // 敌人 >= 3
        });
    }

    // ---------------------------------------------------------------
    // 给真人玩家优先
    // ---------------------------------------------------------------

    [Test]
    public void 复制药水优先真人()
    {
        WakuuPotionRuleMeta? meta = Find("复制药水优先真人");
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.MatchedPotionTypeName, Is.EqualTo(PotionNs + "Duplicator"));
            Assert.That(meta.Target, Is.EqualTo(WakuuPotionTargetKind.HumanFirst));
            Assert.That(meta.Scope, Is.EqualTo(WakuuPotionFightScope.HardFight));
            Assert.That(meta.FirstRoundOnly, Is.True);
        });
    }

    [Test]
    public void 超巨化优先真人()
    {
        WakuuPotionRuleMeta? meta = Find("超巨化优先真人");
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.MatchedPotionTypeName, Is.EqualTo(PotionNs + "GigantificationPotion"));
            Assert.That(meta.Target, Is.EqualTo(WakuuPotionTargetKind.HumanFirst));
        });
    }

    // ---------------------------------------------------------------
    // 角色专属给队友
    // ---------------------------------------------------------------

    [TestCase("扩容给药水机器人", "PotionOfCapacity", "Defect")]
    [TestCase("黑暗精华给故障机器人", "EssenceOfDarkness", "Defect")]
    [TestCase("星星给储君", "StarPotion", "Regent")]
    [TestCase("王之勇气给储君", "KingsCourage", "Regent")]
    [TestCase("骨头酿给亡灵契约师", "BoneBrew", "Necrobinder")]
    [TestCase("尸鬼瓮给亡灵契约师", "PotOfGhouls", "Necrobinder")]
    public void 角色专属药水_指向正确职业(string ruleName, string potionTypeName, string characterTypeName)
    {
        WakuuPotionRuleMeta? meta = Find(ruleName);
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.MatchedPotionTypeName, Is.EqualTo(PotionNs + potionTypeName));
            Assert.That(meta.Target, Is.EqualTo(WakuuPotionTargetKind.AllyCharacter));
            Assert.That(meta.AllyCharacterTypeName, Does.Contain(characterTypeName));
        });
    }

    [Test]
    public void 士兵炖汤给铁甲战士_仅Boss战_非首回合限定()
    {
        WakuuPotionRuleMeta? meta = Find("士兵炖汤给铁甲战士");
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.MatchedPotionTypeName, Is.EqualTo(PotionNs + "SoldiersStew"));
            Assert.That(meta.Scope, Is.EqualTo(WakuuPotionFightScope.BossFight));
            Assert.That(meta.Target, Is.EqualTo(WakuuPotionTargetKind.AllyCharacter));
            Assert.That(meta.AllyCharacterTypeName, Does.Contain("Ironclad"));
            Assert.That(meta.FirstRoundOnly, Is.False);
        });
    }

    // ---------------------------------------------------------------
    // 手牌构成条件类（定向选牌器）
    // ---------------------------------------------------------------

    [Test]
    public void 灰水消耗状态诅咒_带定向选牌器()
    {
        WakuuPotionRuleMeta? meta = Find("灰水消耗状态诅咒");
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.MatchedPotionTypeName, Is.EqualTo(PotionNs + "Ashwater"));
            Assert.That(meta.HasCondition, Is.True);
            Assert.That(meta.HasCardPicker, Is.True);
            Assert.That(meta.Scope, Is.EqualTo(WakuuPotionFightScope.HardFight));
        });
    }

    [Test]
    public void 赌徒特酿换掉坏牌_带定向选牌器()
    {
        WakuuPotionRuleMeta? meta = Find("赌徒特酿换掉坏牌");
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.MatchedPotionTypeName, Is.EqualTo(PotionNs + "GamblersBrew"));
            Assert.That(meta.HasCardPicker, Is.True);
            Assert.That(meta.HasCondition, Is.True);
        });
    }

    [Test]
    public void 痊愈药水无牌可出_仅回合开始_昏眩跳过()
    {
        WakuuPotionRuleMeta? meta = Find("痊愈药水无牌可出");
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.MatchedPotionTypeName, Is.EqualTo(PotionNs + "CureAll"));
            Assert.That(meta.Phases, Is.EqualTo(WakuuPotionPhase.StartOfTurn));
            Assert.That(meta.SkipWhenStunned, Is.True);
            Assert.That(meta.HasCondition, Is.True); // 无牌可出
        });
    }

    // ---------------------------------------------------------------
    // 回合结束防御/资源类
    // ---------------------------------------------------------------

    [Test]
    public void 格挡药水硬仗防御_回合末_带条件()
    {
        WakuuPotionRuleMeta? meta = Find("格挡药水硬仗防御");
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.MatchedPotionTypeName, Is.EqualTo(PotionNs + "BlockPotion"));
            Assert.That(meta.Phases, Is.EqualTo(WakuuPotionPhase.EndOfTurn));
            Assert.That(meta.HasCondition, Is.True);
        });
    }

    [Test]
    public void 能量药水救高费牌_回合末_昏眩跳过()
    {
        WakuuPotionRuleMeta? meta = Find("能量药水救高费牌");
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.MatchedPotionTypeName, Is.EqualTo(PotionNs + "EnergyPotion"));
            Assert.That(meta.Phases, Is.EqualTo(WakuuPotionPhase.EndOfTurn));
            Assert.That(meta.SkipWhenStunned, Is.True);
            Assert.That(meta.HasCondition, Is.True); // 能量为 0 且手牌有高费牌
        });
    }

    [Test]
    public void 异蛇之油剩能量抽牌_回合末_昏眩跳过()
    {
        WakuuPotionRuleMeta? meta = Find("异蛇之油剩能量抽牌");
        Assert.That(meta, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(meta!.MatchedPotionTypeName, Is.EqualTo(PotionNs + "SneckoOil"));
            Assert.That(meta.Phases, Is.EqualTo(WakuuPotionPhase.EndOfTurn));
            Assert.That(meta.SkipWhenStunned, Is.True);
        });
    }

    // ---------------------------------------------------------------
    // 意图触发 / 防御覆盖完整性抽查
    // ---------------------------------------------------------------

    [Test]
    public void 意图触发类规则_全部带条件()
    {
        string[] intentRules = { "甲虫汁敌人攻击", "镣铐敌人攻击", "铁心覆甲", "速度药水有技能牌" };
        foreach (string name in intentRules)
        {
            WakuuPotionRuleMeta? meta = Find(name);
            Assert.That(meta, Is.Not.Null, $"缺少规则: {name}");
            Assert.That(meta!.HasCondition, Is.True, $"{name} 应带触发条件");
        }
    }

    [Test]
    public void 首回合增益攻击类规则_全部硬仗限定()
    {
        string[] firstRoundHardRules =
        {
            "力量首回合", "敏捷首回合", "集中首回合", "异鱼之油首回合", "流动铜液首回合",
            "马萨雷斯赠礼首回合", "明耀酊剂首回合", "宇宙药剂首回合", "精炼混沌首回合", "明晰提取物首回合",
            "火焰首回合对敌", "毒素首回合对敌", "灾厄首回合对敌", "易伤首回合对敌", "虚弱首回合对敌",
            "消亡粉末首回合对敌", "攻击药水首回合", "技能药水首回合", "能力药水首回合", "无色药水首回合",
        };

        foreach (string name in firstRoundHardRules)
        {
            WakuuPotionRuleMeta? meta = Find(name);
            Assert.That(meta, Is.Not.Null, $"缺少规则: {name}");
            Assert.That(meta!.Scope, Is.EqualTo(WakuuPotionFightScope.HardFight), $"{name} 应限定硬仗");
            Assert.That(meta.FirstRoundOnly, Is.True, $"{name} 应限定首回合");
        }
    }
}
