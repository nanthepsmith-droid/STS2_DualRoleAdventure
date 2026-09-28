using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「远端选择兜底要回哪种空结果」的纯逻辑测试（BUG-23 方案 A，2026-09-28）。
///
/// 钉住两件事：① 异步方法的栈帧**只露出状态机名**（`&lt;FromSimpleGrid&gt;d__89` + `MoveNext`），
/// 必须还原成（`CardSelectCmd`, `FromSimpleGrid`）；② 映射表要与 `sts2src` 里各调用点后的
/// `As*()` 一致 —— 猜错类型 = 把"卡死"换成 `InvalidOperationException`。
/// </summary>
[TestFixture]
public class PlayerChoiceCallerClassifierTests
{
    private static PlayerChoiceEmptyResultKind Classify(string type, string method)
    {
        return PlayerChoiceCallerClassifier.Classify(type, method);
    }

    [Test]
    public void 卡牌入口_手牌与战斗堆_要战斗卡结果()
    {
        Assert.That(Classify("CardSelectCmd", "FromHand"), Is.EqualTo(PlayerChoiceEmptyResultKind.CombatCard));
        Assert.That(Classify("CardSelectCmd", "FromHandForDiscard"), Is.EqualTo(PlayerChoiceEmptyResultKind.CombatCard));
        Assert.That(Classify("CardSelectCmd", "FromHandForUpgrade"), Is.EqualTo(PlayerChoiceEmptyResultKind.CombatCard));
        Assert.That(Classify("CardSelectCmd", "FromCombatPile"), Is.EqualTo(PlayerChoiceEmptyResultKind.CombatCard));
    }

    [Test]
    public void 卡牌入口_牌组_要牌组卡结果()
    {
        Assert.That(Classify("CardSelectCmd", "FromDeckForUpgrade"), Is.EqualTo(PlayerChoiceEmptyResultKind.DeckCard));
        Assert.That(Classify("CardSelectCmd", "FromDeckForRemoval"), Is.EqualTo(PlayerChoiceEmptyResultKind.DeckCard));
        Assert.That(Classify("CardSelectCmd", "FromDeckForTransformation"), Is.EqualTo(PlayerChoiceEmptyResultKind.DeckCard));
        Assert.That(Classify("CardSelectCmd", "FromDeckGeneric"), Is.EqualTo(PlayerChoiceEmptyResultKind.DeckCard));
    }

    [Test]
    public void 卡牌入口_网格与三选一_要索引结果()
    {
        Assert.That(Classify("CardSelectCmd", "FromSimpleGrid"), Is.EqualTo(PlayerChoiceEmptyResultKind.Index));
        Assert.That(Classify("CardSelectCmd", "FromSimpleGridForRewards"), Is.EqualTo(PlayerChoiceEmptyResultKind.Index));
        Assert.That(Classify("CardSelectCmd", "FromChooseACardScreen"), Is.EqualTo(PlayerChoiceEmptyResultKind.Index));
    }

    [Test]
    public void 遗物三选一与卡牌奖励_要索引结果()
    {
        Assert.That(Classify("RelicSelectCmd", "FromChooseARelicScreen"), Is.EqualTo(PlayerChoiceEmptyResultKind.Index));
        Assert.That(Classify("CardReward", "OnSelect"), Is.EqualTo(PlayerChoiceEmptyResultKind.Index));
    }

    [Test]
    public void 火堆指定队友_要玩家结果()
    {
        Assert.That(Classify("MendRestSiteOption", "OnSelect"), Is.EqualTo(PlayerChoiceEmptyResultKind.Player));
    }

    [Test]
    public void 第三方自绘选牌_默认按索引兜底()
    {
        // 实例：沙耶 mod 的色素细胞 → CommonActions.SelectCenteredBranchCards（读 AsIndexes()）
        Assert.That(Classify("CommonActions", "SelectCenteredBranchCards"), Is.EqualTo(PlayerChoiceEmptyResultKind.Index));
        Assert.That(Classify(PlayerChoiceCallerClassifier.UnknownType, PlayerChoiceCallerClassifier.UnknownMethod),
            Is.EqualTo(PlayerChoiceEmptyResultKind.Index));
    }

    [Test]
    public void 状态机帧_还原成外层类型与方法名()
    {
        bool ok = PlayerChoiceCallerClassifier.TryNormalizeFrame(
            "<FromSimpleGrid>d__89", "CardSelectCmd", "MoveNext", out string type, out string method);

        Assert.That(ok, Is.True);
        Assert.That(type, Is.EqualTo("CardSelectCmd"));
        Assert.That(method, Is.EqualTo("FromSimpleGrid"));
    }

    [Test]
    public void 顶层状态机帧_没有外层类型时保留自身名字()
    {
        bool ok = PlayerChoiceCallerClassifier.TryNormalizeFrame(
            "<SelectCenteredBranchCards>d__31", null, "MoveNext", out string type, out string method);

        Assert.That(ok, Is.True);
        Assert.That(type, Is.EqualTo("<SelectCenteredBranchCards>d__31"));
        Assert.That(method, Is.EqualTo("SelectCenteredBranchCards"));
    }

    [Test]
    public void 普通同步方法帧_原样保留()
    {
        bool ok = PlayerChoiceCallerClassifier.TryNormalizeFrame(
            "CardReward", "Runs", "OnSelect", out string type, out string method);

        Assert.That(ok, Is.True);
        Assert.That(type, Is.EqualTo("CardReward"));
        Assert.That(method, Is.EqualTo("OnSelect"));
    }

    [Test]
    public void 异步机制内部帧_丢弃()
    {
        Assert.That(
            PlayerChoiceCallerClassifier.TryNormalizeFrame("AsyncTaskMethodBuilder", "System.Runtime.CompilerServices", "MoveNext", out _, out _),
            Is.False);
        Assert.That(
            PlayerChoiceCallerClassifier.TryNormalizeFrame(null, null, "MoveNext", out _, out _),
            Is.False);
        Assert.That(
            PlayerChoiceCallerClassifier.TryNormalizeFrame("X", "Y", null, out _, out _),
            Is.False);
    }

    [Test]
    public void 内部帧_同步器与本补丁自身要跳过()
    {
        Assert.That(PlayerChoiceCallerClassifier.IsInternalFrame("PlayerChoiceSynchronizer", "WaitForRemoteChoice"), Is.True);
        Assert.That(PlayerChoiceCallerClassifier.IsInternalFrame("PlayerChoiceSynchronizer", "SyncLocalChoice"), Is.True);
        Assert.That(PlayerChoiceCallerClassifier.IsInternalFrame(
            PlayerChoiceCallerClassifier.FallbackPatchTypeName, "Prefix"), Is.True);
        Assert.That(PlayerChoiceCallerClassifier.IsInternalFrame("CardSelectCmd", "FromSimpleGrid"), Is.False);
    }
}
