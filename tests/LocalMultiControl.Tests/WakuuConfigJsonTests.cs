using System.Text.Json;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 瓦库托管配置 JSON 解析/序列化纯函数测试。
/// 覆盖默认值、字段覆盖、大小写不敏感、注释/尾逗号容忍、往返与非法输入。
/// </summary>
[TestFixture]
public class WakuuConfigJsonTests
{
    [Test]
    public void 解析空对象_返回全默认值()
    {
        WakuuConfigData data = WakuuConfigJson.Parse("{}")!;

        Assert.That(data, Is.Not.Null);
        Assert.Multiple(() =>
        {
            // 默认关
            Assert.That(data.useVakuuForm, Is.False);
            Assert.That(data.autoUsePotions, Is.False);
            Assert.That(data.neowAutoChoose, Is.False);
            // 默认开
            Assert.That(data.playAllCards, Is.True);
            Assert.That(data.backgroundMode, Is.True);
            Assert.That(data.suppressVanillaEarring, Is.True);
            Assert.That(data.autoClaimCards, Is.True);
            Assert.That(data.autoClaimGoldRelics, Is.True);
            Assert.That(data.autoClaimPotions, Is.True);
            Assert.That(data.autoChooseEvents, Is.True);
            Assert.That(data.autoRestChoice, Is.True);
            // 策略默认值
            Assert.That(data.eventChoiceMode, Is.EqualTo("first"));
            Assert.That(data.cardPickMode, Is.EqualTo("last"));
            // 大脑默认值
            Assert.That(data.wakuuBrain, Is.EqualTo("heuristic"));
        });
    }

    [Test]
    public void 解析大脑开关_auto生效()
    {
        WakuuConfigData data = WakuuConfigJson.Parse("""{ "wakuuBrain": "auto" }""")!;
        Assert.Multiple(() =>
        {
            Assert.That(data.wakuuBrain, Is.EqualTo("auto"));
            Assert.That(data.cardPickMode, Is.EqualTo("last")); // 未提供 → 默认
        });
    }

    [Test]
    public void 解析cardPickMode_rare生效()
    {
        WakuuConfigData data = WakuuConfigJson.Parse("""{ "cardPickMode": "rare" }""")!;
        Assert.Multiple(() =>
        {
            Assert.That(data.cardPickMode, Is.EqualTo("rare"));
            Assert.That(data.eventChoiceMode, Is.EqualTo("first")); // 未提供 → 默认
        });
    }

    [Test]
    public void 解析部分字段_未提供的字段保持默认值()
    {
        WakuuConfigData data = WakuuConfigJson.Parse("""{ "useVakuuForm": true, "autoUsePotions": true }""")!;

        Assert.Multiple(() =>
        {
            Assert.That(data.useVakuuForm, Is.True);
            Assert.That(data.autoUsePotions, Is.True);
            Assert.That(data.playAllCards, Is.True); // 未提供 → 默认
            Assert.That(data.cardPickMode, Is.EqualTo("last")); // 未提供 → 默认
            Assert.That(data.eventChoiceMode, Is.EqualTo("first"));
            Assert.That(data.neowAutoChoose, Is.False);
        });
    }

    [Test]
    public void 解析全量字段_逐一生效()
    {
        const string json = """
        {
            "useVakuuForm": true,
            "playAllCards": false,
            "backgroundMode": false,
            "suppressVanillaEarring": false,
            "autoClaimCards": false,
            "autoClaimGoldRelics": false,
            "autoClaimPotions": false,
            "autoChooseEvents": false,
            "autoRestChoice": false,
            "autoUsePotions": true,
            "neowAutoChoose": true,
            "eventChoiceMode": "random",
            "cardPickMode": "first"
        }
        """;

        WakuuConfigData data = WakuuConfigJson.Parse(json)!;

        Assert.Multiple(() =>
        {
            Assert.That(data.useVakuuForm, Is.True);
            Assert.That(data.playAllCards, Is.False);
            Assert.That(data.backgroundMode, Is.False);
            Assert.That(data.suppressVanillaEarring, Is.False);
            Assert.That(data.autoClaimCards, Is.False);
            Assert.That(data.autoClaimGoldRelics, Is.False);
            Assert.That(data.autoClaimPotions, Is.False);
            Assert.That(data.autoChooseEvents, Is.False);
            Assert.That(data.autoRestChoice, Is.False);
            Assert.That(data.autoUsePotions, Is.True);
            Assert.That(data.neowAutoChoose, Is.True);
            Assert.That(data.eventChoiceMode, Is.EqualTo("random"));
            Assert.That(data.cardPickMode, Is.EqualTo("first"));
        });
    }

    [Test]
    public void 解析大写字段名_大小写不敏感()
    {
        WakuuConfigData data = WakuuConfigJson.Parse("""{ "UseVakuuForm": true, "CardPickMode": "random" }""")!;

        Assert.Multiple(() =>
        {
            Assert.That(data.useVakuuForm, Is.True);
            Assert.That(data.cardPickMode, Is.EqualTo("random"));
        });
    }

    [Test]
    public void 解析注释与尾逗号_被容忍()
    {
        const string json = """
        {
            // 注释行
            "useVakuuForm": true, /* 块注释 */
            "playAllCards": false,
        }
        """;

        WakuuConfigData data = WakuuConfigJson.Parse(json)!;

        Assert.Multiple(() =>
        {
            Assert.That(data.useVakuuForm, Is.True);
            Assert.That(data.playAllCards, Is.False);
        });
    }

    [Test]
    public void 解析未知字段_忽略不报错()
    {
        WakuuConfigData data = WakuuConfigJson.Parse("""{ "unknownField": 123, "useVakuuForm": true }""")!;
        Assert.That(data.useVakuuForm, Is.True);
    }

    [Test]
    public void 解析null字面量_返回null()
    {
        Assert.That(WakuuConfigJson.Parse("null"), Is.Null);
    }

    [Test]
    public void 解析非法JSON_抛JsonException()
    {
        Assert.That(() => WakuuConfigJson.Parse("{ useVakuuForm: true }"), Throws.TypeOf<JsonException>());
        Assert.That(() => WakuuConfigJson.Parse("not json at all"), Throws.TypeOf<JsonException>());
    }

    [Test]
    public void 序列化后重新解析_字段往返一致()
    {
        WakuuConfigData original = new()
        {
            useVakuuForm = true,
            playAllCards = false,
            autoUsePotions = true,
            eventChoiceMode = "random",
            cardPickMode = "first",
        };

        string json = WakuuConfigJson.Serialize(original);
        WakuuConfigData roundTrip = WakuuConfigJson.Parse(json)!;

        Assert.Multiple(() =>
        {
            Assert.That(roundTrip.useVakuuForm, Is.EqualTo(original.useVakuuForm));
            Assert.That(roundTrip.playAllCards, Is.EqualTo(original.playAllCards));
            Assert.That(roundTrip.backgroundMode, Is.EqualTo(original.backgroundMode)); // 默认值也保留
            Assert.That(roundTrip.autoUsePotions, Is.EqualTo(original.autoUsePotions));
            Assert.That(roundTrip.eventChoiceMode, Is.EqualTo(original.eventChoiceMode));
            Assert.That(roundTrip.cardPickMode, Is.EqualTo(original.cardPickMode));
        });
    }

    [Test]
    public void 序列化_字段名保持camelCase()
    {
        WakuuConfigData data = new();
        string json = WakuuConfigJson.Serialize(data);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"useVakuuForm\""));
            Assert.That(json, Does.Contain("\"playAllCards\""));
            Assert.That(json, Does.Contain("\"suppressVanillaEarring\""));
            Assert.That(json, Does.Contain("\"autoClaimPotions\""));
            Assert.That(json, Does.Contain("\"eventChoiceMode\""));
            Assert.That(json, Does.Contain("\"cardPickMode\""));
            Assert.That(json, Does.Contain("\"wakuuBrain\""));
        });
    }
}
