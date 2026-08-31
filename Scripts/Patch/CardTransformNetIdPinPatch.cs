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
/// 当变换发生的瞬间 NetId 不是牌主人时（例如后台瓦库托管与前台角色效果在异步边界交叠），
/// 该视觉分支被跳过：数据层已把新牌加入手牌堆，但手牌 UI 的卡牌节点没替换 → 看起来没生效，
/// 切角色重建手牌 UI 后才正常（与用户实测症状一致）。
///
/// 修复：执行 CardCmd.Transform 期间把 LocalContext.NetId 钉到变换牌的主人，结束后恢复。
/// 与选牌期钉 NetId（NPlayerHandSelectCardsSerializationPatch）同一模式；重入判定用 AsyncLocal。
/// </summary>
[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Transform), new[]
{
    typeof(IEnumerable<CardTransformation>),
    typeof(Rng),
    typeof(CardPreviewStyle),
})]
internal static class CardTransformNetIdPinPatch
{
    /// <summary>重入标记：包装任务重入原始 Transform 期间为 true，避免无限递归。</summary>
    private static readonly System.Threading.AsyncLocal<bool> _inPinned = new System.Threading.AsyncLocal<bool>();

    [HarmonyPriority(Priority.First)]
    [HarmonyPrefix]
    private static bool Prefix(
        IEnumerable<CardTransformation> transformations,
        Rng? rng,
        CardPreviewStyle style,
        ref Task<IEnumerable<CardPileAddResult>> __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return true;
        }

        if (_inPinned.Value)
        {
            return true;
        }

        Player? owner = ResolveOwner(transformations);
        if (owner == null || !LocalSelfCoopContext.LocalPlayerIds.Contains(owner.NetId))
        {
            return true;
        }

        if (LocalContext.NetId == owner.NetId)
        {
            return true; // NetId 已对齐，无需干预
        }

        LocalMultiControlLogger.Info(
            $"[手牌同步修复] 变换期间钉 NetId 到牌主人: owner={owner.NetId}, prevNetId={LocalContext.NetId}, "
            + $"count={transformations?.Count()}");

        // ResolveOwner 返回非空已保证 transformations 非空
        __result = TransformWithPinnedNetIdAsync(transformations!, rng, style, owner.NetId);
        return false;
    }

    private static async Task<IEnumerable<CardPileAddResult>> TransformWithPinnedNetIdAsync(
        IEnumerable<CardTransformation> transformations,
        Rng? rng,
        CardPreviewStyle style,
        ulong ownerId)
    {
        ulong? previousNetId = LocalContext.NetId;
        LocalContext.NetId = ownerId;
        _inPinned.Value = true;
        try
        {
            return await CardCmd.Transform(transformations, rng, style);
        }
        finally
        {
            _inPinned.Value = false;
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
