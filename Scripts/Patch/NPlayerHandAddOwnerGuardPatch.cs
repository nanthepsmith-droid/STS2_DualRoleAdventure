using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 手牌 UI 加入守卫（修复「回合结束古明地恋用 MiniHakkero 消耗的牌进入蕾忍手牌」）。
///
/// 根因：本地双角色下 527 的 MiniHakkero 选牌（NPlayerHand.SelectCards 等待玩家选择）期间，
/// 526 的 MiniHakkero Exhaust 触发枯木树枝等 CardPileCmd.Add，这些牌的 NCard 节点经
/// NPlayerHand.Add 被加入手牌 UI 的 holder；而 SelectCards 的选牌界面就基于该 holder，
/// 导致 527 的选牌界面出现 526 手牌里的牌，玩家能选中它们。
///
/// 修复：选牌进行中（NPlayerHandSelectCardsSerializationPatch.CurrentSelectionOwnerId 非空）时，
/// 拦截「owner 不是本次选牌玩家」的卡牌节点加入手牌 UI（跳过节点创建，数据层不受影响）。
/// </summary>
[HarmonyPatch(typeof(NPlayerHand), nameof(NPlayerHand.Add), new[] { typeof(NCard), typeof(int) })]
internal static class NPlayerHandAddOwnerGuardPatch
{
    [HarmonyPriority(Priority.First)]
    [HarmonyPrefix]
    private static bool Prefix(NPlayerHand __instance, NCard card, ref NHandCardHolder __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return true;
        }

        ulong? selectionOwner = NPlayerHandSelectCardsSerializationPatch.CurrentSelectionOwnerId;
        if (selectionOwner == null)
        {
            return true; // 无选牌进行中，不拦截
        }

        CardModel? model = card?.Model;
        if (model == null || model.Owner == null)
        {
            return true;
        }

        if (model.Owner.NetId == selectionOwner.Value)
        {
            return true; // 选牌 owner 自己的牌正常加入
        }

        // 选牌期间，非选牌 owner 的牌加入手牌 UI → 跳过节点创建（数据层仍正常移动）
        LocalMultiControlLogger.Warn(
            $"[选牌守卫] 拦截非选牌owner的进手牌节点: card={model.Id.Entry}, owner={model.Owner.NetId}, selectionOwner={selectionOwner.Value}");
        __result = null!;
        return false;
    }
}
