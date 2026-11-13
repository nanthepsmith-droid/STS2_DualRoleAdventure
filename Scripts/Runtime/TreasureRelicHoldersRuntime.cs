using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 宝箱**遗物格布局**补齐（12 人扩展 / 极速模式每人 2 件）。
///
/// <para><b>0.113 的原版机制（r350 适配后确认）</b>：`treasure_room.tscn` 里
/// `RelicCollection/Container` 下固定有 `Layout1..Layout6` 六套布局（各含 1~6 个 `NTreasureRoomRelicHolder`）。
/// `NTreasureRoomRelicCollection.InitializeRelics` 只做一件事：
/// `for (i &lt; _holderLayouts.Count) { i == currentRelics.Count - 1 ? 显示 : 隐藏 }`，
/// 再遍历被显示那套的格子初始化遗物 ⇒ **遗物数 &gt; 6 时一套都不匹配**，`_holdersInUse` 为空：
/// 一件也看不见、点不到，`DefaultFocusedControl` 返回 null ⇒ 宝箱房间卡住。</para>
///
/// <para>r351 之前的实现补的是 `_multiplayerHolders`（0.112 及更早的字段），0.113 已经没有了 ⇒
/// 12 人局 / 极速模式（每人 2 件）一直在"遗物放不下"。现在改成：`InitializeRelics` 的 **Prefix** 里，
/// 遗物数 &gt; 6 时按 <see cref="TreasureRelicHolderLayoutPolicy"/> 建一套自己的布局（含 count 个格子），
/// 并把它塞进 `_holderLayouts[count - 1]` —— 正好是原版挑布局的那一格。</para>
///
/// <para>失败只 WARN：退回"原版 6 格"（此时 7+ 件会没人能选，但房间仍可用右下角「跳过」推进）。</para>
/// </summary>
internal static class TreasureRelicHoldersRuntime
{
    private const string HolderSceneInnerPath = "ui/treasure_relic_holder";
    private const string HolderContainerPath = "Container";
    private const string OurLayoutNamePrefix = "LocalRelicLayout";

    private static readonly System.Reflection.FieldInfo? HolderLayoutsField =
        AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_holderLayouts");

    private static readonly System.Reflection.FieldInfo? RelicContainerField =
        AccessTools.Field(typeof(NTreasureRoomRelicCollection), "_relicContainer");

    /// <summary>按当前遗物数量准备一套能放下它们的布局。幂等（同一数量复用已建好的那套）。</summary>
    internal static void EnsureLayoutForCurrentRelics(NTreasureRoomRelicCollection collection)
    {
        try
        {
            int relicCount = RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics?.Count ?? 0;
            if (!TreasureRelicHolderLayoutPolicy.NeedsBackfill(relicCount))
            {
                return;
            }

            if (HolderLayoutsField?.GetValue(collection) is not List<Control> layouts)
            {
                LocalMultiControlLogger.Warn($"宝箱遗物布局补齐跳过：拿不到 _holderLayouts 字段。relics={relicCount}");
                return;
            }

            Control? container = collection.GetNodeOrNull<Control>(HolderContainerPath)
                ?? RelicContainerField?.GetValue(collection) as Control;
            if (container == null)
            {
                LocalMultiControlLogger.Warn($"宝箱遗物布局补齐跳过：找不到 {HolderContainerPath} 节点。relics={relicCount}");
                return;
            }

            int layoutIndex = relicCount - 1;
            if (layouts.Count > layoutIndex
                && layouts[layoutIndex] != null
                && GodotObject.IsInstanceValid(layouts[layoutIndex])
                && CountHolders(layouts[layoutIndex]) == relicCount)
            {
                return;
            }

            Control layout = EnsureOurLayout(container, relicCount);
            while (layouts.Count < layoutIndex)
            {
                // 中间位次（极少见：一次跳好几个结果数）用隐藏的空气布局占位 —— 原版只认下标。
                layouts.Add(CreatePlaceholder(container, layouts.Count));
            }

            if (layouts.Count == layoutIndex)
            {
                layouts.Add(layout);
            }
            else
            {
                layouts[layoutIndex] = layout;
            }

            LocalMultiControlLogger.Info(
                $"宝箱遗物布局已补齐（0.113 布局制）: relics={relicCount}, "
                + $"原版布局上限={TreasureRelicHolderLayoutPolicy.VanillaLayoutCount}, "
                + $"插入位次={layoutIndex}, 格数={relicCount}, scale={TreasureRelicHolderLayoutPolicy.Compute(relicCount)[0].Scale:0.00}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"宝箱遗物布局补齐失败（退回原版 6 格，可用「跳过」推进）: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static Control EnsureOurLayout(Control container, int relicCount)
    {
        string name = $"{OurLayoutNamePrefix}{relicCount}";
        if (container.GetNodeOrNull<Control>(name) is { } cached && CountHolders(cached) == relicCount)
        {
            return cached;
        }

        Control layout = new()
        {
            Name = name,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        layout.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        IReadOnlyList<TreasureRelicHolderPlacement> placements = TreasureRelicHolderLayoutPolicy.Compute(relicCount);
        for (int i = 0; i < placements.Count; i++)
        {
            NTreasureRoomRelicHolder holder = SceneHelper.Instantiate<NTreasureRoomRelicHolder>(HolderSceneInnerPath);
            holder.Name = (i + 1).ToString();
            holder.Visible = true;
            holder.AnchorLeft = 0.5f;
            holder.AnchorTop = 0.5f;
            holder.AnchorRight = 0.5f;
            holder.AnchorBottom = 0.5f;
            ApplyPlacement(holder, placements[i]);
            layout.AddChildSafely(holder);
        }

        container.AddChildSafely(layout);
        return layout;
    }

    private static Control CreatePlaceholder(Control container, int index)
    {
        Control placeholder = new()
        {
            Name = $"{OurLayoutNamePrefix}Filler{index}",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        placeholder.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        container.AddChildSafely(placeholder);
        return placeholder;
    }

    private static int CountHolders(Control layout)
    {
        return layout.GetChildren().OfType<NTreasureRoomRelicHolder>().Count();
    }

    private static void ApplyPlacement(NTreasureRoomRelicHolder holder, TreasureRelicHolderPlacement placement)
    {
        holder.OffsetLeft = placement.Left;
        holder.OffsetTop = placement.Top;
        holder.OffsetRight = placement.Right;
        holder.OffsetBottom = placement.Bottom;

        // 补出来的格子小于原版（图标放大 2 倍后仍不压邻格）⇒ 缩放要绕**自身中心**，
        // 否则默认 pivot=(0,0) 会把格子往左上角缩，中心和算出来的位置对不上。
        holder.PivotOffset = new Vector2(placement.Right - placement.Left, placement.Bottom - placement.Top) * 0.5f;
        holder.Scale = new Vector2(placement.Scale, placement.Scale);
    }
}
