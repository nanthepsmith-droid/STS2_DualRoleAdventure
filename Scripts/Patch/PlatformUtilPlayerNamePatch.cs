using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Platform;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 本地席位的玩家名统一成 <c>botN</c>（语言无关，2026-11-18 §排期 6）。
///
/// <para><b>为什么两条都要补</b>：原版两条入口 —— <c>GetPlayerName</c> 是「BBCode 安全版」
/// （内部调 Raw 再转义），<c>GetPlayerNameRaw</c> 是纯文本版。此前我们只拦了前者，
/// 而**战斗内左侧玩家列表用的恰恰是 Raw**（<c>NMultiplayerPlayerState._Ready</c>）⇒ 列表里显示的还是
/// Steam 昵称。同族只用 Raw 的还有 <c>Creature.cs</c>（creature 的玩家名，tooltip 用）。</para>
///
/// <para><b>只接管本机会话席位</b>：<see cref="LocalSeatLabels.TryResolve"/> 命中才改，其余 id 一律
/// `return true` 放行（真联机队友的昵称一个字都不能动）。</para>
/// </summary>
[HarmonyPatch(typeof(PlatformUtil), nameof(PlatformUtil.GetPlayerName))]
internal static class PlatformUtilGetPlayerNamePatch
{
    [HarmonyPrefix]
    private static bool Prefix(ulong playerId, ref string __result)
    {
        string? label = LocalSeatLabels.TryResolve(playerId);
        if (label == null)
        {
            return true;
        }

        __result = label;
        return false;
    }
}

/// <summary>
/// 同上的纯文本版入口（<c>GetPlayerNameRaw</c>）—— 战斗内左侧玩家列表 / creature 名走的是这条。
/// 与 <see cref="PlatformUtilGetPlayerNamePatch"/> 共用同一个解析入口（<see cref="LocalSeatLabels"/>），
/// 两条入口永远给同一个称呼。
/// </summary>
[HarmonyPatch(typeof(PlatformUtil), nameof(PlatformUtil.GetPlayerNameRaw))]
internal static class PlatformUtilGetPlayerNameRawPatch
{
    [HarmonyPrefix]
    private static bool Prefix(ulong playerId, ref string __result)
    {
        string? label = LocalSeatLabels.TryResolve(playerId);
        if (label == null)
        {
            return true;
        }

        __result = label;
        return false;
    }
}
