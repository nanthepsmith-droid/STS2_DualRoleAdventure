using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom._Ready))]
internal static class NCombatRoomGhostHandsPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatRoom __instance)
    {
        LocalGhostHandsRuntime.OnCombatRoomReady(__instance);
    }
}

[HarmonyPatch(typeof(NGame), nameof(NGame._Input))]
internal static class GhostHandsHotkeysPatch
{
    [HarmonyPostfix]
    private static void Postfix(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey keyEvent || !LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        Key keycode = keyEvent.Keycode;
        Key physicalKeycode = keyEvent.PhysicalKeycode;

        if ((keycode == Key.F8 || physicalKeycode == Key.F8) && keyEvent.IsReleased())
        {
            LocalGhostHandsRuntime.Toggle();
        }

        // Ctrl+方向键调整位置已移到 LocalGhostHandsOverlay.PollMoveKeys（逐帧轮询原始按键）——
        // 部分节点会先于 NGame._Input 消费方向键事件（Ctrl+Right 永远到不了）。
    }
}
