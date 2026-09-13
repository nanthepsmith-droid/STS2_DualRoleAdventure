using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「这一位的回合是谁结束的」归因 + 自动出牌是否停手（BUG-10 收口第二版）。
///
/// 要钉死的语义：
/// ① 被**卡牌效果/原版强行结束**（虚空形态）→ 停手（BUG-10）；
/// ② 被**模组自己收口**且回合尚未推进 → 放行（用户要求保留"本回合内又拿到可出牌就继续打"）；
/// ③ 来源不明 → 保守停手；
/// ④ 全员 ready（回合即将推进）→ 一律停手。
/// </summary>
[TestFixture]
public class WakuuTurnEndOriginTests
{
    private const ulong Wakuu = 1001UL;
    private const int Round = 3;

    [SetUp]
    public void SetUp()
    {
        WakuuTurnEndOrigin.ResetForCombat();
    }

    private static bool Stop(bool ready, bool external, bool byMod, bool allReady)
        => WakuuTurnEndOrigin.ShouldStopAutoplay(ready, external, byMod, allReady);

    [Test]
    public void 未ready_永不停手()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Stop(ready: false, external: true, byMod: false, allReady: true), Is.False);
            Assert.That(Stop(ready: false, external: false, byMod: true, allReady: false), Is.False);
        });
    }

    [Test]
    public void 外部强行结束_停手_虚空形态()
    {
        // VoidForm.OnPlay → PlayerCmd.EndTurn(owner, canBackOut:false)；
        // 此时用户正在看的那一局里瓦库不该再出牌。
        Assert.That(Stop(ready: true, external: true, byMod: false, allReady: false), Is.True);
    }

    [Test]
    public void 来源不明_保守停手()
    {
        // 既不是模组发的、也没登记成外部（例如旧战局残留 / 补丁没覆盖到的新路径）→ 停手。
        Assert.That(Stop(ready: true, external: false, byMod: false, allReady: false), Is.True);
    }

    [Test]
    public void 模组自己收口_回合未推进_放行()
    {
        // 用户明确要求保留：模组因为"当刻无牌可出"收口后，本回合内又因别人的效果拿到可出牌 → 继续打。
        Assert.That(Stop(ready: true, external: false, byMod: true, allReady: false), Is.False);
    }

    [Test]
    public void 模组自己收口_但全员已ready_停手()
    {
        // 全员 ready ⇒ 回合马上推进，再出牌会撞进推进窗口。
        Assert.That(Stop(ready: true, external: false, byMod: true, allReady: true), Is.True);
    }

    [Test]
    public void 外部结束优先于模组收口()
    {
        // 同一回合先被模组收口、后被卡牌强行结束（例如中途被解除 ready 又打出虚空形态）→ 必须停手。
        Assert.That(Stop(ready: true, external: true, byMod: true, allReady: false), Is.True);
    }

    [Test]
    public void 豁免状态_仅模组收口且未全员ready时为真()
    {
        Assert.That(
            WakuuTurnEndOrigin.IsModIssuedExemption(
                playerReadyToEndTurn: true, endedExternally: false, endedByMod: true, allPlayersReadyToEndTurn: false),
            Is.True);
    }

    [Test]
    public void 豁免状态_虚空形态与全员ready都不算()
    {
        Assert.Multiple(() =>
        {
            // 虚空形态（外部结束）→ 不是豁免（r124 首版日志就是在这里误报的）。
            Assert.That(
                WakuuTurnEndOrigin.IsModIssuedExemption(true, endedExternally: true, endedByMod: true, allPlayersReadyToEndTurn: false),
                Is.False);
            // 来源不明 → 不是豁免。
            Assert.That(
                WakuuTurnEndOrigin.IsModIssuedExemption(true, endedExternally: false, endedByMod: false, allPlayersReadyToEndTurn: false),
                Is.False);
            // 全员 ready（回合即将推进）→ 不是豁免。
            Assert.That(
                WakuuTurnEndOrigin.IsModIssuedExemption(true, endedExternally: false, endedByMod: true, allPlayersReadyToEndTurn: true),
                Is.False);
            // 未 ready → 不是豁免。
            Assert.That(
                WakuuTurnEndOrigin.IsModIssuedExemption(false, endedExternally: false, endedByMod: true, allPlayersReadyToEndTurn: false),
                Is.False);
        });
    }

    [Test]
    public void 登记_模组发起与外部发起互斥且后者覆盖前者()
    {
        WakuuTurnEndOrigin.BeginModIssuedEnd();
        WakuuTurnEndOrigin.Record(Wakuu, Round);
        WakuuTurnEndOrigin.EndModIssuedEnd();

        Assert.Multiple(() =>
        {
            Assert.That(WakuuTurnEndOrigin.EndedByMod(Wakuu, Round), Is.True);
            Assert.That(WakuuTurnEndOrigin.EndedExternally(Wakuu, Round), Is.False);
        });

        // 同一 (回合, 玩家) 随后被外部结束 → 归因翻转，不能被旧记录掩护。
        WakuuTurnEndOrigin.Record(Wakuu, Round);

        Assert.Multiple(() =>
        {
            Assert.That(WakuuTurnEndOrigin.EndedByMod(Wakuu, Round), Is.False);
            Assert.That(WakuuTurnEndOrigin.EndedExternally(Wakuu, Round), Is.True);
        });
    }

    [Test]
    public void 登记_按回合与玩家区分()
    {
        WakuuTurnEndOrigin.BeginModIssuedEnd();
        WakuuTurnEndOrigin.Record(Wakuu, Round);
        WakuuTurnEndOrigin.EndModIssuedEnd();

        Assert.Multiple(() =>
        {
            Assert.That(WakuuTurnEndOrigin.EndedByMod(Wakuu, Round), Is.True);
            Assert.That(WakuuTurnEndOrigin.EndedByMod(Wakuu, Round + 1), Is.False, "下一回合不继承");
            Assert.That(WakuuTurnEndOrigin.EndedByMod(Wakuu + 1, Round), Is.False, "别的玩家不继承");
        });
    }

    [Test]
    public void 换战斗清空归因()
    {
        WakuuTurnEndOrigin.Record(Wakuu, Round);
        WakuuTurnEndOrigin.ResetForCombat();

        Assert.Multiple(() =>
        {
            Assert.That(WakuuTurnEndOrigin.EndedExternally(Wakuu, Round), Is.False);
            Assert.That(WakuuTurnEndOrigin.EndedByMod(Wakuu, Round), Is.False);
        });
    }
}
