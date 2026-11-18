using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NGame), nameof(NGame._Input))]
internal static class NGameInputPatch
{
    [HarmonyPostfix]
    private static void Postfix(InputEvent inputEvent)
    {
        LocalGamepadAxisRouter.EnsurePollerAttached();

        if (inputEvent is not InputEventKey keyEvent || !keyEvent.IsReleased())
        {
            return;
        }

        Key keycode = keyEvent.Keycode;
        Key physicalKeycode = keyEvent.PhysicalKeycode;

        // Tab cycles forward, Shift+Tab cycles backward; legacy keys kept as aliases.
        bool isTab = keycode == Key.Tab || physicalKeycode == Key.Tab;

        bool isPrevious = (isTab && keyEvent.ShiftPressed) ||
                          keycode == Key.Bracketleft ||
                          physicalKeycode == Key.Bracketleft ||
                          keycode == Key.T ||
                          physicalKeycode == Key.T;

        bool isNext = (isTab && !keyEvent.ShiftPressed) ||
                      keycode == Key.Bracketright ||
                      physicalKeycode == Key.Bracketright ||
                      keycode == Key.R ||
                      physicalKeycode == Key.R ||
                      keycode == Key.Slash ||
                      physicalKeycode == Key.Slash;

        bool isDecreasePlayerCount = keycode == Key.Minus || physicalKeycode == Key.Minus;
        bool isIncreasePlayerCount = keycode == Key.Equal ||
                                     physicalKeycode == Key.Equal ||
                                     keycode == Key.Plus ||
                                     physicalKeycode == Key.Plus;

        if (!RunManager.Instance.IsInProgress &&
            LocalSelfCoopContext.IsEnabled &&
            (isDecreasePlayerCount || isIncreasePlayerCount))
        {
            int delta = isIncreasePlayerCount ? 1 : -1;
            string hotkeyLabel = isIncreasePlayerCount ? "+ / =" : "-";
            if (LocalSelfCoopContext.AdjustDesiredLocalPlayerCount(delta, $"hotkey:{hotkeyLabel}"))
            {
                int targetCount = LocalSelfCoopContext.DesiredLocalPlayerCount;
                LocalMultiControlLogger.Info($"检测到人数热键 {hotkeyLabel}，本地人数已调整为 {targetCount}");
                NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create(LocalModText.LocalPlayerCount(targetCount)));
            }

            return;
        }

        // 小键盘快速切人（2026-11-18 §排期 5）：**只在局内**生效 —— 局外的大键盘 - / = 是上面那条
        // "本地人数增减"热键，这一段一个字都不动它。
        int seatHotkey = ResolveSeatHotkeyNumber(keycode, physicalKeycode);
        if (seatHotkey > 0 && RunManager.Instance.IsInProgress)
        {
            // 开关（设置页「小键盘快速切人」，默认开）：关掉时小键盘完全不响应。
            if (!LocalWakuuAutopilotConfig.SeatHotkeySwitch)
            {
                return;
            }

            // 出牌 / 选牌 / 选目标进行中不切：切人会换 owner，把选牌上下文打断（同 CanSwitchDuringCombat）。
            // 这里静默跳过（不弹字）——玩家此刻多半是在用大键盘 1~0 选牌，弹提示纯噪声。
            if (IsVanillaHandShortcutBusy())
            {
                return;
            }

            SeatHotkeyResult result = LocalMultiControlRuntime.SwitchControlledPlayerToSeat(
                seatHotkey, $"hotkey:seat-{seatHotkey}");
            ReportSeatHotkey(result);
            return;
        }

        if (!isPrevious && !isNext)
        {
            return;
        }

        if (isPrevious)
        {
            LocalMultiControlLogger.Info("检测到切换热键: Shift+Tab / [ / T (反切)");
            if (RunManager.Instance.IsInProgress)
            {
                LocalMultiControlRuntime.SwitchPreviousControlledPlayer("hotkey:S-Tab/[/T");
            }
            else
            {
                LocalSelfCoopContext.SwitchLobbyEditingPlayer(next: false);
            }

            return;
        }

        LocalMultiControlLogger.Info("检测到切换热键: Tab / ] / R (正切)");
        if (RunManager.Instance.IsInProgress)
        {
            LocalMultiControlRuntime.SwitchNextControlledPlayer("hotkey:Tab/]/R");
        }
        else
        {
            LocalSelfCoopContext.SwitchLobbyEditingPlayer(next: true);
        }
    }

    /// <summary>
    /// **小键盘** <c>1</c>~<c>9</c> <c>/</c> <c>*</c> <c>-</c> ⇒ 号位 <c>1</c>~<c>12</c>（0 = 不是号位键）。
    ///
    /// <para>⚠ 必须用小键盘、**不能用大键盘数字键**（2026-11-18 实机）：原版把大键盘
    /// <c>1</c>~<c>0</c> 绑成"选第 N 张手牌"（<c>MegaInput.selectCard1..10</c>，见
    /// <c>NInputManager._defaultInputMap</c>）⇒ 大键盘第一版实机就撞车。</para>
    ///
    /// <para>同时看逻辑键码与物理键码：NumLock 关着时小键盘会给出 <c>Home</c>/<c>Up</c> 之类的
    /// <c>Keycode</c>，而 <c>PhysicalKeycode</c> 始终是 <c>Kp*</c>（与 Tab 循环键的做法一致）。</para>
    /// </summary>
    private static int ResolveSeatHotkeyNumber(Key keycode, Key physicalKeycode)
    {
        int byKeycode = MapSeatHotkeyNumber(keycode);
        return byKeycode > 0 ? byKeycode : MapSeatHotkeyNumber(physicalKeycode);
    }

    private static int MapSeatHotkeyNumber(Key key)
    {
        return key switch
        {
            Key.Kp1 => 1,
            Key.Kp2 => 2,
            Key.Kp3 => 3,
            Key.Kp4 => 4,
            Key.Kp5 => 5,
            Key.Kp6 => 6,
            Key.Kp7 => 7,
            Key.Kp8 => 8,
            Key.Kp9 => 9,
            Key.KpDivide => 10,
            Key.KpMultiply => 11,
            Key.KpSubtract => 12,
            _ => 0,
        };
    }

    /// <summary>
    /// 原版此刻是否正把 <c>1</c>~<c>0</c> 当"选第 N 张手牌"用（<c>MegaInput.selectCard1..10</c>）。
    /// 选牌 / 拖牌期间切人会破坏出牌上下文（见 <c>CanSwitchDuringCombat</c>），这里先静默让给原版。
    /// </summary>
    private static bool IsVanillaHandShortcutBusy()
    {
        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            return false;
        }

        NPlayerHand hand = combatUi.Hand;
        return hand.InCardPlay || hand.IsInCardSelection || (NTargetManager.Instance?.IsInSelection ?? false);
    }

    /// <summary>把判定结果转成给玩家的一行提示（只在"按了没用"时提示，成功/号位就是当前席位不弹）。</summary>
    private static void ReportSeatHotkey(SeatHotkeyResult result)
    {
        if (!result.NeedsNotice)
        {
            return;
        }

        string text = result.Status switch
        {
            SeatHotkeyStatus.OutOfRange or SeatHotkeyStatus.SessionNotInitialized
                => LocalModText.SeatHotkeyNoSuchSeat(result.SeatNumber, result.SeatCount),
            SeatHotkeyStatus.NotInCombat => LocalModText.SeatHotkeyNotInCombat(result.SeatNumber),
            _ => LocalModText.SeatHotkeyBlocked(result.SeatNumber),
        };

        NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create(text));
    }
}
