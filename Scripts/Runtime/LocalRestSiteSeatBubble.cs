using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 第三方席位（Co-op Bots 的合成 Bot）休息区选择的**可视化兜底**。
///
/// 问题（2026-09-26 实机定位）：非本地席位的休息区选择在 <c>BeginRestSite</c> 那一刻就被索取并当场作答，
/// 而房间节点是之后才实例化的 ⇒ 没有选中动画、没有角色气泡，玩家以为「Bot 不会行动」。
/// 实测：4 个休息区里 Bot 都选了（`success True`，MEND / HEAL / SMITH 各不相同），
/// 但 `[气泡诊断] SetSelecting` 的 owner 只有真人 / 瓦库，**Bot 一条都没有**。
///
/// 做法：订阅游戏自己的公开事件 <c>RestSiteSynchronizer.AfterPlayerOptionChosen</c>（不新增 Harmony 目标），
/// 把第三方席位的已选项记下来，房间就绪后补画「已选」气泡，并打一条日志 ——
/// 这条日志同时回答「它的选择到底有没有生效」（`success` 就是游戏侧的判定）。
///
/// ⚠ 全程纯表现 + try/catch：任何失败都只记 WARN，绝不影响休息区流程本身。
/// </summary>
internal static class LocalRestSiteSeatBubble
{
    /// <summary>待补画的选择（房间还没就绪时先记下来）。</summary>
    private sealed class PendingChoice
    {
        internal RestSiteOption Option = null!;
        internal bool Success;
    }

    private static readonly Dictionary<ulong, PendingChoice> _pending = new();
    private static readonly object _lock = new();

    private static RestSiteSynchronizer? _subscribed;

    /// <summary>进火堆时调用（来自 <c>RestSiteSynchronizerBeginRestSitePatch</c>）：清掉上一轮的待画记录并确保已订阅。</summary>
    public static void OnRestSiteBegun(RestSiteSynchronizer synchronizer)
    {
        try
        {
            lock (_lock)
            {
                _pending.Clear();
            }

            if (!LocalSelfCoopContext.IsEnabled)
            {
                return;
            }

            if (ReferenceEquals(_subscribed, synchronizer))
            {
                return;
            }

            if (_subscribed != null)
            {
                _subscribed.AfterPlayerOptionChosen -= OnAfterPlayerOptionChosen;
            }

            synchronizer.AfterPlayerOptionChosen += OnAfterPlayerOptionChosen;
            _subscribed = synchronizer;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"第三方席位休息区气泡订阅失败: {exception.Message}");
        }
    }

    /// <summary>退局清理：退订 + 清空（避免静态引用拖住上一局的同步器）。</summary>
    public static void Reset(string reason)
    {
        try
        {
            if (_subscribed != null)
            {
                _subscribed.AfterPlayerOptionChosen -= OnAfterPlayerOptionChosen;
                _subscribed = null;
            }

            lock (_lock)
            {
                _pending.Clear();
            }

            LocalMultiControlLogger.Info($"第三方席位休息区气泡已复位: source={reason}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"第三方席位休息区气泡复位失败: source={reason}, error={exception.Message}");
        }
    }

    private static void OnAfterPlayerOptionChosen(RestSiteOption option, bool success, ulong playerId)
    {
        try
        {
            if (!RestSeatBubblePolicy.ShouldRecord(LocalSelfCoopContext.IsEnabled, LocalSelfCoopContext.IsLocalSessionSeat(playerId)))
            {
                return;
            }

            lock (_lock)
            {
                _pending[playerId] = new PendingChoice
                {
                    Option = option,
                    Success = success,
                };
            }

            if (RestSeatBubblePolicy.ShouldShowSelectedBubble(success))
            {
                LocalMultiControlLogger.Info(
                    $"第三方席位休息区选择已记录（将补画气泡）: player={playerId}, option={option.OptionId}, success={success}");
            }
            else
            {
                LocalMultiControlLogger.Warn(
                    $"第三方席位休息区选项执行失败（游戏返回 false，本次未生效）: player={playerId}, option={option.OptionId}");
            }

            // 房间可能已经就绪（慢一点的作答）→ 下一帧试一次；还没就绪就等房间路径再来试。
            Callable.From(delegate { TryApplyPending("seat-choice-event"); }).CallDeferred();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"第三方席位休息区选择记录失败: player={playerId}, error={exception.Message}");
        }
    }

    /// <summary>
    /// 把待补画的气泡画出来（房间就绪后调用；<c>source</c> 只用于日志）。
    /// 拿不到角色节点就**保留**待画记录，等下一次调用（不静默丢弃）。
    /// </summary>
    public static void TryApplyPending(string source)
    {
        try
        {
            if (!LocalSelfCoopContext.IsEnabled)
            {
                return;
            }

            NRestSiteRoom? room = NRestSiteRoom.Instance;
            if (room == null)
            {
                return;
            }

            List<ulong> playerIds;
            lock (_lock)
            {
                if (_pending.Count == 0)
                {
                    return;
                }

                playerIds = new List<ulong>(_pending.Keys);
            }

            RunState? runState = RunManager.Instance.DebugOnlyGetState();

            foreach (ulong playerId in playerIds)
            {
                PendingChoice? choice;
                lock (_lock)
                {
                    if (!_pending.TryGetValue(playerId, out choice))
                    {
                        continue;
                    }
                }

                Player? player = runState?.GetPlayer(playerId);
                if (player == null)
                {
                    continue;
                }

                NRestSiteCharacter? character = room.GetCharacterForPlayer(player);
                if (character == null)
                {
                    continue;
                }

                if (!RestSeatBubblePolicy.ShouldShowSelectedBubble(choice.Success))
                {
                    // 失败的不画"已选"，只把记录摘掉（WARN 已经在事件里打过）。
                    lock (_lock)
                    {
                        _pending.Remove(playerId);
                    }

                    continue;
                }

                character.SetSelectingRestSiteOption(null);
                character.ShowSelectedRestSiteOption(choice.Option);

                lock (_lock)
                {
                    _pending.Remove(playerId);
                }

                LocalMultiControlLogger.Info(
                    $"第三方席位休息区气泡已补画: source={source}, player={playerId}, option={choice.Option.OptionId}");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"补画第三方席位休息区气泡失败: source={source}, error={exception.Message}");
        }
    }
}
