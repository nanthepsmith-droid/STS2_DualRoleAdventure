using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Rewards;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(RewardSynchronizer), nameof(RewardSynchronizer.SyncLocalObtainedPotion))]
internal static class RewardPotionMirrorPatch
{
    private static readonly LocalRewardMirror.Scope MirrorScope = new();

    [HarmonyPostfix]
    private static void Postfix(RewardSynchronizer __instance, PotionModel potion)
    {
        if (!LocalRewardMirror.IsMirrorFeatureEnabled || MirrorScope.IsActive)
        {
            return;
        }

        // 汇总奖励流程中，每个角色已独立生成药水奖励，不需要镜像
        if (CombatRewardMergeContext.IsActive)
        {
            return;
        }

        Player? sourcePlayer = LocalRewardMirror.ResolveSynchronizerSource(__instance);
        if (sourcePlayer == null)
        {
            return;
        }

        // r147：**来源**也必须是本地席位（旧实现只过滤了"镜像给谁"）。
        if (!LocalRewardMirror.IsMirrorableSource(sourcePlayer))
        {
            return;
        }

        // 战斗结束奖励 或 水晶球事件（占卜药水奖励只归拾取者，靠这个判据排除）
        if (!LocalRewardMirror.IsMirrorableRewardContext(sourcePlayer))
        {
            return;
        }

        TaskHelper.RunSafely(MirrorPotionToOtherPlayersAsync(sourcePlayer, potion));
    }

    private static async Task MirrorPotionToOtherPlayersAsync(Player sourcePlayer, PotionModel potion)
    {
        using (MirrorScope.Enter())
        {
            // 只镜像给**本地席位**（第三方席位如 Co-op Bots 的 Bot 由它自己那侧负责）；
            // 来源端同样必须是本地席位（r147，见 MirrorSeatPolicy）。
            foreach (Player otherPlayer in LocalRewardMirror.SelectTargets(sourcePlayer))
            {
                PotionModel mirroredPotion = PotionModel.FromSerializable(potion.ToSerializable(-1));
                PotionProcureResult result = await PotionCmd.TryToProcure(mirroredPotion, otherPlayer);
                LocalMultiControlLogger.Info(
                    $"战利品药水同步: source={sourcePlayer.NetId}, target={otherPlayer.NetId}, potion={mirroredPotion.Id.Entry}, success={result.success}");
            }
        }
    }
}
