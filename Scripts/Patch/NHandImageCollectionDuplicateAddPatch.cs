using System.Collections.Generic;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 待查-1（2026-10-24 修）：宝藏房共享选遗物界面里，本地席位的手牌图像被**重复添加** ——
/// 原版会打一条 `[ERROR] Tried to add hand for player … twice!`。
///
/// <para><b>为什么在本地多控下会重复</b>：`NHandImageCollection.Initialize` 已经对
/// `runState.Players` 里每个席位 `AddHand` 过一次（手牌图像建好），但那个席位的
/// **输入态**在 `PeerInputSynchronizer` 里并不存在；而我们切前台时改 `LocalContext.NetId` 会经由
/// `SyncLocalScreen` → `GetOrCreateStateForPlayer(id)` 新建输入态 ⇒ 触发 `StateAdded`
/// ⇒ 又 `AddHand(id)` 一次。真实联机下两个客户端各自持有一个本机玩家，这个组合不会出现。</para>
///
/// <para><b>为什么可以直接跳过</b>：原版 `AddHand` 的"已存在"分支只有一句 `Log.Error`，
/// **什么都不做**（幂等），所以跳过原方法 = 行为完全不变、只把纯日志噪声关掉。
/// 该噪声唯一的代价是抬高全局 ERROR 计数、污染「异常族期望 0」的判读
/// （r246 首见，r260/r288/r295/r298 复现，单局最多 3 条）。</para>
/// </summary>
[HarmonyPatch(typeof(NHandImageCollection), "AddHand")]
internal static class NHandImageCollectionDuplicateAddPatch
{
    /// <summary>按席位去重打点，避免一局刷屏。</summary>
    private static readonly HashSet<ulong> Logged = new();

    [HarmonyPrefix]
    private static bool Prefix(NHandImageCollection __instance, ulong playerId)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        if (__instance.GetHand(playerId) == null)
        {
            return true;
        }

        if (Logged.Add(playerId))
        {
            LocalMultiControlLogger.Info(
                $"共享手牌图像已存在，跳过重复添加（原版此处只打一条 ERROR）: player={playerId}");
        }

        return false;
    }
}
