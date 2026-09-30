using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 把「当前归属角色」同步到游戏**Run 级同步器**的私有 `_localPlayerId` 字段
/// （R4 第一刀：从 `LocalMultiControlRuntime` 抽出的独立职责）。
///
/// **为什么需要**：游戏把"本机玩家是谁"存进各 Run 同步器自己的私有字段，切人 / 换前后台之后
/// 这些字段仍指向上一个人 ⇒ 奖励领取、事件选项、休息区选择、商店删牌等流程会认错人
/// （典型症状：`SelectLocalReward` 校验 `reward.Player == LocalPlayer` 失败而软锁）。
/// 所以每次切换归属角色都要把它们一起对齐。
///
/// **口径**：`EventSynchronizer` 走的是**事件流所属者**而不是传入席位 ——
/// `UseSingleEventFlow`（默认）下事件由主席位统一作答，所以钉主席位；
/// 关掉该档时才跟当前归属角色。（`FoulPotionPatch` 依赖这条差异，改前先看那份注释。）
///
/// **与 R3 的关系**：反射写一律走唯一入口 <see cref="SynchronizerLocalPlayerId"/>（B3），
/// 本类不再自己 `AccessTools.Field`；写入失败按 `组件:类型` 去重只记一次 WARN（原样保留，避免每帧刷屏）。
/// </summary>
internal static class RunSynchronizerSeatSync
{
    /// <summary>已报过"同步失败"的 `组件:类型` 键（同一种失败只记一条 WARN）。</summary>
    private static readonly HashSet<string> FieldSyncFailures = new HashSet<string>();

    /// <summary>
    /// 把 Run 级同步器的「本地玩家」字段对齐到 <paramref name="playerId"/>。
    /// 不在局内（`RunManager.IsInProgress == false`）时什么都不做。
    /// </summary>
    internal static void Apply(ulong playerId)
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return;
        }

        ulong eventOwnerPlayerId = LocalSelfCoopContext.UseSingleEventFlow
            ? LocalSelfCoopContext.PrimaryPlayerId
            : playerId;
        TrySetLocalPlayerId(RunManager.Instance.EventSynchronizer, eventOwnerPlayerId, nameof(RunManager.EventSynchronizer));
        TrySetLocalPlayerId(RunManager.Instance.RewardsSetSynchronizer, playerId, nameof(RunManager.RewardsSetSynchronizer));
        TrySetLocalPlayerId(RunManager.Instance.RewardSynchronizer, playerId, nameof(RunManager.RewardSynchronizer));
        TrySetLocalPlayerId(RunManager.Instance.RestSiteSynchronizer, playerId, nameof(RunManager.RestSiteSynchronizer));
        TrySetLocalPlayerId(RunManager.Instance.OneOffSynchronizer, playerId, nameof(RunManager.OneOffSynchronizer));
        TrySetLocalPlayerId(RunManager.Instance.TreasureRoomRelicSynchronizer, playerId, nameof(RunManager.TreasureRoomRelicSynchronizer));
        TrySetLocalPlayerId(RunManager.Instance.FlavorSynchronizer, playerId, nameof(RunManager.FlavorSynchronizer));
    }

    private static void TrySetLocalPlayerId(object? target, ulong playerId, string componentName)
    {
        if (target == null)
        {
            return;
        }

        try
        {
            SynchronizerLocalPlayerId.TryWrite(target, playerId);
        }
        catch (Exception exception)
        {
            string key = $"{componentName}:{target.GetType().Name}";
            if (FieldSyncFailures.Add(key))
            {
                LocalMultiControlLogger.Warn($"同步 {key} 的 _localPlayerId 失败: {exception.Message}");
            }
        }
    }
}
