using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 卡面数值折算纯函数测试（r204）。
///
/// 背景（2026-10-03 实机）：猪猪 mod `PIG_MULTI_SHOT`（【猪猪王】变出来的牌）把 `Repeat` 声明成普通
/// `DynamicVar`，而评分大脑/知识层原先用强类型访问器（`DynamicVars.Repeat` = 硬转型 `(RepeatVar)…`）
/// ⇒ InvalidCastException ⇒ **降级路径里再抛一次** ⇒ 冒穿出牌循环（遗物反复闪 + 当回合不出牌）。
/// 折算口径收在本类：读不到变量就按默认值，且**任何输入都不产生负数/异常**。
/// </summary>
[TestFixture]
public class WakuuVarMathTests
{
    [Test]
    public void 段数_没有变量按一段_有则至少一段()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuVarMath.RepeatOrDefault(hasVar: false, rawValue: 0), Is.EqualTo(1));
            Assert.That(WakuuVarMath.RepeatOrDefault(hasVar: false, rawValue: 3), Is.EqualTo(1));
            Assert.That(WakuuVarMath.RepeatOrDefault(hasVar: true, rawValue: 0), Is.EqualTo(1));
            Assert.That(WakuuVarMath.RepeatOrDefault(hasVar: true, rawValue: -5), Is.EqualTo(1));
            Assert.That(WakuuVarMath.RepeatOrDefault(hasVar: true, rawValue: 3), Is.EqualTo(3));
        });
    }

    [Test]
    public void 攻击估算_含力量乘段数_且不为负()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuVarMath.EstimateAttackDamage(enchantedDamage: 6, strength: 2, hits: 3), Is.EqualTo(24));
            Assert.That(WakuuVarMath.EstimateAttackDamage(enchantedDamage: 6, strength: 0, hits: 1), Is.EqualTo(6));
            // 负攻击力（第三方卡/异常数据）不允许变成负分。
            Assert.That(WakuuVarMath.EstimateAttackDamage(enchantedDamage: -9, strength: 0, hits: 1), Is.EqualTo(0));
            // 虚弱类负力量也不能把结果压成负。
            Assert.That(WakuuVarMath.EstimateAttackDamage(enchantedDamage: 3, strength: -9, hits: 2), Is.EqualTo(0));
        });
    }

    [Test]
    public void 格挡估算_含敏捷_且不为负()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuVarMath.EstimateBlockGain(enchantedBlock: 5, dexterity: 3), Is.EqualTo(8));
            Assert.That(WakuuVarMath.EstimateBlockGain(enchantedBlock: 5, dexterity: 0), Is.EqualTo(5));
            Assert.That(WakuuVarMath.EstimateBlockGain(enchantedBlock: 0, dexterity: -4), Is.EqualTo(0));
        });
    }
}
