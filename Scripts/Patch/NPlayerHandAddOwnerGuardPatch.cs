using System;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 手牌 UI 加入守卫 —— 「别人的牌节点不许进**共享手牌 UI**」。判定收敛在纯逻辑
/// <see cref="HandNodeAdmissionPolicy"/>（本类只做翻译：问 Seat 拿身份 + 按判定结果放行 / 拦截）。
///
/// <list type="number">
/// <item><b>选牌进行中</b>（原有，修复「回合结束古明地恋用 MiniHakkero 消耗的牌进入蕾忍手牌」）：
///   527 的 MiniHakkero 选牌（<c>NPlayerHand.SelectCards</c> 等待玩家选择）期间，526 的
///   MiniHakkero Exhaust 触发枯木树枝等 <c>CardPileCmd.Add</c>，这些牌的 NCard 节点经
///   <c>NPlayerHand.Add</c> 被加进手牌 UI 的 holder，而选牌界面就基于该 holder
///   ⇒ 527 的选牌界面出现 526 手牌里的牌、且能被选中 ⇒ 只允许「本次选牌玩家」的牌进入。</item>
/// <item><b>非选牌期间</b>（2026-10-05 新增，修复「瓦库的幽灵牌跑进真人手牌」）：
///   详见下方「幽灵牌」注释 —— 共享手牌 UI 只显示**受控席位**的手牌，我们其它席位的牌节点一律不进。</item>
/// </list>
///
/// <para>⚠ 两条分支**都必须连节点回收一起做**（`FadeOutAndFree`）：调用方只会"更早"把节点重挂到战斗 UI 上
/// 并按旧坐标摆好，然后才走到 <c>Add</c> —— 只拦不回收 = 卡面**停在屏幕中间不动**
/// （2026-10-05 实机：瓦库打【群情激愤】时正赶上真人那侧在选牌 ⇒ 选牌分支拦下节点但没回收，
/// 一张牌滞留在屏幕中央 —— 见 `references/local-multicontrol-pitfalls.md` 坑 U）。</para>
///
/// <para><b>幽灵牌（2026-10-05 用户实机报）</b>：本地多控下瓦库（后台托管席位）的牌**有时**会出现在
/// 真人手牌里（纯视觉），切一下角色就消失；更糟的是弃牌选牌时能**选中**这些幽灵牌、结算照常
/// （数据层动的是瓦库那张牌）而幽灵节点**不会消失** —— 它对应的牌从来不在真人的手牌堆里，
/// 那条"离开手牌就摘节点"的补间按 `IsMe` 判成"不是我的牌"直接 continue ⇒ 永远留着。</para>
///
/// <para><b>根因</b>：手牌容器是共享的（显示谁的手牌由前台决定），而原版决定"要不要把这张牌挂进手牌"
/// 用的是 <c>LocalContext.IsMe(card.Owner)</c>：</para>
/// <list type="bullet">
/// <item><c>CardPileCmd.GetTweenForCardsChangingPiles</c> 的 `Hand` 分支 → <c>handNode?.Add(cardNode)</c>；</item>
/// <item><c>NCardPlayQueue.RemoveCardFromQueueForCancellation</c> → <c>action.OwnerId == LocalContext.NetId</c>
///   时 <c>NPlayerHand.Instance.Add(queueItem.card)</c>（实机证据：2026-10-05 那局 4 次
///   `瓦库出牌入队后被取消（牌留在手牌，交看门狗下一轮）` —— 被取消的出牌正是走这条路把牌"还回手牌"）。</item>
/// </list>
/// <para>一旦 <c>LocalContext</c> 那一刻恰好等于瓦库席位，瓦库的牌节点就会被挂进共享手牌 UI，
/// 而当前显示的可能是真人的手牌 ⇒ 幽灵牌。上游 <c>CardPileCmd.Add</c> 那条已有
/// <see cref="CardPileAddForegroundContextPinPatch"/> 钉住上下文、节点创建那条已有
/// <see cref="CardPileHandVisualOwnerGuardPatch"/> 拦截，但**节点早就存在**的两条路径
/// （出牌区回手牌 / 取消出牌）绕过它们 ⇒ 统一在唯一加入点兜底。</para>
///
/// <para><b>为什么改判定来源而不是继续钉 `LocalContext`</b>："这份手牌 UI 属于谁"由**受控席位**这一事实
/// 决定 —— 直接问 <c>SeatRegistry.ControlledSeatId</c>，与前台切人链路（<c>ApplyControlContext</c>
/// 先写受控位、再重建手牌 UI）同口径；切到瓦库时它自己的牌照常显示（不误伤）。</para>
///
/// <para><b>拦截后做什么</b>：<c>NPlayerHand.Add</c> 的调用方**都不会自己回收节点**
/// （取消出牌那条：<c>RemoveCardFromQueue</c> 注释明说 The node is not freed；手牌切换那条：
/// 节点在调用前已被 `MoveCardNodeToNewPileBeforeTween` 重挂到战斗 UI 上并按旧坐标摆好），
/// 所以这里补一次"原版对非本人牌的处理"（淡出 → 回收），避免节点滞留在屏幕中间。数据层一律不动：
/// 牌该在谁手牌堆就在谁那里，之后切到那个席位时手牌 UI 会按手牌堆重建
/// （<c>RefreshCombatUiForControlledPlayer</c>）⇒ 视觉自愈。</para>
/// </summary>
[HarmonyPatch(typeof(NPlayerHand), nameof(NPlayerHand.Add), new[] { typeof(NCard), typeof(int) })]
internal static class NPlayerHandAddOwnerGuardPatch
{
    /// <summary>本局累计拦截次数（日志用；不参与判定）。</summary>
    private static int _blockedHandNodeCount;

    [HarmonyPriority(Priority.First)]
    [HarmonyPrefix]
    private static bool Prefix(NPlayerHand __instance, NCard? card, ref NHandCardHolder __result)
    {
        // 只有「本地多控回环会话 + 多席」才可能出这类串台；其余一律放行（单机 / 真联机 / 单人冒险）。
        if (!LocalSelfCoopContext.IsEnabled
            || !LocalSelfCoopContext.UseSingleAdventureMode
            || RunManager.Instance.NetService is not LocalLoopbackHostGameService
            || (RunManager.Instance.DebugOnlyGetState()?.Players.Count ?? 0) <= 1)
        {
            return true;
        }

        if (card == null)
        {
            return true;
        }

        CardModel? model = card.Model;
        Player? owner = model?.Owner;
        // 身份口径问 Seat（R0 规则 1），判定收敛在纯逻辑（R0 规则 3）。
        ulong? selectionOwner = NPlayerHandSelectCardsSerializationPatch.CurrentSelectionOwnerId;
        SeatRegistry seats = LocalSeatSource.CurrentSeats();
        ulong? controlledPlayerId = seats.ControlledSeatId;
        ulong ownerId = owner?.NetId ?? 0UL;

        HandNodeAdmission admission = HandNodeAdmissionPolicy.Decide(
            localCoop: true,
            cardOwnerKnown: owner != null,
            selectionActive: selectionOwner != null,
            // 两两比较（卡片主人 ↔ 本次选牌主人），不是"这个 id 是不是我们席位"的判定
            // ⇒ 刻意不走 `LocalSeatSource`（选牌主人本身已由 ResolveSelectionOwnerId 过滤过本地席位）。
            cardOwnerIsSelectionOwner: selectionOwner != null && ownerId == selectionOwner.Value,
            controlledSeatKnown: controlledPlayerId != null,
            cardOwnerIsControlledSeat: owner != null && seats.IsControlled(ownerId),
            cardOwnerIsLocalSeat: owner != null && seats.IsLocalSeat(ownerId));

        switch (admission)
        {
            case HandNodeAdmission.Allow:
                return true;

            case HandNodeAdmission.BlockNotSelectionOwner:
                // 选牌期间，非选牌 owner 的牌加入手牌 UI → 跳过节点创建（数据层仍正常移动）。
                // ⚠ 必须连节点回收一起做：调用方（`GetTweenForCardsChangingPiles` 的 Hand 分支）只会"更早"把
                //   节点重挂到战斗 UI 上并按旧坐标摆好，然后才走到 Add —— 只拦不回收 = 卡面**停在屏幕中间不动**
                //   （2026-10-05 实机：瓦库打出【群情激愤】OUTRAGE 时正赶上真人那侧有选牌流程 ⇒ 节点被拦下、
                //   没人收回 ⇒ 一张牌滞留屏幕中央）。
                LocalMultiControlLogger.Warn(
                    $"[选牌守卫] 拦截非选牌owner的进手牌节点（节点已淡出回收）: card={model!.Id.Entry}, "
                    + $"owner={ownerId}, selectionOwner={selectionOwner!.Value}");
                FadeOutAndFree(card);
                __result = null!;
                return false;

            default:
                _blockedHandNodeCount++;
                LocalMultiControlLogger.Info(
                    $"[手牌视觉守卫] 拦截非前台席位的进手牌节点（防幽灵牌）: card={model!.Id.Entry}, "
                    + $"owner={LocalSelfCoopContext.GetSlotLabel(ownerId)}({ownerId}), "
                    + $"foreground={LocalSelfCoopContext.GetSlotLabel(controlledPlayerId!.Value)}({controlledPlayerId.Value}), "
                    + $"context={LocalContext.NetId?.ToString() ?? "null"}（本局累计 {_blockedHandNodeCount} 次）");
                FadeOutAndFree(card);
                __result = null!;
                return false;
        }
    }

    /// <summary>
    /// 原版对「不是我的牌」的处理（<c>NCardPlayQueue.TweenCardForCancellation</c>：淡出后回收）。
    /// 补间挂在 <c>SceneTree</c> 上（不挂卡牌节点自己）：节点被释放时补间仍会跑完回调，
    /// 否则会出现"淡到一半的节点永远留在场上"。
    /// </summary>
    private static void FadeOutAndFree(NCard card)
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
