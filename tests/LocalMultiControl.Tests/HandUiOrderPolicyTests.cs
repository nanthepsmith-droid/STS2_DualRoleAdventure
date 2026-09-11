using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 手牌 UI 顺序自愈判定（r111/r112，BUG-7）纯逻辑测试。
///
/// 核心口径：**只有「牌是同一批、仅顺序不同」才重排**；任何「多余 / 缺失」一律不处理——
/// 原版手牌变换的视觉更新故意延迟约 0.9s（`NCardTransformShineVfx.PlayUntilCardUpdate`），
/// 那段窗口里「数据已是新牌、UI 还是旧牌」是正常动画态，r111 曾据此整表重建 → 实机回归
/// 「打出的数据链停在屏幕中间不消耗」，r112 起改成只记录不处理（见下第 5 个用例）。
/// </summary>
[TestFixture]
public class HandUiOrderPolicyTests
{
    private static HandUiOrderAction Decide(string pile, string ui)
    {
        return HandUiOrderPolicy.Decide(Split(pile), Split(ui));
    }

    private static List<string> Split(string value)
    {
        return value.Length == 0 ? new List<string>() : new List<string>(value.Split('|'));
    }

    [Test]
    public void 顺序一致时不干预()
    {
        Assert.That(Decide("A|B|C", "A|B|C"), Is.EqualTo(HandUiOrderAction.None));
    }

    [Test]
    public void 两边都空时不干预()
    {
        Assert.That(Decide("", ""), Is.EqualTo(HandUiOrderAction.None));
    }

    [Test]
    public void 同一批牌顺序不同时重排()
    {
        // BUG-7 的主场景：牌都对、只是顺序和数据不一致 → 唯一允许的动作
        Assert.That(Decide("A|B|C", "B|A|C"), Is.EqualTo(HandUiOrderAction.Reorder));
    }

    [Test]
    public void 同名重复牌按次数比较时仍然能判出重排()
    {
        // 多张「防御」必须按次数算，不能被当成同一张
        Assert.That(Decide("A|A|B", "A|B|A"), Is.EqualTo(HandUiOrderAction.Reorder));
    }

    [Test]
    public void 变换动画窗口内不干预_UI带旧牌且数据是新牌()
    {
        // r112 回归用例：变换把 B 变成 C 后，视觉要等 ~0.9s 才把节点模型改成 C。
        // 该窗口「UI=A|B、数据=A|C」是**正常动画态**，据此重建会打断出牌结算。
        Assert.That(Decide("A|C", "A|B"), Is.EqualTo(HandUiOrderAction.None));
    }

    [Test]
    public void UI多出数据里没有的牌时不干预()
    {
        // 同上：多出的通常就是「还没更新完的旧牌节点」，一律不删（删了会缺牌/打断动画）
        Assert.That(Decide("A|B", "A|B|C"), Is.EqualTo(HandUiOrderAction.None));
    }

    [Test]
    public void 数据侧多出牌时不干预_视觉补间中()
    {
        // 抽牌/生成牌的节点是稍后由补间回调加进来的，此刻「数据有、UI 没有」完全正常
        Assert.That(Decide("A|B|C", "A|B"), Is.EqualTo(HandUiOrderAction.None));
    }

    [Test]
    public void 数据侧多且含重复牌时也不干预()
    {
        Assert.That(Decide("A|A|B", "A|B"), Is.EqualTo(HandUiOrderAction.None));
    }

    [Test]
    public void 列出UI多余节点()
    {
        Assert.That(
            HandUiOrderPolicy.ListUiExtras(Split("A|B"), Split("C|A|B")),
            Is.EqualTo(new List<string> { "C" }));
    }

    [Test]
    public void 列出数据侧缺失节点()
    {
        Assert.That(
            HandUiOrderPolicy.ListMissingInUi(Split("A|B|C"), Split("A|B")),
            Is.EqualTo(new List<string> { "C" }));
    }

    [Test]
    public void 多余与缺失都不存在时列表为空()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HandUiOrderPolicy.ListUiExtras(Split("A|B"), Split("B|A")), Is.Empty);
            Assert.That(HandUiOrderPolicy.ListMissingInUi(Split("A|B"), Split("B|A")), Is.Empty);
        });
    }
}
