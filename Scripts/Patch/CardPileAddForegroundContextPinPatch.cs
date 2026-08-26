using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 牌堆移动演出的上下文钉扎：CardPileCmd.Add 大重载内部的视觉门
/// （GetTweenForCardsChangingPiles 的 LocalContext.IsMe(owner) 判断）决定
/// 是否为这次移动创建卡牌节点动画。瓦库托管看门狗会把 NetId 临时换成瓦库，
/// 若该窗口横跨真人回合边界，前台玩家自己的手牌→弃牌、抽牌→手牌演出会被
/// 误判为"非本人"而整体跳过——表现为结束回合手牌不进弃堆、下回合看似没抽牌，
/// 切人重建 UI 后才恢复真实样子。
///
/// 修复：在 Add 执行期间把 LocalContext.NetId 钉到当前前台角色（执行完恢复），
/// 使所有视觉门按前台判定。LocalContext.NetId 是普通静态属性；本补丁依赖
/// 视觉门在原方法首个 await 前同步执行的现状（实测路径成立）。
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.Add), new[]
{
    typeof(IEnumerable<CardModel>),
    typeof(CardPile),
    typeof(CardPilePosition),
    typeof(AbstractModel),
    typeof(bool),
    typeof(bool),
})]
internal static class CardPileAddForegroundContextPinPatch
{
    [HarmonyPrefix]
    private static void Prefix(out ulong? __state)
    {
        __state = null;
        if (!LocalSelfCoopContext.IsEnabled
            || !LocalSelfCoopContext.UseSingleAdventureMode
            || !RunManager.Instance.IsInProgress)
        {
            return;
        }

        ulong? controlledPlayerId = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId;
        if (controlledPlayerId == null || LocalContext.NetId == controlledPlayerId.Value)
        {
            return;
        }

        __state = LocalContext.NetId;
        LocalContext.NetId = controlledPlayerId.Value;
    }

    [HarmonyFinalizer]
    private static void Finalizer(ulong? __state)
    {
        if (__state != null)
        {
            LocalContext.NetId = __state;
        }
    }
}
