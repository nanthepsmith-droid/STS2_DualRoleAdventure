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
    float Bottom)
{
    internal float CenterX => (Left + Right) * 0.5f;

    internal float CenterY => (Top + Bottom) * 0.5f;
}

/// <summary>
/// 宝箱「遗物格数量」的**纯逻辑**判据（宝箱 12 人扩展）。
///
/// <para><b>根因</b>：原版 <c>treasure_room.tscn</c> 里只有 <c>MultiplayerRelicHolder1..4</c> 四个格
/// （场景硬编码，和休息区 `Character_1..4` 同一种形态）。`NTreasureRoomRelicCollection.InitializeRelics`
/// 只遍历 `_multiplayerHolders` ⇒ **第 5 个遗物起根本没有节点**：既看不见也点不到，
/// 于是 12 人只能抢前 4 个（连 `DefaultFocusedControl` 的 `_holdersInUse[slotIndex]` 也会越界）。</para>
///
/// <para><b>做法</b>：遗物数量 = 玩家数量（`TreasureRoomRelicSynchronizer.BeginRelicPicking` 里
/// 每个玩家各生成一个），所以缺的格子在 `InitializeRelics` 的 Prefix 里补建（运行时见
/// <see cref="TreasureRelicHoldersRuntime"/>），摆位由本文件算 —— 4 个及以内**逐值等于原版**，
/// 5 个以上按两排网格铺开（每排最多 6 个，格宽 136、间距 300，屏幕 1920 放得下）。</para>
/// </summary>
internal static class TreasureRelicHolderLayoutPolicy
{
    /// <summary>原版场景里写死的遗物格数量（`MultiplayerRelicHolder1..4`）。</summary>
    internal const int VanillaHolderCount = 4;

    /// <summary>本 mod 的席位上限（本地多控最多 12 人）⇒ 最多需要 12 个格子。</summary>
    internal const int MaxHolderCount = 12;

    /// <summary>一个格子的边长（原版 offset 差：136）。</summary>
    internal const float HolderSize = 136f;

    /// <summary>格子中心点的横向间距（图标聚焦时会放大到 2 倍，300 留得住余量）。</summary>
    internal const float GridPitchX = 300f;

    /// <summary>两排时的纵向间距。</summary>
    internal const float GridPitchY = 280f;

    /// <summary>每排最多几个（12 ⇒ 2 排 × 6）。</summary>
    internal const int MaxPerRow = 6;

    /// <summary>原版 4 个格子的摆位（顺序 = `_multiplayerHolders` 的加入顺序 = 场景节点顺序）。</summary>
    private static readonly TreasureRelicHolderPlacement[] Vanilla =
    [
        new(0, -148f, -180f, -12f, -44f),
        new(1, 12f, 44f, 148f, 180f),
        new(2, -388f, -68f, -252f, 68f),
        new(3, 252f, -68f, 388f, 68f),
    ];

    /// <summary>原版四个格子的摆位（只读，供回归对照）。</summary>
    internal static IReadOnlyList<TreasureRelicHolderPlacement> VanillaPlacements => Vanilla;

    /// <summary>需不需要补建格子：5~12 个遗物才需要（4 个及以内一律不动原版布局）。</summary>
    internal static bool NeedsBackfill(int relicCount) => relicCount > VanillaHolderCount && relicCount <= MaxHolderCount;

    /// <summary>
    /// 算出 <paramref name="relicCount"/> 个格子的摆位。
    /// 4 个及以内返回原版原值；超过 4 个按两排网格（末排可能不满，逐排居中）。
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

        int rows = relicCount <= MaxPerRow ? 1 : 2;
        var placements = new TreasureRelicHolderPlacement[relicCount];
        int seat = 0;
        for (int row = 0; row < rows; row++)
        {
            int inThisRow = Math.Min(MaxPerRow, relicCount - seat);
            float centerY = rows == 1 ? 0f : (row == 0 ? -GridPitchY * 0.5f : GridPitchY * 0.5f);
            for (int column = 0; column < inThisRow; column++)
            {
                float centerX = (column - (inThisRow - 1) * 0.5f) * GridPitchX;
                float half = HolderSize * 0.5f;
                placements[seat] = new TreasureRelicHolderPlacement(
                    seat,
                    centerX - half,
                    centerY - half,
                    centerX + half,
                    centerY + half);
                seat++;
            }
        }

        return placements;
    }
}
