using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库遇到商人时自动投掷污浊药水（2026-08-25 追加需求，随 autoUsePotions 开关生效）。
/// 污浊药水在战斗中会伤害全场（含自己），绝不在战斗中自动使用；
/// 在商店对商人投掷可换 100 金币，压栏不如变现。
///
/// 实现：
/// - 由 MerchantRoomEnterFoulThrowPatch（MerchantRoom.EnterInternal postfix）触发；
/// - 等待商店界面就绪后（复用游戏自己的可用性判定 FoulPotion.GetFoulPotionMerchantTarget，
///   要求商人按钮存在且商店库存未展开），经官方 EnqueueManualUse(null) 入队投掷——
///   战斗外该药水 TargetType=TargetedNoCreature，目标保持空，OnUse 走商人分支（+100 金币）；
/// - 已投掷过的药水实例记录去重（同一瓶只投一次）；多瓦库局各投各的。
/// </summary>
internal static class LocalWakuuMerchantFoulThrow
{
    /// <summary>等待商店界面就绪的超时。</summary>
    private const int MerchantReadyTimeoutMs = 8000;

    /// <summary>两次投掷之间的间隔，让队列串行结算。</summary>
    private const int ThrowIntervalMs = 600;

    /// <summary>已投掷的污浊药水实例（按引用去重，容量以局内污浊药水数为上界）。</summary>
    private static readonly HashSet<object> _thrownFoulPotions = new();

    private static readonly HashSet<ulong> _inFlightOwners = new();
    private static readonly object _flightLock = new();

    /// <summary>由 MerchantRoomEnterFoulThrowPatch 调用。</summary>
    public static void OnMerchantRoomEntered()
    {
        try
        {
            if (!LocalSelfCoopContext.IsEnabled
                || !LocalWakuuAutopilotConfig.AutoUsePotions
                || !RunManager.Instance.IsInProgress)
            {
                return;
            }

            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            if (runState?.Players == null || runState.Players.Count <= 1)
            {
                return;
            }

            foreach (Player player in runState.Players.ToList())
            {
                if (player == null
                    || !LocalWakuuRelicRuntime.IsVakuuFormMode(player)
                    || !player.Potions.OfType<FoulPotion>().Any())
                {
                    continue;
                }

                TryBeginFor(player);
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"扫描瓦库污浊药水失败: {exception.Message}");
        }
    }

    private static void TryBeginFor(Player player)
    {
        ulong ownerId = player.NetId;
        lock (_flightLock)
        {
            if (!_inFlightOwners.Add(ownerId))
            {
                return;
            }
        }

        LocalMultiControlLogger.Info($"瓦库商人污浊药水自动投掷启动: player={ownerId}");
        TaskHelper.RunSafely(RunAsync(player, ownerId));
    }

    private static async Task RunAsync(Player player, ulong ownerId)
    {
        try
        {
            // 等商店界面就绪：房间切换完成 + 商人按钮可点（库存未展开）
            int waitedMs = 0;
            while (RunManager.Instance.IsInProgress
                   && !IsMerchantReady(player)
                   && waitedMs < MerchantReadyTimeoutMs)
            {
                await Task.Delay(200);
                waitedMs += 200;
            }

            if (!IsMerchantReady(player))
            {
                LocalMultiControlLogger.Info(
                    $"瓦库商人界面未就绪（超时），本次不投掷: player={ownerId}, waitedMs={waitedMs}");
                return;
            }

            foreach (FoulPotion foul in player.Potions.OfType<FoulPotion>().ToList())
            {
                if (!RunManager.Instance.IsInProgress)
                {
                    return;
                }

                lock (_flightLock)
                {
                    if (!_thrownFoulPotions.Add(foul))
                    {
                        continue;
                    }
                }

                if (!player.Potions.Contains(foul))
                {
                    continue; // 等待期间已被处理（丢弃/喝掉等极端情况）
                }

                try
                {
                    // 目标传 null：战斗外 TargetedNoCreature 使 OnUse 走"向商人投掷"+100 金币分支
                    foul.EnqueueManualUse(null);
                    LocalMultiControlLogger.Info($"瓦库已自动向商人投掷污浊药水: player={ownerId}, potion={foul.Id.Entry}");
                }
                catch (Exception exception)
                {
                    LocalMultiControlLogger.Warn(
                        $"瓦库投掷污浊药水失败（保留在栏上）: player={ownerId}, error={exception.Message}");
                }

                await Task.Delay(ThrowIntervalMs);
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库商人污浊药水流程异常: player={ownerId}, error={exception.Message}");
        }
        finally
        {
            lock (_flightLock)
            {
                _inFlightOwners.Remove(ownerId);
            }
        }
    }

    /// <summary>复刻游戏可用性判定：当前在商店房且商人按钮存在、库存未展开。</summary>
    private static bool IsMerchantReady(Player player)
    {
        try
        {
            AbstractRoom? room = player.RunState.CurrentRoom;
            if (room is not MerchantRoom)
            {
                return false;
            }

            return FoulPotion.GetFoulPotionMerchantTarget(room).button != null;
        }
        catch
        {
            return false;
        }
    }
}
