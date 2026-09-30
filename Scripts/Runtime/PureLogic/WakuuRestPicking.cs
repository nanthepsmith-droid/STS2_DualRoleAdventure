using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库火堆（休息区）自动选择的纯函数（可行性分析 §9）：
/// 把「全员血量是否都高于某个比例」这类判定抽出来，便于单测且不依赖游戏类型。
/// 血量比例统一用 0~1 的小数表示（0.5 = 50%）。
/// </summary>
internal static class WakuuRestPicking
{
    /// <summary>全员血量比例的判定下限：默认 50%（用户拍板 2026-09-07）。</summary>
    public const decimal DefaultHealthyHpRatio = 0.5m;

    /// <summary>
    /// 所有存活角色的当前血量占上限的比例是否都 ≥ ratio。
    /// 空集合 / 无效上限 → 返回 false（拿不到信息时保持"不满足"，沿用既有行为，不误改优先级）。
    /// </summary>
    public static bool IsAllAboveHpRatio(
        IReadOnlyList<(decimal CurrentHp, decimal MaxHp)>? players,
        decimal ratio = DefaultHealthyHpRatio)
    {
        if (players == null || players.Count == 0)
        {
            return false;
        }

        foreach ((decimal currentHp, decimal maxHp) in players)
        {
            if (maxHp <= 0m)
            {
                continue; // 无效上限（异常/未初始化）忽略，不据此改变优先级
            }

            if (currentHp < maxHp * ratio)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 该休息区选项是否由**游戏本体**提供（判据 = 选项运行期类型所在程序集名 == 游戏程序集名）。
    ///
    /// 为什么要判"来源"：识别"遗物提供的额外选项"旧实现用的是**补集式**判据
    /// （`OptionId != SMITH && != MEND` 就算遗物项），第三方 mod 每加一个休息区选项都会命中该补集 ⇒
    /// 一进房就走"睡觉以外随机"，把「锻造优先」「全员 ≥50% 压制 MEND」两条规则**全部短路**
    /// （2026-09-30 实机 BUG-27，根因链见 `references/local-multicontrol-pitfalls.md` 坑 P）。
    ///
    /// 来源拿不到（null / 空串）时返回 **false = 按"非游戏本体"处理** —— 保守方向：
    /// 来源不明的选项**不该**劫持决策，让它落回正常规则（与 <see cref="IsAllAboveHpRatio"/>
    /// "拿不到信息就不改变优先级"同一取向）。
    /// </summary>
    public static bool IsGameProvidedOptionSource(string? optionAssemblyName, string? gameAssemblyName)
    {
        if (string.IsNullOrEmpty(optionAssemblyName) || string.IsNullOrEmpty(gameAssemblyName))
        {
            return false;
        }

        // 程序集名按规范大小写不敏感（实际只会在 sts2 / 第三方 dll 名之间比）。
        return string.Equals(optionAssemblyName, gameAssemblyName, StringComparison.OrdinalIgnoreCase);
    }
}
