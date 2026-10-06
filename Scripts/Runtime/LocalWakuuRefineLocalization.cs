#nullable enable

using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「炼化」（瓦库四功能之三）的本地化注入。mod 不打包 loc 文件，一律运行期往游戏已有表里 <c>MergeWith</c>：
/// <list type="bullet">
/// <item><c>rest_site_ui</c>：<c>OPTION_LMC_REFINE.name/.description</c> —— 休息区按钮的标题与描述
///   （<c>RestSiteOption.Title/Description</c> 是硬拼的 key，表里没有就取不到值）；</item>
/// <item><c>card_selection</c>：<c>LMC_REFINE_CARDS</c> —— 收编选牌的提示文案。</item>
/// </list>
///
/// 为什么 <c>card_selection</c> 的提示必须兜底：原版 <c>LocString.GetRawText</c> 在**表或键不存在时抛
/// <c>LocException</c>**（不是静默空白），会把整次选牌打断（同族教训见 <see cref="LocalWakuuUniteLocalization"/>）。
/// 所以调用方要用 <see cref="ResolveCardPrompt"/>：查不到就退回原版提示。
/// </summary>
internal static class LocalWakuuRefineLocalization
{
    /// <summary>游戏 <c>card_selection</c> 表（<c>CardSelectorPrefs</c> 的提示都取自这里）。</summary>
    internal const string CardSelectionTable = "card_selection";

    /// <summary>游戏 <c>rest_site_ui</c> 表（休息区选项标题 / 描述）。</summary>
    internal const string RestSiteTable = "rest_site_ui";

    /// <summary>炼化收编选牌的提示键。</summary>
    internal const string CardsPromptKey = "LMC_REFINE_CARDS";

    private static bool _localeCallbackSubscribed;

    public static void Initialize()
    {
        LocManager? locManager = LocManager.Instance;
        if (locManager == null)
        {
            // 初始化早期 LocManager 可能尚未就绪，延后到运行期（进局）重试。
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

    /// <summary>
    /// 取收编选牌提示：我们的键尚未注入（或表不可用）时回退 <paramref name="fallback"/>（原版提示）。
    /// **绝不抛**：宁可显示一句原版提示，也不能让选牌界面开不出来。
    /// </summary>
    internal static LocString ResolveCardPrompt(LocString fallback)
    {
        try
        {
            if (LocString.Exists(CardSelectionTable, CardsPromptKey))
            {
                return new LocString(CardSelectionTable, CardsPromptKey);
            }
        }
        catch (Exception)
        {
            // 表本身不可用（早期 / 异常态）：走回退。
        }

        return fallback;
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
            string optionKeyPrefix = "OPTION_" + RefineWakuuRestSiteOption.RefineOptionId;
            locManager.GetTable(RestSiteTable).MergeWith(new Dictionary<string, string>
            {
                [$"{optionKeyPrefix}.name"] = LocalModText.RefineOptionName,
                [$"{optionKeyPrefix}.description"] = LocalModText.RefineOptionDescription,
            });

            locManager.GetTable(CardSelectionTable).MergeWith(new Dictionary<string, string>
            {
                [CardsPromptKey] = LocalModText.RefineCardsPrompt,
            });
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"注入瓦库炼化本地化失败: {exception.Message}");
        }
    }
}
