using System;
using System.Collections.Generic;
using System.Linq;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 个人记录器的「写时幂等」去重纯逻辑（r120）。
///
/// **为什么换掉"按存档点回滚"的思路（r118/r119 的教训）**：
/// 原思路是"读档时按上一次存档点丢弃被回滚的那段记录"，判据取 `current_run_mp.save` 的
/// **最后修改时间**。实测（r119 实机日志）**该判据永远删不到东西** —— 两次进局的存档点只差 13 秒，
/// 且恰等于**读档那一刻**：说明**读档/进局动作本身会重写存档文件**，于是 mtime 永远 ≥
/// 最近一次抉择的时间，`ts > mtime` 恒为假（`total=0`）。
/// 根因是"想从外部猜出游戏回滚到了哪个状态"这件事本身就不可靠。
///
/// **新思路：不猜，改成幂等** —— 每次写入前，先删掉"同一抉择标识"的旧行，只保留最后一次。
/// 这样无论 SL 多少次，同一抉择在统计里都只算一次：
/// <list type="bullet">
/// <item>事件页：标识 = <c>(runKey, eventId)</c>（同一事件一局只保留最后一次抉择）；</item>
/// <item>卡牌批次：标识 = <c>(runKey, batchKey)</c>，batchKey = 该批 offer 的卡 id 去重排序拼接
///   （SL 后重新点同一批牌 → 覆盖；重新 roll 出不同的牌 → 视为新批次，不误合并）；</item>
/// <item>商店购买：标识 = <c>(runKey, act, kind, item)</c>；</item>
/// <item>删牌：标识 = <c>(runKey, card)</c>。</item>
/// </list>
///
/// 已知代价（刻意接受）：同一局内**合法地**重复遇到同一事件 / 同一幕出现完全相同的卡牌批次，
/// 会被合并成一次。事件在一局内基本不重复；卡牌批次按"卡集合"区分，误合并概率很低，
/// 且合并结果（"这次遇到时最终选了啥"）比"重复计数"更符合直觉。
///
/// 旧数据没有 batch 字段（空串）→ 不参与卡牌批次去重（保留，无法判断）。
/// 本文件不依赖任何游戏类型，可直接单测。
/// </summary>
internal static class WakuuPersonalDedupe
{
    /// <summary>写入事件页之前调用：删掉该局中同一 <paramref name="eventId"/> 的旧行，返回删除数。</summary>
    public static int RemoveEventPage(PersonalStore store, string runKey, string eventId)
    {
        if (store == null || string.IsNullOrEmpty(runKey) || string.IsNullOrEmpty(eventId))
        {
            return 0;
        }

        return store.eventChoices.RemoveAll(r => r != null
            && Same(r.runKey, runKey)
            && Same(r.eventId, eventId));
    }

    /// <summary>写入卡牌批次之前调用：删掉该局中同一批次（<paramref name="batchKey"/>）的旧行。</summary>
    public static int RemoveCardBatch(PersonalStore store, string runKey, string batchKey)
    {
        if (store == null || string.IsNullOrEmpty(runKey) || string.IsNullOrEmpty(batchKey))
        {
            return 0;
        }

        return store.cardOffers.RemoveAll(r => r != null
            && Same(r.runKey, runKey)
            && string.Equals(r.batch, batchKey, StringComparison.Ordinal));
    }

    /// <summary>写入商店购买之前调用：删掉该局同幕同类别同一件的旧行。</summary>
    public static int RemoveShopPurchase(
        PersonalStore store, string runKey, int act, string kind, string item)
    {
        if (store == null || string.IsNullOrEmpty(runKey)
            || string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(item))
        {
            return 0;
        }

        return store.shopPurchases.RemoveAll(r => r != null
            && Same(r.runKey, runKey)
            && r.act == act
            && Same(r.kind, kind)
            && Same(r.item, item));
    }

    /// <summary>
    /// 写入删牌之前调用：删掉该局**同一幕**同一张牌的旧行。
    ///
    /// **为什么要带 <paramref name="act"/>**：删牌是「一次删牌机会 = 一个抉择」，而同一张牌
    /// （如两张 `STRIKE`）在同一局的不同幕被合法地各删一次是很常见的 —— 键里少了 `act` 就会把
    /// 跨幕的两次合法删牌合并成一行，删牌偏好被少算。带上 `act` 后既保留了 SL 覆盖语义
    /// （SL 不会跨幕，同幕重删同一张牌仍会被覆盖），又不再误合并。
    /// 这与商店购买 `(runKey, act, kind, item)` 的口径一致。
    /// </summary>
    public static int RemoveCardRemoval(PersonalStore store, string runKey, int act, string card)
    {
        if (store == null || string.IsNullOrEmpty(runKey) || string.IsNullOrEmpty(card))
        {
            return 0;
        }

        return store.cardRemovals.RemoveAll(r => r != null
            && Same(r.runKey, runKey)
            && r.act == act
            && Same(r.card, card));
    }

    /// <summary>
    /// 批次标识：把该批 offer 的卡 id 归一化（大写）后**去重、排序**再拼接。
    /// 顺序无关、大小写无关，因此"同一批牌"在 SL 前后会得到同一个键。
    /// </summary>
    public static string BuildBatchKey(IEnumerable<string>? cardIds)
    {
        if (cardIds == null)
        {
            return string.Empty;
        }

        List<string> normalized = cardIds
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!.ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return normalized.Count == 0 ? string.Empty : string.Join("|", normalized);
    }

    private static bool Same(string? left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
