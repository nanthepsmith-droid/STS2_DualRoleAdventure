namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库四功能 · ④「**瓦库的爹**」（遗物 + 三卡框架）的纯逻辑判据（无游戏依赖，可单测）。
///
/// 命名口径（2026-10-06 用户拍板）：<c>地狱战神</c> 这个名字留给**战灵召唤**（临时玩家召唤，另一件功能）；
/// 本条（遗物 + 我挡 / 你攻 / 合体 三张占位牌）叫**瓦库的爹**。首版只做"遗物 + 卡框架"，
/// **三张牌的效果留空**（见 <c>Scripts/Models/Cards/LocalWakuuDaddy*Card.cs</c>）。
///
/// 来源：`decision-records/瓦库炼化净化联合地狱战神-功能提案与可行性分析.md` §5
/// + `瓦库四功能-可行性核验报告.md` §7 拍板附录第 ④ 条。
/// </summary>
internal static class WakuuDaddyPolicy
{
    /// <summary>战斗开始时遗物给的手牌张数（我挡 / 你攻 / 合体 各 1 张）。</summary>
    internal const int CardsPerCombat = 3;

    /// <summary>
    /// 某席位是否应获得【瓦库的爹】遗物 = 功能开关开 **且** 该席位属于本地多控会话 **且** 它不是瓦库托管席位。
    ///
    /// 为什么只发给真人席位（提案 §5.1 原话「真人玩家开局获得」）：
    /// 瓦库席位身上已经有托管遗物（【瓦库形态】/【永久低语耳环】），再叠一件"每场战斗往手里塞 3 张牌"的遗物，
    /// 只会给托管出牌添乱（那 3 张是给人做决策的牌，托管 AI 拿它们没有意义）。
    /// </summary>
    internal static bool ShouldGrantRelic(bool featureEnabled, bool isLocalSessionSeat, bool isWakuuSeat)
    {
        return featureEnabled && isLocalSessionSeat && !isWakuuSeat;
    }
}
