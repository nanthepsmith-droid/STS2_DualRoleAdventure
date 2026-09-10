using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 结束回合按钮自愈判定（r104，BUG-2 切到瓦库点结束回合无效）纯逻辑测试：
/// 只在「前台角色可操作但按钮处于不可用状态」时才要求重评，其余情况一律不动。
/// </summary>
[TestFixture]
public class EndTurnButtonReconcilePolicyTests
{
    /// <summary>构造一份「前台角色可操作且按钮正常」的基准参数，测试里只改关心的那一项。</summary>
    private static bool ShouldReconcile(
        bool playerSideActive = true,
        bool combatInProgress = true,
        bool foregroundAlive = true,
        bool foregroundReady = false,
        bool inPickFlow = false,
        bool buttonStateEnabled = true,
        bool buttonInputEnabled = true)
    {
        return EndTurnButtonReconcilePolicy.ShouldReconcile(
            playerSideActive,
            combatInProgress,
            foregroundAlive,
            foregroundReady,
            inPickFlow,
            buttonStateEnabled,
            buttonInputEnabled);
    }

    [Test]
    public void 按钮状态与输入都正常时不重评()
    {
        // 绝大多数帧的常态：零开销放过
        Assert.That(ShouldReconcile(), Is.False);
    }

    [Test]
    public void 按钮被禁用而前台角色仍可操作时需要重评()
    {
        // BUG-2 现场：按钮停在 Disabled，点击事件根本不会派发 → 点结束回合没反应
        Assert.That(ShouldReconcile(buttonStateEnabled: false, buttonInputEnabled: false), Is.True);
    }

    [Test]
    public void 仅输入被禁用也需要重评()
    {
        // 状态是 Enabled 但输入没打开（Enable 尚未生效）时同样点不动
        Assert.That(ShouldReconcile(buttonStateEnabled: true, buttonInputEnabled: false), Is.True);
    }

    [Test]
    public void 仅内部状态为隐藏或禁用也需要重评()
    {
        Assert.That(ShouldReconcile(buttonStateEnabled: false, buttonInputEnabled: true), Is.True);
    }

    [Test]
    public void 前台角色已结束回合时不重评()
    {
        // 此时按钮本该是「撤销结束回合」态，属于游戏自己的状态机，不能抢
        Assert.That(ShouldReconcile(foregroundReady: true, buttonStateEnabled: false), Is.False);
    }

    [Test]
    public void 出牌或选牌流程中不重评()
    {
        Assert.That(ShouldReconcile(inPickFlow: true, buttonStateEnabled: false), Is.False);
    }

    [Test]
    public void 非玩家回合不重评()
    {
        Assert.That(ShouldReconcile(playerSideActive: false, buttonStateEnabled: false), Is.False);
    }

    [Test]
    public void 战斗未进行或前台角色缺失时不重评()
    {
        Assert.That(ShouldReconcile(combatInProgress: false, buttonStateEnabled: false), Is.False);
        Assert.That(ShouldReconcile(foregroundAlive: false, buttonStateEnabled: false), Is.False);
    }
}
