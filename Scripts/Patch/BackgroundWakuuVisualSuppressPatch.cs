using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Godot;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 修复「瓦库后台托管时，其出牌/弃牌动画出现在人类玩家手牌区上方，看起来像丢了我的手牌」。
///
/// 根因：后台托管模式下 EnsureWakuuPerspective 会「跳过自动切换视角」（日志：瓦库形态后台模式，
/// 跳过自动切换视角），手牌 UI 保持显示真人（Session.CurrentControlledPlayerId）的手牌；
/// 但瓦库自动出牌期间 LocalContext.NetId 已临时指向瓦库（临时 owner 上下文），
/// 于是 CardPileCmd.GetTweenForCardsChangingPiles 的视觉门 LocalContext.IsMe(瓦库的牌) = true，
/// 会在手牌区位置新建瓦库的卡牌节点并播放飞到弃牌堆/出牌区的动画 → 真人看到「自己手牌区有牌飞走」。
///
/// 修复：当某批牌堆变化全部属于「后台托管 + 瓦库形态 + 当前视角不是该瓦库」时，直接跳过
/// GetTweenForCardsChangingPiles 的动画（返回 null,false，数据层不受影响）——后台瓦库的牌
/// 本就不显示在真人视角里，动画纯属误导。
/// </summary>
[HarmonyPatch(typeof(CardPileCmd), nameof(CardPileCmd.GetTweenForCardsChangingPiles), new[]
{
    typeof(IEnumerable<CardPileAddResult>),
    typeof(bool),
})]
internal static class BackgroundWakuuVisualSuppressPatch
{
    [HarmonyPriority(Priority.First)]
    [HarmonyPrefix]
    private static bool Prefix(IEnumerable<CardPileAddResult> results, ref (Tween?, bool) __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return true;
        }

        if (results == null)
        {
            return true;
        }

        bool anySuppressible = false;
        foreach (CardPileAddResult result in results)
        {
            if (ShouldSuppress(result))
            {
                anySuppressible = true;
            }
            else
            {
                return true; // 混入需要显示（真人在看）的牌 → 交回原逻辑
            }
        }

        if (anySuppressible)
        {
            LocalMultiControlLogger.Info("[后台瓦库] 跳过牌堆变化动画（后台托管且当前视角非该瓦库）");
            __result = (null, false);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 是否应压制：牌属于「后台托管 + 瓦库形态」且「当前视角玩家不是该牌主人」。
    /// 视角玩家用 SessionState.CurrentControlledPlayerId（后台模式下瓦库回合不切视角，仍停在真人）。
    /// </summary>
    private static bool ShouldSuppress(CardPileAddResult result)
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

        ulong? viewPlayerId = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId;
        return viewPlayerId.HasValue && viewPlayerId.Value != owner.NetId;
    }
}
