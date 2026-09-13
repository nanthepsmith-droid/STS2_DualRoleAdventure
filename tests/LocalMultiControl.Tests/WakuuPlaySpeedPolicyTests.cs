using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「瓦库出牌加速（跳过卡牌堆动画）」判定纯逻辑测试（改进-2 / r117）。
///
/// 只有「开关开 + 本地多控生效 + 出牌者是瓦库形态」三个条件同时成立才跳过演出；
/// 任一不成立都保持原生观感（单人局、真人角色一律不干预）。
/// </summary>
[TestFixture]
public class WakuuPlaySpeedPolicyTests
{
    private static bool Skip(bool toggle, bool localMulti, bool isWakuu)
        => WakuuPlaySpeedPolicy.ShouldSkipCardPileVisuals(toggle, localMulti, isWakuu);

    [Test]
    public void 三条件全满足才跳过()
    {
        Assert.That(Skip(toggle: true, localMulti: true, isWakuu: true), Is.True);
    }

    [Test]
    public void 开关关闭_不跳过()
    {
        Assert.That(Skip(toggle: false, localMulti: true, isWakuu: true), Is.False);
    }

    [Test]
    public void 非本地多控_不跳过_保持原生观感()
    {
        Assert.That(Skip(toggle: true, localMulti: false, isWakuu: true), Is.False);
    }

    [Test]
    public void 出牌者不是瓦库形态_不跳过()
    {
        Assert.That(Skip(toggle: true, localMulti: true, isWakuu: false), Is.False);
    }

    [Test]
    public void 真值表_全组合()
    {
        int skipped = 0;
        foreach (bool toggle in new[] { false, true })
        {
            foreach (bool localMulti in new[] { false, true })
            {
                foreach (bool isWakuu in new[] { false, true })
                {
                    if (Skip(toggle, localMulti, isWakuu))
                    {
                        skipped++;
                        Assert.That(toggle && localMulti && isWakuu, Is.True);
                    }
                }
            }
        }

        Assert.That(skipped, Is.EqualTo(1));
    }

    [Test]
    public void 配置默认开启加速()
    {
        // 默认开是本轮的拍板（多瓦库串行、单张 1.0~1.4s，加速收益最大）；
        // 关掉即恢复完整演出，行为与旧版一致。
        Assert.That(new WakuuConfigData().fastWakuuPlay, Is.True);
    }
}
