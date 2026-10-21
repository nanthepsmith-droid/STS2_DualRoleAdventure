using System;
using System.Collections.Generic;

namespace HextechRunesLocalCoopFix.Scripts;

/// <summary>
/// 「选项很烂时自主 roll」的**纯逻辑**（v1.6.0，用户 2026-10-11 点名的第三件；无 Godot / 游戏类型依赖 ⇒ 可单测）。
///
/// <para><b>为什么需要</b>：瓦库按抓率挑符文，但如果这一屏三张都是"过去几乎没人选"的烂牌，
/// 挑哪张都亏。海克斯自带 reroll（每张候选各一次，`RerollButton_{slot}`），白给的次数不用白不用。</para>
///
/// <para><b>判据只认抓率</b>：用户实测「海克斯每次给的 3 个选项稀有度一致」⇒ 稀有度不能当判据。
/// 这里把"烂"定义为 <b>有数据（offered &gt; 0）且 Laplace 抓率 ≤ 阈值</b>：
/// 没见过（offered = 0 ⇒ 0.5）属于"未知"，**不判烂** —— 宁可保留一个可能是新好东西的选项，
/// 也不为了赌一把把它换掉。</para>
///
/// <para><b>触发条件（保守）</b>：**三张全烂**才动手（任何一张不烂 ⇒ 直接选它）；
/// 动手时换掉<b>最烂的那一张</b>；该槽已用过 reroll（界面按钮 Disabled / 本屏已试过）就换下一张。</para>
/// </summary>
internal static class RuneRerollPolicy
{
    /// <summary>默认"烂"阈值：Laplace 抓率 ≤ 0.4（例：0/1=0.33、0/2=0.25、1/3=0.40；没数据=0.5 不算烂）。</summary>
    internal const double DefaultPoorThreshold = 0.4;

    /// <summary>这一条统计算不算"烂"（有数据才算；没数据 = 未知，不判烂）。</summary>
    internal static bool IsPoor(RunePickStat? stat, double threshold = DefaultPoorThreshold)
    {
        if (stat == null || stat.Offered <= 0)
        {
            return false;
        }

        return RunePickRatePolicy.Score(stat) <= threshold + 1e-9;
    }

    /// <summary>
    /// 该 reroll 哪一槽（返回下标；<c>-1</c> = 不动）。
    ///
    /// <list type="bullet">
    /// <item>三张全烂才 roll（任何一张不烂 / 没数据 ⇒ 返回 -1，直接选它）；</item>
    /// <item>只换<b>抓率最低、且还能 reroll</b> 的那一槽；全都没次数 ⇒ -1；</item>
    /// <item>参数不一致 / 空列表一律 -1（绝不抛）。</item>
    /// </list>
    /// </summary>
    /// <param name="entries">当前候选的 entry 名。</param>
    /// <param name="lookup">按 entry 查统计（可空 ⇒ 全部当"没数据" ⇒ 永不 roll）。</param>
    /// <param name="canReroll">每一槽现在还能不能 reroll（界面按钮未 Disabled 且本屏没试过）。</param>
    /// <param name="threshold">"烂"阈值。</param>
    internal static int ChooseSlotToReroll(
        IReadOnlyList<string>? entries,
        Func<string, RunePickStat?>? lookup,
        IReadOnlyList<bool>? canReroll,
        double threshold = DefaultPoorThreshold)
    {
        if (entries == null || canReroll == null || entries.Count == 0 || entries.Count != canReroll.Count)
        {
            return -1;
        }

        int worstSlot = -1;
        double worstScore = double.MaxValue;

        for (int i = 0; i < entries.Count; i++)
        {
            RunePickStat? stat = SafeLookup(lookup, entries[i]);
            if (!IsPoor(stat, threshold))
            {
                return -1; // 有一张不烂（或未知）⇒ 直接选它，不赌
            }

            if (!canReroll[i])
            {
                continue;
            }

            double score = RunePickRatePolicy.Score(stat);
            if (score < worstScore - 1e-9)
            {
                worstScore = score;
                worstSlot = i;
            }
        }

        return worstSlot;
    }

    private static RunePickStat? SafeLookup(Func<string, RunePickStat?>? lookup, string entry)
    {
        if (lookup == null)
        {
            return null;
        }

        try
        {
            return lookup(entry);
        }
        catch
        {
            return null; // 读统计失败 ⇒ 当"没数据"（不 roll），绝不打断作答
        }
    }
}
