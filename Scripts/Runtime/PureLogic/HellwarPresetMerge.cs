#nullable enable
using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime.PureLogic;

/// <summary>
/// 两份预设表的**合并**（纯逻辑）：**玩家文件**（<c>%APPDATA%\SlayTheSpire2\hellwar_presets.json</c>）
/// 与 **mod 内置**（编译进 dll 的嵌入资源，见 <c>LocalHellwarPresetStore</c>）。
///
/// <list type="bullet">
/// <item>同 <c>id</c> ⇒ **玩家胜出**：内置那条**整条丢弃**（不是改名 —— 改名会变成两套，玩家会看到重复）；</item>
/// <item><c>id</c> 为空的条目两边都保留（归一时各自编号，见 <see cref="HellwarPresetPolicy.Normalize"/>）；</item>
/// <item>顺序 = **玩家在前、内置在后**。这条很关键：归一里"同档多个 <c>default</c> ⇒ 第一个生效"，
///   玩家在前 ⇒ 玩家的默认预设天然优先，不用额外写覆盖逻辑。</item>
/// </list>
///
/// ⚠ 两份**各自**解析失败时由调用方保证"只用另一份"（这里只做合并，不碰 IO）。
/// </summary>
internal static class HellwarPresetMerge
{
    internal static List<HellwarPresetEntryData> Merge(
        IReadOnlyList<HellwarPresetEntryData>? player,
        IReadOnlyList<HellwarPresetEntryData>? builtin)
    {
        List<HellwarPresetEntryData> merged = new();
        HashSet<string> playerIds = new(StringComparer.OrdinalIgnoreCase);

        if (player != null)
        {
            foreach (HellwarPresetEntryData entry in player)
            {
                merged.Add(entry);

                string id = (entry.Id ?? string.Empty).Trim();
                if (id.Length > 0)
                {
                    // 玩家自己重复的 id 不在这里处理（归一会点名并改名），只用来"压住内置的同名条目"。
                    playerIds.Add(id);
                }
            }
        }

        if (builtin != null)
        {
            foreach (HellwarPresetEntryData entry in builtin)
            {
                string id = (entry.Id ?? string.Empty).Trim();
                if (id.Length > 0 && playerIds.Contains(id))
                {
                    continue;
                }

                merged.Add(entry);
            }
        }

        return merged;
    }
}
