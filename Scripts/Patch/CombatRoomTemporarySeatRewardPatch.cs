#nullable enable

using System.Collections.Generic;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 【战神奖励】临时席位（战神）绝不写进战斗房的 `extra_rewards`，读档时解析不到的条目直接丢掉（r287）。
///
/// **事故原形**（2026-10-19 实机「现在直接无法读档了」，日志 `godot__20261019-174712__r286.log`）：
/// <code>
/// [ERROR] Exception loading multiplayer run: System.NullReferenceException
///    at Reward.FromSerializable_Patch10(SerializableReward save, Player player)
///    at CombatRoom.&lt;&gt;c__DisplayClass39_0.&lt;FromSerializable&gt;b__0(SerializableReward sr)
///    at CombatRoom.FromSerializable(...)
///    at NGame.LoadRun(...)
/// </code>
/// 存档里 `pre_finished_room.extra_rewards` 有一项键是 **"4096"（战神席位的 NetId）**：
/// <code>
/// "extra_rewards": { "4096": [ { "reward_type": "special_card",
///                                 "custom_description_encounter_source_id": "ENCOUNTER.THIEVING_HOPPER_WEAK",
///                                 "special_card": { "id": "CARD.DIRGE", ... } } ] }
/// </code>
/// 也就是说：战神在战斗里触发了"额外奖励"（这里是偷宝怪把卡还回来的 `special_card`），
/// 游戏的 `CombatRoom._extraRewards` 按 `Player` 记账 ⇒ 存进了战神席位。
/// **而战神是临时席位，读档时不在 `runState.Players` 里** ⇒ `CombatRoom.FromSerializable` 里
/// `runState.GetPlayer(4096)` 返回 null ⇒ 传给 `Reward.FromSerializable(sr, player)` ⇒ NRE ⇒ **读档直接失败**。
/// （R12「战神无战斗奖励」只挡住了**结算界面**，没挡住底层 `AddExtraReward`，所以奖励对象照样落地。）
///
/// **三道防线**：
/// ① `AddExtraReward` 前缀：临时席位直接不收（与 R12 口径一致，杜绝新增）；
/// ② `ToSerializable` 后置：写盘前把临时席位的键剔掉（兜底历史条目）；
/// ③ `FromSerializable` 前缀：**解析不到玩家的键直接移除**（救已经被写坏的老存档 —— 本机那份就是靠这一条救回来的）。
///
/// ③ 不是"临时席位专属"：任何找不到主人的奖励本来也无法发放，丢掉是唯一合理结果。
/// </summary>
[HarmonyPatch(typeof(CombatRoom))]
internal static class CombatRoomTemporarySeatRewardPatch
{
    /// <summary>已打过"战神触发额外奖励被丢弃"日志的席位（去重，避免每次战斗刷屏）。</summary>
    private static readonly HashSet<ulong> LoggedSkippedSeats = new();

    /// <summary>① 临时席位不收额外奖励（R12：战神无战斗奖励）。</summary>
    [HarmonyPrefix]
    [HarmonyPatch(nameof(CombatRoom.AddExtraReward))]
    private static bool AddExtraRewardPrefix(Player player)
    {
        try
        {
            if (player == null || !LocalSelfCoopContext.IsTemporarySeat(player.NetId))
            {
                return true;
            }

            bool first;
            lock (LoggedSkippedSeats)
            {
                first = LoggedSkippedSeats.Count < 32 && LoggedSkippedSeats.Add(player.NetId);
            }

            if (first)
            {
                LocalMultiControlLogger.Info(
                    $"[战神奖励] 临时席位触发了额外奖励 ⇒ 不收（否则会写进存档、读档时找不到主人而 NRE）: "
                    + $"seat={player.NetId}");
            }

            return false;
        }
        catch (System.Exception exception)
        {
            LocalMultiControlLogger.Warn($"[战神奖励] 判定临时席位失败（放行原逻辑）: {exception.Message}");
            return true;
        }
    }

    /// <summary>② 写盘前剔除临时席位的奖励条目（兜底）。</summary>
    [HarmonyPostfix]
    [HarmonyPatch(nameof(CombatRoom.ToSerializable))]
    private static void ToSerializablePostfix(SerializableRoom __result)
    {
        try
        {
            if (__result == null || __result.ExtraRewards.Count == 0)
            {
                return;
            }

            List<ulong>? remove = null;
            foreach (ulong netId in __result.ExtraRewards.Keys)
            {
                if (LocalSelfCoopContext.IsTemporarySeat(netId))
                {
                    (remove ??= new List<ulong>()).Add(netId);
                }
            }

            if (remove == null)
            {
                return;
            }

            foreach (ulong netId in remove)
            {
                __result.ExtraRewards.Remove(netId);
                LocalMultiControlLogger.Info(
                    $"[战神奖励] 存档前剔除临时席位的额外奖励条目: seat={netId}");
            }
        }
        catch (System.Exception exception)
        {
            LocalMultiControlLogger.Warn($"[战神奖励] 存档前剔除失败（已忽略）: {exception.Message}");
        }
    }

    /// <summary>③ 读档时解析不到主人的奖励直接丢弃（救已写坏的老存档）。</summary>
    [HarmonyPrefix]
    [HarmonyPatch(nameof(CombatRoom.FromSerializable), new[] { typeof(SerializableRoom), typeof(IRunState) })]
    private static void FromSerializablePrefix(SerializableRoom serializableRoom, IRunState? runState)
    {
        try
        {
            if (serializableRoom?.ExtraRewards == null || serializableRoom.ExtraRewards.Count == 0)
            {
                return;
            }

            if (runState == null)
            {
                // 保留原版行为：没有 run state 又有额外奖励时，让 `FromSerializable` 自己抛那句 InvalidOperationException。
                return;
            }

            List<ulong>? orphaned = null;
            foreach (ulong netId in serializableRoom.ExtraRewards.Keys)
            {
                // 以 runState 里能否解析为准（与原版 FromSerializable 的解析方式一致）；解析不到 = 没有主人。
                if (runState.GetPlayer(netId) == null)
                {
                    (orphaned ??= new List<ulong>()).Add(netId);
                }
            }

            if (orphaned == null)
            {
                return;
            }

            foreach (ulong netId in orphaned)
            {
                int dropped = serializableRoom.ExtraRewards[netId]?.Count ?? 0;
                serializableRoom.ExtraRewards.Remove(netId);
                LocalMultiControlLogger.Info(
                    $"[战神奖励] 读档时丢弃找不到主人的额外奖励（避免 Reward.FromSerializable 空引用）: "
                    + $"seat={netId}, rewards={dropped}, temporarySeat={LocalSelfCoopContext.IsTemporarySeat(netId)}");
            }
        }
        catch (System.Exception exception)
        {
            LocalMultiControlLogger.Warn($"[战神奖励] 读档前清理失败（放行原逻辑）: {exception.Message}");
        }
    }
}
