#nullable enable

using System;
using System.Collections.Generic;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// ④「瓦库的爹」【合体】的「本回合你抽牌/弃牌随机走双方的牌堆」两条挂点（r228 建，r230 混弃改为后置）。
///
/// 两条都**只做搬运/改归属，不做判定**（判定在 <see cref="WakuuDaddyCardPolicy"/>、
/// 状态在 <see cref="WakuuDaddyCombatState"/>），并且**只在**该玩家本回合处于合体状态时才介入
/// （<see cref="WakuuDaddyCombatState.TryGetMergedWakuu"/> 为真 ⇒ 一定刚打过【合体】）；
/// 其余情况一律原样放行 ⇒ 对原版/其它 mod 零影响。
///
/// <list type="number">
/// <item><b>混抽</b>（<c>CardPileCmd.Draw</c> 前缀，只搬牌不复写流程）：
///   把「随机来源（自己 / 瓦库的抽牌堆）」顶上那张搬进施牌者抽牌堆**顶部**，
///   随后原版 <c>DrawInternal</c> 原样跑 ⇒ 抽牌历史、<c>ShouldDraw</c> 拦截、手牌上限、
///   演出、没牌时的洗牌全都不受影响（见 <see cref="WakuuDaddyCombatState.InjectMixedDrawSources"/>）。</item>
/// <item><b>混弃</b>（<c>CardCmd.DiscardAndDraw</c> **后置**）：
///   等原版把牌正常弃完（Sly / 钩子 / 后续抽牌全走原版），再把这批牌**搬**到掷硬币选中的那一方弃牌堆
///   （先摘 → 改归属 → 进堆，顺序铁律见 <see cref="WakuuDaddyCombatState.RandomizeDiscardedPile"/>）。
///   ⚠ 不要在弃牌**前**改归属让原版按新 owner 落堆：`CardModel.Pile` 按 Owner 反查，
///   改完归属 `Pile` 变 null ⇒ 原版会跳过摘除 ⇒ 同一张牌挂两个牌堆（r229 实机事故）。</item>
/// </list>
///
/// 前缀不能 await ⇒ 两条都走**同步**的内部方法（<c>RemoveFromCurrentPile</c> /
/// <c>GiveToAnotherPlayer</c> / <c>AddInternal</c>），这也正好保持"静默搬运"（无多余演出）。
/// 任何异常只记一次 WARN 并放行原流程 —— 纯表现/搬运层的问题绝不该反过来打断抽牌或弃牌。
/// </summary>
[HarmonyPatch]
internal static class WakuuDaddyMergePilePatch
{
    private static bool _warned;

    /// <summary>混抽：<c>CardPileCmd.Draw(choiceContext, count, player, fromHandDraw)</c> 前缀（2 参数重载会转发到它）。</summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Draw),
        new[] { typeof(PlayerChoiceContext), typeof(decimal), typeof(Player), typeof(bool) })]
    private static void PrefixDraw(Player player, decimal count)
    {
        try
        {
            WakuuDaddyCombatState.InjectMixedDrawSources(player, count);
        }
        catch (Exception exception)
        {
            WarnOnce("[瓦库的爹] 合体混抽失败（已放行原版抽牌）", exception);
        }
    }

    /// <summary>混弃：<c>CardCmd.DiscardAndDraw(choiceContext, cardsToDiscard, cardsToDraw)</c> **后置**重新安置弃牌堆。</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(CardCmd), nameof(CardCmd.DiscardAndDraw))]
    private static void PostfixDiscardAndDraw(IEnumerable<CardModel> cardsToDiscard)
    {
        try
        {
            WakuuDaddyCombatState.RandomizeDiscardedPile(cardsToDiscard);
        }
        catch (Exception exception)
        {
            WarnOnce("[瓦库的爹] 合体混弃失败（弃牌本身已由原版完成）", exception);
        }
    }

    private static void WarnOnce(string message, Exception exception)
    {
        if (_warned)
        {
            return;
        }

        _warned = true;
        LocalMultiControlLogger.Warn($"{message}: {exception.Message}（后续同类异常不再重复打印）");
    }
}
