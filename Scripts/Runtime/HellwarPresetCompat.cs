#nullable enable
using System;
using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 预设的**本机可用性**（运行时部分）：把 <see cref="HellwarPresetAvailability"/> 的纯判据接到
/// <c>ModelDb</c> 的角色 / 卡 / 遗物查表上，并负责"同一条只报一次"的日志。
///
/// <para><b>取向</b>：预设是跨机器传的（随 mod 分发 / 别人分享）。写预设的人装了某个 mod、用了它的角色与卡；
/// 没装那个 mod 的玩家拿到这份预设时，旧行为是"逐项降级"召出一个残缺战神（玩家不知道发生了什么）
/// ⇒ 现在改成**跳过这条预设**（不进候选、不进随机池、不当默认），其余预设照常
/// （2026-10-12 用户口径）。</para>
///
/// <para>⚠ <b>失败要"开放"不要"封闭"</b>：如果可用性探测本身出问题（<c>ModelDb</c> 没就绪 / 查表抛），
/// 一律按"全部可用"处理 —— 最坏退回旧的逐项降级行为，**绝不能因为探测器坏了就把玩家的预设全隐藏**。</para>
/// </summary>
internal static class HellwarPresetCompat
{
    private const string LogTag = "[地狱战神]";

    private static readonly object _lock = new();
    private static readonly HashSet<string> _reported = new(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string>? _characters;
    private static bool _probed;
    private static bool _degraded;

    /// <summary>
    /// 这条预设在本机可不可用。不可用时 <paramref name="reason"/> 给一句人话（用于日志点名）。
    /// <para>内置默认（<c>PresetId</c> 为空）永远可用：它们是编译期写死的主体 / 召唤者内容，不可能是"没装的 mod"。</para>
    /// </summary>
    internal static bool IsUsable(HellwarWarGodPreset preset, out string reason)
    {
        reason = string.Empty;
        if (preset.PresetId.Length == 0)
        {
            return true;
        }

        EnsureProbed();
        if (_degraded)
        {
            return true;
        }

        HellwarPresetAvailabilityResult result = HellwarPresetAvailability.Evaluate(
            preset.SkinKey,
            preset.DeckText,
            preset.RelicEntries,
            CharacterExists,
            CardExists,
            RelicExists);

        reason = result.Reason;
        return result.Usable;
    }

    /// <summary>点名一条被跳过的预设（**同一 key 整个进程只报一次**，避免每次召唤刷屏）。</summary>
    internal static void ReportSkipped(HellwarWarGodPreset preset, string reason, string context)
    {
        string key = preset.PresetId.Length > 0
            ? $"id:{preset.PresetId}"
            : $"{preset.Tier}|{preset.SkinKey}|{preset.DisplayName}";

        lock (_lock)
        {
            if (!_reported.Add(key))
            {
                return;
            }
        }

        LocalMultiControlLogger.Warn(
            $"{LogTag} 预设已跳过（引用了本机没有的内容，其余预设照常）: {Describe(preset)}, "
            + $"原因={reason}, 场景={context}");
    }

    /// <summary>一次探测：把三张查表建起来；任何一步抛 ⇒ 标降级（按"全部可用"处理）。</summary>
    private static void EnsureProbed()
    {
        lock (_lock)
        {
            if (_probed)
            {
                return;
            }

            _probed = true;
            try
            {
                HashSet<string> characters = new(StringComparer.OrdinalIgnoreCase);
                foreach (CharacterModel character in ModelDb.AllCharacters)
                {
                    characters.Add(character.Id.Entry);
                }

                _characters = characters;

                // 触发表构建（探针 entry 查不到是正常的；只要不抛就说明卡/遗物表可用）。
                _ = HellwarWarGodDecks.LookupByEntry("__preset_availability_probe__");
                _ = HellwarWarGodRelics.LookupByEntry("__preset_availability_probe__");
            }
            catch (Exception exception)
            {
                _degraded = true;
                LocalMultiControlLogger.Warn(
                    $"{LogTag} 预设可用性探测失败（本次按「全部可用」处理，退回逐项降级的老行为）: "
                    + $"{exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private static bool CharacterExists(string entry)
        => _characters != null && _characters.Contains(entry);

    private static bool CardExists(string entry) => HellwarWarGodDecks.LookupByEntry(entry) != null;

    private static bool RelicExists(string entry) => HellwarWarGodRelics.LookupByEntry(entry) != null;

    private static string Describe(HellwarWarGodPreset preset)
    {
        string name = preset.DisplayName.Length > 0 ? preset.DisplayName : preset.SkinKey;
        return preset.PresetId.Length > 0 ? $"{name}({preset.PresetId})" : name;
    }
}
