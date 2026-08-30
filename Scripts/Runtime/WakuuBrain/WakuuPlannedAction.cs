using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>大脑产出的下一步动作类型。</summary>
internal enum WakuuActionKind
{
    PlayCard,
    UsePotion,
    EndTurn,
    HandOffToHuman,
}

/// <summary>
/// 大脑的一次决策结果（只出决策、不执行）。
/// 执行仍走 LocalWakuuRelicRuntime 既有链路（PushSelector 作用域 + CardCmd.AutoPlay +
/// 看门狗）；大脑禁止自己调用 TryManualPlay / AutoPlay / EnqueueManualUse。
/// </summary>
internal readonly struct WakuuPlannedAction
{
    public WakuuPlannedAction(
        WakuuActionKind kind,
        CardModel? card,
        Creature? target,
        PotionModel? potion,
        int potionSlot,
        string reason,
        bool confident)
    {
        Kind = kind;
        Card = card;
        Target = target;
        Potion = potion;
        PotionSlot = potionSlot;
        Reason = reason;
        Confident = confident;
    }

    public WakuuActionKind Kind { get; }

    /// <summary>PlayCard 时对应的卡。</summary>
    public CardModel? Card { get; }

    /// <summary>目标（PlayCard 时为打牌目标；UsePotion 时为药水目标）。</summary>
    public Creature? Target { get; }

    /// <summary>UsePotion 时对应的药水。</summary>
    public PotionModel? Potion { get; }

    /// <summary>UsePotion 时的药水栏位。</summary>
    public int PotionSlot { get; }

    /// <summary>仅用于日志/覆盖层。</summary>
    public string Reason { get; }

    /// <summary>false → 记日志并考虑降级。</summary>
    public bool Confident { get; }
}
