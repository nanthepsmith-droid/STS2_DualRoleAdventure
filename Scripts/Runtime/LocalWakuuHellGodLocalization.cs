#nullable enable

using System;
using System.Collections.Generic;
using LocalMultiControl.Scripts.Models.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// ④「地狱战神」卡牌的本地化注入。mod 不打包 loc 文件，一律**运行期**往游戏已有表里 <c>MergeWith</c>
/// （与 <see cref="LocalWakuuRelicLocalization"/> / <see cref="LocalWakuuRefineLocalization"/> 同款）。
///
/// 卡牌固定读 <c>cards</c> 表的 <c>&lt;entry&gt;.title</c> 与 <c>&lt;entry&gt;.description</c>
/// （见 <c>CardModel.TitleLocString</c> / <c>CardModel.Description</c>）；
/// **缺键会让 <c>LocString</c> 抛 <c>LocException</c>** 冒穿卡牌渲染（卡牌库 / 手牌 / 悬停全部中招）⇒ 必须注入。
/// </summary>
internal static class LocalWakuuHellGodLocalization
{
    /// <summary>游戏 <c>cards</c> 表（所有卡牌的标题 / 描述都取自这里）。</summary>
    internal const string CardsTable = "cards";

    private static bool _localeCallbackSubscribed;

    public static void Initialize()
    {
        LocManager? locManager = LocManager.Instance;
        if (locManager == null)
        {
            // mod 初始器跑在 LocManager.Initialize() 之前 ⇒ 这里通常拿不到实例，
            // 延后到运行期（OnRunLaunched）重试，与遗物 / 休息区选项本地化同一处理。
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
            string entry = ModelDb.GetId<LocalWakuuHellGodPlaceholderCard>().Entry;
            locManager.GetTable(CardsTable).MergeWith(new Dictionary<string, string>
            {
                [$"{entry}.title"] = LocalModText.HellGodPlaceholderCardTitle,
                [$"{entry}.description"] = LocalModText.HellGodPlaceholderCardDescription,
            });
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"注入地狱战神卡牌本地化失败: {exception.Message}");
        }
    }
}
