using System.Collections.Generic;
using ThirdPartyHotPathFix.Scripts.Gates;
using MegaCrit.Sts2.Core.Models;

namespace ThirdPartyHotPathFix.Scripts.Patches;

/// <summary>
/// 四个「前缀判定」补丁体 + 一个能力增删监听。
///
/// 这些方法都跑在每秒上万次的热路径上 —— **只做一次判定、不做别的事、不打日志不分配**
/// （日志由 <see cref="VerdictCounter"/> 在结论翻转时打）。
///
/// 跳过方式说明：
/// - 三家（Koishi / Saya / newsanguo）被我们补丁的是**它们的 Harmony 后缀方法本身**
///   （`Postfix` / `FilterSuppressedDebuffs`），那是个 `void` 方法、`__result` 是 **ref 参数**，
///   前缀 `return false` 只跳过方法体 ⇒ ref 参数原样保留（= 上游给的 listener 序列），语义等价。
/// - YUICore 被补丁的是 `Wrap`（**有返回值**）：跳过时必须自己把 `listeners` 写回 `__result`，
///   否则调用方拿到 null。
/// </summary>
internal static class VendorFilterPatches
{
    /// <summary>Koishi <c>SealAllRelicPatch.Postfix</c> 的前缀。</summary>
    public static bool SkipKoishiSealAll() => ArmedGates.ShouldSkipKoishiSealAll();

    /// <summary>figure_Saya <c>GeneralizedResistanceSuppressionPatch.FilterSuppressedDebuffs</c> 的前缀。</summary>
    public static bool SkipSayaGeneralizedResistance() => ArmedGates.ShouldSkipSayaResistance();

    /// <summary>newsanguo <c>AbilitySuppressionPatch.Postfix</c> 的前缀。</summary>
    public static bool SkipNewsanguoAbilitySuppression() => ArmedGates.ShouldSkipNewsanguo();

    /// <summary>YUICore <c>YuiRelicTriggerReplay.Wrap</c> 的前缀（参数名必须与原名一致：<c>listeners</c>）。</summary>
    public static bool SkipYuiRelicTriggerReplay(
        IEnumerable<AbstractModel> listeners,
        ref IEnumerable<AbstractModel> __result)
    {
        if (!ArmedGates.ShouldSkipYuiRelicReplay())
        {
            return true;
        }

        __result = listeners;
        return false;
    }

    /// <summary><c>Creature.ApplyPowerInternal</c> / <c>RemovePowerInternal</c> 的后缀：只递增版本号。</summary>
    public static void OnPowerMutated() => ArmedGates.BumpPowerVersion();
}
