using System;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 0.113 两个新模式（Dread 恐惧 / Blitz 闪电战）的「本地多角色」入口公共骨架（r351）。
///
/// 与每日挑战 / 自定义模式的入口同构：自建 <see cref="LocalLoopbackHostGameService"/> →
/// 用**官方子屏**自己建厅（`InitializeMultiplayerAsHost`）→ <c>stack.Push</c>。
/// 模式由官方子屏构造 `StartRunLobby` 时写死（`GameMode.Dread` / `GameMode.Blitz`），我们不传模式。
///
/// ⚠ 官方入口（联机菜单上的 `%DreadButton` / `%BlitzButton`）**保持原版**：本类只服务于我们自注入的
/// 两个小按钮，**不碰** `NMultiplayerHostSubmenu.StartHost`（规则见 references/official-entry-coexistence.md）。
/// </summary>
internal static class LocalNewModeSelfCoopEntry
{
    /// <summary>
    /// 走本地回环开局。返回页面实例（失败返回 null）。
    /// </summary>
    /// <param name="submenu">联机菜单子屏（取 `_stack` 用）。</param>
    /// <param name="entryLog">入口锚点日志（每模式一条，`log_scan.py` 依赖逐字文案）。</param>
    /// <param name="saveReason">清多人存档时的原因串。</param>
    /// <param name="hint">进页后的全屏提示文案。</param>
    /// <param name="initialize">调用官方子屏的 `InitializeMultiplayerAsHost`（人数上限由调用方定）。</param>
    internal static TScreen? Enter<TScreen>(
        NMultiplayerHostSubmenu submenu,
        string entryLog,
        string saveReason,
        string hint,
        Action<TScreen, LocalLoopbackHostGameService> initialize)
        where TScreen : NSubmenu
    {
        NSubmenuStack? stack = AccessTools.Field(typeof(NSubmenu), "_stack").GetValue(submenu) as NSubmenuStack;
        if (stack == null)
        {
            LocalMultiControlLogger.Warn($"无法进入本地多角色({saveReason})：未找到 NSubmenuStack。");
            return null;
        }

        LocalMultiControlLogger.Info(entryLog);
        LocalSelfCoopSaveTag.ClearCurrentProfile();
        // 待查-2 口径：先判存在再删（游戏原 API 对不存在的文件会打 `Error deleting path`）。
        MultiplayerRunSaveCleaner.DeleteIfPresent(saveReason);

        ulong primaryPlayerId = LocalSelfCoopContext.ResolvePrimaryPlayerId();
        LocalSelfCoopContext.UseSavedWakuuPlayerIds(Array.Empty<ulong>());
        LocalSelfCoopSaveTag.MarkCurrentProfile(
            LocalSelfCoopContext.LocalPlayerIds.Take(LocalSelfCoopContext.DesiredLocalPlayerCount).ToList());

        LocalLoopbackHostGameService netService = new(primaryPlayerId);
        LocalSelfCoopContext.Enable(netService);

        TScreen screen = stack.GetSubmenuType<TScreen>();
        initialize(screen, netService);
        stack.Push(screen);
        // 会话守卫（r161 口径）：页面还开着（且可见）就算"在流程中"，不按大厅判据提前清会话。
        LocalSelfCoopContext.ActiveSelfCoopLobbyScreen = screen;
        NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create(hint));
        return screen;
    }
}

/// <summary>联机菜单小按钮「本地·恐惧」调用的入口。</summary>
internal static class LocalDreadSelfCoopEntry
{
    internal static void Enter(NMultiplayerHostSubmenu submenu)
    {
        _ = LocalNewModeSelfCoopEntry.Enter<MegaCrit.Sts2.Core.Nodes.Screens.Dread.NDreadScreen>(
            submenu,
            entryLog: "单人多角色恐惧模式入口：改走本地回环开局。",
            saveReason: "进入恐惧模式本地多控",
            hint: LocalModText.EnteredDreadSelfCoopHint,
            initialize: static (screen, netService) =>
                screen.InitializeMultiplayerAsHost(netService, LocalSelfCoopContext.MaxLocalPlayerCount));
    }
}

/// <summary>联机菜单小按钮「本地·闪电战」调用的入口。</summary>
internal static class LocalBlitzSelfCoopEntry
{
    internal static void Enter(NMultiplayerHostSubmenu submenu)
    {
        _ = LocalNewModeSelfCoopEntry.Enter<MegaCrit.Sts2.Core.Nodes.Screens.Blitz.NBlitzScreen>(
            submenu,
            entryLog: "单人多角色闪电战入口：改走本地回环开局。",
            saveReason: "进入闪电战本地多控",
            hint: LocalModText.EnteredBlitzSelfCoopHint,
            initialize: static (screen, netService) =>
                screen.InitializeMultiplayerAsHost(netService, LocalSelfCoopContext.MaxLocalPlayerCount));
    }
}
