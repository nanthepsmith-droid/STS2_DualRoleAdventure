using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 战后奖励汇总的纯判定测试（R1b：从 `CombatRoomOfferRoomEndRewardsPatch` 抽出）。
/// 钉死四件事：① 是否发奖励；② 哪些席位进合并集、哪些交回原版（第三方席位不能混进来 ——
/// 2026-09-25 实机教训：混进来会出现「[未知角色]」的奖励且 CB 侧零日志）；
/// ③ 用谁当奖励界面展示者；④ 跨角色卡组取哪些席位的卡池。
/// </summary>
[TestFixture]
public class CombatRewardMergePolicyTests
{
    [Test]
    public void 没有遭遇_照常发奖励()
    {
        Assert.That(CombatRewardMergePolicy.ShouldGiveRewards(null), Is.True);
    }

    [Test]
    public void 遭遇明确发奖励_发()
    {
        Assert.That(CombatRewardMergePolicy.ShouldGiveRewards(true), Is.True);
    }

    [Test]
    public void 遭遇明确不发奖励_不发()
    {
        Assert.That(CombatRewardMergePolicy.ShouldGiveRewards(false), Is.False);
    }

    [Test]
    public void 本地席位存活_进合并集()
    {
        Assert.That(
            CombatRewardMergePolicy.ShouldIncludeInMergedSet(
                isDead: false, isLocalSessionSeat: true, isTemporarySeat: false),
            Is.True);
    }

    [Test]
    public void 本地席位已阵亡_不进合并集()
    {
        Assert.That(
            CombatRewardMergePolicy.ShouldIncludeInMergedSet(
                isDead: true, isLocalSessionSeat: true, isTemporarySeat: false),
            Is.False);
    }

    [Test]
    public void 第三方席位_不进合并集_交回原版()
    {
        Assert.That(
            CombatRewardMergePolicy.ShouldIncludeInMergedSet(
                isDead: false, isLocalSessionSeat: false, isTemporarySeat: false),
            Is.False);
        Assert.That(
            CombatRewardMergePolicy.ShouldOfferBackToVanilla(
                isDead: false, isLocalSessionSeat: false, isTemporarySeat: false),
            Is.True);
    }

    [Test]
    public void 本地席位_不交回原版()
    {
        Assert.That(
            CombatRewardMergePolicy.ShouldOfferBackToVanilla(
                isDead: false, isLocalSessionSeat: true, isTemporarySeat: false),
            Is.False);
    }

    [Test]
    public void 第三方席位已阵亡_不交回原版()
    {
        Assert.That(
            CombatRewardMergePolicy.ShouldOfferBackToVanilla(
                isDead: true, isLocalSessionSeat: false, isTemporarySeat: false),
            Is.False);
    }

    // ---- 战灵召唤 R12：临时席位（战神）永远不参与战斗奖励 ----
    // 三种"看起来像"的组合都必须是 false：临时席位的 IsLocalSeat 为 false（它不在本地席位表里）
    // ⇒ 不显式排除的话它会走进「交回原版流程」分支，在真人奖励界面上变成「[未知角色]」的奖励。

    [Test]
    public void 临时席位_永不进合并集()
    {
        Assert.That(
            CombatRewardMergePolicy.ShouldIncludeInMergedSet(
                isDead: false, isLocalSessionSeat: true, isTemporarySeat: true),
            Is.False);
        Assert.That(
            CombatRewardMergePolicy.ShouldIncludeInMergedSet(
                isDead: false, isLocalSessionSeat: false, isTemporarySeat: true),
            Is.False);
    }

    [Test]
    public void 临时席位_永不交回原版流程()
    {
        // ⚠ 这条是 R12 的核心：默认（isLocalSessionSeat=false）它**本来会**被交回原版。
        Assert.That(
            CombatRewardMergePolicy.ShouldOfferBackToVanilla(
                isDead: false, isLocalSessionSeat: false, isTemporarySeat: false),
            Is.True);
        Assert.That(
            CombatRewardMergePolicy.ShouldOfferBackToVanilla(
                isDead: false, isLocalSessionSeat: false, isTemporarySeat: true),
            Is.False);
        Assert.That(
            CombatRewardMergePolicy.ShouldOfferBackToVanilla(
                isDead: false, isLocalSessionSeat: true, isTemporarySeat: true),
            Is.False);
    }

    [Test]
    public void 奖励排除闸门_只认临时席位_不动真人()
    {
        Assert.That(CombatRewardMergePolicy.ShouldExcludeFromRewards(isTemporarySeat: false), Is.False);
        Assert.That(CombatRewardMergePolicy.ShouldExcludeFromRewards(isTemporarySeat: true), Is.True);
    }

    [Test]
    public void 展示者_取第一个存活者()
    {
        List<bool> isDead = new() { true, true, false, false };
        Assert.That(CombatRewardMergePolicy.SelectDisplayPlayerIndex(isDead), Is.EqualTo(2));
    }

    [Test]
    public void 展示者_全死取第一个()
    {
        List<bool> isDead = new() { true, true };
        Assert.That(CombatRewardMergePolicy.SelectDisplayPlayerIndex(isDead), Is.EqualTo(0));
    }

    [Test]
    public void 展示者_空列表返回负一()
    {
        Assert.That(CombatRewardMergePolicy.SelectDisplayPlayerIndex(new List<bool>()), Is.EqualTo(-1));
    }

    [Test]
    public void 跨角色卡池_排除自己_排除阵亡_排除第三方_排除无卡池()
    {
        // 自己 / 阵亡的本地席位 / 存活的第三方席位 / 无角色的本地席位 / 两个合格的本地席位
        List<(bool, bool, bool, bool)> candidates = new()
        {
            (true, false, true, true),
            (false, true, true, true),
            (false, false, false, true),
            (false, false, true, false),
            (false, false, true, true),
            (false, false, true, true),
        };

        Assert.That(
            CombatRewardMergePolicy.SelectCrossCharacterPoolCandidateIndices(candidates),
            Is.EqualTo(new[] { 4, 5 }));
    }

    [Test]
    public void 跨角色卡池_保持入参顺序()
    {
        List<(bool, bool, bool, bool)> candidates = new()
        {
            (false, false, true, true),
            (true, false, true, true),
            (false, false, true, true),
        };

        Assert.That(
            CombatRewardMergePolicy.SelectCrossCharacterPoolCandidateIndices(candidates),
            Is.EqualTo(new[] { 0, 2 }));
    }

    [Test]
    public void 跨角色卡池_没有合格候选_返回空()
    {
        List<(bool, bool, bool, bool)> candidates = new() { (true, false, true, true) };
        Assert.That(CombatRewardMergePolicy.SelectCrossCharacterPoolCandidateIndices(candidates), Is.Empty);
    }
}
