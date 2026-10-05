using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>选玩家选择器的一个候选（NetId + 展示标签）。</summary>
internal readonly struct LocalPlayerPickerEntry
{
    internal LocalPlayerPickerEntry(ulong netId, string label)
    {
        NetId = netId;
        Label = label;
    }

    internal ulong NetId { get; }

    internal string Label { get; }
}

/// <summary>
/// 局内「选玩家」选择器（瓦库四功能共用基建，2026-10-05 用户拍板新建）。
///
/// 背景：四个功能（炼化 / 净化 / 我们联合 / 地狱战神）都需要「先选一个瓦库」这一步，
/// 而 mod 全局**没有**任何局内选玩家的现成 UI（核验报告 §6.1）。核验原本建议"按瓦库各生成一条
/// 休息处选项"以回避造 UI，但用户拍板要一个**统一的选择器**覆盖四个功能，故本类提供该基建。
///
/// 形态：代码自建的模态弹层（dll-only mod 无法新增 .tscn 场景，所以全部节点在代码里构造）——
/// 半透明背幕 + 居中面板 + 标题 + 每个候选一个按钮 + 取消按钮，沿用设置页同款
/// <see cref="LocalSimpleTextButton"/> 观感。挂在 <c>NGame</c> 下并置于高层
/// <c>ZIndex</c>，因此盖在休息区房间 UI 之上。
///
/// 契约：
/// <list type="bullet">
/// <item>返回选中的 NetId；取消 / 无人可选 / 节点被外部移除（如换房间清场）一律返回 <c>null</c>；</item>
/// <item><b>绝不挂死</b>：<c>_ExitTree</c> 里补一次结果，调用方的 await 一定会回来；</item>
/// <item>调用方必须 <c>await</c>，并在 <c>null</c> 时把"这次没选"当正常取消（返回 false 让选项不被消费）。</item>
/// </list>
/// </summary>
internal static class LocalWakuuPlayerPicker
{
    /// <summary>
    /// 弹出选择器并等待真人选择。返回 null = 取消 / 不可用。
    /// </summary>
    internal static async Task<ulong?> PickAsync(string title, IReadOnlyList<LocalPlayerPickerEntry> entries)
    {
        if (entries == null || entries.Count == 0)
        {
            return null;
        }

        NGame? game = NGame.Instance;
        if (game == null)
        {
            LocalMultiControlLogger.Warn("选玩家选择器打开失败：NGame 不可用。");
            return null;
        }

        LocalWakuuPlayerPickerOverlay overlay = new(title, entries);
        game.AddChildSafely(overlay);
        LocalMultiControlLogger.Info($"选玩家选择器已打开: title={title}, candidates={entries.Count}");

        ulong? chosen = await overlay.WaitForSelectionAsync();
        LocalMultiControlLogger.Info(
            $"选玩家选择器已关闭: title={title}, chosen={(chosen.HasValue ? chosen.Value.ToString() : "null")}");
        return chosen;
    }
}

/// <summary>选玩家选择器的模态弹层节点（见 <see cref="LocalWakuuPlayerPicker"/>）。</summary>
internal sealed partial class LocalWakuuPlayerPickerOverlay : Control
{
    private const int PanelWidth = 600;
    private const float PanelInnerPadding = 26f;

    private readonly TaskCompletionSource<ulong?> _completion = new();
    private readonly string _title;
    private readonly IReadOnlyList<LocalPlayerPickerEntry> _entries;

    private LocalSimpleTextButton? _firstButton;
    private bool _closed;

    internal LocalWakuuPlayerPickerOverlay(string title, IReadOnlyList<LocalPlayerPickerEntry> entries)
    {
        _title = title;
        _entries = entries;
        Name = "LocalWakuuPlayerPicker";
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        // 盖在房间 UI 之上（同画布内的绘制顺序）
        ZIndex = 100;
    }

    internal Task<ulong?> WaitForSelectionAsync()
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
            LocalMultiControlLogger.Warn($"选玩家选择器构建失败: {exception.Message}");
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

        foreach (LocalPlayerPickerEntry entry in _entries)
        {
            ulong netId = entry.NetId;
            LocalSimpleTextButton button = CreateRowButton(entry.Label, 24, 58f);
            button.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ => Close(netId)));
            column.AddChild(button);
            _firstButton ??= button;
        }

        LocalSimpleTextButton cancel = CreateRowButton(LocalModText.Cancel, 22, 48f);
        cancel.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ => Close(null)));
        column.AddChild(cancel);
        _firstButton ??= cancel;
    }

    private static LocalSimpleTextButton CreateRowButton(string text, int fontSize, float height)
    {
        return new LocalSimpleTextButton
        {
            ButtonText = text,
            FontSize = fontSize,
            CustomMinimumSize = new Vector2(PanelWidth - (PanelInnerPadding * 2f), height),
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
    private void Close(ulong? result)
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
