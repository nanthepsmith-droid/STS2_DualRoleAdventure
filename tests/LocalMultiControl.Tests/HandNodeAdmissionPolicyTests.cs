using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「牌节点能不能进共享手牌 UI」判据的纯逻辑测试（2026-10-05 幽灵牌修复）。
///
/// 方向的代价不对称：**拦多了** = 玩家看不到自己的手牌（最坏）；**拦少了** = 别人的牌混进当前手牌
/// （幽灵牌，能选中弃掉、节点还不消失）。所以逐条钉住"什么时候必须放行"。
/// </summary>
[TestFixture]
public class HandNodeAdmissionPolicyTests
{
    private static HandNodeAdmission Decide(
        bool selectionActive = false,
        bool cardOwnerIsSelectionOwner = false,
        bool controlledSeatKnown = true,
        bool cardOwnerIsControlledSeat = true,
        bool cardOwnerIsLocalSeat = true,
        bool localCoop = true,
        bool cardOwnerKnown = true)
    {
        return HandNodeAdmissionPolicy.Decide(
            localCoop,
            cardOwnerKnown,
            selectionActive,
            cardOwnerIsSelectionOwner,
            controlledSeatKnown,
            cardOwnerIsControlledSeat,
            cardOwnerIsLocalSeat);
    }

    [Test]
    public void 非多控会话一律放行()
    {
        Assert.That(
            Decide(localCoop: false, controlledSeatKnown: true, cardOwnerIsControlledSeat: false),
            Is.EqualTo(HandNodeAdmission.Allow),
            "单机 / 真联机不归我们管");
    }

    [Test]
    public void 主人未知一律放行()
    {
        Assert.That(Decide(cardOwnerKnown: false, cardOwnerIsControlledSeat: false), Is.EqualTo(HandNodeAdmission.Allow));
    }

    // ---- ① 选牌进行中：只允许「本次选牌玩家」的牌 ----
    [Test]
    public void 选牌中_本次选牌玩家的牌照常加入()
    {
        Assert.That(
            Decide(selectionActive: true, cardOwnerIsSelectionOwner: true, cardOwnerIsControlledSeat: false),
            Is.EqualTo(HandNodeAdmission.Allow),
            "选牌界面显示的就是它的手牌（受控位可能已被切走，不按受控位判）");
    }

    [Test]
    public void 选牌中_别人的牌拦下()
    {
        Assert.That(
            Decide(selectionActive: true, cardOwnerIsSelectionOwner: false),
            Is.EqualTo(HandNodeAdmission.BlockNotSelectionOwner),
            "原有守卫口径：选牌期间混入的别人的牌能被选中 ⇒ 必拦");
    }

    // ---- ② 非选牌期间：共享手牌 UI 只显示受控席位的牌 ----
    [Test]
    public void 非选牌_受控席位自己的牌照常加入()
    {
        Assert.That(Decide(cardOwnerIsControlledSeat: true), Is.EqualTo(HandNodeAdmission.Allow));
    }

    [Test]
    public void 非选牌_我们自己别的席位的牌拦下()
    {
        Assert.That(
            Decide(cardOwnerIsControlledSeat: false, cardOwnerIsLocalSeat: true),
            Is.EqualTo(HandNodeAdmission.BlockNotControlledSeat),
            "幽灵牌：瓦库（后台托管席位）的牌不该出现在真人的手牌 UI 里");
    }

    [Test]
    public void 非选牌_受控位未知不干预()
    {
        Assert.That(
            Decide(controlledSeatKnown: false, cardOwnerIsControlledSeat: false),
            Is.EqualTo(HandNodeAdmission.Allow),
            "判据不足 ⇒ 放行（宁可漏拦也不要挡住玩家自己的手牌）");
    }

    [Test]
    public void 非选牌_第三方或联机席位不归这里管()
    {
        Assert.That(
            Decide(cardOwnerIsControlledSeat: false, cardOwnerIsLocalSeat: false),
            Is.EqualTo(HandNodeAdmission.Allow),
            "非本地席位另有归属链（CoopBots / 远端）⇒ 本次不动");
    }

    [Test]
    public void 选牌中优先按选牌口径_不看受控位()
    {
        Assert.That(
            Decide(selectionActive: true, cardOwnerIsSelectionOwner: false, cardOwnerIsControlledSeat: true),
            Is.EqualTo(HandNodeAdmission.BlockNotSelectionOwner),
            "选牌期间连受控席位自己的牌也要让位给选牌玩家（选牌界面里不该出现它的牌）");
    }
}
