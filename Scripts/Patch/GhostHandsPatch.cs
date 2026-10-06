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
        // r215：入战站位快照（此刻 CreateAllyNodes 刚按 LocalContext 算完站位）——
        // 只为诊断「打一半我和瓦库的立绘左右站位对调了」；见 LocalCreaturePositionProbe。
        LocalCreaturePositionProbe.LogCombatLayoutSnapshot(__instance, "combat-room-ready");
        // r222：第三方交互守卫（LexNinja2）—— 进战斗前确保已挂上，否则"死在战斗外的席位"会让它的
        // 侧回合结束补键失败、KeyNotFound 打断回合循环（实机：炼化后结束回合、敌方回合不开始）。
        LexNinja2KelaTurnEndGuardPatch.TryApplyLate();
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
