using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;

namespace LocalMultiControl.Scripts.Patch;

// ============================================================
// 自有统计角标/悬停弹窗的挂载补丁（marker r69）。
// 数据与皮皮军师（SkadaHelper）社区统计 UI 完全分开：本组补丁只把
// LocalStatBadgeUi 的自绘角标挂到三处界面，并在游戏 hover tip 弹出时追加个人统计块。
// 全部失败都会在 LocalStatBadgeUi 内部 try/catch 后降级为 WARN，不影响游戏本体。
// ============================================================

/// <summary>奖励选牌（NCardRewardSelectionScreen.RefreshOptions，re-roll 也覆盖）。</summary>
[HarmonyPatch(typeof(NCardRewardSelectionScreen), "RefreshOptions")]
internal static class StatBadgeRewardRowPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCardRewardSelectionScreen __instance)
    {
        if (__instance != null)
        {
            LocalStatBadgeUi.RefreshRewardScreen(__instance);
        }
    }
}

/// <summary>
/// 商店卡主挂载点（NMerchantInventory.Initialize；含伪商店子类）。
/// 同时打一条统计日志，便于区分「没挂上角标（取不到卡/商品未生成）」与「该卡无个人数据」。
/// </summary>
[HarmonyPatch(typeof(NMerchantInventory), "Initialize")]
internal static class StatBadgeMerchantPatch
{
    [HarmonyPostfix]
    private static void Postfix(NMerchantInventory __instance)
    {
        if (__instance == null)
        {
            return;
        }

        int total = 0;
        int attached = 0;
        foreach (var slot in __instance.GetAllSlots())
        {
            if (slot is not NMerchantCard merchantCard)
            {
                continue;
            }

            total++;
            if (LocalStatBadgeUi.RefreshMerchantCard(merchantCard))
            {
                attached++;
            }
        }

        if (LocalStatBadgeUi.Show)
        {
            LocalMultiControlLogger.Info(
                $"统计角标（商店）：卡槽={total}，已挂角标={attached}，未挂={total - attached}"
                + "（未挂原因可能是该卡无个人记录，或商品条目尚未生成（已排延迟重试））");
        }
    }
}

/// <summary>商店卡补充挂载点（NMerchantCard.FillSlot：单卡填充完毕后立即刷新）。</summary>
[HarmonyPatch(typeof(NMerchantCard), "FillSlot")]
internal static class StatBadgeMerchantFillSlotPatch
{
    [HarmonyPostfix]
    private static void Postfix(NMerchantCard __instance)
    {
        if (__instance != null)
        {
            LocalStatBadgeUi.RefreshMerchantCard(__instance);
        }
    }
}

/// <summary>
/// 事件选项角标挂载点：NEventRoom.SetOptions（进入事件房间的首页与选项被选后的每次状态刷新都会重建按钮，
/// 两者都必经 SetOptions→Layout.AddOptions）。
/// 注意不能只挂 RefreshEventState——该方法仅在选项被选后（StateChanged）触发，而事件首页的选项是
/// SetupLayout→SetOptions 直接渲染的（如明日方舟集成战略等多页事件：第一页真实选项永远没角标，
/// 只有切到只剩「继续」的末页才有；同类坑见 EventSynchronizerBeginEventPatch 注释）。
/// </summary>
[HarmonyPatch(typeof(NEventRoom), "SetOptions")]
internal static class StatBadgeEventPatch
{
    [HarmonyPostfix]
    private static void Postfix(NEventRoom __instance)
    {
        if (__instance?.Layout == null)
        {
            return;
        }

        foreach (var button in __instance.Layout.OptionButtons)
        {
            LocalStatBadgeUi.RefreshEventButtons(button);
        }
    }
}
