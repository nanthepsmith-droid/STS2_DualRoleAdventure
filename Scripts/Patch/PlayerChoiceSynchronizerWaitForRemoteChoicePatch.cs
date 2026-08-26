using System;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 瓦库火堆选项的"选一个队友"类等待自动作答：
/// mod 自绘选项（如欢乐时光 CHH_MUTUAL_AID 互助）在 LocalContext 非本人时走
/// WaitForRemoteChoice(owner, choiceId) 远端分支等待结果——后台瓦库没人能点，
/// 这里在该等待属于本 mod 火堆流程（IsAwaitingOptionExecution）且归属者为瓦库形态角色时，
/// 直接以"另一个存活玩家（真人）"作为目标补全结果，选项得以正常完成。
/// 其他玩家 / 其他系统的同名等待不受影响。
/// </summary>
[HarmonyPatch(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.WaitForRemoteChoice))]
internal static class PlayerChoiceSynchronizerWaitForRemoteChoicePatch
{
    [HarmonyPrefix]
    private static bool Prefix(Player player, ref Task<PlayerChoiceResult> __result)
    {
        if (!LocalWakuuRestAutoChoice.IsAwaitingOptionExecution
            || player == null
            || !LocalWakuuRelicRuntime.IsVakuuFormMode(player))
        {
            return true;
        }

        ulong? teammateNetId = LocalWakuuRestAutoChoice.GetPreferredTeammateNetId(player);
        __result = Task.FromResult(PlayerChoiceResult.FromPlayerId(teammateNetId));
        string targetText = teammateNetId?.ToString() ?? "无";
        LocalMultiControlLogger.Info(
            $"瓦库火堆队友选择已自动指定: owner={player.NetId}, target={targetText}");
        return false;
    }
}
