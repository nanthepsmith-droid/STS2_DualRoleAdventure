using System;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 瓦库自动用药（Phase 2.5）——果汁到手立刻喝：
/// PotionCmd.TryToProcure 成功入库后，若归属者是瓦库形态角色且开关开启、药水为果汁
/// （+5 最大生命，越早喝收益越久），延迟片刻等领取链路收尾后经官方 EnqueueManualUse 入队喝掉。
/// 用官方入队而非直接 OnUseWrapper：领取场景多在战斗外（奖励/事件），入队路径对
/// 战斗内/外都有原生处理（NonCombat / CombatPlayPhaseOnly）。
/// </summary>
[HarmonyPatch(typeof(PotionCmd), nameof(PotionCmd.TryToProcure), new[] { typeof(PotionModel), typeof(Player), typeof(int) })]
internal static class PotionProcuredAutoDrinkPatch
{
    /// <summary>等领取链路（奖励界面/同步镜像）收尾后再入队，避免与进行中的结算交错。</summary>
    private const int DrinkDelayMs = 800;

    [HarmonyPostfix]
    private static void Postfix(PotionModel potion, Player player, ref Task<PotionProcureResult> __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return;
        }

        __result = WrapAsync(__result, potion, player);
    }

    private static async Task<PotionProcureResult> WrapAsync(Task<PotionProcureResult> original, PotionModel potion, Player player)
    {
        PotionProcureResult result = await original;
        try
        {
            if (!result.success
                || !LocalWakuuAutopilotConfig.AutoUsePotions
                || !LocalWakuuRelicRuntime.IsVakuuFormMode(player)
                || potion is not FruitJuice)
            {
                return result;
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"果汁自动饮用判定异常: {exception.Message}");
            return result;
        }

        LocalMultiControlLogger.Info($"瓦库获得果汁，稍后自动饮用: player={player.NetId}, potion={potion.Id.Entry}");
        _ = TaskHelper.RunSafely(DrinkWhenReadyAsync(potion, player));
        return result;
    }

    private static async Task DrinkWhenReadyAsync(PotionModel potion, Player player)
    {
        try
        {
            await Task.Delay(DrinkDelayMs);

            // 等待期间可能已被其他链路处理（丢弃/换槽/手动喝掉），逐项复核
            if (potion.HasBeenRemovedFromState || potion.IsQueued || !player.Potions.Contains(potion))
            {
                LocalMultiControlLogger.Info(
                    $"果汁自动饮用取消（药水已不在栏上或已在队列中）: player={player.NetId}");
                return;
            }

            // 目标传 null：EnqueueManualUse 对 AnyPlayer 类药水会自动落到自己身上
            potion.EnqueueManualUse(null);
            LocalMultiControlLogger.Info($"瓦库果汁已自动入队饮用: player={player.NetId}, potion={potion.Id.Entry}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"果汁自动饮用失败（保留在药水栏）: player={player.NetId}, error={exception.Message}");
        }
    }
}
