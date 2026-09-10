using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 遗物「获得时触发选牌」自动作答判定（r94）纯逻辑测试：
/// 只有「本地多控 + 单人冒险模式 + 该玩家处于瓦库托管 + 非战斗期」才自动作答，
/// 战斗期一律交还原流程（避开开局遗物二选一等依赖真人处理的场景）。
/// </summary>
[TestFixture]
public class WakuuRelicEffectAutoChoiceTests
{
    [Test]
    public void 本地多控且瓦库托管且非战斗时自动作答()
    {
        Assert.That(
            LocalWakuuRelicEffectAutoChoice.ShouldAutoAnswer(
                localSelfCoopEnabled: true, useSingleAdventureMode: true, isVakuuForm: true, combatInProgress: false),
            Is.True);
    }

    [Test]
    public void 非本地多控不自动作答()
    {
        Assert.That(
            LocalWakuuRelicEffectAutoChoice.ShouldAutoAnswer(
                localSelfCoopEnabled: false, useSingleAdventureMode: true, isVakuuForm: true, combatInProgress: false),
            Is.False);
    }

    [Test]
    public void 非单人冒险模式不自动作答()
    {
        Assert.That(
            LocalWakuuRelicEffectAutoChoice.ShouldAutoAnswer(
                localSelfCoopEnabled: true, useSingleAdventureMode: false, isVakuuForm: true, combatInProgress: false),
            Is.False);
    }

    [Test]
    public void 非瓦库托管角色不自动作答()
    {
        // 真人自己拾遗物时必须保留弹屏，交给真人选择
        Assert.That(
            LocalWakuuRelicEffectAutoChoice.ShouldAutoAnswer(
                localSelfCoopEnabled: true, useSingleAdventureMode: true, isVakuuForm: false, combatInProgress: false),
            Is.False);
    }

    [Test]
    public void 战斗进行中不自动作答()
    {
        // 战斗期另有瓦库出牌循环的选择器；且开局遗物二选一等依赖真人处理
        Assert.That(
            LocalWakuuRelicEffectAutoChoice.ShouldAutoAnswer(
                localSelfCoopEnabled: true, useSingleAdventureMode: true, isVakuuForm: true, combatInProgress: true),
            Is.False);
    }

    [Test]
    public void 场景解算_入口覆盖优先于兜底()
    {
        // "删除一张卡"类遗物入口（FromDeckForRemoval）应覆盖兜底的 Transform
        Assert.That(
            LocalWakuuRelicEffectAutoChoice.ResolveScenario(WakuuPickScenario.Remove, WakuuPickScenario.Transform),
            Is.EqualTo(WakuuPickScenario.Remove));
    }

    [Test]
    public void 场景解算_无覆盖时用兜底场景()
    {
        // "变化一张卡"（灵草丹等）没有入口覆盖 → 用压栈时的 Transform
        Assert.That(
            LocalWakuuRelicEffectAutoChoice.ResolveScenario(null, WakuuPickScenario.Transform),
            Is.EqualTo(WakuuPickScenario.Transform));
    }

    [Test]
    public void 场景解算_Unknown覆盖表示退回cardPickMode()
    {
        // "升级一张卡"入口无对应优先级表，覆盖为 Unknown = 退回既有策略（不能掉回 Transform 表）
        Assert.That(
            LocalWakuuRelicEffectAutoChoice.ResolveScenario(WakuuPickScenario.Unknown, WakuuPickScenario.Transform),
            Is.EqualTo(WakuuPickScenario.Unknown));
    }
}
