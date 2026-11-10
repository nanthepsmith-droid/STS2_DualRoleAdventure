using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>商店采购步骤（顺序由 <see cref="WakuuShopOrderPolicy"/> 决定）。</summary>
internal enum WakuuShopStep
{
    Cards = 0,
    Relics = 1,
    Potions = 2,
    Removal = 3,
}

/// <summary>
/// 瓦库商店的**采购顺序**纯逻辑（2026-11-10 用户口径）。
///
/// 原实现固定「卡 → 遗物 → 药水 → 删牌」。用户提出两条例外：
/// <list type="number">
/// <item><b>卡组里有诅咒</b>且删牌服务买得起 ⇒ **先删诅咒**，删完再考虑买东西
///   （诅咒占手牌/牌组位置，留着比买一张普通牌更亏）；</item>
/// <item><b>卡组 ≥ 40 张且店里没有"很好的牌"</b> ⇒ **先买遗物，再删牌**，最后才考虑买卡/药水
///   （卡组已经很厚，再塞普通牌只会稀释；遗物是永久收益，删牌是"减薄"）。</item>
/// </list>
/// 两条都不满足时保持原顺序（卡 → 遗物 → 药水 → 删牌）。
///
/// "很好的牌" = 社区统计胜率 ≥ <see cref="GoodCardWinRate"/>（没有数据的卡不算"很好"）。
/// 删牌服务不可用/买不起时，删牌仍排在最后（运行层会自己跳过）。
/// </summary>
internal static class WakuuShopOrderPolicy
{
    /// <summary>「卡组已经太厚」阈值（≥ 40 张时优先遗物 + 删牌）。</summary>
    public const int BigDeckThreshold = 40;

    /// <summary>「很好的牌」的社区胜率门槛（买卡默认门槛是 0.2；这里更严，用于判断"店里有没有值得买的强卡"）。</summary>
    public const double GoodCardWinRate = 0.35;

    /// <summary>
    /// 决定本轮采购顺序。
    /// </summary>
    /// <param name="deckSize">牌组总张数（含诅咒/状态）。</param>
    /// <param name="curseCount">牌组里的诅咒张数。</param>
    /// <param name="hasGoodCard">店里是否有胜率 ≥ <see cref="GoodCardWinRate"/> 的卡。</param>
    /// <param name="removalAffordable">删牌服务是否可用且买得起（付完仍保留金币保底）。</param>
    public static IReadOnlyList<WakuuShopStep> Decide(
        int deckSize,
        int curseCount,
        bool hasGoodCard,
        bool removalAffordable)
    {
        // ① 有诅咒 ⇒ 先删诅咒（最直接的"净化"），再按常态买
        if (curseCount > 0 && removalAffordable)
        {
            return new[]
            {
                WakuuShopStep.Removal,
                WakuuShopStep.Cards,
                WakuuShopStep.Relics,
                WakuuShopStep.Potions,
            };
        }

        // ② 大卡组 + 店里没好牌 ⇒ 先遗物（永久收益），再删牌（减薄），最后才买卡/药水
        if (deckSize >= BigDeckThreshold && !hasGoodCard && removalAffordable)
        {
            return new[]
            {
                WakuuShopStep.Relics,
                WakuuShopStep.Removal,
                WakuuShopStep.Cards,
                WakuuShopStep.Potions,
            };
        }

        // ③ 常态
        return new[]
        {
            WakuuShopStep.Cards,
            WakuuShopStep.Relics,
            WakuuShopStep.Potions,
            WakuuShopStep.Removal,
        };
    }
}
