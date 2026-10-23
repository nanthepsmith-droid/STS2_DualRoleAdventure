using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「这个角色选择页是不是我们自己的本地多角色页」的纯逻辑测试（r317）。
///
/// 钉死的语义（都来自实机日志，别顺手简化）：
/// ① `NSubmenuStack` **复用同一个页面实例** ⇒ 只比引用不够：官方单人入口拿到同一对象、
///    只是 `_lobby` 换成了单人局大厅 ⇒ 必须再问「大厅是不是还挂在我们的回环服务上」；
/// ② `Pop()` 只把页面 `Visible = false`、不移出树（同 `LocalSelfCoopLobbyScreenPolicy` 的 r158/r161 结论）
///    ⇒ 「页面还开着」必须带 visible，否则退回主菜单后会话永不清；
/// ③ 会话没开着时任何页面都不该挂我们的 UI。
/// </summary>
[TestFixture]
public class LocalCharacterSelectPagePolicyTests
{
    [Test]
    public void 我们的页_会话开着_可见_大厅在回环上_才算我们的页()
    {
        Assert.That(
            LocalCharacterSelectPagePolicy.ShouldShowLocalUi(
                sessionEnabled: true, sameScreen: true, pageOpen: true, lobbyUsesLocalLoopback: true),
            Is.True);
    }

    [Test]
    public void 官方单人页复用同一实例_大厅不是回环_不算我们的页()
    {
        // 2026-10-23 r315 日志实证：我们的页退回主菜单后点官方「单人」，拿到同一个 NCharacterSelectScreen，
        // `_lobby` 变成单人局大厅（`[StartRunLobby (1)]`），旧判据只看引用 ⇒ 人数面板/瓦库开关留在官方页上。
        Assert.That(
            LocalCharacterSelectPagePolicy.ShouldShowLocalUi(
                sessionEnabled: true, sameScreen: true, pageOpen: true, lobbyUsesLocalLoopback: false),
            Is.False);
    }

    [Test]
    public void 页面被pop后不算在流程中()
    {
        // Pop 只隐藏不移出树：即使大厅还是我们的回环服务，也不该继续算"仍在流程中"。
        Assert.That(LocalCharacterSelectPagePolicy.IsOurPage(pageOpen: false, lobbyUsesLocalLoopback: true), Is.False);
        Assert.That(
            LocalCharacterSelectPagePolicy.ShouldShowLocalUi(
                sessionEnabled: true, sameScreen: true, pageOpen: false, lobbyUsesLocalLoopback: true),
            Is.False);
    }

    [Test]
    public void 会话没开着时一律不挂UI()
    {
        Assert.That(
            LocalCharacterSelectPagePolicy.ShouldShowLocalUi(
                sessionEnabled: false, sameScreen: true, pageOpen: true, lobbyUsesLocalLoopback: true),
            Is.False);
    }

    [Test]
    public void 不是同一页面实例时不算我们的页()
    {
        Assert.That(
            LocalCharacterSelectPagePolicy.ShouldShowLocalUi(
                sessionEnabled: true, sameScreen: false, pageOpen: true, lobbyUsesLocalLoopback: true),
            Is.False);
    }

    [Test]
    public void 大厅尚未建好_不算我们的页()
    {
        // 角色选择页的大厅在建页时同步初始化（Enable → InitializeMultiplayerAsHost → Push），
        // 所以"还没建好"只可能是官方页/建页失败；判成 false 才安全。
        Assert.That(LocalCharacterSelectPagePolicy.IsOurPage(pageOpen: true, lobbyUsesLocalLoopback: false), Is.False);
    }
}
