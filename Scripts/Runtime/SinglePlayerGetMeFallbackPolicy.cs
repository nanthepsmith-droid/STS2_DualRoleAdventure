using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「单人局 GetMe 回退」纯逻辑（2026-10-23，用户实机黑屏 ×3）。
///
/// <para><b>根因</b>：原版单人开局固定 <c>Player.CreateForNewRun(character, unlockState, 1uL)</c>
/// ⇒ 玩家 NetId = **1**；而本地多控（本地回环）下 <c>LocalContext.NetId</c> 是 Steam id ⇒
/// 任何第三方只要在 <c>RunState.CreateForNewRun</b> 的后置里调 <c>LocalContext.GetMe(runState)</c>
/// 就会抛 <c>Local player not found in player collection.</c>，把开局流程打断 ⇒ 黑屏。
/// 实机案例：猪猪 <c>CloudAnalyticsRunStartPatch</c>（2026-10-23）。</para>
///
/// <para><b>判据演化</b>：r314 首版要求"本地多控会话启用" ⇒ 实测退出大厅（IsEnabled=false）后照样黑屏，r315 删；
/// r315 还有"该 NetId 不在本地席位表" ⇒ 实测**断线后席位表残留上局 Steam id**，恰好包含请求的 netId ⇒
/// 又被挡住，r316 删。最终判据回归**最本质的一条**：</para>
///
/// <para><b>最终判据</b>（全部满足才回退）：
/// ① 原实现查不到本地 NetId；② 集合里只有 1 个玩家；③ **那个玩家的 NetId == 1**（原版单人开局的硬编码占位 id）。
/// ③ 是关键 —— 它只在"原版单人开局被本地 NetId 语义污染"时成立；
/// 会话错位类场景（集合里是真实 Steam 席位）天然不触发，不需要再去查席位表。</para>
/// </summary>
internal static class SinglePlayerGetMeFallbackPolicy
{
    /// <summary>原版单人开局固定的占位 NetId（<c>NGame.StartNewSingleplayerRun</c> 里 <c>1uL</c>）。</summary>
    public const ulong VanillaSingleplayerNetId = 1UL;

    public static bool ShouldFallback(
        bool localPlayerFound,
        int playerCount,
        ulong solePlayerNetId)
    {
        if (localPlayerFound)
        {
            return false;
        }

        if (playerCount != 1)
        {
            return false;
        }

        return solePlayerNetId == VanillaSingleplayerNetId;
    }
}
