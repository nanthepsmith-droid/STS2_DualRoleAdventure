using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 手牌点击「被静默忽略」的诊断埋点（r148 诊断线，配合 2026-09-25 r147 实机的插队反馈）。
///
/// **为什么要它**：r147 那局实测（3 席局，marker r147）—— 机器人死后瓦库连出 4 张牌的那 12 秒里，
/// 真人一次都没出成牌，日志里**一条痕迹都没有**（既没有我们的 `进入手动出牌临界区`，也没有游戏的
/// 任何提示）；而瓦库一停，真人立刻连出 4 张。也就是说"点了没反应"这件事在日志里是**不可见**的。
///
/// **原版的点击闸门**（`NPlayerHand.OnHolderPressed`）会在这几种情况下**静默 return**：
/// 预览键按着 / 卡片节点还没建出来 / 战斗不在进行 / **有弹层** / 手牌模式不是 Play /
/// **正在播一张牌的出牌动画（InCardPlay）** / 动作被禁用 / 别人正在"额外回合"。
/// 另外即使闸门通过，`NMouseCardPlay` 里还有一道 `Card.CanPlay()`：不通过就只把牌拖回手牌
/// （同样没有任何日志）。
///
/// 本补丁**只记录不干预**（Prefix 不改返回值、不 return false）：
/// - 闸门会拦住这次点击 → 打一条 `手牌点击被忽略: reason=…`（同一 reason 10 秒内只记一条，带累计次数）；
/// - 闸门通过但 `CanPlay` 为假 → 打一条 `手牌点击已受理但出不了牌: …`（带 UnplayableReason / 阻止者）。
/// 两条都是可 grep 的固定锚点，下一局复现即可定性"到底是哪一道闸门挡住的"。
/// </summary>
[HarmonyPatch(typeof(NPlayerHand), "OnHolderPressed")]
internal static class NPlayerHandClickDiagnosticPatch
{
    /// <summary>同一 reason 的日志节流（避免连点刷屏）。</summary>
    private const long ThrottleMs = 10000L;

    private static readonly Dictionary<string, long> _lastLoggedMs = new();
    private static readonly Dictionary<string, int> _hitCounts = new();

    [HarmonyPrefix]
    private static void Prefix(NPlayerHand __instance, NCardHolder holder)
    {
        try
        {
            if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress || __instance == null)
            {
                return;
            }

            string? reason = ResolveIgnoreReason(__instance, holder);
            if (reason != null)
            {
                LogIgnored(reason, holder);
                return;
            }

            LogIfUnplayable(holder);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"手牌点击诊断异常(已忽略): {exception.Message}");
        }
    }

    /// <summary>按原版 `OnHolderPressed` 的判定顺序复刻"这次点击会不会被静默忽略"。</summary>
    private static string? ResolveIgnoreReason(NPlayerHand hand, NCardHolder holder)
    {
        if (hand.PeekButton != null && hand.PeekButton.IsPeeking)
        {
            return "peek-active";
        }

        if (holder is not NHandCardHolder handHolder)
        {
            return "not-hand-holder";
        }

        if (handHolder.CardNode == null)
        {
            return "no-card-node";
        }

        if (!CombatManager.Instance.IsInProgress)
        {
            return "combat-not-in-progress";
        }

        NOverlayStack? stack = NOverlayStack.Instance;
        int screenCount = stack?.ScreenCount ?? 0;
        if (screenCount > 0)
        {
            return $"overlay(screenCount={screenCount}, top={stack?.Peek()?.GetType().Name ?? "none"})";
        }

        NPlayerHand.Mode mode = hand.CurrentMode;
        if (mode == NPlayerHand.Mode.None)
        {
            return "hand-mode-none";
        }

        if (mode != NPlayerHand.Mode.Play)
        {
            // SimpleSelect / UpgradeSelect：这次点击本来就不是"出牌"，不记（选牌流程有自己的日志）。
            return null;
        }

        if (hand.InCardPlay)
        {
            return "hand-in-card-play";
        }

        if (CombatManager.Instance.PlayerActionsDisabled)
        {
            return "player-actions-disabled";
        }

        if (CombatManager.Instance.PlayersTakingExtraTurn.Count > 0)
        {
            CombatState? state = CombatManager.Instance.DebugOnlyGetState();
            Player? me = state == null ? null : LocalContext.GetMe(state);
            if (me == null || !CombatManager.Instance.PlayersTakingExtraTurn.Contains(me))
            {
                return $"extra-turn-other(count={CombatManager.Instance.PlayersTakingExtraTurn.Count})";
            }
        }

        return null;
    }

    /// <summary>闸门放行 → 再报一次"原版接下来会静默把牌拖回去"的不可出牌原因。</summary>
    private static void LogIfUnplayable(NCardHolder holder)
    {
        if (holder is not NHandCardHolder handHolder || handHolder.CardNode?.Model is not CardModel card)
        {
            return;
        }

        if (card.CanPlay(out UnplayableReason reason, out AbstractModel? preventer))
        {
            return;
        }

        LogThrottled(
            $"手牌点击已受理但出不了牌: card={card.Id.Entry}, reason={reason}, "
            + $"preventer={preventer?.Id.Entry ?? "无"}, owner={card.Owner?.NetId.ToString() ?? "?"}");
    }

    private static void LogIgnored(string reason, NCardHolder holder)
    {
        string card = holder is NHandCardHolder handHolder
            ? handHolder.CardNode?.Model?.Id.Entry ?? "?"
            : "?";
        LogThrottled($"手牌点击被忽略: reason={reason}, card={card}");
    }

    private static void LogThrottled(string message)
    {
        string key = message.Split(',')[0];
        _hitCounts.TryGetValue(key, out int count);
        _hitCounts[key] = count + 1;

        long nowMs = (long)Time.GetTicksMsec();
        if (_lastLoggedMs.TryGetValue(key, out long lastMs) && nowMs - lastMs < ThrottleMs)
        {
            return;
        }

        _lastLoggedMs[key] = nowMs;
        LocalMultiControlLogger.Warn($"{message}（本局累计 {_hitCounts[key]} 次）");
    }
}
