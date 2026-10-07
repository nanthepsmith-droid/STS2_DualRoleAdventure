namespace PreloadStallGuard.Scripts.Diagnostics;

/// <summary>
/// 战斗开始推进链的边界打点（**只打日志，不改行为**）。
///
/// 目标是把 `CombatManager.StartCombatInternal` 之后那一串 await 切成可判读的区间：
///
/// ```
/// StartCombatInternal: SetCombatState(NotPlayPhase) ← 日志 "Combat becomes NotPlayPhase from PreCombatSetup"
///   → Hook.BeforeCombatStart        ← BeforeCombatStartPrefix
///   → Cmd.CustomScaledWait(0.5~1s)
///   → StartTurn                     ← StartTurnPrefix
///       → Hook.BeforeSideTurnStart  ← BeforeSideTurnStartPrefix
///       → Cmd.CustomScaledWait(0.5~0.8s)
///       → Creature.AfterTurnStart
///       → Hook.AfterBlockCleared    ← AfterBlockClearedPrefix
///       → SetupPlayerTurn           ← 主 mod 的 "回合开始兜底重评结束回合按钮: source=turn-start-setup"
///   → RunAutoPrePlayPhase           ← RunAutoPrePlayPhasePrefix
///   → SetCombatState(PlayPhase)     ← 日志 "Combat state becomes PlayPhase"
/// ```
///
/// 只要看「最后出现了哪个打点」，就立刻知道卡在哪一段 —— 不必再靠猜。
/// 一次性排查工具：确认病灶后把这些打点连同整个补丁 mod 一起撤掉即可。
/// </summary>
internal static class CombatFlowProbe
{
    internal static void BeforeCombatStartPrefix() => PreloadStallLog.Probe("Hook.BeforeCombatStart 进入");

    internal static void BeforeSideTurnStartPrefix() => PreloadStallLog.Probe("Hook.BeforeSideTurnStart 进入");

    internal static void AfterBlockClearedPrefix() => PreloadStallLog.Probe("Hook.AfterBlockCleared 进入");

    internal static void StartTurnPrefix() => PreloadStallLog.Probe("CombatManager.StartTurn 进入");

    internal static void RunAutoPrePlayPhasePrefix() => PreloadStallLog.Probe("CombatManager.RunAutoPrePlayPhase 进入");
}
