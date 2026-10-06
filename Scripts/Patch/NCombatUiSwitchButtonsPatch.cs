using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._Ready))]
internal static class NCombatUiReadyPatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatUi __instance)
    {
        LocalCombatSwitchButtons.Ensure(__instance);
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Activate))]
internal static class NCombatUiActivatePatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatUi __instance, CombatState state)
    {
        LocalCombatSwitchButtons.Refresh(__instance);
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Enable))]
internal static class NCombatUiEnablePatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatUi __instance)
    {
        LocalCombatSwitchButtons.Refresh(__instance);
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Disable))]
internal static class NCombatUiDisablePatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatUi __instance)
    {
        LocalCombatSwitchButtons.Refresh(__instance);
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Deactivate))]
internal static class NCombatUiDeactivatePatch
{
    [HarmonyPostfix]
    private static void Postfix(NCombatUi __instance)
    {
        LocalCombatSwitchButtons.Refresh(__instance);
    }
}

[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi._ExitTree))]
internal static class NCombatUiExitPatch
{
    [HarmonyPrefix]
    private static void Prefix(NCombatUi __instance)
    {
        LocalCombatSwitchButtons.Remove(__instance);
    }
}

internal static class LocalCombatSwitchButtons
{
    private const string ContainerName = "LocalCombatSwitchContainer";
    private const string PrevButtonName = "LocalCombatSwitchPrevButton";
    private const string NextButtonName = "LocalCombatSwitchNextButton";

    /// <summary>瓦库四功能 · 我们联合：战斗内按钮（条件显示，见 <see cref="LocalWakuuUniteRuntime"/>）。</summary>
    private const string UniteButtonName = "LocalCombatUniteButton";

    private const string TrackerName = "LocalCombatSwitchTracker";
    private static readonly Vector2 PingShowPosRatio = new Vector2(1536f, 932f) / NGame.devResolution;
    private static readonly Vector2 EndTurnAnchorOffset = new Vector2(-28f, -42f);

    /// <summary>「我们联合」按钮相对容器原点的位置（两个切人钮在 (0,0) / (136,0)，这个放它们上面）。</summary>
    private static readonly Vector2 UniteButtonOffset = new Vector2(0f, -40f);

    public static void Ensure(NCombatUi combatUi)
    {
        if (combatUi.GetNodeOrNull<Control>(ContainerName) != null)
        {
            return;
        }

        Control container = new Control
        {
            Name = ContainerName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TopLevel = true,
            ZIndex = 120
        };

        LocalSimpleTextButton prevButton = new LocalSimpleTextButton
        {
            Name = PrevButtonName,
            ButtonText = string.Empty,
            FocusMode = Control.FocusModeEnum.None,
            FontSize = 20,
            Size = new Vector2(140f, 32f),
            CustomMinimumSize = new Vector2(140f, 32f),
            ImageScale = Vector2.One * 1.5f
        };
        prevButton.Connect(
            MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl.SignalName.Released,
            Callable.From<MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl>((_) =>
                LocalControlSwitchGuard.TrySwitchPrevious("combat-ui-up")));
        container.AddChild(prevButton);

        LocalSimpleTextButton nextButton = new LocalSimpleTextButton
        {
            Name = NextButtonName,
            ButtonText = string.Empty,
            FocusMode = Control.FocusModeEnum.None,
            FontSize = 20,
            Size = new Vector2(140f, 32f),
            CustomMinimumSize = new Vector2(140f, 32f),
            Position = new Vector2(136f, 0f),
            ImageScale = Vector2.One * 1.5f,
            MirrorImageX = true
        };
        nextButton.Connect(
            MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl.SignalName.Released,
            Callable.From<MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl>((_) =>
                LocalControlSwitchGuard.TrySwitchNext("combat-ui-down")));
        container.AddChild(nextButton);

        // 瓦库四功能 · 我们联合：条件显示的战斗内按钮（每场战斗一次，见 LocalWakuuUniteRuntime）。
        // 刻意挂在同一个容器里（复用 NCombatUi._Ready 这个既有挂点，不新增 Harmony 目标）。
        LocalSimpleTextButton uniteButton = new LocalSimpleTextButton
        {
            Name = UniteButtonName,
            ButtonText = LocalModText.UniteButtonName,
            FocusMode = Control.FocusModeEnum.None,
            FontSize = 18,
            Size = new Vector2(140f, 32f),
            CustomMinimumSize = new Vector2(140f, 32f),
            Position = UniteButtonOffset,
            Visible = false
        };
        uniteButton.Connect(
            MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl.SignalName.Released,
            Callable.From<MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl>((_) =>
                LocalWakuuUniteRuntime.OnButtonPressed()));
        container.AddChild(uniteButton);

        combatUi.AddChildSafely(container);
        EnsureTracker(combatUi);
        LocalMultiControlLogger.Info("战斗界面已创建 Ping 右侧上下切人按钮。");
        LocalMultiControlLogger.Info($"战斗界面已创建「{LocalModText.UniteButtonName}」按钮（按条件显示）。");
    }

    private static void EnsureTracker(NCombatUi combatUi)
    {
        if (combatUi.GetNodeOrNull<LocalCombatSwitchTracker>(TrackerName) != null)
        {
            return;
        }

        // 注意：不要再用 NCombatUi._Process 打补丁。
        // 该方法在目标版本不存在，曾导致 Harmony 初始化失败，进而引发“无法开始/读档异常”。
        // 这里改为挂一个本地跟随节点逐帧刷新，兼容性更稳。
        LocalCombatSwitchTracker tracker = new LocalCombatSwitchTracker
        {
            Name = TrackerName
        };
        tracker.Initialize(combatUi);
        combatUi.AddChild(tracker);
    }

    public static void Refresh(NCombatUi combatUi)
    {
        Control? container = combatUi.GetNodeOrNull<Control>(ContainerName);
        if (container == null)
        {
            return;
        }

        int runPlayerCount = RunManager.Instance.DebugOnlyGetState()?.Players.Count ?? 0;
        bool hasMultiplePlayers = LocalMultiControlRuntime.SessionState.OrderedPlayerIds.Count > 1 || runPlayerCount > 1;
        bool shouldShow = LocalSelfCoopContext.IsEnabled
                          && RunManager.Instance.IsInProgress
                          && CombatManager.Instance.IsInProgress
                          && hasMultiplePlayers;

        container.Visible = shouldShow;

        // 「我们联合」按钮自己再判一层（开关 / 每场一次 / 有没有候选瓦库）——
        // 放在早退之前求值，短路顺序保证容器隐藏时不会白算。
        Control? uniteButton = container.GetNodeOrNull<Control>(UniteButtonName);
        if (uniteButton != null)
        {
            uniteButton.Visible = shouldShow && LocalWakuuUniteRuntime.ShouldShowButton();
        }

        if (!shouldShow)
        {
            return;
        }

        Viewport? viewport = combatUi.GetViewport();
        if (viewport == null)
        {
            return;
        }

        NEndTurnButton? endTurnButton = FindEndTurnButton(combatUi);
        if (endTurnButton != null)
        {
            container.GlobalPosition = endTurnButton.GlobalPosition + EndTurnAnchorOffset;
            return;
        }

        Vector2 fallbackAnchor = PingShowPosRatio * viewport.GetVisibleRect().Size;
        container.GlobalPosition = fallbackAnchor;
    }

    public static void Remove(NCombatUi combatUi)
    {
        Control? container = combatUi.GetNodeOrNull<Control>(ContainerName);
        container?.QueueFreeSafely();
    }

    private static NEndTurnButton? FindEndTurnButton(Node root)
    {
        if (root is NEndTurnButton endTurnButton)
        {
            return endTurnButton;
        }

        foreach (Node child in root.GetChildren())
        {
            NEndTurnButton? found = FindEndTurnButton(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}

internal sealed partial class LocalCombatSwitchTracker : Node
{
    private NCombatUi? _combatUi;

    public void Initialize(NCombatUi combatUi)
    {
        _combatUi = combatUi;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (_combatUi == null || !GodotObject.IsInstanceValid(_combatUi))
        {
            QueueFree();
            return;
        }

        LocalCombatSwitchButtons.Refresh(_combatUi);
        // r215：角色立绘站位位移探针（内部 250ms 节流、只在真的变了时打日志）。
        LocalCreaturePositionProbe.Sample("combat-tick");
        LocalMultiControlRuntime.TryAutoEndTurnForRelicControlledPlayer();
        // r104（BUG-2）：结束回合按钮状态机绑定前台角色，而自动切人/瓦库自动结束回合会绕开
        // 原版按钮事件，可能把按钮留在禁用/隐藏状态 → 点结束回合没反应。这里逐帧兜底自愈（内部节流）。
        LocalMultiControlRuntime.ReconcileEndTurnButtonForForeground("combat-tick");
        // r111（BUG-7）：显示的手牌 UI 顺序必须等于前台角色手牌堆顺序（后台角色手牌变换跳过视觉后会脱节）。
        // 同样逐帧兜底、内部 250ms 节流；只在真的不一致时动手并打日志。
        LocalMultiControlRuntime.ReconcileDisplayedHandOrder("combat-tick");
    }
}
