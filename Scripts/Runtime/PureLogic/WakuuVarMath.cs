using System;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 卡面数值折算的纯函数（r204）：把"从卡上读到的原始值"折成估计值。
///
/// 抽出来的理由：读值的动作（<see cref="WakuuCardVarReader"/>）必须碰游戏类型，
/// 而折算口径（段数下限、力量/敏捷加成、不为负）是纯算术 —— 放这里就能被单测钉住，
/// 避免"第三方卡的类型不符"再以别的形式把出牌循环炸掉。
/// </summary>
internal static class WakuuVarMath
{
    /// <summary>段数（Repeat）：卡上没有这个变量时按 1 段；有则至少 1 段。</summary>
    public static int RepeatOrDefault(bool hasVar, int rawValue)
    {
        return hasVar ? Math.Max(1, rawValue) : 1;
    }

    /// <summary>攻击粗估伤害 =（卡面伤害含附魔 + 力量）× 段数，不为负。</summary>
    public static int EstimateAttackDamage(int enchantedDamage, int strength, int hits)
    {
        return Math.Max(0, (enchantedDamage + strength) * hits);
    }

    /// <summary>格挡收益 = 卡面格挡（含附魔）+ 敏捷，不为负。</summary>
    public static int EstimateBlockGain(int enchantedBlock, int dexterity)
    {
        return Math.Max(0, enchantedBlock + dexterity);
    }
}
