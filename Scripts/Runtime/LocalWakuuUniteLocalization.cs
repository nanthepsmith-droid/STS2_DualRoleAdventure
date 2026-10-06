using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「我们联合」用到的**选牌提示**本地化注入（手法同 <see cref="LocalWakuuRestSiteLocalization"/>：
/// mod 不打包 loc 文件，改为运行期往游戏已有的 <c>card_selection</c> 表里 MergeWith）。
///
/// 为什么必须注入：原版选牌界面的提示文案是 <c>prefs.Prompt</c> 渲染出来的，而
/// <c>LocString.GetRawText</c> 在**表或键不存在时抛 <c>LocException</c>**（不是静默空白）——
/// 那会把整次选牌打断（同族教训：缺图标把按钮名字一起吃掉，见坑 V）。
/// 因此调用方还要用 <see cref="ResolvePrompt"/> 再兜一层：查不到就退回原版提示。
/// </summary>
internal static class LocalWakuuUniteLocalization
{
    /// <summary>游戏 <c>card_selection</c> 表（<c>CardSelectorPrefs</c> 的所有提示都取自这里）。</summary>
    internal const string CardSelectionTable = "card_selection";

    /// <summary>步骤①的提示键：从自己卡组里选 1 张复制给瓦库。</summary>
    internal const string GivePromptKey = "LMC_UNITE_GIVE";

    /// <summary>步骤②的提示键：从瓦库卡组里选 1 张复制给自己。</summary>
    internal const string TakePromptKey = "LMC_UNITE_TAKE";

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
    /// 取提示文案：我们的键尚未注入（或表不可用）时回退 <paramref name="fallback"/>（原版提示）。
    /// **绝不抛**：宁可显示一句原版提示，也不能让选牌界面开不出来。
    /// </summary>
    internal static LocString ResolvePrompt(string key, LocString fallback)
    {
        try
        {
            if (LocString.Exists(CardSelectionTable, key))
            {
                return new LocString(CardSelectionTable, key);
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
            Dictionary<string, string> entries = new()
            {
                [GivePromptKey] = LocalModText.UniteGivePrompt,
                [TakePromptKey] = LocalModText.UniteTakePrompt,
            };

            locManager.GetTable(CardSelectionTable).MergeWith(entries);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"注入瓦库联合选牌提示本地化失败: {exception.Message}");
        }
    }
}
