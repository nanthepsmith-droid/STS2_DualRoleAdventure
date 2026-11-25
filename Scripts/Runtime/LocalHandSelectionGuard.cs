#nullable enable
using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「正在打出的那张牌不许再被当成手牌目标」的运行时判定与候选过滤（r405，2026-11-25 实机）。
///
/// <para><b>症状</b>：多席位并发出牌时，一张牌打出后因为**别的席位的牌还在飞**，它的节点会暂时停在
/// 屏幕中间（出牌队列 / 出牌区）；这段时间里如果它的**数据还在手牌堆**（原版时序本不会，但异常窗口里会），
/// 「变化手牌里的其它牌 / 消耗 / 丢弃」这类选牌就会把**它自己**当成候选。</para>
///
/// <para><b>判据（三条任一命中即算"正在打出"）</b>：
/// ① 就是本席位这次托管入队、还没跑完的那张牌（<c>_pendingQueuePlays</c>）；
/// ② <c>NCombatUi.PlayQueue.GetCardNode</c> 有它（已入队、还没开始执行）；</para>
/// <para>③ <c>NCombatUi.GetCardFromPlayContainer</c> 有它（已开始执行、节点在出牌区）。</para>
///
/// <para>过滤后语义回到原版：**正在打出的牌不属于手牌**。真命中会打一条 WARN 留证
/// （说明数据层把它留在手牌堆这件事确实发生过，供后续追上游）。</para>
/// </summary>
internal static class LocalHandSelectionGuard
{
    /// <summary>该牌此刻是否处于"正在打出"（见类注释三条判据）。异常一律返回 false（宁可不过滤）。</summary>
    internal static bool IsCardMidPlay(CardModel? card, ulong playerNetId)
    {
        if (card == null)
        {
            return false;
        }

        try
        {
            if (ReferenceEquals(card, LocalWakuuRelicRuntime.TryGetHostedPlayingCard(playerNetId)))
            {
                return true;
            }

            NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
            if (combatUi == null)
            {
                return false;
            }

            if (combatUi.PlayQueue.GetCardNode(card) != null)
            {
                return true;
            }

            return combatUi.GetCardFromPlayContainer(card) != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// 从候选里剔掉"正在打出"的牌并留证（同一张牌 + 同一处调用只记一条 WARN）。
    /// </summary>
    internal static List<CardModel> ExcludeMidPlayCards(
        IReadOnlyList<CardModel> candidates,
        Player player,
        string where)
    {
        try
        {
            List<CardModel> kept = WakuuMidPlayCardGuardPolicy.WithoutMidPlayCards(
                candidates,
                card => IsCardMidPlay(card, player.NetId),
                out List<CardModel> removed);

            if (removed.Count > 0)
            {
                foreach (CardModel card in removed)
                {
                    LocalMultiControlLogger.Warn(
                        $"选牌候选里剔除了「正在打出的牌」: player={player.NetId}, card={card.Id.Entry}, "
                        + $"where={where}, 剩余候选={kept.Count}"
                        + "（说明它的数据还留在手牌堆 = 上游出牌时序的异常窗口，见 r405 注释）");
                }
            }

            return kept;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"选牌候选「正在打出的牌」过滤失败（按原候选继续）: {exception.Message}");
            return new List<CardModel>(candidates);
        }
    }
}
