using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalMerchantInventoryRuntime
{
    private static readonly Dictionary<MerchantRoom, Dictionary<ulong, MerchantInventory>> CachedInventories = new();

    public static void Clear()
    {
        CachedInventories.Clear();
    }

    public static MerchantInventory GetOrCreateInventory(MerchantRoom room, Player player)
    {
        if (!CachedInventories.TryGetValue(room, out Dictionary<ulong, MerchantInventory>? perPlayer))
        {
            perPlayer = new Dictionary<ulong, MerchantInventory>();
            CachedInventories[room] = perPlayer;
        }

        if (!perPlayer.TryGetValue(player.NetId, out MerchantInventory? inventory))
        {
            inventory = BuildDualPoolInventory(player);
            perPlayer[player.NetId] = inventory;
            LocalMultiControlLogger.Info($"已生成角色专属商店库存: player={player.NetId}");
        }

        return inventory;
    }

    public static void BindInventoryToRoom(MerchantRoom room, Player player, MerchantInventory inventory)
    {
        IRunState? runState = player.RunState;
        if (runState == null)
        {
            return;
        }

        // 0.113：`MerchantRoom.Inventories` 已删，库存统一由 `MerchantSynchronizer` 持有
        // （`SetMerchantInventory` 是官方绑定入口，内部按 `GetPlayerSlotIndex` 归档）。
        try
        {
            RunManager.Instance.MerchantSynchronizer.SetMerchantInventory(player, inventory);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"商店库存绑定失败：player={player.NetId}, slot={runState.GetPlayerSlotIndex(player)}, "
                + $"error={exception.GetType().Name}: {exception.Message}");
        }
    }

    public static void RefreshShopRoomForPlayer(ulong playerId)
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null || runState.CurrentRoom is not MerchantRoom merchantRoom)
        {
            return;
        }

        Player? player = runState.GetPlayer(playerId);
        if (player == null)
        {
            return;
        }

        MerchantInventory inventory = GetOrCreateInventory(merchantRoom, player);
        BindInventoryToRoom(merchantRoom, player, inventory);

        if (NMerchantRoom.Instance == null)
        {
            return;
        }

        if (runState.GameMode == GameMode.Blitz)
        {
            // 0.113：Blitz 商店是另一套界面（NBlitzMerchantRoom + 背景资源），库存里只有 `BlitzEntries`
            // （每人 3 次选择）。界面的 `Inventory.Initialize(...)` 只允许调用一次、且 `_Ready` 时按
            // 「进店时的前台」`GetLocalInventory()` 绑定 ⇒ 切席位只能整块换节点（与普通商店同一手法），
            // 否则第二个角色看到的是前一个人的 3 次选择。换节点前先把这个角色条目用到的资源预载一遍
            // （不同玩家的 Blitz 条目类型可能不同，缺预载会在 `NBlitzMerchantSlot` 取图标时抛）。
            TaskHelper.RunSafely(RebuildBlitzShopAsync(runState, playerId, inventory));
            return;
        }

        // 0.113：`NMerchantRoom` 变成抽象类、`Create` 只接收玩家列表（room 从 RunManager 取）。
        NStandardMerchantRoom? refreshedRoomNode = NStandardMerchantRoom.Create(runState.Players);
        if (refreshedRoomNode == null)
        {
            return;
        }

        NRun.Instance?.SetCurrentRoom(refreshedRoomNode);
        LocalMultiControlLogger.Info($"商店界面已切换到当前角色库存: player={playerId}");
    }

    private static async Task RebuildBlitzShopAsync(RunState runState, ulong playerId, MerchantInventory inventory)
    {
        try
        {
            BackgroundAssets backgroundAssets = runState.Act.GenerateBackgroundAssets(
                NCombatRoom.GenerateBackgroundRngForCurrentPoint(runState));
            await PreloadManager.LoadRoomBlitzMerchantAssets(backgroundAssets, inventory.BlitzEntries);
            if (!RunManager.Instance.IsInProgress || runState.CurrentRoom is not MerchantRoom)
            {
                return;
            }

            NBlitzMerchantRoom? refreshedRoomNode = NBlitzMerchantRoom.Create(runState.Players, backgroundAssets);
            if (refreshedRoomNode == null)
            {
                return;
            }

            NRun.Instance?.SetCurrentRoom(refreshedRoomNode);
            LocalMultiControlLogger.Info($"Blitz 商店界面已切换到当前角色库存: player={playerId}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"Blitz 商店界面切换失败(保持当前界面): player={playerId}, "
                + $"error={exception.GetType().Name}: {exception.Message}");
        }
    }

    private static MerchantInventory BuildDualPoolInventory(Player player)
    {
        // 0.113：原版新增 `MerchantInventory.CreateForNormalMerchant(player)`，内部与我们原来手搓的库存
        // **逐行等价**（5 张角色卡 Attack×2/Skill×2/Power、2 张无色 Uncommon/Rare、3 件遗物 RollRarity(rng)
        // ×2 + Shop、3 瓶药水、删牌服务 + `Hook.ShouldAllowMerchantCardRemoval` 判定、随机一张特价牌），
        // 而且 `Setup()` 会统一 Populate + 挂 `PurchaseCompleted` + 分配 Id。
        // 所以这里直接复用上游工厂，不再反射塞 `_characterCardEntries` 等私有列表 —— 以后上游改商店规则
        // 我们也自动跟随（原手搓版每次上游动商店都要跟着改，0.113 就撞了个正着）。
        // Blitz 是另一套库存（`CreateForBlitzMerchant`，只有 BlitzEntries），按模式分流。
        return player.RunState.GameMode == GameMode.Blitz
            ? MerchantInventory.CreateForBlitzMerchant(player)
            : MerchantInventory.CreateForNormalMerchant(player);
    }
}
