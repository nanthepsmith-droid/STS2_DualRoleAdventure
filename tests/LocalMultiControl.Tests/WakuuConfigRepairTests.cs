using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 磁盘配置历史脏值自愈（r110）测试。
/// 已知案例：r84 之前 <c>statBadgeCorner</c>/<c>statBadgeSource</c> 被误写进 <c>wakuuBrain</c>
/// （磁盘上是 <c>"wakuuBrain": "bottomRight"</c>），r84 修了写入路径但旧脏值一直留在盘上。
/// </summary>
[TestFixture]
public class WakuuConfigRepairTests
{
    [Test]
    public void 修复历史脏值_wakuuBrain被写成角标位置()
    {
        WakuuConfigData data = new() { wakuuBrain = "bottomRight" };

        Assert.Multiple(() =>
        {
            Assert.That(LocalWakuuAutopilotConfig.TryRepairHistoricalValues(data), Is.True);
            Assert.That(data.wakuuBrain, Is.EqualTo(LocalWakuuAutopilotConfig.HeuristicBrainMode));
        });
    }

    [Test]
    public void 全默认配置_无改动()
    {
        WakuuConfigData data = new();
        Assert.That(LocalWakuuAutopilotConfig.TryRepairHistoricalValues(data), Is.False);
    }

    [Test]
    public void 修复幂等_第二次不再改动()
    {
        WakuuConfigData data = new() { wakuuBrain = "bottomRight", cardPickMode = "bogus" };

        Assert.Multiple(() =>
        {
            Assert.That(LocalWakuuAutopilotConfig.TryRepairHistoricalValues(data), Is.True);
            Assert.That(LocalWakuuAutopilotConfig.TryRepairHistoricalValues(data), Is.False);
        });
    }

    [Test]
    public void 逐字段归一为合法取值()
    {
        WakuuConfigData data = new()
        {
            wakuuBrain = "solver",
            eventChoiceMode = "middle",
            cardPickMode = "rare2",
            personalTier = "nope",
            statBadgeCorner = "center",
            statBadgeSource = "guess",
            wakuuViewMode = "sometimes",
        };

        Assert.That(LocalWakuuAutopilotConfig.TryRepairHistoricalValues(data), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(data.wakuuBrain, Is.EqualTo(LocalWakuuAutopilotConfig.HeuristicBrainMode));
            Assert.That(data.eventChoiceMode, Is.EqualTo(LocalWakuuAutopilotConfig.FirstChoiceMode));
            Assert.That(data.cardPickMode, Is.EqualTo(LocalWakuuAutopilotConfig.LastChoiceMode));
            Assert.That(data.personalTier, Is.EqualTo(LocalWakuuAutopilotConfig.CharacterFirstTier));
            Assert.That(data.statBadgeCorner, Is.EqualTo(WakuuStatBadgeCorner.BottomLeft));
            Assert.That(data.statBadgeSource, Is.EqualTo(WakuuStatBadgeSource.PersonalOnly));
            Assert.That(data.wakuuViewMode, Is.EqualTo(WakuuViewModes.Never));
        });
    }

    [Test]
    public void 大小写变体_归一为标准形式()
    {
        WakuuConfigData data = new() { wakuuBrain = "AUTO", wakuuViewMode = "KEYNODES" };

        Assert.That(LocalWakuuAutopilotConfig.TryRepairHistoricalValues(data), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(data.wakuuBrain, Is.EqualTo(LocalWakuuAutopilotConfig.AutoBrainMode));
            Assert.That(data.wakuuViewMode, Is.EqualTo(WakuuViewModes.KeyNodes));
        });
    }

    [Test]
    public void 修复后序列化往返_仍是合法值()
    {
        WakuuConfigData data = new() { wakuuBrain = "bottomRight" };
        LocalWakuuAutopilotConfig.TryRepairHistoricalValues(data);

        WakuuConfigData roundTrip = WakuuConfigJson.Parse(WakuuConfigJson.Serialize(data))!;
        Assert.That(LocalWakuuAutopilotConfig.TryRepairHistoricalValues(roundTrip), Is.False);
    }
}
