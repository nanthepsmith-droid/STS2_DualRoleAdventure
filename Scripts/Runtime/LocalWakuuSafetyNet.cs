using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库形态后台托管的通用交互安全网（可行性报告 6.4）。
/// 后台化后，作用域外的交互式弹层（尤其是不走 CardSelectCmd 的第三方自绘界面）
/// 可能无人处理导致流程停滞。本类以固定周期探测两类滞留：
/// 1. 战斗滞留：玩家阶段存在弹层、后台瓦库有牌可出且手牌长时间无变化；
/// 2. 事件滞留：非战斗状态下存在后台瓦库未完成的事件房间。
/// 条件持续超过阈值后把前台切给该瓦库角色并全屏提示，把硬软锁降级为"请人工点一下"。
/// 仅在瓦库形态后台模式开启时生效。
/// </summary>
internal static class LocalWakuuSafetyNet
{
    private const long CheckIntervalMs = 500L;
    private const double CombatStallTimeoutSec = 12.0;
    private const double EventStallTimeoutSec = 8.0;

    private static LocalSafetyNetTicker? _ticker;

    private static long _lastCheckMs;
    private static int? _lastCombatHandSignature;
    private static double _combatStallSeconds;
    private static ulong? _combatStallPlayerId;

    private static double _eventStallSeconds;
    private static ulong? _eventStallPlayerId;

    public static void EnsureTicker()
    {
        if (_ticker != null && GodotObject.IsInstanceValid(_ticker))
        {
            return;
        }

        _ticker = new LocalSafetyNetTicker();
        NGame.Instance?.AddChildSafely(_ticker);
        LocalMultiControlLogger.Info("瓦库交互安全网已挂载。");
    }

    public static void Tick()
    {
        if (!LocalSelfCoopContext.IsEnabled
            || !LocalWakuuAutopilotConfig.UseVakuuForm
            || !LocalWakuuAutopilotConfig.BackgroundMode
            || !RunManager.Instance.IsInProgress)
        {
            ResetTimers();
            return;
        }

        long nowMs = (long)Time.GetTicksMsec();
        if (_lastCheckMs != 0 && nowMs - _lastCheckMs < CheckIntervalMs)
        {
            return;
        }

        double stepSeconds = _lastCheckMs == 0 ? 0 : (nowMs - _lastCheckMs) / 1000.0;
        _lastCheckMs = nowMs;
        TickCombatStall(stepSeconds);
        TickEventStall(stepSeconds);
    }

    private static void TickCombatStall(double stepSeconds)
    {
        if (!CombatManager.Instance.IsInProgress
            || CombatManager.Instance.IsOverOrEnding
            || RunManager.Instance.ActionQueueSynchronizer.CombatState != ActionSynchronizerCombatState.PlayPhase
            || TryGetCurrentSideSafe() != CombatSide.Player)
        {
            ResetCombatTimer();
            return;
        }

        Player? stalled = FindBackgroundFormPlayerWithPlayableCards();
        if (stalled == null || IsForeground(stalled.NetId))
        {
            ResetCombatTimer();
            return;
        }

        // 存在弹层才视为"被界面卡住"；正常自动出牌不会长时间挂着弹层不动。
        if ((NOverlayStack.Instance?.ScreenCount ?? 0) <= 0)
        {
            ResetCombatTimer();
            return;
        }

        int handSignature = ComputeHandSignature(stalled);
        if (_combatStallPlayerId != stalled.NetId || _lastCombatHandSignature != handSignature)
        {
            _combatStallPlayerId = stalled.NetId;
            _lastCombatHandSignature = handSignature;
            _combatStallSeconds = 0;
            return;
        }

        if (IsInTransientPickFlow())
        {
            // 真人正在拖牌/选目标时不累计也不切换，避免打断进行中的操作。
            return;
        }

        _combatStallSeconds += stepSeconds;
        if (_combatStallSeconds < CombatStallTimeoutSec)
        {
            return;
        }

        _combatStallSeconds = 0;
        string tip = LocalModText.Select(
            $"瓦库（{LocalSelfCoopContext.GetSlotLabel(stalled.NetId)}号位）需要手动处理当前界面",
            $"Vakuu (slot {LocalSelfCoopContext.GetSlotLabel(stalled.NetId)}) needs manual attention");
        LocalMultiControlLogger.Warn(
            $"[安全网] 战斗滞留超时，切前台交由人工处理: player={stalled.NetId}, round={stalled.Creature.CombatState?.RoundNumber}");
        SwitchWithHint(stalled.NetId, "wakuu-safety-net-combat", tip);
    }

    private static void TickEventStall(double stepSeconds)
    {
        if (CombatManager.Instance.IsInProgress || NOverlayStack.Instance?.ScreenCount > 0)
        {
            ResetEventTimer();
            return;
        }

        Player? pendingOwner = RunManager.Instance.EventSynchronizer.Events
            .Where((candidate) => candidate.Owner != null && !candidate.IsFinished)
            .Select((candidate) => candidate.Owner!)
            .FirstOrDefault((owner) => IsBackgroundFormPlayer(owner));
        if (pendingOwner == null || IsForeground(pendingOwner.NetId))
        {
            ResetEventTimer();
            return;
        }

        if (_eventStallPlayerId != pendingOwner.NetId)
        {
            _eventStallPlayerId = pendingOwner.NetId;
            _eventStallSeconds = 0;
            return;
        }

        _eventStallSeconds += stepSeconds;
        if (_eventStallSeconds < EventStallTimeoutSec)
        {
            return;
        }

        _eventStallSeconds = 0;
        string tip = LocalModText.Select(
            $"瓦库（{LocalSelfCoopContext.GetSlotLabel(pendingOwner.NetId)}号位）的事件等待选择",
            $"Vakuu (slot {LocalSelfCoopContext.GetSlotLabel(pendingOwner.NetId)}) has an event choice pending");
        LocalMultiControlLogger.Warn($"[安全网] 事件滞留超时，切前台交由人工处理: player={pendingOwner.NetId}");
        SwitchWithHint(pendingOwner.NetId, "wakuu-safety-net-event", tip);
    }

    private static Player? FindBackgroundFormPlayerWithPlayableCards()
    {
        CombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState == null)
        {
            return null;
        }

        foreach (Player player in combatState.Players)
        {
            if (player?.Creature == null
                || !player.Creature.IsAlive
                || !IsBackgroundFormPlayer(player)
                || IsForeground(player.NetId))
            {
                continue;
            }

            bool hasPlayableCards = PileType.Hand.GetPile(player).Cards.Any((card) => card.CanPlay());
            if (hasPlayableCards)
            {
                return player;
            }
        }

        return null;
    }

    private static bool IsBackgroundFormPlayer(Player player)
    {
        return LocalWakuuRelicRuntime.ShouldSuppressForegroundSwitch(player, onlyWhenSelectorActive: false);
    }

    private static bool IsForeground(ulong playerId)
    {
        ulong? current = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId ?? LocalContext.NetId;
        return current == playerId;
    }

    private static int ComputeHandSignature(Player player)
    {
        System.Collections.Generic.IReadOnlyList<CardModel> cards = PileType.Hand.GetPile(player).Cards;
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + cards.Count;
            foreach (CardModel card in cards)
            {
                hash = hash * 31 + (card.EnergyCost?.GetAmountToSpend() ?? 0).GetHashCode();
            }

            return hash;
        }
    }

    private static CombatSide TryGetCurrentSideSafe()
    {
        try
        {
            return CombatManager.Instance.DebugOnlyGetState()?.CurrentSide ?? CombatSide.None;
        }
        catch
        {
            return CombatSide.None;
        }
    }

    private static bool IsInTransientPickFlow()
    {
        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            return false;
        }

        return combatUi.Hand.InCardPlay
            || combatUi.Hand.IsInCardSelection
            || (NTargetManager.Instance?.IsInSelection ?? false);
    }

    private static void SwitchWithHint(ulong playerId, string source, string tip)
    {
        NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create(tip));
        LocalMultiControlRuntime.SwitchControlledPlayerTo(playerId, source);
    }

    private static void ResetCombatTimer()
    {
        _combatStallSeconds = 0;
        _combatStallPlayerId = null;
        _lastCombatHandSignature = null;
    }

    private static void ResetEventTimer()
    {
        _eventStallSeconds = 0;
        _eventStallPlayerId = null;
    }

    private static void ResetTimers()
    {
        _lastCheckMs = 0;
        ResetCombatTimer();
        ResetEventTimer();
    }
}

/// <summary>挂在 NGame 下的常驻帧回调节点，负责驱动安全网周期检测。</summary>
internal sealed partial class LocalSafetyNetTicker : Node
{
    public override void _Process(double delta)
    {
        LocalWakuuSafetyNet.Tick();
    }
}
