using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 目标选择纯函数测试（§18.2.6「目标选择升级」，2026-09-20）。
///
/// 要钉死的语义：**可击杀优先 → 否则有效血量最低（集火）→ 并列取最左**。
/// 旧行为是"第一个可打敌人"，会让伤害平摊、谁也打不死（§21.4.2 #6）。
/// </summary>
[TestFixture]
public class WakuuTargetPickingTests
{
    [Test]
    public void 空敌人列表_返回负一()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuTargetPicking.PickEnemyIndex(new List<int>(), 10), Is.EqualTo(-1));
            Assert.That(WakuuTargetPicking.PickEnemyIndex(null!, 10), Is.EqualTo(-1));
        });
    }

    [Test]
    public void 能击杀时优先可击杀的敌人_按战斗顺序取最左()
    {
        // 有效血量：30 / 5 / 40；伤害 10 ⇒ 只有第 2 个（下标 1）能一次打死
        List<int> effective = new() { 30, 5, 40 };

        Assert.That(WakuuTargetPicking.PickEnemyIndex(effective, 10), Is.EqualTo(1));
    }

    [Test]
    public void 多个都能击杀时_取战斗顺序最靠前的那个()
    {
        List<int> effective = new() { 40, 8, 3 };

        Assert.That(WakuuTargetPicking.PickEnemyIndex(effective, 10), Is.EqualTo(1));
    }

    [Test]
    public void 都打不死时_集火有效血量最低的敌人()
    {
        List<int> effective = new() { 30, 12, 40 };

        Assert.That(WakuuTargetPicking.PickEnemyIndex(effective, 5), Is.EqualTo(1));
    }

    [Test]
    public void 有效血量并列时_取最左()
    {
        List<int> effective = new() { 20, 20, 35 };

        Assert.That(WakuuTargetPicking.PickEnemyIndex(effective, 0), Is.EqualTo(0));
    }

    [Test]
    public void 单个敌人_恒返回零()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuTargetPicking.PickEnemyIndex(new List<int> { 99 }, 1), Is.EqualTo(0));
            Assert.That(WakuuTargetPicking.PickEnemyIndex(new List<int> { 0 }, 0), Is.EqualTo(0));
        });
    }

    [Test]
    public void 有效血量为零的敌人_视同可击杀()
    {
        // 有效血量 0（血量已被格挡吃光）⇒ 任何伤害都能收掉，优先于点它
        List<int> effective = new() { 25, 0 };

        Assert.That(WakuuTargetPicking.PickEnemyIndex(effective, 0), Is.EqualTo(1));
    }

    [Test]
    public void 伤害为零且全满血_仍返回最左而非负一()
    {
        List<int> effective = new() { 10, 20 };

        Assert.That(WakuuTargetPicking.PickEnemyIndex(effective, 0), Is.EqualTo(0));
    }
}
