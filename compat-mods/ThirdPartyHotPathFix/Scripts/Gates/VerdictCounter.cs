namespace ThirdPartyHotPathFix.Scripts.Gates;

/// <summary>
/// 「跳过 / 激活」判定的计数器 + 日志闸门（纯逻辑，可单测）。
///
/// 为什么需要：判定本身每秒跑上万次，**绝不能每次都打日志**；
/// 但只在「结论翻转」时打一行，就能从日志里一眼看出这条过滤链到底有没有被我们摘掉
/// （`跳过=N` 一路涨 / `激活=M` 说明用户真的在玩这家的内容，那就摘不掉）。
///
/// 双层闸门：
/// 1. 结论没变 ⇒ 直接返回 false（热路径几乎零成本）；
/// 2. 结论翻转但距上次打日志不足 <see cref="MinLogIntervalMs"/> ⇒ 也吞掉
///    （newsanguo 的「汉中」判据看的是**手牌**，一回合里反复抽/弃会让结论来回翻）。
/// </summary>
internal sealed class VerdictCounter
{
    private bool _hasLast;
    private bool _last;
    private long _lastLogTick = long.MinValue;

    /// <param name="minLogIntervalMs">两次日志之间的最小间隔（毫秒）；0 = 不限制（单测用）。</param>
    public VerdictCounter(int minLogIntervalMs = 2000)
    {
        MinLogIntervalMs = minLogIntervalMs;
    }

    public int MinLogIntervalMs { get; }

    public long Skipped { get; private set; }

    public long Armed { get; private set; }

    public long Total => Skipped + Armed;

    /// <summary>
    /// 记录一次判定；允许打日志时返回 true。
    /// </summary>
    public bool Record(bool skip)
    {
        if (skip)
        {
            Skipped++;
        }
        else
        {
            Armed++;
        }

        bool flipped = !_hasLast || skip != _last;
        _hasLast = true;
        _last = skip;
        if (!flipped)
        {
            return false;
        }

        if (MinLogIntervalMs <= 0)
        {
            return true;
        }

        long now = Environment.TickCount64;
        if (_lastLogTick != long.MinValue && now - _lastLogTick < MinLogIntervalMs)
        {
            return false;
        }

        _lastLogTick = now;
        return true;
    }

    public string Format() => $"跳过={Skipped} 激活={Armed}";
}
