using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 一个席位的身份快照（R3，纯逻辑）。
///
/// 为什么要它：本 mod 里"这个 id 是谁"曾有四路各自猜 —— `LocalContext.NetId`（回环 sender 上下文）、
/// `Session.CurrentControlledPlayerId`（会话受控位）、反射读 synchronizer 的 `_localPlayerId`、
/// 以及直接比 `player.NetId`。四路并存时，同一个判断在不同补丁里口径可以不同，
/// 出问题只能靠日志倒推。R3 把这些收成「问 <see cref="SeatRegistry"/>」一件事。
///
/// 本类型只描述事实，不含任何判定策略（策略在各调用点，用它的属性组合表达）。
/// </summary>
/// <param name="SeatId">席位 id（0 = 未知/未指定）。</param>
/// <param name="IsLocalSeat">是否在本 mod 的本地席位表里（<c>LocalSelfCoopContext.LocalPlayerIds</c>）。</param>
/// <param name="IsPrimary">是否主席位（1 号位，即游戏认定的"本机玩家"）。</param>
/// <param name="IsControlled">是否是会话当前受控席位（<c>Session.CurrentControlledPlayerId</c>）。</param>
/// <param name="IsContext">是否是"回环 sender 上下文"席位（<c>LocalContext.NetId</c>）。</param>
/// <param name="IsForeground">是否是前台席位（受控位优先、其次上下文 —— 与 <c>TryGetForegroundPlayer</c> 同口径）。</param>
/// <param name="Driver">驱动三态（真人 / 瓦库 / 联机机器人）。</param>
internal readonly record struct SeatIdentity(
    ulong SeatId,
    bool IsLocalSeat,
    bool IsPrimary,
    bool IsControlled,
    bool IsContext,
    bool IsForeground,
    SeatDriverMode Driver)
{
    /// <summary>id 为 0（没拿到身份）—— 与"非本地席位"是两回事。</summary>
    internal bool IsUnknown => SeatId == 0UL;

    /// <summary>能在局里查到、但不属于本 mod 本地席位的玩家（真联机玩家 / 第三方加进来的席位）。</summary>
    internal bool IsRemotePlayer => !IsUnknown && !IsLocalSeat;

    /// <summary>日志/诊断用的紧凑描述（例：<c>seat=100(本地,驱动=瓦库,前台)</c>）。</summary>
    internal string Describe()
    {
        if (IsUnknown)
        {
            return "seat=0(未知)";
        }

        string scope = IsLocalSeat ? "本地" : "非本地";
        List<string> parts = new() { scope, $"驱动={SeatDriverModes.Describe(Driver)}" };
        if (IsPrimary)
        {
            parts.Add("主席位");
        }

        if (IsForeground)
        {
            parts.Add("前台");
        }

        if (IsControlled)
        {
            parts.Add("受控");
        }

        return $"seat={SeatId}({string.Join(",", parts)})";
    }
}

/// <summary>席位表自检发现的问题类型（纯逻辑；调用方据此打 WARN / Info）。</summary>
internal enum SeatConflictKind
{
    /// <summary>席位表里出现了 0（占位符不该被当成席位）。</summary>
    ZeroSeat = 0,

    /// <summary>本地席位表里有重复 id。</summary>
    DuplicateLocalSeat = 1,

    /// <summary>主席位不在本地席位表里。</summary>
    PrimaryNotLocalSeat = 2,

    /// <summary>瓦库席位不在本地席位表里（孤儿驱动席位）。</summary>
    WakuuNotLocalSeat = 3,

    /// <summary>联机机器人席位不在本地席位表里（孤儿驱动席位）。</summary>
    CoopBotsNotLocalSeat = 4,

    /// <summary>同一席位同时被瓦库与联机机器人标了驱动 —— 违反三态互斥。</summary>
    WakuuAndCoopBots = 5,
}

/// <summary>一条自检问题：类型 + 涉及的席位 id（0 表示与具体席位无关）。</summary>
internal readonly record struct SeatConflict(SeatConflictKind Kind, ulong SeatId)
{
    internal string Describe() => Kind switch
    {
        SeatConflictKind.ZeroSeat => "席位表含占位 0（三个来源列表里任一出现即为该项）",
        SeatConflictKind.DuplicateLocalSeat => $"本地席位表重复: seat={SeatId}",
        SeatConflictKind.PrimaryNotLocalSeat => $"主席位不在本地席位表: seat={SeatId}",
        SeatConflictKind.WakuuNotLocalSeat => $"瓦库席位不在本地席位表: seat={SeatId}",
        SeatConflictKind.CoopBotsNotLocalSeat => $"联机机器人席位不在本地席位表: seat={SeatId}",
        SeatConflictKind.WakuuAndCoopBots => $"席位同时被瓦库与联机机器人驱动（违反三态互斥）: seat={SeatId}",
        _ => $"seat={SeatId}",
    };
}
