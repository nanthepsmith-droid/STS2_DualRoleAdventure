using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.UI;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 每日挑战（Daily）本地多控接入（r156）：入口拦截 + 大厅席位 reconcile + 按日期种子分配角色 + 人数 UI。
///
/// 背景与差异（详见 `maintenance-docs/decision-records/本地多角色扩展到Daily模式可行性分析.md`）：
/// ① 游戏把「每日多人」当正经 feature（最多 4 人、按人数分榜），所以只要把联机菜单的 Daily 入口
///    劫持到本地回环即可复用整套流程；
/// ② 与 Custom 的三处硬差异：大厅**异步**建（先 await 时间服务器，`_lobby` 会长时间为 null）、
///    人数上限写死 4（`StartRunLobby._maxPlayers` readonly）、**角色由「日期种子 + 人数」的 RNG 决定**，
///    页面上没有选人按钮 —— 所以角色分配必须由我们对每个席位各驱动一次游戏自己的 `SetupLobbyParams`；
/// ③ 每日榜分数上传由 `DailyRunUtilityUploadScorePatch` 单独拦截（见 `DailyRunUtilityPatch.cs`）。
///
/// ⚠ 角色分配为什么是「逐席位切 sender 再调游戏方法」：`SetupLobbyParams` 只给
/// `lobby.LocalPlayer`（= `Players.Find(p =&gt; p.id == NetService.NetId)`，回环下即当前 sender）设角色。
/// 所以把 sender 依次切到每个本地席位各调用一次，游戏就会按同一 RNG 序列把各 slot 的角色依次设好 ——
/// 不复制游戏 RNG（避免日期/人数 seed 算法漂移），也不碰 `isRandomCharacterResolution`（Daily 传 true 会抛异常）。
/// </summary>
/// <summary>
/// 本地多角色「每日挑战」入口（r156）。
///
/// ⚠ 设计修正（2026-09-27 用户反馈「不能玩原版多人联机每日游戏」）：
/// **不再劫持官方 `NMultiplayerHostSubmenu.StartHost(GameMode.Daily)`** —— 官方多人每日是正经 feature，
/// 必须还给玩家；本机多角色改走联机菜单上独立注入的「单人每日挑战」卡片
/// （`NMultiplayerHostSubmenuPatch` 注入，第 5 张）调用本入口。
///
/// 为什么不做成劫持：Custom 可以劫持是因为 Custom 原本没有"官方多人自定义"这条正当用法；
/// Daily 有（最多 4 人、按人数分榜），劫持会直接砍掉一个玩家在用的官方功能。
/// </summary>
internal static class LocalDailySelfCoopEntry
{
    internal static void Enter(NMultiplayerHostSubmenu submenu)
    {
        NSubmenuStack? stack = AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(submenu) as NSubmenuStack;
        if (stack == null)
        {
            LocalMultiControlLogger.Warn("无法进入每日挑战：未找到 NSubmenuStack。");
            return;
        }

        LocalMultiControlLogger.Info("单人多角色每日挑战入口：改走本地回环开局。");
        LocalSelfCoopSaveTag.ClearCurrentProfile();
        SaveManager.Instance.DeleteCurrentMultiplayerRun();

        // Daily 页建厅时写死 4 人上限且不可扩容 ⇒ 进页前先把本地席位上限收到 4
        LocalSelfCoopContext.SetLobbyLocalPlayerLimit(DailyLobbyPolicy.MaxDailyLocalPlayerCount, "daily-enter");

        ulong primaryPlayerId = LocalSelfCoopContext.ResolvePrimaryPlayerId();
        LocalSelfCoopContext.UseSavedWakuuPlayerIds(Array.Empty<ulong>());
        LocalSelfCoopSaveTag.MarkCurrentProfile(
            LocalSelfCoopContext.LocalPlayerIds.Take(LocalSelfCoopContext.DesiredLocalPlayerCount).ToList());

        LocalLoopbackHostGameService netService = new(primaryPlayerId);
        LocalSelfCoopContext.Enable(netService);

        NDailyRunScreen dailyRunScreen = stack.GetSubmenuType<NDailyRunScreen>();
        // Daily 的 InitializeMultiplayerAsHost 没有人数参数（上限在建厅时写死）
        dailyRunScreen.InitializeMultiplayerAsHost(netService);
        stack.Push(dailyRunScreen);
        // 会话守卫据此判断"还停在我们自己的大厅页上"（r161）：每日大厅是异步建的，
        // `_lobby` 在时间服务器返回前一直是 null，只按大厅判据会把会话误清掉（断网时必现）。
        LocalSelfCoopContext.ActiveSelfCoopLobbyScreen = dailyRunScreen;
        NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create(LocalModText.EnteredDailySelfCoopHint));
    }
}

/// <summary>
/// 每日大厅的本地席位 reconcile + 角色分配。
///
/// 与 Custom 版的关键差异：Daily 页没有公开的 `Lobby` 属性（只有私有 `_lobby`），
/// 且大厅是异步建的 —— `OnSubmenuOpened` 时 `_lobby` 往往还是 null，所以这里做成幂等的
/// 「就绪才动手」：由 `_Process` 每帧重试，直到 `_lobby` 出现并完成一次同步。
/// </summary>
[HarmonyPatch(typeof(NDailyRunScreen), nameof(NDailyRunScreen.OnSubmenuOpened))]
internal static class NDailyRunScreenLocalPlayersOpenPatch
{
    private const int MaxLocalAscensionLevel = 10;

    private static readonly FieldInfo? LobbyField = AccessTools.Field(typeof(NDailyRunScreen), "_lobby");
    private static readonly MethodInfo? SetupLobbyParamsMethod = AccessTools.Method(typeof(NDailyRunScreen), "SetupLobbyParams");
    private static readonly MethodInfo? InitializeDisplayMethod = AccessTools.Method(typeof(NDailyRunScreen), "InitializeDisplay");
    private static readonly FieldInfo? RemotePlayerContainerField = AccessTools.Field(typeof(NDailyRunScreen), "_remotePlayerContainer");

    private static bool _isReconciling;
    private static int _assignedSeatSignature;
    private static ulong _lastRenderedSenderId;

    [HarmonyPostfix]
    private static void Postfix(NDailyRunScreen __instance)
    {
        TryReconcileLocalPlayers(__instance);
    }

    /// <summary>离开每日页时清掉「已分配角色」的指纹（下次进页重新分配）。</summary>
    internal static void ResetAssignmentState()
    {
        _assignedSeatSignature = 0;
        _isReconciling = false;
        _lastRenderedSenderId = 0;
    }

    /// <summary>
    /// 角色卡跟随「当前席位」重绘（r158）。
    ///
    /// 每日页的角色卡由游戏 `InitializeDisplay()` 渲染 `_lobby.LocalPlayer.character`，
    /// 而我们的 ◀/▶ 切换只改回环 sender ⇒ 不重绘就会出现「切到哪个席位都显示同一个角色」。
    /// 这里在 `_Process` 里检测 sender 变化并补一次重绘（同时也覆盖手柄切换等其它路径）。
    /// </summary>
    internal static void RefreshDisplayForCurrentSeat(NDailyRunScreen screen)
    {
        if (!LocalSelfCoopContext.IsEnabled || InitializeDisplayMethod == null)
        {
            return;
        }

        if (LobbyField?.GetValue(screen) is not StartRunLobby lobby
            || lobby.NetService is not LocalLoopbackHostGameService loopback)
        {
            return;
        }

        if (loopback.NetId == _lastRenderedSenderId)
        {
            return;
        }

        _lastRenderedSenderId = loopback.NetId;
        try
        {
            InitializeDisplayMethod.Invoke(screen, null);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"每日挑战角色卡重绘失败(忽略): {exception.Message}");
        }
    }

    /// <summary>
    /// 补齐每日页的「席位卡」（`NRemoteLobbyPlayer`，瓦库托管勾选框就挂在这上面）。
    ///
    /// 为什么卡片会缺：游戏 `OnPlayerConnected` 的过滤是
    /// `player.id != lobby.LocalPlayer.id || _displayLocalPlayer`，而每日页传的是 `displayLocalPlayer: false`；
    /// 我们加伪席位时把回环 sender 切到了该席位 ⇒ 那一刻它被当成"本地玩家自己"⇒ **不建卡片**。
    /// 所以这里在席位稳定、sender 已回主席位之后，对缺失的席位补调一次 `screen.PlayerConnected(player)`。
    /// </summary>
    private static void EnsureRemotePlayerCards(NDailyRunScreen screen, StartRunLobby lobby, List<ulong> targetPlayerIds)
    {
        if (RemotePlayerContainerField?.GetValue(screen) is not Node container)
        {
            return;
        }

        // ⚠ 写后读必须**实时**：刚补建的席位卡要立刻被下一次「已存在」判定看到，否则会每帧重复补建
        // （R2 第一版用 TTL 缓存踩过：一局 `席位卡已补建` 199 次、added 累计 342 张 ⇒ 选人界面「无限玩家」）
        HashSet<ulong> existingSeatIds = new();
        foreach (Node node in LocalNodeTree.EnumerateDescendants(container))
        {
            if (node is NRemoteLobbyPlayer playerNode)
            {
                existingSeatIds.Add(playerNode.PlayerId);
            }
        }

        int added = 0;
        foreach (ulong seatId in targetPlayerIds)
        {
            // 主席位就是"本地玩家自己"，每日页不显示它的卡片（与游戏一致）
            if (seatId == LocalSelfCoopContext.PrimaryPlayerId || existingSeatIds.Contains(seatId))
            {
                continue;
            }

            // StartRunLobbyPlayer 是 struct：FirstOrDefault 落空时返回默认值（id = 0），用 id 回验
            StartRunLobbyPlayer player = lobby.Players.FirstOrDefault(candidate => candidate.id == seatId);
            if (player.id != seatId)
            {
                continue;
            }

            screen.PlayerConnected(player);
            added++;
        }

        if (added > 0)
        {
            LocalMultiControlLogger.Info(
                $"每日挑战席位卡已补建: added={added}, seats={string.Join(",", targetPlayerIds)}");
        }
    }

    internal static void TryReconcileLocalPlayers(NDailyRunScreen screen, bool forceCharacterAssignment = false)
    {
        if (_isReconciling || !LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        if (LobbyField?.GetValue(screen) is not StartRunLobby lobby)
        {
            // 大厅还在等时间服务器（异步建厅）—— 交给 _Process 下一帧重试
            return;
        }

        if (lobby.NetService is not LocalLoopbackHostGameService loopbackService)
        {
            return;
        }

        int seatLimit = DailyLobbyPolicy.ClampSeatCount(LocalSelfCoopContext.LobbyLocalPlayerLimit);
        List<ulong> targetPlayerIds = LocalSelfCoopContext.LocalPlayerIds
            .Take(Math.Min(LocalSelfCoopContext.DesiredLocalPlayerCount, seatLimit))
            .ToList();
        if (targetPlayerIds.Count <= 1)
        {
            return;
        }

        List<ulong> lobbySeatIds = lobby.Players.Select(player => player.id).ToList();
        if (DailyLobbyPolicy.NeedsReconcile(lobbySeatIds, targetPlayerIds, LocalSelfCoopContext.LocalPlayerIds))
        {
            _isReconciling = true;
            try
            {
                UnlockState unlockState = SaveManager.Instance.GenerateUnlockStateFromProgress();
                SerializableUnlockState serializableUnlockState = unlockState.ToSerializable();

                int added = 0;
                foreach (ulong playerId in targetPlayerIds)
                {
                    if (lobby.Players.Any(player => player.id == playerId))
                    {
                        continue;
                    }

                    loopbackService.SetCurrentSenderId(playerId);
                    _ = lobby.AddLocalHostPlayerInternal(serializableUnlockState, MaxLocalAscensionLevel);
                    added++;
                }

                // Daily 页固定 4 席，超出的本地伪席位必须移除，否则会一直卡在「等待其他玩家」
                List<ulong> removablePlayerIds = LocalSelfCoopContext.LocalPlayerIds
                    .Skip(targetPlayerIds.Count)
                    .ToList();
                int removed = 0;
                foreach (ulong removableId in removablePlayerIds)
                {
                    int playerIndex = lobby.Players.FindIndex(player => player.id == removableId);
                    if (playerIndex < 0)
                    {
                        continue;
                    }

                    StartRunLobbyPlayer removedPlayer = lobby.Players[playerIndex];
                    lobby.Players.RemoveAt(playerIndex);
                    lobby.InputSynchronizer.OnPlayerDisconnected(removedPlayer.id);
                    screen.RemotePlayerDisconnected(removedPlayer);
                    removed++;
                }

                bool readyChanged = false;
                for (int i = 0; i < lobby.Players.Count; i++)
                {
                    StartRunLobbyPlayer player = lobby.Players[i];
                    if (player.id == LocalSelfCoopContext.PrimaryPlayerId || player.isReady)
                    {
                        continue;
                    }

                    player.isReady = true;
                    lobby.Players[i] = player;
                    screen.PlayerChanged(player, false);
                    readyChanged = true;
                }

                LocalSelfCoopContext.EnsureLobbySenderContext("daily-run-opened");
                LocalMultiControlLogger.Info(
                    $"每日挑战大厅本地人数已同步: target={targetPlayerIds.Count}, actual={lobby.Players.Count}, "
                    + $"added={added}, removed={removed}, readyChanged={readyChanged}");
            }
            finally
            {
                _isReconciling = false;
            }
        }

        TryAssignDailyCharacters(screen, lobby, loopbackService, targetPlayerIds, forceCharacterAssignment);
        // 席位卡（瓦库勾选框的宿主）必须补建：每日页 displayLocalPlayer=false，
        // 加席位时它们会被当成"本地玩家自己"而跳过建卡（r158）
        EnsureRemotePlayerCards(screen, lobby, targetPlayerIds);
    }

    /// <summary>
    /// 按每日种子给每个本地席位分配角色。
    ///
    /// 做法：把回环 sender 依次切到每个席位，各调用一次游戏自己的 `SetupLobbyParams(lobby)` ——
    /// 它用「日期 + 人数」派生的 RNG 按 `lobby.Players` 顺序 roll 角色，并只给「当前本地玩家」设角色；
    /// 逐个席位驱动一遍就能把全部席位设成"各自 slot 该有的角色"，与官方多人大厅行为一致。
    /// 「禁止随机角色」由游戏保证：我们从不传 `isRandomCharacterResolution: true`。
    /// </summary>
    private static void TryAssignDailyCharacters(
        NDailyRunScreen screen,
        StartRunLobby lobby,
        LocalLoopbackHostGameService loopbackService,
        List<ulong> targetPlayerIds,
        bool force)
    {
        if (SetupLobbyParamsMethod == null)
        {
            return;
        }

        // 只认「大厅里、顺序保持、且正好等于目标集合」的本地席位（顺序 = 游戏 roll 角色的顺序）
        List<ulong> orderedSeats = lobby.Players
            .Select(player => player.id)
            .Where(id => LocalSelfCoopContext.LocalPlayerIds.Contains(id))
            .ToList();
        if (orderedSeats.Count == 0
            || orderedSeats.Any(id => !targetPlayerIds.Contains(id))
            || targetPlayerIds.Any(id => !orderedSeats.Contains(id)))
        {
            return;
        }

        if (!force && !DailyLobbyPolicy.NeedsCharacterAssignment(orderedSeats, _assignedSeatSignature))
        {
            return;
        }

        try
        {
            foreach (ulong seatId in orderedSeats)
            {
                loopbackService.SetCurrentSenderId(seatId);
                LocalContext.NetId = seatId;
                SetupLobbyParamsMethod.Invoke(screen, new object[] { lobby });
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"每日挑战角色分配失败(保持游戏默认角色): {exception.Message}");
            return;
        }
        finally
        {
            LocalSelfCoopContext.EnsureLobbySenderContext("daily-characters-assigned");
        }

        _assignedSeatSignature = DailyLobbyPolicy.ComputeSeatSignature(orderedSeats);
        LocalMultiControlLogger.Info(
            $"每日挑战角色已按日期种子分配: seats={DescribeSeats(lobby, orderedSeats)}, "
            + $"signature={_assignedSeatSignature}");
    }

    private static string DescribeSeats(StartRunLobby lobby, IReadOnlyList<ulong> seatIds)
    {
        return string.Join(",", seatIds.Select(seatId =>
        {
            StartRunLobbyPlayer? player = lobby.Players.FirstOrDefault(candidate => candidate.id == seatId);
            string characterId = player?.character?.Id.Entry ?? "unknown";
            return $"{seatId}:{characterId}";
        }));
    }
}

[HarmonyPatch(typeof(NDailyRunScreen), nameof(NDailyRunScreen._Process))]
internal static class NDailyRunScreenLocalPlayersProcessPatch
{
    [HarmonyPostfix]
    private static void Postfix(NDailyRunScreen __instance)
    {
        // 大厅是异步建的：每帧重试，直到就绪并完成一次同步（幂等，无变化时零副作用）
        NDailyRunScreenLocalPlayersOpenPatch.TryReconcileLocalPlayers(__instance);
        // 席位切换后让角色卡跟随（否则"切到哪个席位都显示同一个角色"）
        NDailyRunScreenLocalPlayersOpenPatch.RefreshDisplayForCurrentSeat(__instance);
    }
}

[HarmonyPatch(typeof(NDailyRunScreen), nameof(NDailyRunScreen.OnSubmenuClosed))]
internal static class NDailyRunScreenLocalPlayersClosePatch
{
    [HarmonyPrefix]
    private static void Prefix()
    {
        NDailyRunScreenLocalPlayersOpenPatch.ResetAssignmentState();
        LocalSelfCoopContext.ResetLobbyLocalPlayerLimit("daily-closed");
    }
}

/// <summary>
/// 出征前的最后校正：Daily 的 `OnEmbarkPressed` 会对「当前本地玩家」`SetReady(true)` 并要求
/// `IsAboutToBeginGame()`，所以这里确保 ① 席位与角色都已就绪（强制重跑一次分配）
/// ② 当前 sender 回到主席位，避免把 ready 打到别的席位上。
/// </summary>
[HarmonyPatch(typeof(NDailyRunScreen), "OnEmbarkPressed")]
internal static class NDailyRunEmbarkGuardPatch
{
    [HarmonyPrefix]
    private static void Prefix(NDailyRunScreen __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        NDailyRunScreenLocalPlayersOpenPatch.TryReconcileLocalPlayers(__instance, forceCharacterAssignment: true);
        LocalSelfCoopContext.EnsureLobbySenderContext("daily-embark");
    }
}

[HarmonyPatch(typeof(NDailyRunScreen), nameof(NDailyRunScreen.OnSubmenuOpened))]
internal static class NDailyRunLocalCountButtonsOpenPatch
{
    [HarmonyPostfix]
    private static void Postfix(NDailyRunScreen __instance)
    {
        LocalDailyRunCountButtons.Sync(__instance);
    }
}

[HarmonyPatch(typeof(NDailyRunScreen), nameof(NDailyRunScreen._Process))]
internal static class NDailyRunLocalCountButtonsProcessPatch
{
    [HarmonyPostfix]
    private static void Postfix(NDailyRunScreen __instance)
    {
        LocalDailyRunCountButtons.Sync(__instance);
    }
}

[HarmonyPatch(typeof(NDailyRunScreen), nameof(NDailyRunScreen.OnSubmenuClosed))]
internal static class NDailyRunLocalCountButtonsClosePatch
{
    [HarmonyPrefix]
    private static void Prefix(NDailyRunScreen __instance)
    {
        LocalDailyRunCountButtons.Remove(__instance);
    }
}

/// <summary>
/// 每日页的人数面板（R2：骨架搬进 <see cref="LocalPlayerCountPanel"/>）。
/// 差异 = 人数 clamp 到 4、落点在屏幕左侧中部（右侧是排行榜、底部是出征/返回键）、首次注入打一条布局日志。
/// </summary>
internal static class LocalDailyRunCountButtons
{
    private static readonly FieldInfo? LobbyField = AccessTools.Field(typeof(NDailyRunScreen), "_lobby");

    private static readonly LocalPlayerCountPanel Panel = new(new LocalPlayerCountPanelOptions(
        namePrefix: "LocalDailyRun",
        sourcePrefix: "daily-ui-button",
        successLogText: "通过每日挑战实体按钮调整本地人数成功",
        resolvePosition: ResolveLeftMiddle)
    {
        ClampTargetCount = static count => Math.Min(count, DailyLobbyPolicy.MaxDailyLocalPlayerCount),
        FirstLayoutLog = LogFirstLayout
    });

    public static void Sync(NDailyRunScreen screen)
    {
        // 只在「本地多控开启 + 本页大厅确实是我们自己的回环服务」时才挂面板（r159）：
        // 官方联机每日页是同一个 NDailyRunScreen 类型，残留会话会让它也冒出我们的人数面板。
        bool shouldShow = LocalSelfCoopContext.IsEnabled
            && LobbyField?.GetValue(screen) is StartRunLobby lobby
            && lobby.NetService is LocalLoopbackHostGameService;
        Panel.Sync(screen, shouldShow);
    }

    public static void Remove(NDailyRunScreen screen)
    {
        Panel.Remove(screen);
    }

    private static Vector2? ResolveLeftMiddle(Vector2 viewportSize, Vector2 panelSize, Node screen)
    {
        // 每日页左侧中部：避开右侧排行榜 / 底部出征·返回键 / 顶部标题与倒计时
        return new Vector2(
            LocalPlayerCountPanel.EdgeMargin,
            Math.Max(LocalPlayerCountPanel.EdgeMargin, (viewportSize.Y - panelSize.Y) * 0.5f));
    }

    private static void LogFirstLayout(Vector2 viewportSize, Vector2 position, Vector2 panelSize)
    {
        LocalMultiControlLogger.Info(
            $"每日挑战人数面板已注入: viewport={viewportSize.X:0}x{viewportSize.Y:0}, position={position.X:0},{position.Y:0}, "
            + $"panel={panelSize.X:0}x{panelSize.Y:0}");
    }
}
