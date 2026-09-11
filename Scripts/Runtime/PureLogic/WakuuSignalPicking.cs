using System;
using System.Collections.Generic;
using System.Text;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 卡牌社区统计信号（SkadaHelper CardStats 的镜像快照）。
/// 刻意不引用第三方类型，纯数据可单测；所有比率统一为 0~1（量纲归一由适配器负责）。
/// </summary>
internal readonly struct WakuuCardSignal
{
    public WakuuCardSignal(string cardId, double pickRate, double winRateHeld, double winRateSkipped, long offerCount)
    {
        CardId = cardId ?? string.Empty;
        PickRate = pickRate;
        WinRateHeld = winRateHeld;
        WinRateSkipped = winRateSkipped;
        OfferCount = offerCount;
    }

    /// <summary>卡牌 id（ModelId.Entry，裸 id 不含升级后缀）。</summary>
    public string CardId { get; }

    /// <summary>该卡被提供时的选取率（0~1）：社区"别人多选什么"的直接信号。</summary>
    public double PickRate { get; }

    /// <summary>拿了这张牌的那批局的胜率（0~1）。</summary>
    public double WinRateHeld { get; }

    /// <summary>没拿这张牌的那批局的胜率（0~1）。</summary>
    public double WinRateSkipped { get; }

    /// <summary>该卡被提供的样本局数；低于阈值视为无数据。</summary>
    public long OfferCount { get; }

    /// <summary>因果增益近似：拿了之后的胜率 − 没拿时的胜率。差值越大越可能是好牌。</summary>
    public double WinRateGain => WinRateHeld - WinRateSkipped;
}

/// <summary>
/// 事件选项统计信号（两种数据源的统一镜像快照）：
/// - **个人记录器**（三级决策链第①级，`personal_stats.json`）：提供**选择率** + 胜率 + 「没选它的局」胜率；
/// - **社区统计 SkadaHelper**：只提供选项文本（用于模糊匹配）+ 选了它的局胜率。
/// 刻意不引用第三方类型，纯数据可单测；所有比率统一为 0~1。
/// </summary>
internal readonly struct WakuuEventSignal
{
    public WakuuEventSignal(
        string text,
        double winRate,
        long count,
        double? chosenRate = null,
        double? winRateSkipped = null)
    {
        Text = text ?? string.Empty;
        WinRate = winRate;
        Count = count;
        ChosenRate = chosenRate;
        WinRateSkipped = winRateSkipped;
    }

    /// <summary>
    /// 匹配/标识用文本：社区链是数据集里的选项文本（英文，用于与本地化文本做模糊匹配）；
    /// 个人链是选项的稳定 loc key（<c>EventOption.TextKey</c>，**不受界面语言影响**）。
    /// </summary>
    public string Text { get; }

    /// <summary>选了该选项的那批局的胜率（0~1）。</summary>
    public double WinRate { get; }

    /// <summary>展示样本数：个人链为"被展示次数"，社区链为"样本局数"；低于阈值视为无数据。</summary>
    public long Count { get; }

    /// <summary>
    /// **选择率**（0~1）：该选项被展示时被选中的比例（"遇到时我多选哪个"）。
    /// 只有个人记录器能提供；社区链为 null（SkadaHelper 的事件条目没有选择率字段）。
    /// </summary>
    public double? ChosenRate { get; }

    /// <summary>
    /// 展示过该选项、但**整局没选它**的那批局的胜率（0~1）。
    /// 只有个人链能提供；社区链为 null。注意：只有确实存在"没选"的局时才应回填（否则 0% 是假基准）。
    /// </summary>
    public double? WinRateSkipped { get; }

    /// <summary>因果增益近似：选了它的胜率 − 没选它的胜率。任一缺失时为 null（视为无增益信号）。</summary>
    public double? WinRateGain => WinRateSkipped.HasValue ? WinRate - WinRateSkipped.Value : (double?)null;

    /// <summary>该信号是否带选择率（个人记录器链为 true，社区统计链为 false）。</summary>
    public bool HasChosenRate => ChosenRate.HasValue;
}

/// <summary>
/// 社区统计信号决策纯函数（可行性分析 §8.2 / §8.4.1 的第②③级：社区统计 → 兜底策略）。
///
/// 设计原则：本文件不依赖任何游戏类型与第三方类型，全部输入输出都是基础类型，可直接单测。
/// 任何一环"无数据"（未安装、查表 miss、样本量不足）都返回 -1，由调用方回退到既有策略，
/// 保证关闭辅助时行为与本 mod 未接入统计时完全一致。
/// </summary>
internal static class WakuuSignalPicking
{
    /// <summary>卡牌信号生效的最小提供样本数（低于此视为无数据，回退原策略）。</summary>
    public const long DefaultMinOfferCount = 200;

    /// <summary>事件选项信号生效的最小样本数。</summary>
    public const long DefaultMinEventCount = 200;

    /// <summary>因果增益（WinRateHeld − WinRateSkipped）在卡牌评分里的默认权重。</summary>
    public const double DefaultGainWeight = 1.0;

    /// <summary>
    /// 参与竞选所需的最低**加权**因果增益（默认 0）。
    ///
    /// 实机发现的问题：候选里常常只有部分卡"有数据"（其余为 mod 卡或样本量不足），
    /// 此时唯一有数据的那张会自动胜出——哪怕它的信号是负的（拿了之后胜率反而下降）。
    /// 那等于用一个**确切的坏信号**去覆盖"领最左"这类中性默认，比不查表更糟。
    /// 因此加权因果增益低于本门槛的候选直接出局、不参与竞选；
    /// 若全部出局则返回 -1（回退默认策略）。定位是"不犯低级错误"而非求最优（§9）。
    ///
    /// 作用在**加权后**的增益上，因此 gainWeight=0 时增益恒为 0、门槛不再剔除任何候选，
    /// 决策退化为纯 PickRate 排序，与该参数的语义自洽。
    /// </summary>
    public const double DefaultMinGain = 0.0;

    /// <summary>模糊匹配的最小可比长度：短于此的文本不参与包含匹配，避免短串误命中。</summary>
    private const int MinFuzzyMatchLength = 6;

    /// <summary>
    /// 把胜率/选择率统一到 0~1：不同数据源可能给 0~1 的比例，也可能给 0~100 的百分比。
    /// NaN/Inf 与负值一律归零（视为无信号）。
    /// </summary>
    public static double NormalizeRate(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0)
        {
            return 0.0;
        }

        return value > 1.0 ? value / 100.0 : value;
    }

    /// <summary>
    /// 卡牌候选按社区信号选最优：主信号 PickRate（别人多选什么），
    /// 叠加因果增益 WinRateHeld − WinRateSkipped（选了之后赢没赢）作加权。
    /// 同分保留最左（与"无数据时取最左"的既有方向一致）。
    ///
    /// 返回选中下标；以下情况返回 -1（调用方回退默认策略）：
    /// 全部候选都无数据（null 或样本量不足），或全部有数据的候选都是负面信号（加权增益低于 minGain）。
    /// </summary>
    public static int PickBestCardIndex(
        IReadOnlyList<WakuuCardSignal?> signals,
        long minOfferCount = DefaultMinOfferCount,
        double gainWeight = DefaultGainWeight,
        double minGain = DefaultMinGain)
    {
        if (signals == null || signals.Count == 0)
        {
            return -1;
        }

        int bestIndex = -1;
        double bestScore = 0.0;
        for (int i = 0; i < signals.Count; i++)
        {
            WakuuCardSignal? candidate = signals[i];
            if (candidate == null || candidate.Value.OfferCount < minOfferCount)
            {
                continue;
            }

            WakuuCardSignal signal = candidate.Value;
            double weightedGain = gainWeight * signal.WinRateGain;
            if (weightedGain < minGain)
            {
                continue; // 负面信号：拿了反而更容易输，直接出局，不参与竞选
            }

            double score = signal.PickRate + weightedGain;
            // 严格大于 → 同分时保留最左
            if (bestIndex < 0 || score > bestScore)
            {
                bestIndex = i;
                bestScore = score;
            }
        }

        return bestIndex;
    }

    /// <summary>
    /// 事件选项文本 → 数据集条目下标匹配。归一化后：完全相等优先，其次双向包含；未命中返回 -1。
    /// 与 SkadaHelper 自身渲染统计面板的口径（文本模糊匹配）保持一致。
    /// </summary>
    public static int MatchEventOptionIndex(string? optionText, IReadOnlyList<WakuuEventSignal> stats)
    {
        if (stats == null || stats.Count == 0)
        {
            return -1;
        }

        string normalizedOption = NormalizeMatchText(optionText);
        if (normalizedOption.Length < MinFuzzyMatchLength)
        {
            return -1;
        }

        // 第一遍：完全相等（最强命中）
        for (int i = 0; i < stats.Count; i++)
        {
            string normalizedStat = NormalizeMatchText(stats[i].Text);
            if (normalizedStat.Length >= MinFuzzyMatchLength && normalizedStat == normalizedOption)
            {
                return i;
            }
        }

        // 第二遍：双向包含（弱命中，取第一个）
        for (int i = 0; i < stats.Count; i++)
        {
            string normalizedStat = NormalizeMatchText(stats[i].Text);
            if (normalizedStat.Length < MinFuzzyMatchLength)
            {
                continue;
            }

            if (normalizedOption.Contains(normalizedStat, StringComparison.Ordinal)
                || normalizedStat.Contains(normalizedOption, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 事件选项按**个人统计信号**选最优（改进-3，口径与卡牌 <see cref="PickBestCardIndex"/> 对齐）：
    /// 主信号 = **选择率**（个人链有）否则退化为 **胜率**（社区链）；
    /// 叠加因果增益（选了 vs 没选）作加权，并让**负面信号**（加权增益低于 <paramref name="minGain"/>）直接出局。
    ///
    /// 为什么负面信号要出局（r48 教训，事件同理）：候选里常常只有部分选项"有数据"，
    /// 此时唯一有数据的那个会自动胜出——哪怕它"选了反而更容易输"，
    /// 等于用一个确切的坏信号覆盖"第一个/最后一个"这类中性默认，比不查表更糟。
    ///
    /// 返回选中下标；全部候选无数据（null / 样本不足）或全部出局时返回 -1（调用方回退下一级）。
    /// 同分保留更靠前的选项（与"最上"兜底方向一致）。
    /// </summary>
    /// <param name="signals">与候选一一对应的信号；null 项表示该候选无数据。</param>
    /// <param name="minCount">样本量门槛；默认 0（个人链由 <c>WakuuPersonalQuery</c> 查询侧把关）。</param>
    public static int PickBestEventOptionIndex(
        IReadOnlyList<WakuuEventSignal?> signals,
        long minCount = 0,
        double gainWeight = DefaultGainWeight,
        double minGain = DefaultMinGain)
    {
        if (signals == null || signals.Count == 0)
        {
            return -1;
        }

        int bestIndex = -1;
        double bestScore = 0.0;
        for (int i = 0; i < signals.Count; i++)
        {
            WakuuEventSignal? candidate = signals[i];
            if (candidate == null || candidate.Value.Count < minCount)
            {
                continue;
            }

            WakuuEventSignal signal = candidate.Value;
            double weightedGain = gainWeight * (signal.WinRateGain ?? 0.0);
            if (weightedGain < minGain)
            {
                continue; // 负面信号：选了反而更容易输，出局不参与竞选
            }

            double primary = signal.ChosenRate ?? signal.WinRate;
            double score = primary + weightedGain;
            // 严格大于 → 同分时保留更靠前的选项
            if (bestIndex < 0 || score > bestScore)
            {
                bestIndex = i;
                bestScore = score;
            }
        }

        return bestIndex;
    }

    /// <summary>
    /// 在已匹配的候选中按胜率选最优：样本量不足的条目视为无数据；同胜率保留更靠前的选项。
    /// 返回命中的数据集中标（stats 下标）；全部无数据返回 -1。
    /// </summary>
    public static int PickBestEventIndex(
        IReadOnlyList<int> matchedStatIndexes,
        IReadOnlyList<WakuuEventSignal> stats,
        long minEventCount = DefaultMinEventCount)
    {
        if (matchedStatIndexes == null || stats == null || matchedStatIndexes.Count == 0)
        {
            return -1;
        }

        int bestIndex = -1;
        double bestWinRate = 0.0;
        foreach (int statIndex in matchedStatIndexes)
        {
            if (statIndex < 0 || statIndex >= stats.Count)
            {
                continue;
            }

            WakuuEventSignal signal = stats[statIndex];
            if (signal.Count < minEventCount)
            {
                continue;
            }

            // 严格大于 → 同胜率保留更靠前的选项（与"最上"兜底方向一致）
            if (bestIndex < 0 || signal.WinRate > bestWinRate)
            {
                bestIndex = statIndex;
                bestWinRate = signal.WinRate;
            }
        }

        return bestIndex;
    }

    /// <summary>
    /// 匹配用文本归一化：转小写 + 剔除所有非字母数字字符（标点、空白、BBCode 富文本标记都不参与比较）。
    /// 中日韩文字属于"字母"，因此中文选项文本在英文数据集里不会误命中，只会整体 miss 后回退。
    /// </summary>
    internal static string NormalizeMatchText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        StringBuilder builder = new(text.Length);
        foreach (char c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }
}
