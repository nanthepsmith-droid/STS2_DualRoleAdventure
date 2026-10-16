#nullable enable
using System;
using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 战灵召唤 · **预设可用性判据**（2026-10-12）的纯逻辑用例。
///
/// <para>口径：跨机器传的预设如果引用了**本机没有**的角色 / 卡 / 遗物，这条预设应当被跳过（不进候选、
/// 不进随机池、不当默认），其余预设照常 —— 而不是召出一个"皮肤被换掉、卡组被换掉"的残缺战神。</para>
///
/// <para>⚠ 两条最容易做错的地方钉在这里：① <c>deck</c> 为 <c>null</c>（继承）/ 空串（不替换）都**不是**缺失；
/// ② **附魔缺失不算不可用**（只影响那张牌的附加效果，卡照给）。</para>
/// </summary>
[TestFixture]
public class HellwarPresetAvailabilityTests
{
    private static readonly HashSet<string> Characters = new(StringComparer.OrdinalIgnoreCase) { "SILENT", "DEFECT" };
    private static readonly HashSet<string> Cards = new(StringComparer.OrdinalIgnoreCase) { "BASH", "STRIKE_IRONCLAD" };
    private static readonly HashSet<string> Relics = new(StringComparer.OrdinalIgnoreCase) { "PEN_NIB" };

    private static HellwarPresetAvailabilityResult Check(string? skin, string? deck, IReadOnlyList<string>? relics)
        => HellwarPresetAvailability.Evaluate(skin, deck, relics, Characters.Contains, Cards.Contains, Relics.Contains);

    [Test]
    public void 全继承的内置式预设永远可用()
    {
        HellwarPresetAvailabilityResult result = Check(null, null, null);

        Assert.That(result.Usable, Is.True);
        Assert.That(result.Reason, Is.Empty);
    }

    [Test]
    public void 召唤者皮肤不做角色表查询()
    {
        Assert.That(Check("summoner", null, null).Usable, Is.True);
        Assert.That(Check("Summoner", null, null).Usable, Is.True, "大小写不敏感");
    }

    [Test]
    public void 本机有的角色皮可用本机没有的不可用()
    {
        Assert.That(Check("SILENT", null, null).Usable, Is.True);

        HellwarPresetAvailabilityResult missing = Check("MOD_CHARACTER", null, null);
        Assert.That(missing.Usable, Is.False);
        Assert.That(missing.Reason, Does.Contain("MOD_CHARACTER"));
    }

    [Test]
    public void 卡组全在本机即可用缺一张即不可用()
    {
        Assert.That(Check(null, "BASH, STRIKE_IRONCLAD", null).Usable, Is.True);

        HellwarPresetAvailabilityResult missing = Check(null, "BASH, MOD_CARD", null);
        Assert.That(missing.Usable, Is.False);
        Assert.That(missing.Reason, Does.Contain("1 张"));
        Assert.That(missing.Reason, Does.Contain("MOD_CARD"));
    }

    [Test]
    public void 卡组为继承或空串都按不替换处理不做卡检查()
    {
        Assert.That(Check("SILENT", null, null).Usable, Is.True, "null = 继承该档内置卡组");
        Assert.That(Check("SILENT", string.Empty, null).Usable, Is.True, "空串 = 不替换（用角色初始卡组）");
        Assert.That(Check("SILENT", "   ", null).Usable, Is.True);
    }

    [Test]
    public void 不替换卡组时仍由角色皮兜住缺失()
    {
        // deck 空串 ⇒ 用该角色的初始卡组；角色本身是 mod 的、玩家没装 ⇒ 必须不可用。
        HellwarPresetAvailabilityResult result = Check("MOD_CHARACTER", string.Empty, null);

        Assert.That(result.Usable, Is.False);
    }

    [Test]
    public void 遗物全在本机即可用缺一件即不可用()
    {
        Assert.That(Check(null, null, null).Usable, Is.True, "null = 继承内置遗物表");
        Assert.That(Check(null, null, Array.Empty<string>()).Usable, Is.True, "空列表 = 一件不加");
        Assert.That(Check(null, null, new[] { "PEN_NIB" }).Usable, Is.True);

        HellwarPresetAvailabilityResult missing = Check(null, null, new[] { "PEN_NIB", "MOD_RELIC" });
        Assert.That(missing.Usable, Is.False);
        Assert.That(missing.Reason, Does.Contain("MOD_RELIC"));
    }

    /// <summary>附魔缺失**不算**不可用（否则第三方 mod 的附魔会让预设大面积消失）。</summary>
    [Test]
    public void 附魔认不出不影响可用性()
    {
        Assert.That(Check(null, "BASH[SHARP], STRIKE_IRONCLAD[ENCHANTMENT.WHATEVER:3]", null).Usable, Is.True);
    }

    /// <summary>退化卡组文本（全是分隔符）**不算**不可用：那是"配置写坏了"，交归一阶段的 WARN 去管。</summary>
    [Test]
    public void 退化卡组文本不因此整条丢弃()
    {
        Assert.That(Check(null, ", ; |", null).Usable, Is.True);
    }

    [Test]
    public void 查询委托抛异常时按不可用处理()
    {
        HellwarPresetAvailabilityResult result = HellwarPresetAvailability.Evaluate(
            "SILENT",
            null,
            null,
            (_) => throw new InvalidOperationException("假装 ModelDb 没就绪"),
            Cards.Contains,
            Relics.Contains);

        Assert.That(result.Usable, Is.False, "宁可不展示，也不要把查不出内容的东西召出来");
    }
}
