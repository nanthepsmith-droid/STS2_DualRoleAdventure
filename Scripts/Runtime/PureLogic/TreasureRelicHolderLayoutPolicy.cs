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
/// <para><b>根因（0.113 起改版）</b>：原版 <c>treasure_room.tscn</c> 的
/// <c>RelicCollection/Container</c> 下有 <c>Layout1..Layout6</c> 六套布局（各 1~6 个格子），
/// <c>InitializeRelics</c> 按 `currentRelics.Count - 1` **选一套**并只显示那套 ⇒
/// **第 7 个遗物起一套都不匹配**：`_holdersInUse` 为空、一件也看不见点不到
/// （`DefaultFocusedControl` 直接返回 null ⇒ 房间卡在"选遗物"里）。
/// 12 人局（12 件遗物）与极速模式（每人 2 件）都会越界 ⇒ 由 <c>TreasureRelicHoldersRuntime</c>
/// 在 0.113 的**布局制**下补一套自己排的布局。</para>
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
    /// <summary>bench（4 格布局）里写死的遗物格数量 —— 见 <see cref="Vanilla"/>。</summary>
    internal const int VanillaHolderCount = 4;

    /// <summary>
    /// 0.113 原版**能显示**的最大遗物数：`treasure_room.tscn` 里 `RelicCollection/Container` 下有
    /// `Layout1..Layout6` 六套布局（各含 1~6 个格子），`InitializeRelics` 按 `currentRelics.Count - 1`
    /// 选其中一套 ⇒ 超过 6 个遗物**一套都不匹配**，`_holdersInUse` 为空、一件也点不到（12 人局旧实现已踩）。
    /// </summary>
    internal const int VanillaLayoutCount = 6;

    /// <summary>
    /// 宝箱遗物数的**最坏情况**：本 mod 席位上限 12 人 × 极速「每人 2 件」= 24。
    /// 只作口径说明（不再用来截断遗物池，见 r353）。
    /// </summary>
    internal const int MaxHolderCount = 24;

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

    /// <summary>
    /// 需不需要自己建一套布局：**超过 <see cref="VanillaLayoutCount"/>（6）才需要**。
    /// 0.113 原版自带 1~6 格六套布局，<= 6 一律不碰（保持原版布局回归契约）。
    /// </summary>
    internal static bool NeedsBackfill(int relicCount) => relicCount > VanillaLayoutCount;

    /// <summary>
    /// 算出 <paramref name="relicCount"/> 个格子的摆位。
    /// 4 个及以内返回原版原值；超过 4 个按上面的方阵口径铺开。
    ///
    /// ⚠ 调用点只在「0.113 原版布局不够用」（&gt; 6）时才用它建格子，5/6 走原版 `Layout5/6`；
    /// 13~24（12 席 × 每人 2 件的最坏情况）按行数等比缩，保证整块仍落在原版面板内。
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

        // 行数超过原版口径（3 行 = ±190）时整体等比缩小，否则大数量会把格子顶出面板。
        float shrink = Math.Min(1f, 3f / rows);
        float pitchX = GridPitchX * shrink;
        float pitchY = GridPitchY * shrink;
        float scale = BackfillScale * shrink;

        // 每行平均分（7 ⇒ 3/2/2 而不是 4/3/0），避免出现"只剩一个遗物孤零零挂在最后一行"。
        int basePerRow = relicCount / rows;
        int extra = relicCount % rows;

        var placements = new TreasureRelicHolderPlacement[relicCount];
        int seat = 0;
        for (int row = 0; row < rows; row++)
        {
            int inThisRow = basePerRow + (row < extra ? 1 : 0);
            float centerY = (row - (rows - 1) * 0.5f) * pitchY;
            float half = HolderSize * 0.5f * scale;
            for (int column = 0; column < inThisRow; column++)
            {
                float centerX = (column - (inThisRow - 1) * 0.5f) * pitchX;
                placements[seat] = new TreasureRelicHolderPlacement(
                    seat,
                    centerX - half,
                    centerY - half,
                    centerX + half,
                    centerY + half,
                    scale);
                seat++;
            }
        }

        return placements;
    }
}
