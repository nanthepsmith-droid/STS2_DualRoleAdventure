using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 知识层只读抽样器（实施草案 §7「M1 不做决策接入；出牌/奖励处打只读抽取统计日志」）。
///
/// 定位：**纯观测**。它只回答"瓦库现在看到的牌，知识层读懂了哪些、读不懂哪些"，
/// 把结果写成 `[瓦库评价]` 锚点日志；**不参与任何决策**，也不写游戏状态。
///
/// 两条设计取舍（都是为了不变成日志洪水，同时保住可排查性）：
/// 1. **读不懂的卡按 id 去重上报**（每张卡最多一条，总量封顶 <see cref="MaxReportedUnknownCards"/> 条）——
///    "第一次见到这张读不懂的卡"才是需要人看的信号，重复上报只会淹没日志；
/// 2. **累计行按固定步长输出**（每 <see cref="SummaryStep"/> 次抽样一条）——
///    用来回答"这一局知识层整体覆盖得怎么样"（= v2 §67 的 `UnknownCardRate` 雏形）。
/// </summary>
internal static class WakuuKnowledgeSampler
{
    /// <summary>累计行步长：每抽样 N 张打一条汇总。</summary>
    public const int SummaryStep = 40;

    /// <summary>读不懂的卡最多上报多少个不同 id（防止一局刷出成百上千条）。</summary>
    public const int MaxReportedUnknownCards = 32;

    private static readonly HashSet<string> ReportedUnknownIds = new(StringComparer.OrdinalIgnoreCase);

    private static int _sampleCount;

    private static int _unknownCount;

    private static float _confidenceSum;

    private static WakuuPortProfile _totalPorts;

    /// <summary>本轮抽样累计的张数（供单测/诊断读取）。</summary>
    public static int SampleCount => _sampleCount;

    /// <summary>
    /// 对一批牌做只读抽样（通常是"当前可打出的手牌"）。
    /// **永不抛异常**：知识层属于诊断路径，任何问题都不该影响出牌。
    /// </summary>
    public static void ObserveHand(IReadOnlyList<CardModel> cards, Player owner, string source)
    {
        try
        {
            if (cards == null || cards.Count == 0)
            {
                return;
            }

            List<WakuuCardFeature> features = WakuuEffectExtractor.ExtractAll(cards, owner);
            for (int i = 0; i < features.Count; i++)
            {
                Record(features[i], source);
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库知识层抽样异常（已忽略，不影响出牌）: {exception.Message}");
        }
    }

    /// <summary>清空累计（跨局重置；也可由单测调用）。</summary>
    public static void Reset()
    {
        ReportedUnknownIds.Clear();
        _sampleCount = 0;
        _unknownCount = 0;
        _confidenceSum = 0f;
        _totalPorts = WakuuPortProfile.Zero;
    }

    private static void Record(in WakuuCardFeature feature, string source)
    {
        _sampleCount++;
        _confidenceSum += feature.Confidence;
        _totalPorts = _totalPorts.Add(feature.Ports);

        if (feature.HasUnknownEffect)
        {
            _unknownCount++;

            string id = string.IsNullOrEmpty(feature.Id) ? "(未命名)" : feature.Id;
            if (ReportedUnknownIds.Count < MaxReportedUnknownCards && ReportedUnknownIds.Add(id))
            {
                LocalMultiControlLogger.Info(
                    $"[瓦库评价] 知识层抽样：遇到读不懂的卡，按未知潜能保守承接（不判 0）: "
                    + $"source={source}, {feature.DescribeShort()}");
            }
        }

        if (_sampleCount % SummaryStep == 0)
        {
            float averageConfidence = _sampleCount > 0 ? _confidenceSum / _sampleCount : 0f;
            float unknownRate = _sampleCount > 0 ? (float)_unknownCount / _sampleCount : 0f;
            LocalMultiControlLogger.Info(
                $"[瓦库评价] 知识层累计：source={source}, 抽样={_sampleCount} 张, "
                + $"未知={_unknownCount} 张({unknownRate:P0}), 平均置信={WakuuConfidence.Describe(averageConfidence)}, "
                + $"端口合计=[{_totalPorts}]");
        }
    }
}
