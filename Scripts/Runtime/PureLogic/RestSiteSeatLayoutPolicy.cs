using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 休息区里一个"角色容器"（原版 <c>BgContainer/Character_N</c>）的摆位。
/// 单位 = 原版场景里的 <c>offset_left/top</c>（相对 <c>BgContainer</c> 的中心锚点）。
/// </summary>
internal readonly record struct RestSiteSeatPlacement(
    int SeatIndex,
    float OffsetX,
    float OffsetY,
    float Scale,
    bool IsFrontRow);

/// <summary>
/// BUG-14（火堆最多只显示 4 个玩家）的**纯逻辑**摆位判据。
///
/// <para><b>根因</b>：原版 <c>NRestSiteRoom._Ready</c> 硬编码
/// <c>GetNode("BgContainer/Character_1..4")</c> 四次，然后对**全部玩家**循环
/// <c>_characterContainers[i].AddChildSafely(...)</c> ⇒ 第 5 个玩家起必然
/// <c>ArgumentOutOfRangeException</c>（我们现在用 Finalizer 接住，但 5 号及以后的立绘/选项气泡
/// 根本没有节点 ⇒ 看不到他们选了什么）。</para>
///
/// <para><b>做法</b>（用户 2026-10-10 拍板的"修法备选 ①"）：运行时给休息区补建
/// <c>Character_5..N</c> 容器并加进 <c>_characterContainers</c>。本文件只负责**算出每个席位摆哪**，
/// 节点创建/克隆在 <see cref="RestSiteSeatLayoutRuntime"/>（纯逻辑 ⇒ 可单测）。</para>
///
/// <para><b>两条硬约束</b>：① <b>4 人及以下必须与原版逐值一致</b>（下面 <see cref="Vanilla"/> 是
/// 从 <c>scenes/rooms/rest_site_room.tscn</c> 抄下来的原值，单测钉死）—— 玩家不超过 4 个时
/// 我们绝不改布局；② 5 人以上才重排：前 4 个容器保持原值已不可能（要腾位置）⇒ 按"前后两排 + 横向
/// 等距 + 自动缩小"铺开，保证不跑到屏幕外。</para>
/// </summary>
internal static class RestSiteSeatLayoutPolicy
{
    /// <summary>原版场景里写死的角色容器数量（`Character_1..4`）。</summary>
    internal const int VanillaContainerCount = 4;

    /// <summary>原版容器缩放（`scale = Vector2(0.5, 0.5)`）。</summary>
    internal const float VanillaScale = 0.5f;

    /// <summary>原版同一排两个席位的横向间距（`Character_1` 624.775 → `Character_2` 1238.78）。</summary>
    internal const float BasePitch = 614.005f;

    /// <summary>一排最多铺多宽（屏幕 1920 宽，留左右边距）。超过就缩 scale 而不是继续拉宽。</summary>
    internal const float MaxRowSpan = 1600f;

    /// <summary>scale 下限：再小就只剩糊点，宁可轻微重叠。</summary>
    internal const float MinScale = 0.20f;

    /// <summary>整排的水平中心（取原版前排两席的中点）。</summary>
    internal const float RowCenterX = 931.7775f;

    /// <summary>前排 y（原版 `Character_1` 641.775 / `Character_2` 648.775 的均值）。</summary>
    internal const float FrontRowY = 645.275f;

    /// <summary>后排 y（原版 `Character_3` 584.775 / `Character_4` 582.775 的均值）；越小越靠上/越远。</summary>
    internal const float BackRowY = 583.775f;

    /// <summary>原版 4 个席位的摆位（顺序 = 场景里 `_characterContainers` 的加入顺序）。</summary>
    private static readonly RestSiteSeatPlacement[] Vanilla =
    [
        new(0, 624.775f, 641.775f, VanillaScale, IsFrontRow: true),
        new(1, 1238.78f, 648.775f, VanillaScale, IsFrontRow: true),
        new(2, 749.775f, 584.775f, VanillaScale, IsFrontRow: false),
        new(3, 1091.78f, 582.775f, VanillaScale, IsFrontRow: false),
    ];

    /// <summary>原版四个席位（只读，供回归对照/日志）。</summary>
    internal static IReadOnlyList<RestSiteSeatPlacement> VanillaPlacements => Vanilla;

    /// <summary>需不需要补建容器（只有超过原版 4 个玩家才需要）。</summary>
    internal static bool NeedsBackfill(int playerCount) => playerCount > VanillaContainerCount;

    /// <summary>
    /// 算出全部席位的摆位（长度 = <paramref name="playerCount"/>）。
    /// 4 人及以下返回原版原值（顺序按席位号），5 人以上按两排重排。
    /// </summary>
    internal static IReadOnlyList<RestSiteSeatPlacement> Compute(int playerCount)
    {
        if (playerCount <= 0)
        {
            return Array.Empty<RestSiteSeatPlacement>();
        }

        if (playerCount <= VanillaContainerCount)
        {
            var vanilla = new RestSiteSeatPlacement[playerCount];
            Array.Copy(Vanilla, vanilla, playerCount);
            return vanilla;
        }

        // 前后两排各占一半（与原版 4 人的分布一致：前 2 / 后 2）；玩家数为奇数时前排多一个。
        int frontCount = (playerCount + 1) / 2;
        int backCount = playerCount / 2;
        int rowSeats = Math.Max(frontCount, backCount);

        float pitch = rowSeats <= 2
            ? BasePitch
            : Math.Min(BasePitch, MaxRowSpan / (rowSeats - 1));
        float scale = Math.Clamp(VanillaScale * pitch / BasePitch, MinScale, VanillaScale);

        var placements = new RestSiteSeatPlacement[playerCount];
        for (int seat = 0; seat < playerCount; seat++)
        {
            bool isFront = seat < frontCount;
            int rowIndex = isFront ? seat : seat - frontCount;
            int rowCount = isFront ? frontCount : backCount;
            float offsetX = RowCenterX + (rowIndex - (rowCount - 1) / 2f) * pitch;
            float offsetY = isFront ? FrontRowY : BackRowY;
            placements[seat] = new RestSiteSeatPlacement(seat, offsetX, offsetY, scale, isFront);
        }

        return placements;
    }
}
