namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 奖励种类（策略层只认这些；运行层负责把具体的 <c>Reward</c> 实例映射过来）。
/// </summary>
internal enum WakuuRewardKind
{
    Other = 0,
    Card,
    Gold,
    Relic,
    Potion,

    /// <summary>
    /// 「特定卡牌」奖励（`SpecialCardReward`）。原版用它做两件事（源码注释点名）：
    /// ① **把被跳虫（ThievingHopper）偷走的牌还给你**（`SwipePower.BeforeDeath` →
    /// `new SpecialCardReward(StolenCard.DeckVersion, 失主)` → `AddExtraReward`）；
    /// ② 事件给指定任务牌（如 `TheLanternKey`）。
    /// 领取动作 = `CardPileCmd.Add(card, PileType.Deck)`（把这张卡加进牌组）；**不领 = 牌就没了**
    /// （`OnSkipped` 只把这次选择记为 `wasPicked: false`）。
    /// </summary>
    SpecialCard,
}

/// <summary>
/// 「这条奖励要不要替瓦库自动领」的纯判定（r150 抽出，便于单测）。
///
/// 回归背景（2026-09-26 实机）：玩家报「瓦库似乎不会取回自己被偷走的牌」。
/// 查证 = `SpecialCardReward` 原先落在 `default: return false`（注释写着"删牌/特殊奖励等保持人工"），
/// 于是**取回被偷走的牌这一条永远不会自动领**，只能靠真人在合并奖励屏上手动点那一条；
/// 真人没点时瓦库就永远拿不回那张牌。本策略把它归到与卡牌奖励同一个开关 `autoClaimCards`。
///
/// ⚠ 刻意**不**覆盖 `CardRemovalReward`（删牌奖励）：它的语义是"选一张牌删掉"，
/// 需要走瓦库的 Remove 选牌规则，属另一条链，保持人工（与战后奖励链的口径一致）。
/// </summary>
internal static class WakuuRewardClaimPolicy
{
    public static bool ShouldAutoClaim(
        WakuuRewardKind kind,
        bool isVakuuForm,
        bool autoClaimCards,
        bool autoClaimGoldRelics,
        bool autoClaimPotions)
    {
        if (!isVakuuForm)
        {
            return false; // 非瓦库角色的奖励一律保持人工领取
        }

        return kind switch
        {
            WakuuRewardKind.Card => autoClaimCards,
            // r150：取回被偷走的牌（以及事件给指定任务牌）跟卡牌奖励同一开关。
            WakuuRewardKind.SpecialCard => autoClaimCards,
            WakuuRewardKind.Gold => autoClaimGoldRelics,
            WakuuRewardKind.Relic => autoClaimGoldRelics,
            WakuuRewardKind.Potion => autoClaimPotions,
            // 删牌奖励等保持人工。
            _ => false,
        };
    }
}
