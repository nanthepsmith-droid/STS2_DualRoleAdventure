using System;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 卡牌**节点**的纯表现层回收工具（r263 从 <c>NPlayerHandAddOwnerGuardPatch</c> 提出来共享）。
///
/// 出现两处以上的"要把一个已经没人管的卡面从屏幕上收走"：
/// <list type="bullet">
/// <item>手牌守卫拦截"非前台席位的牌被挂进共享手牌 UI"（幽灵牌）；</item>
/// <item>托管出牌抛异常后回收**滞留在出牌区**的残影（r262 实机「不死之烟」停在屏幕中间）。</item>
/// </list>
/// 一律只动节点、**不动数据层**（牌该在哪个牌堆就在哪个牌堆）。
/// </summary>
internal static class CardNodeVisualCleanup
{
    /// <summary>
    /// 原版对「不是我的牌」的处理（<c>NCardPlayQueue.TweenCardForCancellation</c>：淡出后回收）。
    /// 补间挂在 <c>SceneTree</c> 上（不挂卡牌节点自己）：节点被释放时补间仍会跑完回调，
    /// 否则会出现"淡到一半的节点永远留在场上"。
    /// </summary>
    internal static void FadeOutAndFree(NCard card)
    {
        try
        {
            SceneTree? tree = card.GetTree();
            if (tree == null)
            {
                card.QueueFreeSafely();
                return;
            }

            Tween tween = tree.CreateTween();
            tween.TweenProperty(card, "modulate:a", 0f, 0.35f)
                .SetEase(Tween.EaseType.Out)
                .SetTrans(Tween.TransitionType.Cubic);
            tween.TweenCallback(Callable.From(card.QueueFreeSafely));
            tween.Play();
        }
        catch (Exception)
        {
            // 纯表现层兜底，绝不能反过来影响出牌流程。
            try
            {
                card.QueueFreeSafely();
            }
            catch
            {
                // 节点已释放：无事可做。
            }
        }
    }
}
