using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「变化成**指定**一张牌」的结果牌登记处（2026-11-23，口径见 TODO §瓦库托管：变化牌处置）。
///
/// <para><b>为什么需要</b>：<c>CardTransformation</c> 有两种形态 —— <c>Replacement != null</c>（变成
/// **指定**的一张牌，如储君仆从 <c>MINION_*</c>）与 <c>Replacement == null</c>（从原牌池里**随机**变一张，
/// 同角色；原版把候选稀有度过滤在 Common/Uncommon/Rare ⇒ 随机变化永远不产 <c>CardRarity.Token</c>）。
/// 用户 2026-11-11 拍板的口径：前者"约等于已经被消耗" ⇒ 出牌侧**直接套用消耗牌的既有优先级**
/// （评分 <c>-5</c>），后者维持原有处理、不打任何标记。</para>
///
/// <para><b>为什么按引用而不是按 Id</b>：同一张牌在牌组里可能同时存在"随机变化来的"和"指定变化来的"
/// 两张，按 id 记会把前者一起标上（用户口径明确区分这两类）。</para>
///
/// <para><b>生命周期</b>：一张牌在一局内是唯一实例（不会被回收复用）⇒ 按引用记是安全的；
/// 牌被消耗 / 再次变化 / 移出牌组后实例本身即成为垃圾，留一条引用到退局为止，
/// 代价可忽略（一局里"指定目标变化"通常只有个位数）。退局统一清
/// （<c>LocalMultiControlRuntime.OnRunCleanup</c>）。</para>
///
/// <para>纯逻辑、不碰游戏类型 ⇒ 可离线单测。</para>
/// </summary>
internal sealed class WakuuTransformReplacementSet
{
    private readonly List<object> _cards = new();

    /// <summary>在册牌数（日志 / 断言用）。</summary>
    public int Count => _cards.Count;

    /// <summary>登记一张牌实例。返回 <c>false</c> 表示已在册（引用相同）或入参为空。</summary>
    public bool Add(object? card)
    {
        if (card == null || Contains(card))
        {
            return false;
        }

        _cards.Add(card);
        return true;
    }

    /// <summary>是否在册（**引用相等**，不接受 id / 值相等）。</summary>
    public bool Contains(object? card)
    {
        if (card == null)
        {
            return false;
        }

        for (int i = 0; i < _cards.Count; i++)
        {
            if (ReferenceEquals(_cards[i], card))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>按引用移除（提前摘除）。返回是否命中。</summary>
    public bool Remove(object? card)
    {
        if (card == null)
        {
            return false;
        }

        for (int i = 0; i < _cards.Count; i++)
        {
            if (ReferenceEquals(_cards[i], card))
            {
                _cards.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    /// <summary>清空（退局 / 换局）。返回清掉的条数。</summary>
    public int Clear()
    {
        int count = _cards.Count;
        _cards.Clear();
        return count;
    }
}
