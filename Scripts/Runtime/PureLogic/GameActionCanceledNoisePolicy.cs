#nullable enable
using System;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「出牌动作在 Canceled 状态下跑完」这条原版 ERROR 的**判定与解析**（纯逻辑，r404）。
///
/// <para>只认两个条件同时成立：消息里是 <c>PlayCardAction</c>，且带那条精确措辞
/// <c>finished execution, but was in state Canceled</c>。药水 / 回合结束等其它动作类型
/// 的同类告警**不匹配**（那可能是我们没适配的地方，必须留着）。</para>
///
/// <para>为什么能压：见 <c>GameActionCanceledNoiseFilterPatch</c> —— 瓦库排队出牌期间目标阵亡时，
/// 原版自己 <c>Cancel()</c> 是**正确行为**（牌留手牌），这条只是通用告警。</para>
/// </summary>
internal static class GameActionCanceledNoisePolicy
{
    /// <summary>原版措辞（`GameAction.Execute` 的 finally 里那句）。</summary>
    internal const string CanceledSuffix = "finished execution, but was in state Canceled";

    /// <summary>只压「出牌动作」这一类。</summary>
    internal const string ActionMarker = "PlayCardAction";

    /// <summary>是否属于"可以降级为 INFO"的那条噪声（调用方还要再叠本地多控会话判据）。</summary>
    internal static bool ShouldFilter(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return text.Contains(ActionMarker, StringComparison.Ordinal)
            && text.Contains(CanceledSuffix, StringComparison.Ordinal);
    }

    /// <summary>从 `GameAction PlayCardAction card: CARD.X (id) index: …` 里抠出 <c>CARD.X</c>；抠不到返回 "?"。</summary>
    internal static string ExtractCardEntry(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "?";
        }

        const string Marker = "card: ";
        int start = text.IndexOf(Marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return "?";
        }

        start += Marker.Length;
        int end = text.IndexOf(' ', start);
        string card = end > start ? text[start..end] : text[start..];
        return card.Length == 0 ? "?" : card;
    }
}
