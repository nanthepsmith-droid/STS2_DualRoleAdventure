#nullable enable
using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 战灵召唤 · 预设战神 v2 **Phase B**（2026-10-11）第二层「选角色预设」与随机池的纯逻辑用例。
///
/// 钉住的口径：
/// <list type="bullet">
/// <item>行结构 = [自动] →（具体候选 &gt; 1 时）[随机] → 各具体预设（**保持候选顺序**）；</item>
/// <item>只有一条具体候选 ⇒ <b>不弹第二层</b>（<see cref="HellwarPresetChoices.ShouldOfferPicker"/>）；</item>
/// <item>随机池 = 内置档位默认 ∪ 文件预设，**去重**（同 id 只留一份）；内置（id 空）按"档位 + 皮肤"区分；</item>
/// <item><see cref="HellwarPresetChoices.PickRandom"/> 对任意整数取模（含负数）、空池返回 null。</item>
/// </list>
/// </summary>
[TestFixture]
public class HellwarPresetChoicesTests
{
    private static HellwarWarGodPreset Preset(
        HellwarWarGodTier tier = HellwarWarGodTier.Rare,
        string skin = "silent",
        string presetId = "",
        string displayName = "")
    {
        return new HellwarWarGodPreset(tier, skin, UpgradeDeck: false, BonusHp: 0, presetId, displayName);
    }

    private static HellwarWarGodPreset FilePreset(
        HellwarWarGodTier tier, string id, string skin = "ironclad")
    {
        return new HellwarWarGodPreset(tier, skin, UpgradeDeck: true, BonusHp: 0, id, id, "BASH, DEFEND_IRONCLAD");
    }

    // ---- 第二层弹层的行结构 ------------------------------------------------------------------------

    [Test]
    public void 只有一条具体候选时不插随机行()
    {
        HellwarWarGodPreset def = Preset(presetId: "p1", displayName: "默认");
        IReadOnlyList<HellwarPresetPickOption> options =
            HellwarPresetChoices.BuildPickOptions(def, new[] { def });

        Assert.That(options, Has.Count.EqualTo(2));
        Assert.That(options[0].Kind, Is.EqualTo(HellwarPresetPickKind.Default));
        Assert.That(options[1].Kind, Is.EqualTo(HellwarPresetPickKind.Concrete));
        Assert.That(options.Any((option) => option.Kind == HellwarPresetPickKind.Random), Is.False);
    }

    [Test]
    public void 多条候选时插入随机行且保持候选顺序()
    {
        HellwarWarGodPreset def = Preset(presetId: "p0", displayName: "默认");
        HellwarWarGodPreset a = FilePreset(HellwarWarGodTier.Rare, "a");
        HellwarWarGodPreset b = FilePreset(HellwarWarGodTier.Rare, "b");

        IReadOnlyList<HellwarPresetPickOption> options =
            HellwarPresetChoices.BuildPickOptions(def, new[] { a, b });

        Assert.That(
            options.Select((option) => option.Kind),
            Is.EqualTo(new[]
            {
                HellwarPresetPickKind.Default,
                HellwarPresetPickKind.Random,
                HellwarPresetPickKind.Concrete,
                HellwarPresetPickKind.Concrete,
            }));
        Assert.That(options[2].Preset.PresetId, Is.EqualTo("a"));
        Assert.That(options[3].Preset.PresetId, Is.EqualTo("b"));
        // 「自动 / 随机」两行携带的都是该档默认预设（供 UI 显示与回落）。
        Assert.That(options[0].Preset.PresetId, Is.EqualTo("p0"));
        Assert.That(options[1].Preset.PresetId, Is.EqualTo("p0"));
    }

    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(2, true)]
    [TestCase(5, true)]
    public void 值不值得弹第二层取决于具体候选条数(int count, bool expected)
    {
        List<HellwarWarGodPreset> concrete = Enumerable.Range(0, count)
            .Select((index) => FilePreset(HellwarWarGodTier.Common, $"p{index}"))
            .ToList();

        Assert.That(HellwarPresetChoices.ShouldOfferPicker(concrete), Is.EqualTo(expected));
    }

    // ---- 随机池（【无尽战神】/【九连环】）----------------------------------------------------------

    [Test]
    public void 随机池并上文件预设且保持内置在前()
    {
        List<HellwarWarGodPreset> builtin = new()
        {
            Preset(HellwarWarGodTier.Common, "summoner"),
            Preset(HellwarWarGodTier.Rare, "silent"),
            Preset(HellwarWarGodTier.Epic, "ironclad"),
        };
        List<HellwarWarGodPreset> file = new()
        {
            FilePreset(HellwarWarGodTier.Rare, "my-rare"),
        };

        IReadOnlyList<HellwarWarGodPreset> pool = HellwarPresetChoices.BuildRandomPool(builtin, file);

        Assert.That(pool, Has.Count.EqualTo(4));
        Assert.That(pool[0].SkinKey, Is.EqualTo("summoner"));
        Assert.That(pool[3].PresetId, Is.EqualTo("my-rare"));
    }

    [Test]
    public void 随机池按id去重()
    {
        List<HellwarWarGodPreset> builtin = new() { FilePreset(HellwarWarGodTier.Rare, "dup") };
        List<HellwarWarGodPreset> file = new() { FilePreset(HellwarWarGodTier.Rare, "dup") };

        IReadOnlyList<HellwarWarGodPreset> pool = HellwarPresetChoices.BuildRandomPool(builtin, file);

        Assert.That(pool, Has.Count.EqualTo(1));
    }

    [Test]
    public void 内置预设按档位与皮肤区分不算重复()
    {
        List<HellwarWarGodPreset> builtin = new()
        {
            Preset(HellwarWarGodTier.Common, "summoner"),
            Preset(HellwarWarGodTier.Rare, "silent"),
        };

        IReadOnlyList<HellwarWarGodPreset> pool =
            HellwarPresetChoices.BuildRandomPool(builtin, System.Array.Empty<HellwarWarGodPreset>());

        Assert.That(pool, Has.Count.EqualTo(2));
    }

    // ---- 随机取 ------------------------------------------------------------------------------------

    [Test]
    public void 随机取按取模且负数也能兜()
    {
        List<HellwarWarGodPreset> pool = new()
        {
            FilePreset(HellwarWarGodTier.Rare, "a"),
            FilePreset(HellwarWarGodTier.Rare, "b"),
            FilePreset(HellwarWarGodTier.Rare, "c"),
        };

        Assert.That(HellwarPresetChoices.PickRandom(pool, 0)!.PresetId, Is.EqualTo("a"));
        Assert.That(HellwarPresetChoices.PickRandom(pool, 4)!.PresetId, Is.EqualTo("b"));
        Assert.That(HellwarPresetChoices.PickRandom(pool, -1)!.PresetId, Is.EqualTo("c"));
    }

    [Test]
    public void 空池随机取返回null()
    {
        Assert.That(
            HellwarPresetChoices.PickRandom(System.Array.Empty<HellwarWarGodPreset>(), 0),
            Is.Null);
    }
}
