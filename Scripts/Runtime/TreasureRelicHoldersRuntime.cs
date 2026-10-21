using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 宝箱**遗物格**补齐（12 人扩展）：原版 `treasure_room.tscn` 里只硬编码了
/// `MultiplayerRelicHolder1..4` 四个格子，而遗物数量 = 玩家数量
/// （`TreasureRoomRelicSynchronizer.BeginRelicPicking` 里每个玩家各生成一个），
/// `InitializeRelics` 又只遍历 `_multiplayerHolders` ⇒ 第 5 个遗物起根本没有节点
/// （看不见、点不到；`DefaultFocusedControl` 的 `_holdersInUse[slotIndex]` 还会越界）。
///
/// <para>在 `NTreasureRoomRelicCollection.InitializeRelics` 的 **Prefix** 里：遗物数 &gt; 4 时补建缺的
/// 格子（同构实例 = `scenes/ui/treasure_relic_holder.tscn`）并按
/// <see cref="TreasureRelicHolderLayoutPolicy"/> 重排；4 个及以内**一个值都不动**（原版布局回归契约）。</para>
///
/// <para>失败只 WARN：退回"只有 4 个格子"的老状态（此时投票仍能推进 —— 所有人只能投 0~3 号，
/// 由原版 RPS + 安慰奖收口），不会卡死。</para>
/// </summary>
internal static class TreasureRelicHoldersRuntime
{
    private const string HolderSceneInnerPath = "ui/treasure_relic_holder";
    private const string HolderContainerPath = "Container";
    private const string HolderNamePrefix = "MultiplayerRelicHolder";

    /// <summary>按当前遗物数量补齐并重排宝箱遗物格。幂等（重复调用只是重设同样的值）。</summary>
    internal static void EnsureHoldersForCurrentRelics(NTreasureRoomRelicCollection collection)
    {
        try
        {
            IReadOnlyList<MegaCrit.Sts2.Core.Models.RelicModel>? relics =
                RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics;
            int relicCount = relics?.Count ?? 0;
            if (!TreasureRelicHolderLayoutPolicy.NeedsBackfill(relicCount))
            {
                return;
            }

            if (AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_multiplayerHolders")?.GetValue(collection)
                is not List<NTreasureRoomRelicHolder> holders)
            {
                LocalMultiControlLogger.Warn($"宝箱遗物格补齐跳过：拿不到 _multiplayerHolders 字段。relics={relicCount}");
                return;
            }

            Control? container = collection.GetNodeOrNull<Control>(HolderContainerPath)
                ?? AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_relicContainer")?.GetValue(collection) as Control;
            if (container == null)
            {
                LocalMultiControlLogger.Warn($"宝箱遗物格补齐跳过：找不到 {HolderContainerPath} 节点。relics={relicCount}");
                return;
            }

            SeedVanillaHolders(collection, container, holders);

            int created = 0;
            while (holders.Count < relicCount)
            {
                NTreasureRoomRelicHolder holder = SceneHelper.Instantiate<NTreasureRoomRelicHolder>(HolderSceneInnerPath);
                holder.Name = $"{HolderNamePrefix}{holders.Count + 1}";
                holder.Visible = false;
                holder.AnchorLeft = 0.5f;
                holder.AnchorTop = 0.5f;
                holder.AnchorRight = 0.5f;
                holder.AnchorBottom = 0.5f;
                container.AddChildSafely(holder);
                holders.Add(holder);
                created++;
            }

            IReadOnlyList<TreasureRelicHolderPlacement> placements = TreasureRelicHolderLayoutPolicy.Compute(holders.Count);
            for (int i = 0; i < holders.Count && i < placements.Count; i++)
            {
                ApplyPlacement(holders[i], placements[i]);
            }

            LocalMultiControlLogger.Info(
                $"宝箱遗物格已补齐（12 人扩展）: relics={relicCount}, 原版格={TreasureRelicHolderLayoutPolicy.VanillaHolderCount}, " +
                $"新建={created}, 总格数={holders.Count}, 每排最多={TreasureRelicHolderLayoutPolicy.MaxPerRow}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"宝箱遗物格补齐失败（退回原版 4 格，投票仍可推进）: {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// 把场景里已有的 `MultiplayerRelicHolder1..4` 认领进 `_multiplayerHolders`
    /// （正常时机该列表已由 `_Ready` 填好；这里只兜"列表为空/部分缺失"的情况）。
    /// </summary>
    private static void SeedVanillaHolders(
        NTreasureRoomRelicCollection collection,
        Control container,
        List<NTreasureRoomRelicHolder> holders)
    {
        if (holders.Count > 0)
        {
            return;
        }

        NTreasureRoomRelicHolder? singleplayer = collection.SingleplayerRelicHolder;
        foreach (NTreasureRoomRelicHolder candidate in container.GetChildren().OfType<NTreasureRoomRelicHolder>())
        {
            if (candidate == singleplayer || !GodotObject.IsInstanceValid(candidate))
            {
                continue;
            }

            holders.Add(candidate);
        }
    }

    private static void ApplyPlacement(NTreasureRoomRelicHolder holder, TreasureRelicHolderPlacement placement)
    {
        holder.OffsetLeft = placement.Left;
        holder.OffsetTop = placement.Top;
        holder.OffsetRight = placement.Right;
        holder.OffsetBottom = placement.Bottom;
    }
}
