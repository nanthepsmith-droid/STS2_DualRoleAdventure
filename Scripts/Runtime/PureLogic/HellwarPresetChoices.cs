#nullable enable
using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime.PureLogic;

/// <summary>
/// 第二层「选角色预设」里一行的**含义**（预设战神 v2 · Phase B，2026-10-11）：
/// <list type="bullet">
/// <item><see cref="Default"/> = 「自动」：用该档的**默认预设**（<c>hellwar_presets.json</c> 标了 <c>default</c> 的那条，没有则内置默认）；</item>
/// <item><see cref="Random"/> = 「随机」：从该档**全部候选**里随机取一个（含文件预设）；</item>
/// <item><see cref="Concrete"/> = 玩家点名的那一档具体预设（角色皮 / 文件预设）。</item>
/// </list>
/// </summary>
internal enum HellwarPresetPickKind
{
    /// <summary>自动：用该档默认预设。</summary>
    Default = 0,

    /// <summary>随机：从本档全部候选里随机取一个。</summary>
    Random = 1,

    /// <summary>具体预设（行里带的那条）。</summary>
    Concrete = 2,
}

/// <summary>
/// 第二层「选角色预设」弹层里**一行**（纯数据）。
/// <paramref name="Preset"/> 只在 <see cref="HellwarPresetPickKind.Concrete"/> 时是"玩家点名的那条"；
/// <see cref="HellwarPresetPickKind.Default"/> 时它是**该档默认预设**（供 UI 显示"自动"后面跟什么）；
/// <see cref="HellwarPresetPickKind.Random"/> 时它同样是默认预设（只是显示用，不参与结果）。
/// </summary>
internal readonly record struct HellwarPresetPickOption(HellwarPresetPickKind Kind, HellwarWarGodPreset Preset);

/// <summary>
/// 战灵召唤 · 预设战神 v2（Phase B，2026-10-11）的**候选 / 随机池**纯逻辑：
/// 把"该弹哪些行"与"随机池怎么并"从运行时（<c>ModelDb</c> 查表、UI）里剥出来，便于单测钉住口径。
///
/// <para><b>为什么要两层</b>（用户拍板④）：第一层选"档位"（普通/罕见/稀有/先古/无尽/九连环），
/// 第二层选"角色预设"——同一档位下可以有多套预设（普通档 = **所有角色**，其余档 = <c>hellwar_presets.json</c> 里挂到该档的条目）。</para>
///
/// <para><b>弹不弹第二层</b>：只有**具体候选 &gt; 1 条**才弹（<see cref="ShouldOfferPicker"/>）——
/// 只有内置默认一条时直接用它，避免"每召一次都要多点一下"。</para>
/// </summary>
internal static class HellwarPresetChoices
{
    /// <summary>
    /// 组出第二层弹层的行：<b>[自动] →（候选 &gt; 1 时）[随机] → 各具体预设</b>。
    /// </summary>
    /// <param name="defaultPreset">该档默认预设（<c>LocalHellwarPresetStore.ResolveDefault</c> 的结果）。</param>
    /// <param name="concrete">该档的具体候选（普通档 = 全部角色；其余档 = 内置默认 + 文件预设）。</param>
    internal static IReadOnlyList<HellwarPresetPickOption> BuildPickOptions(
        HellwarWarGodPreset defaultPreset,
        IReadOnlyList<HellwarWarGodPreset> concrete)
    {
        List<HellwarPresetPickOption> options = new()
        {
            new HellwarPresetPickOption(HellwarPresetPickKind.Default, defaultPreset),
        };

        if (concrete.Count > 1)
        {
            options.Add(new HellwarPresetPickOption(HellwarPresetPickKind.Random, defaultPreset));
        }

        foreach (HellwarWarGodPreset preset in concrete)
        {
            options.Add(new HellwarPresetPickOption(HellwarPresetPickKind.Concrete, preset));
        }

        return options;
    }

    /// <summary>值不值得弹第二层：具体候选 &gt; 1（只有一条时直接用它）。</summary>
    internal static bool ShouldOfferPicker(IReadOnlyList<HellwarWarGodPreset> concrete)
        => concrete.Count > 1;

    /// <summary>
    /// 【无尽战神】/【九连环】的随机池：**内置档位默认** ∪ **文件预设**（去重 —— 同 id 只留一份，先到先得）。
    /// 顺序 = 内置在前、文件在后（池里每套权重相同；文件预设多套时自然提高出现概率，这是"接文件池"的本意）。
    /// </summary>
    internal static IReadOnlyList<HellwarWarGodPreset> BuildRandomPool(
        IReadOnlyList<HellwarWarGodPreset> builtinDefaults,
        IReadOnlyList<HellwarWarGodPreset> filePresets)
    {
        List<HellwarWarGodPreset> pool = new(builtinDefaults.Count + filePresets.Count);
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (HellwarWarGodPreset preset in builtinDefaults)
        {
            if (seen.Add(KeyOf(preset)))
            {
                pool.Add(preset);
            }
        }

        foreach (HellwarWarGodPreset preset in filePresets)
        {
            if (seen.Add(KeyOf(preset)))
            {
                pool.Add(preset);
            }
        }

        return pool;
    }

    /// <summary>
    /// 随机池取一个（把随机数收成参数 ⇒ 可单测；运行时用 <c>RunState.Rng.CombatTargets.NextInt(n)</c> 传进来）。
    /// 池为空 ⇒ 返回 <c>null</c>（调用方回落内置档位表）。
    /// </summary>
    internal static HellwarWarGodPreset? PickRandom(IReadOnlyList<HellwarWarGodPreset> pool, int roll)
    {
        if (pool.Count == 0)
        {
            return null;
        }

        int index = ((roll % pool.Count) + pool.Count) % pool.Count;
        return pool[index];
    }

    /// <summary>去重键：文件预设按 <see cref="HellwarWarGodPreset.PresetId"/>；内置（id 空）按"档位 + 皮肤"。</summary>
    private static string KeyOf(HellwarWarGodPreset preset)
        => preset.PresetId.Length > 0
            ? $"id:{preset.PresetId}"
            : $"builtin:{preset.Tier}|{preset.SkinKey}";
}
