using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;

namespace LocalMultiControl.Scripts.Runtime;

internal static class CrystalSphereMirrorRuntime
{
    private const string CrystalSphereEventId = "CRYSTAL_SPHERE";

    /// <summary>
    /// 水晶球跨角色镜像总开关（金币/遗物/药水/卡牌/诅咒债）。
    /// 现版本每个角色拥有独立的水晶球事件实例并各自结算：选择"揭幕未来/分期付款"时
    /// 只扣自己的钱、占卜奖励只归自己，因此旧版"一人付费全员扣款、一人拾取全员复制"
    /// 的镜像逻辑全部停用。如需恢复共享式体验，改回 true。
    /// </summary>
    internal static bool CrossPlayerMirroringEnabled => false;

    /// <summary>
    /// 是否存在仍在进行中的占卜小游戏弹层（占卜次数未用完）。
    /// 进行中的占卜绝不允许切换角色：切走后 LocalContext 归属他人，
    /// 完成回调 DoLocalCrystalSphereRewards 会因 owner 非当前本地玩家而抛异常，
    /// 事件永远无法完成（软锁）。
    /// </summary>
    public static bool HasActiveDivinationOverlay()
    {
        NOverlayStack? overlayStack = NOverlayStack.Instance;
        if (overlayStack == null || !LocalSelfCoopContext.IsEnabled)
        {
            return false;
        }

        return overlayStack.GetChildren()
            .OfType<NCrystalSphereScreen>()
            .Any((screen) => !IsMinigameFinished(screen));
    }

    public static bool IsInCrystalSphereEventContext(Player? player)
    {
        if (player?.RunState == null)
        {
            return false;
        }

        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode || !RunManager.Instance.IsInProgress)
        {
            return false;
        }

        if (player.RunState.CurrentRoom is not EventRoom)
        {
            return false;
        }

        if (RunManager.Instance.EventSynchronizer.IsShared)
        {
            return false;
        }

        try
        {
            MegaCrit.Sts2.Core.Models.EventModel eventForPlayer = RunManager.Instance.EventSynchronizer.GetEventForPlayer(player);
            return eventForPlayer.Id.Entry == CrystalSphereEventId;
        }
        catch
        {
            return false;
        }
    }

    public static List<Player> GetOtherPlayers(Player sourcePlayer)
    {
        return sourcePlayer.RunState.Players
            .Where((candidate) => candidate.NetId != sourcePlayer.NetId)
            .ToList();
    }

    /// <summary>
    /// 在非共享事件中查找还有水晶球事件未完成的其他角色（排除 excludeNetId）。
    /// </summary>
    public static Player? GetNextPendingCrystalSphereOwner(ulong excludeNetId)
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress)
        {
            return null;
        }

        try
        {
            if (RunManager.Instance.EventSynchronizer.IsShared)
            {
                return null;
            }

            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            if (runState?.CurrentRoom is not EventRoom)
            {
                return null;
            }

            foreach (EventModel candidate in RunManager.Instance.EventSynchronizer.Events)
            {
                if (candidate.Owner == null || candidate.Owner.NetId == excludeNetId || candidate.IsFinished)
                {
                    continue;
                }

                if (candidate.Id.Entry == CrystalSphereEventId)
                {
                    return candidate.Owner;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// 关闭所有"占卜已用完但还挂在弹层栈上"的水晶球小游戏弹层。
    /// 只关闭 entity.IsFinished 的弹层（占卜次数归零 ⇒ 完成源已 SetResult，奖励已发放），
    /// 绝不中断仍在进行中的占卜（那会取消完成源并卡死该角色的事件）。
    /// 返回关闭数量。consumePendingSwitch=true 时同时吃掉待触发的事件自动切换标记，
    /// 避免弹层移除后的自动切换链与调用方自己的切换互相打架。
    /// </summary>
    public static int CloseFinishedMinigameOverlays(string source, bool consumePendingSwitch)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return 0;
        }

        NOverlayStack? overlayStack = NOverlayStack.Instance;
        if (overlayStack == null)
        {
            return 0;
        }

        List<NCrystalSphereScreen> staleScreens = overlayStack.GetChildren()
            .OfType<NCrystalSphereScreen>()
            .Where((screen) => IsMinigameFinished(screen))
            .ToList();
        if (staleScreens.Count == 0)
        {
            return 0;
        }

        if (consumePendingSwitch)
        {
            LocalSelfCoopContext.CancelPendingEventAutoSwitch();
        }

        foreach (NCrystalSphereScreen screen in staleScreens)
        {
            try
            {
                overlayStack.Remove(screen);
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"关闭水晶球占卜弹层失败: source={source}, error={exception.Message}");
            }
        }

        LocalMultiControlLogger.Info(
            $"已关闭已完成的水晶球占卜弹层: count={staleScreens.Count}, source={source}");
        return staleScreens.Count;
    }

    private static bool IsMinigameFinished(NCrystalSphereScreen screen)
    {
        try
        {
            CrystalSphereMinigame? entity =
                AccessTools.Field(typeof(NCrystalSphereScreen), "_entity")?.GetValue(screen) as CrystalSphereMinigame;
            return entity?.IsFinished ?? false;
        }
        catch
        {
            return false;
        }
    }
}
