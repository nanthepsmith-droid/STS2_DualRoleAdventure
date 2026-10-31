using HarmonyLib;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 锁住 <c>compat-mods/ThirdPartyHotPathFix</c> 赖以成立的两条 Harmony 语义：
///
/// 1. 前缀 `return false` 跳过目标方法时，目标方法的 **`ref` 参数保持调用方原值**
///    —— 三家第三方过滤链的 `Postfix(ref IEnumerable&lt;AbstractModel&gt; __result)` 是个 `void` 方法，
///    我们就是靠这一点让 `__result` 原样穿过去的。
/// 2. 零参数前缀也能生效（热路径上不能为了拿 `__result` 而让 Harmony 每次都建 `object[]` 参数数组）。
///
/// 这两条如果哪天变了，闸门就会静默地**把 listener 序列变成 null** = 整个 hook 分发崩掉。
/// </summary>
[TestFixture]
public sealed class HarmonySkipSemanticsTests
{
    private const string HarmonyId = "test.thirdparty.hotpath.skip-semantics";

    private static bool s_prefixRan;

    private static void Probe(ref int value)
    {
        value = 42;
    }

    private static bool SkipPrefix()
    {
        s_prefixRan = true;
        return false;
    }

    [Test]
    public void 前缀返回false时ref参数保持不变()
    {
        var harmony = new Harmony(HarmonyId);
        try
        {
            s_prefixRan = false;
            harmony.Patch(
                AccessTools.Method(typeof(HarmonySkipSemanticsTests), nameof(Probe)),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonySkipSemanticsTests), nameof(SkipPrefix))));

            int value = 5;
            Probe(ref value);

            Assert.That(s_prefixRan, Is.True, "零参数前缀必须被执行");
            Assert.That(value, Is.EqualTo(5), "被跳过的 postfix 不能再改 ref 参数");
        }
        finally
        {
            harmony.UnpatchAll(HarmonyId);
        }
    }
}
