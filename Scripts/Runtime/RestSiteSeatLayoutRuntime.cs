using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// BUG-14：休息区**角色容器**补齐（原版 `NRestSiteRoom._Ready` 只认 `BgContainer/Character_1..4`）。
///
/// <para>在 `_Ready` 的 **Prefix** 里跑：玩家数 &gt; 4 时先补建 `Character_5..N` 控制节点塞进
/// `_characterContainers`，并整排重排（摆位算法见 <see cref="RestSiteSeatLayoutPolicy"/>）。
/// 这样原版那个 `for (i = 0; i &lt; Players.Count; i++) _characterContainers[i].AddChildSafely(...)`
/// 就不会越界 ⇒ `_Ready` 能正常跑完（选项可见性/事件订阅都走原版流程），
/// 5 号及以后的立绘与"他选了什么"气泡也都在。</para>
///
/// <para><b>兜底</b>：任一步失败只 WARN，不抛 —— 下一层还有
/// <c>NRestSiteRoomReadyGuardPatch</c> 的 Finalizer（接住越界 + 让主控席位能选）顶着。</para>
/// </summary>
internal static class RestSiteSeatLayoutRuntime
{
    private const string BgContainerPath = "BgContainer";
    private const string ContainerNamePrefix = "Character_";

    /// <summary>
    /// 按 `_runState.Players.Count` 补齐/重排休息区角色容器。幂等（重复调用只是重设同样的值）。
    /// </summary>
    internal static void EnsureContainersForAllPlayers(NRestSiteRoom room)
    {
        try
        {
            IRunState? runState = AccessTools.Field(typeof(NRestSiteRoom), "_runState")?.GetValue(room) as IRunState;
            int playerCount = runState?.Players.Count ?? 0;
            if (!RestSiteSeatLayoutPolicy.NeedsBackfill(playerCount))
            {
                // 4 人及以下 = 原版布局，一个值都不动（回归契约）
                return;
            }

            if (AccessTools.Field(typeof(NRestSiteRoom), "_characterContainers")?.GetValue(room) is not List<Control> containers)
            {
                LocalMultiControlLogger.Warn($"休息区角色容器补齐跳过：拿不到 _characterContainers 字段。players={playerCount}");
                return;
            }

            if (!room.IsInsideTree())
            {
                LocalMultiControlLogger.Warn($"休息区角色容器补齐跳过：房间节点还没进树。players={playerCount}");
                return;
            }

            Control? bgContainer = room.GetNodeOrNull<Control>(BgContainerPath);
            if (bgContainer == null)
            {
                LocalMultiControlLogger.Warn($"休息区角色容器补齐跳过：找不到 {BgContainerPath} 节点。players={playerCount}");
                return;
            }

            // ⚠ `_characterContainers` 是在原版 _Ready 里才填的，而我们是 Prefix ⇒ 此刻通常**是空的**。
            // 必须先按场景真值把原版那 4 个节点**认领**进列表，否则会给它们再造一份同名副本
            // （r295 首版实机就踩了这个：`新建=12`，4 个原版容器成了没人用的孤儿节点）。
            int reused = SeedVanillaContainers(bgContainer, containers);

            IReadOnlyList<RestSiteSeatPlacement> placements = RestSiteSeatLayoutPolicy.Compute(playerCount);
            int created = 0;
            for (int seat = 0; seat < playerCount; seat++)
            {
                RestSiteSeatPlacement placement = placements[seat];
                Control? container = seat < containers.Count ? containers[seat] : null;
                if (container == null || !GodotObject.IsInstanceValid(container))
                {
                    container = CreateContainer(seat + 1);
                    bgContainer.AddChildSafely(container);
                    if (seat < containers.Count)
                    {
                        containers[seat] = container;
                    }
                    else
                    {
                        containers.Add(container);
                    }

                    created++;
                }

                ApplyPlacement(container, placement);
            }

            LocalMultiControlLogger.Info(
                $"休息区角色容器已补齐（BUG-14）: players={playerCount}, 原版容器={RestSiteSeatLayoutPolicy.VanillaContainerCount}, 复用={reused}, 新建={created}, " +
                $"每排最多={(playerCount + 1) / 2}, scale={placements[0].Scale:0.###}");
        }
        catch (Exception exception)
        {
            // 纯表现层：补不上就退回 Finalizer 那条老路（主控席位仍能选，只是 5+ 没立绘）
            LocalMultiControlLogger.Warn($"休息区角色容器补齐失败（继续用 Finalizer 兜底）: {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// 按场景真值把原版 `BgContainer/Character_1..4` 认领进 <paramref name="containers"/>（前缀阶段列表还是空的）。
    /// 已存在同名节点就直接复用；某个节点缺失（第三方换过场景）就跳过，后面按"缺谁补谁"新建。
    /// </summary>
    private static int SeedVanillaContainers(Control bgContainer, List<Control> containers)
    {
        int reused = 0;
        containers.Clear();
        for (int i = 1; i <= RestSiteSeatLayoutPolicy.VanillaContainerCount; i++)
        {
            Control? existing = bgContainer.GetNodeOrNull<Control>($"{ContainerNamePrefix}{i}");
            if (existing == null || !GodotObject.IsInstanceValid(existing))
            {
                continue;
            }

            containers.Add(existing);
            reused++;
        }

        return reused;
    }

    /// <summary>
    /// 建一个与原版 `Character_N` 同构的空容器：中心锚点、零尺寸、不挡点击。
    /// 真正的摆位由 <see cref="ApplyPlacement"/> 统一设（新老容器同一口径）。
    /// </summary>
    private static Control CreateContainer(int oneBasedIndex)
    {
        var container = new Control
        {
            Name = $"{ContainerNamePrefix}{oneBasedIndex}",
            AnchorLeft = 0.5f,
            AnchorTop = 0.5f,
            AnchorRight = 0.5f,
            AnchorBottom = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        return container;
    }

    private static void ApplyPlacement(Control container, RestSiteSeatPlacement placement)
    {
        container.OffsetLeft = placement.OffsetX;
        container.OffsetTop = placement.OffsetY;
        container.OffsetRight = placement.OffsetX;
        container.OffsetBottom = placement.OffsetY;
        container.Scale = new Vector2(placement.Scale, placement.Scale);
    }
}
