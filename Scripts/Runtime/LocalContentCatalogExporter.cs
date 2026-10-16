#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>导出内容目录的结果（设置页用它决定按钮上显示什么）。</summary>
internal enum ContentCatalogExportResult
{
    /// <summary>已写盘。</summary>
    Exported,

    /// <summary>内容表还没就绪 ⇒ 已排队，<c>ModelDb.Preload</c> 之后自动补一次。</summary>
    Queued,

    /// <summary>失败（只 WARN，不影响游戏）。</summary>
    Failed,
}

/// <summary>
/// 「预设构筑器」的数据源导出（<c>TODO.md §预设构筑器</c> 的 **Phase B-3**）：
/// 把**本机运行期**的卡牌 / 遗物 / 角色 / 附魔（r277 起）枚举成 <c>content_catalog.json</c>
/// （<c>%APPDATA%\SlayTheSpire2\</c>，与 <c>hellwar_presets.json</c> 同目录）。
///
/// <para><b>为什么在游戏内导出</b>：读的是 <c>ModelDb</c>（运行时真值）⇒ **天然含第三方 mod 的卡 / 遗物 / 角色**
/// （这正是"编辑器要再兼容 mod"的关键，见 TODO 里的拍板②）；离线解析 <c>sts2src</c> 只作兜底。</para>
///
/// <para><b>就绪时序（踩过的坑：别在 Preload 之前碰卡池）</b>：
/// <c>ModelDb.Preload()</c> 是在 <c>OneTimeInitialization.ExecuteDeferred</c> 里跑的
/// （**主菜单显示之后**），在那之前读 <c>ModelDb.AllCards</c> 会触发 <c>GenerateAllCards</c> ⇒ 抛异常。
/// 所以本类自己维护一个"就绪位"：<see cref="MarkModelDbReady"/> 由 <c>HellwarContentPatch</c> 的
/// <c>ModelDb.Preload</c> 后置补丁点亮；设置页在那之前点了导出 ⇒ 只**排队**（<see cref="RequestExport"/>），
/// 就绪那一刻自动补一次（<see cref="MarkModelDbReady"/> 内 flush）。</para>
///
/// <para>⚠ 只读、幂等、**永不抛**：任何异常都只 WARN（导出失败不该影响游戏）。锚点 <c>[内容目录]</c>。</para>
/// </summary>
internal static class LocalContentCatalogExporter
{
    private const string LogTag = "[内容目录]";

    private static readonly object _lock = new();
    private static bool _modelDbReady;
    private static bool _pending;

    /// <summary>导出文件完整路径（与 <c>vakuu_autopilot.json</c> / <c>hellwar_presets.json</c> 同目录）。</summary>
    internal static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SlayTheSpire2",
            ContentCatalogJson.FileName);

    /// <summary>内容表（<c>ModelDb</c>）是否已就绪 —— 只有就绪后才敢枚举。</summary>
    internal static bool IsModelDbReady
    {
        get
        {
            lock (_lock)
            {
                return _modelDbReady;
            }
        }
    }

    /// <summary>
    /// <c>ModelDb.Preload()</c> 之后调（挂点 = <c>HellwarContentPatch</c> 的 <c>ModelDb.Preload</c> 后置补丁）：
    /// 点亮就绪位；若此前有人点过导出（排队中），当场补一次。
    /// </summary>
    internal static void MarkModelDbReady(string source)
    {
        bool flush;
        lock (_lock)
        {
            if (!_modelDbReady)
            {
                _modelDbReady = true;
                LocalMultiControlLogger.Info(
                    $"{LogTag} 内容表已就绪（设置页的「导出内容目录」现在可用）, source={source}");
            }

            flush = _pending;
            _pending = false;
        }

        if (flush)
        {
            LocalMultiControlLogger.Info($"{LogTag} 补做排队中的导出（点按钮时内容表还没就绪）");
            ExportNow($"{source}-flush-pending");
        }
    }

    /// <summary>
    /// 设置页点一次「导出内容目录」：就绪 ⇒ 立刻导出；未就绪 ⇒ 排队（<c>Preload</c> 后自动补）。
    /// 绝不抛。
    /// </summary>
    internal static ContentCatalogExportResult RequestExport(string source)
    {
        bool ready;
        lock (_lock)
        {
            ready = _modelDbReady;
            if (!ready)
            {
                _pending = true;
            }
        }

        if (!ready)
        {
            LocalMultiControlLogger.Info(
                $"{LogTag} 内容表尚未就绪（ModelDb 还没 Preload 完）⇒ 已排队，就绪后自动导出, source={source}");
            return ContentCatalogExportResult.Queued;
        }

        return ExportNow(source) ? ContentCatalogExportResult.Exported : ContentCatalogExportResult.Failed;
    }

    /// <summary>枚举 + 原子写盘。任何异常只 WARN 并返回 false。</summary>
    private static bool ExportNow(string source)
    {
        lock (_lock)
        {
            try
            {
                ContentCatalogDocument document = Build();
                document.Counts = ContentCatalogPolicy.Count(document);

                string directory = Path.GetDirectoryName(FilePath) ?? ".";
                Directory.CreateDirectory(directory);
                // 原子写（与 LocalPersonalRecorder.Save 同款）：先写 .tmp 再 Move 覆盖，
                // 中途失败不会留下半份文件给编辑器读。
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, ContentCatalogJson.Serialize(document));
                File.Move(temp, FilePath, overwrite: true);

                int withoutText = CountWithoutText(document);
                LocalMultiControlLogger.Info(
                    $"{LogTag} 已导出内容目录: {ContentCatalogPolicy.DescribeCounts(document.Counts)}"
                    + $", 缺名或描述={withoutText}, 文件={FilePath}, source={source}");

                // 缺文本的（第三方卡缺本地化键 / 描述解析不出）单独点一次名 —— 编辑器那几行会显示成空白，
                // 排查时先看这条，别去翻 json。
                if (withoutText > 0)
                {
                    LocalMultiControlLogger.Warn(
                        $"{LogTag} 有 {withoutText} 条卡 / 遗物读不出中文名或描述（多为第三方 mod 缺本地化键，"
                        + "json 里该字段是空串；编辑器会显示空白，不影响其余内容）");
                }

                return true;
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn(
                    $"{LogTag} 导出失败（不影响游戏）: {exception.GetType().Name}: {exception.Message}"
                    + $", 目标={FilePath}, source={source}");
                return false;
            }
        }
    }

    /// <summary>
    /// 游戏版本 = 游戏自己的 <c>ReleaseInfoManager</c>（读可执行文件旁的 <c>release_info.json</c>，实机值是
    /// <c>"v0.111.0"</c>）；读不到才回落程序集版本。
    ///
    /// <para>⚠ 别用 <c>Assembly.GetName().Version</c>：sts2.dll 的 AssemblyVersion 是 <c>0.1.0.0</c>
    /// （r275 首版导出踩到 ⇒ 这个字段等于没用，没法判断"这份目录是哪版游戏导的"）。</para>
    /// </summary>
    private static string ResolveGameVersion()
    {
        string fromRelease = Safe(
            () => MegaCrit.Sts2.Core.Debug.ReleaseInfoManager.Instance.ReleaseInfo?.Version ?? string.Empty,
            string.Empty);
        if (fromRelease.Length > 0)
        {
            return fromRelease;
        }

        return Safe(() => typeof(CardModel).Assembly.GetName().Version?.ToString() ?? string.Empty, string.Empty);
    }

    /// <summary>名字或描述至少缺一个的条数（卡 + 遗物；角色名空也算）。导出后点一次名，便于判读。</summary>
    private static int CountWithoutText(ContentCatalogDocument document)
    {
        int count = 0;
        foreach (ContentCatalogCard card in document.Cards)
        {
            if (card.Name.Length == 0 || card.Description.Length == 0)
            {
                count += 1;
            }
        }

        foreach (ContentCatalogRelic relic in document.Relics)
        {
            if (relic.Name.Length == 0 || relic.Description.Length == 0)
            {
                count += 1;
            }
        }

        foreach (ContentCatalogCharacter character in document.Characters)
        {
            if (character.Name.Length == 0)
            {
                count += 1;
            }
        }

        foreach (ContentCatalogEnchantment enchantment in document.Enchantments)
        {
            if (enchantment.Name.Length == 0 || enchantment.Description.Length == 0)
            {
                count += 1;
            }
        }

        return count;
    }

    /// <summary>枚举当前 <c>ModelDb</c> 的全部内容（卡 / 遗物 / 角色 / 附魔，含第三方 mod）。逐项容错。</summary>
    private static ContentCatalogDocument Build()
    {
        string baseAssembly = Safe(() => typeof(CardModel).Assembly.GetName().Name ?? string.Empty, string.Empty);
        ContentCatalogDocument document = new()
        {
            Version = ContentCatalogJson.CurrentVersion,
            GameVersion = ResolveGameVersion(),
            ExportedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        };

        int skippedCards = 0;
        HashSet<string> seenCards = new(StringComparer.Ordinal);
        foreach (CardModel card in Enumerate(ModelDb.AllCards, () => ModelDb.CardPool<EventCardPool>().AllCards))
        {
            string id = Safe(() => card.Id.ToString(), string.Empty);
            if (id.Length == 0 || !seenCards.Add(id))
            {
                continue;
            }

            try
            {
                document.Cards.Add(BuildCard(card, baseAssembly));
            }
            catch (Exception)
            {
                skippedCards += 1;
            }
        }

        int skippedRelics = 0;
        HashSet<string> seenRelics = new(StringComparer.Ordinal);
        foreach (RelicModel relic in Enumerate(ModelDb.AllRelics, () => ModelDb.RelicPool<EventRelicPool>().AllRelics))
        {
            string id = Safe(() => relic.Id.ToString(), string.Empty);
            if (id.Length == 0 || !seenRelics.Add(id))
            {
                continue;
            }

            try
            {
                document.Relics.Add(BuildRelic(relic, baseAssembly));
            }
            catch (Exception)
            {
                skippedRelics += 1;
            }
        }

        int skippedCharacters = 0;
        foreach (CharacterModel character in ModelDb.AllCharacters)
        {
            try
            {
                document.Characters.Add(BuildCharacter(character, baseAssembly));
            }
            catch (Exception)
            {
                skippedCharacters += 1;
            }
        }

        int skippedEnchantments = 0;
        HashSet<string> seenEnchantments = new(StringComparer.Ordinal);
        foreach (EnchantmentModel enchantment in ModelDb.DebugEnchantments)
        {
            // `ModelDb.DebugEnchantments` 是**唯一**的附魔全量入口（注释原文 "including mock ones for testing"）
            // ⇒ 必须按 `IsMock` 过滤掉测试用附魔；它基于 `AllAbstractModelSubtypes` ⇒ **mod 附魔也在里面**。
            string id = Safe(() => enchantment.Id.ToString(), string.Empty);
            if (enchantment.IsMock || id.Length == 0 || !seenEnchantments.Add(id))
            {
                continue;
            }

            try
            {
                document.Enchantments.Add(BuildEnchantment(enchantment, baseAssembly));
            }
            catch (Exception)
            {
                skippedEnchantments += 1;
            }
        }

        // 排序：让"同一次导出"稳定可 diff（池的枚举顺序不保证跨版本一致）。
        document.Cards.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
        document.Relics.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
        document.Characters.Sort((left, right) => string.CompareOrdinal(left.Entry, right.Entry));
        document.Enchantments.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));

        if (skippedCards > 0 || skippedRelics > 0 || skippedCharacters > 0 || skippedEnchantments > 0)
        {
            LocalMultiControlLogger.Warn(
                $"{LogTag} 有内容读不出字段（已跳过，不影响其余）: 卡={skippedCards} / 遗物={skippedRelics} "
                + $"/ 角色={skippedCharacters} / 附魔={skippedEnchantments}");
        }

        return document;
    }

    /// <summary>
    /// 枚举两份来源并去重（<see cref="Build"/> 里按 <c>ModelId</c> 去重）。
    /// 口径与 <c>HellwarWarGodRelics</c> 一致：<c>ModelDb.All*</c> **再显式并上 <c>Event*Pool</c>**
    /// —— 那是 mod 自定内容的登记池（它若已被覆盖则去重无副作用，防的是"mod 内容不在 All* 里"漏收）。
    /// </summary>
    private static IEnumerable<T> Enumerate<T>(IEnumerable<T> primary, Func<IEnumerable<T>> extra)
    {
        IEnumerable<T> merged;
        try
        {
            merged = primary.Concat(extra());
        }
        catch (Exception)
        {
            merged = primary;
        }

        foreach (T item in merged)
        {
            if (item is AbstractModel model && model.IsMock)
            {
                continue;
            }

            yield return item;
        }
    }

    private static ContentCatalogCard BuildCard(CardModel card, string baseAssembly)
    {
        string entry = card.Id.Entry;
        string pool = Safe(() => card.Pool.Id.Entry, string.Empty);
        string portrait = ContentCatalogPolicy.NormalizeResourcePath(
            Safe(() => card.PortraitPath, string.Empty));
        return new ContentCatalogCard
        {
            Id = card.Id.ToString(),
            Entry = entry,
            Name = Safe(() => card.Title, string.Empty).Trim(),
            Description = SafeCardDescription(card),
            Rarity = card.Rarity.ToString(),
            Type = card.Type.ToString(),
            Cost = Safe(() => card.EnergyCost.Canonical, -1),
            CostsX = Safe(() => card.EnergyCost.CostsX, false),
            StarCost = Safe(() => card.CanonicalStarCost, -1),
            Pool = pool,
            PoolTitle = Safe(() => card.Pool.Title, string.Empty),
            Portrait = portrait,
            PortraitPng = ResolveCardPortraitPng(portrait),
            HasPortrait = Safe(() => card.HasPortrait, false),
            Source = ContentCatalogPolicy.ResolveSource(
                Safe(() => card.GetType().Assembly.GetName().Name ?? string.Empty, string.Empty), baseAssembly),
        };
    }

    /// <summary>
    /// 卡图**整图**路径 —— 只在**能确定**时给（否则空串；编辑器优先用
    /// <see cref="ContentCatalogCard.Portrait"/>）：
    /// ① <c>Portrait</c> 本身就是图片（mod 覆写成 png / jpg，实机占多数）⇒ 原样给它；
    /// ② <c>Portrait</c> 是图集 <c>.tres</c> ⇒ 按路径换算成 <c>packed/card_portraits/…png</c>
    /// （<see cref="ContentCatalogPolicy.CardPortraitPngHint"/>），**再复核存在性**，不存在就留空。
    /// </summary>
    private static string ResolveCardPortraitPng(string portrait)
    {
        if (ContentCatalogPolicy.IsDirectImagePath(portrait))
        {
            return portrait;
        }

        string hint = ContentCatalogPolicy.CardPortraitPngHint(portrait);
        if (hint.Length == 0)
        {
            return string.Empty;
        }

        return Safe(() => Godot.ResourceLoader.Exists(hint), false) ? hint : string.Empty;
    }

    /// <summary>
    /// 卡面描述：优先走游戏自己的 <c>GetDescriptionForPile</c>（**会把动态变量 / 关键词后缀都展开**）；
    /// 它需要 <c>CombatManager.Instance</c>，而那是静态内联初始化的单例（永远非 null）⇒ 战斗外也安全；
    /// 万一解析不出（第三方卡缺本地化键等）⇒ 回落裸 <c>Description</c>，再不行给空串。
    /// </summary>
    private static string SafeCardDescription(CardModel card)
    {
        string text = Safe(() => card.GetDescriptionForPile(PileType.None), string.Empty);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = Safe(() => card.Description.GetFormattedText(), string.Empty);
        }

        return text.Trim();
    }

    private static ContentCatalogRelic BuildRelic(RelicModel relic, string baseAssembly)
    {
        string entry = relic.Id.Entry;
        string icon = ContentCatalogPolicy.NormalizeResourcePath(Safe(() => relic.IconPath, string.Empty));
        // 大图路径是按规则**推定**的（游戏那个属性 protected）⇒ 复核存在性，**不存在就不给路径**
        // （实机里 mod 遗物基本落空：1555 件只命中 306；给了假路径反而误导编辑器）。
        string bigIconHint = ContentCatalogPolicy.RelicBigIconHint(entry);
        bool hasBigIcon = Safe(() => Godot.ResourceLoader.Exists(bigIconHint), false);
        return new ContentCatalogRelic
        {
            Id = relic.Id.ToString(),
            Entry = entry,
            Name = Safe(() => relic.Title.GetFormattedText(), string.Empty).Trim(),
            // RelicModel.Description 是 private ⇒ 走公开的 DynamicDescription（描述 + 动态变量展开）。
            Description = Safe(() => relic.DynamicDescription.GetFormattedText(), string.Empty).Trim(),
            Rarity = relic.Rarity.ToString(),
            Pool = Safe(() => relic.Pool.Id.Entry, string.Empty),
            Icon = icon,
            BigIcon = hasBigIcon ? bigIconHint : string.Empty,
            HasIcon = Safe(() => Godot.ResourceLoader.Exists(icon), false),
            HasBigIcon = hasBigIcon,
            Source = ContentCatalogPolicy.ResolveSource(
                Safe(() => relic.GetType().Assembly.GetName().Name ?? string.Empty, string.Empty), baseAssembly),
        };
    }

    /// <summary>
    /// 一个附魔（r277）：给编辑器列"能给这张牌加什么附魔"用。
    /// `AllowedCardTypes` 是拿游戏自己的 `CanEnchantCardType` **逐个 CardType 问**出来的
    /// （全允许 ⇒ 清空 = 无限制；`CardType.None` 不问）。
    /// </summary>
    private static ContentCatalogEnchantment BuildEnchantment(EnchantmentModel enchantment, string baseAssembly)
    {
        string icon = ContentCatalogPolicy.NormalizeResourcePath(Safe(() => enchantment.IconPath, string.Empty));
        // 游戏的 IconPath **有 missing 兜底**（`enchantments/missing_enchantment.png`）⇒ 兜底了就当"没有图标"，
        // 别把那张 missing 图当成本附魔的图标发给编辑器。
        bool hasIcon = icon.Length > 0
            && !string.Equals(icon, EnchantmentModel.MissingIconPath, StringComparison.OrdinalIgnoreCase)
            && Safe(() => Godot.ResourceLoader.Exists(icon), false);

        CardType[] allTypes = Enum.GetValues<CardType>();
        List<string> allowed = new();
        foreach (CardType type in allTypes)
        {
            if (type == CardType.None)
            {
                continue;
            }

            if (Safe(() => enchantment.CanEnchantCardType(type), false))
            {
                allowed.Add(type.ToString());
            }
        }

        if (allowed.Count >= allTypes.Length - 1)
        {
            // 全部都允许 ⇒ 空列表 = 无类型限制（编辑器按"任意"显示即可）。
            allowed.Clear();
        }

        return new ContentCatalogEnchantment
        {
            Id = enchantment.Id.ToString(),
            Entry = enchantment.Id.Entry,
            Name = Safe(() => enchantment.Title.GetFormattedText(), string.Empty).Trim(),
            // EnchantmentModel.Description 是 private ⇒ 走公开的 DynamicDescription（描述 + 数量占位展开）。
            Description = Safe(() => enchantment.DynamicDescription.GetFormattedText(), string.Empty).Trim(),
            Icon = hasIcon ? icon : string.Empty,
            HasIcon = hasIcon,
            ShowAmount = Safe(() => enchantment.ShowAmount, false),
            AllowedCardTypes = allowed,
            Source = ContentCatalogPolicy.ResolveSource(
                Safe(() => enchantment.GetType().Assembly.GetName().Name ?? string.Empty, string.Empty), baseAssembly),
        };
    }

    private static ContentCatalogCharacter BuildCharacter(CharacterModel character, string baseAssembly)
    {
        ContentCatalogCharacter result = new()
        {
            Id = character.Id.ToString(),
            Entry = character.Id.Entry,
            Name = Safe(() => character.Title.GetFormattedText(), string.Empty).Trim(),
            IsPlayable = Safe(() => character.IsPlayable, false),
            Source = ContentCatalogPolicy.ResolveSource(
                Safe(() => character.GetType().Assembly.GetName().Name ?? string.Empty, string.Empty), baseAssembly),
        };

        // 初始卡组：**保留重复**（预设 deck 的语义就是"写几遍就是几张"）。
        List<CardModel>? deck = Safe(() => character.StartingDeck.ToList(), null);
        if (deck != null)
        {
            foreach (CardModel card in deck)
            {
                string entry = Safe(() => card.Id.Entry, string.Empty);
                if (entry.Length > 0)
                {
                    result.StartingDeck.Add(entry);
                }
            }
        }

        IReadOnlyList<RelicModel>? relics = Safe(() => character.StartingRelics, null);
        if (relics != null)
        {
            foreach (RelicModel relic in relics)
            {
                string entry = Safe(() => relic.Id.Entry, string.Empty);
                if (entry.Length > 0)
                {
                    result.StartingRelics.Add(entry);
                }
            }
        }

        return result;
    }

    /// <summary>取值兜底：拿不到（第三方内容缺键 / 属性抛）就用 fallback，绝不冒穿整次导出。</summary>
    private static T Safe<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}
