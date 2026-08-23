using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using Godot;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 修复：假商人（商人？？？）事件里只有 1 号位角色能扔浑浊药水（污浊药水），2 号位点了没反应。
/// 根因：本 mod 切人时会把 EventSynchronizer._localPlayerId 同步成当前前台角色
/// （LocalMultiControlRuntime.SyncRunSynchronizerLocalPlayerId，UseSingleEventFlow=false），
/// 而 FakeMerchant 是共享事件（IsShared=true）、事件房间不按角色重建，NFakeMerchant 界面节点
/// 只在进房那一刻通过 EventModel.SetNode 挂到“当时前台角色”的事件实例上。
/// 切到其他角色后 EventRoom.LocalMutableEvent.Node == null：
///   1) FoulPotion.PassesCustomUsabilityCheck → GetFoulPotionMerchantTarget 拿不到按钮 → 药水弹窗“投掷”被禁用；
///   2) FoulPotion.OnUse 在同一处提前 return，就算能瞄准，药水也会被消耗却毫无效果。
/// 对策：解析不到节点时按“本地实例 → 当前显示的自定义事件界面 → 兄弟实例”顺序回退解析；
/// OnUse 的假商人分支用同样的解析结果替原实现完成投掷结算（战斗/真商店等其余分支全部放行原版）。
/// </summary>
[HarmonyPatch(typeof(FoulPotion), nameof(FoulPotion.GetFoulPotionMerchantTarget))]
internal static class FoulPotionMerchantTargetPatch
{
    [HarmonyPostfix]
    private static void Postfix(AbstractRoom room, ref (NMerchantButton? button, Control? screenContext) __result)
    {
        if (__result.button != null || !IsSelfCoopFakeMerchantEventRoom(room, out EventRoom eventRoom))
        {
            return;
        }

        if (!TryResolveMerchantNode(eventRoom, requireInventoryClosed: true, out NFakeMerchant merchantNode))
        {
            LocalMultiControlLogger.Warn("浑浊药水目标回退解析失败：假商人界面节点不可用，维持原版判定。");
            return;
        }

        __result = (merchantNode.MerchantButton, merchantNode);
        LocalMultiControlLogger.Info("浑浊药水目标回退解析成功：已通过非本地实例/当前界面找到假商人按钮。");
    }

    internal static bool IsSelfCoopFakeMerchantEventRoom(AbstractRoom? room, out EventRoom eventRoom)
    {
        eventRoom = null!;
        if (room is not EventRoom candidate)
        {
            return false;
        }
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return false;
        }
        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return false;
        }
        if (CombatManager.Instance.IsInProgress)
        {
            return false;
        }

        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null || runState.Players.Count <= 1)
        {
            return false;
        }

        eventRoom = candidate;
        return candidate.CanonicalEvent is FakeMerchant;
    }

    /// <summary>
    /// 按“原版语义优先”的顺序解析当前可用的 NFakeMerchant 界面节点。
    /// requireInventoryClosed 对应原版 GetFoulPotionMerchantTarget 的库存未展开检查；
    /// OnUse 原实现不做该检查，调用方传 false 保持行为一致。
    /// </summary>
    internal static bool TryResolveMerchantNode(EventRoom eventRoom, bool requireInventoryClosed, out NFakeMerchant merchantNode)
    {
        merchantNode = null!;
        try
        {
            foreach (NFakeMerchant candidate in EnumerateMerchantNodeCandidates(eventRoom))
            {
                if (!IsValidMerchantNode(candidate, requireInventoryClosed))
                {
                    continue;
                }
                merchantNode = candidate;
                return true;
            }
            return false;
        }
        catch (Exception exception)
        {
            // 已释放的 Godot 节点访问可能抛异常（如战斗后残留引用），按解析失败处理。
            LocalMultiControlLogger.Warn($"浑浊药水目标解析异常，按失败处理: {exception.Message}");
            return false;
        }
    }

    private static IEnumerable<NFakeMerchant> EnumerateMerchantNodeCandidates(EventRoom eventRoom)
    {
        // 1) 原版路径：本地（前台角色）事件实例上的节点，命中即等价于原版行为。
        EventModel? localMutableEvent = RunManager.Instance.EventSynchronizer.GetLocalEvent();
        if (localMutableEvent?.Node is NFakeMerchant localNode)
        {
            yield return localNode;
        }

        // 2) 当前实际显示的自定义事件界面：与玩家看到的商人严格一致。
        if (NEventRoom.Instance?.CustomEventNode is NFakeMerchant liveNode)
        {
            yield return liveNode;
        }

        // 3) 兜底：任意兄弟事件实例上挂载的节点（理论上极少走到）。
        foreach (EventModel candidate in RunManager.Instance.EventSynchronizer.Events)
        {
            if (candidate?.Node is NFakeMerchant siblingNode)
            {
                yield return siblingNode;
            }
        }
    }

    private static bool IsValidMerchantNode(NFakeMerchant? merchantNode, bool requireInventoryClosed)
    {
        if (merchantNode == null || !GodotObject.IsInstanceValid(merchantNode))
        {
            return false;
        }
        NMerchantInventory? inventory = merchantNode.Inventory;
        if (requireInventoryClosed && (inventory == null || inventory.IsOpen))
        {
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(FoulPotion), "OnUse")]
internal static class FoulPotionOnUsePatch
{
    [HarmonyPrefix]
    private static bool PrefixOnUse(FoulPotion __instance, ref Task __result)
    {
        if (!FoulPotionMerchantTargetPatch.IsSelfCoopFakeMerchantEventRoom(__instance.Owner?.RunState.CurrentRoom, out EventRoom eventRoom))
        {
            return true;
        }

        if (!FoulPotionMerchantTargetPatch.TryResolveMerchantNode(eventRoom, requireInventoryClosed: false, out NFakeMerchant merchantNode))
        {
            LocalMultiControlLogger.Warn($"假商人事件投掷拦截：未能解析商人界面节点，交回原版逻辑 owner={__instance.Owner?.NetId}");
            return true;
        }

        ulong ownerId = __instance.Owner?.NetId ?? 0UL;
        LocalMultiControlLogger.Info($"假商人事件投掷拦截：代为执行假商人分支 owner={ownerId}");
        __result = ThrowAtFakeMerchantAsync(__instance, merchantNode);
        return false;
    }

    /// <summary>
    /// 复刻 FoulPotion.OnUse 假商人分支：洒落特效 + 逐角色触发 FoulPotionThrown（各自结算奖励并进入战斗）。
    /// 与原版的唯一差异是界面节点来自上面的回退解析。
    /// </summary>
    private static async Task ThrowAtFakeMerchantAsync(FoulPotion potion, NFakeMerchant merchantNode)
    {
        AccessTools.Method(typeof(FoulPotion), "ShowPotionVfx")?
            .Invoke(potion, new object?[] { merchantNode.MerchantButton });

        List<Task> throwTasks = new List<Task>();
        foreach (Player player in potion.Owner.RunState.Players)
        {
            if (RunManager.Instance.EventSynchronizer.GetEventForPlayer(player) is FakeMerchant fakeMerchant)
            {
                throwTasks.Add(fakeMerchant.FoulPotionThrown(potion));
            }
        }
        await Task.WhenAll(throwTasks);
    }
}
