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
}
