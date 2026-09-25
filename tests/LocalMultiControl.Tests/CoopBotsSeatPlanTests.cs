using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 联机机器人（Co-op Bots）席位纯逻辑测试：
/// 配置文本解析 / 收敛到合法席位 / 与瓦库名单的三态互斥（联机机器人优先）/ 三态枚举行为。
/// 对应《Co-op_Bots联机队友兼容可行性分析》§4.3 与 §6 Phase 1。
/// </summary>
[TestFixture]
public class CoopBotsSeatPlanTests
{
    private static readonly List<ulong> Seats1To4 = new() { 1, 2, 3, 4 };

    [Test]
    public void Parse_单个席位_返回该席位()
    {
        Assert.That(CoopBotsSeatPlan.Parse("2", Seats1To4), Is.EqualTo(new List<ulong> { 2 }));
    }

    [TestCase("2,3;5")]
    [TestCase("2,3,5")]
    [TestCase(" 2 ; 3 ,5 ")]
    [TestCase("2|3\n5")]
    public void Parse_多种分隔符_全部识别(string raw)
    {
        Assert.That(CoopBotsSeatPlan.Parse(raw, Seats1To4), Is.EqualTo(new List<ulong> { 2, 3 }));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t")]
    [TestCase("abc")]
    [TestCase("0")]
    [TestCase("0,abc,-1")]
    public void Parse_空或非法输入_返回空表(string? raw)
    {
        Assert.That(CoopBotsSeatPlan.Parse(raw, Seats1To4), Is.Empty);
    }

    [Test]
    public void Parse_重复项去重()
    {
        Assert.That(CoopBotsSeatPlan.Parse("2,2,2", Seats1To4), Is.EqualTo(new List<ulong> { 2 }));
    }

    [Test]
    public void Parse_过滤不在本地席位表内的席位()
    {
        Assert.That(CoopBotsSeatPlan.Parse("2,9", Seats1To4), Is.EqualTo(new List<ulong> { 2 }));
    }

    [Test]
    public void Parse_顺序跟随本地席位表而不是输入顺序()
    {
        List<ulong> allowed = new() { 3, 1, 2 };
        Assert.That(CoopBotsSeatPlan.Parse("2,1,3", allowed), Is.EqualTo(new List<ulong> { 3, 1, 2 }));
    }

    [Test]
    public void Parse_无席位表时按席位号升序()
    {
        Assert.That(CoopBotsSeatPlan.Parse("5,2,3", null), Is.EqualTo(new List<ulong> { 2, 3, 5 }));
    }

    [Test]
    public void Parse_超出上限时截断()
    {
        Assert.That(CoopBotsSeatPlan.Parse("1,2,3", Seats1To4, maxCount: 2), Is.EqualTo(new List<ulong> { 1, 2 }));
    }

    [TestCase(new ulong[] { }, "")]
    [TestCase(new ulong[] { 2 }, "2")]
    [TestCase(new ulong[] { 3, 1, 3 }, "1,3")]
    public void Format_去重并按席位号升序(ulong[] seats, string expected)
    {
        Assert.That(CoopBotsSeatPlan.Format(seats), Is.EqualTo(expected));
    }

    [Test]
    public void ResolveDrivers_两侧重叠时联机机器人优先()
    {
        SeatDriverAssignment assignment = CoopBotsSeatPlan.ResolveDrivers(
            new List<ulong> { 1, 2 },
            new List<ulong> { 2, 3 });

        Assert.That(assignment.Wakuu, Is.EqualTo(new List<ulong> { 1 }));
        Assert.That(assignment.CoopBots, Is.EqualTo(new List<ulong> { 2, 3 }));
        Assert.That(assignment.Conflicts, Is.EqualTo(new List<ulong> { 2 }));
    }

    [Test]
    public void ResolveDrivers_无重叠时互不影响()
    {
        SeatDriverAssignment assignment = CoopBotsSeatPlan.ResolveDrivers(
            new List<ulong> { 1, 2 },
            new List<ulong> { 3 });

        Assert.That(assignment.Wakuu, Is.EqualTo(new List<ulong> { 1, 2 }));
        Assert.That(assignment.CoopBots, Is.EqualTo(new List<ulong> { 3 }));
        Assert.That(assignment.Conflicts, Is.Empty);
    }

    [Test]
    public void ResolveDrivers_乱序重复输入_输出有序去重()
    {
        SeatDriverAssignment assignment = CoopBotsSeatPlan.ResolveDrivers(
            new List<ulong> { 2, 1, 2, 0 },
            new List<ulong> { 3, 3, 2 });

        Assert.That(assignment.Wakuu, Is.EqualTo(new List<ulong> { 1 }));
        Assert.That(assignment.CoopBots, Is.EqualTo(new List<ulong> { 2, 3 }));
        Assert.That(assignment.Conflicts, Is.EqualTo(new List<ulong> { 2 }));
    }

    // 注意：SeatDriverMode 是 internal，[TestCase] 的参数只能用 int（测试方法本身必须是 public）。
    [TestCase(false, false, (int)SeatDriverMode.Human)]
    [TestCase(true, false, (int)SeatDriverMode.Wakuu)]
    [TestCase(false, true, (int)SeatDriverMode.CoopBots)]
    [TestCase(true, true, (int)SeatDriverMode.CoopBots)]
    public void Classify_两侧都命中时以联机机器人为准(bool wakuu, bool coopBots, int expected)
    {
        Assert.That(SeatDriverModes.Classify(wakuu, coopBots), Is.EqualTo((SeatDriverMode)expected));
    }

    [Test]
    public void Next_三态循环闭合()
    {
        Assert.That(SeatDriverModes.Next(SeatDriverMode.Human), Is.EqualTo(SeatDriverMode.Wakuu));
        Assert.That(SeatDriverModes.Next(SeatDriverMode.Wakuu), Is.EqualTo(SeatDriverMode.CoopBots));
        Assert.That(SeatDriverModes.Next(SeatDriverMode.CoopBots), Is.EqualTo(SeatDriverMode.Human));
    }

    [TestCase((int)SeatDriverMode.Human, "真人")]
    [TestCase((int)SeatDriverMode.Wakuu, "瓦库")]
    [TestCase((int)SeatDriverMode.CoopBots, "联机机器人")]
    public void Describe_中文名固定(int mode, string expected)
    {
        Assert.That(SeatDriverModes.Describe((SeatDriverMode)mode), Is.EqualTo(expected));
    }

    [TestCase("human", (int)SeatDriverMode.Human)]
    [TestCase("WAKUU", (int)SeatDriverMode.Wakuu)]
    [TestCase(" coopbots ", (int)SeatDriverMode.CoopBots)]
    public void Parse_三态取值(string raw, int expected)
    {
        Assert.That(SeatDriverModes.Parse(raw), Is.EqualTo((SeatDriverMode)expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("bot")]
    [TestCase("coop-bots")]
    public void Parse_非法取值返回null(string? raw)
    {
        Assert.That(SeatDriverModes.Parse(raw), Is.Null);
    }

    [Test]
    public void DescribeAssignment_逐席给出驱动名()
    {
        string text = CoopBotsSeatPlan.DescribeAssignment(
            new List<ulong> { 2, 1 },
            (seat) => seat == 2 ? SeatDriverMode.CoopBots : SeatDriverMode.Human);

        Assert.That(text, Is.EqualTo("seat=1(真人), seat=2(联机机器人)"));
    }

    [Test]
    public void DescribeAssignment_空表给出占位()
    {
        Assert.That(CoopBotsSeatPlan.DescribeAssignment(new List<ulong>(), (_) => SeatDriverMode.Human), Is.EqualTo("（无）"));
    }
}
