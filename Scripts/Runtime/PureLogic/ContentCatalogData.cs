#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LocalMultiControl.Scripts.Runtime.PureLogic;

/// <summary>
/// 「预设构筑器」（可视化造战神的外部程序，见 <c>TODO.md §预设构筑器</c>）的**数据源**：
/// <c>content_catalog.json</c> 的纯数据模型（DTO）。
///
/// <para><b>谁写谁读</b>：mod 在设置页点一次「导出内容目录」写出本文件（枚举**运行期**的
/// <c>ModelDb</c> ⇒ **天然含第三方 mod 的卡 / 遗物 / 角色**）；编辑器只读它，不自己解析 C# 源码。
/// 玩家侧口径 = 先导出、再开编辑器（没导出则编辑器提示先导出，不显示列表）。</para>
///
/// <para><b>为什么同时给 id 与 entry</b>：预设文件的字段用的都是 **entry**
/// （<c>hellwar_presets.json</c> 的 <c>deck</c> / <c>relics</c>，见 <see cref="HellwarPresetDefinition"/>），
/// 而 <c>ModelId</c> 的完整串（<c>CARD.BASH</c>）更适合日志 / 排查 ⇒ 两个都给，
/// 编辑器拼预设时直接用 <see cref="ContentCatalogCard.Entry"/>。</para>
///
/// <para>⚠ 本文件是**纯逻辑**（无游戏类型、无 IO），JSON 层见 <see cref="ContentCatalogJson"/>；
/// 枚举与写盘在 <c>LocalContentCatalogExporter</c>。</para>
/// </summary>
internal sealed class ContentCatalogDocument
{
    /// <summary>文件格式版本（编辑器按它判断兼容性；当前 = 1）。</summary>
    public int Version { get; set; } = ContentCatalogJson.CurrentVersion;

    /// <summary>游戏主程序集版本（<c>sts2.dll</c> 的 AssemblyVersion；取不到 = 空串）。</summary>
    public string GameVersion { get; set; } = string.Empty;

    /// <summary>导出时刻（UTC，ISO-8601）。玩家装了 / 卸了 mod 后要重新导出，这个字段用于自查新旧。</summary>
    public string ExportedAtUtc { get; set; } = string.Empty;

    /// <summary>条数汇总（由 <see cref="ContentCatalogPolicy.Count"/> 算，见该方法的"同进同退"注释）。</summary>
    public ContentCatalogCounts Counts { get; set; } = new();

    public List<ContentCatalogCard> Cards { get; set; } = new();

    public List<ContentCatalogRelic> Relics { get; set; } = new();

    public List<ContentCatalogCharacter> Characters { get; set; } = new();

    /// <summary>附魔（r277 新增）：预设里写 <c>BASH[SHARP]</c> 用得上，编辑器得能列出来。</summary>
    public List<ContentCatalogEnchantment> Enchantments { get; set; } = new();
}

/// <summary>目录条数汇总（每个"其中 mod"= 来源不是游戏本体的那些）。</summary>
internal sealed class ContentCatalogCounts
{
    public int Cards { get; set; }

    public int Relics { get; set; }

    public int Characters { get; set; }

    public int Enchantments { get; set; }

    public int ModCards { get; set; }

    public int ModRelics { get; set; }

    public int ModCharacters { get; set; }

    public int ModEnchantments { get; set; }
}

/// <summary>一张卡（编辑器卡池列表的一行；字段够拼一个 <c>hellwar_presets.json</c> 的 <c>deck</c> 条目）。</summary>
internal sealed class ContentCatalogCard
{
    /// <summary>完整 <c>ModelId</c> 串（如 <c>CARD.BASH</c>；排查 / 日志用）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary><c>ModelId.Entry</c>（如 <c>BASH</c>）—— 预设文件里写的就是这个。</summary>
    public string Entry { get; set; } = string.Empty;

    /// <summary>本地化标题（当前语言）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>本地化描述（当前语言，已展开动态变量 / 关键词后缀）。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>稀有度（<c>CardRarity</c> 的枚举名：Basic / Common / Uncommon / Rare / Ancient / Token / Status / Curse…）。</summary>
    public string Rarity { get; set; } = string.Empty;

    /// <summary>类型（<c>CardType</c> 的枚举名：Attack / Skill / Power / Status / Curse / Quest）。</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>能量费用（<c>-1</c> = 无费用，如部分状态牌）。</summary>
    public int Cost { get; set; } = -1;

    /// <summary>是否 X 费（X 费卡的 <see cref="Cost"/> 恒为 0，编辑器要按 X 显示）。</summary>
    public bool CostsX { get; set; }

    /// <summary>星星费用（<c>-1</c> = 不用星；储君的攒星牌用）。</summary>
    public int StarCost { get; set; } = -1;

    /// <summary>所属卡池的**稳定标识** = <c>CardPoolModel.Id.Entry</c>
    /// （实机取值形如 <c>IRONCLAD_CARD_POOL</c> / <c>EVENT_CARD_POOL</c> / <c>TOKEN_CARD_POOL</c>；取不到 = 空串）。</summary>
    public string Pool { get; set; } = string.Empty;

    /// <summary>所属卡池的**显示名** = <c>CardPoolModel.Title</c>（如 <c>Ironclad</c>；取不到 = 空串）。
    /// 给编辑器分组 / 显示用（<see cref="Pool"/> 是标识，这个是名字）。</summary>
    public string PoolTitle { get; set; } = string.Empty;

    /// <summary>
    /// 卡面图路径 = 游戏自己的 <c>CardModel.PortraitPath</c>（**权威来源**，2026-10-12 r275 实机确认三种形态）：
    /// <list type="bullet">
    /// <item><c>.tres</c>（≈695 张，本体系 + 少数 mod）⇒ 是**图集条目**（<c>res://images/atlases/card_atlas.sprites/&lt;池&gt;/&lt;entry&gt;.tres</c>），
    ///   要读它的 <c>region</c> 再从 <c>card_atlas_0/1.png</c> 裁剪；</item>
    /// <item><c>.png</c>（≈2064 张，多为 mod）⇒ **整图直读**（mod 覆写了路径，图在**那个 mod 自己的 pck** 里）；</item>
    /// <item><c>.jpg</c>（≈205 张，如 figure_Saya）⇒ 同上，整图直读。</item>
    /// </list>
    /// 已把 <c>\</c> 规范成 <c>/</c>（Godot 在 Windows 上会给反斜杠路径）；<c>res://</c> 前缀保留，由编辑器叠加
    /// **主 pck + 各 mod 的 pck** 解析。
    /// </summary>
    public string Portrait { get; set; } = string.Empty;

    /// <summary>
    /// **整图 png 路径 —— 只在能确定时给**（否则空串，绝不给猜想；编辑器优先用 <see cref="Portrait"/>）：
    /// <list type="bullet">
    /// <item><see cref="Portrait"/> 本身就是图片（png / jpg）⇒ 原样复制过来；</item>
    /// <item><see cref="Portrait"/> 是图集 <c>.tres</c> ⇒ 按游戏同款规则换算成
    ///   <c>res://images/packed/card_portraits/&lt;池&gt;/&lt;entry&gt;.png</c>（见 <see cref="ContentCatalogPolicy.CardPortraitPngHint"/>），
    ///   **并用 <c>ResourceLoader.Exists</c> 复核**，不存在则留空；</item>
    /// <item>其余形态（自定义路径的 <c>.tres</c> 等）⇒ 留空。</item>
    /// </list>
    /// </summary>
    public string PortraitPng { get; set; } = string.Empty;

    /// <summary>
    /// 游戏自己的"有整图立绘"判据（<c>CardModel.HasPortrait</c>）。
    /// ⚠ **只对没覆写图片路径的卡准确**：2026-10-12 实机里 **322 张 mod 卡 <see cref="Portrait"/> 是能直读的 png，
    /// 但这里仍是 <c>false</c>**（mod 覆写了 <c>PortraitPath</c>、游戏那句判据查的是它自己的规则路径）。
    /// ⇒ 编辑器判"这张卡有没有图"要**以 <see cref="Portrait"/> 为准**，本字段只作参考。
    /// </summary>
    public bool HasPortrait { get; set; }

    /// <summary>来源（<c>base</c> = 游戏本体；其余 = 提供该模型的程序集简单名，通常就是 mod 的 dll 名）。</summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>一件遗物。</summary>
internal sealed class ContentCatalogRelic
{
    public string Id { get; set; } = string.Empty;

    public string Entry { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>稀有度（<c>RelicRarity</c> 的枚举名：Starter / Common / Uncommon / Rare / Shop / Event / Ancient）。</summary>
    public string Rarity { get; set; } = string.Empty;

    /// <summary>所属遗物池 entry（取不到 = 空串）。</summary>
    public string Pool { get; set; } = string.Empty;

    /// <summary>
    /// 图标路径 = 游戏自己的 <c>RelicModel.IconPath</c>（**权威来源**）：多为图集条目
    /// <c>res://images/atlases/relic_atlas.sprites/&lt;entry&gt;.tres</c>，但 **mod 常覆写成自己 pck 里的整图 png**
    /// （2026-10-12 实机：png 1224 / tres 331）⇒ 编辑器按扩展名分派（png 直读 / tres 读 region 裁图集）。
    /// 反斜杠已规范成 <c>/</c>，<c>res://</c> 保留。
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// 大图（图鉴用的整图）路径 —— **只在文件真的存在时给**（否则空串）：
    /// 按游戏同款规则推 <c>res://images/relics/&lt;entry 小写&gt;.png</c>（见
    /// <see cref="ContentCatalogPolicy.RelicBigIconHint"/>）再用 <c>ResourceLoader.Exists</c> 复核。
    /// ⚠ 2026-10-12 实机：原版遗物大多有、**mod 遗物基本没有**（1555 件里只有 306 件命中，mod 的大图在自己 pck 里
    /// 且是驼峰文件名）⇒ 编辑器拿不到就回落 <see cref="Icon"/>。
    /// </summary>
    public string BigIcon { get; set; } = string.Empty;

    /// <summary>游戏判据：<see cref="Icon"/> 这条路径存在（2026-10-12 实机 1553/1555 为真，两件假的是 mod 缺图）。</summary>
    public bool HasIcon { get; set; }

    /// <summary><see cref="BigIcon"/> 是否存在（存在才给路径 ⇒ 这个字段与"路径非空"同进同退）。</summary>
    public bool HasBigIcon { get; set; }

    public string Source { get; set; } = string.Empty;
}

/// <summary>一个角色（编辑器选"角色皮"与拼"普通档预设"用）。</summary>
internal sealed class ContentCatalogCharacter
{
    public string Id { get; set; } = string.Empty;

    public string Entry { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>游戏自己的可玩判据（<c>CharacterModel.IsPlayable</c>）。
    /// <c>false</c> = 测试 / 元角色（<c>Deprived</c> / <c>RandomCharacter</c> / <c>DeprecatedCharacter</c>），
    /// 它们的初始卡组是"各角色各一张"的拼凑牌 ⇒ 编辑器**不该**把它们列进候选（与 mod 侧
    /// <c>HellwarPresetCatalog.CharacterPresets</c> 的过滤口径一致）。</summary>
    public bool IsPlayable { get; set; }

    /// <summary>初始卡组：**卡牌 entry 列表、保留重复**（写进预设 <c>deck</c> 时"写几遍就是几张"）。</summary>
    public List<string> StartingDeck { get; set; } = new();

    /// <summary>初始遗物：**遗物 entry 列表**。</summary>
    public List<string> StartingRelics { get; set; } = new();

    public string Source { get; set; } = string.Empty;
}

/// <summary>一个附魔（预设里写 <c>BASH[SHARP]</c> 时的候选；r277 新增）。</summary>
internal sealed class ContentCatalogEnchantment
{
    public string Id { get; set; } = string.Empty;

    /// <summary><c>ModelId.Entry</c>（如 <c>SHARP</c>）—— 预设 <c>[ ]</c> 里写的就是这个。</summary>
    public string Entry { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>图标路径（游戏会**兜底到 missing 图**；兜底了就留空 + <see cref="HasIcon"/>=false）。</summary>
    public string Icon { get; set; } = string.Empty;

    public bool HasIcon { get; set; }

    /// <summary>这个附魔的**数量是否有意义**（游戏 <c>EnchantmentModel.ShowAmount</c>）：
    /// <c>true</c> ⇒ 预设里建议写 <c>[SHARP:3]</c> 这种带数量的；<c>false</c> ⇒ 写不写都是 1。</summary>
    public bool ShowAmount { get; set; }

    /// <summary>**只能给哪些类型的卡**（游戏 <c>CanEnchantCardType</c> 逐个类型问出来的；
    /// 全类型都允许 ⇒ 空列表 = 无限制。⚠ 还有别的限制（默认排除状态 / 诅咒 / 任务牌），
    /// 真正能不能带**以召唤时 `CanEnchant` 的校验为准**）。</summary>
    public List<string> AllowedCardTypes { get; set; } = new();

    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// <c>content_catalog.json</c> 的「文本 ⇄ 数据」层（纯逻辑、无游戏类型、可单测）。
/// 与 <see cref="HellwarPresetJson"/> 同款：System.Text.Json、缩进输出、大小写不敏感的解析。
/// </summary>
internal static class ContentCatalogJson
{
    /// <summary>目录文件名（落 <c>%APPDATA%\SlayTheSpire2\</c>，与 <c>vakuu_autopilot.json</c> / <c>hellwar_presets.json</c> 同目录）。</summary>
    internal const string FileName = "content_catalog.json";

    /// <summary>当前格式版本（<see cref="ContentCatalogDocument.Version"/> 的默认值）。</summary>
    internal const int CurrentVersion = 1;

    /// <summary>序列化（导出用）。<c>Version</c> 非法时兜到 <see cref="CurrentVersion"/>。</summary>
    internal static string Serialize(ContentCatalogDocument document)
    {
        if (document.Version <= 0)
        {
            document.Version = CurrentVersion;
        }

        JsonSerializerOptions options = new() { WriteIndented = true };
        return JsonSerializer.Serialize(document, options);
    }

    /// <summary>解析；结构坏 / 不是对象 / 不是 json ⇒ <c>null</c>（调用方按"文件损坏"处理，绝不抛）。</summary>
    internal static ContentCatalogDocument? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };
            ContentCatalogDocument? document = JsonSerializer.Deserialize<ContentCatalogDocument>(json, options);
            if (document == null)
            {
                return null;
            }

            // 三个列表缺省时给空表（json 里可能只写了 version）。
            document.Cards ??= new List<ContentCatalogCard>();
            document.Relics ??= new List<ContentCatalogRelic>();
            document.Characters ??= new List<ContentCatalogCharacter>();
            document.Enchantments ??= new List<ContentCatalogEnchantment>();
            document.Counts ??= new ContentCatalogCounts();
            return document;
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
}

/// <summary>
/// 目录的**归一策略**（纯逻辑）：来源分类、条数汇总、图片路径推定。
/// 全部是"给定数据 ⇒ 给定结果"的纯函数，便于单测钉住（编辑器与本 mod 共用同一口径）。
/// </summary>
internal static class ContentCatalogPolicy
{
    /// <summary>来源 = 游戏本体。</summary>
    internal const string BaseSourceKey = "base";

    /// <summary>来源未知（拿不到程序集名；正常不该出现）。</summary>
    internal const string UnknownSourceKey = "unknown";

    /// <summary>
    /// 来源分类：程序集简单名与游戏本体一致（大小写不敏感）⇒ <see cref="BaseSourceKey"/>；
    /// 其余 ⇒ 该程序集简单名（通常就是 mod 的 dll 名）；拿不到 ⇒ <see cref="UnknownSourceKey"/>。
    /// </summary>
    internal static string ResolveSource(string? assemblyName, string? baseAssemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
        {
            return UnknownSourceKey;
        }

        if (!string.IsNullOrWhiteSpace(baseAssemblyName)
            && string.Equals(assemblyName, baseAssemblyName, StringComparison.OrdinalIgnoreCase))
        {
            return BaseSourceKey;
        }

        return assemblyName.Trim();
    }

    /// <summary>是不是"我们之外的东西"（编辑器按来源分组 / 玩家排查 mod 卡时用）。</summary>
    internal static bool IsFromMod(string? source)
    {
        return !string.IsNullOrWhiteSpace(source)
            && !string.Equals(source, BaseSourceKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 条数汇总（导出前由 <c>LocalContentCatalogExporter</c> 填进
    /// <see cref="ContentCatalogDocument.Counts"/>）。
    /// ⚠ 口径 = **列表长度**与**逐条 <see cref="ContentCatalogCard.Source"/>**：两者同进同退
    /// （别再算一遍"我枚举了几个"，否则去重后必然对不上）。
    /// </summary>
    internal static ContentCatalogCounts Count(ContentCatalogDocument document)
    {
        IReadOnlyList<ContentCatalogCard> cards = document.Cards;
        IReadOnlyList<ContentCatalogRelic> relics = document.Relics;
        IReadOnlyList<ContentCatalogCharacter> characters = document.Characters;
        IReadOnlyList<ContentCatalogEnchantment> enchantments = document.Enchantments;

        return new ContentCatalogCounts
        {
            Cards = cards.Count,
            Relics = relics.Count,
            Characters = characters.Count,
            Enchantments = enchantments.Count,
            ModCards = cards.Count((card) => IsFromMod(card.Source)),
            ModRelics = relics.Count((relic) => IsFromMod(relic.Source)),
            ModCharacters = characters.Count((character) => IsFromMod(character.Source)),
            ModEnchantments = enchantments.Count((enchantment) => IsFromMod(enchantment.Source)),
        };
    }

    /// <summary>一行摘要（日志 / 编辑器标题栏用）。</summary>
    internal static string DescribeCounts(ContentCatalogCounts counts)
    {
        return $"卡={counts.Cards}(其中 mod {counts.ModCards})"
            + $" / 遗物={counts.Relics}(其中 mod {counts.ModRelics})"
            + $" / 角色={counts.Characters}(其中 mod {counts.ModCharacters})"
            + $" / 附魔={counts.Enchantments}(其中 mod {counts.ModEnchantments})";
    }

    /// <summary>卡图图集条目路径里的目录标记（换算整图 png 的锚点）。</summary>
    internal const string CardAtlasDirectoryMarker = "/card_atlas.sprites/";

    /// <summary>
    /// 资源路径规范化：把 Godot 在 Windows 上给出的反斜杠统一成 <c>/</c>、去掉首尾空白。
    /// （实机里 mod 的立绘路径就有 <c>res://AncientsAwakened\images\card_portraits\x.png</c> 这种形态。）
    /// </summary>
    internal static string NormalizeResourcePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Replace('\\', '/');
    }

    /// <summary>这条资源路径是不是**能直读的图片**（整图），而不是需要裁图集的 <c>.tres</c>。</summary>
    internal static bool IsDirectImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string lower = path.Trim().ToLowerInvariant();
        return lower.EndsWith(".png", StringComparison.Ordinal)
            || lower.EndsWith(".jpg", StringComparison.Ordinal)
            || lower.EndsWith(".jpeg", StringComparison.Ordinal)
            || lower.EndsWith(".webp", StringComparison.Ordinal);
    }

    /// <summary>
    /// 由图集条目路径换算**整图 png 的推定路径**（游戏内部 <c>CardModel.PortraitPngPath</c> 同一条规则：
    /// <c>atlases/card_atlas.sprites/&lt;目录&gt;/&lt;名字&gt;.tres</c> ⇒ <c>packed/card_portraits/&lt;目录&gt;/&lt;名字&gt;.png</c>）。
    ///
    /// <para>⚠ 为什么**不能用卡池 entry 硬拼**（r275 实机踩到）：游戏那句用的是
    /// <c>CardPoolModel.Title.ToLowerInvariant()</c>（= <c>silent</c>），而 <c>Id.Entry</c> 是
    /// <c>SILENT_CARD_POOL</c> ⇒ 照 entry 拼出来的目录名**整批是错的**。这里改成**从游戏自己给的
    /// <paramref name="portraitPath"/> 上做路径换算**，天然与真实目录一致（含 <c>beta/</c> 子目录）。</para>
    ///
    /// <para>不是图集条目（不含 <see cref="CardAtlasDirectoryMarker"/>）⇒ 返回空串（**不给猜想**；
    /// 调用方还会用 <c>ResourceLoader.Exists</c> 复核，不存在也留空）。</para>
    /// </summary>
    internal static string CardPortraitPngHint(string? portraitPath)
    {
        string path = NormalizeResourcePath(portraitPath);
        int marker = path.IndexOf(CardAtlasDirectoryMarker, StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return string.Empty;
        }

        string tail = path.Substring(marker + CardAtlasDirectoryMarker.Length);
        if (tail.Length == 0)
        {
            return string.Empty;
        }

        if (tail.EndsWith(".tres", StringComparison.OrdinalIgnoreCase))
        {
            tail = tail.Substring(0, tail.Length - ".tres".Length);
        }

        return "res://images/packed/card_portraits/" + tail + ".png";
    }

    /// <summary>
    /// 遗物大图的**推定**路径（游戏内部 <c>RelicModel.BigIconPath</c> 同一条规则：
    /// <c>relics/&lt;entry 小写&gt;.png</c> 再套 <c>res://images/</c>）。
    /// ⚠ 游戏那个属性是 protected virtual 且基名可被覆写（<c>IconBaseName</c>）⇒ 推定会落空；
    /// 所以导出时会用 <c>ResourceLoader.Exists</c> 复核，**只有存在才写进** <see cref="ContentCatalogRelic.BigIcon"/>。
    /// </summary>
    internal static string RelicBigIconHint(string entry)
    {
        return "res://images/relics/" + entry.ToLowerInvariant() + ".png";
    }
}
