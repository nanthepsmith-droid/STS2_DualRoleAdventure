#nullable enable

using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Localization;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 【瓦库形态】/【永久低语耳环】两件**遗物**的本地化早期挂点
/// （挂法与 <see cref="WakuuDaddyContentPatch"/> / <see cref="HellwarContentPatch"/> 完全同款）。
///
/// <para><b>为什么要单开这一类</b>（2026-10-26 实机日志判读，r322）：
/// 这两件遗物的本地化原先只在两处注入 —— mod 初始器（那时 `LocManager.Instance` 还是 null，**空转返回**）
/// 和 `LocalMultiControlRuntime.OnRunLaunched`（**已经进局**之后）。可是**没进局**的界面也会读它们：
/// 主菜单的「载入历史 / 遗物图鉴」按玩家历史列出曾经拥有的遗物名。
/// 于是 BaseLib 的 `LocTable.GetRawText` 前缀（`HasEntry` 为假就 WARN 并返回 "relics.&lt;key&gt;" 字符串）
/// 在我们注入之前就打了一串
/// `GetRawText: Key 'LOCAL_WAKUU_FORM_RELIC.title' not found in table 'relics'`
/// （2026-10-26 一局 17 条），而且**返回的是 `relics.LOCAL_WAKUU_FORM_RELIC.title` 这种垃圾串** ⇒ 玩家可见。</para>
///
/// <para><b>为什么挂 <c>LocManager.Initialize()</c> 后置</b>：mod 初始器跑在它之前（`Instance` 为 null），
/// 而它之后、主菜单显示之前，是唯一"实例已就绪 + 任何界面都还没读"的时序点
/// （与「瓦库的爹」/ 战灵召唤同款，那两个类没有这类 WARN 就是证据）。</para>
///
/// <para>目标方法（`LocManager.Initialize`）已被上面两个类用着 ⇒ **不新增目标方法**；
/// 但因为是**新补丁类**，仍需登记 <c>PatchDomainMap</c> 与
/// <c>Scripts/Tools/baselines/targets.baseline.txt</c>（门禁 S7 会点名）。</para>
/// </summary>
[HarmonyPatch]
internal static class WakuuRelicContentPatch
{
    /// <summary><c>LocManager.Initialize()</c> 之后立刻注入两件瓦库遗物的标题/描述。</summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(LocManager), nameof(LocManager.Initialize))]
    private static void PostfixLocManagerInitialize()
    {
        LocalWakuuRelicLocalization.Initialize();
    }
}
