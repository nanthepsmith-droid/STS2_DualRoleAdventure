using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.OnSubmenuOpened))]
internal static class NCharacterSelectScreenOpenPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCharacterSelectScreen __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        LocalSelfCoopContext.ActiveCharacterSelectScreen = __instance;
        LocalSelfCoopContext.EnsureLobbySenderContext("character-select-opened");
        LocalSelfCoopContext.EnsureLocalAscensionOptionsUnlocked(__instance, "character-select-opened");

        NCharacterSelectButton? randomButton =
            AccessTools.Field(typeof(NCharacterSelectScreen), "_randomCharacterButton")?.GetValue(__instance) as NCharacterSelectButton;
        if (randomButton != null)
        {
            randomButton.Visible = false;
            randomButton.Disable();
        }
    }
}

[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.SelectCharacter))]
internal static class NCharacterSelectScreenSelectCharacterPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NCharacterSelectButton charSelectButton)
    {
        LocalSelfCoopContext.EnsureLobbySenderContext("character-select-before-select");

        if (!LocalSelfCoopContext.IsEnabled)
        {
            return true;
        }

        if (charSelectButton.Character is not RandomCharacter)
        {
            return true;
        }

        LocalMultiControlLogger.Warn("本地多控当前禁用随机角色：检测到随机资源缺失风险。");
        NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create(LocalModText.RandomCharacterNotSupported));
        return false;
    }
}

// ⚠ 0.114：原版 `PlayerChanged(StartRunLobbyPlayer player, bool isRandomCharacterResolution)`
// 去掉了第二个参数（`NCharacterSelectScreen.cs: public void PlayerChanged(StartRunLobbyPlayer player)`）。
// 旧签名留着会让 Harmony 报 `Parameter "isRandomCharacterResolution" not found` ⇒ **整个补丁类被跳过**
// （补丁本身不致命，但大厅"换角色"通知就断了）。
[HarmonyPatch(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.PlayerChanged))]
internal static class NCharacterSelectScreenPatch
{
    [HarmonyPostfix]
    private static void Postfix(StartRunLobbyPlayer player)
    {
        LocalSelfCoopContext.NotifyCharacterSelectPlayerChanged(player.id);
    }
}
