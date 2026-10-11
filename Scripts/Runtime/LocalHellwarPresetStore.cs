#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LocalMultiControl.Scripts.Runtime.PureLogic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 战灵召唤 · **「档位 × 角色预设」预设表**的运行时存取（预设战神 v2 的地基，r269）。
///
/// <para><b>职责</b>：读 <c>%APPDATA%\SlayTheSpire2\hellwar_presets.json</c>（编辑器 / 玩家手写的产物），
/// 解析归一（<see cref="HellwarPresetJson"/> + <see cref="HellwarPresetPolicy"/>），
/// 给召唤链提供「某档的默认预设 / 某档的全部预设」。**文件不存在 / 为空 / 全坏 ⇒ 四档全用内置默认
/// （行为与 v2 之前完全一致）**——所以本类是纯加法，不改变任何既有口径。</para>
///
/// <para><b>加载时机</b>（与 <see cref="LocalWakuuAutopilotConfig"/> 同款的三处，幂等、每次重读）：
/// ① entry-init（<c>Entry.Init</c>）；② run-launched（每局开始，玩家中途改文件下一局生效）；
/// ③ <c>ModelDb.Preload</c> 后（<see cref="HellwarContentPatch"/> 的自检锚点，那时卡/遗物 id 才可校验）。</para>
///
/// <para><b>Phase A 边界（2026-10-11 拍板顺序的另一半）</b>：本阶段预设表只接**单点召唤**
/// （选了档位 ⇒ 用该档默认预设）；【无尽战神】/【九连环】的批量随机池仍走内置档位表
/// （<see cref="HellwarWarGodPresets.PickRandom"/>），接文件池与「召唤时选预设」UI 一起放在下一 phase。</para>
/// </summary>
internal static class LocalHellwarPresetStore
{
    private static readonly object _lock = new();
    private static HellwarPresetTable _table = HellwarPresetTable.Empty;
    private static bool _loaded;

    /// <summary>预设表文件完整路径（与 <c>vakuu_autopilot.json</c> / <c>personal_stats.json</c> 同目录）。</summary>
    internal static string FilePath =>
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

    /// <summary>读取并归一预设表。幂等，可重复调用（每次重读文件）；任何异常都退回空表，绝不抛。</summary>
    internal static void Reload(string source)
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    _table = HellwarPresetTable.Empty;
                    if (!_loaded)
                    {
                        // 文件不存在是常态（没自搓过），只打一条 INFO 点名路径，别刷屏。
                        LocalMultiControlLogger.Info(
                            $"[地狱战神] 预设表不存在，四档用内置默认（编辑器/手写的产物放这里）: {FilePath}, source={source}");
                    }

                    _loaded = true;
                    return;
                }

                HellwarPresetTable table = HellwarPresetPolicy.Normalize(HellwarPresetJson.Parse(File.ReadAllText(FilePath)));
                _table = table;
                _loaded = true;

                LocalMultiControlLogger.Info(
                    $"[地狱战神] 预设表已加载: 套数={table.All.Count}, 文件={FilePath}, source={source}"
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
                    $"[地狱战神] 预设表加载失败（四档回落内置默认）: {FilePath}, error={exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    /// <summary>
    /// 某档的**默认预设**（单点召唤用它）：文件该档默认预设（继承映射后）→ 无则内置该档。
    /// 绝不返回 null，也绝不抛。
    /// </summary>
    internal static HellwarWarGodPreset ResolveDefault(HellwarWarGodTier tier)
    {
        try
        {
            HellwarPresetTable table = Table;
            HellwarPresetDefinition? definition = table.DefaultForTier(tier);
            if (definition == null)
            {
                return HellwarWarGodPresets.Resolve(tier);
            }

            return HellwarPresetRuntimeMapping.ToRuntimePreset(definition, tier, HellwarWarGodPresets.Resolve(tier));
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"[地狱战神] 预设解析异常（该档回落内置默认）: 档位={tier}, error={exception.GetType().Name}: {exception.Message}");
            return HellwarWarGodPresets.Resolve(tier);
        }
    }

    /// <summary>某档在预设表里的全部预设（继承映射后；文件没配 = 空）。给下一 phase 的「选预设」UI 用。</summary>
    internal static IReadOnlyList<HellwarWarGodPreset> AllForTier(HellwarWarGodTier tier)
    {
        try
        {
            HellwarPresetTable table = Table;
            if (table.All.Count == 0)
            {
                return Array.Empty<HellwarWarGodPreset>();
            }

            HellwarWarGodPreset builtin = HellwarWarGodPresets.Resolve(tier);
            return table.ForTier(tier)
                .Select((definition) => HellwarPresetRuntimeMapping.ToRuntimePreset(definition, tier, builtin))
                .ToList();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"[地狱战神] 预设枚举异常（返回空，走内置默认）: 档位={tier}, error={exception.GetType().Name}: {exception.Message}");
            return Array.Empty<HellwarWarGodPreset>();
        }
    }
}
