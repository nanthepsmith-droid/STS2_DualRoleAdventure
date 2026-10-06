using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「炼化」（瓦库四功能之三）的纯逻辑测试：档位归一、收编量换算、选项可见性、排除项与目标判据。
///
/// 语义（2026-10-05 拍板，数值照提案原案）：血量比例档位 全部/一半/四分之一（默认全部）；
/// 卡组张数档位 不限/20/5（默认不限，且自选至少 1 张）；
/// 遗物不可收编【永久低语耳环】/【瓦库形态】；目标须"存活且血上限 &gt; 0"。
/// </summary>
[TestFixture]
public class WakuuRefinePolicyTests
{
    // ---- 档位归一：宽松兜默认（磁盘脏值不得让功能落到未定义分支）----

    [Test]
    public void 血量比例_空值或非法_兜全部()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRefinePolicy.NormalizeHpRatio(null), Is.EqualTo(WakuuRefinePolicy.HpRatioAll));
            Assert.That(WakuuRefinePolicy.NormalizeHpRatio("  "), Is.EqualTo(WakuuRefinePolicy.HpRatioAll));
            Assert.That(WakuuRefinePolicy.NormalizeHpRatio("threeQuarters"), Is.EqualTo(WakuuRefinePolicy.HpRatioAll));
        });
    }

    [Test]
    public void 血量比例_大小写与空白_归一为标准形式()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRefinePolicy.NormalizeHpRatio(" HALF "), Is.EqualTo(WakuuRefinePolicy.HpRatioHalf));
            Assert.That(WakuuRefinePolicy.NormalizeHpRatio("Quarter"), Is.EqualTo(WakuuRefinePolicy.HpRatioQuarter));
        });
    }

    [Test]
    public void 卡组档位_空值或非法_兜不限()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRefinePolicy.NormalizeCardLimit(null), Is.EqualTo(WakuuRefinePolicy.CardLimitAny));
            Assert.That(WakuuRefinePolicy.NormalizeCardLimit("all"), Is.EqualTo(WakuuRefinePolicy.CardLimitAny));
            Assert.That(WakuuRefinePolicy.NormalizeCardLimit("10"), Is.EqualTo(WakuuRefinePolicy.CardLimitAny));
        });
    }

    [Test]
    public void 卡组档位_数字档位_归一为标准形式()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRefinePolicy.NormalizeCardLimit(" 20 "), Is.EqualTo(WakuuRefinePolicy.CardLimit20));
            Assert.That(WakuuRefinePolicy.NormalizeCardLimit("5"), Is.EqualTo(WakuuRefinePolicy.CardLimit5));
        });
    }

    // ---- 收编血量换算 ----

    [Test]
    public void 收编血量_全部_等于血上限()
    {
        Assert.That(WakuuRefinePolicy.ResolveHpTransfer(80m, WakuuRefinePolicy.HpRatioAll), Is.EqualTo(80m));
    }

    [Test]
    public void 收编血量_一半与四分之一_向下取整()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRefinePolicy.ResolveHpTransfer(81m, WakuuRefinePolicy.HpRatioHalf), Is.EqualTo(40m));
            Assert.That(WakuuRefinePolicy.ResolveHpTransfer(81m, WakuuRefinePolicy.HpRatioQuarter), Is.EqualTo(20m));
        });
    }

    [Test]
    public void 收编血量_基数非正_返回零()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRefinePolicy.ResolveHpTransfer(0m, WakuuRefinePolicy.HpRatioAll), Is.EqualTo(0m));
            Assert.That(WakuuRefinePolicy.ResolveHpTransfer(-5m, WakuuRefinePolicy.HpRatioAll), Is.EqualTo(0m));
        });
    }

    [Test]
    public void 收编血量_非法档位_按全部处理()
    {
        Assert.That(WakuuRefinePolicy.ResolveHpTransfer(30m, "bogus"), Is.EqualTo(30m));
    }

    // ---- 卡组张数上限 ----

    [Test]
    public void 卡组上限_不限档位_等于卡组张数()
    {
        Assert.That(WakuuRefinePolicy.ResolveCardLimit(WakuuRefinePolicy.CardLimitAny, 37), Is.EqualTo(37));
    }

    [Test]
    public void 卡组上限_数字档位_取档位与卡组张数的较小者()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRefinePolicy.ResolveCardLimit(WakuuRefinePolicy.CardLimit20, 37), Is.EqualTo(20));
            Assert.That(WakuuRefinePolicy.ResolveCardLimit(WakuuRefinePolicy.CardLimit20, 8), Is.EqualTo(8));
            Assert.That(WakuuRefinePolicy.ResolveCardLimit(WakuuRefinePolicy.CardLimit5, 3), Is.EqualTo(3));
        });
    }

    [Test]
    public void 卡组上限_空卡组_为零()
    {
        Assert.That(WakuuRefinePolicy.ResolveCardLimit(WakuuRefinePolicy.CardLimitAny, 0), Is.EqualTo(0));
    }

    [Test]
    public void 选牌可零张_不妨碍小卡组()
    {
        // r220：用户「炼化时不能一张牌都不要」会妨碍玩小卡组 ⇒ Min=0。
        // 代价：空返回不能再区分"取消"与"确认 0 张"，故该步统一按"不拿牌、继续炼化"处理
        // （想中止整次炼化请用选目标 / 选遗物 / 选药水那三步的取消）。
        Assert.That(WakuuRefinePolicy.MinCardsToPick, Is.EqualTo(0));
    }

    // ---- 选项可见性 / 目标判据 / 排除项 ----

    [Test]
    public void 选项可见_条件齐备()
    {
        Assert.That(WakuuRefinePolicy.ShouldShowOption(featureEnabled: true, coopSessionActive: true, candidateCount: 1), Is.True);
    }

    [Test]
    public void 选项可见_开关关或会话不符或无候选_不显示()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRefinePolicy.ShouldShowOption(false, true, 1), Is.False);
            Assert.That(WakuuRefinePolicy.ShouldShowOption(true, false, 1), Is.False);
            Assert.That(WakuuRefinePolicy.ShouldShowOption(true, true, 0), Is.False);
        });
    }

    [Test]
    public void 目标判据_存活且血上限为正()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRefinePolicy.IsEligibleTarget(isAlive: true, targetMaxHp: 70), Is.True);
            Assert.That(WakuuRefinePolicy.IsEligibleTarget(isAlive: false, targetMaxHp: 70), Is.False);
            Assert.That(WakuuRefinePolicy.IsEligibleTarget(isAlive: true, targetMaxHp: 0), Is.False);
        });
    }

    [Test]
    public void 收编开关_直接反映配置()
    {
        // 开关关 ⇒ 不跑"选牌 / 选遗物"两步（牌与遗物留在瓦库身上，给复活留活路）。
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRefinePolicy.ShouldTakeAssets(true), Is.True);
            Assert.That(WakuuRefinePolicy.ShouldTakeAssets(false), Is.False);
        });
    }

    [Test]
    public void 药水步骤_开且有药水且有空位才跑()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                WakuuRefinePolicy.ShouldOfferPotionStep(takePotionsEnabled: true, vakuuPotionCount: 2, freePotionSlots: 1),
                Is.True);
            // 开关关 / 瓦库没药水 / 真人药水栏满 ⇒ 整步跳过（满栏时**不动**瓦库药水）。
            Assert.That(
                WakuuRefinePolicy.ShouldOfferPotionStep(takePotionsEnabled: false, vakuuPotionCount: 2, freePotionSlots: 1),
                Is.False);
            Assert.That(
                WakuuRefinePolicy.ShouldOfferPotionStep(takePotionsEnabled: true, vakuuPotionCount: 0, freePotionSlots: 1),
                Is.False);
            Assert.That(
                WakuuRefinePolicy.ShouldOfferPotionStep(takePotionsEnabled: true, vakuuPotionCount: 2, freePotionSlots: 0),
                Is.False);
        });
    }

    [Test]
    public void 遗物件数档位_归一与默认不限()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRefinePolicy.NormalizeRelicLimit(null), Is.EqualTo(WakuuRefinePolicy.RelicLimitAny));
            Assert.That(WakuuRefinePolicy.NormalizeRelicLimit(" 3 "), Is.EqualTo(WakuuRefinePolicy.RelicLimit3));
            Assert.That(WakuuRefinePolicy.NormalizeRelicLimit("1"), Is.EqualTo(WakuuRefinePolicy.RelicLimit1));
            Assert.That(WakuuRefinePolicy.NormalizeRelicLimit("5"), Is.EqualTo(WakuuRefinePolicy.RelicLimit5));
            Assert.That(WakuuRefinePolicy.NormalizeRelicLimit("bogus"), Is.EqualTo(WakuuRefinePolicy.RelicLimitAny));
        });
    }

    [Test]
    public void 遗物可拿上限_档位与候选数取小()
    {
        // 用户口径（2026-10-06）：只让拿 1 件则炼化收益太低 ⇒ 默认档是"不限"，1/3/5 保留给想收紧的人。
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRefinePolicy.ResolveRelicLimit(WakuuRefinePolicy.RelicLimitAny, 29), Is.EqualTo(29));
            Assert.That(WakuuRefinePolicy.ResolveRelicLimit(WakuuRefinePolicy.RelicLimit5, 29), Is.EqualTo(5));
            Assert.That(WakuuRefinePolicy.ResolveRelicLimit(WakuuRefinePolicy.RelicLimit3, 2), Is.EqualTo(2));
            Assert.That(WakuuRefinePolicy.ResolveRelicLimit(WakuuRefinePolicy.RelicLimit1, 29), Is.EqualTo(1));
            Assert.That(WakuuRefinePolicy.ResolveRelicLimit(WakuuRefinePolicy.RelicLimitAny, 0), Is.EqualTo(0));
        });
    }

    [Test]
    public void 药水可拿上限_取药水数与空位的较小者()
    {
        // 2026-10-06 用户口径「瓦库都死了，我不是想拿几瓶就拿几瓶」⇒ 多选，上限 = 自己的空位数。
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRefinePolicy.ResolvePotionTakeLimit(vakuuPotionCount: 4, freePotionSlots: 3), Is.EqualTo(3));
            Assert.That(WakuuRefinePolicy.ResolvePotionTakeLimit(vakuuPotionCount: 2, freePotionSlots: 3), Is.EqualTo(2));
            Assert.That(WakuuRefinePolicy.ResolvePotionTakeLimit(vakuuPotionCount: 4, freePotionSlots: 0), Is.EqualTo(0));
            Assert.That(WakuuRefinePolicy.ResolvePotionTakeLimit(vakuuPotionCount: 0, freePotionSlots: 3), Is.EqualTo(0));
        });
    }

    [Test]
    public void 排除项_耳环与瓦库形态不可收编()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRefinePolicy.IsExcludedRelic(isWhisperingEarring: true, isVakuuFormRelic: false), Is.True);
            Assert.That(WakuuRefinePolicy.IsExcludedRelic(isWhisperingEarring: false, isVakuuFormRelic: true), Is.True);
            Assert.That(WakuuRefinePolicy.IsExcludedRelic(isWhisperingEarring: false, isVakuuFormRelic: false), Is.False);
        });
    }
}
