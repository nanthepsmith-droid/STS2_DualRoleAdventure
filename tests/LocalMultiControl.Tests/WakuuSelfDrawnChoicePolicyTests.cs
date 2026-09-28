using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「第三方自绘选牌」（BUG-23 方案 B）判据的纯逻辑测试。
///
/// 这一组判据决定**什么时候**我们才承认"这一席就是我"并代第三方驱动它的选牌界面 ——
/// 判错方向的代价不对称：放宽过多会替真人做决定 / 让原版把别人的牌当本地牌（r109 那类视觉异常）；
/// 放宽过少则回到"卡死或跳过"。所以逐条钉住真值表。
/// </summary>
[TestFixture]
public class WakuuSelfDrawnChoicePolicyTests
{
    // ---- IsManagedWakuuSeat：既有「作用域外自动作答」的六项口径 ----
    [Test]
    public void 托管口径_六项全满足才成立()
    {
        Assert.That(Policy(true, true, true, true, true, true), Is.True);
        Assert.That(Policy(false, true, true, true, true, true), Is.False, "本地多控未启用");
        Assert.That(Policy(true, false, true, true, true, true), Is.False, "非单人冒险档");
        Assert.That(Policy(true, true, false, true, true, true), Is.False, "非本地回环会话");
        Assert.That(Policy(true, true, true, false, true, true), Is.False, "非我们的本地席位");
        Assert.That(Policy(true, true, true, true, false, true), Is.False, "非后台托管档（真人可能在看）");
        Assert.That(Policy(true, true, true, true, true, false), Is.False, "非瓦库形态");
    }

    // ---- IsAutomatedSeatInPlay：窗口判据 = 托管口径 + 该席位此刻登记着托管选择器 ----
    [Test]
    public void 自动化窗口_还要该席位正在被我们驱动()
    {
        Assert.That(InPlay(hasManagedSelector: true), Is.True);
        Assert.That(InPlay(hasManagedSelector: false), Is.False, "没在自动化 ⇒ 不碰（真人可能正自己操作）");
        Assert.That(
            WakuuSelfDrawnChoicePolicy.IsAutomatedSeatInPlay(true, true, true, true, false, true, true),
            Is.False,
            "非后台托管档 ⇒ 窗口不成立");
    }

    // ---- ShouldWidenIsMe：只在 原判 false + 窗口内 + 第三方调用方 时改口 ----
    [Test]
    public void 身份放行_三条同时成立才改口()
    {
        Assert.That(
            WakuuSelfDrawnChoicePolicy.ShouldWidenIsMe(originalIsMe: false, automatedSeatInPlay: true, callerIsThirdParty: true),
            Is.True);
        Assert.That(
            WakuuSelfDrawnChoicePolicy.ShouldWidenIsMe(originalIsMe: true, automatedSeatInPlay: true, callerIsThirdParty: true),
            Is.False,
            "原本就是 true ⇒ 无需改口（也不能改：结果是别的东西）");
        Assert.That(
            WakuuSelfDrawnChoicePolicy.ShouldWidenIsMe(originalIsMe: false, automatedSeatInPlay: false, callerIsThirdParty: true),
            Is.False,
            "不在自动化窗口内 ⇒ 不放行");
        Assert.That(
            WakuuSelfDrawnChoicePolicy.ShouldWidenIsMe(originalIsMe: false, automatedSeatInPlay: true, callerIsThirdParty: false),
            Is.False,
            "原版调用方 ⇒ 语义一点不动");
    }

    // ---- ShouldAutoAnswerScreen：窗口内 + 有候选 + 没作答过 ----
    [Test]
    public void 自绘界面作答_窗口内有候选且未作答才代答()
    {
        Assert.That(
            WakuuSelfDrawnChoicePolicy.ShouldAutoAnswerScreen(automatedSeatInPlay: true, optionCount: 3, alreadyAnswered: false),
            Is.True);
        Assert.That(
            WakuuSelfDrawnChoicePolicy.ShouldAutoAnswerScreen(automatedSeatInPlay: true, optionCount: 0, alreadyAnswered: false),
            Is.False,
            "没候选 ⇒ 不代答（留着交真人）");
        Assert.That(
            WakuuSelfDrawnChoicePolicy.ShouldAutoAnswerScreen(automatedSeatInPlay: true, optionCount: 3, alreadyAnswered: true),
            Is.False,
            "已作答过 ⇒ 不重复驱动");
        Assert.That(
            WakuuSelfDrawnChoicePolicy.ShouldAutoAnswerScreen(automatedSeatInPlay: false, optionCount: 3, alreadyAnswered: false),
            Is.False,
            "不在窗口内（如真人自己操作该席位）⇒ 绝不代点");
    }

    private static bool Policy(
        bool enabled,
        bool singleAdventureMode,
        bool loopbackSession,
        bool isLocalSeat,
        bool backgroundMode,
        bool vakuuFormMode)
    {
        return WakuuSelfDrawnChoicePolicy.IsManagedWakuuSeat(
            enabled, singleAdventureMode, loopbackSession, isLocalSeat, backgroundMode, vakuuFormMode);
    }

    private static bool InPlay(bool hasManagedSelector)
    {
        return WakuuSelfDrawnChoicePolicy.IsAutomatedSeatInPlay(
            enabled: true,
            singleAdventureMode: true,
            loopbackSession: true,
            isLocalSeat: true,
            backgroundMode: true,
            vakuuFormMode: true,
            hasManagedSelector: hasManagedSelector);
    }
}
