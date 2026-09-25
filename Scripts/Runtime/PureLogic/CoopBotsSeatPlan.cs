using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 席位驱动三态（POC 版单一来源）：
/// <list type="bullet">
/// <item><see cref="Human"/>：真人自己操作（现状默认）；</item>
/// <item><see cref="Wakuu"/>：本 mod 内置瓦库托管（<c>LocalSelfCoopContext.WakuuPlayerIds</c>）；</item>
/// <item><see cref="CoopBots"/>：第三方 mod「Co-op Bots」接管（<c>CoopBots.AutoPilot.Set(netId, true)</c>）。</item>
/// </list>
/// 三态**互斥**：一个席位同时只允许一种驱动（详见《Co-op_Bots联机队友兼容可行性分析》§2.1）。
/// 选人屏的三态循环钮（Phase 1）会直接复用 <see cref="SeatDriverModes.Next"/>。
/// </summary>
internal enum SeatDriverMode
{
    Human = 0,

    Wakuu = 1,

    CoopBots = 2,
}

/// <summary>席位驱动三态的取值与判定（纯逻辑，无游戏依赖）。</summary>
internal static class SeatDriverModes
{
    /// <summary>
    /// 按两个集合判定席位驱动。**联机机器人优先**：两侧都命中时以 CB 为准
    /// （调用方仍应先做互斥清理并告警，这里只保证判定是确定性的、不会两边都算）。
    /// </summary>
    public static SeatDriverMode Classify(bool wakuuDriven, bool coopBotsDriven)
    {
        if (coopBotsDriven)
        {
            return SeatDriverMode.CoopBots;
        }

        return wakuuDriven ? SeatDriverMode.Wakuu : SeatDriverMode.Human;
    }

    /// <summary>三态循环（真人 → 瓦库 → 联机机器人 → 真人），供每席循环钮使用。</summary>
    public static SeatDriverMode Next(SeatDriverMode mode) => mode switch
    {
        SeatDriverMode.Human => SeatDriverMode.Wakuu,
        SeatDriverMode.Wakuu => SeatDriverMode.CoopBots,
        _ => SeatDriverMode.Human,
    };

    /// <summary>中文名（日志 / UI 文案共用，避免各处各写一份）。</summary>
    public static string Describe(SeatDriverMode mode) => mode switch
    {
        SeatDriverMode.Wakuu => "瓦库",
        SeatDriverMode.CoopBots => "联机机器人",
        _ => "真人",
    };

    /// <summary>配置文本取值（human / wakuu / coopbots）；非法返回 null。</summary>
    public static SeatDriverMode? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "human" => SeatDriverMode.Human,
            "wakuu" => SeatDriverMode.Wakuu,
            "coopbots" => SeatDriverMode.CoopBots,
            _ => null,
        };
    }
}

/// <summary>
/// 互斥清理结果（纯逻辑，不落盘）：<see cref="Wakuu"/> 已剔除与联机机器人重复的席位，
/// <see cref="Conflicts"/> 是被剔除的那批（调用方据此打 WARN 日志）。
/// </summary>
internal sealed record SeatDriverAssignment(
    List<ulong> Wakuu,
    List<ulong> CoopBots,
    List<ulong> Conflicts);

/// <summary>
/// 「联机机器人席位」清单的纯逻辑：解析配置文本 / 收敛到合法席位 / 与瓦库名单互斥。
///
/// POC 阶段席位来源是配置文件里的一行文本（<c>vakuu_autopilot.json</c> 的 <c>coopBotsSeats</c>），
/// Phase 1 换成选人屏三态钮 + 存档标记后，本文件仍复用（解析与互斥口径不变）。
/// </summary>
internal static class CoopBotsSeatPlan
{
    /// <summary>席位上限（与 <see cref="LocalSelfCoopContext.MaxLocalPlayerCount"/> 一致，此处单独写死以保持纯逻辑无运行期依赖）。</summary>
    public const int MaxSeatCount = 12;

    /// <summary>允许的分隔符：逗号 / 分号 / 竖线 / 空白（配置是人手改的，尽量宽容）。</summary>
    private static readonly char[] Separators = { ',', ';', '|', ' ', '\t', '\r', '\n' };

    /// <summary>
    /// 解析席位文本（如 <c>"2,3"</c>）。
    /// <list type="bullet">
    /// <item>非法 token（非数字 / 0 / 超出 <paramref name="allowedSeats"/>）**静默跳过**——配置文件损坏不该让进局失败；</item>
    /// <item>去重；顺序按 <paramref name="allowedSeats"/> 的顺序（取不到时按席位号升序），保证日志与接管顺序稳定。</item>
    /// </list>
    /// </summary>
    public static List<ulong> Parse(string? raw, IReadOnlyList<ulong>? allowedSeats = null, int maxCount = MaxSeatCount)
    {
        List<ulong> seats = new List<ulong>();
        if (string.IsNullOrWhiteSpace(raw) || maxCount <= 0)
        {
            return seats;
        }

        HashSet<ulong> seen = new HashSet<ulong>();
        foreach (string token in raw.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!ulong.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seat) || seat == 0)
            {
                continue;
            }

            if (allowedSeats != null && !allowedSeats.Contains(seat))
            {
                continue;
            }

            if (!seen.Add(seat))
            {
                continue;
            }

            seats.Add(seat);
            if (seats.Count >= maxCount)
            {
                break;
            }
        }

        return SortBySeatOrder(seats, allowedSeats);
    }

    /// <summary>反序列化（写回配置 / 日志用）。空集合返回空串。</summary>
    public static string Format(IEnumerable<ulong> seats)
    {
        List<ulong> ordered = seats.Distinct().Where((seat) => seat != 0).OrderBy((seat) => seat).ToList();
        return string.Join(",", ordered);
    }

    /// <summary>
    /// 互斥收敛：**联机机器人优先**，把同时出现在两边的席位从瓦库名单里剔除并报进
    /// <see cref="SeatDriverAssignment.Conflicts"/>（调度侧据此打 WARN）。
    /// 顺序一律按席位号升序，便于日志比对。
    /// </summary>
    public static SeatDriverAssignment ResolveDrivers(
        IEnumerable<ulong> wakuuSeats,
        IEnumerable<ulong> coopBotsSeats)
    {
        List<ulong> coopBots = coopBotsSeats.Where((seat) => seat != 0).Distinct().OrderBy((seat) => seat).ToList();
        HashSet<ulong> coopBotsSet = coopBots.ToHashSet();

        List<ulong> wakuu = new List<ulong>();
        List<ulong> conflicts = new List<ulong>();
        foreach (ulong seat in wakuuSeats.Where((seat) => seat != 0).Distinct().OrderBy((seat) => seat))
        {
            if (coopBotsSet.Contains(seat))
            {
                conflicts.Add(seat);
                continue;
            }

            wakuu.Add(seat);
        }

        return new SeatDriverAssignment(wakuu, coopBots, conflicts);
    }

    /// <summary>每席 `<c>seat=N(驱动)</c>` 的分配摘要（进局日志用，直接可肉眼核对）。</summary>
    public static string DescribeAssignment(
        IEnumerable<ulong> seats,
        Func<ulong, SeatDriverMode> resolveMode)
    {
        List<string> parts = seats
            .Distinct()
            .OrderBy((seat) => seat)
            .Select((seat) => $"seat={seat}({SeatDriverModes.Describe(resolveMode(seat))})")
            .ToList();
        return parts.Count > 0 ? string.Join(", ", parts) : "（无）";
    }

    private static List<ulong> SortBySeatOrder(List<ulong> seats, IReadOnlyList<ulong>? allowedSeats)
    {
        if (allowedSeats == null || allowedSeats.Count == 0)
        {
            seats.Sort();
            return seats;
        }

        Dictionary<ulong, int> rank = new Dictionary<ulong, int>();
        for (int index = 0; index < allowedSeats.Count; index++)
        {
            rank.TryAdd(allowedSeats[index], index);
        }

        seats.Sort((left, right) => Rank(left, rank).CompareTo(Rank(right, rank)));
        return seats;
    }

    private static int Rank(ulong seat, Dictionary<ulong, int> rank)
    {
        return rank.TryGetValue(seat, out int index) ? index : int.MaxValue;
    }
}
