using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.UI;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.Blitz;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 闪电战（Blitz）大厅页的本地多角色适配（r351）。
///
/// 与恐惧页同构（入口 <see cref="LocalBlitzSelfCoopEntry"/> + 席位 reconcile + 人数面板 + 高亮同步），
/// 三处模式特性：
/// ① 进阶是**跨角色共享**的（`maxBlitzAscension`，`MaxAscension` 取全队最小值）—— 我们补的席位给 10，
///    不会把房主已解锁的进阶压低；
/// ② 随机角色在 Blitz 是合法玩法（`BeginRunLocally` 会把它 roll 成具体角色），所以**不禁用**随机按钮；
/// ③ 商店是另一套（`MerchantInventory.CreateForBlitzMerchant`，每人 3 次选择）——
///    切席位后换库存界面由 `LocalMerchantInventoryRuntime.RefreshShopRoomForPlayer` 负责。
/// </summary>
[HarmonyPatch(typeof(NBlitzScreen), nameof(NBlitzScreen.OnSubmenuOpened))]
internal static class NBlitzScreenLocalSeatsOpenPatch
{
    [HarmonyPostfix]
    private static void Postfix(NBlitzScreen __instance)
    {
        NBlitzLocalSeatSync.ResetHighlight();
        NBlitzLocalSeatSync.TryReconcile(__instance);
        LocalBlitzRunCountButtons.Sync(__instance);
    }
}

[HarmonyPatch(typeof(NBlitzScreen), nameof(NBlitzScreen._Process))]
internal static class NBlitzScreenLocalSeatsProcessPatch
{
    [HarmonyPostfix]
    private static void Postfix(NBlitzScreen __instance)
    {
        NBlitzLocalSeatSync.TryReconcile(__instance);
        NBlitzLocalSeatSync.TrySyncHighlight(__instance);
        LocalBlitzRunCountButtons.Sync(__instance);
    }
}

[HarmonyPatch(typeof(NBlitzScreen), "OnEmbarkPressed")]
internal static class NBlitzEmbarkGuardPatch
{
    [HarmonyPrefix]
    private static void Prefix(NBlitzScreen __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled
            || __instance.Lobby?.NetService is not LocalLoopbackHostGameService loopbackService)
        {
            return;
        }

        NBlitzLocalSeatSync.TryReconcile(__instance);
        loopbackService.SetCurrentSenderId(LocalSelfCoopContext.PrimaryPlayerId);
        LocalContext.NetId = LocalSelfCoopContext.PrimaryPlayerId;
        LocalMultiControlLogger.Info($"闪电战开始前强制校正 sender 到主角色: {LocalSelfCoopContext.PrimaryPlayerId}");
    }
}

[HarmonyPatch(typeof(NBlitzScreen), nameof(NBlitzScreen.OnSubmenuClosed))]
internal static class NBlitzScreenLocalSeatsClosePatch
{
    [HarmonyPrefix]
    private static void Prefix(NBlitzScreen __instance)
    {
        NBlitzLocalSeatSync.ResetHighlight();
        LocalBlitzRunCountButtons.Remove(__instance);
        LocalSelfCoopContext.ResetLobbyLocalPlayerLimit("blitz-closed");
    }
}

internal static class NBlitzLocalSeatSync
{
    private static readonly System.Reflection.FieldInfo? CharButtonContainerField =
        AccessTools.Field(typeof(NBlitzScreen), "_charButtonContainer");

    private static readonly System.Reflection.FieldInfo? SelectedButtonField =
        AccessTools.Field(typeof(NBlitzScreen), "_selectedButton");

    private static readonly LocalLobbyCharacterHighlightState HighlightState = new();

    private static bool _isReconciling;

    internal static void ResetHighlight() => HighlightState.Reset();

    internal static void TryReconcile(NBlitzScreen screen)
    {
        if (_isReconciling || !LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        // 判空（坑 I）：页面的 `_lobby` 在「未建厅 / 已被清理」时就是 null，而 `_Process` 每帧都会进来。
        StartRunLobby? lobby = screen.Lobby;
        if (lobby?.NetService is not LocalLoopbackHostGameService loopbackService)
        {
            return;
        }

        List<ulong> targetPlayerIds = LocalLobbySeatPolicy.ResolveTargetSeats(
            LocalSelfCoopContext.LocalPlayerIds,
            LocalSelfCoopContext.DesiredLocalPlayerCount,
            LocalSelfCoopContext.MaxLocalPlayerCount);
        if (targetPlayerIds.Count <= 1)
        {
            return;
        }

        List<ulong> lobbySeatIds = lobby.Players.Select(player => player.id).ToList();
        if (!LocalLobbySeatPolicy.NeedsReconcile(lobbySeatIds, targetPlayerIds, LocalSelfCoopContext.LocalPlayerIds))
        {
            return;
        }

        _isReconciling = true;
        try
        {
            LocalLobbySeatReconciler.Reconcile(
                lobby,
                loopbackService,
                screen,
                targetPlayerIds,
                logPrefix: "闪电战",
                senderContextSource: "blitz-lobby-opened");
        }
        finally
        {
            _isReconciling = false;
        }
    }

    internal static void TrySyncHighlight(NBlitzScreen screen)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        StartRunLobby? lobby = screen.Lobby;
        if (lobby?.NetService is not LocalLoopbackHostGameService)
        {
            return;
        }

        HighlightState.TrySync(
            screen,
            CharButtonContainerField?.GetValue(screen) as Control,
            lobby,
            SelectedButtonField);
    }
}

/// <summary>闪电战页的人数面板（骨架与每日 / 自定义页共用 <see cref="LocalPlayerCountPanel"/>）。</summary>
internal static class LocalBlitzRunCountButtons
{
    private static readonly LocalPlayerCountPanel Panel = new(new LocalPlayerCountPanelOptions(
        namePrefix: "LocalBlitz",
        sourcePrefix: "blitz-ui-button",
        successLogText: "通过闪电战实体按钮调整本地人数成功",
        resolvePosition: LocalPlayerCountPanelOptions.BottomRight)
    {
        FirstLayoutLog = LogFirstLayout
    });

    internal static void Sync(NBlitzScreen screen)
    {
        bool shouldShow = LocalSelfCoopContext.IsEnabled
            && screen.Lobby?.NetService is LocalLoopbackHostGameService;
        Panel.Sync(screen, shouldShow);
    }

    internal static void Remove(NBlitzScreen screen)
    {
        Panel.Remove(screen);
    }

    private static void LogFirstLayout(Vector2 viewportSize, Vector2 position, Vector2 panelSize)
    {
        LocalMultiControlLogger.Info(
            $"闪电战人数面板已注入: viewport={viewportSize.X:0}x{viewportSize.Y:0}, "
            + $"position={position.X:0},{position.Y:0}, panel={panelSize.X:0}x{panelSize.Y:0}");
    }
}
