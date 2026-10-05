namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「这个牌节点能不能挂进战斗的**共享手牌 UI**」的判定结果（纯逻辑，可单测）。
///
/// <para>背景：战斗 UI 的手牌容器是**共享的**（<c>NCombatRoom.Instance.Ui.Hand</c> 显示谁的手牌由前台决定），
/// 而 <c>NPlayerHand.Add</c> 是**唯一**的加入点。原版决定要不要加用的是
/// <c>LocalContext.IsMe(card.Owner)</c> —— 本地多控下这个上下文随时可能落在别的席位（异步窗口 /
/// 并发出牌刻意不钉上下文），于是别人的牌节点会挂进当前显示的真人手牌里（观感 = 幽灵牌）。</para>
/// </summary>
internal enum HandNodeAdmission
{
    /// <summary>照常加入：与多控无关 / 判据不足 / 确实是这份手牌主人的牌。</summary>
    Allow,

    /// <summary>选牌进行中，但不是「本次选牌玩家」的牌 ⇒ 拦截（原有守卫行为）。</summary>
    BlockNotSelectionOwner,

    /// <summary>非选牌期间，但不是我方**受控席位**的牌（我们自己的其它席位）⇒ 拦截（幽灵牌兜底）。</summary>
    BlockNotControlledSeat,
}

/// <summary>
/// 共享手牌 UI 的加入判定（纯逻辑）。
///
/// <para>判错方向代价不对称：拦多了 = 该显示的手牌不显示（玩家看不到自己的牌）；
/// 拦少了 = 别人的牌混进当前手牌（幽灵牌，且能**被选中弃掉**、节点还不消失）。所以两条规则都收窄到
/// 「明确知道该拦」才拦：受控位未知 / 卡牌主人未知 / 不是我们的本地席位 ⇒ 一律放行，不干预。</para>
///
/// <para>与 <c>SeatRegistry</c> 的分工：<b>身份口径</b>（谁是受控席位 / 谁是不是我们的席位）由调用方问
/// <c>SeatRegistry</c> 后以布尔传入；这里只做组合判定 ⇒ 不依赖 Godot / 游戏运行时类型。</para>
/// </summary>
internal static class HandNodeAdmissionPolicy
{
    /// <summary>
    /// 判定一枚牌节点能不能挂进共享手牌 UI。
    /// </summary>
    /// <param name="localCoop">是不是本 mod 的本地多控回环会话（单机 / 真联机恒放行）。</param>
    /// <param name="cardOwnerKnown">卡牌主人是否已知（未知 ⇒ 放行）。</param>
    /// <param name="selectionActive">此刻是否有选牌流程在等玩家作答（<c>NPlayerHand.SelectCards</c>）。</param>
    /// <param name="cardOwnerIsSelectionOwner">卡牌主人是不是「本次选牌玩家」（两两比较，非席位身份判定）。</param>
    /// <param name="controlledSeatKnown">受控席位是否已知（未知 ⇒ 放行）。</param>
    /// <param name="cardOwnerIsControlledSeat">卡牌主人是不是当前受控席位（= 这份手牌 UI 的主人）。</param>
    /// <param name="cardOwnerIsLocalSeat">卡牌主人是不是我们的本地席位（第三方 / 联机席位不归这里管）。</param>
    internal static HandNodeAdmission Decide(
        bool localCoop,
        bool cardOwnerKnown,
        bool selectionActive,
        bool cardOwnerIsSelectionOwner,
        bool controlledSeatKnown,
        bool cardOwnerIsControlledSeat,
        bool cardOwnerIsLocalSeat)
    {
        if (!localCoop || !cardOwnerKnown)
        {
            return HandNodeAdmission.Allow;
        }

        // ① 选牌进行中：手牌 UI 就是选牌界面，只有本次选牌玩家的牌能进去（原有守卫，口径不动）。
        if (selectionActive)
        {
            return cardOwnerIsSelectionOwner
                ? HandNodeAdmission.Allow
                : HandNodeAdmission.BlockNotSelectionOwner;
        }

        // ② 非选牌期间：共享手牌 UI 只显示受控席位的手牌。
        if (!controlledSeatKnown)
        {
            return HandNodeAdmission.Allow; // 受控位未知 ⇒ 不干预（与 CardPileHandVisualOwnerGuardPatch 同口径）
        }

        if (cardOwnerIsControlledSeat || !cardOwnerIsLocalSeat)
        {
            return HandNodeAdmission.Allow; // 就是当前受控席位自己的牌 / 不归我们管的席位
        }

        return HandNodeAdmission.BlockNotControlledSeat;
    }
}
