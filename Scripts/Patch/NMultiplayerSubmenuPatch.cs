using System;
using System.Collections.Generic;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NMultiplayerSubmenu), "StartLoad")]
internal static class NMultiplayerSubmenuPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NMultiplayerSubmenu __instance)
    {
        if (!LocalSelfCoopSaveTag.TryReadCurrentProfile(out List<ulong> playerIds, out List<ulong> wakuuPlayerIds)
            || playerIds.Count < 2)
        {
            return true;
        }

        ulong primaryPlayerId = playerIds[0];
        LocalMultiControlLogger.Info($"检测到本地多控存档标记，尝试继续游戏: {string.Join(",", playerIds)}");

        // r167（BUG-22）：读档全程开「读档窗口」——否则会话守卫会在读档中途
        // `Disable("no-local-lobby-screen")`，把托管遗物发放与归属守卫一起关掉（瓦库停摆 + 奖励归属软锁）。
        LocalSelfCoopContext.OpenLoadReplayWindow("continue-game");
        LocalSelfCoopContext.UseSavedPlayerIds(playerIds);

        // r166 修（BUG-22）：读档必须**同时恢复瓦库席位**。
        // 此前只恢复玩家 id，而瓦库席位在进我们自己的大厅入口时会被显式清空（三个入口都调
        // `UseSavedWakuuPlayerIds(Array.Empty<ulong>())`）⇒ 读档后 `IsWakuuEnabled` 为空
        // ⇒ 托管遗物不补发、`IsVakuuFormMode=false` ⇒ **瓦库整局不出牌、不自动选事件**
        //（2026-09-27 实机：读档后 3 个事件房全部没被自动选，且无任何瓦库出牌作用域）。
        // 顺序必须在 `UseSavedPlayerIds` 之后 —— 恢复时要按本地席位表过滤。
        LocalSelfCoopContext.UseSavedWakuuPlayerIds(wakuuPlayerIds);
        LocalMultiControlLogger.Info($"读档已恢复瓦库席位: {string.Join(",", wakuuPlayerIds)}");

        ReadSaveResult<SerializableRun> readSaveResult = SaveManager.Instance.LoadAndCanonicalizeMultiplayerRunSave(primaryPlayerId);
        if (!readSaveResult.Success || readSaveResult.SaveData == null)
        {
            NSubmenuButton? loadButton = AccessTools.Field(typeof(NMultiplayerSubmenu), "_loadButton")?.GetValue(__instance) as NSubmenuButton;
            loadButton?.Disable();
            NErrorPopup? popup = NErrorPopup.Create(
                new LocString("main_menu_ui", "INVALID_SAVE_POPUP.title"),
                new LocString("main_menu_ui", "INVALID_SAVE_POPUP.description_run"),
                new LocString("main_menu_ui", "INVALID_SAVE_POPUP.dismiss"),
                showReportBugButton: true);

            if (popup != null && NModalContainer.Instance != null)
            {
                NModalContainer.Instance.Add(popup);
                NModalContainer.Instance.ShowBackstop();
            }

            LocalMultiControlLogger.Warn("本地多控存档读取失败，已弹出坏档提示。");
            return false;
        }

        if (!LocalSelfCoopContext.IsSaveOwnedByLocalSelfCoop(readSaveResult.SaveData))
        {
            LocalMultiControlLogger.Warn("检测到存档玩家ID与本地多控标记不一致，回退原生多人读档流程。");
            LocalSelfCoopSaveTag.ClearCurrentProfile();
            return true;
        }

        NSubmenuStack? stack = AccessTools.Field(typeof(NSubmenu), "_stack")?.GetValue(__instance) as NSubmenuStack;
        if (stack == null)
        {
            LocalMultiControlLogger.Warn("未找到子菜单栈，回退原生多人读档流程。");
            return true;
        }

        LocalLoopbackHostGameService netService = new LocalLoopbackHostGameService(primaryPlayerId);
        LocalSelfCoopContext.Enable(netService);
        NMultiplayerLoadGameScreen loadGameScreen = stack.GetSubmenuType<NMultiplayerLoadGameScreen>();
        loadGameScreen.InitializeAsHost(netService, readSaveResult.SaveData);
        stack.Push(loadGameScreen);
        LocalMultiControlLogger.Info("已使用本地回环服务打开多人读档界面。");
        return false;
    }
}
