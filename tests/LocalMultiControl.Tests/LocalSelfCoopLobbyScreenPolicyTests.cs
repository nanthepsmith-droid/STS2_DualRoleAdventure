using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 会话守卫的「大厅页判据」（r161）纯逻辑测试：必须在树上**且可见**才算"还停在页面上"。
///
/// 为什么这条值得钉死：`NSubmenuStack.Pop()` 只把页面 `Visible = false`、并不移出树，
/// 所以少了 `visible` 判据 ⇒ 从大厅页退回主菜单后会话会残留（r158 修过的老问题复发）；
/// 而要求 `visible` 又正好让断网时的"每日页开着、`_lobby` 还没建好"不被误清（r161 新修的问题）。
/// </summary>
[TestFixture]
public class LocalSelfCoopLobbyScreenPolicyTests
{
    [Test]
    public void 页面在树上且可见才算还停在页面上()
    {
        Assert.That(LocalSelfCoopLobbyScreenPolicy.IsPageOpen(true, true), Is.True);
    }

    [Test]
    public void 页面被pop后不算在流程中()
    {
        // Pop 只把 Visible 打成 false（节点仍在树上）—— 判据必须把它当"已离开"
        Assert.That(LocalSelfCoopLobbyScreenPolicy.IsPageOpen(true, false), Is.False);
    }

    [Test]
    public void 页面不在树上时不算在流程中()
    {
        Assert.That(LocalSelfCoopLobbyScreenPolicy.IsPageOpen(false, true), Is.False);
    }

    [Test]
    public void 页面不可见且不在树上时不算在流程中()
    {
        Assert.That(LocalSelfCoopLobbyScreenPolicy.IsPageOpen(false, false), Is.False);
    }
}
