#nullable enable

using System;
using System.Linq;
using Godot;
using LocalMultiControl.Scripts.Models.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// ④「地狱战神」卡池注册的**运行期自检**（2026-10-06，前置功课用）。
///
/// 为什么必须"延迟"再查：登记发生在 mod 初始器里（**游戏初始化前**，见
/// <c>Entry.RegisterWakuuHellGodCardToPool</c>），那一刻 <c>ModelDb</c> 还没建好、
/// 卡池也读不得（读会触发 <c>GenerateAllCards</c> ⇒ <c>ModelNotFound</c>）。
/// 所以校验挂到 <c>LocalMultiControlRuntime.OnRunLaunched</c>（每次进局，此时 ModelDb 早已就绪）。
///
/// 只读、幂等、**永不抛**（失败只 WARN）—— 它是可观测锚点，不是门禁。
/// 之所以要这条日志：卡没挂进池 / 本地化缺键时**都不会自己报错**
/// （缺键只会在真正渲染那张卡时才抛 <c>LocException</c>），只有这里能提前点名。
/// </summary>
internal static class WakuuHellGodCardProbe
{
    private static bool _verified;

    /// <summary>校验自定卡是否真的进了事件卡池，并顺带验证标题 / 描述本地化与立绘可用（成功后不再重复打印）。</summary>
    internal static void VerifyPoolMembership()
    {
        if (_verified)
        {
            return;
        }

        try
        {
            ModelId id = ModelDb.GetId<LocalWakuuHellGodPlaceholderCard>();
            CardModel card = ModelDb.Card<LocalWakuuHellGodPlaceholderCard>();

            bool inPool = ModelDb.CardPool<EventCardPool>().AllCardIds.Contains(id);
            bool inAllCards = ModelDb.AllCards.Any(candidate => candidate.Id == id);
            bool titleLocOk = LocString.Exists(LocalWakuuHellGodLocalization.CardsTable, $"{id.Entry}.title");
            bool descLocOk = LocString.Exists(LocalWakuuHellGodLocalization.CardsTable, $"{id.Entry}.description");
            bool portraitOk = card.Portrait != null;

            _verified = true;

            string detail =
                $"entry={id.Entry}, 事件池内={Yes(inPool)}, ModelDb.AllCards={Yes(inAllCards)}, "
                + $"标题本地化={Yes(titleLocOk)}, 描述本地化={Yes(descLocOk)}, 立绘可用={Yes(portraitOk)}";

            if (inPool && inAllCards && titleLocOk && descLocOk && portraitOk)
            {
                LocalMultiControlLogger.Info($"[地狱战神验证] 自定卡链路全通: {detail}");
            }
            else
            {
                LocalMultiControlLogger.Warn(
                    $"[地狱战神验证] 自定卡链路**有断点**: {detail}"
                    + "（池内=否 ⇒ 检查 AddModelToPool 是否早于游戏初始化；本地化=否 ⇒ 检查 LocManager 注入时机；"
                    + "立绘=否 ⇒ 检查借用原版立绘的路径）");
            }
        }
        catch (Exception exception)
        {
            // 保持 _verified=false：下次进局再试（早期异常态可能只是时序问题）。
            LocalMultiControlLogger.Warn($"[地狱战神验证] 卡池自检失败（下次进局重试）: {exception.Message}");
        }
    }

    private static string Yes(bool value) => value ? "是" : "否";
}
