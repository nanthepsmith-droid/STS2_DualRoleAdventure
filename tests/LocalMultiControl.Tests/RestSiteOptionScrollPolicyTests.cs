using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 休息处选项滚动条的纯算术用例（r241 那轮"把待销毁的旧按钮也数进去"的事故就发生在这里的输入上）。
/// 真实数据锚（原版场景）：按钮 246.786 宽、`separation = 100`、屏幕 1920 ⇒ 可用 1920−2×24 = 1872。
/// </summary>
[TestFixture]
public class RestSiteOptionScrollPolicyTests
{
    private const float ButtonWidth = 246.786f;
    private const float Separation = 100f;

    private static List<float> Buttons(int count)
    {
        List<float> widths = new();
        for (int i = 0; i < count; i++)
        {
            widths.Add(ButtonWidth);
        }

        return widths;
    }

    [Test]
    public void 内容宽按按钮数与间隔累加()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RestSiteOptionScrollPolicy.ResolveContentWidth(Buttons(3), Separation), Is.EqualTo(3 * ButtonWidth + 2 * Separation).Within(0.01f));
            Assert.That(RestSiteOptionScrollPolicy.ResolveContentWidth(Buttons(7), Separation), Is.EqualTo(7 * ButtonWidth + 6 * Separation).Within(0.01f));
        });
    }

    [Test]
    public void 空表与非正宽返回零()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RestSiteOptionScrollPolicy.ResolveContentWidth(null, Separation), Is.EqualTo(0f));
            Assert.That(RestSiteOptionScrollPolicy.ResolveContentWidth(new List<float>(), Separation), Is.EqualTo(0f));
            Assert.That(RestSiteOptionScrollPolicy.ResolveContentWidth(new List<float> { 0f, -5f }, Separation), Is.EqualTo(0f));
        });
    }

    [Test]
    public void 非法宽被跳过但间隔按有效个数算()
    {
        // 2 个有效按钮（另一个 0 宽被跳过）⇒ 只有 1 段间隔。
        Assert.That(
            RestSiteOptionScrollPolicy.ResolveContentWidth(new List<float> { ButtonWidth, 0f, ButtonWidth }, Separation),
            Is.EqualTo(2 * ButtonWidth + Separation).Within(0.01f));
    }

    [Test]
    public void 负间隔按零处理()
    {
        Assert.That(
            RestSiteOptionScrollPolicy.ResolveContentWidth(Buttons(3), -50f),
            Is.EqualTo(3 * ButtonWidth).Within(0.01f));
    }

    /// <summary>原版 3~5 个选项本来就在屏幕里摆得下 ⇒ **不滚**（保持原版观感）。</summary>
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    public void 少量选项不启用滚动(int buttonCount)
    {
        float content = RestSiteOptionScrollPolicy.ResolveContentWidth(Buttons(buttonCount), Separation);
        Assert.That(RestSiteOptionScrollPolicy.ShouldEnableScroll(content, visibleLimit: 1872f), Is.False,
            $"内容宽={content:0}（{buttonCount} 个）仍在屏幕内");
    }

    /// <summary>6 个开始超屏（1982 &gt; 1872）、7 个更明显（2328）⇒ **启用**。</summary>
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    public void 选项超屏才启用滚动(int buttonCount)
    {
        float content = RestSiteOptionScrollPolicy.ResolveContentWidth(Buttons(buttonCount), Separation);
        Assert.That(RestSiteOptionScrollPolicy.ShouldEnableScroll(content, visibleLimit: 1872f), Is.True,
            $"内容宽={content:0}（{buttonCount} 个）已超出屏幕可用宽");
    }

    [Test]
    public void 屏幕宽未知时不启用()
    {
        Assert.That(RestSiteOptionScrollPolicy.ShouldEnableScroll(9999f, visibleLimit: 0f), Is.False);
    }

    [Test]
    public void 窗口宽取内容与屏幕的较小者()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RestSiteOptionScrollPolicy.ResolveWindowWidth(2328f, 1872f, 12f), Is.EqualTo(1872f).Within(0.01f),
                "内容远超屏幕 ⇒ 窗口开满");
            Assert.That(RestSiteOptionScrollPolicy.ResolveWindowWidth(1880f, 1872f, 12f), Is.EqualTo(1872f).Within(0.01f));
            Assert.That(RestSiteOptionScrollPolicy.ResolveWindowWidth(1800f, 1872f, 12f), Is.EqualTo(1812f).Within(0.01f),
                "内容略小于屏幕 ⇒ 窗口 = 内容 + 余量（最后那个按钮不贴裁剪边缘）");
            Assert.That(RestSiteOptionScrollPolicy.ResolveWindowWidth(2328f, 0f, 12f), Is.EqualTo(0f),
                "屏幕宽未知 ⇒ 不开口子");
        });
    }
}
