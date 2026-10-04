using System.Linq;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「会话复位矩阵」的可单测化（R5-3）。
///
/// 要钉死的语义：
/// ① 三条时序（进 / 出大厅、进 / 退局、读档窗口）**共用同一个关闭出口** ⇒ 无论从哪条路进来，结果一致；
/// ② 关闭后**必须**回初值的项一个不漏（有残留会点名，键即状态名，可直接对照日志）；
/// ③ 刻意不复位的项（目标人数 / 席位表 / 瓦库名单）在关闭后**保持原样** —— 它们是跨会话的配置态，
///    复位反而会把玩家设定的席位冲掉；
/// ④ 窗口判定用传入时间驱动（不读真实时钟）⇒ 边界可精确断言。
/// </summary>
[TestFixture]
public class SelfCoopSessionStateTests
{
    private static SelfCoopSessionState Fresh => new();

    [Test]
    public void 干净会话_无残留()
    {
        SelfCoopSessionState state = Fresh;
        Assert.That(state.FindResiduals(), Is.Empty);
        Assert.That(SelfCoopSessionState.Describe(state.FindResiduals()), Is.EqualTo("无残留"));
    }

    [Test]
    public void 进大厅_编辑席位跟随主席位()
    {
        SelfCoopSessionState state = Fresh;
        state.PrimaryPlayerId = 5;
        state.EnterSession();

        Assert.That(state.IsEnabled, Is.True);
        Assert.That(state.CurrentLobbyEditingPlayerId, Is.EqualTo(5UL));
    }

    [Test]
    public void 进大厅再离开_无残留()
    {
        SelfCoopSessionState state = Fresh;
        state.EnterSession();
        state.CurrentLobbyEditingPlayerId = 3;   // 玩家在大厅里切过席位
        state.LeaveSession();

        Assert.That(state.FindResiduals(), Is.Empty);
    }

    [Test]
    public void 会话开着时_判据会点名_仅供关闭后使用()
    {
        SelfCoopSessionState state = Fresh;
        state.EnterSession();

        // 会话开着时 is-enabled 命中是"正常的"，本判据只允许在会话确实关闭之后用（由调用方保证）。
        Assert.That(state.FindResiduals(), Does.Contain("is-enabled=set"));
    }

    [Test]
    public void 退出时_清掉编辑席位_事件挂起_页面上限_读档窗口()
    {
        SelfCoopSessionState state = Fresh;
        state.EnterSession();
        state.CurrentLobbyEditingPlayerId = 4;
        state.RequestEventAutoSwitch(7);
        state.ConfirmEventAutoSwitch(7);
        state.LobbyLocalPlayerLimit = 4;
        state.OpenLoadReplayWindow(1_000);

        state.LeaveSession();

        Assert.That(state.IsEnabled, Is.False);
        Assert.That(state.CurrentLobbyEditingPlayerId, Is.EqualTo(state.PrimaryPlayerId));
        Assert.That(state.PendingEventAutoSwitchPlayerId, Is.Null);
        Assert.That(state.EventAutoSwitchPending, Is.False);
        Assert.That(state.LobbyLocalPlayerLimit, Is.EqualTo(SelfCoopSessionState.MaxLocalPlayerCount));
        Assert.That(state.LoadReplayWindowOpenedAtMs, Is.Zero);
        Assert.That(state.FindResiduals(), Is.Empty);
    }

    [Test]
    public void 跨会话配置态_关闭时保持原样()
    {
        SelfCoopSessionState state = Fresh;
        state.DesiredLocalPlayerCount = 4;
        state.WakuuPlayerIds.Add(2);
        state.CoopBotsPlayerIds.Add(3);

        state.EnterSession();
        state.LeaveSession();

        Assert.That(state.DesiredLocalPlayerCount, Is.EqualTo(4), "目标人数是配置态，不该被复位");
        Assert.That(state.WakuuPlayerIds, Does.Contain(2UL), "瓦库名单跨会话保留，不该被复位");
        Assert.That(state.CoopBotsPlayerIds, Does.Contain(3UL), "联机机器人名单跨会话保留，不该被复位");
    }

    [Test]
    public void 退局与离开大厅_走同一出口_结果一致()
    {
        // 两条路都只是调 LeaveSession ⇒ 用"同样的脏态"分别关闭，残留判定必须一致。
        SelfCoopSessionState byLobbyExit = Fresh;
        byLobbyExit.EnterSession();
        byLobbyExit.CurrentLobbyEditingPlayerId = 2;
        byLobbyExit.LobbyLocalPlayerLimit = 4;
        byLobbyExit.LeaveSession();

        SelfCoopSessionState byRunCleanup = Fresh;
        byRunCleanup.EnterSession();
        byRunCleanup.CurrentLobbyEditingPlayerId = 2;
        byRunCleanup.LobbyLocalPlayerLimit = 4;
        byRunCleanup.LeaveSession();

        Assert.That(byLobbyExit.FindResiduals(), Is.EqualTo(byRunCleanup.FindResiduals()));
        Assert.That(byLobbyExit.FindResiduals(), Is.Empty);
    }

    [Test]
    public void 读档窗口_开启即有效_到期失效()
    {
        SelfCoopSessionState state = Fresh;
        state.OpenLoadReplayWindow(1_000);

        Assert.That(state.IsLoadReplayWindowActive(1_000), Is.True, "刚开启必须有效（守卫不得下手）");
        Assert.That(state.IsLoadReplayWindowActive(1_000 + LoadReplayWindowPolicy.DefaultTimeoutMs - 1), Is.True);
        Assert.That(state.IsLoadReplayWindowActive(1_000 + LoadReplayWindowPolicy.DefaultTimeoutMs), Is.False,
            "到期即失效（安全阀：读档取消后守卫恢复职责）");
    }

    [Test]
    public void 读档窗口_关闭返回时长_未开窗返回负值()
    {
        SelfCoopSessionState state = Fresh;
        Assert.That(state.CloseLoadReplayWindow(9_999), Is.LessThan(0), "未开窗时调用方应跳过日志");

        state.OpenLoadReplayWindow(500);
        Assert.That(state.CloseLoadReplayWindow(2_000), Is.EqualTo(1_500));
        Assert.That(state.IsLoadReplayWindowActive(2_000), Is.False);
        Assert.That(state.CloseLoadReplayWindow(2_100), Is.LessThan(0), "重复关闭是幂等的");
    }

    [Test]
    public void 会话关闭_带走读档窗口()
    {
        SelfCoopSessionState state = Fresh;
        state.EnterSession();
        state.OpenLoadReplayWindow(1_000);

        state.LeaveSession();

        Assert.That(state.IsLoadReplayWindowActive(1_000), Is.False);
        Assert.That(state.FindResiduals(), Is.Empty);
    }

    [Test]
    public void 事件自动切换_三段式_请求_确认_消费()
    {
        SelfCoopSessionState state = Fresh;
        state.EnterSession();

        state.RequestEventAutoSwitch(7);
        Assert.That(state.PendingEventAutoSwitchPlayerId, Is.EqualTo(7UL));
        Assert.That(state.EventAutoSwitchPending, Is.False);

        Assert.That(state.ConfirmEventAutoSwitch(7), Is.True);
        Assert.That(state.PendingEventAutoSwitchPlayerId, Is.Null, "确认后请求升级为待消费");
        Assert.That(state.EventAutoSwitchPending, Is.True);

        Assert.That(state.TryConsumeEventAutoSwitch(), Is.True);
        Assert.That(state.TryConsumeEventAutoSwitch(), Is.False, "二次消费不重复触发切人");
    }

    [Test]
    public void 事件自动切换_owner不匹配不确认()
    {
        SelfCoopSessionState state = Fresh;
        state.RequestEventAutoSwitch(7);

        Assert.That(state.ConfirmEventAutoSwitch(8), Is.False);
        Assert.That(state.PendingEventAutoSwitchPlayerId, Is.EqualTo(7UL), "不匹配时请求原样保留");
        Assert.That(state.EventAutoSwitchPending, Is.False);
    }

    [Test]
    public void 事件自动切换_无挂起时确认与消费都是false()
    {
        SelfCoopSessionState state = Fresh;
        Assert.That(state.ConfirmEventAutoSwitch(1), Is.False);
        Assert.That(state.TryConsumeEventAutoSwitch(), Is.False);
    }

    [Test]
    public void 事件自动切换_作废清掉请求与待消费()
    {
        SelfCoopSessionState state = Fresh;
        Assert.That(state.CancelEventAutoSwitch(), Is.False, "本就无东西可作废");

        state.RequestEventAutoSwitch(3);
        Assert.That(state.CancelEventAutoSwitch(), Is.True);
        Assert.That(state.PendingEventAutoSwitchPlayerId, Is.Null);
        Assert.That(state.CancelEventAutoSwitch(), Is.False);

        state.RequestEventAutoSwitch(3);
        state.ConfirmEventAutoSwitch(3);
        Assert.That(state.CancelEventAutoSwitch(), Is.True, "待消费也能被作废");
        Assert.That(state.EventAutoSwitchPending, Is.False);
    }

    [Test]
    public void 残留判据_顺序固定且带值()
    {
        SelfCoopSessionState state = Fresh;
        state.IsEnabled = true;
        state.PrimaryPlayerId = 1;
        state.CurrentLobbyEditingPlayerId = 4;
        state.RequestEventAutoSwitch(7);
        state.LobbyLocalPlayerLimit = 4;
        state.OpenLoadReplayWindow(1_000);

        Assert.That(state.FindResiduals(), Is.EqualTo(new[]
        {
            "is-enabled=set",
            "lobby-editing-player=4",
            "pending-event-auto-switch=7",
            "lobby-local-player-limit=4",
            "load-replay-window=set",
        }));
        Assert.That(state.FindResiduals().Distinct().Count(), Is.EqualTo(5), "键不得重复（否则日志点名会歧义）");

        // 「待消费」是另一个时刻（确认后请求已升级、来源清空）⇒ 单独点名。
        state.ConfirmEventAutoSwitch(7);
        Assert.That(state.FindResiduals(), Does.Contain("event-auto-switch-pending=set"));
    }
}
