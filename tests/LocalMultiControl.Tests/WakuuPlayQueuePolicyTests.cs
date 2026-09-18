using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「瓦库出牌走动作队列」（改进-2 / 方案 D 实验档）判定纯逻辑测试。
///
/// 三条要钉死的不变式：
/// ① 路径选择（开关 + 本地多控 + 瓦库形态三者齐备才走队列，默认关 ⇒ 行为与既有完全一致）；
/// ② 扣费归属（只有 inline AutoPlay 需要外层先花；队列路径由 PlayCardAction 自己扣，
///    外层再花一次就是双重扣能量）；
/// ③ 目标归一（只有 AnyEnemy / AnyAlly 用大脑解析出的目标，其余一律 null ——
///    PlayCardAction 对"非 Any 的牌 + 非空目标"会判非法并 Cancel）。
/// </summary>
[TestFixture]
public class WakuuPlayQueuePolicyTests
{
    private static WakuuPlayPath Path(bool toggle, bool localMulti, bool isWakuu)
        => WakuuPlayQueuePolicy.DecidePath(toggle, localMulti, isWakuu);

    [Test]
    public void 三条件全满足才走动作队列()
    {
        Assert.That(Path(toggle: true, localMulti: true, isWakuu: true), Is.EqualTo(WakuuPlayPath.ActionQueue));
    }

    [Test]
    public void 开关关闭_走既有内联自动出牌()
    {
        Assert.That(Path(toggle: false, localMulti: true, isWakuu: true), Is.EqualTo(WakuuPlayPath.InlineAutoPlay));
    }

    [Test]
    public void 非本地多控_走既有路径()
    {
        Assert.That(Path(toggle: true, localMulti: false, isWakuu: true), Is.EqualTo(WakuuPlayPath.InlineAutoPlay));
    }

    [Test]
    public void 出牌者不是瓦库形态_走既有路径()
    {
        Assert.That(Path(toggle: true, localMulti: true, isWakuu: false), Is.EqualTo(WakuuPlayPath.InlineAutoPlay));
    }

    [Test]
    public void 真值表_只有一组走队列()
    {
        int queuePaths = 0;
        foreach (bool toggle in new[] { false, true })
        {
            foreach (bool localMulti in new[] { false, true })
            {
                foreach (bool isWakuu in new[] { false, true })
                {
                    if (Path(toggle, localMulti, isWakuu) == WakuuPlayPath.ActionQueue)
                    {
                        queuePaths++;
                        Assert.That(toggle && localMulti && isWakuu, Is.True);
                    }
                }
            }
        }

        Assert.That(queuePaths, Is.EqualTo(1));
    }

    [Test]
    public void 配置默认关闭实验档()
    {
        // 实验档默认关 = 与既有行为完全一致（方案 §12.6：r117 加速后单张已 ~0.2~0.58s，
        // 方案 D 的收益主要是"多人语义"，语义迁移风险由用户自己决定是否尝试验证）。
        Assert.That(new WakuuConfigData().vakuuPlayQueue, Is.False);
    }

    [Test]
    public void 内联路径需要外层先扣费()
    {
        Assert.That(
            WakuuPlayQueuePolicy.NeedsExternalSpendResources(WakuuPlayPath.InlineAutoPlay),
            Is.True);
    }

    [Test]
    public void 队列路径不得再扣费_防双重扣能量()
    {
        // PlayCardAction.ExecuteAction 自己会 await card.SpendResources()；
        // 外层若仍按旧逻辑先花一次，能量会被扣两次（方案 §12.2 ③）。
        Assert.That(
            WakuuPlayQueuePolicy.NeedsExternalSpendResources(WakuuPlayPath.ActionQueue),
            Is.False);
    }

    [Test]
    public void 只有Any目标才用解析出的目标()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuPlayQueuePolicy.ShouldUseResolvedTarget(isAnyTargetCard: true), Is.True);
            // TargetType.None / Self / AllEnemies / AnyPlayer 等一律传 null
            //（原版真人出牌路径同样如此，否则 PlayCardAction 会因 IsValidTarget 判非法而 Cancel）。
            Assert.That(WakuuPlayQueuePolicy.ShouldUseResolvedTarget(isAnyTargetCard: false), Is.False);
        });
    }

    [Test]
    public void 配置默认关闭并发出牌档()
    {
        // 第二步（去掉全局闸门）风险集中在"真重叠"，默认关 = 与 r121~r125 已实机确认的行为完全一致。
        Assert.That(new WakuuConfigData().vakuuPlayOverlap, Is.False);
    }

    [Test]
    public void 真人插队_只撤尚未开始执行的瓦库动作()
    {
        Assert.Multiple(() =>
        {
            // 并发出牌档 + 还在排队（WaitingForExecution）→ 撤。
            // 这是 r126 实机"真人点牌要白等 2~3 张瓦库牌"的修法（r127）。
            Assert.That(
                WakuuPlayQueuePolicy.ShouldCancelPendingPlayForHumanPlay(overlapEnabled: true, isWaitingForExecution: true),
                Is.True);
            // 正在执行 / 正在等选择 → 绝不能撤：前者异步链已在跑（撤了会让 Execute 以非法状态收尾并报 ERROR），
            // 后者会把选牌流程打断。
            Assert.That(
                WakuuPlayQueuePolicy.ShouldCancelPendingPlayForHumanPlay(overlapEnabled: true, isWaitingForExecution: false),
                Is.False);
            // 非并发档（r121~r125）→ 不做插队，保持既有行为（本来就最多 1 张在排队）。
            Assert.That(
                WakuuPlayQueuePolicy.ShouldCancelPendingPlayForHumanPlay(overlapEnabled: false, isWaitingForExecution: true),
                Is.False);
        });
    }

    [Test]
    public void 并发出牌档只在队列路径生效()
    {
        Assert.Multiple(() =>
        {
            // 只有「队列路径 + 开关开」才是并发档（这是"多瓦库真正重叠"的唯一来源）。
            Assert.That(
                WakuuPlayQueuePolicy.IsOverlappingQueuePlay(WakuuPlayPath.ActionQueue, overlapEnabled: true),
                Is.True);
            // 开关关 → 与升级前一致（照旧抢全局闸门）。
            Assert.That(
                WakuuPlayQueuePolicy.IsOverlappingQueuePlay(WakuuPlayPath.ActionQueue, overlapEnabled: false),
                Is.False);
            // inline 路径**永不**并发：它是就地执行、会同步触发选牌链，没有闸门保护会真抢答全局选择器栈。
            Assert.That(
                WakuuPlayQueuePolicy.IsOverlappingQueuePlay(WakuuPlayPath.InlineAutoPlay, overlapEnabled: true),
                Is.False);
            Assert.That(
                WakuuPlayQueuePolicy.IsOverlappingQueuePlay(WakuuPlayPath.InlineAutoPlay, overlapEnabled: false),
                Is.False);
        });
    }
}
