using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 瓦库奖励自动领取期间抑制卡牌奖励选择弹屏：
/// LocalWakuuRewardAutoClaim 结算瓦库的 CardReward 时置位 SuppressCardRewardScreen，
/// 本补丁让 ShowScreen 直接返回 null——OnSelect 随后走游戏原生
/// "Selector.GetSelectedCardReward" 自动作答分支（TestMode 的原生工作方式）。
/// 标记仅在结算作用域内置位，真人的奖励弹屏不受影响。
/// </summary>
[HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.ShowScreen))]
internal static class NCardRewardSelectionScreenAutoClaimPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ref NCardRewardSelectionScreen? __result)
    {
        if (!LocalWakuuRewardAutoClaim.SuppressCardRewardScreen)
        {
            return true;
        }

        __result = null;
        return false;
    }
}
