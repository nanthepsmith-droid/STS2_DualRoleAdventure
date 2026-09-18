using System;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 改进-2 / 方案 D 第三步（队列路径出牌加速）：瓦库**走动作队列**的那张牌也跳过卡牌堆演出。
///
/// **问题**：r117 的「瓦库出牌加速」（`fastVakuuPlay`，默认开）是靠给 `CardCmd.AutoPlay` 传
/// <c>skipCardPileVisuals: true</c> 实现的 —— 传参只对 **inline 路径**有效。队列路径（方案 D）由
/// `PlayCardAction.ExecuteAction` 自己以 <c>isAutoPlay: false</c> 调 `CardModel.OnPlayWrapper`，
/// **没有**这个形参可传 ⇒ 实机实测「两个开关都开 = 加速失效」（TODO r122 ③），
/// 队列路径每张牌回到约 1s（多瓦库时直接相加成整回合时长）。
///
/// **修法**：在 `CardModel.OnPlayWrapper` 上打前缀，把形参 <c>skipCardPileVisuals</c> 改成 true。
/// 该形参只影响**演出**、不参与任何数据语义，是游戏**官方为自动出牌场景**留的开关；改成 true 后：
/// <list type="bullet">
///   <item>跳过**收尾固定等待** `CustomScaledWait(0.15f - num, 0.3f - num)`（两种出牌分支都走这里）；</item>
///   <item>跳过**结算堆**的补间 —— `CardPileCmd.RemoveFromCombat` / `CardCmd.Exhaust` /
///         `CardPileCmd.Add(..., resultLocation)` 的堆移动动画（数据照常入堆）。</item>
/// </list>
/// 实测收益约 **0.15~0.3s/张**（<c>isAutoPlay: false</c> 分支本来就不走 0.25~0.35 的前段等待，
/// 所以比 r117 在 inline 路径上的收益小；这是"队列路径本来就更像真人出牌"的必然）。
///
/// **限定范围（三条同时成立才动手）**：
/// <list type="number">
///   <item>开关 <c>fastVakuuPlay</c> 开 且 本地多控生效（与 r117 同口径，见 `WakuuPlaySpeedPolicy`）；</item>
///   <item>牌主人处于【瓦库形态】托管；</item>
///   <item>这次出牌**确实是我们替该瓦库入队的**（`LocalWakuuRelicRuntime.HasPendingQueuePlay`）
///         —— 真人手动替瓦库出牌同样 `isAutoPlay: false`，那种"人点的牌"不加速，观感不倒退。</item>
/// </list>
/// 关掉 `fastVakuuPlay` 或 `vakuuPlayQueue` ⇒ 前缀恒为 no-op，行为与 r131 完全一致。
///
/// ⚠ 前缀里的 `Owner` getter 会走 `AbstractModel.AssertMutable()`（非可变模型会抛
/// `CanonicalModelException`）。正常出牌期间牌必为可变，但补丁**绝不允许**因为自己抛异常而打断出牌，
/// 所以整体包一层 try-catch：异常只打 WARN 并原样放过（保持原版演出）。
/// </summary>
[HarmonyPatch(typeof(CardModel), nameof(CardModel.OnPlayWrapper))]
internal static class CardPlayVisualsSkipPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardModel __instance, ref bool skipCardPileVisuals)
    {
        try
        {
            // 已经是要跳过（inline AutoPlay 路径 r117 已传 true）⇒ 无需重复判定。
            if (skipCardPileVisuals)
            {
                return;
            }

            if (!LocalSelfCoopContext.IsEnabled)
            {
                return;
            }

            Player? owner = __instance.Owner;
            if (owner == null || !LocalWakuuRelicRuntime.IsVakuuFormMode(owner))
            {
                return;
            }

            if (!WakuuPlaySpeedPolicy.ShouldSkipCardPileVisualsForQueuedPlay(
                    toggleEnabled: LocalWakuuAutopilotConfig.FastWakuuPlay,
                    localMultiControlEnabled: LocalSelfCoopContext.IsEnabled,
                    isVakuuFormPlayer: true,
                    isWakuuQueuedPlay: LocalWakuuRelicRuntime.HasPendingQueuePlay(owner.NetId)))
            {
                return;
            }

            skipCardPileVisuals = true;
            LocalMultiControlLogger.Info(
                $"瓦库出牌加速（队列路径）：强制跳过卡牌堆演出: owner={owner.NetId}, card={__instance.Id.Entry}");
        }
        catch (Exception exception)
        {
            // 补丁自身的失败绝不允许打断出牌：只记录一次，按原版演出继续。
            LocalMultiControlLogger.Warn($"队列路径出牌加速判定失败（按原版演出继续）: {exception.Message}");
        }
    }
}
