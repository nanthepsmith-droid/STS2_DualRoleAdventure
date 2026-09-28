using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(RewardSynchronizer), nameof(RewardSynchronizer.SyncLocalObtainedCard))]
internal static class RewardCardMirrorPatch
{
    private static readonly LocalRewardMirror.Scope MirrorScope = new();

    [HarmonyPostfix]
    private static void Postfix(RewardSynchronizer __instance, CardModel card)
    {
        if (MirrorScope.IsActive || !LocalRewardMirror.IsMirrorFeatureEnabled)
        {
            return;
        }

        // 每角色独立结算：占卜卡牌奖励只归拾取者，不再镜像到其余角色
        Player? sourcePlayer = LocalRewardMirror.ResolveSynchronizerSource(__instance);
        if (sourcePlayer == null || !LocalRewardMirror.IsCrystalSphereRewardContext(sourcePlayer))
        {
            return;
        }

        // r147：**来源**也必须是本地席位（旧实现只过滤了"镜像给谁"）。
        if (!LocalRewardMirror.IsMirrorableSource(sourcePlayer))
        {
            return;
        }

        TaskHelper.RunSafely(MirrorCardToOtherPlayersAsync(sourcePlayer, card));
    }

    private static async Task MirrorCardToOtherPlayersAsync(Player sourcePlayer, CardModel card)
    {
        using (MirrorScope.Enter())
        {
            // 目标席位沿用 CrystalSphereMirrorRuntime.GetOtherPlayers（本补丁原本就没走席位判据，保持不变）
            foreach (Player otherPlayer in CrystalSphereMirrorRuntime.GetOtherPlayers(sourcePlayer))
            {
                CardModel mirroredCard = otherPlayer.RunState.CreateCard(card, otherPlayer);
                await CardPileCmd.Add(mirroredCard, PileType.Deck);
                LocalMultiControlLogger.Info(
                    $"水晶球事件卡牌奖励同步: source={sourcePlayer.NetId}, target={otherPlayer.NetId}, card={mirroredCard.Id.Entry}");
            }
        }
    }
}
