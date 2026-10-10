using System.Collections.Generic;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 让 <c>LocalContext.GetMe(IPlayerCollection)</c> 认**临时席位（战神）**（战灵召唤 Phase 3 / r249 修复）。
///
/// <para><b>为什么必须</b>：战神刻意**不进 `runState.Players`**（H1 硬约束），而出牌路径上大量
/// UI / 状态刷新会调 `LocalContext.GetMe(runState)`（`runState` 是 `IPlayerCollection`）——
/// 原实现是 <c>playerCollection.GetPlayer(NetId) ?? throw</c>（`LocalContext.cs:32`），
/// 战神在 `runState.Players` 里查不到 ⇒ **每次出牌必抛** `Local player not found in player collection.`</para>
///
/// <para><b>r249 实机铁证</b>：<c>瓦库评分出牌: player=4096, card=…</c> 紧接着
/// <c>瓦库选择器作用域异常退出: … error=Local player not found in player collection.</c> +
/// <c>瓦库看门狗重启失败: player=4096 …</c>；牌一张都打不出去 ⇒ 战神永远不 ready
/// （<c>TryEndIdleWarGodTurns</c> 见到"还有可打牌"就不收口）⇒ **回合卡死、真人无法结束回合**。</para>
///
/// <para><b>收窄条件</b>（三条同时满足才接管，原版 / 真人席位 / 瓦库形态席位一点不动）：
/// ① 上下文（<see cref="LocalContext.NetId"/>）**正对着临时席位** —— 只有我们的托管出牌链会把它钉成战神
/// （<c>LocalWakuuRelicRuntime.RunWatchdogAsync</c> 的钉扎 + finally 还原）；
/// ② 该席位**在册**（<see cref="HellwarWarGodFactory.FindSpawnedWarGod"/> 拿得到 Player 实例 ——
/// <c>GetMe</c> 的参数只有 id，查不到时原实现会抛，所以必须由我们按 id 反查）；
/// ③ 原集合里**确实查不到**（能查到就保持原逻辑，不改变已有行为）。</para>
///
/// <para>⚠ 只补 <c>IPlayerCollection</c> 那个重载：<c>GetMe(ICombatState)</c> 走 <c>CombatState.GetPlayer</c>，
/// 而战神**在** `CombatState.Players` 里（`AddPlayer` 加过）⇒ 本来就不抛；
/// <c>GetMe(IEnumerable&lt;Player&gt;)</c> 那条本来也**不抛**（返回 null）。</para>
/// </summary>
[HarmonyPatch(typeof(LocalContext), nameof(LocalContext.GetMe), new[] { typeof(IPlayerCollection) })]
internal static class LocalContextTemporarySeatGetMePatch
{
    /// <summary>已打过日志的席位（只用于防刷屏，不参与判定）。</summary>
    private static readonly HashSet<ulong> LoggedSeats = new();

    private const int MaxLoggedSeats = 32;

    [HarmonyPrefix]
    private static bool Prefix(IPlayerCollection? playerCollection, ref Player? __result)
    {
        ulong? netId = LocalContext.NetId;
        if (!netId.HasValue || playerCollection == null)
        {
            return true;
        }

        if (!LocalSelfCoopContext.IsTemporarySeat(netId.Value))
        {
            return true;
        }

        // 原本查得到 ⇒ 保持原逻辑（不改变任何已有行为）。
        if (playerCollection.GetPlayer(netId.Value) != null)
        {
            return true;
        }

        Player? warGod = HellwarWarGodFactory.FindSpawnedWarGod(netId.Value);
        if (warGod == null)
        {
            return true;
        }

        __result = warGod;
        LogOnce(netId.Value);
        return false;
    }

    private static void LogOnce(ulong netId)
    {
        lock (LoggedSeats)
        {
            if (LoggedSeats.Count >= MaxLoggedSeats || !LoggedSeats.Add(netId))
            {
                return;
            }
        }

        LocalMultiControlLogger.Info(
            $"LocalContext.GetMe 已按临时席位（战神）解析: seat={netId}（战神不进 runState.Players，"
            + "出牌路径的 GetMe(runState) 靠这条口子拿到它）");
    }
}
