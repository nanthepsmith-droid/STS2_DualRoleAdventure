namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「托管席位（瓦库）的牌正在被 <c>CardCmd.Transform</c> 变换」期间，对
/// <c>LocalContext.NetId</c> 的兜底拉回判定（r201，BUG-29 第三轮）。
///
/// **为什么需要它**（2026-10-03 两轮实机，marker r199 / r200）：
/// 原版 <c>CardCmd.Transform</c> 的**视觉阶段**会做
/// <code>
/// if (!LocalContext.IsMine(cardAdded2)) continue;         // 只有"我的牌"才做视觉
/// NCard nCard = NCard.FindOnTable(original2, PileType.Hand);
/// if (nCard == null) throw new InvalidOperationException("Couldn't get hand node for original card …");
/// </code>
/// 托管席位（后台瓦库）的手牌 UI 根本不渲染（<c>NCombatRoom.Ui.Hand</c> 只显示前台席位）
/// ⇒ 只要此刻上下文等于这个席位，视觉阶段就**必然**抛 ⇒ 出牌以异常结束 ⇒ 牌停在屏幕中央、只换了一部分。
///
/// 而这段异步窗口里 **上下文会被别人钉回牌主人**：
///  - r199 只在 Prefix 让开一次 ⇒ 挡不住（实机日志：`netId=…326 -> …326` 已让开却仍抛）；
///  - r200 想在 <c>LocalContext.IsMine</c> 上挂补丁兜底 ⇒ 也没触发（补丁确实挂上了：
///    `PATCH_RESULT … optional=16/16`，但兜底日志 0 条）—— `IsMine` 是极小方法，JIT 很可能把它
///    **内联**进了 <c>Transform</c> 的状态机，内联副本不会走 Harmony 的入口 ⇒ 补丁形同不存在；
///  - 全机 dll 扫描显示：**只有 RitsuLib 的运行时程序集引用了 `LocalContext.set_NetId`
///    并且同时带 <c>CardCmdTransformPatch</c>** ⇒ 就是它在异步窗口里把上下文钉回牌主人
///    （它要让 mod 卡牌的变换视觉照常播放，这对**同机单席位**是对的，对本地多控的**后台托管席位**就是灾难）。
///
/// ⇒ 正确姿势：**不要试图拦住那个写入、也不要依赖拦截极小方法**，而是
/// 「在状态机每一步之前把上下文拉回安全值」——内联副本读的也是同一个静态属性，绕不开
/// （见 <c>CardTransformAutomatedSeatContextGuardPatch</c> 挂在 <c>CardCmd+&lt;Transform&gt;d__*.MoveNext</c> 上）。
///
/// 本类只做判定，便于单测。
/// </summary>
internal static class AutomatedSeatTransformContextGuard
{
    /// <summary>
    /// 是否需要把上下文拉回安全值；需要时给出该写什么。
    /// </summary>
    /// <param name="currentNetId">此刻的 <c>LocalContext.NetId</c>。</param>
    /// <param name="currentIsAutomatedSeat">此刻的上下文是不是我们托管的席位（瓦库驱动）。</param>
    /// <param name="controlledSeatId">当前受控（前台）席位。</param>
    /// <param name="controlledIsLocalSeat">受控席位是不是本地席位。</param>
    /// <param name="safeNetId">需要写入的值；<c>null</c> 表示"让到空上下文"（<c>IsMine</c> 恒 false）。</param>
    /// <returns>true = 需要拉回；false = 与本次变换无关，不干预。</returns>
    public static bool TryResolveSafeNetId(
        ulong? currentNetId,
        bool currentIsAutomatedSeat,
        ulong? controlledSeatId,
        bool controlledIsLocalSeat,
        out ulong? safeNetId)
    {
        safeNetId = null;

        // 上下文不是托管席位 ⇒ 和"托管席位的手牌被变换"无关（真人自己的变换照旧，视觉不变）。
        if (!currentIsAutomatedSeat || !currentNetId.HasValue)
        {
            return false;
        }

        // 优先让到"另一个本地席位"（前台玩家）：与原保底逻辑同口径。
        if (controlledSeatId.HasValue
            && controlledSeatId.Value != currentNetId.Value
            && controlledIsLocalSeat)
        {
            safeNetId = controlledSeatId.Value;
            return true;
        }

        // 受控位就是它自己 / 拿不到受控位 ⇒ 让到 null：`IsMe` 对任何玩家都返回 false，
        // 托管席位的牌一律跳过原版视觉（数据层在视觉之前就已生效）。
        safeNetId = null;
        return true;
    }
}
