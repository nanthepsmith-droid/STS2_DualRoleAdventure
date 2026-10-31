using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「接管第三方过滤链」这套做法的语义锁（2026-10-31，v1.3.0）。
///
/// 背景：三家（Koishi / figure_Saya / newsanguo）挂在 `CombatState.IterateHookListeners` 的 postfix
/// 只有一句 `__result = Filter(__result)`，被 tier-1 内联后**前缀补丁完全拦不住**
/// （同局日志每家只命中 `跳过=1` 一次，而采样里 Filter 照样在跑，合计 20~25% 主线程）。
/// 转译器路线在本游戏内置 Harmony（MonoMod 后端）上落带分支的 IL 会 NRE，也走不通。
///
/// 所以最终做法是：`Harmony.Unpatch(original, 对方的patch方法)` → 挂我们自己的接力 postfix。
/// 这里用假的原版方法 + 假的第三方 postfix 把三条边界锁死：
/// 1. 卸掉对方方法后，闸门为真 ⇒ 结果与「补丁从未挂过」完全一致（不是空序列、也不是被过滤过）；
/// 2. 闸门为假 ⇒ 接力调用原方法，结果与「对方补丁仍在」一致（玩法不变）；
/// 3. 只卸指定那一个方法，同 original 上的其它补丁不受影响（不能连带别人的效果一起干掉）。
/// </summary>
[TestFixture]
public sealed class VendorFilterHandoverTests
{
    private static bool _skip;
    private static int _vendorCalls;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IEnumerable<int> Source() => new[] { 1, 2, 3 };

    /// <summary>假装是第三方 postfix：把 1 滤掉。</summary>
    private static void VendorPostfix(ref IEnumerable<int> __result)
    {
        _vendorCalls++;
        __result = __result.Where(x => x > 1);
    }

    /// <summary>假装是同一 original 上另一个第三方补丁：把 3 滤掉。</summary>
    private static void OtherVendorPostfix(ref IEnumerable<int> __result) => __result = __result.Where(x => x < 3);

    /// <summary>假装是我们的接力 postfix（闸门 → 不能跳过时调原方法）。</summary>
    private static void Relay(ref IEnumerable<int> __result)
    {
        if (_skip)
        {
            return;
        }

        VendorPostfix(ref __result);
    }

    private static readonly MethodInfo OriginalMethod =
        typeof(VendorFilterHandoverTests).GetMethod(nameof(Source), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly MethodInfo VendorMethod =
        typeof(VendorFilterHandoverTests).GetMethod(nameof(VendorPostfix), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly MethodInfo OtherVendorMethod =
        typeof(VendorFilterHandoverTests).GetMethod(nameof(OtherVendorPostfix), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly MethodInfo RelayMethod =
        typeof(VendorFilterHandoverTests).GetMethod(nameof(Relay), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static Harmony PatchVendorOnly()
    {
        var harmony = new Harmony("localmulticontrol.tests.handover." + Guid.NewGuid().ToString("N"));
        harmony.Patch(OriginalMethod, postfix: new HarmonyMethod(VendorMethod));
        return harmony;
    }

    [Test]
    public void 闸门为真时_结果与从未打过补丁完全一致()
    {
        var harmony = PatchVendorOnly();
        try
        {
            Assert.That(Source(), Is.EqualTo(new[] { 2, 3 }), "先确认对方的补丁真的在生效");

            harmony.Unpatch(OriginalMethod, VendorMethod);
            harmony.Patch(OriginalMethod, postfix: new HarmonyMethod(RelayMethod));

            _skip = true;
            _vendorCalls = 0;

            Assert.That(Source(), Is.EqualTo(new[] { 1, 2, 3 }), "跳过 = 原样返回，绝不能变成空序列");
            Assert.That(_vendorCalls, Is.EqualTo(0), "跳过时不能再调对方的方法（否则一点没省）");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [Test]
    public void 闸门为假时_接力结果与对方补丁在场时一致()
    {
        var harmony = PatchVendorOnly();
        try
        {
            harmony.Unpatch(OriginalMethod, VendorMethod);
            harmony.Patch(OriginalMethod, postfix: new HarmonyMethod(RelayMethod));

            _skip = false;
            _vendorCalls = 0;

            Assert.That(Source(), Is.EqualTo(new[] { 2, 3 }), "机制在场必须原样调用对方逻辑");
            Assert.That(_vendorCalls, Is.EqualTo(1), "每次分发只调一次对方方法");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [Test]
    public void 只卸指定方法_同original上的其它补丁不受影响()
    {
        var harmony = new Harmony("localmulticontrol.tests.handover." + Guid.NewGuid().ToString("N"));
        try
        {
            harmony.Patch(OriginalMethod, postfix: new HarmonyMethod(VendorMethod));
            harmony.Patch(OriginalMethod, postfix: new HarmonyMethod(OtherVendorMethod));
            Assert.That(Source(), Is.EqualTo(new[] { 2 }), "两个补丁叠加：1 和 3 都被滤");

            harmony.Unpatch(OriginalMethod, VendorMethod);
            harmony.Patch(OriginalMethod, postfix: new HarmonyMethod(RelayMethod));

            _skip = true;
            Assert.That(Source(), Is.EqualTo(new[] { 1, 2 }), "只该少掉对方那一个效果，别人的补丁必须还在");
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }
}
