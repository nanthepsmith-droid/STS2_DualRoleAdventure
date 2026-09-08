using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Rewards;

/// <summary>
/// 标记当前是否处于战利品汇总流程中。
/// 当处于汇总流程时，遗物/药水/金币的镜像复制应被抑制，
/// 因为每个角色已经独立生成了自己的奖励。
/// 使用静态计数器而非 AsyncLocal，确保在 UI 回调中也能正确读取。
/// </summary>
internal static class CombatRewardMergeContext
{
    private sealed class MergeMarker
    {
    }

    private static int _depth;
    private static readonly ConditionalWeakTable<AbstractRoom, MergeMarker> MergedRooms = new();

    internal static bool IsActive => _depth > 0;

    internal static bool TryMarkRoomMerged(AbstractRoom room)
    {
        lock (MergedRooms)
        {
            if (MergedRooms.TryGetValue(room, out _))
            {
                return false;
            }

            MergedRooms.Add(room, new MergeMarker());
            return true;
        }
    }

    internal static void Enter()
    {
        _depth++;
    }

    internal static void Exit()
    {
        if (_depth > 0)
        {
            _depth--;
        }
    }

    /// <summary>
    /// 把合并奖励的「展示用 RewardsSet」登记到 RewardsSetSynchronizer。
    ///
    /// 背景：原版 RewardsSet.Offer() 内部会先调 RewardsSetSynchronizer.BeginRewardsSet(this)，
    /// 由它分配 Id 并压入该玩家的奖励栈。本 mod 的合并奖励流程接管了 Offer，
    /// 用 new RewardsSet(displayPlayer).WithCustomRewards(...) 自建展示 set，漏了这一步 →
    /// displaySet.Id 停在默认值 -1。而 NRewardsScreen.UpdateScreenState 在奖励按钮清空时会校验
    /// IsRewardsSetCompleted(_rewardsSet)，该方法对 Id &lt; 0 一律返回 false，于是每次合并奖励屏
    /// 退出都刷一条 "All rewards have been taken, but the rewards set is not complete on the backend!"
    /// （2026-09-05 双人局实测 11 次）。功能上无碍（terminal 屏照常 Completed、流程继续），
    /// 但每场战斗一条 ERROR 会淹掉真正的错误。
    /// </summary>
    /// <summary>已登记但尚未在后端完成的展示集（真人领完最后一张时据此即时完成）。</summary>
    private static readonly List<RewardsSet> _displaySets = new();

    private static readonly object _displaySetsLock = new();

    internal static void BeginDisplaySet(RewardsSet displaySet)
    {
        try
        {
            RewardsSetSynchronizer? synchronizer = RunManager.Instance.RewardsSetSynchronizer;
            if (synchronizer == null)
            {
                return;
            }

            // 返回的 Task 是该 set 的完成源，合并流程改为等 NRewardsScreen 的 Completed 信号，
            // 这里刻意不 await（await 会在真人领完前卡住本方法）。
            _ = synchronizer.BeginRewardsSet(displaySet);
            lock (_displaySetsLock)
            {
                if (!_displaySets.Contains(displaySet))
                {
                    _displaySets.Add(displaySet);
                }
            }

            LocalMultiControlLogger.Info(
                $"合并奖励展示集已登记: player={displaySet.Player.NetId}, setId={displaySet.Id}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"合并奖励展示集登记失败（忽略）: {exception.Message}");
        }
    }

    /// <summary>
    /// 真人领走合并屏的某个奖励后调用：若该展示集的奖励已全部领完，立刻在后端标记完成——
    /// 这样 NRewardsScreen 在「最后一张按钮被领走」（RewardCollectedFrom → UpdateScreenState）
    /// 那一刻就能看到 set 已完成，不再误报 "All rewards have been taken..."。
    /// 语义与原版一致（原版 SelectLocalReward → SelectRewardForPlayer 会在每次领取后
    /// CompleteRewardsSetIfNecessary）；本 mod 的合并领奖走 SelectUnsynchronized，少了这一步，故在此补齐。
    /// </summary>
    internal static void OnRewardClaimed(Reward reward)
    {
        if (reward == null)
        {
            return;
        }

        RewardsSet? displaySet = FindDisplaySetOf(reward);
        if (displaySet == null)
        {
            return;
        }

        if (displaySet.AllRewardsSuccessfullySelected)
        {
            CompleteDisplaySet(displaySet, "merged-rewards-claim-completed");
        }
    }

    /// <summary>
    /// 展示界面走完后（或领完最后一张奖励后），把该展示集在后端标记为已完成。
    ///
    /// 为什么用 SkipLocalRewardsSet：把 set 标记为已完成的 CompleteRewardsSet 是私有方法，
    /// 公开面里只有 SkipLocalRewardsSet() / BeforeLeavingRoom() 能做到；前者只动栈顶一个 set，
    /// 语义等价于原版「离房时跳过剩余奖励」（未领的奖励走 OnSkipped，与原版一致）。
    /// 它发出的 RewardSetSkippedMessage 在本 mod 的回环网络里不会回投给本机
    /// （LocalLoopbackHostGameService.SendMessage 只记日志、不分发给处理器），因此不会二次结算。
    ///
    /// 若真人在领奖期间切过角色（同步器本地玩家 ≠ 展示集归属），会把本地玩家临时对齐到归属者
    /// 再跳过栈顶（该 set 刚压栈、房间内不会插入别的 set，栈顶就是它），完成后恢复原值。
    /// </summary>
    internal static void CompleteDisplaySet(RewardsSet displaySet, string source)
    {
        RemoveFromRegistry(displaySet);
        try
        {
            RewardsSetSynchronizer? synchronizer = RunManager.Instance.RewardsSetSynchronizer;
            if (synchronizer == null || synchronizer.IsRewardsSetCompleted(displaySet))
            {
                return;
            }

            ulong? localPlayerId = TryGetLocalPlayerId(synchronizer);
            bool needAlign = localPlayerId != displaySet.Player.NetId;
            if (needAlign)
            {
                LocalMultiControlLogger.Warn(
                    $"合并奖励展示集完成时同步器本地玩家与展示集归属不一致，临时对齐后完成: "
                    + $"setId={displaySet.Id}, owner={displaySet.Player.NetId}, "
                    + $"syncLocal={localPlayerId?.ToString() ?? "?"}, source={source}");
            }

            ulong? previousLocalId = localPlayerId;
            if (needAlign)
            {
                SetLocalPlayerId(synchronizer, displaySet.Player.NetId);
            }

            try
            {
                synchronizer.SkipLocalRewardsSet();
            }
            finally
            {
                if (needAlign && previousLocalId.HasValue)
                {
                    SetLocalPlayerId(synchronizer, previousLocalId.Value);
                }
            }

            if (!synchronizer.IsRewardsSetCompleted(displaySet))
            {
                LocalMultiControlLogger.Warn(
                    $"合并奖励展示集标记完成后校验仍未通过: setId={displaySet.Id}, source={source}");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"合并奖励展示集标记完成失败（忽略）: {exception.Message}");
        }
    }

    private static RewardsSet? FindDisplaySetOf(Reward reward)
    {
        lock (_displaySetsLock)
        {
            return _displaySets.FirstOrDefault((set) => set != null && set.Rewards != null && set.Rewards.Contains(reward));
        }
    }

    private static void RemoveFromRegistry(RewardsSet displaySet)
    {
        lock (_displaySetsLock)
        {
            _displaySets.Remove(displaySet);
        }
    }

    /// <summary>读同步器的私有「本地玩家」字段（RewardsSetSynchronizer.LocalPlayer 是私有的）。</summary>
    private static ulong? TryGetLocalPlayerId(RewardsSetSynchronizer synchronizer)
    {
        try
        {
            object? value = AccessTools.Field(typeof(RewardsSetSynchronizer), "_localPlayerId")?.GetValue(synchronizer);
            return value is ulong id ? id : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SetLocalPlayerId(RewardsSetSynchronizer synchronizer, ulong playerId)
    {
        try
        {
            AccessTools.Field(typeof(RewardsSetSynchronizer), "_localPlayerId")?.SetValue(synchronizer, playerId);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"合并奖励展示集对齐同步器本地玩家失败(忽略): {exception.Message}");
        }
    }
}
