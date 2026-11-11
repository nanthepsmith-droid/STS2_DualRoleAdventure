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

    /// <summary>结束回合按钮的递归兜底缓存（r282；见 <see cref="ResolveEndTurnButton"/>）。</summary>
    private static NEndTurnButton? _cachedEndTurnButton;

    /// <summary>缓存属于哪一份战斗 UI（换战斗就作废，避免拿着上一场已释放的节点）。</summary>
    private static NCombatUi? _cachedEndTurnButtonOwner;

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

        NEndTurnButton? endTurnButton = ResolveEndTurnButton(combatUi);
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

    /// <summary>
    /// 取结束回合按钮（r282 性能修：**不要再每帧递归全树找**）。
    ///
    /// 原实现的 <see cref="Refresh"/> 挂在逐帧 <c>_Process</c> 上，却每帧都调
    /// <see cref="FindEndTurnButton"/> 递归遍历整个战斗 UI 树 —— 每次 <c>GetChildren()</c> 都要分配，
    /// **战神越多节点越多 ⇒ 每帧开销线性放大**（2026-10-17 用户报「战神一多就会很卡」的嫌疑之一）。
    ///
    /// 现在三级取法，正常路径 O(1)：
    /// ① 原版本来就暴露了 <c>NCombatUi.EndTurnButton</c>（`_Ready` 里 `GetNode&lt;NEndTurnButton&gt;("%EndTurnButton")`）；
    /// ② 万一那一刻还没绑好 → 用**上一次递归找到的缓存**（只要还挂在树上、且属于同一份 UI）；
    /// ③ 都没有才递归一次，然后缓存。
    /// </summary>
    private static NEndTurnButton? ResolveEndTurnButton(NCombatUi combatUi)
    {
        NEndTurnButton? direct = combatUi.EndTurnButton;
        if (direct != null && GodotObject.IsInstanceValid(direct) && direct.IsInsideTree())
        {
            return direct;
        }

        if (_cachedEndTurnButton != null
            && ReferenceEquals(_cachedEndTurnButtonOwner, combatUi)
            && GodotObject.IsInstanceValid(_cachedEndTurnButton)
            && _cachedEndTurnButton.IsInsideTree())
        {
            return _cachedEndTurnButton;
        }

        _cachedEndTurnButtonOwner = combatUi;
        _cachedEndTurnButton = FindEndTurnButton(combatUi);
        return _cachedEndTurnButton;
    }

    /// <summary>递归兜底（只在 <see cref="NCombatUi.EndTurnButton"/> 与缓存都拿不到时跑一次）。</summary>
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
    /// <summary>
    /// 切人按钮容器刷新的节流（r282）：显隐/位置按 **100ms** 更新完全够用（观感上察觉不到），
    /// 但把这个"每帧必做"的活儿从 60fps 降到 10fps —— 战神多开时它的相对开销不再随节点数滚动。
    /// </summary>
    private const long UiRefreshIntervalMs = 100L;

    private NCombatUi? _combatUi;
    private long _lastUiRefreshMs;

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

        // r282：逐帧开销探针（自身零分配，每 5s 打一条；见 LocalPerfProbe）。
        LocalPerfProbe.TickFrames();

        long nowMs = (long)Time.GetTicksMsec();
        if (nowMs - _lastUiRefreshMs >= UiRefreshIntervalMs)
        {
            _lastUiRefreshMs = nowMs;
            long refreshStart = LocalPerfProbe.Begin();
            LocalCombatSwitchButtons.Refresh(_combatUi);
            LocalPerfProbe.End(LocalPerfProbe.Slot.SwitchRefresh, refreshStart);
        }

        // r215：角色立绘站位位移探针（内部 250ms 节流、只在真的变了时打日志）。
        long probeStart = LocalPerfProbe.Begin();
        LocalCreaturePositionProbe.Sample("combat-tick");
        LocalPerfProbe.End(LocalPerfProbe.Slot.PositionProbe, probeStart);

        // r282：内部做了 100ms 重活节流（扫 12 席手牌 + 托管调度 + 战神收口，战神越多越贵）。
        long gateStart = LocalPerfProbe.Begin();
        LocalMultiControlRuntime.TryAutoEndTurnForRelicControlledPlayer();
        LocalPerfProbe.End(LocalPerfProbe.Slot.EndTurnGate, gateStart);

        // r104（BUG-2）：结束回合按钮状态机绑定前台角色，而自动切人/瓦库自动结束回合会绕开
        // 原版按钮事件，可能把按钮留在禁用/隐藏状态 → 点结束回合没反应。这里逐帧兜底自愈（内部节流）。
        long reconcileStart = LocalPerfProbe.Begin();
        LocalMultiControlRuntime.ReconcileEndTurnButtonForForeground("combat-tick");
        LocalPerfProbe.End(LocalPerfProbe.Slot.EndTurnReconcile, reconcileStart);

        // r111（BUG-7）：显示的手牌 UI 顺序必须等于前台角色手牌堆顺序（后台角色手牌变换跳过视觉后会脱节）。
        // 同样逐帧兜底、内部 250ms 节流；只在真的不一致时动手并打日志。
        long handStart = LocalPerfProbe.Begin();
        LocalMultiControlRuntime.ReconcileDisplayedHandOrder("combat-tick");
        LocalPerfProbe.End(LocalPerfProbe.Slot.HandOrderReconcile, handStart);

        // 2026-11-11：角色逃跑（烟雾弹等）后「手牌/抽牌堆/弃牌堆/结束回合」一起消失的探针 + 最小自愈。
        // 内部 500ms 节流、只在状态变化或真的做了自愈时打日志（避免刷屏）。
        long uiVisStart = LocalPerfProbe.Begin();
        LocalMultiControlRuntime.ProbeAndHealCombatUiVisibility("combat-tick");
        LocalPerfProbe.End(LocalPerfProbe.Slot.HandOrderReconcile, uiVisStart);
    }
}
