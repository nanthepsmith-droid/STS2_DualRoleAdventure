using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 让**第三方代码**在"我们正在为某一席自动化"的窗口内，把这一席当作本机玩家（BUG-23 方案 B 第一步）。
///
/// 为什么需要它：单进程本地多控里**每个席位都在同一台机器上**，但游戏侧只有"一个本机玩家"
/// （`LocalContext.NetId`）。第三方自绘选牌的典型写法是
/// <code>
/// if (LocalContext.IsMe(player) &amp;&amp; NetService.Type != Replay) { 弹它自己的选牌界面 }
/// else { await PlayerChoiceSynchronizer.WaitForRemoteChoice(player, choiceId) }   // ★ 无人作答
/// </code>
/// 而并发出牌档刻意不钉全局上下文（方案 D）⇒ 它走 else 死等（见 `TODO.md` §BUG-23 的实证链）。
/// 这里把它的第一分支"打开"，让界面弹出来，再由
/// <see cref="NChooseACardSelectionShowScreenAutoAnswerPatch"/> 按瓦库策略作答。
///
/// **刻意收窄到三条**（判据纯逻辑见 <see cref="WakuuSelfDrawnChoicePolicy"/>，有单测）：
/// <list type="number">
/// <item>只改**第三方**调用方的结果 —— 调用栈上 `LocalContext` 之外的第一个真实帧若属于
///   游戏程序集或本 mod，直接返回原值 ⇒ **原版语义一点不动**（r109 那类"把别人的牌当我自己的牌
///   做前台视觉"的风险面因此被限制在第三方代码里）；</item>
/// <item>只在「后台托管瓦库席位 **且** 此刻正被我们的自动化驱动」（该席位登记着托管选择器）时放行
///   ⇒ 真人亲自操作该席位、或该席位没在自动化时，一律不动；</item>
/// <item>只在原本判 false 时改口（真值不动）。</item>
/// </list>
///
/// ⚠ 副作用说明：窗口内第三方可能为该席位渲染它自己的"本地玩家"表现（HUD/特效/音效）；
/// 若第三方弹的是**我们驱动不了的**界面类，界面会留在屏幕上等真人点（比"无界面死等"仍要好，
/// 且会由方案 A 的兜底日志点名）。这两点都记在 `TODO.md` §BUG-23。
/// </summary>
[HarmonyPatch(typeof(LocalContext), nameof(LocalContext.IsMe), new[] { typeof(Player) })]
internal static class LocalContextThirdPartyIsMePatch
{
    /// <summary>已打过日志的（调用方, 席位）组合；只用于防刷屏，不参与判定。</summary>
    private static readonly HashSet<string> LoggedCallers = new();

    private const int MaxLoggedCallers = 64;

    /// <summary>
    /// 放行口子的**调用方黑名单**（BUG-26，r185）：这些第三方调用方的 `IsMe=true` 本地分支
    /// **不是**弹选牌界面，而是需要前台/真人配合的转场或对话序列 —— 放行只会让它在后台席位上挂死。
    /// 实证（2026-09-28）：TouhouAncients 梦境事件选「离开梦境」后，
    /// `LeaveDreamReentry.OnChosen` 被放行走进 `await LeaveDreamSequence.Play(...)`（对话序列，
    /// 后台席位永远等不到推进）⇒ `option.Chosen()` 挂死 ⇒ 事件自动选择卡死、选择器残留、
    /// 8 秒后被安全网切人工（用户观感 = "瓦库在事件里不会自己选了"）。
    /// 黑名单 = 维持原判 false ⇒ 走原版联机的远端分支（事件转场由房间重建链路完成，方案 A 兜底兜住
    /// 无人作答的等待），这正是 r183 之前一直正常的行为。
    /// 命中黑名单时打一条去重 INFO（键加 deny# 前缀与放行日志分开），便于后续把新调用方补进名单。
    /// </summary>
    private static readonly string[] DenylistedCallerPrefixes =
    [
        "TouhouAncients.Scripts.LeaveDreamReentry",
    ];

    [HarmonyPostfix]
    private static void Postfix(Player? player, ref bool __result)
    {
        if (__result || player == null)
        {
            return;
        }

        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (!LocalSeatSource.IsLocalSeat(player.NetId))
        {
            return;
        }

        bool automatedSeatInPlay = WakuuSelfDrawnChoicePolicy.IsAutomatedSeatInPlay(
            enabled: true,
            singleAdventureMode: true,
            loopbackSession: RunManager.Instance?.NetService is LocalLoopbackHostGameService,
            isLocalSeat: true,
            backgroundMode: LocalWakuuAutopilotConfig.BackgroundMode,
            vakuuFormMode: LocalWakuuRelicRuntime.IsVakuuFormMode(player),
            hasManagedSelector: WakuuSelectorRegistry.TryGet(player.NetId, out _));
        if (!automatedSeatInPlay)
        {
            return;
        }

        if (!TryResolveThirdPartyCaller(out string caller))
        {
            return;
        }

        bool denylisted = IsDenylistedCaller(caller);
        if (!WakuuSelfDrawnChoicePolicy.ShouldWidenIsMe(
                originalIsMe: false,
                automatedSeatInPlay: true,
                callerIsThirdParty: true,
                callerDenylisted: denylisted))
        {
            if (denylisted)
            {
                LogDenylisted(caller, player.NetId);
            }

            return;
        }

        __result = true;
        LogOnce(caller, player.NetId);
    }

    /// <summary>
    /// 调用栈上 `LocalContext` 之外的**第一个真实帧**是不是第三方：是 ⇒ 返回 true 并给出调用方名字；
    /// 是游戏程序集 / 本 mod ⇒ 返回 false（保持原版语义）。
    /// 跳过 `LocalContext` 自身是为了覆盖 `IsMe(Creature)` / `IsMine(card)` / `ContainsMe(...)` 这些包装方法
    /// （它们内部调本方法，真实调用方在更外层）。
    /// </summary>
    private static bool TryResolveThirdPartyCaller(out string caller)
    {
        caller = "unknown";
        try
        {
            StackTrace trace = new(fNeedFileInfo: false);
            Assembly gameAssembly = typeof(LocalContext).Assembly;
            Assembly ourAssembly = typeof(LocalContextThirdPartyIsMePatch).Assembly;

            for (int i = 0; i < trace.FrameCount; i++)
            {
                Type? declaringType = trace.GetFrame(i)?.GetMethod()?.DeclaringType;
                if (declaringType == null)
                {
                    continue;
                }

                if (declaringType == typeof(LocalContext) || declaringType == typeof(LocalContextThirdPartyIsMePatch))
                {
                    continue;
                }

                Assembly assembly = declaringType.Assembly;
                if (assembly == gameAssembly || assembly == ourAssembly)
                {
                    return false;
                }

                caller = declaringType.FullName ?? declaringType.Name;
                return true;
            }
        }
        catch
        {
            // 栈回溯失败 = 无法确认是第三方 ⇒ 保守不放行
        }

        return false;
    }

    private static bool IsDenylistedCaller(string caller)
    {
        foreach (string prefix in DenylistedCallerPrefixes)
        {
            if (caller.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>黑名单命中的去重日志（键与放行日志分开，防刷屏口径一致）。</summary>
    private static void LogDenylisted(string caller, ulong playerId)
    {
        string key = $"deny#{caller}#{playerId}";
        lock (LoggedCallers)
        {
            if (LoggedCallers.Count >= MaxLoggedCallers || !LoggedCallers.Add(key))
            {
                return;
            }
        }

        LocalMultiControlLogger.Info(
            $"第三方询问本地玩家身份，调用方在放行黑名单，保持原判（转场/对话类调用方不能替后台席位放行）: "
            + $"caller={caller}, player={playerId}");
    }

    private static void LogOnce(string caller, ulong playerId)
    {
        string key = $"{caller}#{playerId}";
        lock (LoggedCallers)
        {
            if (LoggedCallers.Count >= MaxLoggedCallers || !LoggedCallers.Add(key))
            {
                return;
            }
        }

        LocalMultiControlLogger.Info(
            $"第三方询问本地玩家身份，已按「同机席位」放行: caller={caller}, player={playerId}");
    }
}
