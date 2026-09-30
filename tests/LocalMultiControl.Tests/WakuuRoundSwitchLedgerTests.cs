using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「瓦库 → 非瓦库自动切人」的每回合一次名额 + 待处理请求台账（R4 第四刀）。
///
/// 要钉死的语义（都是原实现的既有行为，重构时逐字保留）：
/// ① 名额按「战斗身份 + 回合号」记 ⇒ 换战斗后同号回合不继承；
/// ② 手动切到瓦库也算用掉名额（r103：不然自动化会立刻把玩家弹回自己）；
/// ③ 请求是"下一帧再执行"的延迟意图 ⇒ 跨回合 / 名额已被别处用掉即作废并清空；
/// ④ 请求执行失败（或被手动出牌守卫拦住）时**保留**，成功后由调用方清空。
/// </summary>
[TestFixture]
public class WakuuRoundSwitchLedgerTests
{
    private const int CombatA = 1001;
    private const int CombatB = 2002;
    private const int Round = 3;

    private static string KeyA => WakuuRoundSwitchLedger.BuildRoundKey(CombatA, Round);
    private static string KeyB => WakuuRoundSwitchLedger.BuildRoundKey(CombatB, Round);

    [SetUp]
    public void SetUp()
    {
        WakuuRoundSwitchLedger.Reset();
    }

    [Test]
    public void 回合键_格式为战斗身份加回合号()
    {
        Assert.Multiple(() =>
        {
            // 与旧实现 `$"{_lastAutoEndCombatIdentity}:{roundNumber}"` 逐字一致（含初始 -1）。
            Assert.That(WakuuRoundSwitchLedger.BuildRoundKey(-1, 3), Is.EqualTo("-1:3"));
            Assert.That(WakuuRoundSwitchLedger.BuildRoundKey(42, 1), Is.EqualTo("42:1"));
        });
    }

    [Test]
    public void 初始状态_无名额无请求()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRoundSwitchLedger.HasSwitched(KeyA), Is.False);
            Assert.That(WakuuRoundSwitchLedger.HasPending, Is.False);
        });
    }

    [Test]
    public void 占名额_首次为真_再次为假()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRoundSwitchLedger.MarkSwitched(KeyA), Is.True, "首次占用");
            Assert.That(WakuuRoundSwitchLedger.MarkSwitched(KeyA), Is.False, "同一回合再占不成立");
            Assert.That(WakuuRoundSwitchLedger.HasSwitched(KeyA), Is.True);
        });
    }

    [Test]
    public void 登记请求_空闲回合成并可读出来源()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRoundSwitchLedger.TryRegisterPending(KeyA, "wakuu-watchdog-x"), Is.True);
            Assert.That(WakuuRoundSwitchLedger.HasPending, Is.True);
            Assert.That(WakuuRoundSwitchLedger.PendingSource, Is.EqualTo("wakuu-watchdog-x"));
        });
    }

    [Test]
    public void 登记请求_名额已用掉的回合不登记()
    {
        // 原实现：`if (_wakuuToNonWakuuSwitchedRounds.Contains(roundKey)) return;` —— 连日志都不打。
        WakuuRoundSwitchLedger.MarkSwitched(KeyA);

        Assert.Multiple(() =>
        {
            Assert.That(WakuuRoundSwitchLedger.TryRegisterPending(KeyA, "wakuu-watchdog-x"), Is.False);
            Assert.That(WakuuRoundSwitchLedger.HasPending, Is.False);
        });
    }

    [Test]
    public void 未指定来源_兜底为wakuu_pending()
    {
        WakuuRoundSwitchLedger.TryRegisterPending(KeyA, null!);

        Assert.That(WakuuRoundSwitchLedger.PendingSource, Is.EqualTo(WakuuRoundSwitchLedger.DefaultPendingSource));
    }

    [Test]
    public void 无请求时_消费判定为假()
    {
        Assert.That(WakuuRoundSwitchLedger.IsPendingValidFor(KeyA), Is.False);
    }

    [Test]
    public void 请求命中当前回合_可执行且保留()
    {
        WakuuRoundSwitchLedger.TryRegisterPending(KeyA, "wakuu-watchdog-x");

        // 第一次判定为可执行（但不清空：调用方可能因手动出牌守卫或切换失败而放弃执行）。
        Assert.That(WakuuRoundSwitchLedger.IsPendingValidFor(KeyA), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(WakuuRoundSwitchLedger.HasPending, Is.True, "执行前保留");
            Assert.That(WakuuRoundSwitchLedger.PendingSource, Is.EqualTo("wakuu-watchdog-x"));
            Assert.That(WakuuRoundSwitchLedger.IsPendingValidFor(KeyA), Is.True, "下一次 tick 仍可再试");
        });
    }

    [Test]
    public void 切成功后清空请求()
    {
        WakuuRoundSwitchLedger.TryRegisterPending(KeyA, "wakuu-watchdog-x");
        WakuuRoundSwitchLedger.ClearPending();

        Assert.Multiple(() =>
        {
            Assert.That(WakuuRoundSwitchLedger.HasPending, Is.False);
            Assert.That(WakuuRoundSwitchLedger.IsPendingValidFor(KeyA), Is.False);
        });
    }

    [Test]
    public void 请求跨回合_自动作废()
    {
        WakuuRoundSwitchLedger.TryRegisterPending(KeyA, "wakuu-watchdog-x");

        Assert.Multiple(() =>
        {
            Assert.That(
                WakuuRoundSwitchLedger.IsPendingValidFor(WakuuRoundSwitchLedger.BuildRoundKey(CombatA, Round + 1)),
                Is.False);
            Assert.That(WakuuRoundSwitchLedger.HasPending, Is.False, "作废后不残留");
        });
    }

    [Test]
    public void 请求期间名额被别处用掉_自动作废()
    {
        // 场景：先登记了延迟请求，随后玩家手动切到瓦库角色（NoteManualSwitchToWakuu 占掉名额）
        // ⇒ 这个请求不该再执行（否则会把玩家立刻弹回自己）。
        WakuuRoundSwitchLedger.TryRegisterPending(KeyA, "wakuu-watchdog-x");
        WakuuRoundSwitchLedger.MarkSwitched(KeyA);

        Assert.Multiple(() =>
        {
            Assert.That(WakuuRoundSwitchLedger.IsPendingValidFor(KeyA), Is.False);
            Assert.That(WakuuRoundSwitchLedger.HasPending, Is.False);
        });
    }

    [Test]
    public void 换战斗后_同号回合不继承()
    {
        WakuuRoundSwitchLedger.MarkSwitched(KeyA);
        WakuuRoundSwitchLedger.TryRegisterPending(KeyA, "wakuu-watchdog-x");

        WakuuRoundSwitchLedger.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(WakuuRoundSwitchLedger.HasSwitched(KeyA), Is.False);
            Assert.That(WakuuRoundSwitchLedger.HasSwitched(KeyB), Is.False);
            Assert.That(WakuuRoundSwitchLedger.HasPending, Is.False);
        });
    }

    [Test]
    public void 不同战斗的同号回合_互不影响()
    {
        WakuuRoundSwitchLedger.MarkSwitched(KeyA);

        Assert.That(WakuuRoundSwitchLedger.HasSwitched(KeyB), Is.False, "别的战斗的同一回合号不继承名额");
    }
}
