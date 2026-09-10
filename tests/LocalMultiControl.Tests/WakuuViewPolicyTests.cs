using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 瓦库托管视角策略（改进-2 / Phase 0）判定纯逻辑测试。
///
/// 覆盖三条不变式：
/// 1. 只治理【瓦库形态】角色（真人 / 未开启形态一律不抑制切换）；
/// 2. 「后台托管」关闭时档位不生效（等价全程跟随，向后兼容）；
/// 3. 两处防软锁兜底（安全网救援、作用域外真人交互选牌）在后台托管开启时不受档位影响。
/// 以及三档 × 六个触发场景的真值表。
/// </summary>
[TestFixture]
public class WakuuViewPolicyTests
{
    private static bool Suppress(
        string? mode,
        WakuuViewTrigger trigger,
        bool isWakuuFormPlayer = true,
        bool backgroundMode = true,
        bool selectorActive = false)
    {
        return WakuuViewPolicy.ShouldSuppressSwitch(
            mode, backgroundMode, trigger, isWakuuFormPlayer, selectorActive);
    }

    // ---- 不变式 1：只治理瓦库形态角色 ----

    [Test]
    public void 非瓦库形态角色_任何档位与场景都不抑制()
    {
        foreach (string mode in new[] { WakuuViewModes.Never, WakuuViewModes.KeyNodes, WakuuViewModes.Always })
        {
            foreach (WakuuViewTrigger trigger in System.Enum.GetValues<WakuuViewTrigger>())
            {
                Assert.That(
                    Suppress(mode, trigger, isWakuuFormPlayer: false),
                    Is.False,
                    $"mode={mode}, trigger={trigger} 不应抑制真人的切前台动作");
            }
        }
    }

    // ---- 不变式 2：后台托管关闭时档位不生效 ----

    [Test]
    public void 后台托管关闭_任何档位与场景都不抑制()
    {
        foreach (string mode in new[] { WakuuViewModes.Never, WakuuViewModes.KeyNodes, WakuuViewModes.Always })
        {
            foreach (WakuuViewTrigger trigger in System.Enum.GetValues<WakuuViewTrigger>())
            {
                Assert.Multiple(() =>
                {
                    Assert.That(
                        Suppress(mode, trigger, backgroundMode: false),
                        Is.False,
                        $"mode={mode}, trigger={trigger}：后台托管关闭应等价全程跟随");
                    Assert.That(
                        Suppress(mode, trigger, backgroundMode: false, selectorActive: true),
                        Is.False,
                        $"mode={mode}, trigger={trigger}：后台托管关闭时选择器也不改变结论（旧行为）");
                });
            }
        }
    }

    // ---- 不变式 3：防软锁兜底不看档位 ----

    [Test]
    public void 安全网救援_任何档位都不抑制()
    {
        foreach (string mode in new[] { WakuuViewModes.Never, WakuuViewModes.KeyNodes, WakuuViewModes.Always })
        {
            Assert.That(
                Suppress(mode, WakuuViewTrigger.SafetyNetStall),
                Is.False,
                $"mode={mode}：安全网救援必须能切前台，否则硬软锁无解");
        }
    }

    [Test]
    public void 真人交互选牌_无全局选择器时任何档位都必须切()
    {
        foreach (string mode in new[] { WakuuViewModes.Never, WakuuViewModes.KeyNodes, WakuuViewModes.Always })
        {
            Assert.That(
                Suppress(mode, WakuuViewTrigger.HumanInteractionChoice, selectorActive: false),
                Is.False,
                $"mode={mode}：作用域外真人交互选牌必须切前台，否则流程挂死");
        }
    }

    [Test]
    public void 真人交互选牌_有全局选择器时抑制切换_因为会被自动作答不弹UI()
    {
        foreach (string mode in new[] { WakuuViewModes.Never, WakuuViewModes.KeyNodes, WakuuViewModes.Always })
        {
            Assert.That(
                Suppress(mode, WakuuViewTrigger.HumanInteractionChoice, selectorActive: true),
                Is.True,
                $"mode={mode}：有全局选择器 = 自动作答不弹 UI，切了也没意义");
        }
    }

    // ---- 档位真值表：不跟随（默认） ----

    [Test]
    public void 不跟随_常规路径一律抑制()
    {
        foreach (WakuuViewTrigger trigger in new[]
        {
            WakuuViewTrigger.TurnStart,
            WakuuViewTrigger.TurnEnd,
            WakuuViewTrigger.HookEnqueue,
            WakuuViewTrigger.ReactivePlay,
        })
        {
            Assert.That(
                Suppress(WakuuViewModes.Never, trigger),
                Is.True,
                $"never 档应抑制常规路径的前台切换：trigger={trigger}");
        }
    }

    [Test]
    public void 不跟随_是默认档()
    {
        Assert.That(WakuuViewModes.Default, Is.EqualTo(WakuuViewModes.Never));
    }

    // ---- 档位真值表：仅关键节点 ----

    [Test]
    public void 仅关键节点_回合开始时跟随_其余常规路径抑制()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Suppress(WakuuViewModes.KeyNodes, WakuuViewTrigger.TurnStart), Is.False);
            Assert.That(Suppress(WakuuViewModes.KeyNodes, WakuuViewTrigger.TurnEnd), Is.True);
            Assert.That(Suppress(WakuuViewModes.KeyNodes, WakuuViewTrigger.HookEnqueue), Is.True);
            Assert.That(Suppress(WakuuViewModes.KeyNodes, WakuuViewTrigger.ReactivePlay), Is.True);
        });
    }

    // ---- 档位真值表：全程跟随 ----

    [Test]
    public void 全程跟随_常规路径一律不抑制()
    {
        foreach (WakuuViewTrigger trigger in new[]
        {
            WakuuViewTrigger.TurnStart,
            WakuuViewTrigger.TurnEnd,
            WakuuViewTrigger.HookEnqueue,
            WakuuViewTrigger.ReactivePlay,
        })
        {
            Assert.That(
                Suppress(WakuuViewModes.Always, trigger),
                Is.False,
                $"always 档应放行常规路径的前台切换：trigger={trigger}");
        }
    }

    // ---- 非法 / 缺失档位一律回退默认（不跟随） ----

    [Test]
    public void 非法或缺失档位_回退默认不跟随()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Suppress(null, WakuuViewTrigger.TurnStart), Is.True);
            Assert.That(Suppress("", WakuuViewTrigger.TurnStart), Is.True);
            Assert.That(Suppress("bogus", WakuuViewTrigger.TurnStart), Is.True);
            Assert.That(Suppress("  NEVER  ", WakuuViewTrigger.TurnStart), Is.True);
        });
    }

    [Test]
    public void 档位取值_大小写与空白容忍()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuViewModes.Normalize("keyNodes"), Is.EqualTo(WakuuViewModes.KeyNodes));
            Assert.That(WakuuViewModes.Normalize("KEYNODES"), Is.EqualTo(WakuuViewModes.KeyNodes));
            Assert.That(WakuuViewModes.Normalize("  always "), Is.EqualTo(WakuuViewModes.Always));
            Assert.That(WakuuViewModes.Normalize("never"), Is.EqualTo(WakuuViewModes.Never));
            Assert.That(WakuuViewModes.Normalize(null), Is.EqualTo(WakuuViewModes.Never));
            Assert.That(WakuuViewModes.Normalize("whatever"), Is.EqualTo(WakuuViewModes.Never));
        });
    }

    [Test]
    public void 仅关键节点_大小写变体也生效()
    {
        Assert.That(Suppress("KeyNodes", WakuuViewTrigger.TurnStart), Is.False);
        Assert.That(Suppress(" ALWAYS ", WakuuViewTrigger.TurnEnd), Is.False);
    }

    // ---- 「仅关键节点」peek（看一眼后自动切回）判定 ----

    [Test]
    public void peek_仅关键节点且后台托管且瓦库形态时为真()
    {
        Assert.That(
            WakuuViewPolicy.ShouldPeekAtTurnStart(WakuuViewModes.KeyNodes, backgroundMode: true, isWakuuFormPlayer: true),
            Is.True);
    }

    [Test]
    public void peek_其它档位或非瓦库或后台托管关闭时为假()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuViewPolicy.ShouldPeekAtTurnStart(WakuuViewModes.Never, true, true), Is.False);
            Assert.That(WakuuViewPolicy.ShouldPeekAtTurnStart(WakuuViewModes.Always, true, true), Is.False);
            Assert.That(WakuuViewPolicy.ShouldPeekAtTurnStart(WakuuViewModes.KeyNodes, false, true), Is.False);
            Assert.That(WakuuViewPolicy.ShouldPeekAtTurnStart(WakuuViewModes.KeyNodes, true, false), Is.False);
            Assert.That(WakuuViewPolicy.ShouldPeekAtTurnStart("bogus", true, true), Is.False);
            Assert.That(WakuuViewPolicy.ShouldPeekAtTurnStart(null, true, true), Is.False);
        });
    }

    [Test]
    public void peek_与档位表一致_仅关键节点时回合开始不抑制()
    {
        // 一致性：peek 为真 ⇔ keyNodes 下 TurnStart 不被抑制（否则会「说好 peek 却被抑制」）
        Assert.Multiple(() =>
        {
            Assert.That(Suppress(WakuuViewModes.KeyNodes, WakuuViewTrigger.TurnStart), Is.False);
            Assert.That(WakuuViewPolicy.ShouldPeekAtTurnStart(WakuuViewModes.KeyNodes, true, true), Is.True);
        });
    }
}
