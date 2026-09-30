using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 共享镜像（把一次"获得/失去"复制给其它**本地席位**）的共用判据与工具（R2 去重复）。
///
/// 背景：镜像五件套（`RewardPotionMirrorPatch` / `RewardCardMirrorPatch` / `PlayerGainGoldMirrorPatch` /
/// `PlayerLoseGoldMirrorPatch` / `RelicCmdPatch`）与 `SpoilsMapPatch` 各自复写了同一套东西 ——
/// 功能开关门控（6 处）、战斗结束奖励上下文（3 处）、水晶球事件上下文（5 处）、
/// "战斗奖励 or 水晶球"合判（3 处）、目标席位枚举（5 处）、`RewardSynchronizer` 私有字段反射（2 处逐字相同）、
/// 镜像作用域开关（4 处）。这里收成一处，**逐字保持原有语义**（行为零变化）。
///
/// ⚠ 各补丁**仍然各自持有** <see cref="Scope"/> 实例：镜像的幂等隔离原本就是每补丁一份
/// （某一类镜像进行中，不妨碍另一类各自的判定）—— 这条语义**不要**合并成全局单例。
/// </summary>
internal static class LocalRewardMirror
{
    /// <summary>镜像功能总开关（本地多控 + 单人冒险模式）。</summary>
    internal static bool IsMirrorFeatureEnabled
        => LocalSelfCoopContext.IsEnabled && LocalSelfCoopContext.UseSingleAdventureMode;

    /// <summary>来源席位是否可以发起共享镜像（第三方席位如 Co-op Bots 的 Bot 不参与）。</summary>
    internal static bool IsMirrorableSource(Player source)
        => MirrorSeatPolicy.IsMirrorableSource(source.NetId, LocalSelfCoopContext.LocalPlayerIds);

    /// <summary>
    /// 本该镜像到的目标席位：**来源与目标都必须是本地席位**，且不是自己镜给自己。
    /// 每个补丁原本都写了一遍同样的 LINQ，这里收成一处。
    /// </summary>
    internal static IEnumerable<Player> SelectTargets(Player source)
        => source.RunState.Players.Where((candidate) =>
            MirrorSeatPolicy.ShouldMirrorTo(source.NetId, candidate.NetId, LocalSelfCoopContext.LocalPlayerIds));

    /// <summary>
    /// 战斗**结束后**的奖励上下文：在战斗房间里、且战斗已结束。
    /// （战斗进行中的获得不镜像 —— 那是战斗内效果，不是战利品结算。）
    /// </summary>
    internal static bool IsCombatRewardContext(Player source)
        => source.RunState.CurrentRoom is CombatRoom && !CombatManager.Instance.IsInProgress;

    /// <summary>
    /// 水晶球事件上下文：该事件里每个角色独立结算（占卜奖励只归拾取者/揭示者）。
    /// </summary>
    internal static bool IsCrystalSphereRewardContext(Player source)
        => CrystalSphereMirrorRuntime.CrossPlayerMirroringEnabled
            && CrystalSphereMirrorRuntime.IsInCrystalSphereEventContext(source);

    /// <summary>该不该镜像这次获得：战斗奖励上下文 或 水晶球事件上下文。</summary>
    internal static bool IsMirrorableRewardContext(Player source)
        => IsCombatRewardContext(source) || IsCrystalSphereRewardContext(source);

    /// <summary>
    /// `RewardSynchronizer` 的 `_localPlayerId` / `_playerCollection` 是私有的，反射取"这次奖励属于谁"。
    /// （`RewardPotionMirrorPatch` 与 `RewardCardMirrorPatch` 原本各写了一份逐字相同的实现。）
    /// </summary>
    internal static Player? ResolveSynchronizerSource(RewardSynchronizer synchronizer)
    {
        ulong localPlayerId = SynchronizerLocalPlayerId.ReadOrZero(synchronizer, typeof(RewardSynchronizer));
        if (localPlayerId == 0UL)
        {
            return null;
        }

        IPlayerCollection? playerCollection = AccessTools.Field(typeof(RewardSynchronizer), "_playerCollection")
            ?.GetValue(synchronizer) as IPlayerCollection;
        return playerCollection?.GetPlayer(localPlayerId);
    }

    /// <summary>
    /// 镜像作用域：置位期间"由镜像产生的获得"不会再被同一补丁镜像一次（防递归/防重复）。
    /// 用 `using (Scope.Enter()) { … }` 包住整个镜像过程（原来是 `IsMirroring.Value = true` + try/finally）。
    /// </summary>
    internal sealed class Scope
    {
        private readonly AsyncLocal<bool> _isMirroring = new();

        internal bool IsActive => _isMirroring.Value;

        internal IDisposable Enter() => new Handle(_isMirroring);

        private sealed class Handle : IDisposable
        {
            private readonly AsyncLocal<bool> _flag;

            internal Handle(AsyncLocal<bool> flag)
            {
                _flag = flag;
                _flag.Value = true;
            }

            public void Dispose() => _flag.Value = false;
        }
    }
}
