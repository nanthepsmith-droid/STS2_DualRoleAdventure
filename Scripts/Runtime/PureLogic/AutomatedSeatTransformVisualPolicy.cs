namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// `CardCmd.Transform` 视觉阶段「这张牌算不算我的」的托管席位判定（r203，BUG-29 第五轮）。
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
/// 本类只做判定，便于单测。
/// </summary>
internal static class AutomatedSeatTransformVisualPolicy
{
    /// <summary>
    /// 视觉阶段是否把这张牌当作"我的牌"。
    /// </summary>
    /// <param name="baseResult">原版 <c>LocalContext.IsMine(card)</c> 的结果。</param>
    /// <param name="ownerIsAutomatedSeat">这张牌的归属者是不是我们托管的席位（瓦库驱动）。</param>
    /// <returns>托管席位的牌**一律不算**（跳过原版视觉）；其余按原版结果。</returns>
    public static bool ShouldTreatAsMine(bool baseResult, bool ownerIsAutomatedSeat)
    {
        // 注意：与原版口径的差异只在"托管席位"这一格 ——
        // 这类牌跳过视觉只丢装饰（数据层在视觉之前就已生效，手牌 UI 由既有自愈链路重建）。
        return baseResult && !ownerIsAutomatedSeat;
    }
}
