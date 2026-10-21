using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace HextechRunesLocalCoopFix.Scripts;

/// <summary>
/// **瓦库（后台托管）席位的海克斯符文界面自动作答**（补丁点 ⑤，v1.1.0）。
///
/// <para><b>为什么需要</b>：补丁 ① 把「本地席位」也判成「本机玩家」后，瓦库席位会**弹出它自己的**
/// 符文界面等人点 —— 不再软锁（比修前的死等好），但瓦库不会自己选，需要真人替它点一下。
/// 本类就是替这一席作答的那一环（用户 2026-10-05 实测确认瓦库界面确实需要手点后加的）。</para>
///
/// <para><b>只在「后台托管中的瓦库席位」生效</b>（<see cref="MainModBridge.IsAutomatedWakuuSeat"/>）。
/// 真人席位的符文界面**一律不碰**：该由玩家自己挑符文。</para>
///
/// <para><b>作答姿势</b>（与主 mod BUG-23 方案 B2 同型：走游戏自己的点击路径）：</para>
/// <list type="number">
/// <item>界面创建窗口里由 ③ 记下「这一屏属于哪一席」，在 <c>RelicsSelected</c> 被等待时绑定到界面实例；</item>
/// <item>延迟 <see cref="AnswerDelaySeconds"/> 后作答 —— 界面自带 **1000ms 点击保护窗**
///   （<c>AfterOverlayOpened</c> 起算的 <c>IsSelectionConfirmGuardActive</c>），早按会被它吞掉；</item>
/// <item>只按「候选按钮的 <c>Pressed</c> 信号」（候选是 <c>Button</c>，海克斯把 <c>Pressed</c> 连到
///   <c>OnHolderSelected</c>）⇒ 不碰任何私有方法，将来界面内部改动也不会绕过它的副作用；</item>
/// <item>若该屏还叠在别的屏下面（同机多席位会同时入栈）⇒ 等它<b>露出来</b>再按（逐席位依次作答）；</item>
/// <item>按完核对界面是否真的关闭：自选池模式再按一次「确认」、海克斯开了「确认符文选择」时再按一次
///   符文确认；仍未关 ⇒ WARN 点名并交回真人（下一轮适配的实证锚点）。</item>
/// </list>
///
/// <para><b>选哪个符文</b>：**按抓率**（v1.3.0，用户 2026-10-11 点名）——
/// 抓率来自 <see cref="RunePickStats"/>（真人与瓦库的每一屏都记「候选出现过谁 / 选了谁」），
/// 并列时取最左 ⇒ 没有统计时行为与旧版"取最左"一致。
/// 稀有度**不能**当判据（用户实测：海克斯每次给的 3 个选项稀有度一致）。
/// 自选池屏（锻造选择：一屏一大堆符文）仍取最左、也不参与统计。</para>
///
/// <para><b>选项很烂时自主 roll</b>（v1.6.0，用户 2026-10-11 点名的第三件）：反编译确认界面有
/// <c>_rerollButtons</c>（节点名 <c>RerollButton_{slotIndex}</c>，<c>Pressed</c> 连到 <c>OnRerollPressed(slot)</c>；
/// 次数上限 <c>PlayerRuneRerollLimit</c>，按钮 <c>Disabled</c> 即"这槽用完了"）⇒ 三张候选**全烂**时按最烂那张的
/// reroll（判据见 <see cref="RuneRerollPolicy"/>；`HRLF_RUNE_REROLL=0` 可关）。</para>
/// </summary>
internal static class WakuuRuneAutoAnswer
{
    /// <summary>延迟作答：避开界面自带 1000ms 点击保护窗（+ 等界面把候选建好）。</summary>
    private const float AnswerDelaySeconds = 1.2f;

    /// <summary>按下候选后等这么久再核对（够界面跑完自己的收尾/露出下一屏）。</summary>
    private const float StepDelaySeconds = 0.5f;

    /// <summary>这一屏还没轮到显示时的重试间隔。</summary>
    private const float RetryDelaySeconds = 0.6f;

    /// <summary>等这一屏露出的最大次数（约 24s；期间真人可能正在操作别的屏）。超了交回真人。</summary>
    private const int MaxVisibleWaitAttempts = 40;

    /// <summary>「界面实例 → 归属席位」。键是弱引用（界面被回收即失效）。</summary>
    private static readonly ConditionalWeakTable<GodotObject, ScreenState> Screens = new();

    private sealed class ScreenState
    {
        /// <summary>这一屏属于哪一席（NetId）；0 = 未登记。</summary>
        internal ulong Seat;

        /// <summary>是否已经排程/处理过（防同一屏被驱动两次 —— <c>RelicsSelected</c> /
        /// <c>AfterOverlayShown</c> 都可能被多次调用）。</summary>
        internal bool Handled;

        /// <summary>本屏已经试过 reroll 的槽位（v1.6.0）。
        /// 界面在"reroll 结果和原来相同"时会原地返回、按钮也不变灰 ⇒ 只靠 Disabled 判断会无限重试。</summary>
        internal readonly HashSet<int> RerollAttempted = new();
    }

    // ---- 反射面（界面类型与私有字段一律反射，海克斯改名 ⇒ 整体退化为「不代答」，绝不拖垮游戏）----
    private static FieldInfo? _holdersField;          // List<Button>              候选（普通符文屏）
    private static FieldInfo? _relicsField;           // List<RelicModel>          候选对应的符文
    private static FieldInfo? _selfPickButtonsField;  // List<Button>              候选（自选池屏）
    private static FieldInfo? _selfPickPoolField;     // IReadOnlyList<RelicModel> 自选池
    private static FieldInfo? _selfPickConfirmField;  // Button?                   自选池确认
    private static FieldInfo? _playerRuneConfirmField; // Button?                  符文确认（设置里开了才用）
    private static FieldInfo? _pendingSlotField;      // int?                      已单击但未确认的槽位
    private static FieldInfo? _choiceLockedField;     // bool
    private static FieldInfo? _closedField;           // bool
    private static FieldInfo? _rerollButtonsField;    // List<Button>              RerollButton_{slotIndex}（v1.6.0）

    /// <summary>自主 reroll 开关（v1.6.0）：默认开；<c>HRLF_RUNE_REROLL=0</c> 关。</summary>
    private static readonly bool RerollEnabled =
        !string.Equals(System.Environment.GetEnvironmentVariable("HRLF_RUNE_REROLL"), "0", StringComparison.Ordinal);

    /// <summary>"烂"阈值（默认 0.4；<c>HRLF_RUNE_REROLL_THRESHOLD</c> 可覆盖，解析失败用默认）。</summary>
    private static readonly double RerollPoorThreshold = ReadThreshold();

    private static bool _warned;

    /// <summary>挂补丁时调用一次：解析界面私有字段（缺项只 WARN，不抛）。返回是否已就绪。</summary>
    internal static bool Install(Type screenType)
    {
        _holdersField = FindField(screenType, "_holders");
        _relicsField = FindField(screenType, "_relics");
        _selfPickButtonsField = FindField(screenType, "_selfPickButtons");
        _selfPickPoolField = FindField(screenType, "_selfPickPool");
        _selfPickConfirmField = FindField(screenType, "_selfPickConfirm");
        _playerRuneConfirmField = FindField(screenType, "_playerRuneConfirm");
        _pendingSlotField = FindField(screenType, "_pendingPlayerRuneSlot");
        _choiceLockedField = FindField(screenType, "_choiceLocked");
        _closedField = FindField(screenType, "_closed");
        _rerollButtonsField = FindField(screenType, "_rerollButtons");

        if (_holdersField == null || _relicsField == null)
        {
            WarnOnce($"海克斯符文界面的候选字段没解析到（_holders={_holdersField != null}, _relics={_relicsField != null}），"
                + "瓦库席位不会自动作答符文界面（真人点一下即可）。");
            return false;
        }

        return true;
    }

    /// <summary>③ 的创建窗口把「这一屏属于哪一席」登记进来（同一窗口内紧随其后被 <c>RelicsSelected</c> 消费）。</summary>
    internal static void AttachSeat(object? screen, ulong netId)
    {
        if (screen is not GodotObject godotObject || netId == 0UL)
        {
            return;
        }

        try
        {
            _ = Screens.GetValue(godotObject, _ => new ScreenState { Seat = netId });
        }
        catch (Exception exception)
        {
            WarnOnce($"登记符文界面归属失败: {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// 界面开始等待作答时调用：归属席位是「后台托管瓦库」⇒ 排程自动作答；否则只记一条日志（交真人）。
    /// </summary>
    internal static void TrySchedule(object? screen)
    {
        try
        {
            if (screen is not Node node || !GodotObject.IsInstanceValid(node))
            {
                return;
            }

            if (!Screens.TryGetValue((GodotObject)screen, out ScreenState? state) || state.Seat == 0UL || state.Handled)
            {
                return;
            }

            if (_holdersField == null || _relicsField == null)
            {
                return; // Install 已经 WARN 过，不再刷屏
            }

            if (!MainModBridge.IsLocalSeat(state.Seat))
            {
                return;
            }

            bool wakuuSeat = MainModBridge.IsAutomatedWakuuSeat(state.Seat);
            bool coopBotSeat = !wakuuSeat && MainModBridge.IsCoopBotSeat(state.Seat);
            if (!wakuuSeat && !coopBotSeat)
            {
                state.Handled = true;
                Log.Info($"{Entry.LogPrefix} 符文界面归属席位不是「托管席位（瓦库 / Co-op Bots）」，交真人作答: "
                    + $"seat={MainModBridge.SeatLabelById(state.Seat)}({state.Seat}), screen=HextechRuneSelectionScreen");
                return;
            }

            SceneTree? tree = node.GetTree();
            if (tree == null)
            {
                return;
            }

            state.Handled = true;
            ulong seat = state.Seat;
            string driver = wakuuSeat ? "瓦库" : "Co-op Bots";
            Log.Info($"{Entry.LogPrefix} {driver}席位符文界面已登记自动作答（延迟 {AnswerDelaySeconds:0.0}s 避开界面自带的 1s 点击保护窗）: "
                + $"seat={MainModBridge.SeatLabelById(seat)}({seat}), screen=HextechRuneSelectionScreen");
            tree.CreateTimer(AnswerDelaySeconds).Timeout += () => TryAnswer(node, seat, visibleAttempt: 1, retry: false);
        }
        catch (Exception exception)
        {
            WarnOnce($"符文界面自动作答登记失败（交回真人）: {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>作答案（等这一屏露出 → 读候选 → 按选定候选）。</summary>
    private static void TryAnswer(Node node, ulong seat, int visibleAttempt, bool retry)
    {
        try
        {
            if (!IsScreenOpen(node))
            {
                return;
            }

            // 同机多席位的符文界面会**同时入栈**，只有栈顶可交互（海克斯自己的层级如此）
            // ⇒ 这一屏还没露出来就等，等它露出来再按（== 逐席位依次作答）。
            if (node is not CanvasItem canvasItem || !canvasItem.IsVisibleInTree())
            {
                if (visibleAttempt >= MaxVisibleWaitAttempts)
                {
                    Log.Warn($"{Entry.LogPrefix} 瓦库符文界面一直没轮到显示（真人可能在操作别的屏），交回真人: "
                        + $"seat={MainModBridge.SeatLabelById(seat)}({seat}), attempts={visibleAttempt}");
                    return;
                }

                Schedule(node, () => TryAnswer(node, seat, visibleAttempt + 1, retry), RetryDelaySeconds);
                return;
            }

            if (ReadBool(_choiceLockedField, node))
            {
                return; // 已经有人选过了
            }

            if (!TryReadCandidatesPublic(node, out List<Button> buttons, out List<RelicModel> options, out bool selfPickMode))
            {
                Log.Info($"{Entry.LogPrefix} 瓦库符文界面没有可选项（例如「敌人 hex 预览」屏），交回真人: "
                    + $"seat={MainModBridge.SeatLabelById(seat)}({seat})");
                return;
            }

            // v1.6.0：三张候选**全烂**时先自主 reroll（海克斯每张候选各给一次，白给的次数不用白不用）。
            // reroll 会 RebuildCards（旧按钮全失效、还有 1s 点击保护窗重置）⇒ 按一次就重入 TryAnswer 重新读候选。
            if (!selfPickMode && TryRerollPoorOptions(node, seat, options))
            {
                Schedule(node, () => TryAnswer(node, seat, visibleAttempt: 1, retry: false), AnswerDelaySeconds);
                return;
            }

            // 选哪个：**按抓率**（v1.3.0；抓率 = 该符文"出现过/被选过"，来自真人+瓦库的每一屏）。
            // 并列时取最左 ⇒ 没有统计时行为与旧版"取最左"逐个下标一致。
            // 自选池屏（锻造选择：一屏一大堆符文）没有"3 选 1"的语义 ⇒ 仍取最左。
            int index = selfPickMode ? 0 : ChooseIndexByPickRate(options);
            if (index < 0)
            {
                index = 0;
            }

            Button target = buttons[index];
            if (!GodotObject.IsInstanceValid(target))
            {
                WarnOnce($"{Entry.LogPrefix} 选中的候选按钮已失效，交回真人: seat={MainModBridge.SeatLabelById(seat)}({seat})");
                return;
            }

            if (!retry)
            {
                Log.Info($"{Entry.LogPrefix} 瓦库符文界面自动作答: seat={MainModBridge.SeatLabelById(seat)}({seat}), "
                    + $"mode={(selfPickMode ? "自选池" : "符文选择")}, options=[{DescribeOptions(options)}], "
                    + $"picked={RelicEntry(options, index)}, "
                    + $"rule={(selfPickMode ? "自选池取最左" : "按抓率")}, 抓率=[{DescribePickRates(options)}], "
                    + "screen=HextechRuneSelectionScreen");
            }

            // 走游戏自己的点击路径（海克斯把 Button.Pressed 连到自己的 OnHolderSelected / SelectSelfPick）：
            // 不碰任何私有方法，将来界面内部改动也不会绕过它的副作用。
            target.EmitSignal(BaseButton.SignalName.Pressed);
            Schedule(node, () => ConfirmOrVerify(node, seat, selfPickMode, retryAllowed: !retry), StepDelaySeconds);
        }
        catch (Exception exception)
        {
            WarnOnce($"瓦库符文界面自动作答失败，交回真人: seat={MainModBridge.SeatLabelById(seat)}({seat}), "
                + $"error={exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// 按下候选之后：界面关了 = 成功；还开着就按它自己的收尾按钮
    /// （自选池屏要按「确认」；海克斯设置里开了「确认符文选择」时要按符文确认）。
    /// </summary>
    private static void ConfirmOrVerify(Node node, ulong seat, bool selfPickMode, bool retryAllowed)
    {
        try
        {
            if (!IsScreenOpen(node))
            {
                Log.Info($"{Entry.LogPrefix} 瓦库符文界面自动作答完成（界面已关闭）: "
                    + $"seat={MainModBridge.SeatLabelById(seat)}({seat}), screen=HextechRuneSelectionScreen");
                return;
            }

            if (ReadBool(_choiceLockedField, node))
            {
                // 界面已经受理（它自己在等鼠标松开 / 等批次收尾）⇒ 再等一轮确认它关掉，别重复按。
                Schedule(node, () => VerifyClosed(node, seat), StepDelaySeconds);
                return;
            }

            if (selfPickMode && TryPressButton(node, _selfPickConfirmField, "自选池确认"))
            {
                Schedule(node, () => VerifyClosed(node, seat), StepDelaySeconds);
                return;
            }

            if (HasPendingSlot(node) && TryPressButton(node, _playerRuneConfirmField, "符文确认（设置里开了「确认符文选择」）"))
            {
                Schedule(node, () => VerifyClosed(node, seat), StepDelaySeconds);
                return;
            }

            if (retryAllowed)
            {
                // 极少数情况（例如保护窗或界面晚半拍）单击没被受理 ⇒ 再按一次。
                Log.Info($"{Entry.LogPrefix} 瓦库符文界面单击后仍未关闭，重试一次: "
                    + $"seat={MainModBridge.SeatLabelById(seat)}({seat})");
                Schedule(node, () => TryAnswer(node, seat, visibleAttempt: 1, retry: true), RetryDelaySeconds);
                return;
            }

            Log.Warn($"{Entry.LogPrefix} 瓦库符文界面单击后仍未关闭（可能是多选 / 被第三方改写过的界面），交回真人: "
                + $"seat={MainModBridge.SeatLabelById(seat)}({seat}), screen=HextechRuneSelectionScreen");
        }
        catch (Exception exception)
        {
            WarnOnce($"瓦库符文界面作答后的收尾失败，交回真人: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void VerifyClosed(Node node, ulong seat)
    {
        if (!IsScreenOpen(node))
        {
            Log.Info($"{Entry.LogPrefix} 瓦库符文界面自动作答完成（界面已关闭）: "
                + $"seat={MainModBridge.SeatLabelById(seat)}({seat}), screen=HextechRuneSelectionScreen");
            return;
        }

        Log.Warn($"{Entry.LogPrefix} 瓦库符文界面自动作答后仍未关闭（可能界面被第三方改写 / 还在等鼠标松开），交回真人: "
            + $"seat={MainModBridge.SeatLabelById(seat)}({seat}), screen=HextechRuneSelectionScreen");
    }

    // ---- 读界面状态 -------------------------------------------------------------------------------

    private static bool IsScreenOpen(Node node)
    {
        if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree())
        {
            return false; // 已被移除/释放 = 这一屏已经结束
        }

        return !ReadBool(_closedField, node);
    }

    /// <summary>给角标刷新循环复用（同一个"这一屏还开着吗"口径）。</summary>
    internal static bool IsScreenOpenPublic(Node node) => IsScreenOpen(node);

    private static bool ReadBool(FieldInfo? field, object target)
    {
        if (field == null)
        {
            return false;
        }

        try
        {
            return field.GetValue(target) is true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasPendingSlot(object screen)
    {
        if (_pendingSlotField == null)
        {
            return false;
        }

        try
        {
            return _pendingSlotField.GetValue(screen) != null;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryPressButton(object screen, FieldInfo? field, string what)
    {
        if (field == null)
        {
            return false;
        }

        try
        {
            if (field.GetValue(screen) is not Button button || !GodotObject.IsInstanceValid(button))
            {
                return false;
            }

            button.EmitSignal(BaseButton.SignalName.Pressed);
            Log.Info($"{Entry.LogPrefix} 瓦库符文界面已按{what}: screen=HextechRuneSelectionScreen");
            return true;
        }
        catch (Exception exception)
        {
            WarnOnce($"按下符文界面的收尾按钮失败: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// 读候选按钮与它们对应的符文（普通屏 = <c>_holders</c>/<c>_relics</c>；自选池屏 = <c>_selfPickButtons</c>/<c>_selfPickPool</c>）。
    /// <c>internal</c> 给 <see cref="RunePickStats"/> 复用（抓率记账要同一份候选）。
    /// </summary>
    internal static bool TryReadCandidatesPublic(
        object screen,
        out List<Button> buttons,
        out List<RelicModel> options,
        out bool selfPickMode)
    {
        buttons = new List<Button>();
        options = new List<RelicModel>();
        selfPickMode = false;

        ReadList(_holdersField, screen, buttons);
        if (buttons.Count > 0)
        {
            ReadList(_relicsField, screen, options);
            return true;
        }

        ReadList(_selfPickButtonsField, screen, buttons);
        if (buttons.Count > 0)
        {
            selfPickMode = true;
            ReadList(_selfPickPoolField, screen, options);
            return true;
        }

        return false;
    }

    private static void ReadList<T>(FieldInfo? field, object screen, List<T> target) where T : class
    {
        if (field == null)
        {
            return;
        }

        try
        {
            if (field.GetValue(screen) is not IEnumerable items)
            {
                return;
            }

            foreach (object? item in items)
            {
                if (item is T typed)
                {
                    target.Add(typed);
                }
            }
        }
        catch (Exception exception)
        {
            WarnOnce($"读取符文界面候选失败: {exception.GetType().Name}: {exception.Message}");
        }
    }

    // ---- 自主 reroll（v1.6.0）----------------------------------------------------------------------

    /// <summary>
    /// 三张候选**全烂**（有数据且抓率 ≤ 阈值）就按一次 reroll 按钮。
    /// 返回 true = 已按，调用方应延迟重入 <see cref="TryAnswer"/>（界面会重建候选，旧按钮全失效）。
    ///
    /// <para>只走 <c>Button.Pressed</c> 信号（海克斯把 `RerollButton_{slot}` 的 Pressed 连到
    /// <c>OnRerollPressed(slot)</c>）；它自己带 `_choiceLocked` / 次数上限 / "新候选与旧相同则不动" 的守卫。</para>
    /// </summary>
    private static bool TryRerollPoorOptions(Node node, ulong seat, IReadOnlyList<RelicModel> options)
    {
        if (!RerollEnabled || _rerollButtonsField == null)
        {
            return false;
        }

        ScreenState? state = GetState(node);
        if (state == null)
        {
            return false;
        }

        List<Button> rerollButtons = new();
        ReadList(_rerollButtonsField, node, rerollButtons);
        if (rerollButtons.Count == 0 || rerollButtons.Count != options.Count)
        {
            return false; // 海克斯没开 reroll（_rerollFunc 为 null 时根本不建按钮）/ 列表没对齐 ⇒ 不干预
        }

        List<string> entries = new(options.Count);
        for (int i = 0; i < options.Count; i++)
        {
            entries.Add(RelicEntry(options, i));
        }

        List<bool> canReroll = new(rerollButtons.Count);
        for (int i = 0; i < rerollButtons.Count; i++)
        {
            Button button = rerollButtons[i];
            canReroll.Add(GodotObject.IsInstanceValid(button)
                && !button.Disabled
                && !state.RerollAttempted.Contains(i));
        }

        int slot = RuneRerollPolicy.ChooseSlotToReroll(entries, RunePickStats.Lookup, canReroll, RerollPoorThreshold);
        if (slot < 0)
        {
            return false;
        }

        Button target = rerollButtons[slot];
        state.RerollAttempted.Add(slot);
        Log.Info($"{Entry.LogPrefix} 瓦库符文选项全烂，自主 reroll: seat={MainModBridge.SeatLabelById(seat)}({seat}), "
            + $"slot={slot}, 旧={entries[slot]}, options=[{DescribeOptions(options)}], "
            + $"抓率=[{DescribePickRates(options)}], 阈值={RerollPoorThreshold:0.00}, screen=HextechRuneSelectionScreen");
        target.EmitSignal(BaseButton.SignalName.Pressed);
        return true;
    }

    private static ScreenState? GetState(Node node)
    {
        try
        {
            return Screens.TryGetValue((GodotObject)node, out ScreenState? state) ? state : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>读阈值（`HRLF_RUNE_REROLL_THRESHOLD`，要求 0 &lt; x &lt; 1；解析失败用默认 0.4）。</summary>
    private static double ReadThreshold()
    {
        string? raw = System.Environment.GetEnvironmentVariable("HRLF_RUNE_REROLL_THRESHOLD");
        if (!string.IsNullOrWhiteSpace(raw)
            && double.TryParse(
                raw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double value)
            && value > 0.0
            && value < 1.0)
        {
            return value;
        }

        return RuneRerollPolicy.DefaultPoorThreshold;
    }

    // ---- 选哪一个 ---------------------------------------------------------------------------------
    // 规则 = **按抓率**（v1.3.0，用户 2026-10-11 点名；统计见 RunePickStats / RunePickRatePolicy）。
    //   · 抓率 = picked/offered（Laplace 平滑），真人和瓦库的每一屏都记；瓦库只读。
    //   · **并列取最左** ⇒ 没有任何统计时行为与旧版"取最左"完全一致。
    //   · 稀有度不能当判据（用户实测：海克斯每次给的 3 个选项稀有度一致）。
    // 选项很烂时自主 reroll = v1.6.0（见上方 TryRerollPoorOptions / RuneRerollPolicy）。

    private static int ChooseIndexByPickRate(IReadOnlyList<RelicModel> options)
    {
        List<string> entries = new(options.Count);
        for (int i = 0; i < options.Count; i++)
        {
            entries.Add(RelicEntry(options, i));
        }

        return RunePickRatePolicy.ChooseIndex(entries, RunePickStats.Lookup);
    }

    private static string DescribePickRates(IReadOnlyList<RelicModel> options)
    {
        List<string> entries = new(options.Count);
        for (int i = 0; i < options.Count; i++)
        {
            entries.Add(RelicEntry(options, i));
        }

        return RunePickStats.DescribeForLog(entries);
    }

    private static RelicModel? RelicAt(IReadOnlyList<RelicModel> options, int index)
    {
        return index >= 0 && index < options.Count ? options[index] : null;
    }

    private static string RelicEntry(IReadOnlyList<RelicModel> options, int index)
    {
        RelicModel? relic = RelicAt(options, index);
        try
        {
            return relic?.Id.Entry ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static string DescribeOptions(IReadOnlyList<RelicModel> options)
    {
        List<string> parts = new();
        for (int i = 0; i < options.Count; i++)
        {
            parts.Add(RelicEntry(options, i));
        }

        return string.Join(", ", parts);
    }

    // ---- 小工具 -----------------------------------------------------------------------------------

    private static void Schedule(Node node, Action action, float delaySeconds)
    {
        SceneTree? tree = node.GetTree();
        if (tree == null)
        {
            return;
        }

        tree.CreateTimer(delaySeconds).Timeout += action;
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        try
        {
            return type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
        catch
        {
            return null;
        }
    }

    private static void WarnOnce(string message)
    {
        if (_warned)
        {
            return;
        }

        _warned = true;
        Log.Warn($"{Entry.LogPrefix} {message}");
    }
}
