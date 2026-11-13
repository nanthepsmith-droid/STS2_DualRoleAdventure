using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「当前编辑席位」的角色按钮选中态 / 其它席位的远端标记重绘（r351，Dread / Blitz 两个新模式大厅页共用）。
///
/// 为什么需要：标准角色选择页的等价逻辑写在
/// <see cref="LocalSelfCoopContext.SyncCharacterSelectHighlight"/>（只认 `NCharacterSelectScreen`），
/// 而新模式的页面是各自独立的类型（`NDreadScreen` / `NBlitzScreen`），字段名相同但页面类型不同 ⇒
/// 切席位后高亮仍停在"上一个席位点过的角色"，`_selectedButton` 也会残留（出征时会被写进
/// 「上次使用的角色」偏好）。这里把重绘逻辑抽出来给两页共用，不改标准页的既有实现。
/// </summary>
internal static class LocalLobbyCharacterHighlight
{
    /// <summary>
    /// 按大厅现状重绘一页的角色按钮：
    /// ① 清掉所有席位的远端标记；② 只把「当前编辑席位」的角色按钮置为本地选中；
    /// ③ 其它席位按角色补回远端标记；④ 让页面的 `_selectedButton` 跟着切（避免把别人写进偏好）。
    /// </summary>
    internal static void Apply(Node screen, Control charButtonContainer, StartRunLobby lobby, FieldInfo? selectedButtonField)
    {
        List<NCharacterSelectButton> buttons = charButtonContainer.GetChildren().OfType<NCharacterSelectButton>().ToList();
        if (buttons.Count == 0)
        {
            return;
        }

        StartRunLobbyPlayer editingPlayer = lobby.LocalPlayer;
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

        selectedButtonField?.SetValue(screen, selectedButton);
    }
}

/// <summary>
/// 每页一份的高亮重绘去抖状态：只有「编辑席位 / 席位集合 / 任一席位角色」变化时才重绘一次。
///
/// 为什么按指纹而不是只按编辑席位：新增席位、某个席位换了角色都会让按钮上的标记过时；
/// 而 `SelectCharacter` 的本地路径只会重绘"自己被点的那一下"，跨席位时两边可能都不同步。
/// </summary>
internal sealed class LocalLobbyCharacterHighlightState
{
    private ulong _lastEditingPlayerId;
    private int _lastSignature;

    internal void Reset()
    {
        _lastEditingPlayerId = 0;
        _lastSignature = 0;
    }

    internal void TrySync(Node screen, Control? charButtonContainer, StartRunLobby? lobby, FieldInfo? selectedButtonField)
    {
        if (charButtonContainer == null || lobby == null || lobby.Players.Count == 0)
        {
            return;
        }

        ulong editingPlayerId = lobby.LocalPlayer.id;
        int signature = ComputeSignature(lobby.Players);
        if (editingPlayerId == _lastEditingPlayerId && signature == _lastSignature)
        {
            return;
        }

        _lastEditingPlayerId = editingPlayerId;
        _lastSignature = signature;
        try
        {
            LocalLobbyCharacterHighlight.Apply(screen, charButtonContainer, lobby, selectedButtonField);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"大厅角色高亮同步失败(忽略): {exception.Message}");
        }
    }

    private static int ComputeSignature(IReadOnlyList<StartRunLobbyPlayer> players)
    {
        unchecked
        {
            int hash = 17;
            foreach (StartRunLobbyPlayer player in players)
            {
                hash = hash * 31 + (int)(player.id ^ (player.id >> 32));
                hash = hash * 31 + (player.character?.Id.Entry.GetHashCode() ?? 0);
            }

            return hash;
        }
    }
}
