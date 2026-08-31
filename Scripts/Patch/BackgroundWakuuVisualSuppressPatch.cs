using System.Collections.Generic;
using HarmonyLib;
using Godot;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 后台托管瓦库的牌堆变化视觉对齐（r45）：
/// - 保留瓦库出牌动画（牌从瓦库身侧正常飞入出牌区，观感同多人主机看其他玩家）；
/// - 不保留瓦库弃牌动画（进弃牌堆的牌只淡出释放，不往弃牌堆飞，避免在真人手牌区上方造成
///   「我的牌被丢进弃牌堆」的错觉）。
///
/// 原理：CardPileCmd.GetTweenForCardsChangingPiles 对「非本人（LocalContext.IsMe=false）」的牌
/// 有现成处理——出牌(→Play)正常补间，进弃牌堆(→Discard)走「淡出+释放」不飞行。
/// 瓦库自动出牌期间 LocalContext.NetId 被临时指向瓦库（IsMe=true），弃牌会真飞。
/// 本补丁在该方法执行期间把 NetId 钉回「视角玩家（SessionState.CurrentControlledPlayerId）」，
/// 让瓦库的牌按「非本人」路径处理，自然得到上述效果；视角玩家自己的牌不受影响（IsMe=true 路径不变）。
///
/// ⚠ 不能 return false 跳过原方法：跳过会导致瓦库的牌节点不创建/不释放，卡在场上。
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.GetTweenForCardsChangingPiles), new[]
{
    typeof(IEnumerable<CardPileAddResult>),
    typeof(bool),
})]
internal static class BackgroundWakuuVisualSuppressPatch
{
    /// <summary>重入标记：包装内重入原始实现时为 true，避免无限递归。</summary>
    private static readonly System.Threading.AsyncLocal<bool> _inPinned = new System.Threading.AsyncLocal<bool>();

    [HarmonyPriority(Priority.First)]
    [HarmonyPrefix]
    private static bool Prefix(IEnumerable<CardPileAddResult> results, bool fromSilentAdd, ref (Tween?, bool) __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return true;
        }

        if (results == null || _inPinned.Value)
        {
            return true;
        }

        ulong? viewPlayerId = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId;
        if (!viewPlayerId.HasValue)
        {
            return true;
        }

        bool hasWakuuCard = false;
        foreach (CardPileAddResult result in results)
        {
            if (IsBackgroundWakuuNotInView(result, viewPlayerId.Value))
            {
                hasWakuuCard = true;
                break;
            }
        }

        if (!hasWakuuCard)
        {
            return true;
        }

        // 把 NetId 钉到视角玩家再跑原逻辑：瓦库的牌按「非本人」处理（出牌正常、弃牌淡出）。
        // 同步方法、无 await，重入经 _inPinned 保护。
        ulong? previousNetId = LocalContext.NetId;
        LocalContext.NetId = viewPlayerId.Value;
        _inPinned.Value = true;
        try
        {
            // 重入原实现（_inPinned 保护，不会再次进入本前缀）
            __result = CardPileCmd.GetTweenForCardsChangingPiles(results, fromSilentAdd);
        }
        finally
        {
            _inPinned.Value = false;
            LocalContext.NetId = previousNetId;
        }

        return false;
    }

    /// <summary>
    /// 是否是「后台托管 + 瓦库形态」且「当前视角玩家不是该牌主人」的牌。
    /// 视角玩家用 SessionState.CurrentControlledPlayerId（后台模式下瓦库回合不切视角，仍停在真人）。
    /// </summary>
    private static bool IsBackgroundWakuuNotInView(CardPileAddResult result, ulong viewPlayerId)
    {
        if (!result.success || result.cardAdded == null)
        {
            return false;
        }

        Player? owner = result.cardAdded.Owner;
        if (owner == null || !LocalSelfCoopContext.LocalPlayerIds.Contains(owner.NetId))
        {
            return false;
        }

        if (!LocalWakuuAutopilotConfig.BackgroundMode)
        {
            return false;
        }

        if (!LocalWakuuRelicRuntime.IsVakuuFormModeById(owner.NetId))
        {
            return false;
        }

        return viewPlayerId != owner.NetId;
    }
}
