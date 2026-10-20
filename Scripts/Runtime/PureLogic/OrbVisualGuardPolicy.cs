namespace LocalMultiControl.Scripts.Runtime.PureLogic;

/// <summary>
/// 「球的视觉动画要不要跳过」的纯判定（r283；事故原形见
/// <see cref="Patch.NOrbManagerSlotlessSeatPatch"/> 的类注释）。
///
/// 口径只有一条：**没有视觉球槽节点（0 个）就跳过动画**。
/// 数据层（`OrbQueue` 的充能 / 激发 / 容量）与动画完全解耦 —— 跳过动画不影响任何数值。
///
/// 为什么"读不到"（负值）要**放行**而不是跳过：读失败只可能是反射面变了，
/// 这时宁可让原版照旧跑（有球槽的玩家本来就该有动画），也不要把正常玩家的球动画吞掉。
/// </summary>
internal static class OrbVisualGuardPolicy
{
    /// <summary>未知值（反射读失败）—— 必须放行原逻辑。</summary>
    internal const int UnknownSlotCount = -1;

    /// <summary>该 manager 的视觉球槽数量意味着"没有球可画" ⇒ 跳过动画。</summary>
    internal static bool ShouldSkipOrbVisual(int visualSlotCount)
        => visualSlotCount == 0;
}
