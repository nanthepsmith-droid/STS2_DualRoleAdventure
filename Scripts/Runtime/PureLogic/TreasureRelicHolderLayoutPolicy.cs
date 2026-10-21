using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 宝箱里一个遗物格（<c>MultiplayerRelicHolderN</c>）的摆位，单位 = 原版场景里的
/// <c>offset_left/top/right/bottom</c>（相对 <c>RelicCollection/Container</c> 的中心锚点）。
/// </summary>
internal readonly record struct TreasureRelicHolderPlacement(
    int Index,
    float Left,
    float Top,
    float Right,
    float Bottom,
    float Scale)
{
    internal float CenterX => (Left + Right) * 0.5f;

    internal float CenterY => (Top + Bottom) * 0.5f;
}

/// <summary>
/// 宝箱「遗物格数量 / 摆位」的**纯逻辑**判据（宝箱 12 人扩展）。
///
/// <para><b>根因</b>：原版 <c>treasure_room.tscn</c> 里只有 <c>MultiplayerRelicHolder1..4</c> 四个格
/// （场景硬编码，和休息区 `Character_1..4` 同一种形态）。`NTreasureRoomRelicCollection.InitializeRelics`
/// 只遍历 `_multiplayerHolders` ⇒ **第 5 个遗物起根本没有节点**：既看不见也点不到，
/// 于是 12 人只能抢前 4 个（连 `DefaultFocusedControl` 的 `_holdersInUse[slotIndex]` 也会越界）。</para>
///
/// <para><b>布局口径（用户 2026-10-21 第二轮反馈后定稿）</b>：第一版排成"两排 × 6 列"，横向铺到 ±750，
/// **最左边那几件压到玩家状态条底下点不到** ⇒ 改成**方阵**：列数 = <c>ceil(sqrt(n))</c>（上限 4），
/// 行数 = <c>ceil(n/列数)</c>，每行的件数尽量平均（不是"前几行满、最后一行剩 1 个"）；
/// 整块最宽 3×210 = 630、最高 2×190 = 380，仍落在原版 `RelicCollection`（900×580）范围内。
/// 格子的**视觉**尺寸会放大到 2 倍（`OnFocus/OnUnfocus` 把 `Relic` 缩放到 2 / 2.1），所以补出来的格子
/// 统一 `Scale=0.72`（图标 ≈196px，横向间距 210 不重叠）。</para>
/// </summary>
internal static class TreasureRelicHolderLayoutPolicy
{
    /// <summary>原版场景里写死的遗物格数量（`MultiplayerRelicHolder1..4`）。</summary>
    internal const int VanillaHolderCount = 4;

    /// <summary>本 mod 的席位上限（本地多控最多 12 人）⇒ 最多需要 12 个格子。</summary>
    internal const int MaxHolderCount = 12;

    /// <summary>一个格子的逻辑边长（原版 offset 差：136）。</summary>
    internal const float HolderSize = 136f;

    /// <summary>列数上限（方阵口径：4 列 × 3 行 = 12）。</summary>
    internal const int MaxColumns = 4;

    /// <summary>列间距（按"图标放大 2 倍后的实际宽度"留过余量）。</summary>
    internal const float GridPitchX = 210f;

    /// <summary>行间距。</summary>
    internal const float GridPitchY = 190f;

    /// <summary>补出来的格子统一缩放（图标 ≈196px，横向不压到邻格）。</summary>
    internal const float BackfillScale = 0.72f;

    /// <summary>原版 4 个格子的摆位（顺序 = `_multiplayerHolders` 的加入顺序 = 场景节点顺序）。</summary>
    private static readonly TreasureRelicHolderPlacement[] Vanilla =
    [
        new(0, -148f, -180f, -12f, -44f, 1f),
        new(1, 12f, 44f, 148f, 180f, 1f),
        new(2, -388f, -68f, -252f, 68f, 1f),
        new(3, 252f, -68f, 388f, 68f, 1f),
    ];

    /// <summary>原版四个格子的摆位（只读，供回归对照）。</summary>
    internal static IReadOnlyList<TreasureRelicHolderPlacement> VanillaPlacements => Vanilla;

    /// <summary>需不需要补建格子：5~12 个遗物才需要（4 个及以内一律不动原版布局）。</summary>
    internal static bool NeedsBackfill(int relicCount) => relicCount > VanillaHolderCount && relicCount <= MaxHolderCount;

    /// <summary>
    /// 算出 <paramref name="relicCount"/> 个格子的摆位。
    /// 4 个及以内返回原版原值；超过 4 个按上面的方阵口径铺开。
    /// </summary>
    internal static IReadOnlyList<TreasureRelicHolderPlacement> Compute(int relicCount)
    {
        if (relicCount <= 0)
        {
            return Array.Empty<TreasureRelicHolderPlacement>();
        }

        if (relicCount <= VanillaHolderCount)
        {
            var vanilla = new TreasureRelicHolderPlacement[relicCount];
            Array.Copy(Vanilla, vanilla, relicCount);
            return vanilla;
        }

        int columns = Math.Min(MaxColumns, (int)Math.Ceiling(Math.Sqrt(relicCount)));
        int rows = (int)Math.Ceiling(relicCount / (double)columns);

        // 每行平均分（7 ⇒ 3/2/2 而不是 4/3/0），避免出现"只剩一个遗物孤零零挂在最后一行"。
        int basePerRow = relicCount / rows;
        int extra = relicCount % rows;

        var placements = new TreasureRelicHolderPlacement[relicCount];
        int seat = 0;
        for (int row = 0; row < rows; row++)
        {
            int inThisRow = basePerRow + (row < extra ? 1 : 0);
            float centerY = (row - (rows - 1) * 0.5f) * GridPitchY;
            float half = HolderSize * 0.5f * BackfillScale;
            for (int column = 0; column < inThisRow; column++)
            {
                float centerX = (column - (inThisRow - 1) * 0.5f) * GridPitchX;
                placements[seat] = new TreasureRelicHolderPlacement(
                    seat,
                    centerX - half,
                    centerY - half,
                    centerX + half,
                    centerY + half,
                    BackfillScale);
                seat++;
            }
        }

        return placements;
    }
}
