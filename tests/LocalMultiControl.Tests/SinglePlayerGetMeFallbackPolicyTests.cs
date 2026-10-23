using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「单人局 GetMe 回退」判据的纯逻辑测试（2026-10-23）。
///
/// 背景：原版单人开局固定玩家 NetId=1，本地回环下 <c>LocalContext.NetId</c> 是 Steam id，
/// 第三方（猪猪云统计）在开局后置里调 GetMe 会抛 ⇒ 黑屏。判据错的两个方向都糟：
/// 放太宽会把双人局里"查不到就回退唯一玩家"这种真异常吞掉；放太窄黑屏照旧。
/// </summary>
[TestFixture]
public class SinglePlayerGetMeFallbackPolicyTests
{
    private static readonly ulong SteamId = 76561198422527326UL;
    private static readonly IReadOnlyCollection<ulong> LocalIds = new[] { SteamId, 76561198422527327UL };

    private static bool Should(bool found, int count, ulong soleNetId)
    {
        return SinglePlayerGetMeFallbackPolicy.ShouldFallback(found, count, soleNetId);
    }

    [Test]
    public void 单人局_查不到_唯一玩家是原版占位id_要回退()
    {
        Assert.That(Should(found: false, count: 1, soleNetId: 1UL), Is.True);
    }

    [Test]
    public void 断线残留席位表_也不影响_按占位id判_r316补()
    {
        // r315 用"不在本地席位表"当判据，实测断线后席位表残留上局 Steam id ⇒ 误挡。r316 按占位 id 判。
        Assert.That(Should(found: false, count: 1, soleNetId: 1UL), Is.True);
    }

    [Test]
    public void 查得到本地玩家_不回退()
    {
        Assert.That(Should(found: true, count: 1, soleNetId: 1UL), Is.False);
    }

    [Test]
    public void 双人局_不回退()
    {
        Assert.That(Should(found: false, count: 2, soleNetId: 1UL), Is.False);
    }

    [Test]
    public void 会话错位_唯一玩家是真实席位id_不回退()
    {
        // 集合里那个玩家不是原版占位 id ⇒ 不是"被污染的单人开局"，回退会返回错误身份。
        Assert.That(Should(found: false, count: 1, soleNetId: SteamId), Is.False);
    }
}
