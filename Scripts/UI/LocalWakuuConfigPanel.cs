using System;
using Godot;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace LocalMultiControl.Scripts.UI;

/// <summary>
/// 瓦库托管开关面板：纯代码构建的模态弹层（参考"古明地恋"mod 的 KoishiConfigUI）。
/// 结构：半透明黑幕 ColorRect → CenterContainer → 本面板（深色圆角边框）。
/// 每个开关一行：左侧标题+说明文字，右侧游戏原生外观的勾选框。
/// 改动经 LocalWakuuAutopilotConfig.TrySetAndSave 即时写回 vakuu_autopilot.json 并刷新内存生效值；
/// 总开关 useVakuuForm 因涉及开局发遗物，下一局生效。
/// </summary>
internal sealed partial class LocalWakuuConfigPanel : PanelContainer
{
    [Signal]
    public delegate void CloseRequestedEventHandler();

    private const string ModalName = "VakuuConfigModal";

    public override void _Ready()
    {
        BuildUi();
        LocalMultiControlLogger.Info("瓦库托管设置面板已打开");
    }

    /// <summary>把面板包进黑幕弹层并挂到场景根；重复调用不会叠开多个。</summary>
    public static void OpenModal()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            LocalMultiControlLogger.Warn("无法打开瓦库托管设置面板：场景树不可用");
            return;
        }

        if (tree.Root.GetNodeOrNull(ModalName) != null)
        {
            return; // 已打开，避免叠加
        }

        LocalWakuuConfigModal modal = new()
        {
            Name = ModalName,
            Color = new Color(0f, 0f, 0f, 0.7f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        modal.SetAnchorsPreset(LayoutPreset.FullRect);
        modal.CloseRequested += () => modal.QueueFree();

        CenterContainer center = new()
        {
            // 穿透鼠标：点击面板外的黑幕区域时事件落到下层 ColorRect，触发"点空白关闭"
            MouseFilter = MouseFilterEnum.Ignore,
        };
        center.SetAnchorsPreset(LayoutPreset.FullRect);

        LocalWakuuConfigPanel panel = new();
        panel.CloseRequested += modal.Close;
        center.AddChild(panel);
        modal.AddChild(center);
        tree.Root.AddChild(modal);
    }

    private void BuildUi()
    {
        AddThemeStyleboxOverride("panel", CreatePanelStyle());

        MarginContainer margin = new()
        {
            Name = "Margin",
        };
        margin.AddThemeConstantOverride("margin_left", 48);
        margin.AddThemeConstantOverride("margin_right", 48);
        margin.AddThemeConstantOverride("margin_top", 36);
        margin.AddThemeConstantOverride("margin_bottom", 36);
        AddChild(margin);

        VBoxContainer root = new()
        {
            Name = "Root",
        };
        root.AddThemeConstantOverride("separation", 16);
        margin.AddChild(root);

        root.AddChild(CreateLabel("瓦库托管 · 设置", 40, new Color(1f, 0.92f, 0.6f)));
        root.AddChild(CreateDivider());
        root.AddChild(CreateLabel(
            "开关改动立即保存到 vakuu_autopilot.json 并即刻生效（总开关在下一局开局时生效）。",
            20, new Color(0.75f, 0.75f, 0.75f)));

        root.AddChild(CreateToggleRow(
            "瓦库形态托管（总开关）",
            "开启后勾选瓦库的角色改发【瓦库形态】遗物并进入托管；关闭时保持原版\"永久低语耳环\"行为。",
            () => LocalWakuuAutopilotConfig.UseVakuuForm,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("useVakuuForm", value)));

        root.AddChild(CreateToggleRow(
            "打光所有手牌",
            "瓦库每回合自动出完所有可出牌；关闭时沿用原版低语耳环每回合最多 13 张的上限。",
            () => LocalWakuuAutopilotConfig.PlayAllCards,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("playAllCards", value)));

        root.AddChild(CreateToggleRow(
            "后台托管（不切前台）",
            "瓦库回合不再强制切换到该角色视角，全程后台自动出牌与结束回合。",
            () => LocalWakuuAutopilotConfig.BackgroundMode,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("backgroundMode", value)));

        root.AddChild(CreateToggleRow(
            "压制原版低语耳环",
            "持有【瓦库形态】时，局内再获得的原版低语耳环只保留 +1 能量，不再重复触发自动出牌。",
            () => LocalWakuuAutopilotConfig.SuppressVanillaEarring,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("suppressVanillaEarring", value)));

        root.AddChild(CreateDivider());

        CenterContainer closeButtonSlot = new();
        LocalSimpleTextButton closeButton = new()
        {
            ButtonText = "关 闭",
            FontSize = 28,
        };
        closeButton.CustomMinimumSize = new Vector2(240f, 72f);
        closeButton.Connect(NClickableControl.SignalName.Released, Callable.From<NClickableControl>(_ => EmitSignal(SignalName.CloseRequested)));
        closeButtonSlot.AddChild(closeButton);
        root.AddChild(closeButtonSlot);

        // 面板尺寸：内容宽约 1176(文字列)+24(间隔)+324(勾选框)+左右边距，取整留余量
        CustomMinimumSize = new Vector2(1660f, 0f);
        SizeFlagsHorizontal = (SizeFlags)4; // ShrinkCenter（父容器是 CenterContainer 时仅影响测量）
        SizeFlagsVertical = (SizeFlags)4;
    }

    private Control CreateToggleRow(string title, string description, Func<bool> getter, Action<bool> setter)
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 24);

        VBoxContainer textColumn = new();
        textColumn.CustomMinimumSize = new Vector2(1176f, 0f);
        textColumn.SizeFlagsHorizontal = (SizeFlags)3; // ExpandFill
        textColumn.AddThemeConstantOverride("separation", 4);

        Label titleLabel = CreateLabel(title, 28, new Color(1f, 0.85f, 0.2f));
        titleLabel.HorizontalAlignment = HorizontalAlignment.Left;
        textColumn.AddChild(titleLabel);

        Label descLabel = CreateLabel(description, 21, new Color(0.82f, 0.82f, 0.82f));
        descLabel.HorizontalAlignment = HorizontalAlignment.Left;
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        textColumn.AddChild(descLabel);

        row.AddChild(textColumn);
        row.AddChild(new LocalWakuuConfigTickbox(getter, setter)
        {
            SizeFlagsVertical = (SizeFlags)4, // ShrinkCenter：勾选框在行内垂直居中
        });
        return row;
    }

    private static Label CreateLabel(string text, int fontSize, Color color)
    {
        Label label = new()
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static ColorRect CreateDivider()
    {
        return new ColorRect
        {
            Color = new Color(0.35f, 0.35f, 0.35f, 0.8f),
            CustomMinimumSize = new Vector2(0f, 2f),
        };
    }

    private static StyleBoxFlat CreatePanelStyle()
    {
        StyleBoxFlat style = new()
        {
            BgColor = new Color(0.08f, 0.08f, 0.08f, 1f),
        };
        style.SetCornerRadiusAll(24);
        style.SetBorderWidthAll(2);
        style.BorderColor = new Color(0.3f, 0.3f, 0.3f, 1f);
        return style;
    }
}

/// <summary>
/// 设置面板的黑幕层：点击空白处或按 ESC 关闭（走 CloseRequested 统一收口）。
/// </summary>
internal sealed partial class LocalWakuuConfigModal : ColorRect
{
    [Signal]
    public delegate void CloseRequestedEventHandler();

    public void Close()
    {
        QueueFree();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            EmitSignal(SignalName.CloseRequested);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            EmitSignal(SignalName.CloseRequested);
            // 标记已处理，避免 ESC 同时把底层设置界面也关掉
            GetViewport().SetInputAsHandled();
        }
    }
}
