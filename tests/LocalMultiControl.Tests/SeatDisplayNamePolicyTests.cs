using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 本地席位显示名（<c>botN</c> / <c>playerN</c>）的纯逻辑映射（2026-11-18 §排期 6）。
///
/// 口径：<c>N = 会话席位序 + 1</c>（与 Tab 循环 / 小键盘切人同序，两种称呼**共用同一套号位**）；
/// 非本地席位必须能识别出来（返回 null ⇒ 调用方放行原版昵称，真联机队友的名字一个字都不能动）。
/// </summary>
[TestFixture]
public class SeatDisplayNamePolicyTests
{
    [Test]
    public void 瓦库席位_从0起转成botN()
    {
        Assert.That(SeatDisplayNamePolicy.ResolveFromSlotIndex(0, SeatDisplayKind.Wakuu), Is.EqualTo("bot1"));
        Assert.That(SeatDisplayNamePolicy.ResolveFromSlotIndex(1, SeatDisplayKind.Wakuu), Is.EqualTo("bot2"));
        Assert.That(SeatDisplayNamePolicy.ResolveFromSlotIndex(11, SeatDisplayKind.Wakuu), Is.EqualTo("bot12"));
    }

    [Test]
    public void 真人席位_从0起转成playerN()
    {
        Assert.That(SeatDisplayNamePolicy.ResolveFromSlotIndex(0, SeatDisplayKind.Human), Is.EqualTo("player1"));
        Assert.That(SeatDisplayNamePolicy.ResolveFromSlotIndex(2, SeatDisplayKind.Human), Is.EqualTo("player3"));
        Assert.That(SeatDisplayNamePolicy.ResolveFromSlotIndex(11, SeatDisplayKind.Human), Is.EqualTo("player12"));
    }

    [Test]
    public void 两种称呼共用同一套号位()
    {
        // 混合局：1/2 号是瓦库、3 号是真人 ⇒ bot1 / bot2 / player3（号位 = 小键盘切人的号位）。
        Assert.That(SeatDisplayNamePolicy.ResolveFromSlotIndex(0, SeatDisplayKind.Wakuu), Is.EqualTo("bot1"));
        Assert.That(SeatDisplayNamePolicy.ResolveFromSlotIndex(1, SeatDisplayKind.Wakuu), Is.EqualTo("bot2"));
        Assert.That(SeatDisplayNamePolicy.ResolveFromSlotIndex(2, SeatDisplayKind.Human), Is.EqualTo("player3"));
    }

    [Test]
    public void 席位下标_负数等于不是本地席位()
    {
        Assert.That(SeatDisplayNamePolicy.ResolveFromSlotIndex(-1, SeatDisplayKind.Human), Is.Null);
        Assert.That(SeatDisplayNamePolicy.ResolveFromSlotIndex(-1, SeatDisplayKind.Wakuu), Is.Null);
        Assert.That(SeatDisplayNamePolicy.ResolveFromSlotIndex(int.MinValue, SeatDisplayKind.Wakuu), Is.Null);
    }

    [Test]
    public void 名字必须语言无关_不含中文与空格()
    {
        // 这条钉死"不许再走 角色N / Player N 那套按语言分支的文案"：切语言后名字不该变。
        foreach (string label in new[]
                 {
                     SeatDisplayNamePolicy.ResolveFromSlotIndex(0, SeatDisplayKind.Wakuu)!,
                     SeatDisplayNamePolicy.ResolveFromSlotIndex(11, SeatDisplayKind.Human)!,
                 })
        {
            Assert.That(label, Does.Not.Contain(" "));
            Assert.That(label, Does.Not.Contain("角色"));
            Assert.That(label, Does.Not.Contain("Player"));
            Assert.That(
                label.StartsWith(SeatDisplayNamePolicy.BotPrefix)
                || label.StartsWith(SeatDisplayNamePolicy.PlayerPrefix),
                Is.True);
        }
    }
}
