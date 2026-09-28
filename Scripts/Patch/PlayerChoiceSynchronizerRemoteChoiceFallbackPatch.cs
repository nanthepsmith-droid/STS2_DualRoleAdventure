using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 「远端选择」防软锁兜底（BUG-23，2026-09-28）：
/// 归属者是我们自己的本地席位（且是**后台托管的瓦库**）时，宁可按"空结果"放行，也不许原地死等。
///
/// 背景（实测证据见 `TODO.md` §BUG-23）：第三方 mod 会**自己实现选牌**，形状通常是
/// <code>
/// uint choiceId = PlayerChoiceSynchronizer.ReserveChoiceId(player);
/// if (LocalContext.IsMe(player) &amp;&amp; NetService.Type != Replay) { 弹它自己的 NChooseACardSelectionScreen … }
/// else { await PlayerChoiceSynchronizer.WaitForRemoteChoice(player, choiceId) … }
/// </code>
/// 它**既不读 `CardSelectCmd.Selector`、也不走 `CardSelectCmd.From*`** ⇒ 我们的三件套
/// （`From*` 的 Selector 短路 / 作用域外作答补丁 / 选择器按归属者分发）全都够不着。
/// 并发出牌档又**刻意不钉全局上下文**（方案 D，见 `LocalWakuuRelicRuntime`）⇒ `LocalContext.IsMe(瓦库席位)`
/// 为 false ⇒ 走 else 远端分支 ⇒ **单进程回环里没有任何人会回答** ⇒ 卡牌停屏、行动队列永久
/// `waiting for player choice`（沙耶 mod 色素细胞实测：choiceId=6，卡到退出）。
///
/// ⚠ **为什么必须按"结果类型"给空值**：`PlayerChoiceResult` 是类型化的
/// （`AsIndex()` / `AsIndexes()` / `AsCombatCards()` / `AsDeckCards()` / `AsPlayerId()` 对错的
/// `ChoiceType` 一律抛 `InvalidOperationException`），而 `WaitForRemoteChoice(player, choiceId)` 这层
/// **看不到调用方期望哪种类型** ⇒ 盲回一种类型只是把"卡死"换成"异常"。
/// 所以这里用**调用栈推断**调用方，再按它需要的类型给空值；推不出来时按 `Index` 兜底
/// （第三方自绘选牌实测都是 indexes），并把调用方名字打进日志 —— 万一某个调用方需要别的类型，
/// 日志里会立刻看出是谁（届时在 <see cref="TryInferEmptyResult"/> 的映射表里补一行）。
///
/// 行为边界（刻意收窄，越界一律不动）：
/// <list type="bullet">
/// <item>只在本地多控 + 单人冒险 + **本地回环**（我们自己的会话）里生效；</item>
/// <item>只对我们的**本地席位**生效（第三方合成 Bot / 真联机玩家 ⇒ 原样等待）；</item>
/// <item>只对**后台托管的瓦库形态**席位生效（真人席位不动 —— 真人该走的是本地 UI 分支）；</item>
/// <item>火堆"选一个队友"类等待交给既有补丁精确作答（`IsAwaitingOptionExecution`），这里让路。</item>
/// </list>
///
/// 代价（用户 2026-09-28 拍板接受）：该次选择被**跳过**（例：色素细胞 = 不拿那张牌），
/// 功能完整版（我们驱动它的界面、按瓦库策略作答）单独一批做，见 `TODO.md` §BUG-23 方案 B。
/// </summary>
[HarmonyPatch(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.WaitForRemoteChoice))]
internal static class PlayerChoiceSynchronizerRemoteChoiceFallbackPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Player player, uint choiceId, ref Task<PlayerChoiceResult> __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return true;
        }

        if (player == null || !LocalSeatSource.IsLocalSeat(player.NetId))
        {
            return true;
        }

        // 只兜底「后台托管的瓦库」：真人席位要走的是本地 UI 分支，不能替他做决定。
        if (!LocalWakuuRelicRuntime.IsVakuuFormMode(player))
        {
            return true;
        }

        // 火堆"选一个队友"类等待有既有补丁精确作答（它以"另一个存活玩家"补全结果），让路。
        if (LocalWakuuRestAutoChoice.IsAwaitingOptionExecution)
        {
            return true;
        }

        PlayerChoiceResult empty = BuildEmptyResult(out string kind, out string caller);
        __result = Task.FromResult(empty);
        LocalMultiControlLogger.Warn(
            $"瓦库远端选择无人作答，已按空结果放行（防软锁兜底，本次选择被跳过）: "
            + $"player={player.NetId}, choiceId={choiceId}, 结果类型={kind}, 调用方={caller} —— "
            + "若这是第三方 mod 自绘的选牌，属已知限制（TODO.md §BUG-23）；"
            + "若调用方是本 mod 适配过的入口，请把本行报给维护者（可能需要补映射表）。");
        return false;
    }

    /// <summary>
    /// 按调用栈推断调用方需要的结果类型，给出对应的"空结果"
    /// （分类与映射表在纯逻辑 <see cref="PlayerChoiceCallerClassifier"/>，有单测钉住）。
    /// </summary>
    private static PlayerChoiceResult BuildEmptyResult(out string kind, out string caller)
    {
        (string callerType, string callerMethod) = ResolveCaller();
        caller = $"{callerType}.{callerMethod}";

        switch (PlayerChoiceCallerClassifier.Classify(callerType, callerMethod))
        {
            case PlayerChoiceEmptyResultKind.CombatCard:
                kind = "combat-card";
                return PlayerChoiceResult.FromMutableCombatCards(Array.Empty<CardModel>());

            case PlayerChoiceEmptyResultKind.DeckCard:
                kind = "deck-card";
                return PlayerChoiceResult.FromMutableDeckCards(Array.Empty<CardModel>());

            case PlayerChoiceEmptyResultKind.Player:
                kind = "player";
                return PlayerChoiceResult.FromPlayerId(null);

            default:
                // `AsIndex()` 对空列表给 -1、`AsIndexes()` 给空列表 ⇒ index 型调用方都能安全继续。
                kind = "index";
                return PlayerChoiceResult.FromIndex(null);
        }
    }

    /// <summary>
    /// 取第一个"真实业务调用方"帧（跳过选择同步器与本补丁自己），并把编译器生成的状态机名还原成
    /// 真实方法名与声明类型 —— 异步方法在栈上只会露出 `&lt;FromSimpleGrid&gt;d__42` + `MoveNext`。
    /// 还原规则是纯逻辑（<see cref="PlayerChoiceCallerClassifier.TryNormalizeFrame"/>，有单测）。
    /// </summary>
    private static (string Type, string Method) ResolveCaller()
    {
        try
        {
            StackTrace trace = new(fNeedFileInfo: false);
            for (int i = 0; i < trace.FrameCount; i++)
            {
                System.Reflection.MethodBase? method = trace.GetFrame(i)?.GetMethod();
                Type? declaringType = method?.DeclaringType;
                if (method == null || declaringType == null)
                {
                    continue;
                }

                if (!PlayerChoiceCallerClassifier.TryNormalizeFrame(
                        declaringType.Name,
                        declaringType.DeclaringType?.Name,
                        method.Name,
                        out string callerType,
                        out string callerMethod))
                {
                    continue;
                }

                if (PlayerChoiceCallerClassifier.IsInternalFrame(callerType, callerMethod))
                {
                    continue;
                }

                return (callerType, callerMethod);
            }
        }
        catch
        {
            // 栈回溯失败不影响兜底本身：按未知处理（走 Index 默认）
        }

        return (PlayerChoiceCallerClassifier.UnknownType, PlayerChoiceCallerClassifier.UnknownMethod);
    }
}
