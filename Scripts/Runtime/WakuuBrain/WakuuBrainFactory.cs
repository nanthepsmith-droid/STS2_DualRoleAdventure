namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库大脑工厂：按配置（vakuuBrain 开关）返回大脑实例。
/// heuristic=原「最左可打牌」逻辑（默认，行为零变化）；scored=启发式评分（§18.2）；
/// auto 先探测可用求解器（未来 CombatSolver 适配器，反射探测未命中即静默降级），
/// 未探测到一律回退启发式——行为与 heuristic 完全相同。
/// </summary>
internal static class WakuuBrainFactory
{
    private static readonly IWakuuCombatBrain Heuristic = new HeuristicWakuuBrain();

    private static readonly IWakuuCombatBrain Scored = new ScoredWakuuBrain();

    /// <summary>当前生效的大脑（主循环每轮调用；创建后缓存，配置变更在下次加载时生效）。</summary>
    private static IWakuuCombatBrain? _current;

    public static IWakuuCombatBrain Create()
    {
        if (_current != null)
        {
            return _current;
        }

        _current = LocalWakuuAutopilotConfig.BrainMode switch
        {
            LocalWakuuAutopilotConfig.ScoredBrainMode => Scored,
            LocalWakuuAutopilotConfig.AutoBrainMode => TryCreateAuto() ?? Heuristic,
            _ => Heuristic,
        };

        LocalMultiControlLogger.Info($"瓦库大脑就绪: mode={LocalWakuuAutopilotConfig.BrainMode}, id={_current.Id}");
        return _current;
    }

    /// <summary>重置缓存（配置 Reload 时调用，让新开关值生效）。</summary>
    public static void Reset()
    {
        _current = null;
    }

    private static IWakuuCombatBrain? TryCreateAuto()
    {
        // 未来：反射探测 CombatSolver 适配器（IsAvailable=false → null → 回退启发式）。
        // 参照瓦库托管优化可行性分析 21.3.4：未安装 / Entry.Enabled=false / 战斗 Players.Count != 1 → false。
        return null;
    }
}
