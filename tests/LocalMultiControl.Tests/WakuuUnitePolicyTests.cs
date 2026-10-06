using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「我们联合」（瓦库四功能之三）的按钮显示判定纯逻辑测试。
///
/// 语义（2026-10-06 拍板）：战斗内 HUD 按钮、**每场战斗一次**、双向复制；
/// 按钮只在「开关开 + 回环单人冒险局 + 战斗进行中 + 本场没用过 + 有候选瓦库 + 自己卡组非空」时显示。
/// </summary>
[TestFixture]
public class WakuuUnitePolicyTests
{
    private static bool ShouldShow(
        bool featureEnabled = true,
        bool coopSessionActive = true,
        bool combatInProgress = true,
        bool alreadyUsedThisCombat = false,
        int candidateCount = 1,
        int actorDeckCount = 10)
    {
        return WakuuUnitePolicy.ShouldShowButton(
            featureEnabled, coopSessionActive, combatInProgress, alreadyUsedThisCombat, candidateCount, actorDeckCount);
    }

    [Test]
    public void 条件齐备_显示按钮()
    {
        Assert.That(ShouldShow(), Is.True);
    }

    [Test]
    public void 开关关闭_不显示()
    {
        Assert.That(ShouldShow(featureEnabled: false), Is.False);
    }

    [Test]
    public void 非本地多控回环局_不显示()
    {
        Assert.That(ShouldShow(coopSessionActive: false), Is.False);
    }

    [Test]
    public void 不在战斗中_不显示()
    {
        Assert.That(ShouldShow(combatInProgress: false), Is.False);
    }

    [Test]
    public void 本场已发动过_不显示()
    {
        Assert.That(ShouldShow(alreadyUsedThisCombat: true), Is.False, "每场战斗一次");
    }

    [Test]
    public void 没有候选瓦库_不显示()
    {
        Assert.That(ShouldShow(candidateCount: 0), Is.False);
    }

    [Test]
    public void 自己卡组为空_不显示()
    {
        Assert.That(ShouldShow(actorDeckCount: 0), Is.False, "步骤①要从自己卡组选一张");
    }

    [Test]
    public void 消耗机会_至少成功一步才算()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuUnitePolicy.ConsumesCombatChance(gaveToVakuu: true, tookFromVakuu: false), Is.True);
            Assert.That(WakuuUnitePolicy.ConsumesCombatChance(gaveToVakuu: false, tookFromVakuu: true), Is.True);
            Assert.That(WakuuUnitePolicy.ConsumesCombatChance(gaveToVakuu: true, tookFromVakuu: true), Is.True);
            Assert.That(WakuuUnitePolicy.ConsumesCombatChance(gaveToVakuu: false, tookFromVakuu: false), Is.False,
                "两步都取消 ⇒ 机会保留（误点不该丢掉整场机会）");
        });
    }
}
