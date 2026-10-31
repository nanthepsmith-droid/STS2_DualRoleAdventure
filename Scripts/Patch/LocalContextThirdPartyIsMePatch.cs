using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.Runtime.PureLogic;
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

    /// <summary>r329：调用点 → 判定结论（键 = 首个真实帧的 `MethodHandle`）。见 <see cref="TryResolveThirdPartyCaller"/>。</summary>
    private static readonly Dictionary<IntPtr, CallerVerdict> CallerVerdicts = new();

    private static readonly object CallerVerdictsLock = new();

    /// <summary>一个调用点的判定结论（是否第三方 + 调用方名字）。</summary>
    private readonly struct CallerVerdict
    {
        internal CallerVerdict(bool isThirdParty, string caller)
        {
            IsThirdParty = isThirdParty;
            Caller = caller;
        }

        internal bool IsThirdParty { get; }

        internal string Caller { get; }
    }

    private const int MaxLoggedCallers = 64;

    /// <summary>
    /// r329：回溯**最多看几帧再下结论**。原逻辑"见到第一个既不是 `LocalContext` 也不是本补丁的帧就下结论"
    /// ⇒ 更深的帧本来就不会被用到（真实调用方最多隔 1~2 层包装方法）。这一条只限制**扫描**，不限制捕获成本。
    /// </summary>
    private const int MaxFramesToInspect = 6;

    private const int MaxCallerVerdicts = 512;

    /// <summary>
    /// r329：栈回溯的**每帧时间预算（毫秒）** —— 这条才是真正封顶的那个。
    ///
    /// 为什么必须有预算：.NET 9/10 的 `StackTrace` **没有**"只捕获最近 N 帧"的公开构造（本机反射实测：
    /// 只有 `()/(bool)/(int)/(int,bool)/(Exception…)/(IEnumerable&lt;StackFrame&gt;)`，没有 `frameCount` 重载）
    /// ⇒ 每次回溯都会把整条栈（本作里是几十帧的 async + Harmony 链）抓完，**单次成本改不动**。
    /// 唯一能让它不再吃满主线程的办法就是限制每帧最多花多少毫秒。
    ///
    /// 超预算时的行为 = **维持原判**（不放行），这是保守方向：
    ///   * 第三方"要不要弹选牌界面"的查询发生在选牌/事件这类**出牌循环在 await** 的时刻，预算应当是满的；
    ///   * 被 Saya 的 `GeneralizedResistancePower` / Koishi 的 `SealAllRelicPatch` 这类**每帧功率/遗物查询**
    ///     刷爆预算时丢掉放行，得到的正是 r109 之前一直正常的原版语义（第三方按"远端玩家"处理）。
    /// </summary>
    private const long WalkBudgetMsPerFrame = 2;

    private static ulong _budgetFrame = ulong.MaxValue;

    private static long _walkMsThisFrame;

    /// <summary>
    /// 放行口子的**调用方黑名单**（BUG-26，r185）：这些第三方调用方的 `IsMe=true` 本地分支
    /// **不是**弹选牌界面，而是需要单一本地语义（或前台/真人配合）的流程 —— 放行会把流程带错。
    ///
    /// 两类实证：
    /// <list type="number">
    /// <item><b>转场/对话类</b>（BUG-26，2026-09-28）：TouhouAncients 梦境事件选「离开梦境」后，
    ///   `LeaveDreamReentry.OnChosen` 被放行走进 `await LeaveDreamSequence.Play(...)`（对话序列，
    ///   后台席位永远等不到推进）⇒ `option.Chosen()` 挂死 ⇒ 事件自动选择卡死、选择器残留、
    ///   8 秒后被安全网切人工（用户观感 = "瓦库在事件里不会自己选了"）；</item>
    /// <item><b>布局类</b>（r217，2026-10-06）：NinjaSlayer 的 <c>YamotoKokiAllyLayoutPatch</c>
    ///   给自己的同伴（YamotoKoki 等召唤物）重排队友站位 —— 它先按 `LocalContext.IsMe` 给
    ///   `NCombatRoom.PositionPlayersAndPets` 的入参排序，再让原版方法落位；而原版落位是
    ///   <c>foreach (node) if (IsMe(node)) list.Insert(0, node)</c> ⇒ **两个 `IsMe=true` 的节点会被
    ///   `Insert(0)` 反转相对次序**。放行后"瓦库"也成了 me ⇒ 每次第三方重排都会把两人左右对调
    ///   （实机证据：`caller=NinjaSlayer.Code.Patches.YamotoKokiAllyLayoutPatch, player=<瓦库>` 紧接
    ///   探针的 `节点索引 1 -> 0 / 0 -> 1` 对调；见 `references/local-multicontrol-pitfalls.md` 坑 X）。
    ///   黑名单 = 维持原判 ⇒ 全场只有一个 me ⇒ 排序确定，重排不再对调。</item>
    /// </list>
    ///
    /// 黑名单 = 维持原判 false（走原版联机的远端/单一本地语义分支），这正是放行口子出现之前一直正常的行为。
    /// 命中黑名单时打一条去重 INFO（键加 deny# 前缀与放行日志分开），便于后续把新调用方补进名单。
    /// ⚠ 新增条目时请同时看它在**排序/`Insert(0)`/布局**里怎么用 `IsMe` 的结果 ——
    /// "两个 me" 在那种代码里不只是语义问题，是会**改变顺序**的。
    /// </summary>
    private static readonly string[] DenylistedCallerPrefixes =
    [
        "TouhouAncients.Scripts.LeaveDreamReentry",
        "NinjaSlayer.Code.Patches.YamotoKokiAllyLayoutPatch",
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

        // 战灵召唤 Phase 0 POC：临时席位（战神）虽然是"本地席位"（要享受自动就绪），
        // 但**绝不能**被第三方当成"我"放行（它不是任何人操作的席位）⇒ 显式排除。
        if (HellwarPocPolicy.IsTemporarySeat(player.NetId))
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

        // r329：这条栈回溯曾经吃掉主线程 63.9%（见 TryResolveThirdPartyCaller 注释）⇒ 加进逐帧探针，
        // 以后每 5s 的窗口行里直接能看 `第三方IsMe放行=…ms(调用 N)`，不用再采样一次才知道有没有复发。
        long resolveStart = LocalPerfProbe.Begin();
        bool thirdPartyCaller = TryResolveThirdPartyCaller(out string caller);
        LocalPerfProbe.End(LocalPerfProbe.Slot.ThirdPartyIsMe, resolveStart);

        if (!thirdPartyCaller)
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
    ///
    /// ⚠ r329（2026-10-30）性能修复，**这条是热路径**：
    /// `dotnet-trace` 采样显示主线程 63.9% 自耗时在 `StackTrace.CaptureStackTrace`，调用方就是本方法 ——
    /// figure_Saya 的 `GeneralizedResistancePower.IsCountedDebuff`、Koishi 的 `SealAllRelicPatch.Filter`
    /// 会在每次 `IsMe` 查询时走到这里（12 席 + 一堆功率/遗物时每秒上万次）⇒ 全栈回溯被放大成帧时间主项。
    /// 两条修法：① 回溯**有界**（只取最近 <see cref="MaxFramesToInspect"/> 帧，语义不变，见该常量）；
    /// ② 结论按**方法句柄**记忆化（同一个调用点第二次起只花"有界回溯 + 字典命中"）。
    /// </summary>
    private static bool TryResolveThirdPartyCaller(out string caller)
    {
        caller = "unknown";

        // ① 每帧预算闸：超了就不回溯（维持原判），见 WalkBudgetMsPerFrame。
        ulong frame = Engine.GetProcessFrames();
        if (frame != _budgetFrame)
        {
            _budgetFrame = frame;
            _walkMsThisFrame = 0L;
        }

        if (_walkMsThisFrame >= WalkBudgetMsPerFrame)
        {
            return false;
        }

        long walkStart = Stopwatch.GetTimestamp();
        try
        {
            StackTrace trace = new(fNeedFileInfo: false);
            Assembly gameAssembly = typeof(LocalContext).Assembly;
            Assembly ourAssembly = typeof(LocalContextThirdPartyIsMePatch).Assembly;

            int inspected = 0;
            for (int i = 0; i < trace.FrameCount && inspected < MaxFramesToInspect; i++)
            {
                MethodBase? method = trace.GetFrame(i)?.GetMethod();
                Type? declaringType = method?.DeclaringType;
                if (declaringType == null)
                {
                    continue;
                }

                if (declaringType == typeof(LocalContext) || declaringType == typeof(LocalContextThirdPartyIsMePatch))
                {
                    continue;
                }

                inspected++;

                // 同一个调用点（方法句柄）第二次起不必再比对程序集 / 取类型名。
                IntPtr key = SafeMethodHandle(method);
                if (key != IntPtr.Zero && TryGetCachedVerdict(key, out bool cachedThirdParty, out string cachedCaller))
                {
                    caller = cachedCaller;
                    return cachedThirdParty;
                }

                Assembly assembly = declaringType.Assembly;
                bool thirdParty = assembly != gameAssembly && assembly != ourAssembly;
                string resolved = declaringType.FullName ?? declaringType.Name;
                if (key != IntPtr.Zero)
                {
                    CacheVerdict(key, thirdParty, resolved);
                }

                caller = resolved;
                return thirdParty;
            }
        }
        catch
        {
            // 栈回溯失败 = 无法确认是第三方 ⇒ 保守不放行
        }
        finally
        {
            _walkMsThisFrame += (Stopwatch.GetTimestamp() - walkStart) * 1000L / Stopwatch.Frequency;
        }

        return false;
    }

    /// <summary>动态方法/泛型实例在某些运行时取句柄会抛 ⇒ 取不到就不做记忆化（只影响速度，不影响判定）。</summary>
    private static IntPtr SafeMethodHandle(MethodBase? method)
    {
        if (method == null)
        {
            return IntPtr.Zero;
        }

        try
        {
            return method.MethodHandle.Value;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static bool TryGetCachedVerdict(IntPtr key, out bool thirdParty, out string caller)
    {
        lock (CallerVerdictsLock)
        {
            if (CallerVerdicts.TryGetValue(key, out CallerVerdict verdict))
            {
                thirdParty = verdict.IsThirdParty;
                caller = verdict.Caller;
                return true;
            }
        }

        thirdParty = false;
        caller = "unknown";
        return false;
    }

    private static void CacheVerdict(IntPtr key, bool thirdParty, string caller)
    {
        lock (CallerVerdictsLock)
        {
            if (CallerVerdicts.Count >= MaxCallerVerdicts)
            {
                return; // 满了就不再记（宁可多回溯，也不无限长）
            }

            CallerVerdicts[key] = new CallerVerdict(thirdParty, caller);
        }
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
