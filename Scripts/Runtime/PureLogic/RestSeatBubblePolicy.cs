namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「第三方席位的休息区选择要不要补画气泡」的纯判定。
///
/// 背景（2026-09-26 实机定位，marker r148 那局 r146 之后的第一幕整局）：
/// 非本地席位的休息区选择是在 <c>RestSiteSynchronizer.BeginRestSite</c> 那一刻就被索取、并且被当场作答的
/// （Co-op Bots 的**合成 Bot** 正是这条路径：日志里它在 `Preloading 'RestSite Room'` **之前**就选完了），
/// 而休息区房间节点是**之后**才实例化的 ⇒ 整个选择过程没有任何动画、也没有角色气泡，
/// 玩家会以为「Bot 在休息处不会行动」。
///
/// 做法：在游戏自己的 <c>AfterPlayerOptionChosen</c> 事件里把结果记下来，等房间就绪后补画一个「已选」气泡。
/// **纯表现，不改任何数据**；判据放这里以便单测。
/// </summary>
internal static class RestSeatBubblePolicy
{
    /// <summary>
    /// 只有「本地多控已启用」且「该席位不属于本地多控会话」（= 第三方席位，典型是 CB 的合成 Bot）才需要补画。
    /// 本地席位不用管：真人由原版 UI 显示，瓦库走 <c>LocalWakuuRestAutoChoice</c> 自己驱动气泡。
    /// </summary>
    public static bool ShouldRecord(bool isLocalMultiControlEnabled, bool isLocalSessionSeat)
    {
        return isLocalMultiControlEnabled && !isLocalSessionSeat;
    }

    /// <summary>
    /// 只有游戏侧返回 success 时才画「已选」气泡；失败（例如 MEND 没拿到目标、
    /// 或玩家自己点的选项被游戏拒掉）只记一条 WARN —— 否则正好会把"其实没生效的选择"画成已选，
    /// 而这正是我们要避免的误判。
    /// </summary>
    public static bool ShouldShowSelectedBubble(bool success)
    {
        return success;
    }
}
