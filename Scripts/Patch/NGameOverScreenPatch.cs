using System;
using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Saves;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 结算页「保存徽章到进度」的兜底（2026-09-27，r155，BUG-20）。
///
/// 原版 `NGameOverScreen.SaveBadgesToProgress` 用**索引器**取本地玩家的角色统计：
/// `SaveManager.Instance.Progress.CharacterStats[_localPlayer.Character.Id]`。
/// 只要进度存档里没有该角色的 `CharacterStats` 条目（例如第三方 mod 角色、
/// 或首次以该角色结束整局），就抛 `KeyNotFoundException` →
/// `AnimateBadges` 出错 → `AnimateRunSummary` 在 `_mainMenuButton.Visible/Enable` 之前中断
/// ⇒ **结算第二页没有「返回主菜单」按钮，卡死出不去**（2026-09-27 实机：主玩家角色是
/// wtw 的五条悟 `CHARACTER.WTW_CHARACTER_GOJO_SATORU`，进度里恰无该条目）。
///
/// 根因（为什么不写入）由 <see cref="ProgressSaveManagerUpdateWithRunDataPatch"/> 修，
/// 这里做第二层兜底：进原方法前若发现条目缺失，就用游戏自己的
/// `ProgressState.GetOrCreateCharacterStats` 补建一个，保证原方法索引必中。
/// 与根因修复叠加后：进度恢复正常写入 + 任何遗漏角色都不会再锁死结算页。
/// </summary>
[HarmonyPatch(typeof(NGameOverScreen), "SaveBadgesToProgress")]
internal static class NGameOverScreenSaveBadgesToProgressPatch
{
    private static readonly FieldInfo? LocalPlayerField = AccessTools.Field(typeof(NGameOverScreen), "_localPlayer");

    [HarmonyPrefix]
    private static void Prefix(NGameOverScreen __instance)
    {
        try
        {
            if (LocalPlayerField?.GetValue(__instance) is not Player localPlayer)
            {
                return;
            }

            if (localPlayer.Character?.Id is not ModelId characterId)
            {
                return;
            }

            ProgressState? progress = SaveManager.Instance?.Progress;
            if (progress == null || progress.CharacterStats.ContainsKey(characterId))
            {
                return;
            }

            progress.GetOrCreateCharacterStats(characterId);
            LocalMultiControlLogger.Info(
                $"结算页徽章保存兜底：进度里缺该角色统计，已补建条目（防结算页无按钮卡死）: character={characterId.Entry}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"结算页徽章保存兜底异常(忽略): {exception.Message}");
        }
    }
}
