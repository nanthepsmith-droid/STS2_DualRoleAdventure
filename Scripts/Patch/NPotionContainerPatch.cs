using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NPotionContainer), nameof(NPotionContainer.AnimatePotion))]
internal static class NPotionContainerPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(NPotionContainer), nameof(NPotionContainer.Initialize))]
    private static void PostfixInitialize(NPotionContainer __instance, IRunState runState)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        try
        {
            // R3 B2：药水栏绑到「回环上下文当前对着的那位」，逐字等价于旧写法
            // `LocalContext.NetId ?? PrimaryPlayerId`（不是前台口径）。
            ulong targetPlayerId = LocalSeatSource.ContextSeatId() ?? LocalSelfCoopContext.PrimaryPlayerId;
            if (!TryBindPotionContainerToPlayer(__instance, runState, targetPlayerId))
            {
                LocalMultiControlLogger.Warn($"药水栏初始化失败：未找到目标玩家 {targetPlayerId}。");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"重建药水栏失败: {exception.Message}");
        }
    }

    [HarmonyPrefix]
    private static bool PrefixAnimatePotion(NPotionContainer __instance, PotionModel potion, Godot.Vector2? startPosition = null)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        List<NPotionHolder>? holders = AccessTools.Field(typeof(NPotionContainer), "_holders")?.GetValue(__instance) as List<NPotionHolder>;
        NPotionHolder? holder = holders?.FirstOrDefault((node) => node.Potion != null && node.Potion.Model == potion);
        if (holder?.Potion == null)
        {
            NoteSkippedPotionAnimation(potion.Id.Entry);
            return false;
        }

        TaskHelper.RunSafely(holder.Potion.PlayNewlyAcquiredAnimation(startPosition));
        return false;
    }

    /// <summary>
    /// 「当前视图里没有这瓶药水，跳过进场动画」——**合并成一条汇总日志**（2026-11-10 用户要求降噪）。
    ///
    /// 触发场景很常见且天然成批：瓦库形态在后台一次拿到/用掉多瓶（娇嫩蕨草开战填满 3~5 格、
    /// 遗物驱动腾栏连喝 5 瓶），每瓶都会走到这里 ⇒ 旧实现会连刷 5 条 WARN（实测 r339 一局 5+5 条）。
    ///
    /// 用**抖动合并（debounce）**而不是"首次后固定 400ms"：实测相邻两瓶之间常隔 300~800ms
    /// （每瓶一次命令 await），固定窗口会把一批拆成 3 条（r340：3 瓶 → 3 条 INFO）。
    /// 现在每次新来一瓶都把结算时刻往后推 <see cref="PotionAnimQuietMs"/>，安静下来才打一条。
    /// </summary>
    private static void NoteSkippedPotionAnimation(string entry)
    {
        lock (PotionAnimLogLock)
        {
            PendingPotionAnimSkips.Add(entry);
            PotionAnimFlushAtMs = (long)Godot.Time.GetTicksMsec() + PotionAnimQuietMs;
            if (PotionAnimFlushScheduled)
            {
                return;
            }

            PotionAnimFlushScheduled = true;
        }

        TaskHelper.RunSafely(FlushSkippedPotionAnimationsAsync());
    }

    private static async Task FlushSkippedPotionAnimationsAsync()
    {
        // 轮询等待"安静窗口"：每 200ms 看一次，期间只要又来了新药水就继续等。
        while (true)
        {
            await Task.Delay(200);

            List<string> batch;
            lock (PotionAnimLogLock)
            {
                long now = (long)Godot.Time.GetTicksMsec();
                if (now < PotionAnimFlushAtMs && PendingPotionAnimSkips.Count > 0)
                {
                    continue;
                }

                batch = new List<string>(PendingPotionAnimSkips);
                PendingPotionAnimSkips.Clear();
                PotionAnimFlushScheduled = false;
            }

            if (batch.Count == 0)
            {
                return;
            }

            List<string> distinct = batch.Distinct().ToList();
            string sample = string.Join(",", distinct.Take(6));
            string more = distinct.Count > 6 ? "…" : string.Empty;
            LocalMultiControlLogger.Info(
                $"跳过药水动画（当前视图不存在这些药水，纯表现）: {batch.Count} 瓶 / 去重 {distinct.Count} 种: {sample}{more}");
            return;
        }
    }

    /// <summary>抖动合并的安静窗口：最后一瓶之后安静这么久才结算（覆盖两瓶之间 300~800ms 的间隔）。</summary>
    private const long PotionAnimQuietMs = 1500L;

    private static readonly object PotionAnimLogLock = new();

    private static readonly List<string> PendingPotionAnimSkips = new();

    private static bool PotionAnimFlushScheduled;

    private static long PotionAnimFlushAtMs;

    internal static bool TryBindPotionContainerToPrimaryPlayer(NPotionContainer potionContainer, IRunState runState)
    {
        return TryBindPotionContainerToPlayer(potionContainer, runState, LocalSelfCoopContext.PrimaryPlayerId);
    }

    internal static bool TryBindPotionContainerToPlayer(NPotionContainer potionContainer, IRunState runState, ulong playerId)
    {
        MegaCrit.Sts2.Core.Entities.Players.Player? targetPlayer = runState.GetPlayer(playerId);
        if (targetPlayer == null)
        {
            return false;
        }

        AccessTools.Method(typeof(NPotionContainer), "DisconnectPlayerEvents")?.Invoke(potionContainer, null);
        AccessTools.Field(typeof(NPotionContainer), "_player")?.SetValue(potionContainer, targetPlayer);
        AccessTools.Method(typeof(NPotionContainer), "ConnectPlayerEvents")?.Invoke(potionContainer, null);

        List<NPotionHolder>? holders = AccessTools.Field(typeof(NPotionContainer), "_holders")?.GetValue(potionContainer) as List<NPotionHolder>;
        if (holders == null)
        {
            return false;
        }

        foreach (NPotionHolder holder in holders.ToList())
        {
            holder.GetParent()?.RemoveChild(holder);
            holder.QueueFreeSafely();
        }

        holders.Clear();
        AccessTools.Field(typeof(NPotionContainer), "_focusedHolder")?.SetValue(potionContainer, null);

        AccessTools.Method(typeof(NPotionContainer), "GrowPotionHolders")?.Invoke(potionContainer, new object[] { targetPlayer.MaxPotionCount });
        foreach (PotionModel ownedPotion in targetPlayer.Potions)
        {
            AccessTools.Method(typeof(NPotionContainer), "Add")?.Invoke(potionContainer, new object[] { ownedPotion, true });
        }

        LocalMultiControlLogger.Info($"药水栏已重建到目标玩家: player={targetPlayer.NetId}, slotCount={targetPlayer.MaxPotionCount}");
        return true;
    }
}
