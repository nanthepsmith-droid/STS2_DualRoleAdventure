using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// ④「地狱战神」**内容层**的两个早期挂点（2026-10-06，r225→r226 修正挂点）。
///
/// r225 的教训：把自检挂在「每次进局」（<c>RunManager.Launch</c>）上 ⇒
/// 用户在主菜单开了卡牌库、压根没进局，锚点 `[地狱战神验证]` **一次都没打**（实机日志实证）。
/// 而且卡牌库是**主菜单就能打开**的界面，本地化/入池这两件事都必须在那之前就绪。
/// 所以改为挂在「初始化早期」的两个公开静态方法上（见下），谁都不依赖"进局"。
/// </summary>
[HarmonyPatch]
internal static class WakuuHellGodContentPatch
{
    /// <summary>
    /// ① <c>LocManager.Initialize()</c> 之后立刻注入卡牌本地化。
    ///
    /// 为什么必须这么早：卡牌固定读 `cards` 表的 `&lt;entry&gt;.title/.description`，
    /// **缺键会让 `LocString` 抛 `LocException`** 冒穿卡牌渲染；而卡牌库（`NCardLibrary`）在
    /// **主菜单**就能打开 —— mod 初始器跑在 `LocManager.Initialize()` **之前**（那时 `Instance` 还是 null，
    /// `LocalWakuuHellGodLocalization.Initialize()` 只能空转返回），
    /// 原来只在 `OnRunLaunched` 补一次 ⇒ 主菜单直接开卡牌库这条路上本地化是缺的。
    /// 这个时序点（`LocManager` 刚建好 `Instance`、`ModelDb`/主菜单都还没开始）是唯一稳妥的早期注入点。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(LocManager), nameof(LocManager.Initialize))]
    private static void PostfixLocManagerInitialize()
    {
        LocalWakuuHellGodLocalization.Initialize();
    }

    /// <summary>
    /// ② <c>ModelDb.Preload()</c> 之后校验自定卡是否真的进了卡池。
    ///
    /// 为什么挂这里：
    /// <list type="bullet">
    /// <item>登记（<c>ModHelper.AddModelToPool</c>）发生在**游戏初始化前**，那时 `ModelDb` 还没建好、
    ///   读池会触发 `GenerateAllCards` ⇒ `ModelNotFound`；</item>
    /// <item><c>Preload()</c> 正是「卡池首次访问并冻结」的那一刻，**在主菜单显示后不久**、
    ///   任何进局之前 ⇒ 不需要进局就能验证；</item>
    /// <item>此时 `AtlasManager.LoadAllAtlases()` 已跑完（立绘能读到）、`LocManager` 早已就绪
    ///   （后者在 `ModelDb.Init` 之前就 `Initialize` 了）⇒ 标题/描述/立绘三项检查都成立。</item>
    /// </list>
    /// 先补一次本地化注入，再自检（保证「本地化=是」验的是真实可用状态，而不是"因为没注入才=否"）。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Preload))]
    private static void PostfixModelDbPreload()
    {
        LocalWakuuHellGodLocalization.Initialize();
        WakuuHellGodCardProbe.VerifyPoolMembership();
    }
}
