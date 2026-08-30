using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 策略取值规范化（first/last/random）纯函数测试。
/// 覆盖 LocalWakuuAutopilotConfig.NormalizeChoiceMode 的合法/边界/非法输入。
/// </summary>
[TestFixture]
public class ConfigNormalizationTests
{
    [TestCase("first", "first")]
    [TestCase("last", "last")]
    [TestCase("random", "random")]
    [TestCase("FIRST", "first")]
    [TestCase("Last", "last")]
    [TestCase("RANDOM", "random")]
    [TestCase(" first ", "first")]
    [TestCase("  last  ", "last")]
    [TestCase("\trandom\n", "random")]
    public void NormalizeChoiceMode_合法取值_返回规范化值(string input, string expected)
    {
        Assert.That(LocalWakuuAutopilotConfig.NormalizeChoiceMode(input), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\t")]
    public void NormalizeChoiceMode_空或空白_返回null(string? input)
    {
        Assert.That(LocalWakuuAutopilotConfig.NormalizeChoiceMode(input), Is.Null);
    }

    [TestCase("middle")]
    [TestCase("random2")]
    [TestCase("FIRST!")]
    [TestCase("first,last")]
    [TestCase("F")]
    [TestCase("l")]
    [TestCase("随机")]
    public void NormalizeChoiceMode_非法取值_返回null(string input)
    {
        Assert.That(LocalWakuuAutopilotConfig.NormalizeChoiceMode(input), Is.Null);
    }

    [Test]
    public void NormalizeChoiceMode_大小写与空格组合_归一为小写()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LocalWakuuAutopilotConfig.NormalizeChoiceMode(" RandoM "), Is.EqualTo("random"));
            Assert.That(LocalWakuuAutopilotConfig.NormalizeChoiceMode("FiRsT"), Is.EqualTo("first"));
            Assert.That(LocalWakuuAutopilotConfig.NormalizeChoiceMode("lAsT"), Is.EqualTo("last"));
        });
    }

    [Test]
    public void 模式常量_与纯函数来源一致()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LocalWakuuAutopilotConfig.FirstChoiceMode, Is.EqualTo(WakuuChoiceModes.First));
            Assert.That(LocalWakuuAutopilotConfig.LastChoiceMode, Is.EqualTo(WakuuChoiceModes.Last));
            Assert.That(LocalWakuuAutopilotConfig.RandomChoiceMode, Is.EqualTo(WakuuChoiceModes.Random));
            Assert.That(LocalWakuuAutopilotConfig.HeuristicBrainMode, Is.EqualTo(WakuuBrainModes.Heuristic));
            Assert.That(LocalWakuuAutopilotConfig.AutoBrainMode, Is.EqualTo(WakuuBrainModes.Auto));
        });
    }
}

/// <summary>瓦库大脑模式（wakuuBrain 开关）取值规范化测试。</summary>
[TestFixture]
public class BrainModeNormalizationTests
{
    [TestCase("heuristic", "heuristic")]
    [TestCase("auto", "auto")]
    [TestCase("HEURISTIC", "heuristic")]
    [TestCase("Auto", "auto")]
    [TestCase(" heuristic ", "heuristic")]
    [TestCase("  auto  ", "auto")]
    public void NormalizeBrainMode_合法取值_返回规范化值(string input, string expected)
    {
        Assert.That(LocalWakuuAutopilotConfig.NormalizeBrainMode(input), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("solver")]
    [TestCase("random")]
    [TestCase("first")]
    [TestCase("heuristic2")]
    public void NormalizeBrainMode_非法取值_返回null(string? input)
    {
        Assert.That(LocalWakuuAutopilotConfig.NormalizeBrainMode(input), Is.Null);
    }

    [Test]
    public void NormalizeBrainMode_默认回退启发式()
    {
        // 与 Apply 的兜底一致：非法/缺失时落到 heuristic（行为零变化）
        Assert.That(LocalWakuuAutopilotConfig.NormalizeBrainMode("solver") ?? LocalWakuuAutopilotConfig.HeuristicBrainMode,
            Is.EqualTo("heuristic"));
    }
}
