using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Rooms;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 进入商店房时触发瓦库污浊药水自动投掷（见 LocalWakuuMerchantFoulThrow）。
/// 只做触发，具体等待/去重/投掷都在运行时类内完成。
/// </summary>
[HarmonyPatch(typeof(MerchantRoom), "EnterInternal")]
internal static class MerchantRoomEnterFoulThrowPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        LocalWakuuMerchantFoulThrow.OnMerchantRoomEntered();
    }
}
