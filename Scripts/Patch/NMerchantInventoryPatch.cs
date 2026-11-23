using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NMerchantInventory), nameof(NMerchantInventory.Initialize))]
internal static class NMerchantInventoryPatch
{
    [HarmonyPrefix]
    private static void Prefix(ref MerchantInventory inventory)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (runState?.CurrentRoom is not MerchantRoom merchantRoom)
        {
            return;
        }

        // R3 B2：商店库存要绑到「回环上下文当前对着的那位」，逐字等价于旧写法
        // `LocalContext.NetId ?? PrimaryPlayerId`（不是前台口径）。
        ulong currentPlayerId = LocalSeatSource.ContextSeatId() ?? LocalSelfCoopContext.PrimaryPlayerId;
        Player? currentPlayer = runState.GetPlayer(currentPlayerId) ?? LocalContext.GetMe(runState);
        if (currentPlayer == null)
        {
            return;
        }

        inventory = LocalMerchantInventoryRuntime.GetOrCreateInventory(merchantRoom, currentPlayer);
        LocalMerchantInventoryRuntime.BindInventoryToRoom(merchantRoom, currentPlayer, inventory);
        LocalMultiControlLogger.Info($"商店库存绑定到当前角色: player={currentPlayer.NetId}");

        // Phase 4：瓦库角色的商店视图打开 → 触发自动采购（默认关 shopAssist）
        LocalWakuuMerchantAuto.OnMerchantInventoryShown(currentPlayer);

        // r394：极速模式（Blitz）商店是另一套库存（6 格 / 3 次机会 / 不花钱），走独立的判定表与流程。
        // 触发点仍是这里（进店即 Initialize，不需要真人打开商人界面），内部会按游戏模式与开关自行分流。
        LocalWakuuBlitzShopAuto.OnShopRoomReady(currentPlayer);
    }
}
