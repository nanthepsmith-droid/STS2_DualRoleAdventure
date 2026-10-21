using System;

namespace HextechRunesLocalCoopFix.Scripts;

/// <summary>
/// 符文候选「选取率角标」的**纯逻辑**（v1.7.0；无 Godot / 游戏类型依赖 ⇒ 可单测）。
///
/// <para><b>为什么</b>（用户 2026-10-21）：海克斯符文界面上看不到每个选项的选取率，
/// 而瓦库自动作答正是按它挑的 —— 真人看不到就没法判断"它为什么选这个"。
/// 这里只负责"数字怎么显示"，画到卡面上由 <see cref="RunePickRateBadge"/> 做。</para>
///
/// <para><b>口径</b>：显示**原始比例** <c>picked / offered</c>（不是决策用的 Laplace 平滑分）——
/// 真人要看的是"我过去选过它几次"，而不是被先验拉过的分。没有任何记录 ⇒ 显示 <c>—</c>。</para>
/// </summary>
internal static class RuneBadgePolicy
{
    /// <summary>角标档位（用于选颜色）。</summary>
    internal enum Tier
    {
        /// <summary>没记录（offered = 0）。</summary>
        Unknown,

        /// <summary>选取率 &lt; 30%。</summary>
        Low,

        /// <summary>30% ≤ 选取率 &lt; 50%。</summary>
        Mid,

        /// <summary>选取率 ≥ 50%。</summary>
        High,
    }

    /// <summary>角标文本：有记录 = <c>33% (0/1)</c>；没记录 = <c>—</c>。</summary>
    internal static string Format(int offered, int picked)
    {
        if (offered <= 0)
        {
            return "—";
        }

        int safePicked = Math.Clamp(picked, 0, offered);
        int percent = (int)Math.Round(safePicked * 100.0 / offered);
        return $"{percent}% ({safePicked}/{offered})";
    }

    /// <summary>按 <c>picked / offered</c> 判档位（没记录 = Unknown）。</summary>
    internal static Tier Classify(int offered, int picked)
    {
        if (offered <= 0)
        {
            return Tier.Unknown;
        }

        int safePicked = Math.Clamp(picked, 0, offered);
        double rate = (double)safePicked / offered;
        if (rate >= 0.5)
        {
            return Tier.High;
        }

        return rate >= 0.3 ? Tier.Mid : Tier.Low;
    }
}
