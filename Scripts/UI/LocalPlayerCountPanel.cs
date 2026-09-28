using System;
using Godot;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace LocalMultiControl.Scripts.UI;

/// <summary>
/// 大厅页「本地人数 -/+ · 切换编辑席位 ◀/▶」面板的差异项（R2 去重复）。
///
/// 三份实现的差异只有这几项，其余骨架（控件尺寸 / 行距列距 / 位置算式 / 回调 / 移除）全部共用：
/// ① 面板与按钮的节点名前缀；② 人数调整日志的 source 串与文案；③ 面板落点；
/// ④ 标准角色选择页额外的手柄提示图标与「刚建好/刚调整完」的钩子。
/// </summary>
internal sealed class LocalPlayerCountPanelOptions
{
    internal LocalPlayerCountPanelOptions(
        string namePrefix,
        string sourcePrefix,
        string successLogText,
        Func<Vector2, Vector2, Node, Vector2?> resolvePosition)
    {
        NamePrefix = namePrefix;
        SourcePrefix = sourcePrefix;
        SuccessLogText = successLogText;
        ResolvePosition = resolvePosition;
    }

    /// <summary>`LocalSelfCoop` / `LocalDailyRun` / `LocalCustomRun` —— 拼出面板与按钮的节点名。</summary>
    internal string NamePrefix { get; }

    /// <summary>`ui-button` / `daily-ui-button` / `custom-ui-button` —— 人数调整日志的来源。</summary>
    internal string SourcePrefix { get; }

    /// <summary>成功日志前缀，后面接 `: N`。</summary>
    internal string SuccessLogText { get; }

    /// <summary>面板落点；返回 null 表示本帧不布置（标准页确认按钮还没就绪）。</summary>
    internal Func<Vector2, Vector2, Node, Vector2?> ResolvePosition { get; }

    /// <summary>面板尺寸被收敛后的目标人数（每日页要 clamp 到 4）。</summary>
    internal Func<int, int> ClampTargetCount { get; init; } = static count => count;

    /// <summary>布局完成后的钩子（标准页用它刷新手柄提示图标）。参数 = 面板、第二列 X、第二行 Y。</summary>
    internal Action<Control, float, float>? AfterLayout { get; init; }

    /// <summary>面板刚建好并挂上宿主后的钩子（标准页在此补提示图标与建好日志）。</summary>
    internal Action<Control>? AfterPanelCreated { get; init; }

    /// <summary>人数调整成功后的钩子（标准页据此立刻重排一次）。</summary>
    internal Action? AfterAdjust { get; init; }

    /// <summary>首次布局日志（每日页的「人数面板已注入」）。参数 = 视口尺寸、落点、面板尺寸。</summary>
    internal Action<Vector2, Vector2, Vector2>? FirstLayoutLog { get; init; }

    /// <summary>右下角落点（标准页 / 自定义页），带 <see cref="LocalPlayerCountPanel.EdgeMargin"/> 边距。</summary>
    internal static Vector2? BottomRight(Vector2 viewportSize, Vector2 panelSize, Node screen)
    {
        return new Vector2(
            viewportSize.X - panelSize.X - LocalPlayerCountPanel.EdgeMargin,
            viewportSize.Y - panelSize.Y - LocalPlayerCountPanel.EdgeMargin);
    }
}

/// <summary>
/// 「本地人数 -/+ · 切换编辑席位 ◀/▶」面板（R2 去重复，2026-09-28）。
///
/// 原本标准角色选择页 / 每日页 / 自定义页各写了一份 ~130 行、逐字同构的实现
/// （控件尺寸 `(140,32)`、字号 20、行距 50%、列距 44、位置算式与四个按钮的回调全一样）。
/// 这里收成一处，差异走 <see cref="LocalPlayerCountPanelOptions"/>；
/// 调用方只负责**门控判据**（三种页面的判据不同，见各自 Sync）与 Remove 转发。
///
/// ⚠ 面板与按钮的节点名、日志文案是实机契约的一部分（`log_scan.py --preset daily` 等锚点依赖），
/// 改这里等于改契约 —— 保持 `NamePrefix + CountPanel/MinusButton/PlusButton/PrevButton/NextButton`。
/// </summary>
internal sealed class LocalPlayerCountPanel
{
    /// <summary>面板离屏幕边缘的边距（右下角落点与每日页左侧落点共用）。</summary>
    internal const float EdgeMargin = 18f;

    private static readonly Vector2 ButtonSize = new(140f, 32f);
    private const float VerticalGapRatio = 0.5f;
    private const float HorizontalGap = 44f;

    private readonly LocalPlayerCountPanelOptions _options;
    private Node? _screen;
    private bool _firstLayoutLogged;

    internal LocalPlayerCountPanel(LocalPlayerCountPanelOptions options)
    {
        _options = options;
    }

    internal string PanelName => _options.NamePrefix + "CountPanel";
    private string MinusButtonName => _options.NamePrefix + "MinusButton";
    private string PlusButtonName => _options.NamePrefix + "PlusButton";
    private string PrevButtonName => _options.NamePrefix + "PrevButton";
    private string NextButtonName => _options.NamePrefix + "NextButton";

    /// <summary>`shouldShow` 由调用方按各自页面判据给出（官方页 / 残留会话一律 false ⇒ 摘掉面板）。</summary>
    internal void Sync(Node screen, bool shouldShow)
    {
        if (!shouldShow)
        {
            Remove(screen);
            return;
        }

        _screen = screen;
        Control panel = EnsurePanel(screen);
        UpdateLayout(screen, panel);
    }

    internal void Remove(Node screen)
    {
        Control? existingPanel = screen.GetNodeOrNull<Control>(PanelName);
        existingPanel?.QueueFreeSafely();
        _firstLayoutLogged = false;
        if (ReferenceEquals(_screen, screen))
        {
            _screen = null;
        }
    }

    private Control EnsurePanel(Node screen)
    {
        Control? existingPanel = screen.GetNodeOrNull<Control>(PanelName);
        if (existingPanel != null)
        {
            return existingPanel;
        }

        Control panel = new()
        {
            Name = PanelName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 80
        };

        LocalSimpleTextButton minusButton = CreateButton(MinusButtonName, "-", false);
        minusButton.Connect(NClickableControl.SignalName.Released,
            Callable.From<NClickableControl>((_) => OnAdjustPlayerCount(-1)));
        panel.AddChild(minusButton);

        LocalSimpleTextButton plusButton = CreateButton(PlusButtonName, "+", true);
        plusButton.Connect(NClickableControl.SignalName.Released,
            Callable.From<NClickableControl>((_) => OnAdjustPlayerCount(1)));
        panel.AddChild(plusButton);

        LocalSimpleTextButton prevButton = CreateButton(PrevButtonName, string.Empty, false);
        prevButton.Connect(NClickableControl.SignalName.Released,
            Callable.From<NClickableControl>((_) => OnSwitchLobbyPlayer(false)));
        panel.AddChild(prevButton);

        LocalSimpleTextButton nextButton = CreateButton(NextButtonName, string.Empty, true);
        nextButton.Connect(NClickableControl.SignalName.Released,
            Callable.From<NClickableControl>((_) => OnSwitchLobbyPlayer(true)));
        panel.AddChild(nextButton);

        screen.AddChildSafely(panel);
        _firstLayoutLogged = false;
        _options.AfterPanelCreated?.Invoke(panel);
        return panel;
    }

    private static LocalSimpleTextButton CreateButton(string name, string text, bool mirrorX)
    {
        return new LocalSimpleTextButton
        {
            Name = name,
            ButtonText = text,
            FocusMode = Control.FocusModeEnum.None,
            FontSize = 20,
            Size = ButtonSize,
            CustomMinimumSize = ButtonSize,
            ImageScale = Vector2.One * 1.5f,
            MirrorImageX = mirrorX
        };
    }

    private void UpdateLayout(Node screen, Control panel)
    {
        Viewport? viewport = screen.GetViewport();
        if (viewport == null)
        {
            return;
        }

        float verticalGap = ButtonSize.Y * VerticalGapRatio;
        float secondColumnX = ButtonSize.X + HorizontalGap;
        float secondRowY = ButtonSize.Y + verticalGap;
        Vector2 panelSize = new(secondColumnX + ButtonSize.X, secondRowY + ButtonSize.Y);
        Vector2 viewportSize = viewport.GetVisibleRect().Size;

        Vector2? position = _options.ResolvePosition(viewportSize, panelSize, screen);
        if (position == null)
        {
            return;
        }

        panel.Position = position.Value;

        panel.GetNodeOrNull<LocalSimpleTextButton>(MinusButtonName)!.Position = Vector2.Zero;
        panel.GetNodeOrNull<LocalSimpleTextButton>(PlusButtonName)!.Position = new Vector2(secondColumnX, 0f);
        panel.GetNodeOrNull<LocalSimpleTextButton>(PrevButtonName)!.Position = new Vector2(0f, secondRowY);
        panel.GetNodeOrNull<LocalSimpleTextButton>(NextButtonName)!.Position = new Vector2(secondColumnX, secondRowY);

        _options.AfterLayout?.Invoke(panel, secondColumnX, secondRowY);

        if (_options.FirstLayoutLog != null && !_firstLayoutLogged)
        {
            _firstLayoutLogged = true;
            _options.FirstLayoutLog(viewportSize, position.Value, panelSize);
        }
    }

    private void OnAdjustPlayerCount(int delta)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        string source = _options.SourcePrefix + (delta > 0 ? ":+" : ":-");
        if (!LocalSelfCoopContext.AdjustDesiredLocalPlayerCount(delta, source))
        {
            return;
        }

        int targetCount = _options.ClampTargetCount(LocalSelfCoopContext.DesiredLocalPlayerCount);
        NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create(LocalModText.LocalPlayerCount(targetCount)));
        LocalMultiControlLogger.Info($"{_options.SuccessLogText}: {targetCount}");
        _options.AfterAdjust?.Invoke();
    }

    private static void OnSwitchLobbyPlayer(bool next)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        LocalSelfCoopContext.SwitchLobbyEditingPlayer(next);
    }
}
