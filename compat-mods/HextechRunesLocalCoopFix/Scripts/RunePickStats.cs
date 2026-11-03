using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace HextechRunesLocalCoopFix.Scripts;

/// <summary>
/// 符文**抓率统计**的运行时壳：读写 `%APPDATA%\SlayTheSpire2\hextech_rune_stats.json`，
/// 并在符文界面出现时给每个候选按钮挂"按下即记账"的一次性回调。
///
/// <para><b>口径</b>（用户 2026-10-11 点名）：照抄主 mod 的 `personal_stats.json` 思路 ——
/// 真人和瓦库的每一屏都记「候选出现过谁 / 最后选了谁」，瓦库随后按抓率挑（<see cref="RunePickRatePolicy"/>）。
/// **自选池屏（锻造选择：一屏一大堆符文）不记**，否则 offered 会被冲爆、抓率失去意义。</para>
///
/// <para><b>开关</b>：环境变量 `HRLF_RUNE_STATS=0` 关闭（关掉后瓦库回到"取最左"）。
/// 纯表现/偏好层：任何读写失败只 WARN 一次，绝不影响符文选择本身。</para>
/// </summary>
internal static class RunePickStats
{
    private const string FileName = "hextech_rune_stats.json";
    private const string AppFolderName = "SlayTheSpire2";

    private static readonly object Gate = new();

    /// <summary>每一屏只记一次（<c>RelicsSelected</c> 可能被重复驱动；按钮也可能被重按）。</summary>
    private static readonly ConditionalWeakTable<GodotObject, ScreenObservation> Screens = new();

    private static RunePickStatsData? _data;
    private static bool _loadAttempted;
    private static bool _warned;
    private static bool _selfPickWarned;

    private static readonly bool Disabled =
        string.Equals(System.Environment.GetEnvironmentVariable("HRLF_RUNE_STATS"), "0", StringComparison.Ordinal);

    private sealed class ScreenObservation
    {
        internal bool PickedRecorded;
    }

    internal static bool Enabled => !Disabled;

    /// <summary>符文界面出现时调用：记 offered + 给候选按钮挂"按下记 picked"。</summary>
    internal static void ObserveScreen(object? screen, ulong seat, string source)
    {
        if (!Enabled || screen is not GodotObject godotObject)
        {
            return;
        }

        try
        {
            if (!WakuuRuneAutoAnswer.TryReadCandidatesPublic(screen, out List<Button> buttons, out List<RelicModel> options, out bool selfPickMode))
            {
                return; // 没有候选（例如敌人 hex 预览 / 提示屏）
            }

            if (selfPickMode)
            {
                if (!_selfPickWarned)
                {
                    _selfPickWarned = true;
                    Log.Info($"{Entry.LogPrefix} 符文抓率：自选池屏（锻造选择等）不参与统计（一屏一大堆符文，记进去会冲爆 offered）。");
                }

                return;
            }

            if (options.Count == 0 || buttons.Count == 0)
            {
                return;
            }

            if (Screens.TryGetValue(godotObject, out _))
            {
                return; // 这一屏已经记过
            }

            var observation = new ScreenObservation();
            Screens.Add(godotObject, observation);

            var entries = new List<string>(options.Count);
            for (int i = 0; i < options.Count; i++)
            {
                entries.Add(EntryOf(options[i]));
            }

            // v1.8.0：把档位也打进日志 —— 真人席位的那一屏同样要能看出"三张是不是同档"
            //（用户 2026-10-31 的观察正是从"roll 前后档位变了"来的）。
            RuneRarityReader.TryReadRanks(godotObject, options, out int[] rarityRanks);

            RecordOffered(entries);

            for (int i = 0; i < buttons.Count && i < entries.Count; i++)
            {
                Button? button = buttons[i];
                if (!IsUsable(button))
                {
                    continue;
                }

                string entry = entries[i];
                GodotObject screenObject = godotObject;
                button.Pressed += () => RecordPicked(observation, screenObject, entry, seat);
            }

            Log.Info($"{Entry.LogPrefix} 符文抓率记账（候选）: seat={MainModBridge.SeatLabelById(seat)}({seat}), "
                + $"options=[{Describe(entries)}], 档位=[{RuneRarityPolicy.DescribeRanks(rarityRanks)}], source={source}");
        }
        catch (Exception exception)
        {
            WarnOnce($"符文抓率记账失败（不影响选择）: {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>瓦库挑符文时读账本；没记录过返回 null（= 按 0.5 先验参与比较）。</summary>
    internal static RunePickStat? Lookup(string entry)
    {
        if (!Enabled || string.IsNullOrEmpty(entry))
        {
            return null;
        }

        lock (Gate)
        {
            RunePickStatsData data = EnsureLoadedLocked();
            if (!data.Runes.TryGetValue(entry, out RunePickStat? stat))
            {
                return null;
            }

            return new RunePickStat { Offered = stat.Offered, Picked = stat.Picked };
        }
    }

    /// <summary>给日志用：`entry=抓率(选中/出现)`；没数据打 `-`。</summary>
    internal static string DescribeForLog(IReadOnlyList<string> entries)
    {
        List<string> parts = new(entries.Count);
        foreach (string entry in entries)
        {
            RunePickStat? stat = Lookup(entry);
            if (stat == null)
            {
                parts.Add($"{entry}=-");
                continue;
            }

            parts.Add($"{entry}={RunePickRatePolicy.Score(stat):0.00}({stat.Picked}/{stat.Offered})");
        }

        return string.Join(", ", parts);
    }

    private static void RecordOffered(IReadOnlyList<string> entries)
    {
        lock (Gate)
        {
            RunePickStatsData data = EnsureLoadedLocked();
            foreach (string entry in entries)
            {
                if (string.IsNullOrEmpty(entry) || entry == "unknown")
                {
                    continue;
                }

                data.Runes[entry] = RunePickRatePolicy.RecordOffered(
                    data.Runes.TryGetValue(entry, out RunePickStat? existing) ? existing : null);
            }

            SaveLocked(data);
        }
    }

    private static void RecordPicked(ScreenObservation observation, GodotObject screen, string entry, ulong seat)
    {
        if (!Enabled || observation.PickedRecorded)
        {
            return;
        }

        try
        {
            if (!IsUsable(screen))
            {
                return; // 界面已释放 ⇒ 连按多次的尾巴，忽略
            }

            if (string.IsNullOrEmpty(entry) || entry == "unknown")
            {
                return;
            }

            observation.PickedRecorded = true;

            lock (Gate)
            {
                RunePickStatsData data = EnsureLoadedLocked();
                data.Runes[entry] = RunePickRatePolicy.RecordPicked(
                    data.Runes.TryGetValue(entry, out RunePickStat? existing) ? existing : null);
                SaveLocked(data);

                RunePickStat stat = data.Runes[entry];
                Log.Info($"{Entry.LogPrefix} 符文抓率记账（选中）: seat={MainModBridge.SeatLabelById(seat)}({seat}), "
                    + $"picked={entry}, 累计={stat.Picked}/{stat.Offered}");
            }
        }
        catch (Exception exception)
        {
            WarnOnce($"符文抓率记账（选中）失败（不影响选择）: {exception.GetType().Name}: {exception.Message}");
        }
    }

    // ---- 文件 -------------------------------------------------------------------------------------

    internal static string FilePath()
    {
        string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, AppFolderName, FileName);
    }

    private static RunePickStatsData EnsureLoadedLocked()
    {
        if (_loadAttempted && _data != null)
        {
            return _data;
        }

        _loadAttempted = true;
        _data = Load();
        return _data;
    }

    private static RunePickStatsData Load()
    {
        try
        {
            string path = FilePath();
            if (!File.Exists(path))
            {
                return new RunePickStatsData();
            }

            string json = File.ReadAllText(path, Encoding.UTF8);
            RunePickStatsData data = RunePickRatePolicy.Parse(json);
            Log.Info($"{Entry.LogPrefix} 符文抓率统计已加载: 条目={data.Runes.Count}, path={path}");
            return data;
        }
        catch (Exception exception)
        {
            WarnOnce($"读取符文抓率统计失败（按空账本继续，行为=取最左）: {exception.GetType().Name}: {exception.Message}");
            return new RunePickStatsData();
        }
    }

    private static void SaveLocked(RunePickStatsData data)
    {
        try
        {
            string path = FilePath();
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = path + ".tmp";
            File.WriteAllText(temp, RunePickRatePolicy.Serialize(data), Encoding.UTF8);
            File.Move(temp, path, overwrite: true); // 原子替换：不会留下半截 JSON
        }
        catch (Exception exception)
        {
            WarnOnce($"写入符文抓率统计失败（本轮统计只在内存里）: {exception.GetType().Name}: {exception.Message}");
        }
    }

    // ---- 小工具 -----------------------------------------------------------------------------------

    private static string EntryOf(RelicModel? relic)
    {
        try
        {
            return relic?.Id.Entry ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string Describe(IReadOnlyList<string> entries)
    {
        return string.Join(", ", entries);
    }

    private static bool IsUsable(GodotObject? godotObject)
    {
        if (godotObject == null)
        {
            return false;
        }

        try
        {
            return GodotObject.IsInstanceValid(godotObject) && !godotObject.IsQueuedForDeletion();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static void WarnOnce(string message)
    {
        if (_warned)
        {
            return;
        }

        _warned = true;
        Log.Warn($"{Entry.LogPrefix} {message}");
    }
}
