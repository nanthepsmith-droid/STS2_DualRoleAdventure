using HextechRunesLocalCoopFix.Scripts;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 符文选取率角标的纯逻辑测试（compat-mods/HextechRunesLocalCoopFix，v1.7.0）。
///
/// 口径（用户 2026-10-21 点名要"看到选取率"）：角标显示**原始比例** `picked / offered`
/// （不是决策用的 Laplace 平滑分）；没记录显示 `—`；颜色档位 30% / 50% 分界。
/// </summary>
[TestFixture]
public class RuneBadgePolicyTests
{
    [Test]
    public void 没记录显示横线()
    {
        Assert.That(RuneBadgePolicy.Format(0, 0), Is.EqualTo("—"));
        Assert.That(RuneBadgePolicy.Format(-3, 2), Is.EqualTo("—"));
    }

    [Test]
    public void 正常比例带明细()
    {
        Assert.That(RuneBadgePolicy.Format(3, 1), Is.EqualTo("33% (1/3)"));
        Assert.That(RuneBadgePolicy.Format(3, 3), Is.EqualTo("100% (3/3)"));
        Assert.That(RuneBadgePolicy.Format(3, 0), Is.EqualTo("0% (0/3)"));
        Assert.That(RuneBadgePolicy.Format(2, 1), Is.EqualTo("50% (1/2)"));
    }

    [Test]
    public void 脏数据夹在合法区间()
    {
        Assert.That(RuneBadgePolicy.Format(1, 9), Is.EqualTo("100% (1/1)"));
        Assert.That(RuneBadgePolicy.Format(4, -2), Is.EqualTo("0% (0/4)"));
    }

    [Test]
    public void 档位按三成与五成分界()
    {
        Assert.That(RuneBadgePolicy.Classify(0, 0), Is.EqualTo(RuneBadgePolicy.Tier.Unknown));
        Assert.That(RuneBadgePolicy.Classify(10, 5), Is.EqualTo(RuneBadgePolicy.Tier.High));
        Assert.That(RuneBadgePolicy.Classify(10, 4), Is.EqualTo(RuneBadgePolicy.Tier.Mid));
        Assert.That(RuneBadgePolicy.Classify(10, 3), Is.EqualTo(RuneBadgePolicy.Tier.Mid), "30% 算中档");
        Assert.That(RuneBadgePolicy.Classify(10, 2), Is.EqualTo(RuneBadgePolicy.Tier.Low));
        Assert.That(RuneBadgePolicy.Classify(1, 0), Is.EqualTo(RuneBadgePolicy.Tier.Low));
    }
}
