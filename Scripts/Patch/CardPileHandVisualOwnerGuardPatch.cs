using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 手牌视觉节点归属守卫（修复"酒狐合成牌错误显示进玩家手牌"）。
///
/// 根因（纯显示层，数据层归属一直正确）：
/// CardPileCmd.GetTweenForCardsChangingPiles 用 LocalContext.IsMe(card.Owner) 决定是否为
/// 进手牌的卡创建 NCard 视觉节点，而节点被挂到共享战斗 UI 当前显示的手牌上。
/// 瓦库托管后台模式在看门狗窗口内会临时把 LocalContext 换成瓦库玩家，
/// 此时瓦库的合成产物/抽牌等进手牌事件会被误判为"本人"，视觉节点因此落到
/// 前台玩家的手上；等下次切人重建 UI 时又消失。表现为"合成的牌进了玩家手牌，
/// 但瓦库依旧能正常打出"。
///
/// 修复：多控会话下改用"前台角色"判定——目标牌堆是手牌且卡牌归属者不是前台角色时
/// 跳过节点创建。调用方对 null 结果全部有 null 保护（nCard?.UpdateVisuals / if (nCard != null)），
/// 跳过安全。Play 堆等战斗演出不受影响（targetPileType 限定 Hand 才拦截）。
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), "CreateCardNodeAndUpdateVisuals",
    new[] { typeof(CardModel), typeof(PileType?), typeof(PileType), typeof(bool) })]
internal static class CardPileHandVisualOwnerGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CardModel card, PileType targetPileType, ref NCard __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        if (!(RunManager.Instance.NetService is LocalLoopbackHostGameService))
        {
            return true;
        }

        if ((RunManager.Instance.DebugOnlyGetState()?.Players.Count ?? 0) <= 1)
        {
            return true;
        }

        // 只拦"进手牌"的视觉节点；Play/弃牌堆等演出保持原样
        if (targetPileType != PileType.Hand)
        {
            return true;
        }

        ulong? controlledPlayerId = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId;
        if (controlledPlayerId == null || card.Owner == null || card.Owner.NetId == controlledPlayerId.Value)
        {
            return true;
        }

        __result = null!;
        LocalMultiControlLogger.Info(
            $"已跳过非前台角色的进手牌视觉节点（防串手牌显示）: card={card.Id.Entry}, owner={card.Owner.NetId}, foreground={controlledPlayerId.Value}");
        return false;
    }
}
