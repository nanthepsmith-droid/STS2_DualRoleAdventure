using System;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 个人偏好记录器的两类纯逻辑判定（r129/r130）。
///
/// ① <see cref="IsDeckRemovalPrompt"/>：牌组通用选牌入口（<c>CardSelectCmd.FromDeckGeneric</c>）里
///    哪一次才算「删牌」 —— 同一条入口还被 DollysMirror（复制）/ WoodCarvings（变化）复用。
///
/// ② <see cref="IsAutoScopeOwnedBy"/>：瓦库自动化作用域（事件自动选择 / 奖励自动领取 / 商店自动采购）
///    是不是**就是这一位玩家**的自动化。**必须按归属者比较，不能只判"作用域是否非空"**：
///    这些作用域是 AsyncLocal、会沿异步链残留，2026-09-13 实机（marker r129）里真人自己的商店删牌
///    就被"别的角色正在自动采购"的残留作用域整条吞掉（连删 2 张同名牌，日志两条都是
///    `跳过: 瓦库商店自动采购作用域内`）—— 商店购买记录长期只有 2 行也是同一个原因。
///
/// ⚠ 另一个同源坑（r130，BUG-12 真正根因）：`LocalWakuuMerchantAuto.PurchaseOwnerId` 曾是
///    **AsyncLocal 字段本身**，被调用方写成 `PurchaseOwnerId == null`（恒 false）⇒ 删牌记录
///    全历史 0 行。字段已收私有、改经值属性暴露；这里也只接受 `ulong?` 值参数。
/// </summary>
public static class WakuuRecordScopePolicy
{
    /// <summary>`CardSelectorPrefs.RemoveSelectionPrompt` 的本地化键。</summary>
    public const string RemovalPromptKey = "TO_REMOVE";

    /// <summary>
    /// 是否是「从牌组删除一张牌」的选牌。取不到提示键（null / 空）→ false：宁可少记，不要错记。
    /// 只认键、不比较表名 —— 第三方 mod 可能自建表复用同一个键。
    /// <c>TO_EXHAUST</c> / <c>TO_DISCARD</c> 是**手牌**语义，不算删牌（也不走牌组通用入口，双保险）。
    /// </summary>
    public static bool IsDeckRemovalPrompt(string? promptKey)
    {
        return string.Equals(promptKey, RemovalPromptKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// 该自动化作用域是不是"这一位玩家自己的"（是 → 本次操作由瓦库自动化产生，不是真人决策，不该记）。
    /// 作用域为空（null）→ false：没有对应自动化在跑，正常记录。
    /// </summary>
    public static bool IsAutoScopeOwnedBy(ulong? scopeOwnerId, ulong playerId)
    {
        return scopeOwnerId.HasValue && scopeOwnerId.Value == playerId;
    }
}
