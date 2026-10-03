using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// `CardCmd.Transform` 视觉门的托管席位改写目标（r203，BUG-29 第五轮）。
///
/// 由 <c>CardTransformAutomatedSeatGuardPatch</c> 的 transpiler 把原版那句
/// <c>LocalContext.IsMine(cardAdded2)</c> **换成对本方法的调用**：
/// 两者签名完全一致（<c>bool(CardModel)</c>），所以是"一个操作数替换"级别的 IL 改写，栈不变、语义单一。
///
/// 为什么这样最稳（四轮实机的教训，详见 references 坑 S）：
/// - 不依赖 `LocalContext.NetId` 在任何时刻的值 ⇒ 谁写、什么时候写都无所谓（r199/r201 都栽在这条线上）；
/// - 不依赖拦截极小方法 ⇒ 不怕 JIT 内联（r200 栽在这条线上）；
/// - 判定纯粹看"这张牌的归属者是不是托管席位" ⇒ 确定性、可离线单测（<see cref="AutomatedSeatTransformVisualPolicy"/>）。
/// </summary>
internal static class AutomatedSeatTransformVisualGate
{
    /// <summary>
    /// 替换原版 <c>LocalContext.IsMine(CardModel)</c> 的调用点。
    /// 托管席位（后台瓦库）的牌在变换视觉里一律不当成"我的牌"：
    /// 它的手牌 UI 不存在，去前台手牌找节点必然抛 `Couldn't get hand node`。
    /// </summary>
    public static bool IsMineForTransformVisual(CardModel? card)
    {
        bool baseResult = LocalContext.IsMine(card);
        bool ownerIsAutomatedSeat = card?.Owner != null
                                    && LocalSeatSource.CurrentSeats().IsWakuuDriven(card.Owner.NetId);
        return AutomatedSeatTransformVisualPolicy.ShouldTreatAsMine(baseResult, ownerIsAutomatedSeat);
    }
}
