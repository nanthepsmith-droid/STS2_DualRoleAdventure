using System;
using System.Collections.Generic;
using LocalMultiControl.Scripts.Models.Relics;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalWakuuRelicLocalization
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
            string starterEntry = ModelDb.GetId<LocalWakuuStarterRelic>().Entry;
            Dictionary<string, string> starterEntries = new()
            {
                [$"{starterEntry}.title"] = LocalModText.Select("永久低语耳环", "Permanent Whispering Earring"),
                [$"{starterEntry}.description"] = LocalModText.Select(
                    "永久让瓦库接管你的回合。",
                    "Vakuu permanently takes over your turns."),
                [$"{starterEntry}.eventDescription"] = LocalModText.Select(
                    "永久让瓦库接管你的回合。",
                    "Vakuu permanently takes over your turns."),
                [$"{starterEntry}.flavor"] = LocalModText.Select(
                    "它不再只接管第一回合，而是接管每一回合。",
                    "No longer just the first turn. Vakuu takes every turn.")
            };

            locManager.GetTable("relics").MergeWith(starterEntries);

            string formEntry = ModelDb.GetId<LocalWakuuFormRelic>().Entry;
            Dictionary<string, string> formEntries = new()
            {
                [$"{formEntry}.title"] = LocalModText.Select("瓦库形态", "Vakuu Form"),
                [$"{formEntry}.description"] = LocalModText.Select(
                    "让瓦库玩算你赢了。",
                    "Letting Vakuu play counts as you winning."),
                [$"{formEntry}.eventDescription"] = LocalModText.Select(
                    "让瓦库玩算你赢了。",
                    "Letting Vakuu play counts as you winning."),
                [$"{formEntry}.flavor"] = LocalModText.Select(
                    "赢了，但只赢了一半——毕竟出牌的不是你。",
                    "You won, but only half of it — Vakuu did all the playing.")
            };

            locManager.GetTable("relics").MergeWith(formEntries);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"注入瓦库专用遗物本地化失败: {exception.Message}");
        }
    }
}
