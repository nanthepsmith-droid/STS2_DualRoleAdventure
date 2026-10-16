#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using LocalMultiControl.Scripts.Runtime.PureLogic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 战灵召唤 · **「档位 × 角色预设」预设表**的运行时存取（预设战神 v2 的地基，r269）。
///
/// <para><b>两个来源（合并，2026-10-12 起）</b>：
/// <list type="number">
/// <item><b>玩家文件</b> <c>%APPDATA%\SlayTheSpire2\hellwar_presets.json</c>（编辑器 / 玩家手写的产物）—— 优先；</item>
/// <item><b>mod 内置</b> = 编译进 dll 的**嵌入资源** <c>Scripts/Data/hellwar_presets.json</c>
///   （随工坊包一起走，见 <see cref="BuiltinResourceName"/>）—— 用来"把预设当官方内容分发"。</item>
/// </list>
/// 同 <c>id</c> ⇒ 玩家胜出（内置那条整条丢弃）；顺序 = 玩家在前（同档多个 <c>default</c> 时玩家的先生效），
/// 见 <see cref="HellwarPresetMerge"/>。哪一份坏了就只用另一份（绝不让一份坏影响另一份）；
/// 两份都没有 / 都空 ⇒ 四档全用内置默认（行为与 v2 之前完全一致）。</para>
///
/// <para><b>本机可用性</b>：跨机器传的预设可能引用了本机没装的内容 ⇒
/// <see cref="UsableForTier"/> / <see cref="ResolveDefault"/> 会把这类预设**跳过**并点名
/// （见 <see cref="HellwarPresetCompat"/>），而不是召出一个残缺战神。</para>
///
/// <para><b>加载时机</b>（与 <see cref="LocalWakuuAutopilotConfig"/> 同款的三处，幂等、每次重读）：
/// ① entry-init（<c>Entry.Init</c>）；② run-launched（每局开始，玩家中途改文件下一局生效）；
/// ③ <c>ModelDb.Preload</c> 后（<see cref="HellwarContentPatch"/> 的自检锚点）。</para>
/// </summary>
internal static class LocalHellwarPresetStore
{
    /// <summary>内置预设的**嵌入资源名**（与 <c>LocalMultiControl.csproj</c> 的 <c>LogicalName</c> 必须一致）。</summary>
    internal const string BuiltinResourceName = "LocalMultiControl.Scripts.Data.hellwar_presets.json";

    private static readonly object _lock = new();
    private static HellwarPresetTable _table = HellwarPresetTable.Empty;
    private static bool _loaded;

    /// <summary>玩家那份预设表的完整路径（与 <c>vakuu_autopilot.json</c> / <c>personal_stats.json</c> 同目录）。</summary>
    internal static string PlayerFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SlayTheSpire2",
            HellwarPresetJson.FileName);

    /// <summary>当前缓存的归一结果（未加载过 = 空表）。</summary>
    internal static HellwarPresetTable Table
    {
        get
        {
            lock (_lock)
            {
                return _table;
            }
        }
    }

    /// <summary>读取并归一预设表（玩家文件 ∪ 内置资源）。幂等，可重复调用；任何异常都退回空表，绝不抛。</summary>
    internal static void Reload(string source)
    {
        lock (_lock)
        {
            try
            {
                List<HellwarPresetEntryData> playerEntries = ReadPlayerEntries();
                List<HellwarPresetEntryData> builtinEntries = ReadBuiltinEntries();
                List<HellwarPresetEntryData> merged = HellwarPresetMerge.Merge(playerEntries, builtinEntries);

                if (merged.Count == 0)
                {
                    _table = HellwarPresetTable.Empty;
                    if (!_loaded)
                    {
                        // 两边都没有是常态（没自搓过、也没内置）⇒ 只打一条 INFO 点名路径，别刷屏。
                        LocalMultiControlLogger.Info(
                            $"[地狱战神] 预设表为空（玩家文件 + 内置资源都没有内容），四档用内置默认。"
                            + $" 玩家文件={PlayerFilePath}, 内置资源={BuiltinResourceName}, source={source}");
                    }

                    _loaded = true;
                    return;
                }

                HellwarPresetTable table = HellwarPresetPolicy.Normalize(new HellwarPresetFileData(1, merged));
                _table = table;
                _loaded = true;

                LocalMultiControlLogger.Info(
                    $"[地狱战神] 预设表已加载: 套数={table.All.Count}"
                    + $"（玩家 {playerEntries.Count} 条 / 内置 {builtinEntries.Count} 条）, "
                    + $"玩家文件={PlayerFilePath}, source={source}"
                    + (table.Problems.Count == 0 ? "" : $", 问题={table.Problems.Count} 条（见后续 WARN）"));
                foreach (string problem in table.Problems)
                {
                    LocalMultiControlLogger.Warn($"[地狱战神] 预设表问题: {problem}");
                }
            }
            catch (Exception exception)
            {
                _table = HellwarPresetTable.Empty;
                _loaded = true;
                LocalMultiControlLogger.Warn(
                    $"[地狱战神] 预设表加载失败（四档回落内置默认）: 玩家文件={PlayerFilePath}, "
                    + $"error={exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// 某档的**默认预设**（单点召唤"自动"用它）：
    /// 文件该档的显式默认（可用时）→ 该档**第一个可用**的文件预设 → 内置该档。
    /// 绝不返回 null，也绝不抛。
    /// </summary>
    internal static HellwarWarGodPreset ResolveDefault(HellwarWarGodTier tier)
    {
        try
        {
            HellwarWarGodPreset builtin = HellwarWarGodPresets.Resolve(tier);
            IReadOnlyList<HellwarPresetDefinition> definitions = Table.ForTier(tier);
            if (definitions.Count == 0)
            {
                return builtin;
            }

            // 先看显式 default（归一保证同档只有一个 default 生效）。
            foreach (HellwarPresetDefinition definition in definitions)
            {
                if (definition.IsDefault)
                {
                    HellwarWarGodPreset? preset = TryResolveUsable(definition, tier, builtin, "自动默认");
                    if (preset != null)
                    {
                        return preset;
                    }

                    break;
                }
            }

            // 显式默认不可用（或压根没写）⇒ 该档第一个可用的。
            foreach (HellwarPresetDefinition definition in definitions)
            {
                HellwarWarGodPreset? preset = TryResolveUsable(definition, tier, builtin, "自动默认(回退)");
                if (preset != null)
                {
                    return preset;
                }
            }

            return builtin;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"[地狱战神] 预设解析异常（该档回落内置默认）: 档位={tier}, error={exception.GetType().Name}: {exception.Message}");
            return HellwarWarGodPresets.Resolve(tier);
        }
    }

    /// <summary>
    /// 某档**可用的**文件预设（继承映射后）：「选角色预设」的候选、【无尽战神】/【九连环】的随机池都用它。
    /// 引用了本机没有内容的预设在此处被剔除并点名（同条只报一次）。
    /// </summary>
    internal static IReadOnlyList<HellwarWarGodPreset> UsableForTier(HellwarWarGodTier tier, string context)
    {
        try
        {
            HellwarWarGodPreset builtin = HellwarWarGodPresets.Resolve(tier);
            List<HellwarWarGodPreset> list = new();
            foreach (HellwarPresetDefinition definition in Table.ForTier(tier))
            {
                HellwarWarGodPreset? preset = TryResolveUsable(definition, tier, builtin, context);
                if (preset != null)
                {
                    list.Add(preset);
                }
            }

            return list;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"[地狱战神] 预设枚举异常（返回空，走内置默认）: 档位={tier}, error={exception.GetType().Name}: {exception.Message}");
            return Array.Empty<HellwarWarGodPreset>();
        }
    }

    /// <summary>映射 + 可用性判定；不可用则点名并返回 <c>null</c>。</summary>
    private static HellwarWarGodPreset? TryResolveUsable(
        HellwarPresetDefinition definition,
        HellwarWarGodTier tier,
        HellwarWarGodPreset builtin,
        string context)
    {
        HellwarWarGodPreset preset = HellwarPresetRuntimeMapping.ToRuntimePreset(definition, tier, builtin);
        if (HellwarPresetCompat.IsUsable(preset, out string reason))
        {
            return preset;
        }

        HellwarPresetCompat.ReportSkipped(preset, reason, context);
        return null;
    }

    /// <summary>读玩家那份（文件不存在 = 空表；解析失败 = 空表 + WARN，不影响内置那份）。</summary>
    private static List<HellwarPresetEntryData> ReadPlayerEntries()
    {
        string path = PlayerFilePath;
        if (!File.Exists(path))
        {
            return new List<HellwarPresetEntryData>();
        }

        try
        {
            HellwarPresetFileData? data = HellwarPresetJson.Parse(File.ReadAllText(path));
            if (data == null)
            {
                LocalMultiControlLogger.Warn(
                    $"[地狱战神] 玩家预设文件解析失败（本次只用内置预设）: {path}");
                return new List<HellwarPresetEntryData>();
            }

            return data.Presets;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"[地狱战神] 玩家预设文件读取失败（本次只用内置预设）: {path}, "
                + $"error={exception.GetType().Name}: {exception.Message}");
            return new List<HellwarPresetEntryData>();
        }
    }

    /// <summary>读 mod 内置那份（嵌入资源；找不到 / 解析失败 = 空表，不影响玩家那份）。</summary>
    private static List<HellwarPresetEntryData> ReadBuiltinEntries()
    {
        try
        {
            using Stream? stream = typeof(LocalHellwarPresetStore).Assembly
                .GetManifestResourceStream(BuiltinResourceName);
            if (stream == null)
            {
                // 正常的：仓库里那份模板是空的 / 没被嵌入（构建配置问题会在这里显形，但绝不阻断）。
                return new List<HellwarPresetEntryData>();
            }

            using StreamReader reader = new(stream);
            HellwarPresetFileData? data = HellwarPresetJson.Parse(reader.ReadToEnd());
            if (data == null)
            {
                LocalMultiControlLogger.Warn(
                    $"[地狱战神] 内置预设资源解析失败（本次只用玩家预设）: {BuiltinResourceName}");
                return new List<HellwarPresetEntryData>();
            }

            if (data.Presets.Count > 0)
            {
                LocalMultiControlLogger.Info(
                    $"[地狱战神] 内置预设已读取: 条数={data.Presets.Count}（来源=嵌入资源 {BuiltinResourceName}）");
            }

            return data.Presets;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"[地狱战神] 内置预设资源读取失败（本次只用玩家预设）: {BuiltinResourceName}, "
                + $"error={exception.GetType().Name}: {exception.Message}");
            return new List<HellwarPresetEntryData>();
        }
    }
}
