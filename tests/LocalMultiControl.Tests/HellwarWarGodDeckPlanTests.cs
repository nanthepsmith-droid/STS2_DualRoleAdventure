using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 战灵召唤 · **手工预设卡组**的配置文本解析层用例（2026-10-10 用户拍板：
/// "手工 id 列表 + 开放到配置文件让玩家自定义"）。
///
/// 这一层的价值：把"什么算一条 / 什么算没配置 / 升级标记怎么写 / 拼错了会发生什么"这些**写进 json 的契约**
/// 钉成断言 —— 以后改格式只该改实现，不该悄悄改语义（玩家盘上的配置会跟着变味）。
/// </summary>
[TestFixture]
public class HellwarWarGodDeckPlanTests
{
    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\r\n\t ")]
    [TestCase(",,, ;; ||")]
    public void 空或全是分隔符等于没配置(string? text)
    {
        Assert.That(
            HellwarWarGodDeckPlan.Parse(text),
            Is.Null,
            "返回 null = 「没配置」⇒ 调用方用内置默认卡组（**不是**空卡组）");
    }

    [Test]
    public void 逗号分号竖线空白都能当分隔符()
    {
        IReadOnlyList<HellwarWarGodDeckEntry>? entries =
            HellwarWarGodDeckPlan.Parse("STRIKE_IRONCLAD, DEFEND_IRONCLAD; BASH\nIRON_WAVE|TWIN_STRIKE");

        Assert.That(entries, Is.Not.Null);
        Assert.That(
            entries!.Select((entry) => entry.Entry),
            Is.EqualTo(new[] { "STRIKE_IRONCLAD", "DEFEND_IRONCLAD", "BASH", "IRON_WAVE", "TWIN_STRIKE" }));
        Assert.That(entries!.Select((entry) => entry.Upgraded), Is.All.False);
    }

    [Test]
    public void 加号与entry之间有空格时不会被当成升级()
    {
        // 契约：分隔符含空白 ⇒ `BASH +` 的 `+` 是独立 token（空 entry）被丢弃，`BASH` 保持"不升级"。
        IReadOnlyList<HellwarWarGodDeckEntry>? entries = HellwarWarGodDeckPlan.Parse("BASH +");

        Assert.That(entries, Is.Not.Null);
        Assert.That(entries!.Count, Is.EqualTo(1));
        Assert.That(entries[0], Is.EqualTo(new HellwarWarGodDeckEntry("BASH", Upgraded: false)));
    }

    [Test]
    public void 末尾加号等于这张升级且可叠写()
    {
        // ⚠ `+` 必须**贴着 entry**写：分隔符含空白，`BACKFLIP +` 会被切成两个 token（后一个是空的 `+` ⇒ 被丢弃）。
        IReadOnlyList<HellwarWarGodDeckEntry>? entries =
            HellwarWarGodDeckPlan.Parse("BACKFLIP+, DASH, POISONED_STAB++");

        Assert.That(entries, Is.Not.Null);
        Assert.That(
            entries!.Select((entry) => (entry.Entry, entry.Upgraded)),
            Is.EqualTo(new[]
            {
                ("BACKFLIP", true),
                ("DASH", false),
                ("POISONED_STAB", true),
            }));
    }

    [Test]
    public void 前缀CARD可省略且大小写不敏感且统一成大写()
    {
        // ⚠ 解析只把 entry **统一成大写**，不会替你补下划线：写 `DemonForm` 得到 `DEMONFORM`（查不到卡）。
        // 正确写法就是游戏里那份 `ModelId.Entry`（`DEMON_FORM`）。
        IReadOnlyList<HellwarWarGodDeckEntry>? entries =
            HellwarWarGodDeckPlan.Parse("card.bash, CARD.DEMON_FORM, Card.POMMEL_STRIKE+");

        Assert.That(entries, Is.Not.Null);
        Assert.That(
            entries!.Select((entry) => entry.Entry),
            Is.EqualTo(new[] { "BASH", "DEMON_FORM", "POMMEL_STRIKE" }));
        Assert.That(entries![2].Upgraded, Is.True);
    }

    [Test]
    public void 写几遍就是几张且保留顺序()
    {
        IReadOnlyList<HellwarWarGodDeckEntry>? entries =
            HellwarWarGodDeckPlan.Parse("STRIKE_REGENT+, DEFEND_REGENT, STRIKE_REGENT+");

        Assert.That(entries, Is.Not.Null);
        Assert.That(entries!.Count, Is.EqualTo(3));
        Assert.That(entries[0].Entry, Is.EqualTo("STRIKE_REGENT"));
        Assert.That(entries[2].Entry, Is.EqualTo("STRIKE_REGENT"));
        Assert.That(entries[2].Upgraded, Is.True);
    }

    [Test]
    public void 格式化出来的文本能被原样解析回去()
    {
        IReadOnlyList<HellwarWarGodDeckEntry>? original =
            HellwarWarGodDeckPlan.Parse("BASH+, TWIN_STRIKE, BASH+");
        string formatted = HellwarWarGodDeckPlan.Format(original);

        Assert.That(formatted, Is.EqualTo("BASH+, TWIN_STRIKE, BASH+"));

        IReadOnlyList<HellwarWarGodDeckEntry>? roundTrip = HellwarWarGodDeckPlan.Parse(formatted);
        Assert.That(
            roundTrip!.Select((entry) => (entry.Entry, entry.Upgraded)),
            Is.EqualTo(original!.Select((entry) => (entry.Entry, entry.Upgraded))));
    }

    [Test]
    public void 没有条目时格式化与摘要都返回空()
    {
        Assert.That(HellwarWarGodDeckPlan.Format(null), Is.EqualTo(string.Empty));
        Assert.That(HellwarWarGodDeckPlan.Format(new List<HellwarWarGodDeckEntry>()), Is.EqualTo(string.Empty));
        Assert.That(HellwarWarGodDeckPlan.Describe(null), Is.EqualTo("（无）"));
    }

    [Test]
    public void 摘要带张数与配置文本()
    {
        IReadOnlyList<HellwarWarGodDeckEntry>? entries =
            HellwarWarGodDeckPlan.Parse("STRIKE_REGENT, DEFEND_REGENT+");

        Assert.That(HellwarWarGodDeckPlan.Describe(entries), Is.EqualTo("2 张: STRIKE_REGENT, DEFEND_REGENT+"));
    }

    [Test]
    public void 包装裸entry列表时不带升级标记()
    {
        IReadOnlyList<HellwarWarGodDeckEntry> wrapped = HellwarWarGodDeckPlan.Wrap(new[] { "BASH", "DASH" });

        Assert.That(wrapped.Count, Is.EqualTo(2));
        Assert.That(wrapped.Select((entry) => entry.Upgraded), Is.All.False);
        Assert.That(HellwarWarGodDeckPlan.Format(wrapped), Is.EqualTo("BASH, DASH"));
    }

    // ---- 附魔（r277）---------------------------------------------------------------------------------

    [Test]
    public void 方括号里写附魔不写数量时数量为一()
    {
        IReadOnlyList<HellwarWarGodDeckEntry>? entries = HellwarWarGodDeckPlan.Parse("BASH[SHARP], DASH");

        Assert.That(entries, Is.Not.Null);
        Assert.That(entries![0].Entry, Is.EqualTo("BASH"));
        Assert.That(entries[0].EnchantEntry, Is.EqualTo("SHARP"));
        Assert.That(entries[0].EnchantAmount, Is.EqualTo(HellwarWarGodDeckPlan.DefaultEnchantAmount));
        Assert.That(entries[0].Upgraded, Is.False);
        Assert.That(entries[1].EnchantEntry, Is.Null, "没写附魔的条目保持 null");
    }

    [Test]
    public void 升级标记与附魔的先后顺序随便()
    {
        IReadOnlyList<HellwarWarGodDeckEntry>? entries =
            HellwarWarGodDeckPlan.Parse("BASH+[SHARP], DASH[SHARP]+, POMMEL_STRIKE++[SHARP]");

        Assert.That(entries, Is.Not.Null);
        Assert.That(
            entries!.Select((entry) => (entry.Entry, entry.Upgraded, entry.EnchantEntry)),
            Is.EqualTo(new[]
            {
                ("BASH", true, "SHARP"),
                ("DASH", true, "SHARP"),
                ("POMMEL_STRIKE", true, "SHARP"),
            }));
    }

    [Test]
    public void 附魔可写数量()
    {
        IReadOnlyList<HellwarWarGodDeckEntry>? entries =
            HellwarWarGodDeckPlan.Parse("BASH[SHARP:3], DASH[SHARP:0], TWIN_STRIKE[SHARP:abc]");

        Assert.That(entries, Is.Not.Null);
        Assert.That(entries![0].EnchantAmount, Is.EqualTo(3));
        Assert.That(entries[1].EnchantAmount, Is.EqualTo(HellwarWarGodDeckPlan.DefaultEnchantAmount), "非法数量回落 1");
        Assert.That(entries[2].EnchantAmount, Is.EqualTo(HellwarWarGodDeckPlan.DefaultEnchantAmount), "非法数量回落 1");
    }

    [Test]
    public void 附魔前缀可选且大小写不敏感()
    {
        IReadOnlyList<HellwarWarGodDeckEntry>? entries = HellwarWarGodDeckPlan.Parse(
            "BASH[enchantment.sharp], DASH[Enchant.Swift:2]");

        Assert.That(entries, Is.Not.Null);
        Assert.That(entries![0].EnchantEntry, Is.EqualTo("SHARP"));
        Assert.That(entries[1].EnchantEntry, Is.EqualTo("SWIFT"));
        Assert.That(entries[1].EnchantAmount, Is.EqualTo(2));
    }

    [Test]
    public void 括号不配对时不当附魔且整串保留()
    {
        // `BASH[` 里的 `[` 没有配对的 `]` ⇒ 不按附魔切，整串原样当卡名（运行时按"认不出的卡"WARN，不静默吃掉）。
        IReadOnlyList<HellwarWarGodDeckEntry>? entries = HellwarWarGodDeckPlan.Parse("BASH[, DASH[SHARP]");

        Assert.That(entries, Is.Not.Null);
        Assert.That(entries![0].Entry, Is.EqualTo("BASH["));
        Assert.That(entries[0].EnchantEntry, Is.Null);
        Assert.That(entries[1].Entry, Is.EqualTo("DASH"));
        Assert.That(entries[1].EnchantEntry, Is.EqualTo("SHARP"));
    }

    [Test]
    public void 格式化会把附魔写回去且能往返()
    {
        IReadOnlyList<HellwarWarGodDeckEntry>? original =
            HellwarWarGodDeckPlan.Parse("BASH+[SHARP], DASH[SHARP:3], TWIN_STRIKE");
        string formatted = HellwarWarGodDeckPlan.Format(original);

        Assert.That(formatted, Is.EqualTo("BASH+[SHARP], DASH[SHARP:3], TWIN_STRIKE"));

        IReadOnlyList<HellwarWarGodDeckEntry>? roundTrip = HellwarWarGodDeckPlan.Parse(formatted);
        Assert.That(
            roundTrip!.Select((entry) => (entry.Entry, entry.Upgraded, entry.EnchantEntry, entry.EnchantAmount)),
            Is.EqualTo(original!.Select((entry) => (entry.Entry, entry.Upgraded, entry.EnchantEntry, entry.EnchantAmount))));
    }
}
