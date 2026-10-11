#nullable enable
using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 战灵召唤 · **「档位 × 角色预设」预设表**（预设战神 v2 地基，r269）的纯逻辑用例。
///
/// 钉住的口径（2026-10-11 用户拍板）：
/// <list type="bullet">
/// <item>json 字段缺省（null）= **继承该档内置默认**；deck 显式空串 = **不替换**；relics 空列表 = **一件不加**；</item>
/// <item>件数阶梯**只提示不阻断**（Policy 层不做件数校验，阶梯提示在运行时 <c>HellwarWarGodRelics.GrantTo</c>）；</item>
/// <item>档位名中文别名：**「罕见」= Rare、「稀有」= Epic**（游戏侧命名如此，最容易写反的地方）；</item>
/// <item>表再脏也不抛 —— 问题只进 <see cref="HellwarPresetTable.Problems"/>，消费端永远有内置默认兜底。</item>
/// </list>
/// </summary>
[TestFixture]
public class HellwarPresetDataTests
{
    private static HellwarPresetDefinition NormalizeOne(string json)
    {
        HellwarPresetTable table = HellwarPresetPolicy.Normalize(HellwarPresetJson.Parse(json));
        Assert.That(table.All, Has.Count.EqualTo(1), "本用例假定恰好归一出一条预设");
        return table.All[0];
    }

    private static HellwarPresetDefinition Definition(
        string id = "p1",
        string name = "预设一",
        IReadOnlyList<HellwarWarGodTier>? tiers = null,
        string? skin = "silent",
        string? deckText = "STRIKE_IRONCLAD, BASH+",
        IReadOnlyList<string>? relics = null,
        bool? upgradeDeck = null,
        int? bonusHp = null,
        bool isDefault = true)
    {
        return new HellwarPresetDefinition(
            id,
            name,
            tiers ?? new List<HellwarWarGodTier> { HellwarWarGodTier.Rare },
            skin,
            deckText,
            relics,
            upgradeDeck,
            bonusHp,
            isDefault);
    }

    // ---- 解析层 -----------------------------------------------------------------------------------

    [Test]
    public void 解析合法json并保留全部字段()
    {
        const string json = """
            {
              "version": 1,
              "presets": [
                {
                  "id": "my-silent",
                  "name": "我的猎手",
                  "tiers": ["Rare", "Epic"],
                  "skin": "SILENT",
                  "deck": "NEUTRALIZE, BACKFLIP+, BACKFLIP",
                  "relics": ["RELIC.ORICHALCUM", "  "],
                  "upgradeDeck": false,
                  "bonusHp": 10,
                  "default": true
                }
              ]
            }
            """;

        HellwarPresetFileData? data = HellwarPresetJson.Parse(json);
        Assert.That(data, Is.Not.Null);
        Assert.That(data!.Presets, Has.Count.EqualTo(1));
        HellwarPresetEntryData entry = data.Presets[0];
        Assert.Multiple(() =>
        {
            Assert.That(entry.Id, Is.EqualTo("my-silent"));
            Assert.That(entry.Name, Is.EqualTo("我的猎手"));
            Assert.That(entry.Tiers, Is.EquivalentTo(new[] { "Rare", "Epic" }));
            Assert.That(entry.Skin, Is.EqualTo("SILENT"));
            Assert.That(entry.Deck, Does.Contain("BACKFLIP+"));
            Assert.That(entry.Relics, Is.Not.Null.And.Count.EqualTo(2), "空白条目先保留，归一时才剔除");
            Assert.That(entry.UpgradeDeck, Is.False);
            Assert.That(entry.BonusHp, Is.EqualTo(10));
            Assert.That(entry.Default, Is.True);
        });
    }

    [Test]
    public void 坏json与空json都返回null()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarPresetJson.Parse("{ 不是 json"), Is.Null);
            Assert.That(HellwarPresetJson.Parse("   "), Is.Null);
            Assert.That(HellwarPresetJson.Parse("[]"), Is.Null, "根不是对象 ⇒ 视为损坏");
            Assert.That(HellwarPresetJson.Parse("{}"), Is.Null, "缺 presets ⇒ 视为损坏");
        });
    }

    [Test]
    public void 序列化往返后信息不丢()
    {
        HellwarPresetFileData data = new(1, new List<HellwarPresetEntryData>
        {
            new()
            {
                Id = "p1",
                Name = "测试",
                Tiers = new List<string> { "Ancient" },
                Skin = "summoner",
                Deck = "STRIKE_IRONCLAD+",
                Relics = new List<string> { "ORICHALCUM" },
                UpgradeDeck = true,
                BonusHp = 5,
                Default = true,
            },
        });

        HellwarPresetFileData? parsed = HellwarPresetJson.Parse(HellwarPresetJson.Serialize(data));
        Assert.That(parsed, Is.Not.Null);
        HellwarPresetEntryData entry = parsed!.Presets.Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.Id, Is.EqualTo("p1"));
            Assert.That(entry.Name, Is.EqualTo("测试"));
            Assert.That(entry.Tiers, Is.EquivalentTo(new[] { "Ancient" }));
            Assert.That(entry.Skin, Is.EqualTo("summoner"));
            Assert.That(entry.Deck, Is.EqualTo("STRIKE_IRONCLAD+"));
            Assert.That(entry.Relics, Is.EquivalentTo(new[] { "ORICHALCUM" }));
            Assert.That(entry.UpgradeDeck, Is.True);
            Assert.That(entry.BonusHp, Is.EqualTo(5));
            Assert.That(entry.Default, Is.True);
        });
    }

    // ---- 归一 / 校验层 ----------------------------------------------------------------------------

    [Test]
    public void 空数据归一为空表()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarPresetPolicy.Normalize(null).All, Is.Empty);
            Assert.That(HellwarPresetPolicy.Normalize(new HellwarPresetFileData(1, new List<HellwarPresetEntryData>())).All, Is.Empty);
            Assert.That(HellwarPresetTable.Empty.DefaultForTier(HellwarWarGodTier.Rare), Is.Null);
        });
    }

    [Test]
    public void 缺id与缺名自动补齐()
    {
        HellwarPresetDefinition definition = NormalizeOne(
            """{ "presets": [ { "tiers": ["Rare"], "skin": "silent" } ] }""");
        Assert.Multiple(() =>
        {
            Assert.That(definition.Id, Is.EqualTo("preset-1"));
            Assert.That(definition.DisplayName, Is.EqualTo("preset-1"), "显示名空白 = 用 id");
        });
    }

    [Test]
    public void 中文档位别名映射到正确档位()
    {
        HellwarPresetDefinition definition = NormalizeOne(
            """{ "presets": [ { "id": "p", "tiers": ["普通", "罕见", "稀有", "先古"] } ] }""");
        Assert.That(
            definition.Tiers,
            Is.EquivalentTo(new[]
            {
                HellwarWarGodTier.Common,
                HellwarWarGodTier.Rare,
                HellwarWarGodTier.Epic,
                HellwarWarGodTier.Ancient,
            }),
            "⚠「罕见」=Rare、「稀有」=Epic（游戏侧命名如此）");
    }

    [Test]
    public void 全非法档位整条跳过并记问题()
    {
        HellwarPresetTable table = HellwarPresetPolicy.Normalize(HellwarPresetJson.Parse(
            """{ "presets": [ { "id": "p", "tiers": ["无敌档"] }, { "id": "q", "tiers": ["Rare"] } ] }"""));
        Assert.Multiple(() =>
        {
            Assert.That(table.All.Select((definition) => definition.Id), Is.EquivalentTo(new[] { "q" }));
            Assert.That(table.Problems, Has.Some.Contains("无敌档"));
        });
    }

    [Test]
    public void 缺tiers整条跳过()
    {
        HellwarPresetTable table = HellwarPresetPolicy.Normalize(HellwarPresetJson.Parse(
            """{ "presets": [ { "id": "p" } ] }"""));
        Assert.That(table.All, Is.Empty);
        Assert.That(table.Problems, Is.Not.Empty);
    }

    [Test]
    public void 同档多个default只留第一个()
    {
        HellwarPresetTable table = HellwarPresetPolicy.Normalize(HellwarPresetJson.Parse(
            """
            { "presets": [
                { "id": "a", "tiers": ["Rare"], "default": true },
                { "id": "b", "tiers": ["Rare"], "default": true }
            ] }
            """));
        Assert.Multiple(() =>
        {
            Assert.That(table.DefaultForTier(HellwarWarGodTier.Rare)!.Id, Is.EqualTo("a"));
            Assert.That(table.Problems, Has.Some.Contains("b"));
        });
    }

    [Test]
    public void 无显式default时取该档第一条()
    {
        HellwarPresetTable table = HellwarPresetPolicy.Normalize(HellwarPresetJson.Parse(
            """
            { "presets": [
                { "id": "x", "tiers": ["Epic"] },
                { "id": "y", "tiers": ["Epic"] }
            ] }
            """));
        Assert.Multiple(() =>
        {
            Assert.That(table.DefaultForTier(HellwarWarGodTier.Epic)!.Id, Is.EqualTo("x"));
            Assert.That(table.ForTier(HellwarWarGodTier.Epic), Has.Count.EqualTo(2), "两条都还在，只是默认取第一条");
        });
    }

    [Test]
    public void 负数血量归零并记问题()
    {
        HellwarPresetDefinition definition = NormalizeOne(
            """{ "presets": [ { "id": "p", "tiers": ["Rare"], "bonusHp": -7 } ] }""");
        Assert.Multiple(() =>
        {
            Assert.That(definition.BonusHp, Is.EqualTo(0));
            Assert.That(definition.UpgradeDeck, Is.Null);
        });
    }

    [Test]
    public void 遗物去RELIC前缀并剔除空白()
    {
        HellwarPresetDefinition definition = NormalizeOne(
            """{ "presets": [ { "id": "p", "tiers": ["Rare"], "relics": ["RELIC.ORICHALCUM", "  "] } ] }""");
        Assert.That(definition.RelicEntries, Is.EquivalentTo(new[] { "ORICHALCUM" }));
    }

    // ---- 继承映射（definition → 运行时预设） -------------------------------------------------------

    [Test]
    public void 缺省字段继承该档内置默认()
    {
        HellwarPresetDefinition definition = Definition(
            skin: null,
            deckText: null,
            relics: null,
            upgradeDeck: null,
            bonusHp: null);

        HellwarWarGodPreset runtime = HellwarPresetRuntimeMapping.ToRuntimePreset(
            definition, HellwarWarGodTier.Rare, HellwarWarGodPresets.Resolve(HellwarWarGodTier.Rare));

        Assert.Multiple(() =>
        {
            Assert.That(runtime.SkinKey, Is.EqualTo(HellwarWarGodPresets.SkinSilent), "罕见档内置 = 静默猎手皮");
            Assert.That(runtime.UpgradeDeck, Is.True, "罕见档内置 = 全升级");
            Assert.That(runtime.BonusHp, Is.EqualTo(0));
            Assert.That(runtime.DeckText, Is.Null, "null = 走旧链（hellwarDeck* → 内置默认）");
            Assert.That(runtime.RelicEntries, Is.Null, "null = 内置遗物表");
            Assert.That(runtime.PresetId, Is.EqualTo("p1"));
            Assert.That(runtime.IsBuiltin, Is.False);
        });
    }

    [Test]
    public void 显式字段直通且空串与null语义不同()
    {
        HellwarWarGodPreset replaced = HellwarPresetRuntimeMapping.ToRuntimePreset(
            Definition(deckText: "BASH, BASH", relics: new List<string> { "ORICHALCUM" }, upgradeDeck: false, bonusHp: 30),
            HellwarWarGodTier.Rare,
            HellwarWarGodPresets.Resolve(HellwarWarGodTier.Rare));
        Assert.Multiple(() =>
        {
            Assert.That(replaced.DeckText, Is.EqualTo("BASH, BASH"), "显式卡组直通");
            Assert.That(replaced.RelicEntries, Is.EquivalentTo(new[] { "ORICHALCUM" }), "显式遗物完全接管");
            Assert.That(replaced.UpgradeDeck, Is.False);
            Assert.That(replaced.BonusHp, Is.EqualTo(30));
        });

        HellwarWarGodPreset noReplace = HellwarPresetRuntimeMapping.ToRuntimePreset(
            Definition(deckText: ""),
            HellwarWarGodTier.Rare,
            HellwarWarGodPresets.Resolve(HellwarWarGodTier.Rare));
        Assert.Multiple(() =>
        {
            Assert.That(noReplace.DeckText, Is.Empty, "显式空串 = 不替换（DeckPlan.Parse(\"\" ) = null ⇒ 保留角色初始卡组）");
            Assert.That(noReplace.DeckText, Is.Not.Null, "与「继承」（null）语义必须可区分");
        });

        HellwarWarGodPreset noRelics = HellwarPresetRuntimeMapping.ToRuntimePreset(
            Definition(relics: new List<string>()),
            HellwarWarGodTier.Rare,
            HellwarWarGodPresets.Resolve(HellwarWarGodTier.Rare));
        Assert.That(noRelics.RelicEntries, Is.Not.Null.And.Empty, "空列表 = 一件不加（与「继承内置」可区分）");
    }

    [Test]
    public void 内置默认仍是无附加内容的旧record()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarWarGodPresets.All, Has.Length.EqualTo(4), "内置四档一个不少");
            foreach (HellwarWarGodPreset preset in HellwarWarGodPresets.All)
            {
                Assert.That(preset.IsBuiltin, Is.True, "内置档不携带预设 id");
                Assert.That(preset.DeckText, Is.Null, "内置档走旧链");
                Assert.That(preset.RelicEntries, Is.Null, "内置档走内置遗物表");
            }

            Assert.That(HellwarWarGodPresets.Describe(HellwarWarGodPresets.Rare), Does.Contain("内置默认"));
        });
    }
}
