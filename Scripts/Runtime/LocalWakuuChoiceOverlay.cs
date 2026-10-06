#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 通用「局内选项选择弹层」——瓦库四功能自建 UI 的共用基建（dll-only mod 无法新增 .tscn 场景，
/// 所以全部节点在代码里构造）：半透明背幕 + 居中面板 + 标题 +（可滚动的）选项列表 + 确认/取消，
/// 沿用设置页同款 <see cref="LocalSimpleTextButton"/> 观感，挂在 <c>NGame</c> 下并置于高层
/// <c>ZIndex</c>（盖在休息区 / 战斗 UI 之上）。
///
/// 两种模式：
/// <list type="bullet">
/// <item><b>单选</b>（<see cref="LocalWakuuChoiceOverlay.PickIndexAsync"/>）：点一下即选中并关闭，
///   返回**一个**下标；</item>
/// <item><b>多选</b>（<see cref="LocalWakuuChoiceOverlay.PickIndexesAsync"/>）：行可反复勾选/取消，
///   点「确认」才关闭，返回**选中的下标列表**（可以是空列表 = "确认了什么都不选"）；
///   勾选数被 <c>maxSelect</c> 卡住（到上限后再点未选行会被忽略，标题下方实时显示 `已选 x/N`）。</item>
/// </list>
///
/// 返回值由调用方映射成自己的载荷：
/// <list type="bullet">
/// <item><see cref="LocalWakuuPlayerPicker"/> → 席位 NetId；</item>
/// <item><see cref="LocalWakuuRelicPicker"/> → 遗物实例；</item>
/// <item><see cref="LocalWakuuPotionPicker"/> → 药水实例（可多瓶）。</item>
/// </list>
///
/// ⚠ 列表放进 <c>ScrollContainer</c> 并按内容/上限取高（瓦库 20+ 件遗物也点得到）；
/// 键盘/手柄移动时 <c>FollowFocus</c> 会自动把焦点项滚进视野。
///
/// 契约（沿袭自选玩家选择器）：
/// <list type="bullet">
/// <item><b>绝不挂死</b>：<c>_ExitTree</c> 里补一次结果，调用方的 await 一定会回来；</item>
/// <item>调用方必须 <c>await</c>，并在 <c>null</c> 时把"这次没选"当正常取消。</item>
/// </list>
/// </summary>
internal static class LocalWakuuChoiceOverlay
{
    /// <summary>弹出**单选**弹层并等待真人选择。返回选中项下标；null = 取消 / 不可用。</summary>
    internal static async Task<int?> PickIndexAsync(string title, IReadOnlyList<string> labels)
    {
        IReadOnlyList<int>? indexes = await PickIndexesAsync(title, labels, multiSelect: false, maxSelect: 1);
        return indexes is { Count: > 0 } ? indexes[0] : null;
    }

    /// <summary>
    /// 弹出**多选**弹层并等待真人确认。返回选中的下标列表（可能为空 = 确认了"什么都没选"）；
    /// null = 取消 / 不可用。单选模式（<paramref name="multiSelect"/>=false）点一下即关闭。
    /// </summary>
    internal static async Task<IReadOnlyList<int>?> PickIndexesAsync(
        string title,
        IReadOnlyList<string> labels,
        bool multiSelect,
        int maxSelect)
    {
        if (labels == null || labels.Count == 0)
        {
            return null;
        }

        NGame? game = NGame.Instance;
        if (game == null)
        {
            LocalMultiControlLogger.Warn($"选项选择器打开失败：NGame 不可用。title={title}");
            return null;
        }

        int limit = multiSelect ? Math.Max(maxSelect, 1) : 1;
        LocalWakuuChoiceOverlayNode overlay = new(title, labels, multiSelect, limit);
        game.AddChildSafely(overlay);

        return await overlay.WaitForSelectionAsync();
    }
}

/// <summary>通用选项选择弹层的节点（见 <see cref="LocalWakuuChoiceOverlay"/>）。</summary>
internal sealed partial class LocalWakuuChoiceOverlayNode : Control
{
    private const int PanelWidth = 600;
    private const float PanelInnerPadding = 26f;

    /// <summary>单个选项按钮的高度。</summary>
    private const float RowHeight = 58f;

    /// <summary>选项之间的间距。</summary>
    private const float RowSeparation = 10f;

    /// <summary>
    /// 选项列表**最大高度**（约 1080 屏高的 48%）；内容超过它就在列表内滚动。
    /// 目的：候选多时（如瓦库遗物 20+ 件）不再"一页放不下"、下方的候选点不到。
    /// </summary>
    private const float MaxListHeight = 520f;

    private readonly TaskCompletionSource<IReadOnlyList<int>?> _completion = new();
    private readonly string _title;
    private readonly IReadOnlyList<string> _labels;
    private readonly bool _multiSelect;
    private readonly int _maxSelect;
    private readonly List<LocalSimpleTextButton> _rowButtons = new();
    private readonly HashSet<int> _selected = new();

    private LocalSimpleTextButton? _firstButton;
    private LocalSimpleTextButton? _confirmButton;
    private Label? _hintLabel;
    private bool _closed;

    internal LocalWakuuChoiceOverlayNode(string title, IReadOnlyList<string> labels, bool multiSelect, int maxSelect)
    {
        _title = title;
        _labels = labels;
        _multiSelect = multiSelect;
        _maxSelect = maxSelect;
        Name = "LocalWakuuChoiceOverlay";
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        // 盖在房间 UI 之上（同画布内的绘制顺序）
        ZIndex = 100;
    }

    internal Task<IReadOnlyList<int>?> WaitForSelectionAsync()
    {
        return _completion.Task;
    }

    public override void _Ready()
    {
        try
        {
            BuildVisualTree();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"选项选择器构建失败: {exception.Message}");
            Close(null);
            return;
        }

        _firstButton?.GrabFocus();
    }

    public override void _ExitTree()
    {
        // 兜底：节点被外部移除 / 清场时也要解开等待，绝不让上层流程挂死。
        if (!_closed)
        {
            _closed = true;
            _completion.TrySetResult(null);
        }
    }

    private void BuildVisualTree()
    {
        ColorRect backdrop = new()
        {
            Name = "Backdrop",
            Color = new Color(0f, 0f, 0f, 0.62f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        CenterContainer center = new()
        {
            Name = "Center",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        PanelContainer panel = new()
        {
            Name = "Panel",
            CustomMinimumSize = new Vector2(PanelWidth, 0f),
        };
        panel.AddThemeStyleboxOverride("panel", CreatePanelStyle());
        center.AddChild(panel);

        VBoxContainer column = new()
        {
            Name = "Column",
        };
        column.AddThemeConstantOverride("separation", 14);
        panel.AddChild(column);

        Label title = new()
        {
            Name = "Title",
            Text = _title,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.75f));
        column.AddChild(title);

        if (_multiSelect)
        {
            // 多选：把"最多能拿几件"和"当前选了几件"摆在标题下，避免玩家选了拿不走的数量。
            _hintLabel = new Label
            {
                Name = "Hint",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            _hintLabel.AddThemeFontSizeOverride("font_size", 20);
            _hintLabel.AddThemeColorOverride("font_color", new Color(0.78f, 0.82f, 0.9f));
            column.AddChild(_hintLabel);
        }

        // 选项列表放进 ScrollContainer 并**按内容/上限取高**：候选多时（如瓦库遗物 20+ 件）
        // 不再一页放不下 —— 之前直接把按钮全铺在列里，超出屏幕的候选根本点不到。
        ScrollContainer scroll = new()
        {
            Name = "Scroll",
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            FollowFocus = true, // 键盘/手柄上下移动时自动把焦点项滚进视野
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        scroll.CustomMinimumSize = new Vector2(PanelWidth - (PanelInnerPadding * 2f), ResolveListHeight(_labels.Count));
        column.AddChild(scroll);

        VBoxContainer list = new()
        {
            Name = "List",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        list.AddThemeConstantOverride("separation", (int)RowSeparation);
        scroll.AddChild(list);

        for (int index = 0; index < _labels.Count; index++)
        {
            int captured = index;
            LocalSimpleTextButton button = CreateRowButton(RowText(index), 24, RowHeight);
            button.Connect(
                NClickableControl.SignalName.Released,
                Callable.From<NClickableControl>(_ => OnRowPressed(captured)));
            list.AddChild(button);
            _rowButtons.Add(button);
            _firstButton ??= button;
        }

        if (_multiSelect)
        {
            _confirmButton = CreateRowButton(ConfirmText(), 24, 54f);
            _confirmButton.Connect(
                NClickableControl.SignalName.Released,
                Callable.From<NClickableControl>(_ => Close(_selected.ToList())));
            column.AddChild(_confirmButton);
        }

        LocalSimpleTextButton cancel = CreateRowButton(LocalModText.Cancel, 22, 48f);
        cancel.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ => Close(null)));
        column.AddChild(cancel);
        _firstButton ??= cancel;

        RefreshMultiSelectTexts();
    }

    /// <summary>行被点：单选直接落选并关闭；多选切换勾选（到上限后忽略新增）。</summary>
    private void OnRowPressed(int index)
    {
        if (_closed)
        {
            return;
        }

        if (!_multiSelect)
        {
            Close(new[] { index });
            return;
        }

        if (_selected.Contains(index))
        {
            _selected.Remove(index);
        }
        else if (_selected.Count >= _maxSelect)
        {
            // 到上限：忽略本次勾选（提示文案里已写明上限），并打一条便于核对的日志。
            LocalMultiControlLogger.Info(
                $"选药水/选多项：已达上限 {_maxSelect}，忽略本次勾选。title={_title}, index={index}");
            return;
        }
        else
        {
            _selected.Add(index);
        }

        RefreshMultiSelectTexts();
    }

    private void RefreshMultiSelectTexts()
    {
        if (!_multiSelect)
        {
            return;
        }

        for (int index = 0; index < _rowButtons.Count; index++)
        {
            _rowButtons[index].ButtonText = RowText(index);
        }

        if (_hintLabel != null)
        {
            _hintLabel.Text = HintText();
        }

        if (_confirmButton != null)
        {
            _confirmButton.ButtonText = ConfirmText();
        }
    }

    /// <summary>行文案：多选时未选行用全角括号占位，保持两态等宽（勾选后文字不跳）。</summary>
    private string RowText(int index)
    {
        string label = _labels[index];
        if (!_multiSelect)
        {
            return label;
        }

        return _selected.Contains(index) ? $"【已选】{label}" : $"【　　】{label}";
    }

    private string HintText()
    {
        return $"已选 {_selected.Count}/{_maxSelect}";
    }

    private string ConfirmText()
    {
        return _selected.Count > 0 ? $"确认（{_selected.Count} 项）" : "确认（不拿）";
    }

    /// <summary>列表高度 = 内容高度（候选少时贴着内容），但不超过 <see cref="MaxListHeight"/>。</summary>
    private static float ResolveListHeight(int rowCount)
    {
        if (rowCount <= 0)
        {
            return RowHeight;
        }

        float content = (rowCount * RowHeight) + (Math.Max(rowCount - 1, 0) * RowSeparation);
        return Math.Min(content, MaxListHeight);
    }

    private static LocalSimpleTextButton CreateRowButton(string text, int fontSize, float height)
    {
        return new LocalSimpleTextButton
        {
            ButtonText = text,
            FontSize = fontSize,
            // 预留 18px 给滚动条（列表出现滚动条时按钮不会被压到/盖住）。
            CustomMinimumSize = new Vector2(PanelWidth - (PanelInnerPadding * 2f) - 18f, height),
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
    }

    private static StyleBoxFlat CreatePanelStyle()
    {
        StyleBoxFlat style = new()
        {
            BgColor = new Color(0.08f, 0.09f, 0.11f, 0.96f),
            BorderColor = new Color(0.55f, 0.45f, 0.25f, 0.9f),
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(10);
        style.SetContentMarginAll(PanelInnerPadding);
        return style;
    }

    /// <summary>
    /// 收尾：先把节点移出场景树（避免上层流程继续跑时弹层还留在屏幕上），再解开等待。
    /// 结果只写一次（<c>_closed</c> 守卫），与 <c>_ExitTree</c> 的兜底互不打架。
    /// </summary>
    private void Close(IReadOnlyList<int>? result)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;

        Node? parent = GetParent();
        parent?.RemoveChild(this);
        if (IsInstanceValid(this))
        {
            QueueFree();
        }

        _completion.TrySetResult(result);
    }
}
