using System;
using System.Collections.Generic;
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
/// 直接操作瓦库玩家自己的 EventModel 实例，逐页按配置策略（first/last/random）选一个可用选项，
/// 与 UI 点击最终执行的 option.Chosen() 同一落点，绕开消息层避免回环双执行。
///
/// 送死保护（复刻原版联机限制，见 NEventOptionButton.OnRelease）：
/// 多人局中点击"当前血量下会死"的选项会被游戏硬拦截（角色弹拒绝气泡），
/// 我们直调 Chosen() 会绕过该拦截，因此这里自行评估
/// option.WillKillPlayer?.Invoke(owner)——为 true 的选项视为禁止项跳过；
/// 若一页全是禁止项则停住交还真人（拍板 #7）。
///
/// 其余中止规则（拍板 #7：复杂事件停住等真人）：
/// - 共享事件一律不碰（EventModel.IsShared / EventSynchronizer.IsShared 投票制）；
/// - 选项触发战斗（EnteringEventCombat 触发）→ 立即停止；
/// - 选择后出现任何弹层（小游戏等，如水晶球）→ 立即停止；
/// - 涅奥（NEOW）默认排除，neowAutoChoose 开关放行；
/// - 水晶球（CRYSTAL_SPHERE）绝对排除。
/// </summary>
internal static class LocalWakuuEventAutoChoice
{
    private const string NeowEventId = "NEOW";
    private const string CrystalSphereEventId = "CRYSTAL_SPHERE";

    /// <summary>首页选项就绪等待上限（毫秒）。</summary>
    private const int OptionsReadyTimeoutMs = 5000;

    /// <summary>正在自动选择的事件归属者（按玩家去重，双瓦库局互不阻塞）。</summary>
    private static readonly HashSet<ulong> _inFlightOwners = new();
    private static readonly object _flightLock = new();

    /// <summary>random 策略用的独立随机源：不动游戏 RunState RNG，避免污染局内随机序列。</summary>
    private static readonly Random _random = new();
    private static readonly object _randomLock = new();

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

            LocalMultiControlLogger.Info(
                $"瓦库事件自动选择启动: player={ownerId}, event={eventModel.Id.Entry}, strategy={LocalWakuuAutopilotConfig.EventChoiceMode}");
            TaskHelper.RunSafely(RunAsync(eventModel, ownerId));
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库事件自动选择启动失败: {exception.Message}");
        }
    }

    /// <summary>
    /// 扫描本局所有玩家事件，对满足条件的（瓦库角色、非共享、允许列表）启动自动选择。
    /// 由 EventSynchronizerBeginEventPatch 在进事件房时调用。
    /// </summary>
    public static void TryBeginPendingEvents()
    {
        if (!LocalSelfCoopContext.IsEnabled
            || !LocalWakuuAutopilotConfig.AutoChooseEvents
            || !RunManager.Instance.IsInProgress
            || RunManager.Instance.EventSynchronizer.IsShared)
        {
            return;
        }

        foreach (EventModel candidate in RunManager.Instance.EventSynchronizer.Events)
        {
            TryBegin(candidate);
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

    private static bool HasSelectableOption(EventModel eventModel)
    {
        return eventModel.CurrentOptions.Any((o) => !o.IsLocked);
    }

    /// <summary>
    /// 复刻原版多人局的送死拦截判定（NEventOptionButton.OnRelease）：
    /// 选项标记了 WillKillPlayer 且对当前归属者评估为 true = 现在选就会死，禁止选择。
    /// </summary>
    private static bool WouldKillOwnerNow(EventModel eventModel, EventOption option)
    {
        if (option.WillKillPlayer == null || eventModel.Owner == null)
        {
            return false;
        }

        try
        {
            return option.WillKillPlayer(eventModel.Owner);
        }
        catch (Exception exception)
        {
            // 判定委托自身异常时保守处理：视作会死，宁可不选
            LocalMultiControlLogger.Warn(
                $"评估事件选项致死条件异常，保守跳过: event={eventModel.Id.Entry}, option={option.TextKey}, error={exception.Message}");
            return true;
        }
    }

    /// <summary>按策略从候选里挑一个：first=第一个 / last=最后一个 / random=随机（逻辑抽为纯函数）。</summary>
    private static EventOption? SelectByStrategy(IReadOnlyList<EventOption> candidates)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        int index;
        lock (_randomLock)
        {
            index = WakuuStrategyPicking.PickIndexByStrategy(
                candidates.Count, LocalWakuuAutopilotConfig.EventChoiceMode, _random);
        }

        return index >= 0 ? candidates[index] : null;
    }

    private static async Task RunAsync(EventModel eventModel, ulong ownerId)
    {
        try
        {
            int page = 0;
            while (RunManager.Instance.IsInProgress && !eventModel.IsFinished)
            {
                // 首页需等待事件模型异步初始化（EventSynchronizer.BeginEvent 内部
                // 对 BeginEvent 是 fire-and-forget，CurrentOptions 稍后才填充）
                if (page == 0)
                {
                    int waitedMs = 0;
                    while (RunManager.Instance.IsInProgress
                           && !eventModel.IsFinished
                           && !HasSelectableOption(eventModel)
                           && waitedMs < OptionsReadyTimeoutMs)
                    {
                        await Task.Delay(150);
                        waitedMs += 150;
                    }
                }

                List<EventOption> candidates = eventModel.CurrentOptions
                    .Where((o) => !o.IsLocked && !o.IsProceed)
                    .ToList();
                if (candidates.Count == 0)
                {
                    LocalMultiControlLogger.Info(
                        $"瓦库事件无可自动选择的选项，停住等真人处理: event={eventModel.Id.Entry}, page={page}");
                    return;
                }

                // 送死保护：剔除"现在选就会死"的选项；若整页都是死路则停住等真人
                List<EventOption> safeCandidates = candidates
                    .Where((o) => !WouldKillOwnerNow(eventModel, o))
                    .ToList();
                if (safeCandidates.Count == 0)
                {
                    LocalMultiControlLogger.Info(
                        $"瓦库事件当前页全部选项都会致死（联机死亡保护），停住等真人处理: "
                        + $"event={eventModel.Id.Entry}, page={page}, options={candidates.Count}");
                    return;
                }

                EventOption? option = SelectByStrategy(safeCandidates);
                if (option == null)
                {
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
                        $"瓦库事件已自动选择: event={eventModel.Id.Entry}, page={page}, "
                        + $"strategy={LocalWakuuAutopilotConfig.EventChoiceMode}, option={option.TextKey}");
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
