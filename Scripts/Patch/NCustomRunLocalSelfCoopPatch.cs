using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.UI;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 本地多角色「自定义模式」入口（r158）。
///
/// ⚠ 设计修正（2026-09-27 用户反馈「装了我们这个 mod 就不能玩原版联机自定模式」）：
/// **不再劫持官方 `NMultiplayerHostSubmenu.StartHost(GameMode.Custom)`** —— 官方自定义多人入口还给玩家；
/// 本机多角色改走联机菜单「单人多角色」卡片下方的小按钮（见 `LocalSelfCoopMenuButtons`）调用本入口。
/// 与 Daily 同理：官方入口是玩家的正当用法，mod 只应"并存"，不应"接管"。
/// </summary>
internal static class LocalCustomSelfCoopEntry
{
    internal static void Enter(NMultiplayerHostSubmenu submenu)
    {
        NSubmenuStack? stack = AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(submenu) as NSubmenuStack;
        if (stack == null)
        {
            LocalMultiControlLogger.Warn("无法进入自定义模式：未找到 NSubmenuStack。");
            return;
        }

        LocalMultiControlLogger.Info("单人多角色自定义模式入口：改走本地回环开局。");
        LocalSelfCoopSaveTag.ClearCurrentProfile();
        SaveManager.Instance.DeleteCurrentMultiplayerRun();

        ulong primaryPlayerId = LocalSelfCoopContext.ResolvePrimaryPlayerId();
        LocalSelfCoopContext.UseSavedWakuuPlayerIds(Array.Empty<ulong>());
        LocalSelfCoopSaveTag.MarkCurrentProfile(
            LocalSelfCoopContext.LocalPlayerIds.Take(LocalSelfCoopContext.DesiredLocalPlayerCount).ToList());

        LocalLoopbackHostGameService netService = new(primaryPlayerId);
        LocalSelfCoopContext.Enable(netService);

        NCustomRunScreen customRunScreen = stack.GetSubmenuType<NCustomRunScreen>();
        customRunScreen.InitializeMultiplayerAsHost(netService, LocalSelfCoopContext.MaxLocalPlayerCount);
        stack.Push(customRunScreen);
        // 与每日页同一套会话守卫判据（r161）：页面在（且可见）就不算"没有大厅页面"。
        LocalSelfCoopContext.ActiveSelfCoopLobbyScreen = customRunScreen;
        NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create(LocalModText.EnteredLocalSelfCoopHint));
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.OnSubmenuOpened))]
internal static class NCustomRunScreenLocalPlayersPatch
{
    private const int MaxLocalAscensionLevel = 10;
    private static bool _isReconciling;

    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance)
    {
        TryReconcileLocalPlayers(__instance);
    }

    internal static void TryReconcileLocalPlayers(NCustomRunScreen screen)
    {
        if (_isReconciling)
        {
            return;
        }

        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        // 判空（r159）：Custom 页的 `_lobby` 在「未建厅 / 已被清理」时是 null，
        // 而 `_Process` 每帧都会进来 ⇒ 直接访问 `lobby.NetService` 会每帧抛 NullReferenceException。
        StartRunLobby? lobby = screen.Lobby;
        if (lobby?.NetService is not LocalLoopbackHostGameService loopbackService)
        {
            return;
        }

        EnsureLobbyMaxCapacity(lobby);
        List<ulong> targetPlayerIds = LocalSelfCoopContext.LocalPlayerIds
            .Take(LocalSelfCoopContext.DesiredLocalPlayerCount)
            .ToList();
        if (targetPlayerIds.Count <= 1)
        {
            return;
        }

        bool needsReconcile = targetPlayerIds.Any((id) => lobby.Players.All((player) => player.id != id))
                              || lobby.Players.Any((player) =>
                                  LocalSelfCoopContext.LocalPlayerIds.Contains(player.id) && !targetPlayerIds.Contains(player.id));
        if (!needsReconcile)
        {
            return;
        }

        _isReconciling = true;
        try
        {
            UnlockState unlockState = SaveManager.Instance.GenerateUnlockStateFromProgress();
            SerializableUnlockState serializableUnlockState = unlockState.ToSerializable();

            int added = 0;
            foreach (ulong playerId in targetPlayerIds)
            {
                bool exists = lobby.Players.Any((player) => player.id == playerId);
                if (exists)
                {
                    continue;
                }

                loopbackService.SetCurrentSenderId(playerId);
                _ = lobby.AddLocalHostPlayerInternal(serializableUnlockState, MaxLocalAscensionLevel);
                added++;
            }

            List<ulong> removablePlayerIds = LocalSelfCoopContext.LocalPlayerIds
                .Skip(targetPlayerIds.Count)
                .ToList();
            int removed = 0;
            foreach (ulong removableId in removablePlayerIds)
            {
                int playerIndex = lobby.Players.FindIndex((player) => player.id == removableId);
                if (playerIndex < 0)
                {
                    continue;
                }

                StartRunLobbyPlayer removedPlayer = lobby.Players[playerIndex];
                lobby.Players.RemoveAt(playerIndex);
                lobby.InputSynchronizer.OnPlayerDisconnected(removedPlayer.id);
                screen.RemotePlayerDisconnected(removedPlayer);
                removed++;
            }

            bool readyChanged = false;
            for (int i = 0; i < lobby.Players.Count; i++)
            {
                StartRunLobbyPlayer player = lobby.Players[i];
                if (player.id == LocalSelfCoopContext.PrimaryPlayerId || player.isReady)
                {
                    continue;
                }

                player.isReady = true;
                lobby.Players[i] = player;
                screen.PlayerChanged(player, false);
                readyChanged = true;
            }

            LocalSelfCoopContext.EnsureLobbySenderContext("custom-run-opened");
            LocalMultiControlLogger.Info(
                $"自定义模式大厅本地人数已同步: target={targetPlayerIds.Count}, actual={lobby.Players.Count}, added={added}, removed={removed}, readyChanged={readyChanged}");
        }
        finally
        {
            _isReconciling = false;
        }
    }

    private static void EnsureLobbyMaxCapacity(StartRunLobby lobby)
    {
        // beta111 起 StartRunLobby 的 _maxPlayers 为构造函数中一次性传入的 readonly 字段，
        // 无法再通过反射调整。本地多控大厅创建时统一以 MaxLocalPlayerCount 初始化，无需再扩容。
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen._Process))]
internal static class NCustomRunScreenLocalPlayersProcessPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance)
    {
        NCustomRunScreenLocalPlayersPatch.TryReconcileLocalPlayers(__instance);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), "OnEmbarkPressed")]
internal static class NCustomRunEmbarkGuardPatch
{
    [HarmonyPrefix]
    private static void Prefix(NCustomRunScreen __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled
            || __instance.Lobby?.NetService is not LocalLoopbackHostGameService loopbackService)
        {
            return;
        }

        NCustomRunScreenLocalPlayersPatch.TryReconcileLocalPlayers(__instance);
        loopbackService.SetCurrentSenderId(LocalSelfCoopContext.PrimaryPlayerId);
        LocalContext.NetId = LocalSelfCoopContext.PrimaryPlayerId;
        LocalMultiControlLogger.Info($"自定义模式开始前强制校正 sender 到主角色: {LocalSelfCoopContext.PrimaryPlayerId}");
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.OnSubmenuOpened))]
internal static class NCustomRunLocalCountButtonsOpenPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance)
    {
        LocalCustomRunCountButtons.Sync(__instance);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen._Process))]
internal static class NCustomRunLocalCountButtonsProcessPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance)
    {
        LocalCustomRunCountButtons.Sync(__instance);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.OnSubmenuClosed))]
internal static class NCustomRunLocalCountButtonsClosePatch
{
    [HarmonyPrefix]
    private static void Prefix(NCustomRunScreen __instance)
    {
        LocalCustomRunCountButtons.Remove(__instance);
    }
}

/// <summary>
/// 自定义页的人数面板（R2：骨架搬进 <see cref="LocalPlayerCountPanel"/>，这里只剩门控与落点）。
/// </summary>
internal static class LocalCustomRunCountButtons
{
    private static readonly LocalPlayerCountPanel Panel = new(new LocalPlayerCountPanelOptions(
        namePrefix: "LocalCustomRun",
        sourcePrefix: "custom-ui-button",
        successLogText: "通过自定义模式实体按钮调整本地人数成功",
        resolvePosition: LocalPlayerCountPanelOptions.BottomRight));

    public static void Sync(NCustomRunScreen screen)
    {
        // 只在「本地多控开启 + 本页大厅确实是我们自己的回环服务」时才挂面板（r159）：
        // 否则残留会话会让官方联机自定义页也冒出我们的席位/切人按钮。
        bool shouldShow = LocalSelfCoopContext.IsEnabled
            && screen.Lobby?.NetService is LocalLoopbackHostGameService;
        Panel.Sync(screen, shouldShow);
    }

    public static void Remove(NCustomRunScreen screen)
    {
        Panel.Remove(screen);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.OnSubmenuOpened))]
internal static class NCustomRunSelectionSyncOpenPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance)
    {
        LocalCustomRunSelectionSync.TrySync(__instance);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.PlayerChanged))]
internal static class NCustomRunSelectionSyncPlayerChangedPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance)
    {
        LocalCustomRunSelectionSync.TrySync(__instance);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen._Process))]
internal static class NCustomRunSelectionSyncProcessPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCustomRunScreen __instance)
    {
        LocalCustomRunSelectionSync.TrySync(__instance);
    }
}

internal static class LocalCustomRunSelectionSync
{
    private static bool _isSyncing;

    public static void TrySync(NCustomRunScreen screen)
    {
        if (!LocalSelfCoopContext.IsEnabled || _isSyncing)
        {
            return;
        }

        // 判空（r161 / BUG-21）：与上面三处是同一个坑 —— Custom 页的 `_lobby` 在「未建厅 / 已被清理」时是 null，
        // 而主菜单里这个子屏常驻、`_Process` 每帧都会进来 ⇒ 直接访问 `lobby.NetService` 会每帧抛
        // NullReferenceException。r159 只补了 `TryReconcileLocalPlayers` / `NCustomRunEmbarkGuardPatch` /
        // `LocalCustomRunCountButtons.Sync` 三处，**漏了这里**；r161 实机日志（联网局 981 条 / 断网局 304 条 NRE）
        // 全部来自本行。
        StartRunLobby? lobby = screen.Lobby;
        if (lobby?.NetService is not LocalLoopbackHostGameService)
        {
            return;
        }

        _isSyncing = true;
        try
        {
            Control? charButtonContainer = AccessTools.Field(typeof(NCustomRunScreen), "_charButtonContainer")
                ?.GetValue(screen) as Control;
            if (charButtonContainer == null)
            {
                return;
            }

            ulong editingPlayerId = LocalContext.NetId ?? LocalSelfCoopContext.PrimaryPlayerId;
            if (lobby.Players.All((player) => player.id != editingPlayerId))
            {
                editingPlayerId = LocalSelfCoopContext.PrimaryPlayerId;
            }

            StartRunLobbyPlayer editingPlayer = lobby.Players.FirstOrDefault((player) => player.id == editingPlayerId);

            List<NCharacterSelectButton> buttons = charButtonContainer.GetChildren().OfType<NCharacterSelectButton>().ToList();
            foreach (NCharacterSelectButton button in buttons)
            {
                foreach (StartRunLobbyPlayer player in lobby.Players)
                {
                    button.OnRemotePlayerDeselected(player.id);
                }
            }

            NCharacterSelectButton? selectedButton = null;
            foreach (NCharacterSelectButton button in buttons)
            {
                bool isSelected = button.Character == editingPlayer.character;
                AccessTools.Field(typeof(NCharacterSelectButton), "_isSelected")?.SetValue(button, isSelected);
                if (isSelected)
                {
                    selectedButton = button;
                }
            }

            foreach (StartRunLobbyPlayer player in lobby.Players)
            {
                if (player.id == editingPlayer.id)
                {
                    continue;
                }

                NCharacterSelectButton? targetButton = buttons.FirstOrDefault((button) => button.Character == player.character);
                targetButton?.OnRemotePlayerSelected(player.id);
            }

            foreach (NCharacterSelectButton button in buttons)
            {
                AccessTools.Method(typeof(NCharacterSelectButton), "RefreshState")?.Invoke(button, Array.Empty<object>());
            }

            AccessTools.Field(typeof(NCustomRunScreen), "_selectedButton")?.SetValue(screen, selectedButton);
        }
        finally
        {
            _isSyncing = false;
        }
    }
}
