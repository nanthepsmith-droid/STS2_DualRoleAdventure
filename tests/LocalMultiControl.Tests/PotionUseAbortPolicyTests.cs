using System;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// §待查-11：自动用药异常路径的收尾判定（弹残留模型 / 异常详情拼行）。
/// </summary>
[TestFixture]
public class PotionUseAbortPolicyTests
{
    [Test]
    public void 栈顶是这瓶药水_应继续弹()
    {
        Assert.That(PotionUseAbortPolicy.ShouldPopResidualModel(topIsPotion: true, poppedSoFar: 0), Is.True);
    }

    [Test]
    public void 栈顶是别人_一律不动()
    {
        Assert.That(PotionUseAbortPolicy.ShouldPopResidualModel(topIsPotion: false, poppedSoFar: 0), Is.False);
    }

    [Test]
    public void 已达上限_停止弹栈_防死循环()
    {
        Assert.That(
            PotionUseAbortPolicy.ShouldPopResidualModel(
                topIsPotion: true,
                poppedSoFar: PotionUseAbortPolicy.DefaultMaxResidualPops),
            Is.False);
    }

    [Test]
    public void 上限内_仍可继续弹()
    {
        Assert.That(
            PotionUseAbortPolicy.ShouldPopResidualModel(
                topIsPotion: true,
                poppedSoFar: PotionUseAbortPolicy.DefaultMaxResidualPops - 1),
            Is.True);
    }

    [Test]
    public void 非法层数_不弹()
    {
        Assert.That(PotionUseAbortPolicy.ShouldPopResidualModel(topIsPotion: true, poppedSoFar: -1), Is.False);
    }

    [Test]
    public void 异常详情_含类型名与消息()
    {
        string text = PotionUseAbortPolicy.DescribeException(new InvalidOperationException("Nullable object must have a value."));

        Assert.That(text, Does.Contain("InvalidOperationException"));
        Assert.That(text, Does.Contain("Nullable object must have a value."));
    }

    [Test]
    public void 异常详情_带栈帧并压成单行()
    {
        Exception? captured = null;
        try
        {
            throw new InvalidOperationException("抛一个真的，才有栈");
        }
        catch (Exception exception)
        {
            captured = exception;
        }

        string text = PotionUseAbortPolicy.DescribeException(captured!);

        Assert.That(text, Does.Contain("|"));
        Assert.That(text, Does.Not.Contain("\n"));
        Assert.That(text, Does.Not.Contain("\r"));
    }

    [Test]
    public void 异常详情_超长时截断并加省略号()
    {
        string text = PotionUseAbortPolicy.DescribeException(
            new InvalidOperationException(new string('x', 2000)),
            maxChars: 100);

        Assert.That(text.Length, Is.EqualTo(101));
        Assert.That(text, Does.EndWith("…"));
    }

    [Test]
    public void 异常详情_空输入不抛()
    {
        Assert.That(PotionUseAbortPolicy.DescribeException(null!), Is.EqualTo("unknown"));
    }
}
