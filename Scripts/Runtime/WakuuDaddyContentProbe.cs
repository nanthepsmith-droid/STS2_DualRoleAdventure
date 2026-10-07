#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Models.Cards;
using LocalMultiControl.Scripts.Models.Relics;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// ④「瓦库的爹」（遗物 + 三张占位牌）的**运行期自检** —— 一条日志同时报四件内容是否真的可用。
///
/// 为什么必须"延迟"再查：登记发生在 mod 初始器里（**游戏初始化前**，见
/// <c>Entry.RegisterWakuuDaddyContentToPool</c>），那一刻 <c>ModelDb</c> 还没建好、卡池也读不得
/// （读会触发 <c>GenerateAllCards</c> ⇒ <c>ModelNotFound</c>）。所以校验挂在
/// <c>ModelDb.Preload()</c> 的后置补丁 <see cref="Patch.WakuuDaddyContentPatch"/> 上 ——
/// 卡池首次访问并冻结那一刻，**主菜单之后不久、任何进局之前**（不必进局）⇒ 卡牌库/遗物库看得到就是它。
///
/// 只读、幂等、**永不抛**（失败只 WARN）—— 它是可观测锚点，不是门禁。
/// 之所以要这条日志：内容没挂进池 / 本地化缺键时**都不会自己报错**
/// （缺键只会在真正渲染那张卡时才抛 <c>LocException</c>），只有这里能提前点名。
/// 锚点：`[瓦库的爹验证]`。
/// </summary>
internal static class WakuuDaddyContentProbe
{
    private static bool _verified;

    /// <summary>校验三张占位牌与遗物是否真的进了池、本地化与立绘是否可用（成功后不再重复打印）。</summary>
    internal static void VerifyContent()
    {
        if (_verified)
        {
            return;
        }

        try
        {
            HashSet<ModelId> cardPoolIds = ModelDb.CardPool<EventCardPool>().AllCardIds.ToHashSet();
            HashSet<ModelId> relicPoolIds = ModelDb.RelicPool<EventRelicPool>().AllRelicIds;

            List<string> blocks = new()
            {
                DescribeCard<LocalWakuuDaddyShieldCard>("我挡", cardPoolIds),
                DescribeCard<LocalWakuuDaddyFocusCard>("你攻", cardPoolIds),
                DescribeCard<LocalWakuuDaddyMergeCard>("合体", cardPoolIds),
                DescribeRelic<LocalWakuuDaddyRelic>("遗物", relicPoolIds),
            };

            bool allOk = !blocks.Any(block => block.Contains("否", StringComparison.Ordinal));
            _verified = true;

            string detail = string.Join(" ", blocks);
            if (allOk)
            {
                LocalMultiControlLogger.Info($"[瓦库的爹验证] 内容链路全通: {detail}");
            }
            else
            {
                LocalMultiControlLogger.Warn(
                    $"[瓦库的爹验证] 内容链路**有断点**: {detail}"
                    + "（池=否 ⇒ 检查 AddModelToPool 是否早于游戏初始化；题/述=否 ⇒ 检查 LocManager 注入时机；"
                    + "图=否 ⇒ 检查借用原版立绘的路径）");
            }
        }
        catch (Exception exception)
        {
            // 保持 _verified=false：下次再试（早期异常态可能只是时序问题）。
            LocalMultiControlLogger.Warn($"[瓦库的爹验证] 内容自检失败（下次重试）: {exception.Message}");
        }
    }

    private static string DescribeCard<T>(string label, HashSet<ModelId> poolIds) where T : CardModel
    {
        ModelId id = ModelDb.GetId<T>();
        bool inPool = poolIds.Contains(id);
        bool titleOk = LocString.Exists(LocalWakuuDaddyLocalization.CardsTable, $"{id.Entry}.title");
        bool descOk = LocString.Exists(LocalWakuuDaddyLocalization.CardsTable, $"{id.Entry}.description");
        bool portraitOk = ModelDb.Card<T>().Portrait != null;
        return $"{label}[池={Yes(inPool)},题={Yes(titleOk)},述={Yes(descOk)},图={Yes(portraitOk)}]";
    }

    private static string DescribeRelic<T>(string label, HashSet<ModelId> poolIds) where T : RelicModel
    {
        ModelId id = ModelDb.GetId<T>();
        bool inPool = poolIds.Contains(id);
        bool titleOk = LocString.Exists(LocalWakuuDaddyLocalization.RelicsTable, $"{id.Entry}.title");
        bool descOk = LocString.Exists(LocalWakuuDaddyLocalization.RelicsTable, $"{id.Entry}.description");
        return $"{label}[池={Yes(inPool)},题={Yes(titleOk)},述={Yes(descOk)}]";
    }

    private static string Yes(bool value) => value ? "是" : "否";
}
