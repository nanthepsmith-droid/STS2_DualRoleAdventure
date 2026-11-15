using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 托管出牌「选择器作用域在飞」的**租约账本**（r358）。
///
/// **为什么不是裸计数器**（2026-11-15 实机）：原来是 `int _selectorScopeInFlight` + `Interlocked`。
/// 出牌循环一旦**永久卡在某个 await 上**（上一局/上一场战斗结束时任务被拆掉，await 既不完成也不抛），
/// `finally` 就永不执行 ⇒ 计数只增不减。而新一局/新一场战斗**不会**重置它（只有选择器栈探针读它），
/// 于是整个后续进程里 `打 => 拒绝 selector-scope-busy`：
/// <code>瓦库看门狗调度统计: rejected=selector-scope-busy:19, selectorStackCount=0   ← 栈是空的，计数却是 4</code>
/// 后果（当日实机）：非并发档的席位（**战神**就是这一档）永远调度不上 ⇒ 它手上的牌永远打不掉 ⇒
/// 它这一席永远不 ready ⇒ `PlayersReadyToEndTurn.Count` 差一个 ⇒ 真人点「结束回合」毫无反应。
///
/// **本类的语义**：每个作用域是一条**带开始时刻的租约**。
/// <list type="bullet">
///   <item>`Acquire` 发租约；`Release` 按令牌收回 —— 已经被剪掉/被整体重置的令牌再 Release 是**空操作**
///   （绝不会把计数减成负数，这正是旧实现换成租约的收益）；</item>
///   <item>`CountActive` 会先把**超过 <c>staleAfterMs</c> 仍未归还**的租约剪掉再计数：那些任务已经不会返回，
///   继续让它们挡住别的席位只会把整桌拖死（与 `HostedWatchdogKeyPolicy.IsStuckInFlight` 同一条口径）；</item>
///   <item>`ResetAll` 给"换战斗 / 退局"这两个复位枢纽用，返回被清掉的条数（用于点名日志）。</item>
/// </list>
///
/// 纯逻辑（不碰 Godot / 游戏类型），便于单测。
/// </summary>
internal sealed class SelectorScopeLeaseBook
{
    private readonly Dictionary<long, long> _startedAtMs = new();
    private readonly object _gate = new();
    private long _seed;

    /// <summary>发一条租约，返回令牌。令牌只用于 <see cref="Release"/>。</summary>
    public long Acquire(long nowMs)
    {
        lock (_gate)
        {
            long token = ++_seed;
            _startedAtMs[token] = nowMs;
            return token;
        }
    }

    /// <summary>
    /// 收回租约，返回**剩余条数**（未剪枝）。令牌不在账上（已被剪 / 已整体重置）⇒ 空操作。
    /// </summary>
    public int Release(long token)
    {
        lock (_gate)
        {
            _startedAtMs.Remove(token);
            return _startedAtMs.Count;
        }
    }

    /// <summary>
    /// 有效在飞条数：先剪掉超过 <paramref name="staleAfterMs"/> 未归还的租约，再返回剩余条数。
    /// <paramref name="staleAfterMs"/> ≤ 0 表示不剪枝（只在单测/诊断里用）。
    /// </summary>
    public int CountActive(long nowMs, long staleAfterMs, out int prunedStale)
    {
        prunedStale = 0;
        lock (_gate)
        {
            if (staleAfterMs > 0 && _startedAtMs.Count > 0)
            {
                List<long>? expired = null;
                foreach (KeyValuePair<long, long> pair in _startedAtMs)
                {
                    long startedAtMs = pair.Value;
                    if (nowMs >= startedAtMs && nowMs - startedAtMs >= staleAfterMs)
                    {
                        (expired ??= new List<long>()).Add(pair.Key);
                    }
                }

                if (expired != null)
                {
                    foreach (long token in expired)
                    {
                        _startedAtMs.Remove(token);
                    }

                    prunedStale = expired.Count;
                }
            }

            return _startedAtMs.Count;
        }
    }

    /// <summary>未剪枝的条数（诊断用）。</summary>
    public int RawCount
    {
        get
        {
            lock (_gate)
            {
                return _startedAtMs.Count;
            }
        }
    }

    /// <summary>清空账本，返回清掉的条数（换战斗 / 退局复位用，>0 时调用方应点名）。</summary>
    public int ResetAll()
    {
        lock (_gate)
        {
            int cleared = _startedAtMs.Count;
            _startedAtMs.Clear();
            return cleared;
        }
    }
}
