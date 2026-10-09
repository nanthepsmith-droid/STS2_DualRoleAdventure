#nullable enable
using System;
using System.Collections.Generic;
using LocalMultiControl.Scripts.Models.Relics;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 战灵召唤（原 ④「地狱战神」，#21）**Phase 1** 的本地化注入。mod 不打包 loc 文件，一律**运行期**往游戏已有表里
/// <c>MergeWith</c>（与 <see cref="LocalWakuuRelicLocalization"/> / <see cref="LocalWakuuDaddyLocalization"/> 同款）。
///
/// <list type="bullet">
/// <item>遗物读 <c>relics</c> 表的 <c>.title</c> / <c>.description</c> / <c>.eventDescription</c> / <c>.flavor</c>；</item>
/// <item>休息处选项读 <c>rest_site_ui</c> 表的 <c>OPTION_LMC_SPLIT_SPIRIT.name/.description</c>
///   （<c>RestSiteOption.Title/Description</c> 是硬拼的 key，表里没有就取不到值 ⇒ 按钮连名字都设不上）。</item>
/// </list>
/// **缺键会让 <c>LocString</c> 抛 <c>LocException</c>** 冒穿渲染（遗物描述页 / 休息区按钮）⇒ 必须注入。
///
/// 注入时机：mod 初始器阶段 3 一次（那时 <c>LocManager.Instance</c> 还是 null，实际空转）+
/// <c>LocManager.Initialize()</c> 后置补丁 <see cref="Patch.HellwarContentPatch"/>（主菜单之前 ⇒ 遗物库能渲染）
/// + 每次进局。
/// </summary>
internal static class LocalWakuuHellwarLocalization
{
    /// <summary>游戏 <c>relics</c> 表（遗物名称 / 描述 / 事件描述 / 风味文本）。</summary>
    internal const string RelicsTable = "relics";

    /// <summary>游戏 <c>rest_site_ui</c> 表（休息区选项标题 / 描述）。</summary>
    internal const string RestSiteTable = "rest_site_ui";

    private static bool _localeCallbackSubscribed;

    public static void Initialize()
    {
        LocManager? locManager = LocManager.Instance;
        if (locManager == null)
        {
            // 初始化早期 LocManager 可能尚未就绪，延后到 Initialize 后置补丁 / 进局时重试。
            return;
        }

        InjectLocalization(locManager);
        if (_localeCallbackSubscribed)
        {
            return;
        }

        locManager.SubscribeToLocaleChange(InjectLocalization);
        _localeCallbackSubscribed = true;
    }

    private static void InjectLocalization()
    {
        LocManager? locManager = LocManager.Instance;
        if (locManager == null)
        {
            return;
        }

        InjectLocalization(locManager);
    }

    private static void InjectLocalization(LocManager locManager)
    {
        try
        {
            string relicEntry = ModelDb.GetId<LocalWakuuHellwarEarring>().Entry;
            locManager.GetTable(RelicsTable).MergeWith(new Dictionary<string, string>
            {
                [$"{relicEntry}.title"] = LocalModText.HellwarEarringTitle,
                [$"{relicEntry}.description"] = LocalModText.HellwarEarringDescription,
                [$"{relicEntry}.eventDescription"] = LocalModText.HellwarEarringDescription,
                [$"{relicEntry}.flavor"] = LocalModText.HellwarEarringFlavor,
            });

            string optionKeyPrefix = "OPTION_" + HellwarSplitSpiritOption.SplitSpiritOptionId;
            locManager.GetTable(RestSiteTable).MergeWith(new Dictionary<string, string>
            {
                [$"{optionKeyPrefix}.name"] = LocalModText.SplitSpiritOptionName,
                [$"{optionKeyPrefix}.description"] = LocalModText.SplitSpiritOptionDescription,
            });
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[地狱战神] 注入战灵召唤本地化失败: {exception.Message}");
        }
    }
}
