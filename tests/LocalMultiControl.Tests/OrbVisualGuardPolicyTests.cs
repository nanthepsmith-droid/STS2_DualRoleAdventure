using LocalMultiControl.Scripts.Runtime.PureLogic;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// r283：球槽动画兜底 —— 没有视觉球槽节点时必须跳过动画（数据层照常），
/// 事故原形 = 战神打出 SPINNER 后回合开始充球把 turn loop 打死。
/// </summary>
[TestFixture]
public class OrbVisualGuardPolicyTests
{
    [Test]
    public void 没有球槽节点_跳过动画()
    {
        Assert.That(OrbVisualGuardPolicy.ShouldSkipOrbVisual(0), Is.True);
    }

    [Test]
    public void 有球槽节点_照常播动画()
    {
        Assert.Multiple(() =>
        {
            Assert.That(OrbVisualGuardPolicy.ShouldSkipOrbVisual(1), Is.False);
            Assert.That(OrbVisualGuardPolicy.ShouldSkipOrbVisual(3), Is.False);
        });
    }

    [Test]
    public void 读不到槽位数_必须放行原逻辑()
    {
        // 反射面变了 ⇒ 宁可让原版照旧跑，也不要把正常玩家的球动画吞掉。
        Assert.That(OrbVisualGuardPolicy.ShouldSkipOrbVisual(OrbVisualGuardPolicy.UnknownSlotCount), Is.False);
    }
}
