using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 修复「手牌变换后 UI 与实际不同步」（铁甲战士用效果变化所有手牌时偶发看起来没生效）。
///
/// 根因：CardCmd.Transform 的视觉门 `if (!LocalContext.IsMine(cardAdded2)) continue;` 用全局
/// LocalContext.NetId 判断「牌是否属于本地玩家」。本地双角色下 NetId 会在两个本地角色间切换，
/// 当变换发生的瞬间 NetId 不是牌主人时（实测 prevNetId=527、owner=526），该视觉分支被跳过：
/// 数据层已把新牌加入手牌堆，但手牌 UI 的卡牌节点没替换 → 看起来没生效，
/// 切角色重建手牌 UI 后才正常。
///
/// 修复（r42 重写）：执行 CardCmd.Transform 期间把 LocalContext.NetId 钉到变换牌的主人，结束后恢复。
/// ⚠ 必须用 void 前缀 + postfix 包装恢复，**不能 return false 跳过原方法**——
/// 若跳过原方法，其它 mod（如 RitsuLib CardCmdTransformPatch）在本方法上的 Prefix 也不会执行，
/// 其 __state 保持 null、Postfix 访问 __state.Snapshots 会 NRE，导致出牌动作失败、
/// 牌卡在屏幕中间不进弃牌堆（实测 PlayCardAction PRIMAL_FORCE 卡死）。
/// </summary>
[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Transform), new[]
{
    typeof(IEnumerable<CardTransformation>),
    typeof(Rng),
    typeof(CardPreviewStyle),
})]
internal static class CardTransformNetIdPinPatch
{
    /// <summary>钉住前的 NetId（当前异步链）。</summary>
    private static readonly System.Threading.AsyncLocal<ulong?> _previousNetId = new System.Threading.AsyncLocal<ulong?>();

    /// <summary>当前异步链是否已钉住 NetId（postfix 据此包装恢复）。</summary>
    private static readonly System.Threading.AsyncLocal<bool> _pinActive = new System.Threading.AsyncLocal<bool>();

    [HarmonyPriority(Priority.First)]
    [HarmonyPrefix]
    private static void Prefix(IEnumerable<CardTransformation> transformations)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return;
        }

        Player? owner = ResolveOwner(transformations);
        if (owner == null || !LocalSelfCoopContext.LocalPlayerIds.Contains(owner.NetId))
        {
            return;
        }

        if (LocalContext.NetId == owner.NetId)
        {
            return; // NetId 已对齐，无需干预
        }

        // 只钉 NetId、不跳过原方法：保证其它 mod 在本方法上的 Prefix/__state 照常执行。
        _previousNetId.Value = LocalContext.NetId;
        LocalContext.NetId = owner.NetId;
        _pinActive.Value = true;

        LocalMultiControlLogger.Info(
            $"[手牌同步修复] 变换期间钉 NetId 到牌主人: owner={owner.NetId}, prevNetId={_previousNetId.Value}");
    }

    [HarmonyPostfix]
    private static void Postfix(ref Task<IEnumerable<CardPileAddResult>> __result)
    {
        if (_pinActive.Value)
        {
            ulong? previous = _previousNetId.Value;
            _pinActive.Value = false;
            _previousNetId.Value = null;

            if (previous.HasValue)
            {
                __result = RestoreNetIdAfterAsync(__result, previous.Value);
            }
        }
    }

    private static async Task<IEnumerable<CardPileAddResult>> RestoreNetIdAfterAsync(
        Task<IEnumerable<CardPileAddResult>> task, ulong previousNetId)
    {
        try
        {
            return await task;
        }
        finally
        {
            LocalContext.NetId = previousNetId;
        }
    }

    /// <summary>取变换组中第一张牌的归属者（同一组变换牌应属同一玩家）。</summary>
    private static Player? ResolveOwner(IEnumerable<CardTransformation>? transformations)
    {
        if (transformations == null)
        {
            return null;
        }

        foreach (CardTransformation transformation in transformations)
        {
            if (transformation.Original?.Owner != null)
            {
                return transformation.Original.Owner;
            }
        }

        return null;
    }
}
