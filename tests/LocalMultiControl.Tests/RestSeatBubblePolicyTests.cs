using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 第三方席位休息区气泡判据测试（r149）。
///
/// 回归背景（2026-09-26 实机定位）：非本地席位的休息区选择在 BeginRestSite 那一刻就被索取并当场作答
/// （Co-op Bots 的合成 Bot），而休息区房间节点之后才实例化 ⇒ 没有动画、没有气泡，
/// 玩家以为「Bot 在休息处不会行动」。我们改成"记录 + 房间就绪后补画气泡"，
/// 这里的判据决定「谁需要补画」与「什么情况才画成已选」。
/// </summary>
[TestFixture]
public class RestSeatBubblePolicyTests
{
    [Test]
    public void ShouldRecord_本地多控开启且为第三方席位_需要记录()
    {
        Assert.That(RestSeatBubblePolicy.ShouldRecord(isLocalMultiControlEnabled: true, isLocalSessionSeat: false), Is.True);
    }

    [Test]
    public void ShouldRecord_本地席位_不需要记录()
    {
        Assert.That(RestSeatBubblePolicy.ShouldRecord(isLocalMultiControlEnabled: true, isLocalSessionSeat: true), Is.False);
    }

    [Test]
    public void ShouldRecord_未开本地多控_不记录()
    {
        // 未开本地多控时局内不可能有第三方合成席位；记录只会白留垃圾。
        Assert.That(RestSeatBubblePolicy.ShouldRecord(isLocalMultiControlEnabled: false, isLocalSessionSeat: false), Is.False);
    }

    [Test]
    public void ShouldShowSelectedBubble_执行成功_画已选()
    {
        Assert.That(RestSeatBubblePolicy.ShouldShowSelectedBubble(success: true), Is.True);
    }

    [Test]
    public void ShouldShowSelectedBubble_执行失败_不画已选()
    {
        // 关键：失败（例如 MEND 没拿到目标）必须只记 WARN，不能画成"已选"，
        // 否则正好把"其实没生效的选择"显示成生效了。
        Assert.That(RestSeatBubblePolicy.ShouldShowSelectedBubble(success: false), Is.False);
    }
}
