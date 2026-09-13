using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「这一位的回合是谁结束的」归因（BUG-10 收口的第二版，2026-09-13）。
///
/// **为什么要归因**：原版拦"结束后还出牌"靠 UI（`PlayerActionsDisabled` → 手牌禁用），
/// 而我们的自动出牌链路绕开 UI，必须自己判。第一版（r122）用"只要该玩家 `IsPlayerReadyToEndTurn`
/// 就停手"，虽然修好了虚空形态，但**连带否掉了用户想保留的行为**：
/// 「模组自己收口（无牌可出时全员收口）之后，本回合内又因别人的效果拿到可出牌 → 继续打」。
///
/// **两类结束的含义完全不同**：
/// <list type="bullet">
/// <item><b>外部结束</b>（卡牌效果/原版）：如虚空形态 `VoidForm.OnPlay` → `PlayerCmd.EndTurn(owner, canBackOut: false)`
///   —— 玩家已经把回合**交出去了**，之后不该再出牌（BUG-10 要拦的就是这个）；</item>
/// <item><b>模组收口</b>（`LocalMultiControlRuntime.TryEndAllPlayersWhenNoCards`）：只是"这一位当刻没牌可打"
///   的便利收口，若本回合内它又拿到可出牌，继续打是合理的（用户明确要求保留）。</item>
/// </list>
///
/// 判据见 <see cref="ShouldStopAutoplay"/>；登记由 `CombatManager.SetReadyToEndTurn` 的前缀补丁 +
/// 模组自己发起结束时的 <see cref="BeginModIssuedEnd"/> 标记共同完成。
/// 本文件不依赖任何游戏类型，可直接单测。
/// </summary>
internal static class WakuuTurnEndOrigin
{
    private static readonly HashSet<string> _modEnded = new(StringComparer.Ordinal);
    private static readonly HashSet<string> _externallyEnded = new(StringComparer.Ordinal);
    private static bool _modIssuing;

    /// <summary>
    /// 「该玩家的自动出牌是否必须停手」——纯判定。
    /// </summary>
    /// <param name="playerReadyToEndTurn">该玩家是否已处于 ready-to-end-turn（`CombatManager.IsPlayerReadyToEndTurn`）。</param>
    /// <param name="endedExternally">本回合这一位的 ready 是否由**模组以外**的东西造成（卡牌效果 / 原版逻辑）。</param>
    /// <param name="endedByMod">本回合这一位的 ready 是否由**模组自己**的"无牌可出→收口"造成。</param>
    /// <param name="allPlayersReadyToEndTurn">是否所有玩家都已 ready（回合即将推进）。</param>
    /// <returns>true = 停手（熔断自动出牌）。</returns>
    public static bool ShouldStopAutoplay(
        bool playerReadyToEndTurn,
        bool endedExternally,
        bool endedByMod,
        bool allPlayersReadyToEndTurn)
    {
        if (!playerReadyToEndTurn)
        {
            return false;
        }

        // 外部强行结束（虚空形态等）**或来源不明**：一律停手。
        // 刻意把"来源不明"也算成停手（保守）—— 旧战局残留、补丁未覆盖到的新路径都不该让它继续出牌。
        if (endedExternally || !endedByMod)
        {
            return true;
        }

        // 模组自己收口：只要还没到"全员 ready、回合马上要推进"，就放行（允许本回合内拿到新牌继续打）。
        // 一旦全员 ready，再出牌就会撞进回合推进窗口，必须停手。
        return allPlayersReadyToEndTurn;
    }

    /// <summary>
    /// 「此刻是否正处在**模组收口被放行**的状态」（即 <see cref="ShouldStopAutoplay"/> 判定为"不停手"、
    /// 且原因是"模组自己的收口还没等到全员 ready"）。
    /// 供日志使用：只有它成立时打"模组收口后继续出牌"才是准确的 ——
    /// 单看 `IsPlayerReadyToEndTurn` 会把**虚空形态**那种外部强行结束也误报成"模组收口后继续出牌"
    /// （r124 首版就这么错过一次，实机日志 8926 行误报、下一行立刻被 8927 熔断）。
    /// </summary>
    public static bool IsModIssuedExemption(
        bool playerReadyToEndTurn,
        bool endedExternally,
        bool endedByMod,
        bool allPlayersReadyToEndTurn)
    {
        return playerReadyToEndTurn && endedByMod && !endedExternally && !allPlayersReadyToEndTurn;
    }

    /// <summary>登记"这一位的回合结束"；由 `CombatManager.SetReadyToEndTurn` 的前缀补丁调用。</summary>
    public static void Record(ulong playerNetId, int roundNumber)
    {
        string key = BuildKey(playerNetId, roundNumber);
        if (_modIssuing)
        {
            _modEnded.Add(key);
            _externallyEnded.Remove(key);
            return;
        }

        _externallyEnded.Add(key);
        _modEnded.Remove(key);
    }

    /// <summary>模组即将自己发起一次结束回合（`PlayerCmd.EndTurn`）——包裹住这一次调用。</summary>
    public static void BeginModIssuedEnd()
    {
        _modIssuing = true;
    }

    /// <summary>模组发起的结束回合调用已返回。</summary>
    public static void EndModIssuedEnd()
    {
        _modIssuing = false;
    }

    /// <summary>本回合这一位的 ready 是否由模组自己造成。</summary>
    public static bool EndedByMod(ulong playerNetId, int roundNumber)
    {
        return _modEnded.Contains(BuildKey(playerNetId, roundNumber));
    }

    /// <summary>本回合这一位的 ready 是否由模组以外的东西造成。</summary>
    public static bool EndedExternally(ulong playerNetId, int roundNumber)
    {
        return _externallyEnded.Contains(BuildKey(playerNetId, roundNumber));
    }

    /// <summary>换战斗/退出本局时清空（回合号在战斗内才有意义）。</summary>
    public static void ResetForCombat()
    {
        _modEnded.Clear();
        _externallyEnded.Clear();
        _modIssuing = false;
    }

    private static string BuildKey(ulong playerNetId, int roundNumber)
    {
        return $"{roundNumber}:{playerNetId}";
    }
}
