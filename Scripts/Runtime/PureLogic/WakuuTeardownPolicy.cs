namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 运行/战斗「清理期预期异常」的判定纯函数（r110）。
///
/// 背景（实机 marker r107）：退出这一局回主菜单时，日志出现
/// <c>瓦库选择器作用域异常退出: player=…327, round=2, error=Nullable object must have a value.</c>
/// 与随之而来的 <c>瓦库看门狗重启失败</c>。上下文显示它发生在 <c>RunManager.CleanUp</c>：
/// 清除时的探针是 <c>瓦库选择器栈探针: source=run-cleanup, …, inFlight=1</c> ——
/// **自动出牌作用域仍在飞，而游戏状态已经被拆掉**，异步链里访问已失效的 Nullable 就抛异常。
///
/// 这类异常不是故障，只是「清理期正常中止」：不该打 WARN，更不该上抛
/// （上抛会让看门狗再报一条失败，把一次正常退出记成两个问题）。
/// </summary>
internal static class WakuuTeardownPolicy
{
    /// <summary>
    /// 该异常是否应视为「清理期预期中止」（＝ 不打 WARN、不上抛）。
    /// 判定只用两个客观事实，便于单测：运行是否仍在进行、是否还能取到 RunState。
    /// </summary>
    /// <param name="runInProgress"><c>RunManager.Instance.IsInProgress</c>。</param>
    /// <param name="hasRunState"><c>RunManager.Instance.DebugOnlyGetState() != null</c>。</param>
    public static bool ShouldTreatAsExpectedAbort(bool runInProgress, bool hasRunState)
    {
        return !runInProgress || !hasRunState;
    }
}
