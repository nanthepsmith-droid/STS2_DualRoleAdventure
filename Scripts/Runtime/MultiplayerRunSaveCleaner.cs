using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「进本地多控流程前清掉旧多人 run 存档」的**静默版**（TODO 待查-2，2026-10-24）。
///
/// <para><b>为什么要包一层</b>：游戏自己的 `SaveManager.DeleteCurrentMultiplayerRun()` 是
/// **无条件删两个文件**（`current_run_mp.save` + `.backup`）；底层
/// `CloudSaveStore.DeleteFile → GodotFileIo.DeleteFile` 在目标**不存在**时只打一条
/// `[ERROR] Error deleting path …: Failed` 就返回。我们三个入口（单人多角色 / 每日 / 自定义）
/// 每次进入都调它 ⇒ 只要这一局之前没留下待删存档，就会刷 2~4 条 ERROR，
/// 抬高全局 ERROR 计数、污染「异常族期望 0」的判读（r239/r240/r247 都见过）。</para>
///
/// <para><b>语义不变</b>：只是**先判存在再删**。删不掉（句柄占用等真实失败）仍由游戏自己报错。
/// 反射面（`RunSaveManager.CurrentMultiplayerRunSavePath` / `_saveStore`）任何一步拿不到，
/// 就**退回游戏原 API** —— 宁可多一条日志，也不能漏清存档。</para>
/// </summary>
internal static class MultiplayerRunSaveCleaner
{
    /// <param name="reason">调用场景（只进日志，便于实机判读是哪条入口）。</param>
    internal static void DeleteIfPresent(string reason)
    {
        SaveManager? saveManager = SaveManager.Instance;
        if (saveManager == null)
        {
            return;
        }

        try
        {
            if (!saveManager.HasMultiplayerRunSave)
            {
                LocalMultiControlLogger.Info($"历史多人存档本来就不存在，跳过清理（{reason}）");
                return;
            }

            RunSaveManager? runSaveManager =
                AccessTools.Field(typeof(SaveManager), "_runSaveManager")?.GetValue(saveManager) as RunSaveManager;
            string? path = runSaveManager == null
                ? null
                : AccessTools.PropertyGetter(typeof(RunSaveManager), "CurrentMultiplayerRunSavePath")
                    ?.Invoke(runSaveManager, null) as string;
            ISaveStore? store = runSaveManager == null
                ? null
                : AccessTools.Field(typeof(RunSaveManager), "_saveStore")?.GetValue(runSaveManager) as ISaveStore;

            if (string.IsNullOrEmpty(path) || store == null)
            {
                LocalMultiControlLogger.Info($"历史多人存档清理：反射面不可用，退回游戏原 API（{reason}）");
                saveManager.DeleteCurrentMultiplayerRun();
                return;
            }

            int deleted = 0;
            int skipped = 0;
            foreach (string candidate in new[] { path, path + ".backup" })
            {
                if (!store.FileExists(candidate))
                {
                    skipped++;
                    continue;
                }

                store.DeleteFile(candidate);
                deleted++;
            }

            LocalMultiControlLogger.Info(
                $"历史多人存档已清理（{reason}）: 删除 {deleted} 个、跳过不存在 {skipped} 个");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"历史多人存档清理走了兜底路径（{reason}）: {exception.GetType().Name}: {exception.Message}");
            saveManager.DeleteCurrentMultiplayerRun();
        }
    }
}
