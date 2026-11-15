namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// `CardCmd.Transform` 视觉阶段「这张牌算不算我的」的托管席位判定（r203，BUG-29 第五轮；
/// r360 追加「前台托管席位」这一格）。
///
/// 原版视觉阶段第一句是 <c>if (!LocalContext.IsMine(cardAdded2)) continue;</c>：
/// 只有"本机玩家的牌"才去 <c>NCard.FindOnTable(original, PileType.Hand)</c> 找原牌节点播换牌特效，
/// **找不到就 throw**。托管席位（后台瓦库）的手牌 UI 根本不渲染（<c>NCombatRoom.Ui.Hand</c> 只显示前台席位）
/// ⇒ 只要该判定为 true，就必然抛 `Couldn't get hand node for original card …` ⇒ 出牌以异常结束、
/// 牌停在屏幕中央、只换了一部分（2026-10-01 / 10-03 三轮实机）。
///
/// **为什么必须在这一句上做判定**（四轮教训，详见 references 坑 S）：
/// - r199 只在 Prefix 让开一次 `LocalContext.NetId` ⇒ 挡不住异步窗口里的写入；
/// - r200 给 `LocalContext.IsMine` 挂补丁 ⇒ 极小方法被 JIT 内联，补丁形同不存在；
/// - r201/r202 挂 `CardCmd+&lt;Transform&gt;d__13.MoveNext`、每步之前把上下文拉回 ⇒ 实机证明**写回发生在同一个状态机步内**
///   （前缀一次都没命中：`已拉回安全值` 0 条），仍然抛。
/// ⇒ 唯一不受"谁在什么时候写全局状态""JIT 会不会内联"影响的姿势：**把这句判定本身改写掉**
/// （transpiler 换调用点，见 <c>AutomatedSeatTransformVisualGate</c>）。
///
/// **r360（2026-11-15 用户实机报「瓦库打牌手牌视觉与实际不同步」）**：
/// r199 那句「托管席位的手牌 UI 根本不存在」在**全瓦库**（连前台角色都是瓦库托管）时不成立 ——
/// 此时屏幕上显示的手牌就是该席位的，跳过视觉等于"数据换了牌、UI 还画着旧牌"⇒ 手牌内容永久不同步，
/// 直到切人重建；而且随后托管按新数据出牌时，原版 `NCardPlayQueue` 在自己手牌里找不到新牌节点
/// （日志 `A local card was played, but the card holder passed is null`）⇒ 播放队列凭空造一张视觉牌。
/// ⇒ 现在按 `ownerIsForeground` 分流：**后台**托管席位照旧跳过（它的手牌 UI 不存在），
/// **前台**托管席位按原版播视觉（额外要求"原牌节点确实还在手牌容器里"，见 <c>handNodesAvailable</c>）。
///
/// 本类只做判定，便于单测。
/// </summary>
internal static class AutomatedSeatTransformVisualPolicy
{
    /// <summary>
    /// 视觉阶段是否把这张牌当作"我的牌"。
    /// </summary>
    /// <param name="baseResult">原版 <c>LocalContext.IsMine(card)</c> 的结果。</param>
    /// <param name="ownerIsAutomatedSeat">这张牌的归属者是不是我们托管的席位（瓦库驱动）。</param>
    /// <param name="ownerIsForeground">这张牌的归属者是不是当前前台（受控）席位。</param>
    /// <param name="handNodesAvailable">
    /// 仅对「前台托管席位」有意义：视觉阶段那一刻，手牌容器里是否真的存在**原牌**节点
    /// （判定见 <c>AutomatedSeatTransformVisualGate.HasPendingOriginalHandNode</c>）。
    /// false ⇒ 照旧跳过视觉（宁可丢动画，也不要 `Couldn't get hand node` 抛穿调用链）。
    /// </param>
    /// <returns>
    /// 后台托管席位的牌**一律不算**（跳过原版视觉）；前台托管席位按原版结果（且原牌节点在场）；
    /// 非托管席位完全按原版结果。
    /// </returns>
    public static bool ShouldTreatAsMine(
        bool baseResult,
        bool ownerIsAutomatedSeat,
        bool ownerIsForeground = false,
        bool handNodesAvailable = true)
    {
        if (!baseResult)
        {
            return false;
        }

        if (!ownerIsAutomatedSeat)
        {
            // 非托管席位（真人自己的牌）：与原版完全一致。
            return true;
        }

        if (!ownerIsForeground)
        {
            // 后台托管席位：屏幕上的手牌不是它的 ⇒ 原版视觉必然去前台手牌找节点并抛。
            return false;
        }

        // 前台托管席位：手牌 UI 正是它的那份，节点还在就照原版播视觉（r360）。
        return handNodesAvailable;
    }
}
