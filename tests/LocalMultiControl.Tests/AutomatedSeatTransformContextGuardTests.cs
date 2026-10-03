using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 变换期间「上下文被钉回托管席位」的拉回判定（r201 / BUG-29 第三轮）纯逻辑测试。
///
/// 判定只在一种情况下干预：**上下文此刻是托管席位**（否则与本次变换无关，真人自己的变换视觉照旧）。
/// 拉回目标：受控（前台）席位是另一个本地席位时让给它；否则让到 null（<c>IsMe</c> 恒 false）。
/// </summary>
[TestFixture]
public class AutomatedSeatTransformContextGuardTests
{
    private const ulong Wakuu = 327;
    private const ulong Human = 326;
    private const ulong Remote = 999;

    [Test]
    public void 上下文不是托管席位_不干预()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                AutomatedSeatTransformContextGuard.TryResolveSafeNetId(Human, false, Wakuu, true, out _),
                Is.False);
            Assert.That(
                AutomatedSeatTransformContextGuard.TryResolveSafeNetId(Remote, false, Human, true, out _),
                Is.False);
        });
    }

    [Test]
    public void 上下文为空_不干预()
    {
        // 没有上下文（IsMe 本来就恒 false）⇒ 无需干预。
        Assert.That(
            AutomatedSeatTransformContextGuard.TryResolveSafeNetId(null, true, Human, true, out _),
            Is.False);
    }

    [Test]
    public void 上下文是托管席位_受控位是另一个本地席位_让给受控位()
    {
        Assert.That(
            AutomatedSeatTransformContextGuard.TryResolveSafeNetId(Wakuu, true, Human, true, out ulong? safe),
            Is.True);
        Assert.That(safe, Is.EqualTo(Human));
    }

    [Test]
    public void 上下文是托管席位_受控位就是它自己_让到空()
    {
        // 实机（BUG-25 唯我）教训：安全值不能是牌主人自己，否则 IsMine 仍为 true。
        Assert.That(
            AutomatedSeatTransformContextGuard.TryResolveSafeNetId(Wakuu, true, Wakuu, true, out ulong? safe),
            Is.True);
        Assert.That(safe, Is.Null);
    }

    [Test]
    public void 上下文是托管席位_受控位缺失或非本地_让到空()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                AutomatedSeatTransformContextGuard.TryResolveSafeNetId(Wakuu, true, null, false, out ulong? safeNoControl),
                Is.True);
            Assert.That(safeNoControl, Is.Null);

            Assert.That(
                AutomatedSeatTransformContextGuard.TryResolveSafeNetId(Wakuu, true, Remote, false, out ulong? safeRemote),
                Is.True);
            Assert.That(safeRemote, Is.Null);
        });
    }

    [Test]
    public void 真值表_逐格期望成立()
    {
        ulong?[] currents = { null, Human, Wakuu };
        ulong?[] controlled = { null, Human, Wakuu, Remote };

        foreach (ulong? current in currents)
        {
            foreach (bool isAutomated in new[] { true, false })
            {
                foreach (ulong? control in controlled)
                {
                    foreach (bool controlIsLocal in new[] { true, false })
                    {
                        bool resolved = AutomatedSeatTransformContextGuard.TryResolveSafeNetId(
                            current, isAutomated, control, controlIsLocal, out ulong? safe);

                        // 期望"是否需要干预" = 上下文存在且是托管席位。
                        Assert.That(resolved, Is.EqualTo(current.HasValue && isAutomated),
                            $"是否需要拉回漂移: current={current?.ToString() ?? "null"}, auto={isAutomated}");

                        if (!resolved)
                        {
                            continue;
                        }

                        // 期望"拉回目标" = 受控位（需为另一个本地席位）否则 null。
                        ulong? expectedSafe = control.HasValue
                                              && control.Value != current!.Value
                                              && controlIsLocal
                            ? control.Value
                            : null;
                        Assert.That(safe, Is.EqualTo(expectedSafe),
                            $"拉回目标漂移: current={current}, control={control?.ToString() ?? "null"}, "
                            + $"controlIsLocal={controlIsLocal}");
                    }
                }
            }
        }
    }
}
