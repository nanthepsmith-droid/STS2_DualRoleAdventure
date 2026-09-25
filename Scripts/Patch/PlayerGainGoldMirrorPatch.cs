using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Rewards;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rooms;

namespace LocalMultiControl.Scripts.Patch;

internal static class GoldMirrorSuppressionContext
{
    private static readonly AsyncLocal<int> SuppressDepth = new();

    internal static bool ShouldSuppressGoldMirror => SuppressDepth.Value > 0;

    internal static void EnterSuppression()
    {
        SuppressDepth.Value++;
    }

    internal static async Task<T> ExitSuppressionWhenCompleteAsync<T>(Task<T> task)
    {
        try
        {
            return await task;
        }
        finally
        {
            ExitSuppressionOnce();
        }
    }

    internal static void ExitSuppressionOnce()
    {
        if (SuppressDepth.Value > 0)
        {
            SuppressDepth.Value--;
        }
    }
}

[HarmonyPatch(typeof(PlayerCmd), nameof(PlayerCmd.GainGold))]
internal static class PlayerGainGoldMirrorPatch
{
    private static readonly AsyncLocal<bool> IsMirroring = new();

    [HarmonyPostfix]
    private static void Postfix(decimal amount, Player player, bool wasStolenBack, ref Task __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (amount <= 0m || IsMirroring.Value)
        {
            return;
        }

        // r147：**来源**也必须是本地席位。旧实现只过滤了"镜像给谁"，于是第三方席位（Co-op Bots 的
        // 合成 Bot）自己的奖励金币被当成"本地角色共享"复制给两个真人（2026-09-25 实机 12 条
        // `owner=12716757972810793218`）；而且 Co-op Bots 的金币作弊是 Prefix 把金额 ×3、
        // 我们的镜像 Postfix 拿到的是**已放大**的值 ⇒ 真人跟着拿 3 倍。
        if (!MirrorSeatPolicy.IsMirrorableSource(player.NetId, LocalSelfCoopContext.LocalPlayerIds))
        {
            return;
        }

        if (GoldMirrorSuppressionContext.ShouldSuppressGoldMirror)
        {
            LocalMultiControlLogger.Info($"遗物流程金币跳过镜像: amount={amount}, owner={player.NetId}");
            return;
        }

        // 汇总奖励流程中，每个角色已独立生成金币奖励，不需要镜像
        if (CombatRewardMergeContext.IsActive)
        {
            return;
        }

        bool isCombatRewardContext = player.RunState.CurrentRoom is CombatRoom && !CombatManager.Instance.IsInProgress;
        // 每角色独立结算：占卜奖励金币只归拾取者，不再镜像到其余角色
        bool isCrystalSphereContext = CrystalSphereMirrorRuntime.CrossPlayerMirroringEnabled
            && CrystalSphereMirrorRuntime.IsInCrystalSphereEventContext(player);
        if (!isCombatRewardContext && !isCrystalSphereContext)
        {
            return;
        }

        __result = MirrorGoldToOtherPlayersAsync(amount, player, wasStolenBack, __result);
    }

    private static async Task MirrorGoldToOtherPlayersAsync(decimal amount, Player sourcePlayer, bool wasStolenBack, Task originalTask)
    {
        await originalTask;

        // 只镜像给**本地席位**：第三方席位（Co-op Bots 的合成 Bot）是独立队友，不该拿我们的金币
        // （更严重的是会污染它的决策状态；r145 之前一律按"本地角色"处理）。
        // 来源端同样必须是本地席位（r147，见 MirrorSeatPolicy）。
        var otherPlayers = sourcePlayer.RunState.Players
            .Where((candidate) => MirrorSeatPolicy.ShouldMirrorTo(sourcePlayer.NetId, candidate.NetId, LocalSelfCoopContext.LocalPlayerIds))
            .ToList();
        if (otherPlayers.Count == 0)
        {
            return;
        }

        IsMirroring.Value = true;
        try
        {
            foreach (Player otherPlayer in otherPlayers)
            {
                await PlayerCmd.GainGold(amount, otherPlayer, wasStolenBack);
            }

            LocalMultiControlLogger.Info(
                $"事件/流程金币已同步到其余角色: amount={amount}, owner={sourcePlayer.NetId}, mirrored={string.Join(",", otherPlayers.Select((player) => player.NetId))}");
        }
        finally
        {
            IsMirroring.Value = false;
        }
    }
}
