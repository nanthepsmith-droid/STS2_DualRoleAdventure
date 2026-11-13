using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.UI;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.Dread;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 恐惧模式（Dread）大厅页的本地多角色适配（r351）。
///
/// 与每日挑战 / 自定义页同构：入口（<see cref="LocalDreadSelfCoopEntry"/>）自己用回环服务建厅，
/// 页面上的本地席位由 <see cref="LocalLobbySeatReconciler"/> 补齐；角色由真人逐席位点选
/// （先点席位卡上的切人按钮 / 人数面板的 ◀▶，再点角色）。
///
/// ⚠ Dread 的三处模式特性（不要当成 bug 去"修"）：
/// ① 多人局**没有**随机角色与轮换角色按钮（`InitRotatingCharacterButton` 多人直接 `Visible=false`），
///    而 `StartRunLobby.BeginRunLocally` 对 Dread + 随机/轮换角色会直接抛 ⇒ 本地多控天然安全；
/// ② 多人局进阶强制 10（`AddLocalHostPlayer(..., 10, ...)`），我们补的席位同样给 10；
/// ③ 轮换连胜`isRotatingDread` 只在单人勾选，多人恒为 false ⇒ 各席位的 `ExtraFields` 互不影响。
/// </summary>
[HarmonyPatch(typeof(NDreadScreen), nameof(NDreadScreen.OnSubmenuOpened))]
internal static class NDreadScreenLocalSeatsOpenPatch
{
    [HarmonyPostfix]
    private static void Postfix(NDreadScreen __instance)
    {
        NDreadLocalSeatSync.ResetHighlight();
        NDreadLocalSeatSync.TryReconcile(__instance);
        LocalDreadRunCountButtons.Sync(__instance);
    }
}

[HarmonyPatch(typeof(NDreadScreen), nameof(NDreadScreen._Process))]
internal static class NDreadScreenLocalSeatsProcessPatch
{
    [HarmonyPostfix]
    private static void Postfix(NDreadScreen __instance)
    {
        NDreadLocalSeatSync.TryReconcile(__instance);
        NDreadLocalSeatSync.TrySyncHighlight(__instance);
        LocalDreadRunCountButtons.Sync(__instance);
    }
}

/// <summary>
/// 出征前的最后校正：`OnEmbarkPressed` 会对「当前 sender」`SetReady(true)`，且 `BeginRun` 用
/// `_lobby.LocalPlayer` 决定谁是"我"。所以先把 sender 收回主席位，避免把 ready / 偏好写到别的席位上。
/// </summary>
[HarmonyPatch(typeof(NDreadScreen), "OnEmbarkPressed")]
internal static class NDreadEmbarkGuardPatch
{
    [HarmonyPrefix]
    private static void Prefix(NDreadScreen __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled
            || __instance.Lobby?.NetService is not LocalLoopbackHostGameService loopbackService)
        {
            return;
        }

        NDreadLocalSeatSync.TryReconcile(__instance);
        loopbackService.SetCurrentSenderId(LocalSelfCoopContext.PrimaryPlayerId);
        LocalContext.NetId = LocalSelfCoopContext.PrimaryPlayerId;
        LocalMultiControlLogger.Info($"恐惧模式开始前强制校正 sender 到主角色: {LocalSelfCoopContext.PrimaryPlayerId}");
    }
}

[HarmonyPatch(typeof(NDreadScreen), nameof(NDreadScreen.OnSubmenuClosed))]
internal static class NDreadScreenLocalSeatsClosePatch
{
    [HarmonyPrefix]
    private static void Prefix(NDreadScreen __instance)
    {
        NDreadLocalSeatSync.ResetHighlight();
        LocalDreadRunCountButtons.Remove(__instance);
        LocalSelfCoopContext.ResetLobbyLocalPlayerLimit("dread-closed");
    }
}

internal static class NDreadLocalSeatSync
{
    private static readonly System.Reflection.FieldInfo? CharButtonContainerField =
        AccessTools.Field(typeof(NDreadScreen), "_charButtonContainer");

    private static readonly System.Reflection.FieldInfo? SelectedButtonField =
        AccessTools.Field(typeof(NDreadScreen), "_selectedButton");

    private static readonly LocalLobbyCharacterHighlightState HighlightState = new();

    private static bool _isReconciling;

    internal static void ResetHighlight() => HighlightState.Reset();

    internal static void TryReconcile(NDreadScreen screen)
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
                logPrefix: "恐惧模式",
                senderContextSource: "dread-lobby-opened");
        }
        finally
        {
            _isReconciling = false;
        }
    }

    internal static void TrySyncHighlight(NDreadScreen screen)
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

/// <summary>
/// 恐惧页的人数面板（骨架与每日 / 自定义页共用 <see cref="LocalPlayerCountPanel"/>）。
/// 落点取右下（与自定义页一致）；左侧是角色按钮行、右侧是连胜/分数区。
/// </summary>
internal static class LocalDreadRunCountButtons
{
    private static readonly LocalPlayerCountPanel Panel = new(new LocalPlayerCountPanelOptions(
        namePrefix: "LocalDread",
        sourcePrefix: "dread-ui-button",
        successLogText: "通过恐惧模式实体按钮调整本地人数成功",
        resolvePosition: LocalPlayerCountPanelOptions.BottomRight)
    {
        FirstLayoutLog = LogFirstLayout
    });

    internal static void Sync(NDreadScreen screen)
    {
        // 只在「本地多控开启 + 本页大厅确实是我们自己的回环服务」时才挂面板：
        // 官方联机恐惧页是同一个类型，残留会话会让它也冒出我们的人数面板。
        bool shouldShow = LocalSelfCoopContext.IsEnabled
            && screen.Lobby?.NetService is LocalLoopbackHostGameService;
        Panel.Sync(screen, shouldShow);
    }

    internal static void Remove(NDreadScreen screen)
    {
        Panel.Remove(screen);
    }

    private static void LogFirstLayout(Vector2 viewportSize, Vector2 position, Vector2 panelSize)
    {
        LocalMultiControlLogger.Info(
            $"恐惧模式人数面板已注入: viewport={viewportSize.X:0}x{viewportSize.Y:0}, "
            + $"position={position.X:0},{position.Y:0}, panel={panelSize.X:0}x{panelSize.Y:0}");
    }
}
