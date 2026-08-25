using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库角色非共享事件自动选择（可行性分析·需求三 b）：
/// 直接操作瓦库玩家自己的 EventModel 实例，逐页选第一个可用选项（最上），
/// 与 UI 点击最终执行的 option.Chosen() 同一落点，绕开消息层避免回环双执行。
///
/// 中止规则（拍板 #7：复杂事件停住等真人）：
/// - 共享事件一律不碰（EventModel.IsShared / EventSynchronizer.IsShared 投票制）；
/// - 选项触发战斗（EnteringEventCombat 触发）→ 立即停止；
/// - 选择后出现任何弹层（小游戏等，如水晶球）→ 立即停止；
/// - 会击杀玩家的选项跳过（WillKillPlayer 保护）；
/// - 涅奥（NEOW）默认排除，neowAutoChoose 开关放行；
/// - 水晶球（CRYSTAL_SPHERE）绝对排除。
/// </summary>
internal static class LocalWakuuEventAutoChoice
{
    private const string NeowEventId = "NEOW";
    private const string CrystalSphereEventId = "CRYSTAL_SPHERE";

    /// <summary>正在自动选择的事件归属者（按玩家去重，双瓦库局互不阻塞）。</summary>
    private static readonly HashSet<ulong> _inFlightOwners = new();
    private static readonly object _flightLock = new();

    /// <summary>NEventRoom.RefreshEventState postfix 调用；条件不满足时静默返回。</summary>
    public static void TryBegin(EventModel eventModel)
    {
        try
        {
            if (!LocalSelfCoopContext.IsEnabled
                || !LocalWakuuAutopilotConfig.AutoChooseEvents
                || !RunManager.Instance.IsInProgress
                || RunManager.Instance.EventSynchronizer.IsShared
                || eventModel.Owner == null
                || eventModel.IsFinished
                || eventModel.IsShared
                || !LocalWakuuRelicRuntime.IsVakuuFormMode(eventModel.Owner)
                || !IsEventAllowed(eventModel))
            {
                return;
            }

            // 防重入：Chosen() 会触发 StateChanged → RefreshEventState → 本入口再次被调；
            // 按归属者去重（双瓦库局两个事件可并行各选各的）
            ulong ownerId = eventModel.Owner.NetId;
            lock (_flightLock)
            {
                if (!_inFlightOwners.Add(ownerId))
                {
                    return;
                }
            }

            LocalMultiControlLogger.Info($"瓦库事件自动选择启动: player={ownerId}, event={eventModel.Id.Entry}");
            TaskHelper.RunSafely(RunAsync(eventModel, ownerId));
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库事件自动选择启动失败: {exception.Message}");
        }
    }

    private static bool IsEventAllowed(EventModel eventModel)
    {
        string id = eventModel.Id.Entry.ToUpperInvariant();
        if (id == CrystalSphereEventId)
        {
            return false; // 水晶球绝对排除（其完成回调要求 owner==GetMe，软锁高发）
        }

        if (id == NeowEventId && !LocalWakuuAutopilotConfig.NeowAutoChoose)
        {
            return false; // 涅奥默认关（拍板 #3）
        }

        return true;
    }

    private static async Task RunAsync(EventModel eventModel, ulong ownerId)
    {
        try
        {
            int page = 0;
            while (RunManager.Instance.IsInProgress && !eventModel.IsFinished)
            {
                EventOption? option = eventModel.CurrentOptions.FirstOrDefault((o) =>
                    !o.IsLocked && !o.IsProceed && o.WillKillPlayer == null);
                if (option == null)
                {
                    LocalMultiControlLogger.Info(
                        $"瓦库事件无可自动选择的选项，停住等真人处理: event={eventModel.Id.Entry}, page={page}");
                    return;
                }

                bool enteringCombat = false;
                Action combatHandler = () => enteringCombat = true;
                eventModel.EnteringEventCombat += combatHandler;
                try
                {
                    await option.Chosen();
                    page++;
                    LocalMultiControlLogger.Info(
                        $"瓦库事件已自动选最上: event={eventModel.Id.Entry}, page={page}, option={option.TextKey}");
                }
                finally
                {
                    eventModel.EnteringEventCombat -= combatHandler;
                }

                if (enteringCombat)
                {
                    LocalMultiControlLogger.Info(
                        $"瓦库事件选项触发战斗，停止自动选择: event={eventModel.Id.Entry}");
                    return;
                }

                // 小游戏/奖励弹层出现即停（水晶球类自定义事件的兜底）
                if (NOverlayStack.Instance?.ScreenCount > 0)
                {
                    LocalMultiControlLogger.Info(
                        $"瓦库事件选择后出现弹层，停住等真人处理: event={eventModel.Id.Entry}");
                    return;
                }

                await Task.Delay(250);
            }

            LocalMultiControlLogger.Info($"瓦库事件自动选择完成: event={eventModel.Id.Entry}, pages={page}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"瓦库事件自动选择异常，剩余部分交还真人: event={eventModel.Id.Entry}, error={exception.Message}");
        }
        finally
        {
            lock (_flightLock)
            {
                _inFlightOwners.Remove(ownerId);
            }
        }
    }
}
