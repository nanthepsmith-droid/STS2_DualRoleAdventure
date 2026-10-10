#nullable enable
using System.Collections.Generic;
using HarmonyLib;
using LocalMultiControl.Scripts.Models.Relics;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 战灵召唤（原 ④「地狱战神」，#21）**Phase 1** 内容层的两个早期挂点
/// （挂法与 <see cref="WakuuDaddyContentPatch"/> 完全同款，理由见那边的注释）。
///
/// 两个挂点（<c>LocManager.Initialize</c> / <c>ModelDb.Preload</c>）都是"全 mod 共用的早期初始化点"，
/// 与「瓦库的爹」目标方法相同 ⇒ **不新增目标方法**；但因为是**新补丁类**，
/// 仍需登记 <c>PatchDomainMap</c> 与 <c>Scripts/Tools/baselines/targets.baseline.txt</c>（门禁会点名）。
/// </summary>
[HarmonyPatch]
internal static class HellwarContentPatch
{
    /// <summary>
    /// ① <c>LocManager.Initialize()</c> 之后立刻注入遗物 / 休息处选项本地化。
    ///
    /// 为什么必须这么早：遗物读 <c>relics</c> 表的同名键、休息区选项读 <c>rest_site_ui</c> 的硬拼键，
    /// **缺键会让 <c>LocString</c> 抛 <c>LocException</c>** 冒穿渲染；而遗物库/卡牌库在**主菜单**就能打开。
    /// mod 初始器跑在 <c>LocManager.Initialize()</c> 之前（那时 <c>Instance</c> 是 null，注入只能空转返回），
    /// 只在进局时补一次的话，主菜单那条路是缺的。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(LocManager), nameof(LocManager.Initialize))]
    private static void PostfixLocManagerInitialize()
    {
        LocalWakuuHellwarLocalization.Initialize();
    }

    /// <summary>
    /// ② <c>ModelDb.Preload()</c> 之后补一次注入 + 打一条"遗物已就绪"锚点。
    ///
    /// 这个时点是「卡池 / 遗物池首次访问并冻结」的那一刻、**在主菜单显示后不久、任何进局之前** ⇒
    /// 不需要进局就能验证遗物确实被 <c>ModelDb</c> 扫到（登记发生在游戏初始化前，那时读不了模型表）。
    /// 只打日志不做断言（与 <c>WakuuDaddyContentProbe</c> 的"入池四项检查"相比刻意做轻：
    /// 本轮只有一个遗物、没有自定卡池项，出错面小）。
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Preload))]
    private static void PostfixModelDbPreload()
    {
        LocalWakuuHellwarLocalization.Initialize();

        try
        {
            RelicModel earring = ModelDb.Relic<LocalWakuuHellwarEarring>();
            LocalMultiControlLogger.Info(
                $"[地狱战神] 遗物【地狱耳环】已就绪: entry={earring.Id.Entry}, 计数显示={earring.ShowCounter}");
        }
        catch (System.Exception exception)
        {
            LocalMultiControlLogger.Warn($"[地狱战神] 遗物【地狱耳环】就绪自检失败（描述页可能异常）: {exception.Message}");
        }

        // 预设卡组自检（2026-10-10）：把三个高档的**内置默认卡组**在启动时打印成"配置文本"。
        // 双重作用：① 验证每个内置默认卡牌的**类型引用**真的能在 ModelDb 里解析出来
        //（内置默认写的是 `ModelDb.Card<T>()`，理论上编译期就校验了，这里做运行期兜底）；
        // ② 日志里直接给玩家一份**可以复制进 `hellwarDeck*` 键的模板**（想改从这份改最省事）。
        // 只打日志、不抛（与上面那条锚点同款：出错面小，别把初始化带崩）。
        try
        {
            HellwarWarGodTier[] tiers =
            {
                HellwarWarGodTier.Rare,
                HellwarWarGodTier.Epic,
                HellwarWarGodTier.Ancient,
            };
            foreach (HellwarWarGodTier tier in tiers)
            {
                IReadOnlyList<CardModel>? deck = HellwarWarGodDecks.DefaultDeckOf(tier);
                LocalMultiControlLogger.Info(
                    $"[地狱战神] 预设卡组已就绪: 档位={tier}, 张数={deck?.Count ?? 0}, "
                    + $"内容={HellwarWarGodDecks.DescribeBuiltinDeck(tier)}");
            }
        }
        catch (System.Exception exception)
        {
            LocalMultiControlLogger.Warn($"[地狱战神] 预设卡组就绪自检失败（召唤时会重新解析并打 WARN）: {exception.Message}");
        }
    }
}
