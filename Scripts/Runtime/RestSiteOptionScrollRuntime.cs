#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 休息处**选项横向滚动条**（2026-10-09 用户提出：选项太多时"根本放不下"）。
///
/// 病灶（原版场景 `scenes/rooms/rest_site_room.tscn` 写死）：
/// `%ChoicesContainer` 是个 **HBoxContainer**、`anchors_preset = 8`（居中）、`offset_left=-399 / offset_right=400`
/// ⇒ 容器宽 799px；而每个选项按钮 246.786px、间隔 `separation = 100`
/// ⇒ 内容宽 = n×247 + (n−1)×100：**3 个 = 941（原版常态，仍摆得下）、6 个 = 1982、7 个 = 2328**
/// ⇒ 6 个以上就**超出屏幕**（1920 宽、居中排布 ⇒ 两端按钮被挤出屏幕、**点不到**）。
///
/// 做法：**内容真的超出屏幕时**才把 `%ChoicesContainer` 包进横向 `ScrollContainer`
/// （`ScrollMode.Auto` ⇒ 不溢出时与原版逐像素一致；溢出时给**可拖动 / 可滚轮**的横向滚动条，
/// `FollowFocus = true` ⇒ 手柄左右切焦点会自动滚到那个按钮）。窗口宽度取
/// `min(内容宽, 屏幕宽 − 2×边距)` ⇒ 能一屏看多少看多少，剩下的才滚。
///
/// 三条硬要求（踩过就炸，r241 首测一次踩中前两条）：
/// <list type="number">
/// <item>🔴 **只数"活着"的子节点**：游戏 `UpdateRestSiteOptions` 用 `QueueFree` 销毁旧按钮，
///   **那一帧它们还在子节点列表里** ⇒ 按 `GetChildCount()` 算宽度会算成 3 代叠加（r241 实测
///   `需要宽=7183, buttons=21`）⇒ HBox 被撑到 7183 宽 + `alignment = Center` ⇒ **左边多出 2400+px 空白**，
///   表现为"滚到最左什么都不显示、1/3 处才见到第一个选项"，且可见内容被夹在裁剪边缘 ⇒ **文字看不全、点不到**。
///   ⇒ 过滤 `IsQueuedForDeletion()`，并**只数选项按钮**（容器正常只装 `NRestSiteButton`）。</item>
/// <item>🔴 **必须给 HBox 设 `CustomMinimumSize.X`**：ScrollContainer 只把子节点撑到
///   `max(自身尺寸, 子节点最小尺寸)`；HBox 若仍按可视宽度排版，超出的按钮会被 ScrollContainer **裁掉**
///   —— 从"能拖到"变成"永远够不到"。</item>
/// <item>**包装层要向下加高**（`OffsetBottom += 滚动条高度+4`）：滚动条高度是从内容区**扣**出来的，
///   而按钮最小高度就是那 163px ⇒ 直接沿用原矩形会把按钮底部（含文字）切掉。下方到 Description 标签本有约 95px 空隙。</item>
/// </list>
///
/// ⚠ **不要用 `Control.GetCombinedMinimumSize()` / `Control.Size` 做判定**：两者都是**延迟布局**的结果，
/// 而按钮是 `UpdateRestSiteOptions` 刚 `AddChild` 出来的 ⇒ 同一帧读到旧值/0。可靠来源 =
/// ① 宽度用**锚点+偏移**自己算；② 内容宽用**每个按钮的 `CustomMinimumSize.X`**（场景写死、实例化即可读）累加
/// + HBox 的 `separation` 主题常量。
///
/// **自愈**：包好后挂 `ChildEnteredTree` / `ChildExitingTree`（旧按钮**真的**消失时才触发）⇒ 下一帧重算内容宽，
/// 游戏自己调 `UpdateRestSiteOptions`（选完选项后剩余选项重排）也能跟上。
///
/// **算术与阈值收在纯逻辑**：公式（Σ + (n−1)×间隔）、"要不要滚"（按**屏幕**可用宽）、窗口宽都在
/// <see cref="RestSiteOptionScrollPolicy"/>（可单测，见 `RestSiteOptionScrollPolicyTests`）；
/// 本类只负责"数活着的按钮 + 建/挂节点"这些碰 Godot 的部分。
///
/// **可关闭**（配置 `restSiteOptionScroll`）：用户口径「**可能与其它 mod 功能重合**，需要加个开关」
/// —— 别人也在改这块布局时关掉即可。
/// </summary>
internal static class RestSiteOptionScrollRuntime
{
    /// <summary>包装层节点名（用来判"这个容器已经被我们包过了"）。</summary>
    internal const string WrapperName = "LmcRestSiteOptionScroll";

    /// <summary>已挂"子节点变动 ⇒ 重算"信号的标记（挂在容器上，防止重复连接）。</summary>
    private const string HookedMeta = "lmc_rest_scroll_hooked";

    /// <summary>上一次同步的内容宽（挂在容器上，只用来判"要不要打一条变更日志"）。</summary>
    private const string LastWidthMeta = "lmc_rest_scroll_last_width";

    /// <summary>滚动条预留高度兜底值（取不到主题尺寸时用它；原版 HScrollBar 实际约 12~20px）。</summary>
    private const float FallbackScrollbarReserve = 28f;

    /// <summary>名字带兜底值（`%Label` 量不到时用它；源自场景几何：标签在 y=165、字号 28 + 描边 12）。</summary>
    private const float FallbackLabelBand = 48f;

    /// <summary>窗口两侧留的边距（超出屏幕才算溢出 ⇒ 判据是"按钮会不会被屏幕切掉"）。</summary>
    private const float SideMargin = 24f;

    /// <summary>窗口比内容多留一点余量，免得最后一个按钮贴着裁剪边缘。</summary>
    private const float ContentPadding = 12f;

    /// <summary>
    /// 由 <see cref="RestSiteUiRefreshUtil.TryRefresh"/>、进入房间的 deferred 自检链、以及容器子节点变动信号调用
    /// （**不新增 Harmony 目标**）。幂等：已包过就只刷新内容宽。
    /// </summary>
    internal static void TryEnsureScrollable(NRestSiteRoom? room)
    {
        try
        {
            if (room == null || !GodotObject.IsInstanceValid(room) || !room.IsInsideTree())
            {
                return;
            }

            if (!LocalWakuuAutopilotConfig.RestSiteOptionScroll)
            {
                return;
            }

            Control? choicesContainer = AccessTools.Field(typeof(NRestSiteRoom), "_choicesContainer")?.GetValue(room) as Control;
            Control? choicesScreen = AccessTools.Field(typeof(NRestSiteRoom), "_choicesScreen")?.GetValue(room) as Control;
            if (choicesContainer == null || choicesScreen == null)
            {
                return;
            }

            float requiredWidth = ResolveRequiredWidth(choicesContainer, out int liveButtonCount);
            if (requiredWidth <= 0f)
            {
                // 按钮还没建出来（options 为空 / 全是待销毁的旧按钮）⇒ 等下一次触发再试。
                return;
            }

            if (choicesContainer.GetParent() is ScrollContainer existing && existing.Name == WrapperName)
            {
                SyncWidth(choicesContainer, requiredWidth, liveButtonCount);
                return;
            }

            float visibleLimit = ResolveVisibleLimit(room);
            if (visibleLimit <= 0f)
            {
                // 还量不到屏幕宽（房间没进视口）⇒ 下一次触发再试。
                return;
            }

            if (!RestSiteOptionScrollPolicy.ShouldEnableScroll(requiredWidth, visibleLimit))
            {
                // 内容还在屏幕上摆得下（原版 3~5 个选项就是这种）⇒ **一行都不碰原版布局**。
                return;
            }

            // ⚠ 名字标签的"外挂高度"必须在 **Reparent 之前**量（量的是原版布局下的几何）。
            float labelBand = ResolveLabelBand(choicesContainer);
            Wrap(room, choicesContainer, choicesScreen, requiredWidth, visibleLimit, liveButtonCount, labelBand);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[休息区滚动] 启用休息处选项滚动条失败（已忽略）: {exception.Message}");
        }
    }

    /// <summary>
    /// 容器**内容**需要的宽度 = Σ(按钮最小宽) + (n−1)×separation。
    /// **只数活着、且真的是选项按钮的子节点**（`QueueFree` 掉的旧按钮当帧还在列表里，见类注释第 1 条）。
    /// 一个都取不到时返回 0（= 还没就绪，等下一次触发）。
    /// </summary>
    private static float ResolveRequiredWidth(Control choicesContainer, out int liveButtonCount)
    {
        liveButtonCount = 0;
        List<Control> live = new();
        foreach (Node child in choicesContainer.GetChildren())
        {
            if (child is not Control control)
            {
                continue;
            }

            if (!GodotObject.IsInstanceValid(control) || control.IsQueuedForDeletion())
            {
                continue;
            }

            live.Add(control);
        }

        // 优先只数选项按钮；万一某个 mod 往容器里塞了装饰性 Control，不该被算进内容宽。
        List<Control> buttons = live.Where(control => control is NRestSiteButton).ToList();
        IReadOnlyList<Control> counted = buttons.Count > 0 ? buttons : live;
        liveButtonCount = counted.Count;
        if (counted.Count == 0)
        {
            return 0f;
        }

        List<float> widths = new(counted.Count);
        foreach (Control control in counted)
        {
            widths.Add(Math.Max(control.CustomMinimumSize.X, control.GetCombinedMinimumSize().X));
        }

        // 公式（Σ + (n−1)×间隔）与阈值都收在纯逻辑里，见 RestSiteOptionScrollPolicy + 它的单测。
        return RestSiteOptionScrollPolicy.ResolveContentWidth(widths, choicesContainer.GetThemeConstant("separation"));
    }

    /// <summary>
    /// 选项名字**挂在按钮下方**的那一段高度（原版靠"谁都不裁剪"才看得见，r242 被包装层的 `clip_contents` 裁掉）。
    ///
    /// 几何出处（`rest_site_button.tscn`）：按钮 `custom_minimum_size.y = 162.879`，
    /// `%Label` 是 `anchors_preset = 15` + **`offset_top = offset_bottom = 165`** ⇒ 它的矩形在按钮**下方**（高度 0），
    /// 文字从 y=165 开始往下画（字号 28 + outline 12）⇒ 比容器底还多出约 40~50px。
    /// 所以包装层必须**再往下加这么多**，否则文字下半截被裁、滚动条还会压在文字上（既挡字又挡点击）。
    /// </summary>
    private static float ResolveLabelBand(Control choicesContainer)
    {
        float containerHeight = choicesContainer.OffsetBottom - choicesContainer.OffsetTop;
        float contentBottom = containerHeight;
        bool measured = false;

        foreach (Node child in choicesContainer.GetChildren())
        {
            if (child is not NRestSiteButton button || !GodotObject.IsInstanceValid(button))
            {
                continue;
            }

            if (button.GetNodeOrNull<Control>("%Label") is not Control label)
            {
                continue;
            }

            // 文字高度用**主题字号 + 描边**算（场景：font_size 28 / outline_size 12），
            // 不要用 `GetCombinedMinimumSize().Y` —— 它把 `line_spacing = 40` 也算进去（偏大 30+px，
            // 会让包装层一直压到下面的 Description 文字上）。
            float fontPx = label.GetThemeFontSize("font_size");
            float outlinePx = Math.Max(0f, label.GetThemeConstant("outline_size"));
            float textHeight = fontPx > 0f ? fontPx + outlinePx + 6f : Math.Max(0f, label.GetCombinedMinimumSize().Y);
            contentBottom = Math.Max(contentBottom, label.Position.Y + textHeight);
            measured = true;
        }

        if (!measured)
        {
            // 一个名字标签都没量到（`%Label` 取不到时）⇒ 用保守常量，宁可多留一点也不要把字裁掉。
            return FallbackLabelBand;
        }

        return Math.Max(0f, contentBottom - containerHeight);
    }

    /// <summary>
    /// 🔴 **Reparent 会打断按钮的 `AnimateIn`，必须自己把 `mouse_filter` 补成 `Stop`**（r242 实测"点不了"的根因）。
    ///
    /// 链路：`rest_site_button.tscn` 里按钮是 `mouse_filter = 2 (Ignore)`，**唯一**把它翻成 `Stop` 的地方是
    /// <c>NRestSiteButton.AnimateIn()</c> 里 <c>await tween.AwaitFinished(_cts.Token)</c> **之后**那一行；
    /// 而 `Reparent` 必然触发一次 exit/enter tree ⇒ 按钮的 `_ExitTree()` 先 <c>_cts.Cancel()</c>
    /// ⇒ 那行可能永远不执行 ⇒ 按钮收不到 `MouseEntered` ⇒ <c>NClickableControl</c> 的按下/松开都要求
    /// <c>IsFocused</c>（由 hover 驱动）⇒ **整个按钮点不动**（r241 只有"没被包层的那个休息处"能点，正是这个原因）。
    ///
    /// 修复 = 照抄游戏那一行的结果（`MouseFilter = Stop`）。**只修 Ignore 的**（已经 Stop 的不动），
    /// 并把"修了几个"打出来 ⇒ 下次判读日志能直接确认这条假设。
    /// </summary>
    private static int RepairButtonClickability(Control choicesContainer)
    {
        int repaired = 0;
        foreach (Node child in choicesContainer.GetChildren())
        {
            if (child is not NRestSiteButton button || !GodotObject.IsInstanceValid(button))
            {
                continue;
            }

            if (button.MouseFilter == Control.MouseFilterEnum.Ignore)
            {
                button.MouseFilter = Control.MouseFilterEnum.Stop;
                repaired++;
            }
        }

        return repaired;
    }

    /// <summary>屏幕上"按钮不被切掉"的最大内容宽 = 视口宽 − 两侧边距。</summary>
    private static float ResolveVisibleLimit(NRestSiteRoom room)
    {
        Vector2 viewport = room.GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
        if (viewport.X <= 0f)
        {
            return 0f;
        }

        return Math.Max(0f, viewport.X - 2f * SideMargin);
    }

    /// <summary>已包好时：把内容宽同步给 HBox（选项增减都要跟），变了才打一条日志。</summary>
    private static void SyncWidth(Control choicesContainer, float requiredWidth, int liveButtonCount)
    {
        float previousWidth = choicesContainer.HasMeta(LastWidthMeta)
            ? choicesContainer.GetMeta(LastWidthMeta).AsSingle()
            : requiredWidth;

        choicesContainer.CustomMinimumSize = new Vector2(requiredWidth, choicesContainer.CustomMinimumSize.Y);

        if (Math.Abs(previousWidth - requiredWidth) > 1f)
        {
            choicesContainer.SetMeta(LastWidthMeta, requiredWidth);
            LocalMultiControlLogger.Info(
                $"[休息区滚动] 滚动内容宽已更新: {previousWidth:0} → {requiredWidth:0}, "
                + $"buttons={liveButtonCount}, nodes={choicesContainer.GetChildCount()}");
        }
    }

    private static void Wrap(
        NRestSiteRoom room,
        Control choicesContainer,
        Control choicesScreen,
        float requiredWidth,
        float visibleLimit,
        int liveButtonCount,
        float labelBand)
    {
        // 窗口宽度：能一屏看多少看多少，剩下的才滚（内容不超屏时根本不会走到这里）。
        float windowWidth = RestSiteOptionScrollPolicy.ResolveWindowWidth(requiredWidth, visibleLimit, ContentPadding);

        ScrollContainer wrapper = new()
        {
            Name = WrapperName,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            // 手柄左右切焦点时自动滚到那个按钮（不做的话手柄玩家够不到两端）。
            FollowFocus = true,
            // Pass：空白处的点击继续冒泡（不影响原版），滚轮在按钮上也能冒泡到我们这儿。
            MouseFilter = Control.MouseFilterEnum.Pass,
        };

        // 视觉位置：**纵向**沿用原容器（照抄锚点与偏移），**横向**按窗口宽居中。
        wrapper.AnchorLeft = choicesContainer.AnchorLeft;
        wrapper.AnchorTop = choicesContainer.AnchorTop;
        wrapper.AnchorRight = choicesContainer.AnchorRight;
        wrapper.AnchorBottom = choicesContainer.AnchorBottom;
        wrapper.OffsetLeft = -windowWidth / 2f;
        wrapper.OffsetTop = choicesContainer.OffsetTop;
        wrapper.OffsetRight = windowWidth / 2f;
        wrapper.OffsetBottom = choicesContainer.OffsetBottom;
        wrapper.GrowHorizontal = choicesContainer.GrowHorizontal;
        wrapper.GrowVertical = choicesContainer.GrowVertical;

        choicesScreen.AddChild(wrapper);

        // 向下加高：① 选项名字那段（挂在按钮下方，见 ResolveLabelBand）② 滚动条自己那一行
        //（进树后主题才生效 ⇒ 这时候问真实高度；问不到就用兜底常量）。
        // ⚠ 顺序很重要：**先留名字、再留滚动条** ⇒ 滚动条落在文字**下面**（r242 实测它压在文字上：挡字又挡点击）。
        wrapper.OffsetBottom = choicesContainer.OffsetBottom
            + labelBand
            + ResolveScrollbarReserve(wrapper);

        // 把原容器搬进包装层，并让它铺满（宽度由 ScrollContainer 按"最小宽度"决定 ⇒ 真溢出才横向变宽）。
        choicesContainer.Reparent(wrapper, keepGlobalTransform: false);
        choicesContainer.AnchorLeft = 0f;
        choicesContainer.AnchorTop = 0f;
        choicesContainer.AnchorRight = 1f;
        choicesContainer.AnchorBottom = 1f;
        choicesContainer.OffsetLeft = 0f;
        choicesContainer.OffsetTop = 0f;
        choicesContainer.OffsetRight = 0f;
        choicesContainer.OffsetBottom = 0f;
        choicesContainer.CustomMinimumSize = new Vector2(requiredWidth, choicesContainer.CustomMinimumSize.Y);
        choicesContainer.SetMeta(LastWidthMeta, requiredWidth);

        // 🔴 Reparent 打断了按钮的 `AnimateIn`（见 RepairButtonClickability 注释）⇒ 自己把可点性补回来。
        int repaired = RepairButtonClickability(choicesContainer);

        wrapper.ScrollHorizontal = 0;
        HookContainerChanges(room, choicesContainer);

        LocalMultiControlLogger.Info(
            $"[休息区滚动] 选项已溢出屏幕，已启用横向滚动条（可拖动 / 可滚轮）: "
            + $"需要宽={requiredWidth:0}, 窗口宽={windowWidth:0}, 屏幕可用={visibleLimit:0}, "
            + $"buttons={liveButtonCount}, nodes={choicesContainer.GetChildCount()}, "
            + $"名字带={labelBand:0}, 可点性已修={repaired}, "
            + $"separator={choicesContainer.GetThemeConstant("separation")}");
    }

    /// <summary>
    /// 挂"子节点变动 ⇒ 下一帧重算内容宽"。用**树进出**信号（而不是 `QueueFree` 当帧）是刻意的：
    /// 游戏换选项时旧按钮先在 `QueueFree` 里躺一帧 —— 那正是 r241 算错的时刻；
    /// 等它们**真的退出树**再重算，宽度就永远是"当前真实选项数"。
    /// </summary>
    private static void HookContainerChanges(NRestSiteRoom room, Control choicesContainer)
    {
        if (choicesContainer.HasMeta(HookedMeta))
        {
            return;
        }

        choicesContainer.SetMeta(HookedMeta, true);
        choicesContainer.ChildEnteredTree += _ => ScheduleResync(room);
        choicesContainer.ChildExitingTree += _ => ScheduleResync(room);
    }

    private static void ScheduleResync(NRestSiteRoom room)
    {
        Callable.From(delegate
        {
            TryEnsureScrollable(room);
        }).CallDeferred();
    }

    /// <summary>滚动条高度（问**已经在树里**的包装层自己的滚动条；取不到就用兜底常量）。</summary>
    private static float ResolveScrollbarReserve(ScrollContainer wrapper)
    {
        try
        {
            Vector2 minSize = wrapper.GetHScrollBar()?.GetCombinedMinimumSize() ?? Vector2.Zero;
            return minSize.Y > 0f ? minSize.Y + 4f : FallbackScrollbarReserve;
        }
        catch (Exception)
        {
            return FallbackScrollbarReserve;
        }
    }
}
