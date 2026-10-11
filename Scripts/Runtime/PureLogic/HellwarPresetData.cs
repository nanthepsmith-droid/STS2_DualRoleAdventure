#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LocalMultiControl.Scripts.Runtime.PureLogic;

/// <summary>
/// 战灵召唤 · **「档位 × 角色预设」二维预设表**的一条（纯数据；2026-10-11 用户拍板：
/// 同一档位要有**不同的**预设战神，普通档要有**所有角色**，玩家选档后再选想要的预设）。
///
/// <para>字段语义（编辑器产物 <c>hellwar_presets.json</c> 与本层一一对应）：</para>
/// <list type="bullet">
/// <item><see cref="Tiers"/> = 这套预设挂在哪些档位（**一套可复用多档**）；</item>
/// <item><see cref="Skin"/> = 角色皮：保留键 <c>summoner</c> = 召唤者自身角色（自动兼容 mod 角色）；
///   其余按 <c>CharacterModel.Id.Entry</c> 匹配（如 <c>SILENT</c> / <c>IRONCLAD</c>；大小写不敏感；
///   mod 角色待接，见 <c>HellwarSummonCombatRuntime.ResolveSkin</c>）；</item>
/// <item><see cref="DeckText"/> = 卡组，格式与 <c>hellwarDeck*</c> 键**完全一致**（<c>NEUTRALIZE, BACKFLIP+, …</c>，
///   解析复用 <see cref="HellwarWarGodDeckPlan"/>）；<c>null</c> = **继承**该档内置默认；
///   显式空串 = **不替换**（用该角色的初始卡组）；</item>
/// <item><see cref="RelicEntries"/> = 预设遗物（裸 entry 列表）；<c>null</c> = **继承**该档内置默认；
///   空列表 = **一件都不加**；非空 = 完全接管（不与内置合并）；</item>
/// <item><see cref="UpgradeDeck"/> / <see cref="BonusHp"/>：缺省 = 继承该档内置默认。</item>
/// </list>
///
/// ⚠ 件数阶梯（成本 − 1）**只做提示、不阻断**（2026-10-11 用户拍板：允许破坏平衡，
/// 「玩家想怎么样就怎么样我们无权干涉」）。
/// </summary>
/// <param name="Id">预设唯一 id（编辑器生成；空 = 归一时自动编号）。</param>
/// <param name="DisplayName">显示名（召唤时选预设的 UI / 日志用；空 = 用 Id）。</param>
/// <param name="Tiers">挂载的档位（至少一个；非法条目在归一时剔除）。</param>
/// <param name="Skin">角色皮（null = 继承该档内置默认）。</param>
/// <param name="DeckText">卡组文本（null = 继承；空串 = 不替换；其余 = 自定义卡组）。</param>
/// <param name="RelicEntries">预设遗物（null = 继承；空列表 = 不加；非空 = 完全接管）。</param>
/// <param name="UpgradeDeck">整组全升级（null = 继承）。</param>
/// <param name="BonusHp">额外血上限（null = 继承）。</param>
/// <param name="IsDefault">该档的**默认预设**（召唤时不弹选择就用它；同档多个 = 取第一个并记问题）。</param>
internal sealed record HellwarPresetDefinition(
    string Id,
    string DisplayName,
    IReadOnlyList<HellwarWarGodTier> Tiers,
    string? Skin,
    string? DeckText,
    IReadOnlyList<string>? RelicEntries,
    bool? UpgradeDeck,
    int? BonusHp,
    bool IsDefault);

/// <summary>
/// 归一后的整份预设表（不可变）：<see cref="All"/> = 全部合法预设；
/// <see cref="Problems"/> = 归一时发现的问题（启动自检逐条 WARN，**不阻断加载**）。
/// </summary>
internal sealed class HellwarPresetTable
{
    /// <summary>空表（文件不存在 / 内容为空 ⇒ 四档全用内置默认，行为与旧版一致）。</summary>
    internal static readonly HellwarPresetTable Empty =
        new(Array.Empty<HellwarPresetDefinition>(), Array.Empty<string>());

    internal HellwarPresetTable(IReadOnlyList<HellwarPresetDefinition> all, IReadOnlyList<string> problems)
    {
        All = all;
        Problems = problems;
    }

    internal IReadOnlyList<HellwarPresetDefinition> All { get; }

    internal IReadOnlyList<string> Problems { get; }

    /// <summary>挂在某档的全部预设（保序）。</summary>
    internal IReadOnlyList<HellwarPresetDefinition> ForTier(HellwarWarGodTier tier)
    {
        List<HellwarPresetDefinition> result = new();
        foreach (HellwarPresetDefinition definition in All)
        {
            if (definition.Tiers.Contains(tier))
            {
                result.Add(definition);
            }
        }

        return result;
    }

    /// <summary>
    /// 某档的**默认预设**（召唤时不弹选择就用它）：显式 <c>IsDefault</c> 优先，否则该档第一条；
    /// 该档一条都没有 ⇒ <c>null</c>（调用方回落内置默认）。
    /// </summary>
    internal HellwarPresetDefinition? DefaultForTier(HellwarWarGodTier tier)
    {
        IReadOnlyList<HellwarPresetDefinition> forTier = ForTier(tier);
        foreach (HellwarPresetDefinition definition in forTier)
        {
            if (definition.IsDefault)
            {
                return definition;
            }
        }

        return forTier.Count > 0 ? forTier[0] : null;
    }
}

/// <summary>
/// <c>hellwar_presets.json</c> 的「文本 ⇄ 数据」解析层（纯逻辑、无游戏类型、可单测）。
/// 解析**只做结构**（DTO），字段归一 / 校验在 <see cref="HellwarPresetPolicy"/>。
/// </summary>
internal static class HellwarPresetJson
{
    /// <summary>预设表文件名（落 <c>%APPDATA%\SlayTheSpire2\</c>，与 <c>vakuu_autopilot.json</c> 同目录）。</summary>
    internal const string FileName = "hellwar_presets.json";

    private const int CurrentVersion = 1;

    private sealed class FileDto
    {
        public int Version { get; set; }

        public List<EntryDto>? Presets { get; set; }
    }

    private sealed class EntryDto
    {
        public string? Id { get; set; }

        public string? Name { get; set; }

        public List<string>? Tiers { get; set; }

        public string? Skin { get; set; }

        public string? Deck { get; set; }

        public List<string>? Relics { get; set; }

        public bool? UpgradeDeck { get; set; }

        public int? BonusHp { get; set; }

        public bool Default { get; set; }
    }

    /// <summary>解析 json 文本；结构坏 / 根本不是对象 ⇒ <c>null</c>（调用方按"文件损坏"处理，用内置默认兜底）。</summary>
    internal static HellwarPresetFileData? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            FileDto? dto = JsonSerializer.Deserialize<FileDto>(json, options);
            if (dto?.Presets == null)
            {
                return null;
            }

            List<HellwarPresetEntryData> entries = new(dto.Presets.Count);
            foreach (EntryDto entry in dto.Presets)
            {
                if (entry == null)
                {
                    continue;
                }

                entries.Add(new HellwarPresetEntryData
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    Tiers = entry.Tiers,
                    Skin = entry.Skin,
                    Deck = entry.Deck,
                    Relics = entry.Relics,
                    UpgradeDeck = entry.UpgradeDeck,
                    BonusHp = entry.BonusHp,
                    Default = entry.Default,
                });
            }

            return new HellwarPresetFileData(dto.Version, entries);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>把数据序列化回 json（导出模板 / 编辑器联调用；字段名与解析一致）。</summary>
    internal static string Serialize(HellwarPresetFileData data)
    {
        FileDto dto = new()
        {
            Version = data.Version <= 0 ? CurrentVersion : data.Version,
            Presets = new List<EntryDto>(data.Presets.Count),
        };
        foreach (HellwarPresetEntryData entry in data.Presets)
        {
            dto.Presets.Add(new EntryDto
            {
                Id = entry.Id,
                Name = entry.Name,
                Tiers = entry.Tiers,
                Skin = entry.Skin,
                Deck = entry.Deck,
                Relics = entry.Relics,
                UpgradeDeck = entry.UpgradeDeck,
                BonusHp = entry.BonusHp,
                Default = entry.Default,
            });
        }

        JsonSerializerOptions options = new() { WriteIndented = true };
        return JsonSerializer.Serialize(dto, options);
    }
}

/// <summary>json 里一条预设的原始数据（字段缺省 = 继承该档内置默认，见 <see cref="HellwarPresetDefinition"/>）。</summary>
internal sealed class HellwarPresetEntryData
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public List<string>? Tiers { get; set; }

    public string? Skin { get; set; }

    public string? Deck { get; set; }

    public List<string>? Relics { get; set; }

    public bool? UpgradeDeck { get; set; }

    public int? BonusHp { get; set; }

    public bool Default { get; set; }
}

/// <summary>整份文件的原始数据。</summary>
internal sealed record HellwarPresetFileData(int Version, List<HellwarPresetEntryData> Presets);

/// <summary>
/// 预设表的**归一 / 校验**（纯逻辑）：结构解析（<see cref="HellwarPresetJson.Parse"/>）之后的第二段。
/// <list type="bullet">
/// <item>id 空白 ⇒ 自动编号（<c>preset-1</c> 起，冲突顺延）；显示名空白 ⇒ 用 id；</item>
/// <item>档位名支持 英文 id + 中文别名（普通/罕见/稀有/先古，大小写不敏感）—— ⚠ 注意中文
///   「罕见」= <see cref="HellwarWarGodTier.Rare"/>、「稀有」= <see cref="HellwarWarGodTier.Epic"/>（游戏侧命名如此）；</item>
/// <item>档位一条都不合法 ⇒ 整条跳过并记问题（不阻断其余预设）；</item>
/// <item>同档显式 default 多个 ⇒ 第一个生效、其余记问题；</item>
/// <item>bonusHp 负数 ⇒ 归 0 并记问题。</item>
/// </list>
/// 任何问题都**只记不抛**：预设表再脏也不能影响召唤（消费端还有内置默认兜底）。
/// </summary>
internal static class HellwarPresetPolicy
{
    internal static HellwarPresetTable Normalize(HellwarPresetFileData? data)
    {
        if (data == null || data.Presets.Count == 0)
        {
            return HellwarPresetTable.Empty;
        }

        List<HellwarPresetDefinition> normalized = new(data.Presets.Count);
        List<string> problems = new();
        HashSet<string> usedIds = new(StringComparer.OrdinalIgnoreCase);
        int autoIndex = 0;

        for (int i = 0; i < data.Presets.Count; i++)
        {
            HellwarPresetEntryData entry = data.Presets[i];
            int ordinal = i + 1;

            string id = (entry.Id ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                id = $"preset-{ordinal}";
                while (!usedIds.Add(id))
                {
                    id = $"preset-{ordinal}-{++autoIndex}";
                }
            }
            else if (!usedIds.Add(id))
            {
                string unique = $"{id}-{ordinal}";
                problems.Add($"第 {ordinal} 条预设的 id「{id}」重复，已改名为「{unique}」");
                id = unique;
                usedIds.Add(id);
            }

            string displayName = (entry.Name ?? string.Empty).Trim();
            if (displayName.Length == 0)
            {
                displayName = id;
            }

            List<HellwarWarGodTier> tiers = new();
            if (entry.Tiers == null || entry.Tiers.Count == 0)
            {
                problems.Add($"预设「{displayName}」({id}) 没写 tiers，已跳过（tiers = 挂哪些档位，如 [\"Rare\"]）");
                continue;
            }

            foreach (string tierName in entry.Tiers)
            {
                if (TryParseTier(tierName, out HellwarWarGodTier tier))
                {
                    if (!tiers.Contains(tier))
                    {
                        tiers.Add(tier);
                    }
                }
                else
                {
                    problems.Add(
                        $"预设「{displayName}」({id}) 的档位「{tierName}」认不出，已跳过"
                        + "（可选：common/普通、rare/罕见、epic/稀有、ancient/先古）");
                }
            }

            if (tiers.Count == 0)
            {
                problems.Add($"预设「{displayName}」({id}) 没有任何合法档位，整条跳过");
                continue;
            }

            int? bonusHp = entry.BonusHp;
            if (bonusHp.HasValue && bonusHp.Value < 0)
            {
                problems.Add($"预设「{displayName}」({id}) 的 bonusHp={bonusHp.Value} 为负数，已按 0 处理");
                bonusHp = 0;
            }

            string? deckText = entry.Deck;
            if (deckText != null && HellwarWarGodDeckPlan.Parse(deckText) == null && deckText.Trim().Length > 0)
            {
                // 只可能是"全分隔符"之类的退化输入；真正的"认不出卡牌"由运行时查表时 WARN。
                problems.Add($"预设「{displayName}」({id}) 的 deck 文本解析不出任何条目，将按「不替换卡组」处理");
            }

            normalized.Add(new HellwarPresetDefinition(
                id,
                displayName,
                tiers,
                NormalizeSkin(entry.Skin),
                deckText,
                NormalizeRelics(entry.Relics),
                entry.UpgradeDeck,
                bonusHp,
                entry.Default));
        }

        MarkDuplicateDefaults(normalized, problems);
        return new HellwarPresetTable(normalized, problems);
    }

    /// <summary>档位名 → 枚举（英文 id + 中文别名；⚠「罕见」=Rare、「稀有」=Epic，游戏侧命名如此）。</summary>
    internal static bool TryParseTier(string? text, out HellwarWarGodTier tier)
    {
        switch ((text ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "common":
            case "normal":
            case "普通":
                tier = HellwarWarGodTier.Common;
                return true;
            case "rare":
            case "罕见":
                tier = HellwarWarGodTier.Rare;
                return true;
            case "epic":
            case "稀有":
                tier = HellwarWarGodTier.Epic;
                return true;
            case "ancient":
            case "先古":
            case "远古":
                tier = HellwarWarGodTier.Ancient;
                return true;
            default:
                tier = HellwarWarGodTier.Common;
                return false;
        }
    }

    /// <summary>档位枚举 → 规范英文名（写回模板 / 日志用）。</summary>
    internal static string FormatTier(HellwarWarGodTier tier)
        => tier switch
        {
            HellwarWarGodTier.Rare => "rare",
            HellwarWarGodTier.Epic => "epic",
            HellwarWarGodTier.Ancient => "ancient",
            _ => "common",
        };

    /// <summary>皮肤键：去空白；保留键（summoner 等）与角色 entry 都按原样交给运行时匹配（大小写不敏感）。</summary>
    private static string? NormalizeSkin(string? skin)
    {
        string trimmed = (skin ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>遗物 entry：去空白 + 去 <c>RELIC.</c> 前缀（宽松，与卡组的 <c>CARD.</c> 同款）；null 保持 null（= 继承）。</summary>
    private static IReadOnlyList<string>? NormalizeRelics(List<string>? relics)
    {
        if (relics == null)
        {
            return null;
        }

        List<string> entries = new(relics.Count);
        foreach (string raw in relics)
        {
            string token = (raw ?? string.Empty).Trim();
            if (token.StartsWith("RELIC.", StringComparison.OrdinalIgnoreCase))
            {
                token = token["RELIC.".Length..].Trim();
            }

            if (token.Length > 0)
            {
                entries.Add(token);
            }
        }

        return entries;
    }

    /// <summary>同档显式 default 多个 ⇒ 第一个生效、其余改回非默认并记问题（保证 DefaultForTier 确定性）。</summary>
    private static void MarkDuplicateDefaults(List<HellwarPresetDefinition> normalized, List<string> problems)
    {
        foreach (HellwarWarGodTier tier in Enum.GetValues<HellwarWarGodTier>())
        {
            bool seenDefault = false;
            for (int i = 0; i < normalized.Count; i++)
            {
                HellwarPresetDefinition definition = normalized[i];
                if (!definition.Tiers.Contains(tier) || !definition.IsDefault)
                {
                    continue;
                }

                if (!seenDefault)
                {
                    seenDefault = true;
                    continue;
                }

                normalized[i] = definition with { IsDefault = false };
                problems.Add(
                    $"预设「{definition.DisplayName}」({definition.Id}) 与更早的预设同为「{FormatTier(tier)}」档默认，"
                    + "本条已改为非默认（每档默认只有一个，先到先得）");
            }
        }
    }
}

/// <summary>
/// 预设定义 → 运行时 <see cref="HellwarWarGodPreset"/> 的**继承映射**（纯逻辑、可单测）：
/// 缺省字段（null）继承该档**内置默认**，其余直通。
/// </summary>
internal static class HellwarPresetRuntimeMapping
{
    internal static HellwarWarGodPreset ToRuntimePreset(
        HellwarPresetDefinition definition,
        HellwarWarGodTier tier,
        HellwarWarGodPreset builtin)
    {
        return new HellwarWarGodPreset(
            tier,
            definition.Skin ?? builtin.SkinKey,
            definition.UpgradeDeck ?? builtin.UpgradeDeck,
            definition.BonusHp ?? builtin.BonusHp,
            definition.Id,
            definition.DisplayName,
            definition.DeckText,
            definition.RelicEntries);
    }
}
