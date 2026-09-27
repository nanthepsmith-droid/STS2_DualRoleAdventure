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
/// - 一行大卡 = 官方「标准 / 每日 / 自定义」三张 + 我们的「单人多角色」一张（排在最左）；
/// - 该行下方居中挂一行小按钮：`本地·自定义模式` / `本地·每日挑战` —— 分别调用
///   <see cref="LocalCustomSelfCoopEntry"/> / <see cref="LocalDailySelfCoopEntry"/>。
///
/// 为什么改成这样（2026-09-27 用户反馈「装了我们这个 mod 就不能玩原版联机每日/自定义」）：
/// 官方三张卡全部保持原版行为（点官方「每日挑战」= 原版多人联机每日；点官方「自定义」= 原版自定义多人），
/// 本机多角色只从我们自己的卡片/小按钮进 —— mod 与官方联机**并存**，不再"接管"任何官方入口。
/// </summary>
[HarmonyPatch(typeof(NMultiplayerHostSubmenu), nameof(NMultiplayerHostSubmenu._Ready))]
internal static class NMultiplayerHostSubmenuPatch
{
    internal const string LocalSelfCoopButtonName = "LocalSelfCoopButton";
    internal const string ModePanelName = "LocalSelfCoopModePanel";
    internal const string CustomModeButtonName = "LocalSelfCoopCustomButton";
    internal const string DailyModeButtonName = "LocalSelfCoopDailyButton";
    internal const string TrackerName = "LocalSelfCoopModeTracker";

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
            NSubmenuButton? standardButton = __instance.GetNodeOrNull<NSubmenuButton>("StandardButton");
            NSubmenuButton? dailyButton = __instance.GetNodeOrNull<NSubmenuButton>("DailyButton");
            NSubmenuButton? customButton = __instance.GetNodeOrNull<NSubmenuButton>("CustomRunButton");
            if (standardButton == null)
            {
                LocalMultiControlLogger.Warn("未找到 StandardButton，无法注入本地多角色入口。");
                return;
            }

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

            List<NSubmenuButton> officialButtons = new() { standardButton };
            if (dailyButton != null)
            {
                officialButtons.Add(dailyButton);
            }

            if (customButton != null)
            {
                officialButtons.Add(customButton);
            }

            officialButtons = officialButtons.OrderBy((item) => item.Position.X).ToList();
            List<NSubmenuButton> allCards = new() { localButton };
            allCards.AddRange(officialButtons);
            ArrangeButtonsHorizontally(officialButtons, new List<NSubmenuButton> { localButton });
            EnsureModeButtons(__instance, container, allCards);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Error($"注入“单人多角色”入口失败: {exception}");
        }
    }

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
    /// 卡片行下方的小按钮条：`本地·自定义模式` / `本地·每日挑战`。
    /// 位置由 <see cref="LocalSelfCoopMenuButtonsTracker"/> 每帧跟随卡片行（居中 + 贴底），
    /// 这样即使官方容器重排卡片也不会错位。
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

        customButton.Position = Vector2.Zero;
        dailyButton.Position = new Vector2(ModeButtonSize.X + ModeButtonGap, 0f);
        container.AddChildSafely(panel);

        LocalSelfCoopMenuButtonsTracker tracker = new() { Name = TrackerName };
        tracker.Initialize(container, panel, allCards, ModeButtonGap, ModePanelGapBelowCards);
        container.AddChildSafely(tracker);

        LocalMultiControlLogger.Info("联机菜单已注入本地多角色模式按钮：本地·自定义模式 / 本地·每日挑战。");
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
        SaveManager.Instance.DeleteCurrentMultiplayerRun();
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

        float panelWidth = _panel.Size.X;
        if (panelWidth <= 1f)
        {
            panelWidth = 220f * 2f + _buttonGap;
        }

        float centerX = (minX + maxX) * 0.5f;
        _panel.Position = new Vector2(centerX - panelWidth * 0.5f, maxBottom + _gapBelowCards);
    }
}
