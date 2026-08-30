namespace LocalMultiControl.Scripts.Runtime;

/// <summary>药水评估相位（位标志）：回合开始出牌前 / 回合结束前。</summary>
[Flags]
internal enum WakuuPotionPhase
{
    StartOfTurn = 1,
    EndOfTurn = 2,
    Both = StartOfTurn | EndOfTurn,
}

/// <summary>规则适用的战斗范围（值顺序与运行时私有枚举一致，0/1/2）。</summary>
internal enum WakuuPotionFightScope
{
    AnyCombat,
    HardFight,   // 精英或 Boss
    BossFight,   // 仅 Boss
}

/// <summary>目标解析策略（值顺序与运行时私有枚举一致，0/1/2）。</summary>
internal enum WakuuPotionTargetKind
{
    Default,          // AnyEnemy→第一个敌人；AllEnemies→null；其余→自己
    HumanFirst,       // 优先给存活的真人队友，没有则自用
    AllyCharacter,    // 自己就是该职业则自用，否则给该职业的存活队友，再没有则自用
}

/// <summary>
/// 药水规则表的元数据快照（纯数据，供单元测试校验规则表内容，不依赖游戏运行时）。
/// MatchedPotionTypeName 由运行时从 Match 谓词的 "p is X" 表达式提取。
/// </summary>
internal sealed record WakuuPotionRuleMeta(
    string Name,
    WakuuPotionPhase Phases,
    WakuuPotionFightScope Scope,
    bool FirstRoundOnly,
    bool SkipWhenStunned,
    bool DiscardInsteadOfUse,
    WakuuPotionTargetKind Target,
    string? AllyCharacterTypeName,
    string? MatchedPotionTypeName,
    bool HasCondition,
    bool HasCardPicker);
