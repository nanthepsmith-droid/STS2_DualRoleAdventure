namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「我们自己 push 的大厅页是否还开着」的判定纯函数（r161）。
///
/// 用途：<see cref="LocalSelfCoopSessionGuard"/> 在「未进局 + 查不到挂在回环服务上的大厅」时，
/// 还要再问一句「玩家是不是正停在我们自己的大厅页上」——
/// **每日页的大厅是异步建的**（先 `await` 时间服务器，断网时 DNS 失败还要重试两回），
/// 这段窗口里 `_lobby` 一直是 null；只按大厅判据会误判成"没有大厅页"并把会话清掉，
/// 实机表现 = 断网进「本地·每日挑战」只有单人、连加人按钮都没有（2026-09-27 r159 断网局实测）。
///
/// ⚠ `visible` 这一条**不是**可省的：`NSubmenuStack.Pop` 只把页面 `Visible = false`、**并不把节点移出树**
/// （见 `sts2src/src/Core/Nodes/Screens/MainMenu/NSubmenuStack.cs` 的 `Pop()`），
/// 少了它就会退回 r158 要修的老问题（从大厅页退回主菜单后会话残留 ⇒ 官方联机页冒出我们的按钮）。
/// 抽成纯函数并配单测，就是为了让后来者"顺手简化掉"时先撞到一条红测试。
/// </summary>
internal static class LocalSelfCoopLobbyScreenPolicy
{
    /// <summary>
    /// 记着的大厅页这一帧是否算「会话仍在流程中」。
    ///
    /// 调用方负责先判「引用非空 + Godot 对象仍有效」（那两个判断需要短路的实例调用，不能纯函数化）。
    /// </summary>
    /// <param name="insideTree">页面是否仍在场景树里。</param>
    /// <param name="visible">页面是否可见（被 pop、或被上层子页盖住时为 false）。</param>
    internal static bool IsPageOpen(bool insideTree, bool visible)
    {
        return insideTree && visible;
    }
}
