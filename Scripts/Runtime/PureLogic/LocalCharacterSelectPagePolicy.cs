namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「这个角色选择页是不是我们自己的本地多角色页」的判定纯函数（r317）。
///
/// 为什么需要：`NSubmenuStack` **复用同一个页面实例**（`GetSubmenuType&lt;NCharacterSelectScreen&gt;()`
/// 返回的是缓存对象），所以我们从自己的大厅退回主菜单、再点官方「单人」时拿到的**是同一个对象** ——
/// 只按 `ReferenceEquals(ActiveCharacterSelectScreen, screen)` 判会继续成立，人数面板 /
/// 全局瓦库开关就留在官方单人页上（2026-10-23 r315 日志实证：`[StartRunLobby (1)]` 之后
/// 仍打出「角色选择页已创建本地人数 +/- 实体按钮」）。
///
/// 真正的分水岭是**大厅的宿主服务**：我们的页挂在 `LocalLoopbackHostGameService` 上，
/// 官方单人页是 `NetSingleplayerGameService`、官方联机页是真实 NetHost。
///
/// 另一半原因在会话侧：`NSubmenuStack.Pop()` 只把页面 `Visible = false`、**不移出树**，
/// 所以「页面开着」必须带 `visible`（同 <see cref="LocalSelfCoopLobbyScreenPolicy"/> 的 r158/r161 结论），
/// 否则会话守卫会一直以为玩家还停在我们的大厅里 ⇒ 会话永不清、残留状态继续污染官方页面。
/// 抽成纯函数并配单测，是为了让后来者"顺手简化掉"时先撞到一条红测试。
/// </summary>
internal static class LocalCharacterSelectPagePolicy
{
    /// <summary>页面这一帧是否仍算「我们的本地多角色角色选择页」（会话守卫用）。</summary>
    /// <param name="pageOpen">页面在树上**且可见**（被 pop 掉的页面只是隐藏，仍在树上）。</param>
    /// <param name="lobbyUsesLocalLoopback">页面当前挂的大厅是否仍在我们建的本地回环服务上。</param>
    internal static bool IsOurPage(bool pageOpen, bool lobbyUsesLocalLoopback)
    {
        return pageOpen && lobbyUsesLocalLoopback;
    }

    /// <summary>页面上是否该挂我们的注入 UI（人数面板 / 全局瓦库开关）。</summary>
    /// <param name="sessionEnabled">本地多控会话是否开着。</param>
    /// <param name="sameScreen">是否就是会话登记的那一页（`ActiveCharacterSelectScreen`）。</param>
    /// <param name="pageOpen">页面在树上**且可见**。</param>
    /// <param name="lobbyUsesLocalLoopback">页面当前挂的大厅是否仍在我们建的本地回环服务上。</param>
    internal static bool ShouldShowLocalUi(
        bool sessionEnabled,
        bool sameScreen,
        bool pageOpen,
        bool lobbyUsesLocalLoopback)
    {
        return sessionEnabled && sameScreen && IsOurPage(pageOpen, lobbyUsesLocalLoopback);
    }
}
