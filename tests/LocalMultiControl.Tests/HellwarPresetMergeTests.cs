#nullable enable
using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 战灵召唤 · **预设表合并**（玩家文件 ∪ mod 内置，2026-10-12）的纯逻辑用例：
/// 同 <c>id</c> 玩家胜出、顺序玩家在前、空 id 两边都留 —— 以及"玩家在前"带来的
/// **同档默认预设由玩家先到先生效**这条关键副作用。
/// </summary>
[TestFixture]
public class HellwarPresetMergeTests
{
    private static HellwarPresetEntryData Entry(
        string? id,
        string name,
        string tier = "Rare",
        bool isDefault = false,
        string? deck = null)
        => new()
        {
            Id = id,
            Name = name,
            Tiers = new List<string> { tier },
            Deck = deck,
            Default = isDefault,
        };

    [Test]
    public void 玩家与内置不同id时两边都保留且玩家在前()
    {
        List<HellwarPresetEntryData> merged = HellwarPresetMerge.Merge(
            new List<HellwarPresetEntryData> { Entry("player-1", "玩家的") },
            new List<HellwarPresetEntryData> { Entry("builtin-1", "内置的") });

        Assert.That(merged.Select((entry) => entry.Id), Is.EqualTo(new[] { "player-1", "builtin-1" }));
    }

    [Test]
    public void 同id时玩家胜出且内置那条整条丢弃()
    {
        List<HellwarPresetEntryData> merged = HellwarPresetMerge.Merge(
            new List<HellwarPresetEntryData> { Entry("same", "玩家的", deck: "BASH") },
            new List<HellwarPresetEntryData> { Entry("same", "内置的", deck: "STRIKE_IRONCLAD") });

        Assert.That(merged, Has.Count.EqualTo(1), "同 id 只应剩一条（不是改名成两条）");
        Assert.That(merged[0].Name, Is.EqualTo("玩家的"));
        Assert.That(merged[0].Deck, Is.EqualTo("BASH"));
    }

    [Test]
    public void id大小写不敏感地判同一条()
    {
        List<HellwarPresetEntryData> merged = HellwarPresetMerge.Merge(
            new List<HellwarPresetEntryData> { Entry("Rare-Poison", "玩家的") },
            new List<HellwarPresetEntryData> { Entry("rare-poison", "内置的") });

        Assert.That(merged, Has.Count.EqualTo(1));
        Assert.That(merged[0].Name, Is.EqualTo("玩家的"));
    }

    [Test]
    public void id为空时两边都保留交给归一各自编号()
    {
        List<HellwarPresetEntryData> merged = HellwarPresetMerge.Merge(
            new List<HellwarPresetEntryData> { Entry(null, "玩家空id") },
            new List<HellwarPresetEntryData> { Entry(null, "内置空id") });

        Assert.That(merged, Has.Count.EqualTo(2));
    }

    [Test]
    public void 任一份为空时另一份原样通过()
    {
        List<HellwarPresetEntryData> onlyBuiltin = HellwarPresetMerge.Merge(null, new List<HellwarPresetEntryData> { Entry("b", "内置") });
        Assert.That(onlyBuiltin, Has.Count.EqualTo(1));

        List<HellwarPresetEntryData> onlyPlayer = HellwarPresetMerge.Merge(new List<HellwarPresetEntryData> { Entry("p", "玩家") }, null);
        Assert.That(onlyPlayer, Has.Count.EqualTo(1));

        Assert.That(HellwarPresetMerge.Merge(null, null), Is.Empty);
    }

    /// <summary>
    /// **关键副作用**：合并后归一时"同档多个 default ⇒ 第一个生效" ⇒ 玩家在前 ⇒ **玩家的默认预设优先**，
    /// 内置那条被降级为非默认并记进问题清单（而不是悄悄反客为主）。
    /// </summary>
    [Test]
    public void 合并后玩家写在前因此玩家的同档默认预设先生效()
    {
        List<HellwarPresetEntryData> merged = HellwarPresetMerge.Merge(
            new List<HellwarPresetEntryData> { Entry("player-default", "玩家的默认", isDefault: true) },
            new List<HellwarPresetEntryData> { Entry("builtin-default", "内置的默认", isDefault: true) });

        HellwarPresetTable table = HellwarPresetPolicy.Normalize(new HellwarPresetFileData(1, merged));
        IReadOnlyList<HellwarPresetDefinition> forTier = table.ForTier(HellwarWarGodTier.Rare);

        Assert.That(forTier[0].Id, Is.EqualTo("player-default"));
        Assert.That(table.DefaultForTier(HellwarWarGodTier.Rare)!.Id, Is.EqualTo("player-default"));
        Assert.That(table.Problems.Any((problem) => problem.Contains("多个同档默认") || problem.Contains("同为")), Is.True,
            "内置那条被降级应当留下一条问题记录");
    }
}

/// <summary>
/// **内置预设的嵌入资源**必须真的在 dll 里、而且能被解析 —— 这是"随 mod 分发预设"的命脉，
/// 而它的两种失败方式都是**静默**的：① csproj 的 <c>LogicalName</c> 与代码常量不一致（资源取不到）；
/// ② json 里写了注释 / 尾逗号（mod 侧解析不跳过注释 ⇒ 整份读不出来）。
/// </summary>
[TestFixture]
public class HellwarPresetBuiltinResourceTests
{
    [Test]
    public void 资源名常量与csproj约定一致()
    {
        Assert.That(LocalHellwarPresetStore.BuiltinResourceName,
            Is.EqualTo("LocalMultiControl.Scripts.Data.hellwar_presets.json"));
    }

    [Test]
    public void 嵌入资源存在且可解析()
    {
        using System.IO.Stream? stream = typeof(LocalHellwarPresetStore).Assembly
            .GetManifestResourceStream(LocalHellwarPresetStore.BuiltinResourceName);

        Assert.That(stream, Is.Not.Null,
            "取不到内置预设资源：检查 LocalMultiControl.csproj 的 <EmbeddedResource ... LogicalName=...> 是否与常量一致");

        using System.IO.StreamReader reader = new(stream!);
        HellwarPresetFileData? data = HellwarPresetJson.Parse(reader.ReadToEnd());

        Assert.That(data, Is.Not.Null,
            "内置预设 json 解析失败：注意 mod 侧解析**不跳过注释**、也不允许尾逗号");
    }

    /// <summary>
    /// 内置那份**数据本身**也要过归一（2026-10-15 起这份不再是空模板 ⇒ 它是"随 mod 分发的内容"，
    /// 手改坏一处会静默影响所有玩家：id 重复 / 档位名认不出 / 一条都没挂档位 / deck 全是分隔符 …
    /// 归一都会记成 `Problems`）。空模板（0 条）也是合法的 ⇒ 不钉条数，只钉"没有问题"。
    /// </summary>
    [Test]
    public void 内置预设数据归一后没有问题()
    {
        using System.IO.Stream? stream = typeof(LocalHellwarPresetStore).Assembly
            .GetManifestResourceStream(LocalHellwarPresetStore.BuiltinResourceName);
        Assert.That(stream, Is.Not.Null, "取不到内置预设资源");

        using System.IO.StreamReader reader = new(stream!);
        HellwarPresetFileData? data = HellwarPresetJson.Parse(reader.ReadToEnd());
        Assert.That(data, Is.Not.Null, "内置预设 json 解析失败");

        HellwarPresetTable table = HellwarPresetPolicy.Normalize(data);

        Assert.That(
            table.Problems,
            Is.Empty,
            "内置预设数据有归一问题（去 Scripts/Data/hellwar_presets.json 修）："
            + string.Join(" | ", table.Problems));
    }
}
