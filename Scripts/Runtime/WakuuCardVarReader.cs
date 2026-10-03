using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 读卡面数值变量的**类型无关**读取器（r204）。
///
/// **为什么必须有它**（2026-10-03 实机，瓦库形态一直闪 + 当回合不出牌）：
/// 游戏 <c>DynamicVarSet</c> 的强类型访问器是**硬转型**，例如
/// <c>public RepeatVar Repeat =&gt; (RepeatVar)_vars["Repeat"];</c>（Damage/Block 同理）。
/// 第三方卡可以把同名变量声明成别的类型 —— 实测猪猪 mod 的 `PIG_MULTI_SHOT`
/// （由【猪猪王】变身产生）把 `Repeat` 声明成普通 <c>DynamicVar</c> ⇒ 读 `DynamicVars.Repeat` 抛
/// <c>InvalidCastException: Unable to cast object of type 'DynamicVar' to type 'RepeatVar'</c>。
/// 后果链（日志实见）：知识层按未知卡兜住 → 评分大脑 catch 后走"降级为最左可打牌" →
/// **降级路径里的 `ResolveTarget → EstimateDamage` 再抛一次**（同一处强类型访问器）⇒ 异常冒穿
/// `TryDecideNext` → 选择器作用域/出牌作用域以异常退出 → 看门狗重启失败 ⇒
/// **遗物 hook 被反复重触发（视觉上"遗物一直闪"）、当回合一张牌也不出**。
///
/// ⇒ 规矩：**读卡面变量一律走本读取器**（只用 <see cref="DynamicVar"/> 基类的
/// <c>IntValue</c> / <c>EnchantedValue</c>，与具体变量类型无关），并且**永不抛异常**。
/// 与 <see cref="WakuuEffectExtractor"/> 顶部那条"不能用属性访问判空"是同一族坑（r111 起就有记录）。
/// </summary>
internal static class WakuuCardVarReader
{
    /// <summary>按 key 读变量的整数值（缺键 / 类型异常 ⇒ false，不抛）。</summary>
    public static bool TryReadInt(CardModel? card, string key, out int value)
    {
        if (TryGetVar(card, key, out DynamicVar? dynamicVar))
        {
            return TryConvert(() => dynamicVar!.IntValue, out value);
        }

        value = 0;
        return false;
    }

    /// <summary>按 key 读变量的"含附魔"整数值（缺键 / 类型异常 ⇒ false，不抛）。</summary>
    public static bool TryReadEnchantedInt(CardModel? card, string key, out int value)
    {
        if (TryGetVar(card, key, out DynamicVar? dynamicVar))
        {
            return TryConvert(() => (int)dynamicVar!.EnchantedValue, out value);
        }

        value = 0;
        return false;
    }

    private static bool TryGetVar(CardModel? card, string key, out DynamicVar? value)
    {
        value = null;
        if (card == null || string.IsNullOrEmpty(key))
        {
            return false;
        }

        try
        {
            // IReadOnlyDictionary<string, DynamicVar> 的 TryGetValue：与具体变量类型无关。
            if (card.DynamicVars.TryGetValue(key, out DynamicVar? found) && found != null)
            {
                value = found;
                return true;
            }
        }
        catch (Exception)
        {
            // 连取变量集合都炸的场景（第三方数据异常）：一律当作"读不到"，绝不让异常外溢。
        }

        return false;
    }

    private static bool TryConvert(Func<int> read, out int value)
    {
        try
        {
            value = read();
            return true;
        }
        catch (Exception)
        {
            value = 0;
            return false;
        }
    }

    /// <summary>便于调用点枚举（只读诊断用）。</summary>
    public static IReadOnlyDictionary<string, DynamicVar>? VarsOrNull(CardModel? card)
    {
        try
        {
            return card?.DynamicVars;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
