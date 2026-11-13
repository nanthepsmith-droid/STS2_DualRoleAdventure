using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 极速模式（Blitz）宝箱「每人 2 件」（r352）。
///
/// <para><b>原版口径</b>（`TreasureRoomRelicSynchronizer.BeginRelicPicking`）：
/// 单人 = 每人 1 件 + 额外 1 件，`_roundsLeft = 2` ⇒ 一个人选两轮、拿 2 件；
/// 多人 = 每人 1 件 + 额外 2 件，但 `_roundsLeft = 1` ⇒ **只投一轮**，多出来的遗物按"没人投就跳过"处理。
/// 本地多控走的是多人分支，实机表现 = 2 人局宝箱里 4 件遗物，每人只能拿 1 件（另外 2 件被丢掉）。</para>
///
/// <para><b>本补丁</b>：仍然**只结算一次**（避开原版"多轮"里 `_holdersInUse.RemoveAt(relicIndex)`
/// 按下标乱序删除导致第二轮卡面与遗物错位的坑），但把投票分成两个阶段：
/// 第一阶段每人各投 1 件 → 清票 → 第二阶段每人再投 1 件 → 一次性派发。
/// 派发规则见 <see cref="BlitzTreasurePickPolicy"/>（先认玩家点的，再按席位顺序补足到 2 件）。</para>
/// </summary>
[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.OnPicked))]
internal static class BlitzTreasureDoublePickPatch
{
    [HarmonyPrefix]
    private static bool Prefix(TreasureRoomRelicSynchronizer __instance, Player player, int? index)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        try
        {
            return !BlitzTreasureDoublePickRuntime.HandlePick(__instance, player, index);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"极速宝箱双选接管失败，回退原版单轮: {exception.GetType().Name}: {exception.Message}");
            BlitzTreasureDoublePickRuntime.Reset(__instance);
            return true;
        }
    }
}

/// <summary>极速宝箱遗物池的「每人 2 件」口径补齐（原版多人只给 人数 + 2 件）。</summary>
[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking))]
internal static class BlitzTreasurePoolTopUpPatch
{
    [HarmonyPostfix]
    private static void Postfix(TreasureRoomRelicSynchronizer __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        try
        {
            BlitzTreasureDoublePickRuntime.TopUpPoolForDoublePick(__instance);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"极速宝箱遗物池补齐失败（沿用原版数量）: {exception.GetType().Name}: {exception.Message}");
        }
    }
}

internal static class BlitzTreasureDoublePickRuntime
{
    private sealed class PickState
    {
        internal readonly Dictionary<ulong, int> FirstPicks = new();

        internal bool SecondPhase;
    }

    private static readonly Dictionary<TreasureRoomRelicSynchronizer, PickState> States = new();

    private static readonly FieldInfo? RunStateField =
        AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_runState");

    private static readonly FieldInfo? VotesField =
        AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_votes");

    private static readonly FieldInfo? PredictedVoteField =
        AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_predictedVote");

    private static readonly FieldInfo? SharedGrabBagField =
        AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_sharedGrabBag");

    private static readonly FieldInfo? RngField =
        AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_rng");

    private static readonly FieldInfo? VotesChangedField =
        AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "VotesChanged");

    private static readonly FieldInfo? RelicsAwardedField =
        AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "RelicsAwarded");

    private static readonly MethodInfo? EndRelicVotingMethod =
        AccessTools.Method(typeof(TreasureRoomRelicSynchronizer), "EndRelicVoting");

    /// <summary>会话 / 退局清理（静态状态，跨战斗不能留）。</summary>
    internal static void Reset(string reason)
    {
        if (States.Count == 0)
        {
            return;
        }

        LocalMultiControlLogger.Info($"极速宝箱双选状态已清空: count={States.Count}, reason={reason}");
        States.Clear();
    }

    internal static void Reset(TreasureRoomRelicSynchronizer synchronizer)
    {
        States.Remove(synchronizer);
    }

    /// <summary>把遗物池补到「每个席位 2 件」（上限 = 本 mod 的席位口径 12）。</summary>
    internal static void TopUpPoolForDoublePick(TreasureRoomRelicSynchronizer synchronizer)
    {
        if (!TryGetBlitzContext(synchronizer, out IRunState? runState, out IReadOnlyList<Player>? players))
        {
            return;
        }

        if (synchronizer.CurrentRelics is not List<RelicModel> relics || relics.Count == 0)
        {
            return;
        }

        // r353：不再按格子上限截断 —— 12 席 × 每人 2 件 = 24 件都得放得下
        // （原版多人只给 人数 + 2 件，12 人局只有 14 件 ⇒ 实机上"该有 24 件却只看到 14 件"）。
        int target = players!.Count * BlitzTreasurePickPolicy.PicksPerPlayer;
        if (relics.Count >= target)
        {
            return;
        }

        if (SharedGrabBagField?.GetValue(synchronizer) is not RelicGrabBag grabBag
            || RngField?.GetValue(synchronizer) is not Rng rng)
        {
            LocalMultiControlLogger.Warn("极速宝箱遗物池补齐跳过：拿不到 RelicGrabBag / Rng 字段。");
            return;
        }

        int before = relics.Count;
        while (relics.Count < target)
        {
            RelicRarity rarity = RelicFactory.RollRarity(rng);
            RelicModel relic = grabBag.PullFromFront(rarity, runState!) ?? RelicFactory.FallbackRelic;
            relics.Add(relic);
        }

        LocalMultiControlLogger.Info(
            $"极速宝箱遗物池已按「每人 2 件」补齐: 玩家={players.Count}, 原版={before}, "
            + $"新增={relics.Count - before}, 最终={relics.Count}");
    }

    /// <summary>处理一次投票。返回 true = 已接管（跳过原版结算），false = 交回原版。</summary>
    internal static bool HandlePick(TreasureRoomRelicSynchronizer synchronizer, Player player, int? index)
    {
        if (!TryGetBlitzContext(synchronizer, out IRunState? runState, out IReadOnlyList<Player>? players))
        {
            return false;
        }

        if (VotesField?.GetValue(synchronizer) is not List<TreasureRoomRelicSynchronizer.PlayerVote> votes)
        {
            return false;
        }

        if (synchronizer.CurrentRelics is not IReadOnlyList<RelicModel> relics || relics.Count == 0)
        {
            return false;
        }

        int sharedCount = Math.Min(votes.Count, players!.Count);
        if (sharedCount < 2)
        {
            return false;
        }

        int slot = -1;
        for (int i = 0; i < sharedCount; i++)
        {
            if (players[i].NetId == player.NetId)
            {
                slot = i;
                break;
            }
        }

        if (slot < 0)
        {
            return false;
        }

        if (index.HasValue && (index.Value < 0 || index.Value >= relics.Count))
        {
            // 越界交给原版抛（保持既有诊断口径）。
            return false;
        }

        PickState state = GetOrCreateState(synchronizer);
        votes[slot].index = index;
        votes[slot].voteReceived = true;

        if (!state.SecondPhase)
        {
            if (index.HasValue)
            {
                state.FirstPicks[player.NetId] = index.Value;
            }

            PulseVotesChanged(synchronizer);
            if (!AllVotesReceived(votes, sharedCount))
            {
                return true;
            }

            state.SecondPhase = true;
            // 第一阶段选走的遗物仍然留在屏上（我们只结算一次、不跑原版的多轮移除动画），
            // 所以这里**把它们置灰禁用**，否则第二阶段玩家会点到"已经被拿走"的那件，
            // 那一票只能被丢弃再随机补一件（实机 12 人局就点中了）。
            DimAlreadyPickedHolders(state.FirstPicks.Values);
            ResetVotes(synchronizer, votes, sharedCount);
            NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create(LocalModText.BlitzTreasureSecondPick));
            LocalMultiControlLogger.Info(
                $"极速宝箱第一阶段完成，进入第二阶段: 已选={state.FirstPicks.Count}/{sharedCount}, "
                + $"遗物={relics.Count}");
            return true;
        }

        PulseVotesChanged(synchronizer);
        if (!AllVotesReceived(votes, sharedCount))
        {
            return true;
        }

        Settle(synchronizer, players, relics, votes, sharedCount, state);
        return true;
    }

    /// <summary>
    /// 第二阶段的"已选走"标记：把第一阶段被选走的那些格子**禁用 + 置灰**。
    ///
    /// 为什么不做真正的移除：原版多轮的移除在 `AnimateRelicAwards` 里（`_holdersInUse.RemoveAt(relicIndex)`
    /// 按下标顺序删 ⇒ 删 2 件以上时下标错位、卡面与遗物对不上，我们在 r352 就是为躲这个才改成"只结算一次"）。
    /// 置灰不改任何列表、也就不会错位，玩家一眼能看出哪些已经被拿走了。
    /// </summary>
    private static void DimAlreadyPickedHolders(IEnumerable<int> pickedIndexes)
    {
        try
        {
            NTreasureRoomRelicCollection? collection = NRun.Instance?.TreasureRoom
                ?.GetNodeOrNull<NTreasureRoomRelicCollection>("RelicCollection");
            if (collection == null)
            {
                return;
            }

            if (AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_holdersInUse")?.GetValue(collection)
                is not List<NTreasureRoomRelicHolder> holders)
            {
                return;
            }

            int dimmed = 0;
            foreach (int index in pickedIndexes.Distinct())
            {
                if (index < 0 || index >= holders.Count)
                {
                    continue;
                }

                holders[index].Disable();
                holders[index].Modulate = new Color(0.55f, 0.55f, 0.55f, 1f);
                dimmed++;
            }

            LocalMultiControlLogger.Info(
                $"极速宝箱第二阶段：第一阶段的 {dimmed} 件已置灰禁用（剩 {holders.Count - dimmed} 件可选）");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"极速宝箱已选格置灰失败(忽略): {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void Settle(
        TreasureRoomRelicSynchronizer synchronizer,
        IReadOnlyList<Player> players,
        IReadOnlyList<RelicModel> relics,
        List<TreasureRoomRelicSynchronizer.PlayerVote> votes,
        int sharedCount,
        PickState state)
    {
        int?[] firstPicks = new int?[sharedCount];
        for (int i = 0; i < sharedCount; i++)
        {
            firstPicks[i] = state.FirstPicks.TryGetValue(players[i].NetId, out int picked) ? picked : null;
        }

        int?[] secondPicks = new int?[sharedCount];
        for (int i = 0; i < sharedCount; i++)
        {
            secondPicks[i] = votes[i].voteReceived ? votes[i].index : null;
        }

        List<BlitzTreasureAssignment> assignments = BlitzTreasurePickPolicy.BuildAssignments(
            sharedCount, relics.Count, firstPicks, secondPicks);
        List<RelicPickingResult> results = new(assignments.Count);
        foreach (BlitzTreasureAssignment assignment in assignments)
        {
            results.Add(new RelicPickingResult
            {
                type = assignment.FromPick
                    ? RelicPickingResultType.OnlyOnePlayerVoted
                    : RelicPickingResultType.ConsolationPrize,
                relic = relics[assignment.RelicIndex],
                relicIndex = assignment.RelicIndex,
                player = players[assignment.PlayerSlot]
            });
        }

        List<int> unused = BlitzTreasurePickPolicy.ResolveUnusedRelicIndexes(relics.Count, assignments);
        foreach (int unusedIndex in unused)
        {
            results.Add(new RelicPickingResult
            {
                type = RelicPickingResultType.Skipped,
                relic = relics[unusedIndex],
                relicIndex = unusedIndex,
                player = null
            });
        }

        InvokeRelicsAwarded(synchronizer, results);
        EndRelicVotingMethod?.Invoke(synchronizer, null);
        TreasureRoomRelicSynchronizerPatch.SkipAutoSwitchOnceFor(synchronizer);
        States.Remove(synchronizer);

        string detail = string.Join(",", assignments.Select(a =>
            $"{players[a.PlayerSlot].NetId}<-{a.RelicIndex}{(a.FromPick ? "选" : "补")}"));
        LocalMultiControlLogger.Info(
            $"极速宝箱双选结算: 席位={sharedCount}, 遗物={relics.Count}, 派发={assignments.Count}, "
            + $"无人选={unused.Count}, 明细={detail}");
    }

    private static bool TryGetBlitzContext(
        TreasureRoomRelicSynchronizer synchronizer,
        out IRunState? runState,
        out IReadOnlyList<Player>? players)
    {
        runState = RunStateField?.GetValue(synchronizer) as IRunState;
        players = (runState as IPlayerCollection)?.Players;
        if (runState == null || players == null || players.Count < 2)
        {
            return false;
        }

        if (runState.GameMode != GameMode.Blitz)
        {
            return false;
        }

        return true;
    }

    private static PickState GetOrCreateState(TreasureRoomRelicSynchronizer synchronizer)
    {
        if (!States.TryGetValue(synchronizer, out PickState? state))
        {
            state = new PickState();
            States[synchronizer] = state;
        }

        return state;
    }

    private static bool AllVotesReceived(List<TreasureRoomRelicSynchronizer.PlayerVote> votes, int sharedCount)
    {
        for (int i = 0; i < sharedCount; i++)
        {
            if (!votes[i].voteReceived)
            {
                return false;
            }
        }

        return true;
    }

    private static void ResetVotes(
        TreasureRoomRelicSynchronizer synchronizer,
        List<TreasureRoomRelicSynchronizer.PlayerVote> votes,
        int sharedCount)
    {
        for (int i = 0; i < sharedCount; i++)
        {
            votes[i].index = null;
            votes[i].voteReceived = false;
        }

        // 原版在"所有票到齐"时才清预测票；我们自己接管了结算 ⇒ 这里也要清，否则本机玩家的投票标记不消失。
        PredictedVoteField?.SetValue(synchronizer, null);
        PulseVotesChanged(synchronizer);
    }

    private static void PulseVotesChanged(TreasureRoomRelicSynchronizer synchronizer)
    {
        Action? callback = VotesChangedField?.GetValue(synchronizer) as Action;
        callback?.Invoke();
    }

    private static void InvokeRelicsAwarded(
        TreasureRoomRelicSynchronizer synchronizer,
        List<RelicPickingResult> results)
    {
        // ⚠ 0.113 的事件签名是 Action<List<RelicPickingResult>, bool>（第二参数 = 本次是否结束）。
        Action<List<RelicPickingResult>, bool>? callback =
            RelicsAwardedField?.GetValue(synchronizer) as Action<List<RelicPickingResult>, bool>;
        callback?.Invoke(results, arg2: true);
    }
}
