#nullable enable
using System;
using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 战灵召唤 · **预设战神 v2（Phase B，2026-10-11）的运行时目录**：
/// 把"某档有哪些可选的预设"（第二层「选角色预设」的候选）与"随机池"（【无尽战神】/【九连环】）
/// 从召唤流程里剥出来，供 <see cref="HellwarSummonCombatRuntime"/> 使用。
///
/// <para><b>候选来源</b>（用户拍板 ①②③）：</para>
/// <list type="bullet">
/// <item><b>普通档</b> = **所有原版角色**（每角色一套：该角色的**初始卡组 + 初始遗物**）——
///   即拍板②「普通档读 <c>CharacterModel</c> 初始牌组 + 初始遗物，且要有所有角色」；</item>
/// <item><b>罕见 / 稀有 / 先古</b> = 该档**内置默认** + <c>hellwar_presets.json</c> 挂到该档的预设
///   （拍板①：这三档要哪几套内容**等用户手搓给表**，UI 先把通道打通，表来即生效）；</item>
/// <item>「自动 / 随机」两行由 <see cref="HellwarPresetChoices.BuildPickOptions"/> 统一插入（拍板③）。</item>
/// </list>
///
/// <para>⚠ 全部只读、幂等、**永不抛**：目录出问题最坏只是"候选少几条"，召唤本身有内置默认兜底。</para>
/// </summary>
internal static class HellwarPresetCatalog
{
    /// <summary>日志前缀（与 <see cref="HellwarSummonCombatRuntime"/> 同款，便于判读时 grep 一处）。</summary>
    private const string LogTag = "[地狱战神]";

    /// <summary>普通档「角色预设」的预设 id 前缀（日志 / 排查用，标明它来自角色表而非文件）。</summary>
    internal const string CharacterPresetPrefix = "char:";

    private static IReadOnlyList<HellwarWarGodPreset>? _characterPresets;

    /// <summary>
    /// 某档的**具体候选**（第二层弹层里除「自动 / 随机」之外的每一行）：
    /// 普通档 = 全部角色；其余档 = 内置默认 + 文件预设。
    /// </summary>
    internal static IReadOnlyList<HellwarWarGodPreset> ConcreteFor(HellwarWarGodTier tier)
    {
        List<HellwarWarGodPreset> list = new();
        if (tier == HellwarWarGodTier.Common)
        {
            list.AddRange(CharacterPresets());
        }
        else
        {
            list.Add(HellwarWarGodPresets.Resolve(tier));
        }

        // 玩家文件 ∪ 内置（可用性过滤在 UsableForTier 里做：引用了本机没有内容的预设会被剔除并点名）。
        list.AddRange(LocalHellwarPresetStore.UsableForTier(tier, "选角色预设候选"));
        return list;
    }

    /// <summary>
    /// 普通档的「所有角色」候选（每角色一套）：皮肤 = 该角色，卡组 = **不替换**（走该角色初始卡组），
    /// 不额外加遗物（角色初始遗物随角色自带）⇒ 正是"初始卡组 + 初始遗物"。
    /// 懒构建并缓存（角色表在开局后不变）；任何异常 ⇒ 退回空表（消费端回落内置默认）。
    /// </summary>
    internal static IReadOnlyList<HellwarWarGodPreset> CharacterPresets()
    {
        if (_characterPresets != null)
        {
            return _characterPresets;
        }

        try
        {
            List<HellwarWarGodPreset> list = new();
            foreach (CharacterModel character in ModelDb.AllCharacters)
            {
                // 只收**可玩角色**：`ModelDb.AllCharacters` 里还可能混着测试 / 元角色
                //（`Deprived` / `RandomCharacter` / `DeprecatedCharacter`，`IsPlayable=false`，
                // 初始卡组是"各角色各一张"的拼凑牌）⇒ 选到它们只会得到一个废牌战神，直接剔除。
                if (!character.IsPlayable)
                {
                    continue;
                }

                string entry = character.Id.Entry;
                list.Add(new HellwarWarGodPreset(
                    HellwarWarGodTier.Common,
                    entry,
                    UpgradeDeck: false,
                    BonusHp: 0,
                    PresetId: CharacterPresetPrefix + entry,
                    DisplayName: CharacterDisplayName(character),
                    // 显式空串 = 不替换卡组（用该角色初始卡组）；空列表 = 不额外加遗物（用角色初始遗物）。
                    DeckText: string.Empty,
                    RelicEntries: Array.Empty<string>()));
            }

            _characterPresets = list;
            return list;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"{LogTag} 角色预设目录构建失败（普通档回落内置默认）: {exception.Message}");
            _characterPresets = Array.Empty<HellwarWarGodPreset>();
            return _characterPresets;
        }
    }

    /// <summary>
    /// 【无尽战神】的随机池：**普通 / 罕见 / 稀有**（可选含先古）三档的"内置默认 ∪ 文件预设"。
    /// 没有文件预设时与 <see cref="HellwarWarGodPresets.ResolveRandomPool"/> **逐条一致**（零回归）。
    /// </summary>
    internal static IReadOnlyList<HellwarWarGodPreset> EndlessPool(bool includeAncient)
    {
        List<HellwarWarGodPreset> builtin = new();
        List<HellwarWarGodPreset> file = new();
        foreach (HellwarWarGodPreset preset in HellwarWarGodPresets.ResolveRandomPool(includeAncient))
        {
            builtin.Add(preset);
            file.AddRange(LocalHellwarPresetStore.UsableForTier(preset.Tier, "无尽战神池"));
        }

        return HellwarPresetChoices.BuildRandomPool(builtin, file);
    }

    /// <summary>
    /// 【终极九连环】的随机池：先古档的"内置默认 ∪ 文件预设"（没有文件预设时 = 单条内置先古，与旧行为一致）。
    /// </summary>
    internal static IReadOnlyList<HellwarWarGodPreset> NineChainPool()
    {
        List<HellwarWarGodPreset> builtin = new() { HellwarWarGodPresets.Resolve(HellwarWarGodTier.Ancient) };
        return HellwarPresetChoices.BuildRandomPool(
            builtin,
            LocalHellwarPresetStore.UsableForTier(HellwarWarGodTier.Ancient, "九连环池"));
    }

    /// <summary>第二层弹层里一行的**显示文案**（自动 / 随机 / 具体预设）。</summary>
    internal static string DescribePickOption(HellwarPresetPickOption option)
    {
        switch (option.Kind)
        {
            case HellwarPresetPickKind.Default:
                return LocalModText.HellwarPresetAutoLabel(DescribePresetShort(option.Preset));
            case HellwarPresetPickKind.Random:
                return LocalModText.HellwarPresetRandomLabel;
            default:
                return DescribePresetShort(option.Preset);
        }
    }

    /// <summary>预设的**简称**（UI / 日志用）：文件预设用它的显示名；内置 / 角色预设回落到"档位 · 内置默认"。</summary>
    internal static string DescribePresetShort(HellwarWarGodPreset preset)
    {
        if (preset.DisplayName.Length > 0)
        {
            return preset.DisplayName;
        }

        return LocalModText.HellwarPresetBuiltinLabel(LocalModText.HellwarTierName(preset.Tier));
    }

    /// <summary>角色名（本地化标题）；取不到时回落 entry（绝不抛）。</summary>
    private static string CharacterDisplayName(CharacterModel character)
    {
        try
        {
            string title = character.Title.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
        }
        catch (Exception)
        {
            // 本地化缺键：回落 entry，不影响召唤。
        }

        return character.Id.Entry;
    }
}
