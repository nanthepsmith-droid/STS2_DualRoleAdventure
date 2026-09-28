using System;
using System.Collections.Generic;
using System.Linq;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 席位身份的**唯一取数入口**（R3，纯逻辑）。
///
/// 用法：调用点不再自己拼 `CurrentControlledPlayerId ?? LocalContext.NetId`、
/// 也不再各自 `LocalPlayerIds.Contains(id)` 裸比，而是构造一次快照后问它：
/// <list type="bullet">
/// <item><see cref="IsLocalSeat"/>：是不是我们的本地席位（第三方席位 / 真联机玩家 ⇒ false）；</item>
/// <item><see cref="IsPrimary"/>：是不是主席位（游戏认定的"本机玩家"）；</item>
/// <item><see cref="IsControlled"/>/<see cref="IsContext"/>/<see cref="IsForeground"/>：当前受控位 / 回环 sender 上下文 / 前台；</item>
/// <item><see cref="DriverOf"/>/<see cref="IsWakuuDriven"/>/<see cref="IsCoopBotsDriven"/>：这一席由谁驱动（三态互斥，联机机器人优先）；</item>
/// <item><see cref="Resolve"/>：拿一份完整快照（日志 / 诊断用）。</item>
/// </list>
///
/// **纯逻辑**：不依赖 Godot / 游戏类型，四个原始输入都由调用方传进来
/// （本地席位表 / 瓦库席位 / 联机机器人席位 / 主席位 / 受控席位 / 上下文席位）。
/// 这样"身份口径"能被单测钉住，而不是散在补丁里靠实机验证。
///
/// ⚠ **R3 第一轮只提供入口，不接线**：现有调用点一个都没改（行为零变化）。
/// 接线按 ADR 分批做，每批一局实机。
/// </summary>
internal sealed class SeatRegistry
{
    private readonly List<ulong> _localSeatIds;
    private readonly HashSet<ulong> _localSeatSet;
    private readonly HashSet<ulong> _wakuuSeats;
    private readonly HashSet<ulong> _coopBotsSeats;
    private readonly ulong _primarySeatId;
    private readonly ulong? _controlledSeatId;
    private readonly ulong? _contextSeatId;
    private readonly List<SeatConflict> _conflicts;

    private SeatRegistry(
        List<ulong> localSeatIds,
        HashSet<ulong> wakuuSeats,
        HashSet<ulong> coopBotsSeats,
        ulong primarySeatId,
        ulong? controlledSeatId,
        ulong? contextSeatId,
        List<SeatConflict> conflicts)
    {
        _localSeatIds = localSeatIds;
        _localSeatSet = localSeatIds.ToHashSet();
        _wakuuSeats = wakuuSeats;
        _coopBotsSeats = coopBotsSeats;
        _primarySeatId = primarySeatId;
        _controlledSeatId = controlledSeatId;
        _contextSeatId = contextSeatId;
        _conflicts = conflicts;
    }

    /// <summary>本地席位表（已归一化：丢 0、去重、保持传入顺序）。</summary>
    internal IReadOnlyList<ulong> LocalSeatIds => _localSeatIds;

    /// <summary>瓦库席位（已丢 0；驱动三态判定用，也给命中校验 / 日志用）。</summary>
    internal IReadOnlyCollection<ulong> WakuuSeatIds => _wakuuSeats;

    /// <summary>联机机器人席位（已丢 0；同上）。</summary>
    internal IReadOnlyCollection<ulong> CoopBotsSeatIds => _coopBotsSeats;

    /// <summary>主席位（0 = 未指定）。</summary>
    internal ulong PrimarySeatId => _primarySeatId;

    /// <summary>会话当前受控席位（null = 会话未初始化 / 无受控位）。</summary>
    internal ulong? ControlledSeatId => _controlledSeatId;

    /// <summary>回环 sender 上下文席位（<c>LocalContext.NetId</c>；null = 未设置）。</summary>
    internal ulong? ContextSeatId => _contextSeatId;

    /// <summary>前台席位 = 受控位优先、其次上下文（0 = 两者都空）。与 <c>TryGetForegroundPlayer</c> 同口径。</summary>
    internal ulong ForegroundSeatId => _controlledSeatId ?? _contextSeatId ?? 0UL;

    /// <summary>前台席位，两者都空时回退主席位（部分调用点要的就是这个口径）。</summary>
    internal ulong ForegroundOrPrimarySeatId => ForegroundSeatId != 0UL ? ForegroundSeatId : _primarySeatId;

    /// <summary>席位表自检结果（互斥违反 / 孤儿驱动席位 / 重复 id 等），按类型与 id 稳定排序。</summary>
    internal IReadOnlyList<SeatConflict> Conflicts => _conflicts;

    /// <summary>
    /// 构造快照。<paramref name="localSeatIds"/> 传 null 视为空表；三张表里的 0 会被丢掉（并记为自检问题）。
    /// </summary>
    internal static SeatRegistry Create(
        IEnumerable<ulong>? localSeatIds,
        IEnumerable<ulong>? wakuuSeatIds,
        IEnumerable<ulong>? coopBotsSeatIds,
        ulong primarySeatId,
        ulong? controlledSeatId = null,
        ulong? contextSeatId = null)
    {
        List<ulong> rawLocal = localSeatIds?.ToList() ?? new List<ulong>();
        List<ulong> normalizedLocal = new();
        HashSet<ulong> seenLocal = new();
        bool sawZero = rawLocal.Any(id => id == 0UL)
            || (wakuuSeatIds != null && wakuuSeatIds.Any(id => id == 0UL))
            || (coopBotsSeatIds != null && coopBotsSeatIds.Any(id => id == 0UL));
        bool sawDuplicate = false;

        foreach (ulong id in rawLocal)
        {
            if (id == 0UL)
            {
                continue;
            }

            if (!seenLocal.Add(id))
            {
                sawDuplicate = true;
                continue;
            }

            normalizedLocal.Add(id);
        }

        HashSet<ulong> wakuu = wakuuSeatIds?.Where(id => id != 0UL).ToHashSet() ?? new HashSet<ulong>();
        HashSet<ulong> coopBots = coopBotsSeatIds?.Where(id => id != 0UL).ToHashSet() ?? new HashSet<ulong>();

        List<SeatConflict> conflicts = new();
        if (sawZero)
        {
            conflicts.Add(new SeatConflict(SeatConflictKind.ZeroSeat, 0UL));
        }

        if (sawDuplicate)
        {
            foreach (ulong id in FindDuplicates(rawLocal))
            {
                conflicts.Add(new SeatConflict(SeatConflictKind.DuplicateLocalSeat, id));
            }
        }

        if (primarySeatId != 0UL && !seenLocal.Contains(primarySeatId))
        {
            conflicts.Add(new SeatConflict(SeatConflictKind.PrimaryNotLocalSeat, primarySeatId));
        }

        foreach (ulong id in wakuu.Where(id => !seenLocal.Contains(id)).OrderBy(id => id))
        {
            conflicts.Add(new SeatConflict(SeatConflictKind.WakuuNotLocalSeat, id));
        }

        foreach (ulong id in coopBots.Where(id => !seenLocal.Contains(id)).OrderBy(id => id))
        {
            conflicts.Add(new SeatConflict(SeatConflictKind.CoopBotsNotLocalSeat, id));
        }

        foreach (ulong id in wakuu.Where(coopBots.Contains).OrderBy(id => id))
        {
            conflicts.Add(new SeatConflict(SeatConflictKind.WakuuAndCoopBots, id));
        }

        conflicts.Sort(static (left, right) =>
            left.Kind != right.Kind ? left.Kind.CompareTo(right.Kind) : left.SeatId.CompareTo(right.SeatId));

        return new SeatRegistry(normalizedLocal, wakuu, coopBots, primarySeatId, controlledSeatId, contextSeatId, conflicts);
    }

    /// <summary>是不是本 mod 的本地席位（0 与第三方席位都是 false）。</summary>
    internal bool IsLocalSeat(ulong seatId) => seatId != 0UL && _localSeatSet.Contains(seatId);

    /// <summary>是不是主席位。</summary>
    internal bool IsPrimary(ulong seatId) => seatId != 0UL && seatId == _primarySeatId;

    /// <summary>是不是会话当前受控席位（受控位为空时恒 false —— 不要拿它当"前台"用）。</summary>
    internal bool IsControlled(ulong seatId) => seatId != 0UL && _controlledSeatId == seatId;

    /// <summary>是不是回环 sender 上下文席位（<c>LocalContext.NetId</c>）。</summary>
    internal bool IsContext(ulong seatId) => seatId != 0UL && _contextSeatId == seatId;

    /// <summary>是不是前台席位（受控位优先、其次上下文）。</summary>
    internal bool IsForeground(ulong seatId) => seatId != 0UL && ForegroundSeatId == seatId;

    /// <summary>这一席由谁驱动；非本地席位 / 未知席位返回 null（不要当成"真人"）。</summary>
    internal SeatDriverMode? DriverOf(ulong seatId)
    {
        if (!IsLocalSeat(seatId))
        {
            return null;
        }

        return SeatDriverModes.Classify(_wakuuSeats.Contains(seatId), _coopBotsSeats.Contains(seatId));
    }

    /// <summary>这一席是否瓦库托管（非本地席位恒 false —— 与旧 `IsWakuuEnabled` 口径一致）。</summary>
    internal bool IsWakuuDriven(ulong seatId) => DriverOf(seatId) == SeatDriverMode.Wakuu;

    /// <summary>这一席是否被联机机器人接管（非本地席位恒 false —— 与旧 `IsCoopBotsDriven` 口径一致）。</summary>
    internal bool IsCoopBotsDriven(ulong seatId) => DriverOf(seatId) == SeatDriverMode.CoopBots;

    /// <summary>取完整身份快照（永不返回 null，未知 id 得到 <see cref="SeatIdentity.IsUnknown"/>）。</summary>
    internal SeatIdentity Resolve(ulong seatId)
    {
        bool isLocalSeat = IsLocalSeat(seatId);
        return new SeatIdentity(
            seatId,
            isLocalSeat,
            IsPrimary(seatId),
            IsControlled(seatId),
            IsContext(seatId),
            IsForeground(seatId),
            isLocalSeat ? SeatDriverModes.Classify(_wakuuSeats.Contains(seatId), _coopBotsSeats.Contains(seatId)) : SeatDriverMode.Human);
    }

    /// <summary>把自检结果拼成一行（空 = 没有问题）；调用方据此打 WARN。</summary>
    internal string DescribeConflicts()
    {
        return _conflicts.Count == 0
            ? "席位表自检通过"
            : string.Join("; ", _conflicts.Select(conflict => conflict.Describe()));
    }

    private static IEnumerable<ulong> FindDuplicates(IReadOnlyList<ulong> ids)
    {
        HashSet<ulong> seen = new();
        List<ulong> duplicates = new();
        foreach (ulong id in ids)
        {
            if (id == 0UL || seen.Add(id))
            {
                continue;
            }

            if (!duplicates.Contains(id))
            {
                duplicates.Add(id);
            }
        }

        duplicates.Sort();
        return duplicates;
    }
}
