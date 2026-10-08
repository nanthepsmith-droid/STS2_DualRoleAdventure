using LocalMultiControl.Scripts.Runtime.PureLogic;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 战灵召唤 Phase 0 POC 纯逻辑测试（临时席位号段 / 环境开关 / 是否该生成）。
///
/// 这一层的价值：把"临时席位与正式席位天然隔离"这条**架构前提**钉成断言 ——
/// 一旦有人把哨兵段挪到 1..12 里，正式席位的大厅/切人/存档全部会被污染。
/// </summary>
[TestFixture]
public class HellwarPocPolicyTests
{
    [Test]
    public void 临时席位段与正式席位互不重叠()
    {
        Assert.That(HellwarPocPolicy.IsTemporarySeat(0), Is.False, "0 是空席位");
        for (ulong seat = 1; seat <= 12; seat++)
        {
            Assert.That(HellwarPocPolicy.IsTemporarySeat(seat), Is.False, $"正式席位 {seat} 不能被当成临时席位");
        }

        Assert.That(HellwarPocPolicy.IsTemporarySeat(HellwarPocPolicy.TempSeatBase), Is.True);
        Assert.That(HellwarPocPolicy.IsTemporarySeat(HellwarPocPolicy.TempSeatLimit), Is.False, "上限是半开区间");
        Assert.That(HellwarPocPolicy.IsTemporarySeat(0xFFFFUL), Is.False, "远离 Steam ID 与哨兵段");
    }

    [Test]
    public void 席位号分配从段起点开始且递增()
    {
        Assert.That(HellwarPocPolicy.SeatIdAt(0), Is.EqualTo(HellwarPocPolicy.TempSeatBase));
        Assert.That(HellwarPocPolicy.SeatIdAt(1), Is.EqualTo(HellwarPocPolicy.TempSeatBase + 1));
        Assert.That(HellwarPocPolicy.SeatIdAt(-3), Is.EqualTo(HellwarPocPolicy.TempSeatBase), "负数按 0 处理，不产生越界 id");
        Assert.That(HellwarPocPolicy.IsTemporarySeat(HellwarPocPolicy.SeatIdAt(1)), Is.True);
    }

    [TestCase("1", true)]
    [TestCase(" 1 ", true)]
    [TestCase("true", true)]
    [TestCase("TRUE", true)]
    [TestCase("on", true)]
    [TestCase("On", true)]
    [TestCase("0", false)]
    [TestCase("false", false)]
    [TestCase("off", false)]
    [TestCase("yes", false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    [TestCase(null, false)]
    public void 环境开关解析(string? raw, bool expected)
    {
        Assert.That(HellwarPocPolicy.ParseEnvFlag(raw), Is.EqualTo(expected));
    }

    [TestCase(null, false, Description = "文件不存在 = 关")]
    [TestCase("", true, Description = "空文件 = 开（最省事的开法）")]
    [TestCase("   ", true)]
    [TestCase("1", true)]
    [TestCase("on", true)]
    [TestCase("0", false, Description = "显式关")]
    [TestCase("false", false)]
    [TestCase("OFF", false)]
    [TestCase("anything", true)]
    public void 标志文件解析(string? content, bool expected)
    {
        Assert.That(HellwarPocPolicy.ParseFlagFile(content), Is.EqualTo(expected));
    }

    [Test]
    public void 全部条件满足才生成()
    {
        Assert.That(HellwarPocPolicy.ShouldSpawn(true, true, true, false, 0), Is.True);

        Assert.That(HellwarPocPolicy.ShouldSpawn(false, true, true, false, 0), Is.False, "POC 开关没开");
        Assert.That(HellwarPocPolicy.ShouldSpawn(true, false, true, false, 0), Is.False, "不是本地多控会话");
        Assert.That(HellwarPocPolicy.ShouldSpawn(true, true, false, false, 0), Is.False, "战斗未在进行中");
        Assert.That(HellwarPocPolicy.ShouldSpawn(true, true, true, true, 0), Is.False, "本场已生成过");
        Assert.That(HellwarPocPolicy.ShouldSpawn(true, true, true, false, 1), Is.False, "场上已有战神");
    }

    [Test]
    public void 跳过原因按优先级点名()
    {
        Assert.That(HellwarPocPolicy.DescribeSkipReason(false, false, false, true, 3), Is.EqualTo("poc-disabled"));
        Assert.That(HellwarPocPolicy.DescribeSkipReason(true, false, false, true, 3), Is.EqualTo("session-disabled"));
        Assert.That(HellwarPocPolicy.DescribeSkipReason(true, true, false, true, 3), Is.EqualTo("combat-not-live"));
        Assert.That(
            HellwarPocPolicy.DescribeSkipReason(true, true, true, true, 3),
            Is.EqualTo("already-spawned-this-combat"));
        Assert.That(HellwarPocPolicy.DescribeSkipReason(true, true, true, false, 3), Is.EqualTo("war-god-already-alive"));
        Assert.That(HellwarPocPolicy.DescribeSkipReason(true, true, true, false, 0), Is.EqualTo("spawn"));
    }
}
