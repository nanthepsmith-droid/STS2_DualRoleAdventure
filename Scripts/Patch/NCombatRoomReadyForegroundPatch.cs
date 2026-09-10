using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 战斗房间节点 _Ready 时（战斗 UI 建起来**之前**）先把控制上下文切回 1 号位（r98）。
///
/// 背景（BUG-1 / 辉星不同步）：本地多控下「入战前把前台停在瓦库托管角色」时，
/// 原版 NCombatUi.Activate 与第三方 mod（如 RegentFX「万象辉星」的星环
/// `SetupStarRingForLocalPlayer`）都会在房间创建/UI 初始化时按 **LocalContext 的 me** 绑定各自的 UI。
/// 原先只在 NCombatRoom.OnCombatSetUp 之后才切回 1 号位，为时已晚：
/// 那些已绑定的 UI（能量球、辉星计数器、第三方星环）会留在瓦库身上。
///
/// 放在 _Ready 的**前缀**：Harmony 里所有前缀都早于任何 mod 的后缀，
/// 因此第三方挂在 NCombatRoom._Ready 上的后缀（按 LocalContext 绑定本地玩家）也能拿到正确的 me。
/// 只处理 ActiveCombat：VisualOnly 是「战斗风事件房」，那里不该抢事件归属。
/// </summary>
[HarmonyPatch(typeof(NCombatRoom), "_Ready")]
internal static class NCombatRoomReadyForegroundPatch
{
    [HarmonyPrefix]
    private static void Prefix(NCombatRoom __instance)
    {
        if (__instance == null || __instance.Mode != CombatRoomMode.ActiveCombat)
        {
            return;
        }

        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress)
        {
            return;
        }

        LocalMultiControlRuntime.SwitchControlledPlayerTo(LocalSelfCoopContext.PrimaryPlayerId, "combat-room-ready");
    }
}
