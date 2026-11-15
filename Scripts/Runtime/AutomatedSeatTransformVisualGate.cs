using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// `CardCmd.Transform` 视觉门的托管席位改写目标（r203，BUG-29 第五轮；r360 追加前台托管席位分流）。
///
/// 由 <c>CardTransformAutomatedSeatGuardPatch</c> 的 transpiler 把原版那句
/// <c>LocalContext.IsMine(cardAdded2)</c> **换成对本方法的调用**：
/// 两者签名完全一致（<c>bool(CardModel)</c>），所以是"一个操作数替换"级别的 IL 改写，栈不变、语义单一。
///
/// 为什么这样最稳（四轮实机的教训，详见 references 坑 S）：
/// - 不依赖 `LocalContext.NetId` 在任何时刻的值 ⇒ 谁写、什么时候写都无所谓（r199/r201 都栽在这条线上）；
/// - 不依赖拦截极小方法 ⇒ 不怕 JIT 内联（r200 栽在这条线上）；
/// - 判定纯粹看"这张牌的归属者是不是托管席位" ⇒ 确定性、可离线单测（<see cref="AutomatedSeatTransformVisualPolicy"/>）。
///
/// r360：判定拆成「后台托管席位（跳过视觉）/ 前台托管席位（手牌 UI 就是它的 ⇒ 照原版播）」。
/// 后者额外做一次**视觉那一刻**的节点存在性检查 —— 这正是 r199 栽跟头的地方（Prefix 探针看到节点、
/// 视觉阶段却查不到）。这句话本身就在视觉阶段执行，与紧随其后的
/// <c>NCard.FindOnTable(original, PileType.Hand)</c> 同一时刻，所以判断是准的。
/// </summary>
internal static class AutomatedSeatTransformVisualGate
{
    /// <summary>
    /// 替换原版 <c>LocalContext.IsMine(CardModel)</c> 的调用点。
    /// 后台托管席位（后台瓦库）的牌在变换视觉里一律不当成"我的牌"：
    /// 它的手牌 UI 不存在，去前台手牌找节点必然抛 `Couldn't get hand node`。
    /// 前台托管席位（全瓦库）的手牌 UI 就是它那份 ⇒ 照原版播视觉（r360）。
    /// </summary>
    public static bool IsMineForTransformVisual(CardModel? card)
    {
        bool baseResult = LocalContext.IsMine(card);
        if (!baseResult || card?.Owner == null)
        {
            return baseResult;
        }

        SeatRegistry seats = LocalSeatSource.CurrentSeats();
        bool ownerIsAutomatedSeat = seats.IsWakuuDriven(card.Owner.NetId);
        bool ownerIsForeground = ownerIsAutomatedSeat && seats.IsControlled(card.Owner.NetId);
        bool handNodesAvailable = !ownerIsForeground || HasPendingOriginalHandNode(card.Owner);
        return AutomatedSeatTransformVisualPolicy.ShouldTreatAsMine(
            baseResult, ownerIsAutomatedSeat, ownerIsForeground, handNodesAvailable);
    }

    /// <summary>
    /// 视觉阶段那一刻，手牌容器里是否还有"原牌"节点 —— 判据是**有一张 holder 的模型已经不在手牌堆里**
    /// （变换的数据阶段先把原牌移出手牌、新牌加入，视觉阶段才拿原牌节点播特效；
    /// 所以"节点在、模型已不在堆里"就是待替换的原牌节点）。
    ///
    /// 手牌 UI 还没建好 / 不在战斗 / 拿不到手牌堆 ⇒ 返回 false（保守：跳过视觉，绝不冒 `Couldn't get hand node`）。
    /// </summary>
    internal static bool HasPendingOriginalHandNode(Player? player)
    {
        try
        {
            if (player?.PlayerCombatState == null)
            {
                return false;
            }

            NPlayerHand? hand = NCombatRoom.Instance?.Ui?.Hand;
            if (hand?.CardHolderContainer == null)
            {
                return false;
            }

            HashSet<CardModel> pileCards = new(PileType.Hand.GetPile(player).Cards);
            foreach (Godot.Node child in hand.CardHolderContainer.GetChildren())
            {
                if (child is NCardHolder holder
                    && Godot.GodotObject.IsInstanceValid(holder)
                    && holder.CardNode?.Model is { } model
                    && !pileCards.Contains(model))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception)
        {
            // 纯视觉探针：任何异常都按"节点不可用"处理（跳过视觉）。
            return false;
        }
    }
}
