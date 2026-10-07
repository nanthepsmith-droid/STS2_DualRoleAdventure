#nullable enable

using System;
using System.Collections.Generic;
using LocalMultiControl.Scripts.Models.Cards;
using LocalMultiControl.Scripts.Models.Relics;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// ④「瓦库的爹」（遗物 + 三张占位牌）的本地化注入。mod 不打包 loc 文件，一律**运行期**往游戏已有表里
/// <c>MergeWith</c>（与 <see cref="LocalWakuuRelicLocalization"/> / <see cref="LocalWakuuRefineLocalization"/> 同款）。
///
/// <list type="bullet">
/// <item>卡牌固定读 <c>cards</c> 表的 <c>&lt;entry&gt;.title</c> / <c>&lt;entry&gt;.description</c>
///   （<c>CardModel.TitleLocString</c> / <c>CardModel.Description</c>）；</item>
/// <item>遗物读 <c>relics</c> 表的 <c>.title</c> / <c>.description</c> / <c>.eventDescription</c> / <c>.flavor</c>。</item>
/// </list>
/// **缺键会让 <c>LocString</c> 抛 <c>LocException</c>** 冒穿渲染（卡牌库 / 手牌 / 遗物描述页全部中招）⇒ 必须注入。
///
/// 注入时机：mod 初始器阶段 3 一次（那时 <c>LocManager.Instance</c> 还是 null，实际空转）+
/// <c>LocManager.Initialize()</c> 后置补丁 <see cref="Patch.WakuuDaddyContentPatch"/>（主菜单之前 ⇒
/// 卡牌库 / 遗物库都能渲染）+ 每次进局。
/// </summary>
internal static class LocalWakuuDaddyLocalization
{
    /// <summary>游戏 <c>cards</c> 表（所有卡牌的标题 / 描述都取自这里）。</summary>
    internal const string CardsTable = "cards";

    /// <summary>游戏 <c>relics</c> 表（遗物名称 / 描述 / 事件描述 / 风味文本）。</summary>
    internal const string RelicsTable = "relics";

    private static bool _localeCallbackSubscribed;

    public static void Initialize()
    {
        LocManager? locManager = LocManager.Instance;
        if (locManager == null)
        {
            // mod 初始器跑在 LocManager.Initialize() 之前 ⇒ 这里通常拿不到实例，
            // 延后到 Initialize 后置补丁 / 进局时重试（与遗物 / 休息区选项本地化同一处理）。
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
            Dictionary<string, string> cardEntries = new();
            AddCard(cardEntries, ModelDb.GetId<LocalWakuuDaddyShieldCard>().Entry,
                LocalModText.DaddyShieldCardTitle, LocalModText.DaddyShieldCardDescription);
            AddCard(cardEntries, ModelDb.GetId<LocalWakuuDaddyFocusCard>().Entry,
                LocalModText.DaddyFocusCardTitle, LocalModText.DaddyFocusCardDescription);
            AddCard(cardEntries, ModelDb.GetId<LocalWakuuDaddyMergeCard>().Entry,
                LocalModText.DaddyMergeCardTitle, LocalModText.DaddyMergeCardDescription);
            locManager.GetTable(CardsTable).MergeWith(cardEntries);

            string relicEntry = ModelDb.GetId<LocalWakuuDaddyRelic>().Entry;
            locManager.GetTable(RelicsTable).MergeWith(new Dictionary<string, string>
            {
                [$"{relicEntry}.title"] = LocalModText.DaddyRelicTitle,
                [$"{relicEntry}.description"] = LocalModText.DaddyRelicDescription,
                [$"{relicEntry}.eventDescription"] = LocalModText.DaddyRelicDescription,
                [$"{relicEntry}.flavor"] = LocalModText.DaddyRelicFlavor,
            });
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"注入瓦库的爹内容本地化失败: {exception.Message}");
        }
    }

    private static void AddCard(
        Dictionary<string, string> sink, string entry, string title, string description)
    {
        sink[$"{entry}.title"] = title;
        sink[$"{entry}.description"] = description;
    }
}
