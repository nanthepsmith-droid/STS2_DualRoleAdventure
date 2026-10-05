using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库四功能用到**休息区自定义选项**的本地化注入（与 <see cref="LocalWakuuRelicLocalization"/>
/// 同款手法：mod 不打包 loc 文件，改为运行期往游戏已有的 <c>rest_site_ui</c> 表里 MergeWith）。
///
/// 为什么必须注入：<c>RestSiteOption.Title</c> / <c>Description</c> 是硬拼的
/// <c>OPTION_&lt;OptionId&gt;.name/.description</c>（游戏源码 <c>RestSiteOption.cs:27-29</c>），
/// 表里没有这个 key 时按钮文字会取不到值。
///
/// 键名规则（与 <see cref="PurifyWakuuRestSiteOption.PurifyOptionId"/> 对齐）：
/// <c>OPTION_LMC_PURIFY.name</c> / <c>OPTION_LMC_PURIFY.description</c>；
/// description 里的 <c>{Amount}</c> 由选项自己 <c>LocString.Add("Amount", …)</c> 填充。
/// </summary>
internal static class LocalWakuuRestSiteLocalization
{
    private static bool _localeCallbackSubscribed;

    public static void Initialize()
    {
        LocManager? locManager = LocManager.Instance;
        if (locManager == null)
        {
            // 初始化早期 LocManager 可能尚未就绪，延后到运行期重试。
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
            string optionKeyPrefix = "OPTION_" + PurifyWakuuRestSiteOption.PurifyOptionId;
            Dictionary<string, string> entries = new()
            {
                [$"{optionKeyPrefix}.name"] = LocalModText.PurifyOptionName,
                [$"{optionKeyPrefix}.description"] = LocalModText.PurifyOptionDescription,
            };

            locManager.GetTable("rest_site_ui").MergeWith(entries);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"注入瓦库休息区选项本地化失败: {exception.Message}");
        }
    }
}
