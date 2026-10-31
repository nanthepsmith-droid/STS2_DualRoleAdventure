using System;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using ThirdPartyHotPathFix.Scripts.Gates;

namespace ThirdPartyHotPathFix.Scripts.Patches;

/// <summary>
/// 三家（Koishi / figure_Saya / newsanguo）过滤链的「接管」实现（v1.3.0）。
///
/// 为什么是接管而不是挂前缀 / 转译器（2026-10-31 两轮实测）：
/// - 挂前缀：这三家的 patch 方法体只有一句 `__result = Filter(__result)`，**tier-1 被 JIT 内联**
///   ⇒ 前缀只覆盖 tier-0（同局日志每家只命中 `跳过=1` 一次，而采样里 Filter 照样在跑）。
/// - 写转译器：本游戏内置的 Harmony（MonoMod 后端）在落带分支的 IL 时直接
///   `NullReferenceException @ CecilILGenerator.Emit(OpCode, Label)`（真实单测抓到的，
///   游戏与测试用的是同一个 `0Harmony.dll`）⇒ 分支指令走不通。
/// - 接管：把对方挂在 `CombatState.IterateHookListeners` 上的那**一个** postfix 卸下来
///   （`Harmony.Unpatch(original, vendorMethod)`），换成我们自己的 postfix；
///   我们的 postfix 里先问闸门，**机制不在场直接返回**（对方什么都不做 = 语义等价），
///   在场才反射调用对方原来的 patch 方法（原样执行，玩法完全不变）。
///
/// 安全边界：
/// - 只有「拿到对方 patch 方法 + 卸下成功」才接管；任何一步失败就保持原样（宁可变慢）。
/// - 接力时拿不到对方方法句柄 ⇒ 什么都不做（**绝不静默丢掉对方的过滤效果**）。
/// - 卸下的只是那一个方法，不动对方的其它补丁。
/// </summary>
internal static class VendorFilterHandover
{
    private static MethodInfo? _koishiPostfix;
    private static MethodInfo? _sayaPostfix;
    private static MethodInfo? _newsanguoPostfix;

    /// <summary>三家是否已被我们接管（日志用）。</summary>
    internal static bool KoishiTakenOver { get; private set; }

    internal static bool SayaTakenOver { get; private set; }

    internal static bool NewsanguoTakenOver { get; private set; }

    internal static void RememberVendor(string key, MethodInfo vendor)
    {
        switch (key)
        {
            case "koishi":
                _koishiPostfix ??= vendor;
                break;
            case "saya":
                _sayaPostfix ??= vendor;
                break;
            case "newsanguo":
                _newsanguoPostfix ??= vendor;
                break;
        }
    }

    internal static void MarkTakenOver(string label, bool ok)
    {
        switch (label)
        {
            case "koishi":
                KoishiTakenOver |= ok;
                break;
            case "saya":
                SayaTakenOver |= ok;
                break;
            case "newsanguo":
                NewsanguoTakenOver |= ok;
                break;
        }
    }

    /// <summary>Koishi <c>SealAllRelicPatch.Postfix</c> 的接管者（签名必须与目标 original 的 ref 参数一致）。</summary>
    public static void KoishiRelay(ref IEnumerable<AbstractModel> __result)
    {
        if (ArmedGates.ShouldSkipKoishiSealAll())
        {
            return;
        }

        MethodInfo? vendor = _koishiPostfix;
        if (vendor == null)
        {
            return;
        }

        object?[] args = { __result };
        vendor.Invoke(null, args);
        __result = (IEnumerable<AbstractModel>)args[0]!;
    }

    /// <summary>figure_Saya <c>FilterSuppressedDebuffs</c> 的接管者。</summary>
    public static void SayaRelay(ref IEnumerable<AbstractModel> __result)
    {
        if (ArmedGates.ShouldSkipSayaResistance())
        {
            return;
        }

        MethodInfo? vendor = _sayaPostfix;
        if (vendor == null)
        {
            return;
        }

        object?[] args = { __result };
        vendor.Invoke(null, args);
        __result = (IEnumerable<AbstractModel>)args[0]!;
    }

    /// <summary>newsanguo <c>AbilitySuppressionPatch.Postfix</c> 的接管者（它还要 CombatState）。</summary>
    public static void NewsanguoRelay(CombatState __instance, ref IEnumerable<AbstractModel> __result)
    {
        if (ArmedGates.ShouldSkipNewsanguo())
        {
            return;
        }

        MethodInfo? vendor = _newsanguoPostfix;
        if (vendor == null)
        {
            return;
        }

        object?[] args = { __instance, __result };
        vendor.Invoke(null, args);
        __result = (IEnumerable<AbstractModel>)args[1]!;
    }
}
