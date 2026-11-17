using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Logging;

namespace YuWanCardLocalCoopFix;

/// <summary>
/// 商店条目"购买一次"的双版本入口（本 compat mod 自带一份，不依赖主 mod 的程序集）。
///
/// <para>0.113 是 <c>MerchantEntry.OnTryPurchaseUnsynchronized(inventory, ignoreCost)</c>；
/// 0.111 及更早是 <c>OnTryPurchaseWrapper(inventory, ignoreCost)</c>（逐行等价，只是改名）。
/// 直接写其中一个名字 ⇒ 另一个分支上要么编不过、要么运行时 <c>MissingMethodException</c>
/// （"队友帮付"会走到 catch 把金币退回，功能静默失效）。</para>
///
/// <para>⚠ compat mod 是**单份 dll**（不像主 mod 拆 loader + 每分支载荷），所以这里必须反射。</para>
/// </summary>
internal static class MerchantPurchaseBridge
{
    private static readonly MethodInfo? RenamedStyle = AccessTools.Method(
        typeof(MerchantEntry),
        "OnTryPurchaseUnsynchronized",
        new[] { typeof(MerchantInventory), typeof(bool) });

    private static readonly MethodInfo? LegacyStyle = AccessTools.Method(
        typeof(MerchantEntry),
        "OnTryPurchaseWrapper",
        new[] { typeof(MerchantInventory), typeof(bool) });

    private static int _warned;

    /// <summary>该版本用的是哪个入口（日志用）。</summary>
    internal static string ResolvedStyle =>
        RenamedStyle != null ? "OnTryPurchaseUnsynchronized(0.113+)"
        : LegacyStyle != null ? "OnTryPurchaseWrapper(0.112-)"
        : "未解析到";

    /// <summary>
    /// 让某个商店条目尝试购买一次。找不到入口/调用失败一律返回 <c>false</c>（调用方按"没成交"退款）。
    /// </summary>
    internal static async Task<bool> TryPurchaseAsync(
        MerchantEntry? entry,
        MerchantInventory? inventory,
        bool ignoreCost)
    {
        if (entry == null)
        {
            return false;
        }

        MethodInfo? method = RenamedStyle ?? LegacyStyle;
        if (method == null)
        {
            if (System.Threading.Interlocked.Exchange(ref _warned, 1) == 0)
            {
                Log.Warn($"{Entry.LogPrefix} 商店购买入口未解析到"
                    + "（既无 OnTryPurchaseUnsynchronized 也无 OnTryPurchaseWrapper）⇒ 队友帮付无法结算货物");
            }

            return false;
        }

        object? result = method.Invoke(entry, new object?[] { inventory, ignoreCost });
        return result is Task<bool> task && await task.ConfigureAwait(false);
    }
}
