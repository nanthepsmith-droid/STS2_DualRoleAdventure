using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(OneOffSynchronizer), nameof(OneOffSynchronizer.DoLocalTreasureRoomRewards))]
internal static class OneOffSynchronizerSpoilsMapPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref Task<int> __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        __result = ResolveSpoilsMapForAllLocalPlayersAsync(__result);
    }

    private static async Task<int> ResolveSpoilsMapForAllLocalPlayersAsync(Task<int> originalTask)
    {
        int totalGold = await originalTask;

        try
        {
            Player? localPlayer = LocalContext.GetMe(RunManager.Instance.DebugOnlyGetState());
            if (localPlayer?.RunState?.Players == null || localPlayer.RunState.Players.Count <= 1)
            {
                return totalGold;
            }

            IRunState runState = localPlayer.RunState;
            if (!runState.CurrentMapCoord.HasValue)
            {
                return totalGold;
            }

            MapPoint? currentPoint = runState.Map.GetPoint(runState.CurrentMapCoord.Value);
            bool currentHasSpoilsQuest = currentPoint?.Quests.Any((quest) => quest is SpoilsMap) ?? false;
            if (!currentHasSpoilsQuest)
            {
                // r82 兜底：读档/跨会话继续后，宝箱点上的 SpoilsMap quest 偶发丢失（地图 UI 的 X 标记
                // 仍保留、但 MapPoint.Quests 未恢复，原版与旧补丁都会因此静默不触发藏宝图结算）。
                // 藏宝图生效的幕使用 SpoilsActMap：全幕只有一个宝箱点。因此只要全图已无任何 spoils
                // quest、却仍有人持有本幕 SpoilsMap 卡，就判定为 quest 丢失修复场景，在所在宝箱房结算；
                // 若 quest 还在其它宝箱点上（说明玩家进错了宝箱）则维持不结算，避免误发。
                bool anySpoilsQuestElsewhere = runState.Map.GetAllMapPoints()
                    .Any((point) => point != currentPoint && point.Quests.Any((quest) => quest is SpoilsMap));
                if (anySpoilsQuestElsewhere)
                {
                    LocalMultiControlLogger.Info(
                        "宝箱房间无藏宝图 quest 且该 quest 位于其它宝箱点，跳过藏宝图结算。");
                    return totalGold;
                }

                LocalMultiControlLogger.Warn(
                    $"宝箱房间检测到藏宝图 quest 丢失（读档后未恢复），按持卡兜底结算: coord={runState.CurrentMapCoord}");
            }

            List<Player> playersWithSpoilsMap = runState.Players
                .Where((player) => player.Deck.Cards.OfType<SpoilsMap>().Any((map) => map.SpoilsActIndex == runState.CurrentActIndex))
                .ToList();
            if (playersWithSpoilsMap.Count == 0)
            {
                return totalGold;
            }

            int syncedCount = 0;
            foreach (Player player in playersWithSpoilsMap)
            {
                SpoilsMap? spoilsMap = player.Deck.Cards
                    .OfType<SpoilsMap>()
                    .FirstOrDefault((map) => map.SpoilsActIndex == runState.CurrentActIndex);
                if (spoilsMap == null)
                {
                    continue;
                }

                int gainedGold = await spoilsMap.OnQuestComplete();
                totalGold += gainedGold;
                syncedCount++;
                LocalMultiControlLogger.Info($"宝箱房间触发藏宝图结算: player={player.NetId}, gold={gainedGold}");
            }

            LocalMultiControlLogger.Info($"宝箱房间藏宝图批处理完成: processed={syncedCount}, players={runState.Players.Count}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"宝箱房间藏宝图批处理失败: {exception.Message}");
        }

        return totalGold;
    }
}
