using System.Collections.Generic;
using System.Linq;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 药水稀有度（语义与游戏 `MegaCrit.Sts2.Core.Entities.Potions.PotionRarity` 一一对应）。
/// 运行时按 **显式映射** 赋值（见 `LocalWakuuPotionAutoUse.MapDumpRarity`），**不做数值强转** ——
/// 游戏若新增档位，映射会落到 <see cref="None"/>（= 不参与腾栏），而不是静默串档。
/// </summary>
internal enum WakuuPotionRarity
{
    None = 0,
    Common = 1,
    Uncommon = 2,
    Rare = 3,
    Event = 4,
    Token = 5,
}

/// <summary>触发「腾栏」的遗物（谁是这一轮腾栏策略的来源）。</summary>
internal enum WakuuPotionDumpCause
{
    None = 0,
    /// <summary>白兽雕像：每场战斗结束必掉药水（掉落稀有度只可能是 Common/Uncommon/Rare）。</summary>
    WhiteBeastStatue = 1,
    /// <summary>娇嫩蕨草：每场战斗开始时用随机药水填满空栏 ⇒ 战斗中尽量把 common/uncommon 用掉。</summary>
    DelicateFrond = 2,
    /// <summary>石化蟾蜍：每场战斗开始时发一瓶药水石头 ⇒ 只需腾出一个空栏。</summary>
    PetrifiedToad = 3,
}

/// <summary>
/// 腾栏计划：需要腾几个空栏、允许用到哪一档稀有度、是否"尽量腾空"。
/// 由玩家身上的三件遗物推导（<see cref="WakuuPotionDumpPolicy.Resolve"/>）。
/// </summary>
internal readonly record struct WakuuPotionDumpPlan(
    WakuuPotionDumpCause Cause,
    int SlotsToFree,
    bool DumpAll,
    WakuuPotionRarity MaxRarity)
{
    /// <summary>没有相关遗物（不腾栏）。</summary>
    public static WakuuPotionDumpPlan Inactive => new(WakuuPotionDumpCause.None, 0, false, WakuuPotionRarity.None);

    public bool IsActive => Cause != WakuuPotionDumpCause.None;

    /// <summary>用于日志/锚点的中文来源名。</summary>
    public string CauseName => Cause switch
    {
        WakuuPotionDumpCause.WhiteBeastStatue => "白兽雕像",
        WakuuPotionDumpCause.DelicateFrond => "娇嫩蕨草",
        WakuuPotionDumpCause.PetrifiedToad => "石化蟾蜍",
        _ => "无",
    };
}

/// <summary>
/// 瓦库自动用药的「遗物驱动腾栏」纯逻辑（2026-11-10 用户口径）：
///
/// 1) <b>白兽雕像</b> —— 战斗结束必定掉药水（原版掉落稀有度 = Common/Uncommon/Rare，Event 不会掉）
///    ⇒ 战斗中即使规则条件不满足，也按 **稀有度从低到高** 用掉一瓶，直到留出 1 个空栏；
/// 2) <b>娇嫩蕨草</b> —— 每场战斗开始时把空栏用随机药水填满
///    ⇒ 战斗中尽量把 **Common/Uncommon** 用掉（腾空越多，下回合开战补得越多）；
///    注意：蕨草先把空栏填满，蟾蜍那瓶石头随后无处可放 ⇒ 两者不叠加；
/// 3) <b>石化蟾蜍</b> —— 每场战斗开始时发一瓶药水石头
///    ⇒ 只用 **Common** 腾出 1 个空栏。
///
/// 三者同时存在时取**最强的一档**（蕨草 &gt; 白兽 &gt; 蟾蜍）：蕨草要把 common/uncommon 全用掉，
/// 本身就覆盖了另外两件"留 1 个空格"的需求。
///
/// **"无视环境"的边界（2026-11-10 用户修订）**：本文件只算"腾几个格、哪档稀有度可以动"。
/// 运行时对 **Common 档直接喝**；**Uncommon/Rare 档必须通过它自己那条规则的判定**
/// （相位/首回合/附加条件/昏眩都照旧），**只把战斗范围放宽到 AnyCombat**（精英/Boss → 普通怪也用）。
/// 否则"把好东西盲喝掉"比留着更亏 —— 用户原话「无视环境喝药有点没用」。
///
/// 明确排除：Event / Token / None 稀有度（Event 掉落不到；石头是 Token）、
/// 自动生效类药水（瓶中精灵 `Usage=Automatic`，无法主动使用）—— 见运行时过滤。
/// </summary>
internal static class WakuuPotionDumpPolicy
{
    /// <summary>按遗物与否推导腾栏计划（三件遗物的优先级：蕨草 &gt; 白兽 &gt; 蟾蜍）。</summary>
    public static WakuuPotionDumpPlan Resolve(bool whiteBeastStatue, bool delicateFrond, bool petrifiedToad)
    {
        // 蕨草：把 common/uncommon 尽量用掉（DumpAll）——同时也满足另外两件"留一个空格"的需求
        if (delicateFrond)
        {
            return new WakuuPotionDumpPlan(WakuuPotionDumpCause.DelicateFrond, 0, true, WakuuPotionRarity.Uncommon);
        }

        // 白兽雕像：最低稀有度优先、允许到 Rare（Event 仍然排除 = Rank 0）
        if (whiteBeastStatue)
        {
            return new WakuuPotionDumpPlan(WakuuPotionDumpCause.WhiteBeastStatue, 1, false, WakuuPotionRarity.Rare);
        }

        // 石化蟾蜍：只动 Common
        if (petrifiedToad)
        {
            return new WakuuPotionDumpPlan(WakuuPotionDumpCause.PetrifiedToad, 1, false, WakuuPotionRarity.Common);
        }

        return WakuuPotionDumpPlan.Inactive;
    }

    /// <summary>
    /// 该稀有度在本次计划里的「腾栏优先级」：0 = 不参与腾栏；1/2/3 = Common/Uncommon/Rare
    /// （数字越小越先被用掉）。
    /// </summary>
    public static int RankOf(WakuuPotionDumpPlan plan, WakuuPotionRarity rarity)
    {
        if (!plan.IsActive)
        {
            return 0;
        }

        if (rarity is WakuuPotionRarity.None or WakuuPotionRarity.Event or WakuuPotionRarity.Token)
        {
            return 0;
        }

        return (int)rarity <= (int)plan.MaxRarity ? (int)rarity : 0;
    }

    /// <summary>
    /// 本次腾栏目标要留出多少空栏：
    /// `DumpAll`（蕨草）⇒ 全部栏位都算目标（即"能空多少空多少"）；否则 = 计划里的固定值。
    /// </summary>
    public static int ResolveNeededSlots(WakuuPotionDumpPlan plan, int maxSlots)
    {
        if (!plan.IsActive)
        {
            return 0;
        }

        return plan.DumpAll ? maxSlots : plan.SlotsToFree;
    }

    /// <summary>现在还要不要继续腾栏（已经有足够空栏就停）。</summary>
    public static bool ShouldDump(WakuuPotionDumpPlan plan, int rank, int openSlots, int neededSlots)
    {
        return rank > 0 && NeedsDump(plan, openSlots, neededSlots);
    }

    /// <summary>本轮是否需要腾栏（不看具体候选，只看"还缺几个空栏"）。</summary>
    public static bool NeedsDump(WakuuPotionDumpPlan plan, int openSlots, int neededSlots)
    {
        return plan.IsActive && neededSlots > 0 && openSlots < neededSlots;
    }

    /// <summary>
    /// 把候选药水按「稀有度从低到高、同档按栏位顺序」排序，返回**栏位号**序列。
    /// 输入 = (栏位号, 优先级) 列表；优先级 0 的项应在上游就被过滤掉（这里也直接忽略）。
    /// </summary>
    public static IReadOnlyList<int> OrderCandidates(IReadOnlyList<(int Slot, int Rank)> candidates)
    {
        return candidates
            .Where((c) => c.Rank > 0)
            .OrderBy((c) => c.Rank)
            .ThenBy((c) => c.Slot)
            .Select((c) => c.Slot)
            .ToList();
    }
}
