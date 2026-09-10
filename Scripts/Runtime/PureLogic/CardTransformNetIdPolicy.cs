namespace LocalMultiControl.Scripts.Runtime;

/// <summary>手牌变换时对 <c>LocalContext.NetId</c> 的处理方式（r109）。</summary>
internal enum CardTransformNetIdAction
{
    /// <summary>不动 NetId（牌主不是本地玩家，或 NetId 已满足本次需求）。</summary>
    None,

    /// <summary>把 NetId 钉到牌主人：前台角色的变换，视觉需要在前台手牌里找到原卡节点。</summary>
    PinToOwner,

    /// <summary>
    /// 把 NetId **让开**牌主人：后台角色的变换，必须让原版按 <c>IsMine=false</c> 跳过视觉，
    /// 否则会到前台手牌找原卡节点并抛 <c>InvalidOperationException</c>。
    /// </summary>
    ShiftAwayFromOwner,
}

/// <summary>
/// 手牌变换（<c>CardCmd.Transform</c>）期间 <c>LocalContext.NetId</c> 的处理策略纯函数（r109）。
///
/// 背景（实机 marker r108，瓦库打出「数据链」/酒狐「不等价交换」等手牌变换牌）：
/// <code>
/// if (!LocalContext.IsMine(cardAdded)) continue;              // 只有"我的牌"才做视觉
/// if (cardAdded.Pile.Type == PileType.Hand) {
///     NCard nCard = NCard.FindOnTable(original, PileType.Hand);
///     if (nCard == null) throw new InvalidOperationException($"Couldn't get hand node for original card {original}!");
///     ...
/// }
/// </code>
/// 瓦库自动出牌期间 <c>RunWatchdogAsync</c> 会把 <c>LocalContext.NetId</c> 钉在瓦库身上，
/// 于是瓦库自己手牌的变换被判成"是我的牌" → 到**前台手牌**（显示的是真人手牌）找节点 → 找不到 → 抛异常
/// → 抛穿异步链 → 出牌中断、牌停在屏幕中间、效果没跑完、也没消耗。
///
/// r59 的修法只覆盖了"要不要**钉** NetId"，没有覆盖"NetId **已经**是牌主人"的情形，所以这里补上。
/// </summary>
internal static class CardTransformNetIdPolicy
{
    /// <summary>
    /// 决定本次手牌变换要不要动 NetId、怎么动。
    /// </summary>
    /// <param name="isOwnerLocal">牌主人是否是本地玩家（不是 → 原版自己会按 IsMine=false 跳过视觉）。</param>
    /// <param name="isOwnerForeground">牌主人是否就是当前前台（受控）角色。</param>
    /// <param name="currentNetIdIsOwner">当前 <c>LocalContext.NetId</c> 是否已等于牌主人。</param>
    public static CardTransformNetIdAction Decide(
        bool isOwnerLocal,
        bool isOwnerForeground,
        bool currentNetIdIsOwner)
    {
        if (!isOwnerLocal)
        {
            return CardTransformNetIdAction.None;
        }

        if (isOwnerForeground)
        {
            // 前台角色的变换：视觉分支要在前台手牌里找原卡节点，必须保证 IsMine=true。
            return currentNetIdIsOwner
                ? CardTransformNetIdAction.None
                : CardTransformNetIdAction.PinToOwner;
        }

        // 后台角色的变换：前台手牌里根本没有它的牌，视觉分支只会抛异常 → 必须让 IsMine=false。
        // 关键：自动出牌期间 NetId 可能**已经**等于牌主人（被看门狗钉住），
        // 此时"什么都不做"并不安全，必须显式让开。
        return currentNetIdIsOwner
            ? CardTransformNetIdAction.ShiftAwayFromOwner
            : CardTransformNetIdAction.None;
    }
}
