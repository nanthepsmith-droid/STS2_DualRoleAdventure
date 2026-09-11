using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 选牌选择器「按归属者分发」判定纯逻辑测试（改进-2 / Phase 1）。
///
/// 三条不变式：
/// 1. 归属者未知 → 保持栈顶（与升级前完全一致，信息不足不冒险）；
/// 2. 归属者是**真人** → 一律摘掉托管选择器（返回 null）走 UI，无论注册表是否命中；
/// 3. 归属者是**瓦库形态** → 注册表命中才精确分发，未命中退回栈顶（兼容旧行为）。
/// </summary>
[TestFixture]
public class WakuuSelectorDispatchTests
{
    // ---- 不变式 1：信息不足 ----

    [Test]
    public void 无归属者_保持栈顶()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                WakuuSelectorDispatch.Decide(hasChooser: false, registryHit: false, chooserIsWakuu: false),
                Is.EqualTo(SelectorDispatchDecision.KeepTop));
            Assert.That(
                WakuuSelectorDispatch.Decide(hasChooser: false, registryHit: true, chooserIsWakuu: true),
                Is.EqualTo(SelectorDispatchDecision.KeepTop),
                "归属者未知时不应按注册表猜测（宁可退回旧行为）");
        });
    }

    // ---- 不变式 2：真人绝不自动作答 ----

    [Test]
    public void 真人归属者_未命中注册表_摘掉选择器走UI()
    {
        Assert.That(
            WakuuSelectorDispatch.Decide(hasChooser: true, registryHit: false, chooserIsWakuu: false),
            Is.EqualTo(SelectorDispatchDecision.ReturnNull));
    }

    [Test]
    public void 真人归属者_即使命中注册表也摘掉_防静默吃掉真人选择()
    {
        // 防御性：登记表只该装瓦库；万一遇到非瓦库 id，宁可走 UI 也不能自动作答
        // （r106 工具箱教训：真人的选择被静默吃掉比"多弹一次界面"恶劣得多）
        Assert.That(
            WakuuSelectorDispatch.Decide(hasChooser: true, registryHit: true, chooserIsWakuu: false),
            Is.EqualTo(SelectorDispatchDecision.ReturnNull));
    }

    // ---- 不变式 3：瓦库按归属分发 / 未登记退回栈顶 ----

    [Test]
    public void 瓦库归属者_命中注册表_使用自己的选择器()
    {
        Assert.That(
            WakuuSelectorDispatch.Decide(hasChooser: true, registryHit: true, chooserIsWakuu: true),
            Is.EqualTo(SelectorDispatchDecision.UseOwned));
    }

    [Test]
    public void 瓦库归属者_未命中注册表_退回栈顶语义()
    {
        Assert.That(
            WakuuSelectorDispatch.Decide(hasChooser: true, registryHit: false, chooserIsWakuu: true),
            Is.EqualTo(SelectorDispatchDecision.KeepTop));
    }

    // ---- 全组合真值表 ----

    [Test]
    public void 真值表_全组合与设计表一致()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuSelectorDispatch.Decide(false, false, false), Is.EqualTo(SelectorDispatchDecision.KeepTop));
            Assert.That(WakuuSelectorDispatch.Decide(false, false, true), Is.EqualTo(SelectorDispatchDecision.KeepTop));
            Assert.That(WakuuSelectorDispatch.Decide(false, true, false), Is.EqualTo(SelectorDispatchDecision.KeepTop));
            Assert.That(WakuuSelectorDispatch.Decide(false, true, true), Is.EqualTo(SelectorDispatchDecision.KeepTop));
            Assert.That(WakuuSelectorDispatch.Decide(true, false, false), Is.EqualTo(SelectorDispatchDecision.ReturnNull));
            Assert.That(WakuuSelectorDispatch.Decide(true, false, true), Is.EqualTo(SelectorDispatchDecision.KeepTop));
            Assert.That(WakuuSelectorDispatch.Decide(true, true, false), Is.EqualTo(SelectorDispatchDecision.ReturnNull));
            Assert.That(WakuuSelectorDispatch.Decide(true, true, true), Is.EqualTo(SelectorDispatchDecision.UseOwned));
        });
    }

    [Test]
    public void 只有瓦库加命中注册表这一种组合才精确分发()
    {
        int useOwnedCount = 0;
        foreach (bool hasChooser in new[] { false, true })
        {
            foreach (bool registryHit in new[] { false, true })
            {
                foreach (bool chooserIsWakuu in new[] { false, true })
                {
                    if (WakuuSelectorDispatch.Decide(hasChooser, registryHit, chooserIsWakuu)
                        == SelectorDispatchDecision.UseOwned)
                    {
                        useOwnedCount++;
                        Assert.That(hasChooser && registryHit && chooserIsWakuu, Is.True);
                    }
                }
            }
        }

        Assert.That(useOwnedCount, Is.EqualTo(1));
    }
}
