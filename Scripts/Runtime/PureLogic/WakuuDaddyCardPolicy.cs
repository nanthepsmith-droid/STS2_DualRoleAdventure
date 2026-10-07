using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime.PureLogic;

/// <summary>
/// ④「瓦库的爹」三张牌效果（我挡 / 你攻 / 合体）的**纯判定**（不引用 Godot / 游戏运行时类型，可单测）。
///
/// 实装口径（2026-10-07，照提案 §5.1）：
/// <list type="bullet">
/// <item><b>我挡</b>：选一个队友 ⇒ 它本回合受到的攻击伤害改由施牌者承受、且只有一半，抽 1 张。<br/>
///   判定见 <see cref="CanShieldTarget"/>（不能选自己）。</item>
/// <item><b>你攻</b>：选一个敌人 ⇒ 本回合所有**瓦库席位**出牌时优先集火它，抽 1 张。真人自己不受影响。</item>
/// <item><b>合体</b>：选一个**瓦库队友** ⇒ 它的手牌与能量都归你；本回合你抽/弃牌随机走双方的牌堆，抽 1 张。<br/>
///   判定见 <see cref="CanMergeTarget"/>；「随机」的候选集见 <see cref="ResolveMergeDrawSources"/> /
///   <see cref="ShouldDiscardToOther"/>。</item>
/// </list>
/// </summary>
internal static class WakuuDaddyCardPolicy
{
    /// <summary>合体混抽候选：**自己**的抽牌堆（本来就是它，无需搬运）。</summary>
    internal const int MergeDrawOwn = 0;

    /// <summary>合体混抽候选：**瓦库**的抽牌堆（需要把那张牌搬进施牌者的抽牌堆顶部）。</summary>
    internal const int MergeDrawOther = 1;

    /// <summary>
    /// 合体混抽的候选来源（按固定顺序返回下标；空列表 = 两边抽牌堆都空 ⇒ 不混）。
    ///
    /// 只算「抽牌堆非空」的来源：空的那一侧由原版抽牌流程自己走「抽牌堆空了就洗弃牌堆」，
    /// 我们不去替它洗（那会牵动另一名玩家的牌堆归属，风险大于收益）。
    /// </summary>
    internal static IReadOnlyList<int> ResolveMergeDrawSources(bool ownDrawAvailable, bool otherDrawAvailable)
    {
        List<int> sources = new(2);
        if (ownDrawAvailable)
        {
            sources.Add(MergeDrawOwn);
        }

        if (otherDrawAvailable)
        {
            sources.Add(MergeDrawOther);
        }

        return sources;
    }

    /// <summary>合体混弃目的地：<c>true</c> = 进**瓦库**的弃牌堆（否则进自己的）；各半。</summary>
    internal static bool ShouldDiscardToOther(int roll) => (roll & 1) == 1;

    /// <summary>合体目标是否合法：必须点在一个**瓦库托管席位**上、且不是自己。</summary>
    internal static bool CanMergeTarget(bool targetIsWakuuSeat, bool targetIsSelf)
        => targetIsWakuuSeat && !targetIsSelf;

    /// <summary>我挡目标是否合法：是队友即可（不能选自己 —— 自己挡自己没有意义）。</summary>
    internal static bool CanShieldTarget(bool targetIsSelf) => !targetIsSelf;
}
