using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 战神立绘的**绘制层级**（r366，纯逻辑）。
///
/// **问题**（用户 2026-11-17 实机）：「召唤出战神的战神都是在本体图层上面的，挡到了」——
/// 原版 `NCombatRoom.AddCreatureNode` 是 `_allyContainer.AddChildSafely(node)`，
/// 后加的子节点**画在上面** ⇒ 战神把绑主（真人）的立绘挡住。
///
/// **口径**：战神站在召唤物那一层，**不挡本体**；同一组里**后召唤的更靠下层**
/// （先召唤的在前面，后召唤的从后面顶上来，观感像"排到队尾"）。
///
/// 容器里的子节点顺序 = 绘制顺序（Godot 里子节点没有 YSort 时按 index 绘制，后面的盖前面的），
/// 所以这里只算"期望的原始下标顺序"：
/// <list type="number">
///   <item><b>非生物节点</b>（场景自带的装饰/底衬）原样留在最前面 —— 不能被战神盖住，也不能去盖它；</item>
///   <item><b>战神</b>按**倒序**（最后召唤的最靠前 = 最下层）；</item>
///   <item><b>其它生物</b>（真人 / 宠物 / 敌人）保持原顺序，画在最上层 —— 这就是"本体不被挡"。</item>
/// </list>
/// </summary>
internal static class WarGodLayerPolicy
{
    /// <summary>
    /// 算出容器子节点的**期望绘制顺序**（值 = 原子节点下标）。
    /// 三个入参都按"原子节点下标"给，互不重叠；返回值是它们的稳定重排，长度 = 三者之和。
    /// </summary>
    /// <param name="nonCreatureIndices">非生物节点的下标（按原顺序）。</param>
    /// <param name="warGodIndicesOldestFirst">战神节点的下标，**按召唤顺序**（先召唤的在前）。</param>
    /// <param name="otherCreatureIndices">其它生物节点的下标（按原顺序）。</param>
    public static List<int> BuildDrawOrder(
        IReadOnlyList<int> nonCreatureIndices,
        IReadOnlyList<int> warGodIndicesOldestFirst,
        IReadOnlyList<int> otherCreatureIndices)
    {
        int nonCreatureCount = nonCreatureIndices?.Count ?? 0;
        int warGodCount = warGodIndicesOldestFirst?.Count ?? 0;
        int otherCount = otherCreatureIndices?.Count ?? 0;

        List<int> order = new(nonCreatureCount + warGodCount + otherCount);
        for (int i = 0; i < nonCreatureCount; i++)
        {
            order.Add(nonCreatureIndices![i]);
        }

        // 后召唤的排到更前（= 更下层）：倒序遍历召唤顺序。
        for (int i = warGodCount - 1; i >= 0; i--)
        {
            order.Add(warGodIndicesOldestFirst![i]);
        }

        for (int i = 0; i < otherCount; i++)
        {
            order.Add(otherCreatureIndices![i]);
        }

        return order;
    }
}
