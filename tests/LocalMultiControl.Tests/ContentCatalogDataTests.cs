#nullable enable
using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 预设构筑器 · **Phase B-3**（2026-10-12）导出内容目录的纯逻辑用例。
///
/// <para>钉住的口径：</para>
/// <list type="bullet">
/// <item><b>来源分类</b>：程序集名 == 游戏本体（大小写不敏感）⇒ <c>base</c>；其余 ⇒ 该程序集名（mod）；
///   拿不到 ⇒ <c>unknown</c>（<see cref="IsFromMod"/> 把 unknown 也算"非本体"）；</item>
/// <item><b>条数汇总</b>：以**列表长度 + 逐条 Source** 为准（同进同退，别另算一遍枚举数）；</item>
/// <item><b>图片路径推定</b>：卡图整图 / 遗物大图按游戏内部的同款规则拼，且**池名 / entry 一律小写**；</item>
/// <item><b>JSON 往返</b>：序列化 ⇒ 解析 ⇒ 再序列化**逐字节相同**；版本非法兜到 1；坏文本 / 空文本 ⇒ null。</item>
/// </list>
/// </summary>
[TestFixture]
public class ContentCatalogDataTests
{
    // ---- 来源分类 -----------------------------------------------------------------------------------

    [Test]
    public void 程序集名与本体一致时归为本体()
    {
        Assert.That(ContentCatalogPolicy.ResolveSource("sts2", "sts2"),
            Is.EqualTo(ContentCatalogPolicy.BaseSourceKey));
    }

    [Test]
    public void 程序集名比对本体大小写不敏感()
    {
        Assert.That(ContentCatalogPolicy.ResolveSource("STS2", "sts2"),
            Is.EqualTo(ContentCatalogPolicy.BaseSourceKey));
        Assert.That(ContentCatalogPolicy.ResolveSource("sts2", "STS2"),
            Is.EqualTo(ContentCatalogPolicy.BaseSourceKey));
    }

    [Test]
    public void 第三方程序集名原样保留()
    {
        Assert.That(ContentCatalogPolicy.ResolveSource("YuWanCard", "sts2"), Is.EqualTo("YuWanCard"));
        Assert.That(ContentCatalogPolicy.ResolveSource(" DualRoleAdventure ", "sts2"),
            Is.EqualTo("DualRoleAdventure"));
    }

    [Test]
    public void 拿不到程序集名时归为未知()
    {
        Assert.That(ContentCatalogPolicy.ResolveSource(null, "sts2"),
            Is.EqualTo(ContentCatalogPolicy.UnknownSourceKey));
        Assert.That(ContentCatalogPolicy.ResolveSource("   ", "sts2"),
            Is.EqualTo(ContentCatalogPolicy.UnknownSourceKey));
    }

    [Test]
    public void 非本体来源都算来自mod()
    {
        // ⚠ 判据是**换算之后的 source 字段值**（本体已被 ResolveSource 折成 "base"），
        //   不是原始程序集名 —— 所以这里先过一遍 ResolveSource 再看。
        string baseSource = ContentCatalogPolicy.ResolveSource("sts2", "sts2");
        Assert.That(ContentCatalogPolicy.IsFromMod(baseSource), Is.False);
        Assert.That(ContentCatalogPolicy.IsFromMod(ContentCatalogPolicy.BaseSourceKey), Is.False);
        Assert.That(ContentCatalogPolicy.IsFromMod("YuWanCard"), Is.True);
        Assert.That(ContentCatalogPolicy.IsFromMod(ContentCatalogPolicy.UnknownSourceKey), Is.True);
        Assert.That(ContentCatalogPolicy.IsFromMod(null), Is.False);
    }

    // ---- 条数汇总 -----------------------------------------------------------------------------------

    [Test]
    public void 条数汇总以列表长度与逐条来源为准()
    {
        ContentCatalogDocument document = new()
        {
            Cards = new List<ContentCatalogCard>
            {
                new() { Id = "CARD.BASH", Source = ContentCatalogPolicy.BaseSourceKey },
                new() { Id = "CARD.YUI_ONE", Source = "YuWanCard" },
            },
            Relics = new List<ContentCatalogRelic>
            {
                new() { Id = "RELIC.BURNING_BLOOD", Source = ContentCatalogPolicy.BaseSourceKey },
                new() { Id = "RELIC.PEN_NIB", Source = ContentCatalogPolicy.BaseSourceKey },
                new() { Id = "RELIC.HEXTECH_ONE", Source = "HextechRunes" },
            },
            Characters = new List<ContentCatalogCharacter>
            {
                new() { Entry = "IRONCLAD", Source = ContentCatalogPolicy.BaseSourceKey },
                new() { Entry = "KOISHI-KOISHI", Source = "EcoLib" },
            },
            Enchantments = new List<ContentCatalogEnchantment>
            {
                new() { Entry = "SHARP", Source = ContentCatalogPolicy.BaseSourceKey },
                new() { Entry = "MOD_ENCHANT", Source = "SomeMod" },
            },
        };

        ContentCatalogCounts counts = ContentCatalogPolicy.Count(document);

        Assert.That(counts.Cards, Is.EqualTo(2));
        Assert.That(counts.Relics, Is.EqualTo(3));
        Assert.That(counts.Characters, Is.EqualTo(2));
        Assert.That(counts.Enchantments, Is.EqualTo(2));
        Assert.That(counts.ModCards, Is.EqualTo(1));
        Assert.That(counts.ModRelics, Is.EqualTo(1));
        Assert.That(counts.ModCharacters, Is.EqualTo(1));
        Assert.That(counts.ModEnchantments, Is.EqualTo(1));
    }

    [Test]
    public void 条数摘要把总数与mod数都写出来()
    {
        ContentCatalogCounts counts = new()
        {
            Cards = 596,
            ModCards = 12,
            Relics = 299,
            ModRelics = 3,
            Characters = 23,
            ModCharacters = 18,
            Enchantments = 22,
            ModEnchantments = 4,
        };

        string text = ContentCatalogPolicy.DescribeCounts(counts);

        Assert.That(
            text,
            Is.EqualTo("卡=596(其中 mod 12) / 遗物=299(其中 mod 3) / 角色=23(其中 mod 18) / 附魔=22(其中 mod 4)"));
    }

    // ---- 图片路径（r275 首版导出踩过：卡池 entry ≠ 图集目录名）-----------------------------------------

    [Test]
    public void 资源路径规范化会把反斜杠换成斜杠()
    {
        // 实机里 mod 的立绘就是这种形态：res://AncientsAwakened\images\card_portraits\x.png
        Assert.That(ContentCatalogPolicy.NormalizeResourcePath("res://AncientsAwakened\\images\\x.png"),
            Is.EqualTo("res://AncientsAwakened/images/x.png"));
        Assert.That(ContentCatalogPolicy.NormalizeResourcePath("  res://a/b.tres  "),
            Is.EqualTo("res://a/b.tres"));
        Assert.That(ContentCatalogPolicy.NormalizeResourcePath(null), Is.Empty);
    }

    [Test]
    public void 能直读的整图路径按扩展名判定()
    {
        Assert.That(ContentCatalogPolicy.IsDirectImagePath("res://m/images/cards/a.png"), Is.True);
        Assert.That(ContentCatalogPolicy.IsDirectImagePath("res://m/images/cards/a.JPG"), Is.True);
        Assert.That(ContentCatalogPolicy.IsDirectImagePath("res://images/atlases/card_atlas.sprites/x/a.tres"), Is.False);
        Assert.That(ContentCatalogPolicy.IsDirectImagePath(""), Is.False);
    }

    [Test]
    public void 卡图整图路径从游戏自己给的图集路径换算()
    {
        // ⚠ 不能用卡池 entry 硬拼：Id.Entry = SILENT_CARD_POOL，而图集目录是 CardPoolModel.Title 的小写 = silent。
        Assert.That(
            ContentCatalogPolicy.CardPortraitPngHint("res://images/atlases/card_atlas.sprites/silent/abrasive.tres"),
            Is.EqualTo("res://images/packed/card_portraits/silent/abrasive.png"));
        // beta 子目录一并保持
        Assert.That(
            ContentCatalogPolicy.CardPortraitPngHint("res://images/atlases/card_atlas.sprites/event/beta/abundance.tres"),
            Is.EqualTo("res://images/packed/card_portraits/event/beta/abundance.png"));
    }

    [Test]
    public void 非图集路径不给整图猜想()
    {
        Assert.That(ContentCatalogPolicy.CardPortraitPngHint("res://AncientsAwakened/images/card_portraits/x.png"),
            Is.Empty);
        Assert.That(ContentCatalogPolicy.CardPortraitPngHint("res://someMod/custom/a.tres"), Is.Empty);
        Assert.That(ContentCatalogPolicy.CardPortraitPngHint(""), Is.Empty);
    }

    [Test]
    public void 遗物大图路径按entry小写拼()
    {
        Assert.That(ContentCatalogPolicy.RelicBigIconHint("BURNING_BLOOD"),
            Is.EqualTo("res://images/relics/burning_blood.png"));
    }

    // ---- JSON 往返 ----------------------------------------------------------------------------------

    private static ContentCatalogDocument SampleDocument()
    {
        return new ContentCatalogDocument
        {
            GameVersion = "0.111.0",
            ExportedAtUtc = "2026-10-12T00:00:00Z",
            Counts = new ContentCatalogCounts { Cards = 1, Relics = 1, Characters = 1, ModCards = 0, ModRelics = 0, ModCharacters = 0 },
            Cards = new List<ContentCatalogCard>
            {
                new()
                {
                    Id = "CARD.BASH",
                    Entry = "BASH",
                    Name = "重击",
                    Description = "造成 8 点伤害。",
                    Rarity = "Basic",
                    Type = "Attack",
                    Cost = 2,
                    CostsX = false,
                    StarCost = -1,
                    Pool = "IRONCLAD",
                    Portrait = "res://images/atlases/card_atlas.sprites/ironclad/bash.tres",
                    PortraitPng = "res://images/packed/card_portraits/ironclad/bash.png",
                    HasPortrait = false,
                    Source = ContentCatalogPolicy.BaseSourceKey,
                },
            },
            Relics = new List<ContentCatalogRelic>
            {
                new()
                {
                    Id = "RELIC.BURNING_BLOOD",
                    Entry = "BURNING_BLOOD",
                    Name = "燃烧之血",
                    Description = "战斗结束后回复 6 点生命。",
                    Rarity = "Starter",
                    Pool = "IRONCLAD",
                    Icon = "res://images/atlases/relic_atlas.sprites/burning_blood.tres",
                    BigIcon = "res://images/relics/burning_blood.png",
                    HasIcon = false,
                    HasBigIcon = false,
                    Source = ContentCatalogPolicy.BaseSourceKey,
                },
            },
            Characters = new List<ContentCatalogCharacter>
            {
                new()
                {
                    Id = "CHARACTER.IRONCLAD",
                    Entry = "IRONCLAD",
                    Name = "铁甲战士",
                    IsPlayable = true,
                    StartingDeck = new List<string> { "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "BASH" },
                    StartingRelics = new List<string> { "BURNING_BLOOD" },
                    Source = ContentCatalogPolicy.BaseSourceKey,
                },
            },
            Enchantments = new List<ContentCatalogEnchantment>
            {
                new()
                {
                    Id = "ENCHANTMENT.SHARP",
                    Entry = "SHARP",
                    Name = "锋利",
                    Description = "这张牌多造成 3 点伤害。",
                    Icon = "res://images/enchantments/sharp.png",
                    HasIcon = true,
                    ShowAmount = true,
                    AllowedCardTypes = new List<string> { "Attack" },
                    Source = ContentCatalogPolicy.BaseSourceKey,
                },
            },
        };
    }

    [Test]
    public void 序列化后可原样解析回来()
    {
        string json = ContentCatalogJson.Serialize(SampleDocument());
        ContentCatalogDocument? parsed = ContentCatalogJson.Parse(json);

        Assert.That(parsed, Is.Not.Null);
        Assert.That(ContentCatalogJson.Serialize(parsed!), Is.EqualTo(json), "往返后再序列化应逐字节相同");
        Assert.That(parsed!.Cards[0].Entry, Is.EqualTo("BASH"));
        Assert.That(parsed.Cards[0].Name, Is.EqualTo("重击"));
        Assert.That(parsed.Cards[0].Cost, Is.EqualTo(2));
        Assert.That(parsed.Characters[0].StartingDeck, Is.EqualTo(new[] { "STRIKE_IRONCLAD", "STRIKE_IRONCLAD", "BASH" }));
        Assert.That(parsed.Characters[0].StartingRelics, Is.EqualTo(new[] { "BURNING_BLOOD" }));
        Assert.That(parsed.Relics[0].BigIcon, Is.EqualTo("res://images/relics/burning_blood.png"));
    }

    [Test]
    public void 文件名与版本常量稳定()
    {
        Assert.That(ContentCatalogJson.FileName, Is.EqualTo("content_catalog.json"));
        Assert.That(new ContentCatalogDocument().Version, Is.EqualTo(ContentCatalogJson.CurrentVersion));
    }

    [Test]
    public void 版本非法时序列化兜到当前版本()
    {
        ContentCatalogDocument document = SampleDocument();
        document.Version = 0;

        ContentCatalogDocument? parsed = ContentCatalogJson.Parse(ContentCatalogJson.Serialize(document));

        Assert.That(parsed!.Version, Is.EqualTo(ContentCatalogJson.CurrentVersion));
    }

    [Test]
    public void 空文本与坏文本解析返回空()
    {
        Assert.That(ContentCatalogJson.Parse(""), Is.Null);
        Assert.That(ContentCatalogJson.Parse("   "), Is.Null);
        Assert.That(ContentCatalogJson.Parse("{ 这不是 json"), Is.Null);
        Assert.That(ContentCatalogJson.Parse("[1,2,3]"), Is.Null, "数组不是对象 ⇒ 结构坏");
    }

    [Test]
    public void 缺列表字段时解析出空表而不是空引用()
    {
        ContentCatalogDocument? parsed = ContentCatalogJson.Parse("{\"Version\":1}");

        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.Cards, Is.Empty);
        Assert.That(parsed.Relics, Is.Empty);
        Assert.That(parsed.Characters, Is.Empty);
        Assert.That(parsed.Enchantments, Is.Empty);
        Assert.That(parsed.Counts, Is.Not.Null);
    }
}
