using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 战灵召唤 Phase 2b：**D7 预设档表**的用例（罕见 / 稀有 / 先古 三档落地）。
///
/// 这一层的价值：把"四档齐全 + 成本递增 + 先古不进随机池（D17）+ 先古档是九连环的前置（D19）"
/// 这些**内容口径**钉成断言 —— 以后调参只该改表，不该悄悄改语义。
/// </summary>
[TestFixture]
public class HellwarWarGodPresetsTests
{
    [Test]
    public void 四档齐全且成本递增()
    {
        Assert.That(HellwarWarGodPresets.All, Has.Length.EqualTo(4));
        Assert.That(
            HellwarWarGodPresets.All.Select((preset) => preset.Tier),
            Is.EqualTo(new[]
            {
                HellwarWarGodTier.Common,
                HellwarWarGodTier.Rare,
                HellwarWarGodTier.Epic,
                HellwarWarGodTier.Ancient,
            }));

        List<int> costs = HellwarWarGodPresets.All.Select((preset) => preset.Cost).ToList();
        Assert.That(costs, Is.EqualTo(new[] { 1, 2, 3, 4 }), "D7 成本表：普通 1 / 罕见 2 / 稀有 3 / 先古 4");
    }

    [Test]
    public void 普通档用召唤者自身角色且不改卡组()
    {
        // 这是"自动兼容 mod 角色"的唯一入口（用户口径），不能被改成固定原版角色。
        Assert.Multiple(() =>
        {
            Assert.That(HellwarWarGodPresets.Common.SkinKey, Is.EqualTo(HellwarWarGodPresets.SkinOfSummoner));
            Assert.That(HellwarWarGodPresets.Common.UpgradeDeck, Is.False);
            Assert.That(HellwarWarGodPresets.Common.BonusHp, Is.EqualTo(0));
        });
    }

    [Test]
    public void 高档位全部升级卡组且先古额外加血()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarWarGodPresets.Rare.UpgradeDeck, Is.True);
            Assert.That(HellwarWarGodPresets.Epic.UpgradeDeck, Is.True);
            Assert.That(HellwarWarGodPresets.Ancient.UpgradeDeck, Is.True);
            Assert.That(HellwarWarGodPresets.Ancient.BonusHp, Is.EqualTo(HellwarWarGodPresets.AncientBonusHp));
            Assert.That(HellwarWarGodPresets.AncientBonusHp, Is.GreaterThan(0), "先古要比别人肉");
        });
    }

    [Test]
    public void 非普通档的皮肤各不相同()
    {
        List<string> skins = HellwarWarGodPresets.All
            .Where((preset) => preset.SkinKey != HellwarWarGodPresets.SkinOfSummoner)
            .Select((preset) => preset.SkinKey)
            .ToList();

        Assert.That(skins, Is.Unique, "每个高档位一个独立皮肤，玩家才看得出'随机到哪一档'");
        Assert.That(skins, Does.Not.Contain(HellwarWarGodPresets.SkinOfSummoner));
    }

    [Test]
    public void 先古不进无尽战神的随机池()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                HellwarWarGodPresets.RandomPool.Select((preset) => preset.Tier),
                Is.EqualTo(new[] { HellwarWarGodTier.Common, HellwarWarGodTier.Rare, HellwarWarGodTier.Epic }));
            Assert.That(HellwarWarGodPresets.IsInRandomPool(HellwarWarGodTier.Ancient), Is.False, "D17：先古是九连环专属");
            Assert.That(HellwarWarGodPresets.IsInRandomPool(HellwarWarGodTier.Common), Is.True);
        });
    }

    [Test]
    public void 随机取档按掷点确定性映射()
    {
        int size = HellwarWarGodPresets.RandomPool.Length;
        Assert.Multiple(() =>
        {
            Assert.That(HellwarWarGodPresets.PickRandom(0).Tier, Is.EqualTo(HellwarWarGodTier.Common));
            Assert.That(HellwarWarGodPresets.PickRandom(size).Tier, Is.EqualTo(HellwarWarGodTier.Common), "越界回绕");
            Assert.That(HellwarWarGodPresets.PickRandom(-1).Tier, Is.EqualTo(HellwarWarGodTier.Epic), "负数回绕不越界");
            Assert.That(HellwarWarGodPresets.PickRandom(int.MaxValue), Is.Not.Null);
        });
    }

    [Test]
    public void 非法的档位取值兜普通档()
    {
        Assert.That(HellwarWarGodPresets.Resolve((HellwarWarGodTier)99).Tier, Is.EqualTo(HellwarWarGodTier.Common));
        Assert.That(HellwarWarGodPresets.Resolve(HellwarWarGodTier.Ancient).SkinKey, Is.EqualTo(HellwarWarGodPresets.SkinRegent));
    }

    [Test]
    public void 摘要包含每一档()
    {
        string summary = HellwarWarGodPresets.DescribeAll();
        foreach (HellwarWarGodPreset preset in HellwarWarGodPresets.All)
        {
            Assert.That(summary, Does.Contain(preset.SkinKey), $"摘要应含皮肤键 {preset.SkinKey}");
        }
    }
}
