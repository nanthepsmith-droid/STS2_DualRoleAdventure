using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using NUnit.Framework;
using ThirdPartyHotPathFix.Scripts.Patches;

namespace LocalMultiControl.Tests;

/// <summary>
/// `compat-mods/ThirdPartyHotPathFix` 里「放宽 listener 列表初始容量」的 IL 匹配单测。
///
/// 这个补丁是**转译器**，改错不会报错、只会悄悄改坏一个容量表达式（或改到别的 List 构造上），
/// 所以「认得出目标形态 / 不碰别的形态」必须锁死在测试里。
/// 背景：`new List&lt;AbstractModel&gt;(Players.Count * 50)` 在 12 席 + 符文类 mod 时越界，
/// 每次 hook 分发都扩容一次，实测吃主线程 28~30%。
/// </summary>
[TestFixture]
public sealed class ListenerCapacityTranspilerTests
{
    private static readonly ConstructorInfo ListenerListCtor =
        typeof(List<AbstractModel>).GetConstructor(new[] { typeof(int) })!;

    private static readonly ConstructorInfo CardListCtor =
        typeof(List<CardModel>).GetConstructor(new[] { typeof(int) })!;

    [Test]
    public void 认得原版形态_Ldc_I4_S_50_乘_建列表()
    {
        var il = new List<CodeInstruction>
        {
            new(OpCodes.Callvirt, typeof(List<AbstractModel>).GetProperty(nameof(List<AbstractModel>.Count))!.GetGetMethod()),
            new(OpCodes.Ldc_I4_S, (sbyte)50),
            new(OpCodes.Mul),
            new(OpCodes.Newobj, ListenerListCtor),
        };

        int replaced = ListenerListCapacityPatch.RaiseInPlace(il);

        Assert.That(replaced, Is.EqualTo(1));
        Assert.That(il[1].opcode, Is.EqualTo(OpCodes.Ldc_I4));
        Assert.That(il[1].operand, Is.EqualTo(ListenerListCapacityPatch.Multiplier));
    }

    [Test]
    public void 也认_Ldc_I4_满整型常量()
    {
        var il = new List<CodeInstruction>
        {
            new(OpCodes.Ldc_I4, 50),
            new(OpCodes.Mul),
            new(OpCodes.Newobj, ListenerListCtor),
        };

        Assert.That(ListenerListCapacityPatch.RaiseInPlace(il), Is.EqualTo(1));
        Assert.That(il[0].operand, Is.EqualTo(ListenerListCapacityPatch.Multiplier));
    }

    [Test]
    public void 没有乘法就不动()
    {
        var il = new List<CodeInstruction>
        {
            new(OpCodes.Ldc_I4_S, (sbyte)50),
            new(OpCodes.Newobj, ListenerListCtor),
        };

        Assert.That(ListenerListCapacityPatch.RaiseInPlace(il), Is.EqualTo(0), "形态不符宁可不改");
        Assert.That(il[0].operand, Is.EqualTo((sbyte)50));
    }

    [Test]
    public void 别的泛型列表不动()
    {
        var il = new List<CodeInstruction>
        {
            new(OpCodes.Ldc_I4_S, (sbyte)50),
            new(OpCodes.Mul),
            new(OpCodes.Newobj, CardListCtor),
        };

        Assert.That(ListenerListCapacityPatch.RaiseInPlace(il), Is.EqualTo(0), "只碰 List<AbstractModel>");
    }

    [Test]
    public void 替换后保留原指令的分支标签()
    {
        var label = new Label();
        var original = new CodeInstruction(OpCodes.Ldc_I4_S, (sbyte)50);
        original.labels.Add(label);
        var il = new List<CodeInstruction>
        {
            original,
            new(OpCodes.Mul),
            new(OpCodes.Newobj, ListenerListCtor),
        };

        Assert.That(ListenerListCapacityPatch.RaiseInPlace(il), Is.EqualTo(1));
        Assert.That(il[0].labels, Contains.Item(label), "丢了标签会让跳转错位");
    }
}
