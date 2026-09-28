using System.Collections.Generic;
using MegaCrit.Sts2.Core.Context;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 席位身份的**唯一取数源（薄适配层）**（R3）：把三个动态来源读成一份不可变快照
/// <see cref="SeatRegistry"/>，让调用点只问 `LocalSeatSource.CurrentSeats().IsForeground(id)` 这类问题，
/// 不再各自拼 `Session.CurrentControlledPlayerId ?? LocalContext.NetId ?? PrimaryPlayerId`
/// 或各自 `LocalPlayerIds.Contains(id)` 裸比。
///
/// 三个来源：<list type="bullet">
/// <item><see cref="LocalSelfCoopContext"/>：本地席位表 / 瓦库席位 / 联机机器人席位 / 主席位；</item>
/// <item>`LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId`：会话当前受控位；</item>
/// <item><c>LocalContext.NetId</c>：回环 sender 上下文（会为瓦库后台出牌临时漂移）。</item>
/// </list>
///
/// **缓存与命中校验**：这些读点里有些挂在每帧路径上（结束回合按钮自愈、回合开始抽牌判定），
/// 所以快照做了一份缓存，但**命中校验是权威的内容比对**（三个来源逐一比对，任一不同就重建）——
/// 这正是 R2 那轮（r170/r171）踩出来的规矩：缓存可以用，但"命中即正确"必须靠权威校验，
/// 不能靠 TTL 猜。因此**不需要**任何手工失效调用：写点改完来源后，下一次调用自然重建
/// （包括"写后读"：`ApplyControlContext` 改完 `LocalContext.NetId` 再读，看到的一定是新值）。
///
/// ⚠ 唯一新增的静态可变状态就是这个派生缓存（`SeatRegistry` 实例本身不可变）；
/// R5（生命周期契约）若要把静态态收进 Session，这里一并搬。
/// </summary>
internal static class LocalSeatSource
{
    private static SeatRegistry? _cachedSeats;

    /// <summary>当前席位快照（内容未变时返回同一实例）。</summary>
    internal static SeatRegistry CurrentSeats()
    {
        // 单次引用读（引用写在 .NET 上是原子的）：并发最坏情况是多建一份，不会读到半成品。
        SeatRegistry? cached = _cachedSeats;
        if (cached != null && IsUpToDate(cached))
        {
            return cached;
        }

        SeatRegistry fresh = Build();
        _cachedSeats = fresh;
        return fresh;
    }

    /// <summary>当前前台席位（受控位优先、其次上下文；0 = 都没有）。</summary>
    internal static ulong ForegroundSeatId() => CurrentSeats().ForegroundSeatId;

    /// <summary>
    /// 回环 sender 上下文席位（等价于旧写法 `LocalContext.NetId`；null = 未设置）。
    ///
    /// ⚠ **不要用 <see cref="ForegroundSeatId"/> 顶替它**：前台口径是「受控位优先」，
    /// 而我们的自动化作用域（奖励自动领取 / 商店自动采购 / 瓦库出牌看门狗）恰好是
    /// **把上下文对齐到归属者、受控位仍停在真人** —— 两种口径在那时会给出不同的 id。
    /// 旧代码写 `LocalContext.NetId` 的地方要的就是上下文值，这里必须逐字等价。
    /// </summary>
    internal static ulong? ContextSeatId() => CurrentSeats().ContextSeatId;

    /// <summary>
    /// 「上下文位 ?? 主席位」（等价于旧写法
    /// `LocalContext.NetId ?? LocalSelfCoopContext.PrimaryPlayerId`；0 = 两者都没有）。
    ///
    /// ⚠ 刻意**不是** <see cref="ForegroundSeatId"/>：旧口径里**没有受控位**这一层，
    /// 在「受控位已设、上下文未设」时两种口径会给出不同的 id（动作队列兜底要的就是旧口径，
    /// 见 <see cref="ContextSeatId"/> 的口径坑注释）。
    /// </summary>
    internal static ulong ContextOrPrimarySeatId()
        => CurrentSeats().ContextSeatId ?? CurrentSeats().PrimarySeatId;

    /// <summary>该席位是否前台（受控位优先、其次上下文）。</summary>
    internal static bool IsForegroundSeat(ulong seatId) => CurrentSeats().IsForeground(seatId);

    /// <summary>
    /// 该 id 是否就是回环 sender 上下文当前指向的席位（等价于旧写法
    /// `LocalContext.NetId == id`，0 与 null 都恒 false）。用于"上下文是否已经是对着这个人"
    /// 这类写前判定（奖励自动领取的作用域对齐）。
    /// </summary>
    internal static bool IsContextSeat(ulong seatId) => CurrentSeats().IsContext(seatId);

    /// <summary>该席位是否属于本地多控会话（第三方席位 / 0 都是 false）。</summary>
    internal static bool IsLocalSeat(ulong seatId) => CurrentSeats().IsLocalSeat(seatId);

    /// <summary>席位表自检摘要（空问题 = "席位表自检通过"），供会话初始化日志用。</summary>
    internal static string DescribeConflicts() => CurrentSeats().DescribeConflicts();

    private static SeatRegistry Build()
    {
        return SeatRegistry.Create(
            LocalSelfCoopContext.LocalPlayerIds,
            LocalSelfCoopContext.WakuuPlayerIds,
            LocalSelfCoopContext.CoopBotsPlayerIds,
            LocalSelfCoopContext.PrimaryPlayerId,
            LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId,
            LocalContext.NetId);
    }

    /// <summary>
    /// 命中校验：与三个来源逐一比对（标量比引用值、集合比内容）。
    /// 三个来源的集合都是**原地增删**（`_localPlayerIds.Clear()+AddRange`），
    /// 所以这里不能比引用、也不能比 TTL，只能比内容。
    /// </summary>
    private static bool IsUpToDate(SeatRegistry cached)
    {
        if (cached.ContextSeatId != LocalContext.NetId
            || cached.ControlledSeatId != LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId
            || cached.PrimarySeatId != LocalSelfCoopContext.PrimaryPlayerId)
        {
            return false;
        }

        return SequenceEquals(cached.LocalSeatIds, LocalSelfCoopContext.LocalPlayerIds)
            && SetEquals(cached.WakuuSeatIds, LocalSelfCoopContext.WakuuPlayerIds)
            && SetEquals(cached.CoopBotsSeatIds, LocalSelfCoopContext.CoopBotsPlayerIds);
    }

    private static bool SequenceEquals(IReadOnlyList<ulong> left, IReadOnlyList<ulong> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool SetEquals(IReadOnlyCollection<ulong> cached, IReadOnlyCollection<ulong> current)
    {
        if (cached.Count != current.Count)
        {
            return false;
        }

        foreach (ulong seatId in current)
        {
            if (!cached.Contains(seatId))
            {
                return false;
            }
        }

        return true;
    }
}
