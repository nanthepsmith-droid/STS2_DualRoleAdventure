#nullable enable

using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// ④「瓦库的爹」**内容层**的两个早期挂点（r226 的挂点经验，2026-10-07 随功能改名沿用）。
///
/// r225 的教训：把自检挂在「每次进局」（<c>RunManager.Launch</c>）上 ⇒
/// 用户在主菜单开了卡牌库、压根没进局，锚点 `[瓦库的爹验证]` **一次都没打**（实机日志实证）。
/// 而且卡牌库/遗物库是**主菜单就能打开**的界面，本地化/入池这两件事都必须在那之前就绪。
/// 所以挂在「初始化早期」的两个公开静态方法上（见下），谁都不依赖"进局"。
/// </summary>
[HarmonyPatch]
internal static class WakuuDaddyContentPatch
{
    /// <summary>
    /// ① <c>LocManager.Initialize()</c> 之后立刻注入卡牌与遗物本地化。
    ///
    /// 为什么必须这么早：卡牌固定读 `cards` 表的 `&lt;entry&gt;.title/.description`、
    /// 遗物读 `relics` 表的同名键，**缺键会让 `LocString` 抛 `LocException`** 冒穿渲染；
    /// 而卡牌库/遗物库在**主菜单**就能打开 —— mod 初始器跑在 `LocManager.Initialize()` **之前**
    /// （那时 `Instance` 还是 null，`LocalWakuuDaddyLocalization.Initialize()` 只能空转返回），
    /// 只在 `OnRunLaunched` 补一次的话，主菜单直接开卡牌库这条路本地化是缺的。
    /// 这个时序点（`LocManager` 刚建好 `Instance`、`ModelDb`/主菜单都还没开始）是唯一稳妥的早期注入点。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(LocManager), nameof(LocManager.Initialize))]
    private static void PostfixLocManagerInitialize()
    {
        LocalWakuuDaddyLocalization.Initialize();
    }

    /// <summary>
    /// ② <c>ModelDb.Preload()</c> 之后校验内容是否真的进了池。
    ///
    /// 为什么挂这里：
    /// <list type="bullet">
    /// <item>登记（<c>ModHelper.AddModelToPool</c>）发生在**游戏初始化前**，那时 `ModelDb` 还没建好、
    ///   读池会触发 `GenerateAllCards` ⇒ `ModelNotFound`；</item>
    /// <item><c>Preload()</c> 正是「卡池/遗物池首次访问并冻结」的那一刻，**在主菜单显示后不久**、
    ///   任何进局之前 ⇒ 不需要进局就能验证；</item>
    /// <item>此时 `AtlasManager.LoadAllAtlases()` 已跑完（立绘能读到）、`LocManager` 早已就绪
    ///   （后者在 `ModelDb.Init` 之前就 `Initialize` 了）⇒ 池/标题/描述/立绘四项检查都成立。</item>
    /// </list>
    /// 先补一次本地化注入，再自检（保证「题/述=是」验的是真实可用状态，而不是"因为没注入才=否"）。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Preload))]
    private static void PostfixModelDbPreload()
    {
        LocalWakuuDaddyLocalization.Initialize();
        WakuuDaddyContentProbe.VerifyContent();
    }
}
