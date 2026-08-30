using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 决策上下文：传给 <see cref="IWakuuCombatBrain"/> 一次「下一步」评估所需的全部标量。
/// 归属者显式化：<see cref="Wakuu"/> 必填，禁止任何实现内部用 LocalContext.GetMe——
/// 瓦库是后台角色，GetMe 在多控环境下语义漂移（瓦库托管优化可行性分析 21.3.1 约束 5）。
/// </summary>
internal readonly struct WakuuDecisionContext
{
    public WakuuDecisionContext(
        Player wakuu,
        ICombatState combat,
        IReadOnlyList<CardModel> hand,
        int energy,
        int turnNumber,
        int playedThisTurn,
        bool isBackground)
    {
        Wakuu = wakuu;
        Combat = combat;
        Hand = hand;
        Energy = energy;
        TurnNumber = turnNumber;
        PlayedThisTurn = playedThisTurn;
        IsBackground = isBackground;
    }

    /// <summary>决策归属者（瓦库角色）。</summary>
    public Player Wakuu { get; }

    /// <summary>当前战斗状态。</summary>
    public ICombatState Combat { get; }

    /// <summary>瓦库当前手牌（快照引用，决策时只读）。</summary>
    public IReadOnlyList<CardModel> Hand { get; }

    /// <summary>瓦库当前能量。</summary>
    public int Energy { get; }

    /// <summary>当前回合数（1 起）。</summary>
    public int TurnNumber { get; }

    /// <summary>本回合已打出张数（供护栏与调试）。</summary>
    public int PlayedThisTurn { get; }

    /// <summary>后台托管模式（影响是否允许产生交互）。</summary>
    public bool IsBackground { get; }
}
