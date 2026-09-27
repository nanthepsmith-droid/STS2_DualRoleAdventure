using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 本地多控会话的生命周期守卫（r158）。
///
/// 为什么需要：会话只在「进局结束（`RunManager.CleanUp`）」或「ESC 重启房间」时关闭。
/// 玩家若从我们的大厅页（角色选择 / 自定义 / 每日）**直接退回主菜单**，会话会一直开着 ⇒
/// ① 官方联机页面里冒出我们的席位增减 / 切人按钮；② 官方 host 拿到被改绑的
/// `LocalContext.NetId` 与残留回环 NetService —— 表现为「官方联机入口进不去」。
///
/// 判据（保守、只在确认"没有我们的页面、也没进局"时才关）：
/// - 未进局（`!RunManager.IsInProgress`）；
/// - 且树里**没有**任何仍挂在本地回环 `NetService` 上的大厅页（角色选择页 / 自定义页 / 每日页）；
/// - r161 再加一档：**我们自己 push 的那个大厅页还开着**（`IsInsideTree() &amp;&amp; Visible`）也算在流程中
///   —— 每日大厅是异步建的，`_lobby` 就绪之前不能清会话（断网时必现，见 `HasLocalLobbyScreen` 注释）；
/// - 连续满足约 1 秒（跨过"页面切换的一帧空窗"）。
/// </summary>
internal sealed partial class LocalSelfCoopSessionGuard : Node
{
    private const string NodeName = "LocalSelfCoopSessionGuard";
    private const double CheckIntervalSeconds = 0.5;
    private const double IdleGraceSeconds = 1.0;

    private static LocalSelfCoopSessionGuard? _instance;

    private static readonly System.Reflection.FieldInfo? DailyLobbyField =
        AccessTools.Field(typeof(NDailyRunScreen), "_lobby");

    private double _checkAccumulator;
    private double _idleSeconds;

    /// <summary>在本地多控会话启用时挂到 `NGame` 上（重复调用无副作用）。</summary>
    internal static void EnsureAttached()
    {
        if (_instance != null && GodotObject.IsInstanceValid(_instance))
        {
            return;
        }

        NGame? game = NGame.Instance;
        if (game == null)
        {
            return;
        }

        LocalSelfCoopSessionGuard guard = new() { Name = NodeName };
        game.AddChildSafely(guard);
        _instance = guard;
    }

    public override void _Process(double delta)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            _idleSeconds = 0;
            return;
        }

        if (RunManager.Instance?.IsInProgress == true)
        {
            _idleSeconds = 0;
            return;
        }

        _checkAccumulator += delta;
        if (_checkAccumulator < CheckIntervalSeconds)
        {
            return;
        }

        _checkAccumulator = 0;
        if (HasLocalLobbyScreen())
        {
            _idleSeconds = 0;
            return;
        }

        _idleSeconds += CheckIntervalSeconds;
        if (_idleSeconds < IdleGraceSeconds)
        {
            return;
        }

        _idleSeconds = 0;
        LocalMultiControlLogger.Info("本地多控会话已自动结束：当前没有本地多角色大厅页面，且未进局。");
        LocalSelfCoopContext.Disable("no-local-lobby-screen");
    }

    private static bool HasLocalLobbyScreen()
    {
        // r161：我们自己 push 的大厅页**还开着（且可见）**就算"在流程中"。
        // 为什么需要：每日页的大厅是**异步**建的（先 await 时间服务器，断网时 DNS 失败还要重试两回），
        // 这段窗口里 `_lobby` 一直是 null ⇒ 只看大厅判据会误判"没有大厅页"并把会话清掉，
        // 实机表现 = 断网进「本地·每日挑战」只有单人、连加人按钮都没有（r159 断网局实测）。
        // ⚠ 判据必须带 `Visible`：`NSubmenuStack.Pop` 只把页面 `Visible = false`、**并不移出树**，
        // 少了这一条就退回 r158 要修的老问题（从大厅页退回主菜单后会话残留、官方页冒出我们的按钮）。
        CanvasItem? activeLobbyScreen = LocalSelfCoopContext.ActiveSelfCoopLobbyScreen;
        if (activeLobbyScreen != null
            && GodotObject.IsInstanceValid(activeLobbyScreen)
            && LocalSelfCoopLobbyScreenPolicy.IsPageOpen(
                activeLobbyScreen.IsInsideTree(), activeLobbyScreen.Visible))
        {
            return true;
        }

        NCharacterSelectScreen? characterSelect = LocalSelfCoopContext.ActiveCharacterSelectScreen;
        if (characterSelect != null
            && GodotObject.IsInstanceValid(characterSelect)
            && characterSelect.IsInsideTree())
        {
            return true;
        }

        NGame? game = NGame.Instance;
        if (game == null)
        {
            return false;
        }

        foreach (Node node in Enumerate(game))
        {
            if (node is NCustomRunScreen customRunScreen)
            {
                if (customRunScreen.Lobby?.NetService is LocalLoopbackHostGameService)
                {
                    return true;
                }

                continue;
            }

            if (node is NDailyRunScreen
                && DailyLobbyField?.GetValue(node) is StartRunLobby dailyLobby
                && dailyLobby.NetService is LocalLoopbackHostGameService)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<Node> Enumerate(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            yield return child;
            foreach (Node nested in Enumerate(child))
            {
                yield return nested;
            }
        }
    }
}
