#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Models.Relics;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.Runtime.PureLogic;
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

                // ② 预设遗物（r255）：同样在启动时打一份 + 与"件数阶梯"（成本 − 1）对账。
                IReadOnlyList<RelicModel> relics = HellwarWarGodRelics.PresetRelicsOf(tier);
                LocalMultiControlLogger.Info(
                    $"[地狱战神] 预设遗物已就绪: 档位={tier}, {HellwarWarGodRelics.DescribePresetRelics(tier)}");
                int expected = HellwarSummonPolicy.ResolvePresetRelicCount(tier);
                if (relics.Count != expected)
                {
                    LocalMultiControlLogger.Warn(
                        $"[地狱战神] 预设遗物件数与阶梯不符（配置表被改过？）: 档位={tier}, "
                        + $"实际={relics.Count} 件 / 期望={expected} 件（口径 = 成本 − 1）");
                }
            }
        }
        catch (System.Exception exception)
        {
            LocalMultiControlLogger.Warn($"[地狱战神] 预设卡组就绪自检失败（召唤时会重新解析并打 WARN）: {exception.Message}");
        }

        // ③ 预设表（预设战神 v2 地基，r269）：加载 `hellwar_presets.json`（编辑器 / 玩家手写的产物）
        //    并把每档的**默认预设**打一行 —— 双重作用：① 启动就能看见"这一档会用哪套内容"；
        //    ② 趁 ModelDb 已就绪把预设里的卡 / 遗物 id **当场校验**（认不出的当场点名，不用等召唤）。
        //    只打日志不抛（与上面两块同款）。
        try
        {
            LocalHellwarPresetStore.Reload("modeldb-preload");
            HellwarPresetTable table = LocalHellwarPresetStore.Table;
            if (table.All.Count == 0)
            {
                // ⚠ 这里**不能 return**（r273 实机踩过）：下面的 ④ 预设候选自检与预设表无关，
                //    一旦提前返回，"没写预设文件"这个最常见的情况下 ④ 永远打不出来（锚点全缺 ⇒ 判读时误以为链路没走到）。
                LocalMultiControlLogger.Info(
                    "[地狱战神] 预设表: 未配置（文件不存在或为空），四档用内置默认。"
                    + $"想自定义（或给编辑器用）就写 {LocalHellwarPresetStore.FilePath}");
            }
            else
            {
                foreach (HellwarWarGodTier tier in Enum.GetValues<HellwarWarGodTier>())
                {
                    HellwarPresetDefinition? definition = table.DefaultForTier(tier);
                    if (definition == null)
                    {
                        LocalMultiControlLogger.Info($"[地狱战神] 预设表: 档位={tier} 未配置预设，用内置默认");
                        continue;
                    }

                    HellwarWarGodPreset runtime = HellwarPresetRuntimeMapping.ToRuntimePreset(
                        definition, tier, HellwarWarGodPresets.Resolve(tier));
                    int deckCount = HellwarWarGodDeckPlan.Parse(runtime.DeckText)?.Count ?? 0;
                    LocalMultiControlLogger.Info(
                        $"[地狱战神] 预设表: 档位={tier} 默认预设=\"{runtime.DisplayName}\"(id={runtime.PresetId}, "
                        + $"皮肤={runtime.SkinKey}, 卡组={deckCount} 张, 遗物="
                        + $"{runtime.RelicEntries?.Count.ToString() ?? "继承内置"} 件, 全升级={runtime.UpgradeDeck}, "
                        + $"额外血={runtime.BonusHp})");
                    VerifyPresetIds(tier, runtime);
                }
            }
        }
        catch (System.Exception exception)
        {
            LocalMultiControlLogger.Warn($"[地狱战神] 预设表自检失败（召唤时会重新解析并打 WARN）: {exception.Message}");
        }

        // ④ 预设候选（预设战神 v2 · Phase B，r273）：把每档"第二层选预设"的候选打一行。
        //    关键契约 = **普通档必须是全部可玩角色**（皮肤能在角色表里解析到；卡组/遗物随角色自带），
        //    其余档 = 内置默认 + `hellwar_presets.json` 挂到该档的预设。只打日志不抛。
        try
        {
            foreach (HellwarWarGodTier tier in HellwarSummonPolicy.SelectableTiers)
            {
                IReadOnlyList<HellwarWarGodPreset> concrete = HellwarPresetCatalog.ConcreteFor(tier);
                bool offersPicker = HellwarPresetChoices.ShouldOfferPicker(concrete);
                string names = concrete.Count == 0
                    ? ""
                    : string.Join(", ", concrete.Select((preset) => HellwarPresetCatalog.DescribePresetShort(preset)));
                LocalMultiControlLogger.Info(
                    $"[地狱战神] 预设候选已就绪: 档位={tier}, {concrete.Count} 套[{names}]"
                    + (offersPicker ? "（召唤时弹第二层「选角色预设」）" : "（只有一套 ⇒ 直接用它，不弹第二层）"));
            }
        }
        catch (System.Exception exception)
        {
            LocalMultiControlLogger.Warn($"[地狱战神] 预设候选自检失败（召唤时会重新构建）: {exception.Message}");
        }
    }

    /// <summary>趁 ModelDb 已就绪，把预设里的卡 / 遗物 id 当场查一遍（认不出的 WARN 点名，不抛）。</summary>
    private static void VerifyPresetIds(HellwarWarGodTier tier, HellwarWarGodPreset preset)
    {
        if (preset.DeckText != null)
        {
            IReadOnlyList<HellwarWarGodDeckEntry>? entries = HellwarWarGodDeckPlan.Parse(preset.DeckText);
            if (entries != null)
            {
                List<string> missingCards = entries
                    .Select((entry) => entry.Entry)
                    .Where((entry) => HellwarWarGodDecks.LookupByEntry(entry) == null)
                    .Distinct()
                    .ToList();
                if (missingCards.Count > 0)
                {
                    LocalMultiControlLogger.Warn(
                        $"[地狱战神] 预设卡组里有认不出的卡牌（召唤时会跳过）: 档位={tier}, 预设=\"{preset.DisplayName}\", "
                        + $"认不出=[{string.Join(", ", missingCards)}]");
                }
            }
        }

        if (preset.RelicEntries != null)
        {
            List<string> missingRelics = preset.RelicEntries
                .Where((entry) => HellwarWarGodRelics.LookupByEntry(entry) == null)
                .Distinct()
                .ToList();
            if (missingRelics.Count > 0)
            {
                LocalMultiControlLogger.Warn(
                    $"[地狱战神] 预设遗物里有认不出的 id（召唤时会跳过）: 档位={tier}, 预设=\"{preset.DisplayName}\", "
                    + $"认不出=[{string.Join(", ", missingRelics)}]");
            }
        }
    }
}
