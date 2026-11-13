using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 联机菜单入口注入（r158 定型：**零劫持**）。
///
/// 结构：
/// - 一行大卡 = 官方「标准 / 每日 / 闪电战 / 恐惧 / 自定义」五张（0.113 起；未解锁的会被原版隐藏）
///   + 我们的「单人多角色」一张（排在最左）；
/// - 该行下方居中挂一行小按钮：`本地·自定义模式` / `本地·每日挑战` / `本地·恐惧` / `本地·闪电战` ——
///   分别调用 <see cref="LocalCustomSelfCoopEntry"/> / <see cref="LocalDailySelfCoopEntry"/> /
///   <see cref="LocalDreadSelfCoopEntry"/> / <see cref="LocalBlitzSelfCoopEntry"/>。
///
/// 为什么改成这样（2026-09-27 用户反馈「装了我们这个 mod 就不能玩原版联机每日/自定义」）：
/// 官方五张卡全部保持原版行为（点官方「每日挑战」= 原版多人联机每日；点官方「恐惧」= 原版恐惧多人），
/// 本机多角色只从我们自己的卡片/小按钮进 —— mod 与官方联机**并存**，不再"接管"任何官方入口。
/// </summary>
[HarmonyPatch(typeof(NMultiplayerHostSubmenu), nameof(NMultiplayerHostSubmenu._Ready))]
internal static class NMultiplayerHostSubmenuPatch
{
    internal const string LocalSelfCoopButtonName = "LocalSelfCoopButton";
    internal const string ModePanelName = "LocalSelfCoopModePanel";
    internal const string CustomModeButtonName = "LocalSelfCoopCustomButton";
    internal const string DailyModeButtonName = "LocalSelfCoopDailyButton";
    internal const string DreadModeButtonName = "LocalSelfCoopDreadButton";
    internal const string BlitzModeButtonName = "LocalSelfCoopBlitzButton";
    internal const string TrackerName = "LocalSelfCoopModeTracker";

    /// <summary>小按钮条的排列顺序（未解锁的模式按钮会被隐藏，排列时跳过）。</summary>
    private static readonly string[] ModeButtonNames =
    {
        CustomModeButtonName,
        DailyModeButtonName,
        DreadModeButtonName,
        BlitzModeButtonName
    };

    private const float CardGap = 26f;
    private const float MinCardGap = 6f;
    private const float ScreenMargin = 24f;
    private const float ModeButtonGap = 16f;
    private const float ModePanelGapBelowCards = 14f;
    private static readonly Vector2 ModeButtonSize = new(220f, 40f);

    [HarmonyPostfix]
    private static void Postfix(NMultiplayerHostSubmenu __instance)
    {
        try
        {
            NSubmenuButton? standardButton = FindButton(__instance, "StandardButton");
            if (standardButton == null)
            {
                LocalMultiControlLogger.Warn("未找到 StandardButton，无法注入本地多角色入口。");
                return;
            }

            NSubmenuButton? customButton = FindButton(__instance, "CustomRunButton");
            NSubmenuButton templateButton = customButton ?? standardButton;
            Control container = templateButton.GetParent<Control>();

            NSubmenuButton localButton = EnsureCard(
                __instance,
                container,
                templateButton,
                LocalSelfCoopButtonName,
                LocalModText.LocalSelfCoopCardTitle,
                LocalModText.LocalSelfCoopCardDescription,
                () => OnLocalSelfCoopPressed(__instance));

            Relayout(__instance, localButton);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Error($"注入“单人多角色”入口失败: {exception}");
        }
    }

    /// <summary>
    /// 按卡片名找官方卡片。**0.113 起卡片是 unique name（`%StandardButton`）**，而且官方从 3 张变 5 张
    /// （新增 `BlitzButton` / `DreadButton`）。所以这里 `%名字` 优先、裸名字回退 —— 旧版的直接子节点
    /// 路径仍然命中，新版靠 unique name 命中，不依赖中间容器层级（实测 0.113 裸名直接子节点查找返回 null，
    /// 就是"入口消失"的原因）。
    /// </summary>
    private static NSubmenuButton? FindButton(Node root, string name)
    {
        return root.GetNodeOrNull<NSubmenuButton>("%" + name)
            ?? root.GetNodeOrNull<NSubmenuButton>(name);
    }

    /// <summary>
    /// 重排卡片行：我们的卡放最左，官方卡片里**只排当前可见的**（未解锁的 Dread/Blitz 被原版隐藏，
    /// 不能给它们留空位）。可重复调用（`_Ready` 之后原版还会 `RefreshButtons()` 改可见性）。
    /// </summary>
    internal static void Relayout(NMultiplayerHostSubmenu submenu, NSubmenuButton? localButton = null)
    {
        localButton ??= submenu.FindChild(LocalSelfCoopButtonName, recursive: true, owned: false) as NSubmenuButton;
        if (localButton == null || !GodotObject.IsInstanceValid(localButton))
        {
            return;
        }

        List<NSubmenuButton> officialButtons = new();
        foreach (string name in OfficialCardNames)
        {
            NSubmenuButton? button = FindButton(submenu, name);
            if (button != null && GodotObject.IsInstanceValid(button) && button.Visible)
            {
                officialButtons.Add(button);
            }
        }

        if (officialButtons.Count == 0)
        {
            return;
        }

        officialButtons = officialButtons.OrderBy((item) => item.Position.X).ToList();
        ArrangeButtonsHorizontally(officialButtons, new List<NSubmenuButton> { localButton });

        List<NSubmenuButton> allCards = new() { localButton };
        allCards.AddRange(officialButtons);

        Control? container = localButton.GetParentOrNull<Control>();
        if (container != null)
        {
            EnsureModeButtons(submenu, container, allCards);
            SyncModeButtonVisibility(submenu, container);
        }
    }

    /// <summary>官方卡片名（0.113 顺序：标准 / 每日 / Blitz / Dread / 自定义）。</summary>
    private static readonly string[] OfficialCardNames =
    {
        "StandardButton",
        "DailyButton",
        "BlitzButton",
        "DreadButton",
        "CustomRunButton"
    };

    private static NSubmenuButton EnsureCard(
        NMultiplayerHostSubmenu submenu,
        Control container,
        NSubmenuButton templateButton,
        string name,
        string title,
        string description,
        Action onPressed)
    {
        NSubmenuButton? existing = submenu.GetNodeOrNull<NSubmenuButton>(name);
        if (existing != null)
        {
            return existing;
        }

        NSubmenuButton button = CreateStyledButton(templateButton);
        button.Name = name;
        EnsureVisualResourcesUnique(button);
        ApplyButtonText(button, title, description);
        button.Connect(NClickableControl.SignalName.Released, Callable.From<NButton>((_) => onPressed()));

        container.AddChild(button);
        container.MoveChild(button, 0);
        return button;
    }

    /// <summary>
    /// 卡片行下方的小按钮条：`本地·自定义模式` / `本地·每日挑战` / `本地·恐惧` / `本地·闪电战`。
    /// 位置由 <see cref="LocalSelfCoopMenuButtonsTracker"/> 每帧跟随卡片行（居中 + 贴底），
    /// 这样即使官方容器重排卡片也不会错位。
    ///
    /// 恐惧 / 闪电战两个新模式的按钮**跟随官方卡片的「可见 + 可用」状态**（见
    /// <see cref="SyncModeButtonVisibility"/>）：官方没解锁时我们不抢先开一条绕过解锁的入口。
    /// </summary>
    private static void EnsureModeButtons(NMultiplayerHostSubmenu submenu, Control container, List<NSubmenuButton> allCards)
    {
        LocalSelfCoopMenuButtonsTracker? existingTracker =
            container.GetNodeOrNull<LocalSelfCoopMenuButtonsTracker>(TrackerName);
        if (existingTracker != null)
        {
            existingTracker.UpdateCards(allCards);
            return;
        }

        Control panel = new()
        {
            Name = ModePanelName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 80
        };

        LocalSimpleTextButton customButton = CreateModeButton(CustomModeButtonName, LocalModText.LocalCustomSelfCoopButton);
        customButton.Connect(NClickableControl.SignalName.Released,
            Callable.From<NClickableControl>((_) => LocalCustomSelfCoopEntry.Enter(submenu)));
        panel.AddChildSafely(customButton);

        LocalSimpleTextButton dailyButton = CreateModeButton(DailyModeButtonName, LocalModText.LocalDailySelfCoopButton);
        dailyButton.Connect(NClickableControl.SignalName.Released,
            Callable.From<NClickableControl>((_) => LocalDailySelfCoopEntry.Enter(submenu)));
        panel.AddChildSafely(dailyButton);

        LocalSimpleTextButton dreadButton = CreateModeButton(DreadModeButtonName, LocalModText.LocalDreadSelfCoopButton);
        dreadButton.Connect(NClickableControl.SignalName.Released,
            Callable.From<NClickableControl>((_) => LocalDreadSelfCoopEntry.Enter(submenu)));
        panel.AddChildSafely(dreadButton);

        LocalSimpleTextButton blitzButton = CreateModeButton(BlitzModeButtonName, LocalModText.LocalBlitzSelfCoopButton);
        blitzButton.Connect(NClickableControl.SignalName.Released,
            Callable.From<NClickableControl>((_) => LocalBlitzSelfCoopEntry.Enter(submenu)));
        panel.AddChildSafely(blitzButton);

        LayoutModeButtons(panel);
        container.AddChildSafely(panel);

        LocalSelfCoopMenuButtonsTracker tracker = new() { Name = TrackerName };
        tracker.Initialize(container, panel, allCards, ModeButtonGap, ModePanelGapBelowCards);
        container.AddChildSafely(tracker);

        LocalMultiControlLogger.Info(
            "联机菜单已注入本地多角色模式按钮：本地·自定义模式 / 本地·每日挑战 / 本地·恐惧 / 本地·闪电战。");
    }

    /// <summary>
    /// 新模式两个按钮的可见性 = 官方对应卡片的「可见 + 可用」（0.113：恐惧未解锁时官方卡 `Visible=false`，
    /// 闪电战未解锁时只是 `SetEnabled(false)`）。隐藏后重排一次，避免留空位。
    /// </summary>
    private static void SyncModeButtonVisibility(NMultiplayerHostSubmenu submenu, Control container)
    {
        Control? panel = container.GetNodeOrNull<Control>(ModePanelName);
        if (panel == null)
        {
            return;
        }

        SetModeButtonVisible(panel, DreadModeButtonName, IsOfficialEntryAvailable(submenu, "DreadButton"));
        SetModeButtonVisible(panel, BlitzModeButtonName, IsOfficialEntryAvailable(submenu, "BlitzButton"));
        LayoutModeButtons(panel);
    }

    private static bool IsOfficialEntryAvailable(Node root, string name)
    {
        NSubmenuButton? button = FindButton(root, name);
        return button != null && GodotObject.IsInstanceValid(button) && button.Visible && button.IsEnabled;
    }

    private static void SetModeButtonVisible(Control panel, string name, bool visible)
    {
        if (panel.GetNodeOrNull<LocalSimpleTextButton>(name) is not { } button)
        {
            return;
        }

        if (button.Visible != visible)
        {
            button.Visible = visible;
            LocalMultiControlLogger.Info($"本地多角色模式按钮可见性已同步: name={name}, visible={visible}");
        }
    }

    /// <summary>按「当前可见」的小按钮从左到右排一遍（先隐藏后重排即可消掉空位）。</summary>
    private static void LayoutModeButtons(Control panel)
    {
        float x = 0f;
        foreach (string name in ModeButtonNames)
        {
            if (panel.GetNodeOrNull<LocalSimpleTextButton>(name) is not { } button || !button.Visible)
            {
                continue;
            }

            button.Position = new Vector2(x, 0f);
            x += ModeButtonSize.X + ModeButtonGap;
        }
    }

    private static LocalSimpleTextButton CreateModeButton(string name, string text)
    {
        return new LocalSimpleTextButton
        {
            Name = name,
            ButtonText = text,
            FocusMode = Control.FocusModeEnum.None,
            FontSize = 18,
            Size = ModeButtonSize,
            CustomMinimumSize = ModeButtonSize,
            ImageScale = Vector2.One * 1.4f,
            MirrorImageX = false
        };
    }

    private static NSubmenuButton CreateStyledButton(NSubmenuButton templateButton)
    {
        const Node.DuplicateFlags duplicateFlags = Node.DuplicateFlags.Groups |
                                                   Node.DuplicateFlags.Scripts |
                                                   Node.DuplicateFlags.UseInstantiation;
        return templateButton.Duplicate((int)duplicateFlags) as NSubmenuButton
            ?? throw new InvalidOperationException("复制模板按钮失败。");
    }

    private static void EnsureVisualResourcesUnique(NSubmenuButton button)
    {
        Node? bgPanelNode = button.FindChild("BgPanel", recursive: true, owned: false);
        if (bgPanelNode is CanvasItem bgPanel && bgPanel.Material is ShaderMaterial material)
        {
            bgPanel.Material = material.Duplicate() as ShaderMaterial;
        }
    }

    private static void ApplyButtonText(NSubmenuButton button, string title, string description)
    {
        Node? titleNode = button.FindChild("Title", recursive: true, owned: false);
        if (titleNode is Label titleLabel)
        {
            titleLabel.Text = title;
        }

        Node? descriptionNode = button.FindChild("Description", recursive: true, owned: false);
        if (descriptionNode is RichTextLabel descriptionLabel)
        {
            descriptionLabel.Text = description;
        }
    }

    /// <summary>
    /// 把「我们的卡片（放最左） + 官方原有卡片」排成一行：官方卡片的整体中心保持不动，
    /// 间距在 6~26px 之间自适应（窗口越窄越紧），避免把两侧顶出屏幕。
    /// </summary>
    private static void ArrangeButtonsHorizontally(
        List<NSubmenuButton> originalButtons,
        List<NSubmenuButton> localButtons)
    {
        if (originalButtons.Count == 0)
        {
            return;
        }

        float y = originalButtons[0].Position.Y;
        float cardWidth = originalButtons.Select((item) => item.Size.X).DefaultIfEmpty(0f).Max();
        if (cardWidth <= 1f)
        {
            cardWidth = localButtons.Select((item) => item.Size.X).DefaultIfEmpty(0f).Max();
        }

        if (cardWidth <= 1f)
        {
            return;
        }

        float centerX = originalButtons.Average((item) => item.Position.X + item.Size.X * 0.5f);

        List<NSubmenuButton> arranged = new();
        arranged.AddRange(localButtons);
        arranged.AddRange(originalButtons);

        int count = arranged.Count;
        Vector2 viewportSize = localButtons.Count > 0 && localButtons[0].IsInsideTree()
            ? localButtons[0].GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero
            : Vector2.Zero;

        float gap = CardGap;
        if (viewportSize.X > 1f && count > 1)
        {
            float available = viewportSize.X - ScreenMargin * 2f - cardWidth * count;
            gap = Math.Clamp(available / (count - 1), MinCardGap, CardGap);
        }

        float totalWidth = cardWidth * count + gap * (count - 1);
        float startX = centerX - totalWidth * 0.5f;
        for (int index = 0; index < count; index++)
        {
            arranged[index].Position = new Vector2(startX + index * (cardWidth + gap), y);
        }

        LocalMultiControlLogger.Info(
            $"联机菜单卡片已重排: count={count}, cardWidth={cardWidth:0}, gap={gap:0}, "
            + $"startX={startX:0}, viewport={viewportSize.X:0}");
    }

    private static void OnLocalSelfCoopPressed(NMultiplayerHostSubmenu submenu)
    {
        LocalMultiControlLogger.Info("进入单人多角色流程。");
        LocalSelfCoopSaveTag.ClearCurrentProfile();
        // 待查-2：先判存在再删（游戏原 API 对不存在的文件会打 `Error deleting path`）。
        MultiplayerRunSaveCleaner.DeleteIfPresent("进入单人多角色流程");
        LocalMultiControlLogger.Info("已清理历史多人存档，避免旧格式校验干扰。");

        NSubmenuStack? stack = GetStack(submenu);
        if (stack == null)
        {
            LocalMultiControlLogger.Warn("无法打开角色选择：未找到 NSubmenuStack。");
            return;
        }

        ulong primaryPlayerId = LocalSelfCoopContext.ResolvePrimaryPlayerId();
        LocalSelfCoopContext.UseSavedWakuuPlayerIds(Array.Empty<ulong>());
        LocalSelfCoopSaveTag.MarkCurrentProfile(LocalSelfCoopContext.LocalPlayerIds.Take(LocalSelfCoopContext.DesiredLocalPlayerCount).ToList());
        LocalLoopbackHostGameService netService = new LocalLoopbackHostGameService(primaryPlayerId);
        LocalSelfCoopContext.Enable(netService);

        NCharacterSelectScreen characterSelectScreen = stack.GetSubmenuType<NCharacterSelectScreen>();
        LocalSelfCoopContext.ActiveCharacterSelectScreen = characterSelectScreen;
        // 以最大容量初始化大厅，实际活跃人数由 LocalSelfCoopContext 按目标人数裁剪到2~12。
        characterSelectScreen.InitializeMultiplayerAsHost(netService, LocalSelfCoopContext.MaxLocalPlayerCount);
        if (!LocalSelfCoopContext.BootstrapLocalPlayers(characterSelectScreen))
        {
            LocalMultiControlLogger.Warn("初始化本地多角色队伍失败，已回退到默认流程。");
        }

        stack.Push(characterSelectScreen);
        NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create(LocalModText.EnteredLocalSelfCoopHint));
        LocalMultiControlLogger.Info("已跳转到本地多角色队伍角色选择界面。");
    }

    private static NSubmenuStack? GetStack(NSubmenu submenu)
    {
        return AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(submenu) as NSubmenuStack;
    }
}

/// <summary>
/// 官方联机入口守卫（r158）：玩家点官方「标准 / 每日 / 自定义」卡片时，先把**残留**的本地多控会话清掉。
///
/// 为什么需要：本地多控会话只在「进局结束（RunManager.CleanUp）」或「ESC 重启房间」时关闭；
/// 玩家若只是进了我们的大厅页又退回主菜单，会话仍在开着 ⇒ ① 官方页面里会冒出我们的席位/切人按钮，
/// ② 官方 host 可能拿到被改绑的 `LocalContext.NetId` 与残留回环 NetService（表现为进不去）。
/// 这里在官方入口处立即清理，另有 <see cref="LocalSelfCoopSessionGuard"/> 做兜底超时清理。
/// </summary>
[HarmonyPatch(typeof(NMultiplayerHostSubmenu), nameof(NMultiplayerHostSubmenu.StartHost))]
internal static class NMultiplayerHostSubmenuOfficialEntryGuardPatch
{
    [HarmonyPrefix]
    private static void Prefix(GameMode gameMode)
    {
        if (!LocalSelfCoopContext.IsEnabled || RunManager.Instance?.IsInProgress == true)
        {
            return;
        }

        LocalSelfCoopContext.Disable($"official-host-enter:{gameMode}");
        LocalMultiControlLogger.Info($"进入官方联机入口前已清理残留的本地多控会话: gameMode={gameMode}");
    }
}

/// <summary>
/// 模式按钮条的位置跟随器：卡片行的整体中心 + 底边 ⇒ 面板位置（居中贴底）。
/// 卡片位置可能被官方容器重排，所以每帧跟随而不是只算一次。
/// </summary>
internal sealed partial class LocalSelfCoopMenuButtonsTracker : Node
{
    private Control? _container;
    private Control? _panel;
    private float _buttonGap;
    private float _gapBelowCards;
    private readonly List<NSubmenuButton> _cards = new();

    public void Initialize(
        Control container,
        Control panel,
        List<NSubmenuButton> cards,
        float buttonGap,
        float gapBelowCards)
    {
        _container = container;
        _panel = panel;
        _buttonGap = buttonGap;
        _gapBelowCards = gapBelowCards;
        UpdateCards(cards);
        SetProcess(true);
    }

    public void UpdateCards(List<NSubmenuButton> cards)
    {
        _cards.Clear();
        _cards.AddRange(cards);
    }

    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_container) || !GodotObject.IsInstanceValid(_panel))
        {
            QueueFree();
            return;
        }

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float maxBottom = float.MinValue;
        foreach (NSubmenuButton card in _cards)
        {
            if (!GodotObject.IsInstanceValid(card))
            {
                continue;
            }

            Rect2 rect = new(card.Position, card.Size);
            minX = Math.Min(minX, rect.Position.X);
            maxX = Math.Max(maxX, rect.End.X);
            maxBottom = Math.Max(maxBottom, rect.End.Y);
        }

        if (minX > maxX)
        {
            return;
        }

        float panelWidth = ResolvePanelWidth();
        if (panelWidth <= 1f)
        {
            return;
        }

        float centerX = (minX + maxX) * 0.5f;
        _panel.Position = new Vector2(centerX - panelWidth * 0.5f, maxBottom + _gapBelowCards);
    }

    /// <summary>
    /// 面板实际宽度 = 可见小按钮宽度之和 + 间隙（面板本身是裸 <see cref="Control"/>，不会跟着子节点自适应）。
    /// 0.113 起小按钮可能只有 2~4 个（新的两个模式未解锁时隐藏），所以必须**按当前可见集合**算。
    /// </summary>
    private float ResolvePanelWidth()
    {
        if (_panel == null || !GodotObject.IsInstanceValid(_panel))
        {
            return 0f;
        }

        float width = 0f;
        int visibleCount = 0;
        foreach (Node child in _panel.GetChildren())
        {
            if (child is not Control control || !control.Visible)
            {
                continue;
            }

            width += control.Size.X;
            visibleCount++;
        }

        if (visibleCount > 0)
        {
            return width + _buttonGap * (visibleCount - 1);
        }

        return _panel.Size.X;
    }
}

/// <summary>
/// 0.113：原版在 `_Ready` 之后还会调 `RefreshButtons()` 改卡片可见性（Dread 未解锁时 `Visible=false`）。
/// 我们的卡片行是按"当前可见的官方卡"重排的，所以每次可见性变化后再排一次 —— 否则会留下一个空格子
/// 或者把隐藏的 Dread 也占掉宽度。
/// </summary>
[HarmonyPatch(typeof(NMultiplayerHostSubmenu), "RefreshButtons")]
internal static class NMultiplayerHostSubmenuRefreshPatch
{
    [HarmonyPostfix]
    private static void Postfix(NMultiplayerHostSubmenu __instance)
    {
        try
        {
            NMultiplayerHostSubmenuPatch.Relayout(__instance);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"联机菜单卡片重排失败（忽略，沿用当前布局）: {exception.Message}");
        }
    }
}
