using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「读档窗口」纯逻辑测试（r167）：窗口有效期内会话守卫不下手；
/// 未开启 / 已过期 / 时钟回退 一律视为不活跃（保证"读档取消后会话残留"这个老问题能照旧被收拾）。
/// </summary>
[TestFixture]
public class LoadReplayWindowPolicyTests
{
    [Test]
    public void 未开启窗口_不活跃()
    {
        Assert.That(LoadReplayWindowPolicy.IsActive(openedAtMs: 0, nowMs: 1_000), Is.False);
        Assert.That(LoadReplayWindowPolicy.IsActive(openedAtMs: -5, nowMs: 1_000), Is.False);
    }

    [Test]
    public void 刚开启_活跃()
    {
        Assert.That(LoadReplayWindowPolicy.IsActive(openedAtMs: 10_000, nowMs: 10_000), Is.True);
    }

    [Test]
    public void 未到超时_活跃()
    {
        Assert.That(
            LoadReplayWindowPolicy.IsActive(openedAtMs: 10_000, nowMs: 10_000 + 179_999),
            Is.True);
    }

    [Test]
    public void 超过超时_不活跃()
    {
        Assert.That(
            LoadReplayWindowPolicy.IsActive(openedAtMs: 10_000, nowMs: 10_000 + 180_000),
            Is.False);
    }

    [Test]
    public void 时钟回退_不活跃()
    {
        Assert.That(LoadReplayWindowPolicy.IsActive(openedAtMs: 10_000, nowMs: 9_000), Is.False);
    }

    [Test]
    public void 超时为零或负数_不活跃()
    {
        Assert.That(LoadReplayWindowPolicy.IsActive(10_000, 10_001, timeoutMs: 0), Is.False);
        Assert.That(LoadReplayWindowPolicy.IsActive(10_000, 10_001, timeoutMs: -1), Is.False);
    }

    [Test]
    public void 默认超时为三分钟()
    {
        Assert.That(LoadReplayWindowPolicy.DefaultTimeoutMs, Is.EqualTo(180_000));
    }
}
