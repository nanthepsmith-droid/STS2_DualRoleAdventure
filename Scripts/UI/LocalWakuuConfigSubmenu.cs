using Godot;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace LocalMultiControl.Scripts.UI;

/// <summary>
/// 瓦库托管设置页：一个真正的 NSubmenu 子菜单（BaseLib 的 NModConfigSubmenu 同款思路），
/// 由注入按钮经 PushSubmenuType 推入原版子菜单栈，观感与游戏自带设置页一致：
/// 无黑幕弹层、无浮动圆角面板，原生返回按钮 + 居中内容列。
/// 开关控件沿用游戏原生外观勾选框（LocalWakuuConfigTickbox），
/// 改动经 LocalWakuuAutopilotConfig.TrySetAndSave 即时写回 vakuu_autopilot.json。
/// </summary>
internal sealed partial class LocalWakuuConfigSubmenu : NSubmenu
{
    private LocalWakuuConfigTickbox? _firstToggle;

    public LocalWakuuConfigSubmenu()
    {
        // NSubmenu 是全屏 Control；由子菜单栈负责 Visible 切换
        SetAnchorsPreset(LayoutPreset.FullRect);
        GrowHorizontal = GrowDirection.End;
        GrowVertical = GrowDirection.End;
        MouseFilter = MouseFilterEnum.Stop;
    }

    protected override Control? InitialFocusedControl => _firstToggle;

    /// <summary>
    /// 注意：NSubmenu 基类约定子类不要调 base._Ready()，改为直接调 ConnectSignals()；
    /// ConnectSignals 会按节点名 "BackButton" 找原生返回按钮并接上 _stack.Pop()，
    /// 因此返回按钮必须先于本调用加入子树。
    /// </summary>
    public override void _Ready()
    {
        AddChild(CreateBackButton());
        ConnectSignals();
        BuildContent();
        LocalMultiControlLogger.Info("瓦库托管设置子菜单已构建");
    }

    private static NBackButton CreateBackButton()
    {
        PackedScene? scene = ResourceLoader.Load<PackedScene>(
            SceneHelper.GetScenePath("ui/back_button"), null, ResourceLoader.CacheMode.Reuse);
        NBackButton backButton = scene != null
            ? scene.Instantiate<NBackButton>()
            : new NBackButton();
        backButton.Name = "BackButton";
        return backButton;
    }

    private void BuildContent()
    {
        CenterContainer contentSlot = new()
        {
            Name = "Content",
            // 穿透鼠标：不挡住左下角的原生返回按钮
            MouseFilter = MouseFilterEnum.Ignore,
        };
        contentSlot.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(contentSlot);

        VBoxContainer column = new()
        {
            Name = "Column",
        };
        column.AddThemeConstantOverride("separation", 14);
        contentSlot.AddChild(column);

        Label title = CreateLabel("瓦 库 托 管", 42, new Color(1f, 0.95f, 0.75f));
        column.AddChild(title);
        column.AddChild(CreateDivider(new Color(0.55f, 0.45f, 0.25f, 0.9f)));
        column.AddChild(CreateLabel(
            "改动立即保存到 vakuu_autopilot.json（总开关在下一局开局时生效）。",
            20, new Color(0.72f, 0.72f, 0.72f)));
        column.AddChild(CreateSpacer(10));

        AddToggleRow(column,
            "瓦库形态托管（总开关）",
            "开启后勾选瓦库的角色改发【瓦库形态】遗物并进入托管；关闭时保持原版\"永久低语耳环\"行为。",
            () => LocalWakuuAutopilotConfig.UseVakuuForm,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("useVakuuForm", value));
        AddToggleRow(column,
            "打光所有手牌",
            "瓦库每回合自动出完所有可出牌；关闭时沿用原版低语耳环每回合最多 13 张的上限。",
            () => LocalWakuuAutopilotConfig.PlayAllCards,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("playAllCards", value));
        AddToggleRow(column,
            "后台托管（不切前台）",
            "瓦库回合不再强制切换到该角色视角，全程后台自动出牌与结束回合。",
            () => LocalWakuuAutopilotConfig.BackgroundMode,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("backgroundMode", value));
        AddToggleRow(column,
            "压制原版低语耳环",
            "持有【瓦库形态】时，局内再获得的原版低语耳环只保留 +1 能量，不再重复触发自动出牌。",
            () => LocalWakuuAutopilotConfig.SuppressVanillaEarring,
            value => LocalWakuuAutopilotConfig.TrySetAndSave("suppressVanillaEarring", value));

        column.AddChild(CreateSpacer(6));
        column.AddChild(CreateDivider(new Color(0.35f, 0.35f, 0.35f, 0.7f)));
        column.AddChild(CreateLabel("点左下角返回按钮或再次打开本页可随时退出。", 18, new Color(0.6f, 0.6f, 0.6f)));
    }

    private void AddToggleRow(VBoxContainer column, string title, string description, Func<bool> getter, Action<bool> setter)
    {
        HBoxContainer row = new();
        row.AddThemeConstantOverride("separation", 28);

        VBoxContainer textColumn = new();
        textColumn.CustomMinimumSize = new Vector2(880f, 0f);
        textColumn.SizeFlagsHorizontal = (SizeFlags)3; // ExpandFill
        textColumn.AddThemeConstantOverride("separation", 2);

        Label titleLabel = CreateLabel(title, 26, new Color(1f, 0.85f, 0.35f));
        titleLabel.HorizontalAlignment = HorizontalAlignment.Left;
        textColumn.AddChild(titleLabel);

        Label descLabel = CreateLabel(description, 19, new Color(0.8f, 0.78f, 0.72f));
        descLabel.HorizontalAlignment = HorizontalAlignment.Left;
        descLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        textColumn.AddChild(descLabel);

        row.AddChild(textColumn);

        LocalWakuuConfigTickbox tickbox = new(getter, setter)
        {
            SizeFlagsVertical = (SizeFlags)4, // ShrinkCenter：勾选框在行内垂直居中
        };
        _firstToggle ??= tickbox;
        row.AddChild(tickbox);

        column.AddChild(row);
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

    private static ColorRect CreateDivider(Color color)
    {
        return new ColorRect
        {
            Color = color,
            CustomMinimumSize = new Vector2(0f, 2f),
        };
    }

    private static Control CreateSpacer(float height)
    {
        return new Control { CustomMinimumSize = new Vector2(0f, height) };
    }
}
