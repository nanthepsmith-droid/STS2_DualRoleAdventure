using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;

namespace HextechRunesLocalCoopFix.Scripts;

/// <summary>
/// 海克斯大乱斗（<c>HextechRunes</c>）「逐席位符文选择」在**本地多控**下的兼容补丁。
///
/// <para><b>病灶</b>：海克斯的选择流程是按原版联机写的，判「这一席是不是本机玩家」用的是
/// <c>HextechRuneSelectionCoordinator.IsLocalPlayer(Player)</c>（等价于
/// <c>LocalContext.IsMe(player) || player.NetId == NetService.NetId</c>）。
/// 一个客户端只有**一个**「本机玩家」，所以本地多控的 N 个席位里只有主席位走本地分支
/// （弹符文界面、等人点），其余席位走**远端等待分支** —— 单进程回环里没有任何远端实例会回消息，
/// 于是 <c>WaitForRemoteHextechChoice</c> 无限重试，批次 <c>Task.WhenAll</c> 永不完成 ⇒
/// 整个符文/行动流程挂起（用户观感：第二个玩家的符文界面切不过去、选不了 ⇒ 软锁）。</para>
///
/// <para><b>做法</b>（两处放宽 + 一处标注，全部只在「本地多控回环会话 + 本地席位」成立时生效）：</para>
/// <list type="number">
/// <item><c>IsLocalPlayer</c> 后缀：把本地席位也判成「本机玩家」⇒ 每个席位都弹自己的符文界面，
///   不再有无人作答的远端等待；</item>
/// <item><c>HextechRuneSelectionScreen.RelicsSelected(bool)</c> 前缀：把 <c>removeOverlay=false</c>
///   改成 <c>true</c>（选完即关闭）。原版联机下「保持界面显示"等待其他玩家"」是对的，
///   但本地多控的两个席位界面会**同时**入栈叠起来：只有栈顶可交互，选完不关就永远轮不到下面那个
///   ⇒ 关掉栈顶、露出下一个，逐席位依次作答；</item>
/// <item><c>CreateLocalRuneSelectionScreenAsync</c> + <c>CreateRuneSelectionScreenAsync</c> 前缀：
///   给逐席位符文界面的标题补上「· 角色N」，让玩家知道这一屏是在给谁选（符文是逐席位的，不标注就分不清）；</item>
/// <item><c>DismissAfterSelectionComplete</c> 前缀：② 的收尾兜底 —— 界面已被提前关掉后，海克斯批次的
///   finally 仍会对**已释放**的节点调收尾方法（它自己 catch 成一条 `ObjectDisposedException` WARN）。
///   功能无影响，但那是我们引入的噪声，直接跳过无事可做的收尾。</item>
/// <item><b>⑤ 瓦库席位自动作答</b>（v1.1.0，2026-10-05 用户实测「瓦库会弹自己的符文界面、需要真人手点」后加）：
///   ③ 记下的归属席位在 <c>RelicsSelected</c> 被等待时绑定到界面实例；只有它确实是
///   **后台托管中的瓦库席位**时，才延迟 1.2s 走游戏自己的点击路径（候选按钮的 <c>Pressed</c> 信号）
///   替它选一个符文（取最左），详见 <see cref="WakuuRuneAutoAnswer"/>。
///   真人席位的符文界面**一律不碰**。</item>
/// <item><b>⑧ 锻造器路径也登记归属</b>（v1.2.3，2026-10-11 用户实测「商店买完【随机属性锻造器】还是
///   不会自动选」后加）：商店买的随机属性锻造器 / 锻造奖励 / 随机锻造发放走的是**另一个协调器**
///   <c>HextechForgeSelectionCoordinator</c>（`SelectForge → SelectLocalForge → CreateForgeSelectionScreenAsync`），
///   它自己的建屏方法，既不经过 ③a/③b，也不改标题 ⇒ 归属一直没登记、⑤ 无从代答（真人只能手点）。
///   这里给同一条链补上同型的「建屏前记席位 → 建屏时登记」两步（见 ⑧a/⑧b）。</item>
/// </list>
///
/// <para><b>刻意不做</b>：不动海克斯的数值/发牌/敌人 hex 同步逻辑；不改游戏本体行为
/// （<c>IsLocalPlayer</c> 是第三方自己的方法）；不引用海克斯 dll（全反射，海克斯缺席即整体跳过）。</para>
/// </summary>
internal static class HextechRuneSelectionCompatPatch
{
    /// <summary>逐席位符文界面的默认标题 loc 键（与海克斯自己用的同一张表/同一个键）。</summary>
    private const string SelectionTitleTable = "relic_collection";
    private const string SelectionTitleKey = "HEXTECH_SELECTION_TITLE";

    /// <summary>幂等日志去重（键 = 场景+席位），避免每个批次刷屏。</summary>
    private static readonly HashSet<string> LoggedKeys = new();

    /// <summary>
    /// 「当前正在为哪一席创建符文界面」。由
    /// <c>CreateLocalRuneSelectionScreenAsync</c>（带 <c>Player</c> 参数）的前缀写入，
    /// 由紧随其后的 <c>CreateRuneSelectionScreenAsync</c> 前缀消费 —— 两者在**同一个同步窗口**内
    /// 先后执行，所以用普通静态字段即可，**不要**用 AsyncLocal（后者会沿异步链泄漏、回不去）。
    /// </summary>
    private static Player? _pendingScreenOwner;

    /// <summary>
    /// 「下一块界面属于哪一席」（③b 消费 <see cref="_pendingScreenOwner"/> 后落到这里，
    /// 由该界面自己的 <c>RelicsSelected</c> 消费 ⇒ ⑤ 自动作答用它判断这一屏要不要代答）。
    /// 0 = 没有待绑定的席位（非逐席位创建，例如敌人 hex 预览 / 「无可选符文」提示屏）。
    ///
    /// ⚠ r267 起**自带标题的屏也会被登记**（③b 里"登记归属"与"改标题"已解耦）。
    /// 残留风险由"每次新创建窗口开头清零"兜住（见 <see cref="CreateLocalRuneSelectionScreenPrefix"/>）。
    ///
    /// <para>⚠ v1.2.3（r268）起**锻造器路径由 ⑧a/⑧b 单独登记**：商店买的【随机属性锻造器】/ 锻造奖励
    /// 走的是 <c>HextechForgeSelectionCoordinator</c>，它有自己的建屏方法，**根本不经过** ③a/③b
    /// ⇒ 原来这一格永远是 0、⑤ 无从代答（用户 2026-10-11 实测「买完还是不会自动选」）。</para>
    /// </summary>
    private static ulong _seatForNextScreen;

    internal static void Install(
        Harmony harmony,
        Type coordinatorType,
        Type? forgeCoordinatorType,
        Type screenType,
        List<string> patched,
        List<string> missing)
    {
        // ① 「这一席是不是本机玩家」：本地席位放宽为 true。
        Patch(harmony, coordinatorType, "IsLocalPlayer", 2, patched, missing, postfix: nameof(IsLocalPlayerPostfix));

        // ② 选完即关闭：让叠在弹层栈里的多个席位界面能逐个作答。
        Patch(harmony, screenType, "RelicsSelected", 1, patched, missing, prefix: nameof(RelicsSelectedPrefix));

        // ③ 归属标注：先记下这次创建是给谁（带 Player 的那个方法），再在真正建界面时改写标题。
        //
        // ⚠ 标题挂在「异步方法」CreateRuneSelectionScreenAsync 上而**不是**小的同步工厂
        // HextechRuneSelectionScreen.Create 上：小方法会被 JIT 内联，内联副本不走 Harmony 入口
        // ⇒ 补丁形同不存在（本仓库踩过同类坑，见 skill references「坑 S」第 5 条）。
        // 异步方法的 kickoff 一定不会被内联，且它的参数里就有 titleOverride。
        Patch(harmony, coordinatorType, "CreateLocalRuneSelectionScreenAsync", 9, patched, missing,
            prefix: nameof(CreateLocalRuneSelectionScreenPrefix));
        Patch(harmony, coordinatorType, "CreateRuneSelectionScreenAsync", 10, patched, missing,
            prefix: nameof(CreateRuneSelectionScreenPrefix));

        // ④ ②的收尾兜底：界面被我们提前关掉后，批次 finally 仍会对**已释放**的节点调
        //    DismissAfterSelectionComplete() ⇒ 海克斯自己 catch 住并打一条
        //    `Failed to dismiss blocking rune selection screen: ObjectDisposedException` WARN（×每个界面）。
        //    功能无影响，但那是**我们引入的日志噪声**（会污染第三方 WARN 计数），这里直接跳过无事可做的收尾。
        Patch(harmony, screenType, "DismissAfterSelectionComplete", 0, patched, missing,
            prefix: nameof(DismissAfterSelectionCompletePrefix));

        // ⑧ 锻造器路径（v1.2.3）：商店买的【随机属性锻造器】、锻造奖励、「随机锻造发放」走的是
        //    **另一个协调器** HextechForgeSelectionCoordinator，它自己建屏（且自带标题、不经 ③a/③b）
        //    ⇒ 归属一直没登记、⑤ 无从代答。用户 2026-10-11 实测「商店买完还是不会自动选」。

        //    它那条链是：SelectForge → SelectLocalForge → CreateForgeSelectionScreenAsync，
        //    所以 ⑧a 挂在**唯一建屏前的那一步**（SelectLocalForge）记席位，⑧b 在建屏方法里消费。
        if (forgeCoordinatorType == null)
        {
            missing.Add("HextechForgeSelectionCoordinator(锻造选择屏：商店锻造器/锻造奖励)");
            Log.Warn($"{Entry.LogPrefix} 未找到锻造选择协调器（海克斯可能已更新）："
                + "商店买的【随机属性锻造器】弹出的界面仍不会被自动作答（真人点一下即可）。");
        }
        else
        {
            Patch(harmony, forgeCoordinatorType, "SelectLocalForge", 3, patched, missing,
                prefix: nameof(SelectLocalForgePrefix));
            Patch(harmony, forgeCoordinatorType, "CreateForgeSelectionScreenAsync", 1, patched, missing,
                prefix: nameof(CreateForgeSelectionScreenPrefix));
        }
    }

    /// <summary>
    /// ⑧a 锻造器路径的「创建窗口开始」：记下这屏是给哪一席建的。
    ///
    /// <para>锻造选择屏**不走** ③a（那是符文协调器的入口），它自己那条链是
    /// <c>SelectForge → SelectLocalForge → CreateForgeSelectionScreenAsync</c>；
    /// <see cref="SelectLocalForgePrefix"/> 挂在**唯一**建屏前的那一步，由紧随其后的
    /// <see cref="CreateForgeSelectionScreenPrefix"/> 消费（与 ③a/③b 同一套两步法）。</para>
    /// </summary>
    private static void SelectLocalForgePrefix(Player? player)
    {
        if (player == null || !MainModBridge.IsLocalCoopRun())
        {
            return;
        }

        _pendingScreenOwner = player;
        LogOnce(
            $"forge-create#{player.NetId}",
            $"将为该席位创建锻造选择界面（商店的【随机属性锻造器】/ 锻造奖励走的是另一个协调器）: "
            + $"player={player.NetId}");
    }

    /// <summary>
    /// ⑧b 消费 ⑧a 记下的席位：锻造选择屏**自带标题**（`HEXTECH_FORGE_SELECTION_TITLE`）⇒ 不动标题，
    /// 只登记归属，交给 ⑤ 在 <c>RelicsSelected</c> 被等待时绑到界面实例上自动作答。
    /// </summary>
    private static void CreateForgeSelectionScreenPrefix()
    {
        Player? owner = _pendingScreenOwner;
        _pendingScreenOwner = null; // 消费掉：窗口在这里关闭，避免残留绑定串到下一屏

        if (owner == null || !MainModBridge.IsLocalCoopRun() || !MainModBridge.IsLocalSeat(owner.NetId))
        {
            return;
        }

        _seatForNextScreen = owner.NetId;
        LogOnce(
            $"forge-seat#{owner.NetId}",
            $"符文界面已登记归属（锻造选择屏：自带标题、不改标题，但要能自动作答）: player={owner.NetId}");
    }

    /// <summary>
    /// ④ 节点已释放 / 已排队释放时跳过收尾（此时「移除弹层 + 等鼠标松开」早已没有对象可做）。
    /// </summary>
    private static bool DismissAfterSelectionCompletePrefix(object? __instance, ref Task __result)
    {
        if (IsUsableGodotObject(__instance))
        {
            return true;
        }

        __result = Task.CompletedTask;
        return false;
    }

    private static bool IsUsableGodotObject(object? instance)
    {
        if (instance is not GodotObject godotObject)
        {
            return false;
        }

        try
        {
            return GodotObject.IsInstanceValid(godotObject) && !godotObject.IsQueuedForDeletion();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>① 放宽「本机玩家」判定：只对本地多控会话里的**本地席位**改口，其余一律不动。</summary>
    private static void IsLocalPlayerPostfix(Player? player, ref bool __result)
    {
        if (__result || player == null || !MainModBridge.IsLocalCoopRun() || !MainModBridge.IsLocalSeat(player.NetId))
        {
            return;
        }

        __result = true;
        LogOnce(
            $"islocal#{player.NetId}",
            $"本地席位已按「本机玩家」放行（原版只认一个本机玩家；不放行该席位的符文界面不会弹、"
            + $"也没人会替它作答）: player={player.NetId}");
    }

    /// <summary>② 本地多控下选完即关闭界面（原版的「保持等待其他玩家」在同机多席位下会把下一个界面永久挡住）。</summary>
    private static void RelicsSelectedPrefix(object? __instance, ref bool removeOverlay)
    {
        // ⑤ 先把「这一屏属于哪一席」绑到界面实例上（③b 在同一同步窗口里放的），
        //    再决定要不要替它自动作答。真人在玩时这些判断都不会命中 ⇒ 行为与原来一致。
        ulong owner = TryBindAndScheduleAutoAnswer(__instance);

        if (removeOverlay || !MainModBridge.IsLocalCoopRun())
        {
            return;
        }

        removeOverlay = true;
        string ownerLabel = owner != 0UL ? owner.ToString() : "unknown";
        LogOnce(
            $"relics#{ownerLabel}",
            $"符文界面选完即关闭（本地多控：多席位界面会叠在弹层栈里，关掉栈顶才能露出下一个）: "
            + $"screen={__instance?.GetType().Name ?? "unknown"}, 最近创建席位={ownerLabel}");
    }

    /// <summary>
    /// ⑤ 绑定归属并排程自动作答：只有 <c>RelicsSelected</c> 被等待的那块界面**恰好是**
    /// 逐席位创建出来的（<see cref="_seatForNextScreen"/> 非 0）时才成立；
    /// 是不是「后台托管瓦库」由 <see cref="WakuuRuneAutoAnswer"/> 内部再判一次（真人席位不代答）。
    /// 返回被绑定的席位 NetId（0 = 这块屏不是逐席位创建的）。
    /// </summary>
    private static ulong TryBindAndScheduleAutoAnswer(object? screen)
    {
        ulong seat = _seatForNextScreen;
        _seatForNextScreen = 0UL;

        if (seat == 0UL || screen == null)
        {
            return 0UL;
        }

        try
        {
            WakuuRuneAutoAnswer.AttachSeat(screen, seat);
            WakuuRuneAutoAnswer.TrySchedule(screen);
        }
        catch (Exception exception)
        {
            Log.Warn($"{Entry.LogPrefix} 绑定符文界面归属失败（该屏不会自动作答，交真人）: "
                + $"{exception.GetType().Name}: {exception.Message}");
        }

        return seat;
    }

    /// <summary>③a 记下这次符文界面是给哪一席创建的（紧随其后的建界面调用会消费它）。</summary>
    private static void CreateLocalRuneSelectionScreenPrefix(Player? player)
    {
        // 新的创建窗口开始 ⇒ 上一个窗口残留的归属绑定作废（防错绑到别的屏）。
        _seatForNextScreen = 0UL;

        if (player == null || !MainModBridge.IsLocalCoopRun())
        {
            return;
        }

        _pendingScreenOwner = player;
        LogOnce(
            $"create#{player.NetId}",
            $"将为该席位创建符文界面（同一行后面若没有『已标注归属』，说明建界面那一环的补丁被内联跳过）: "
            + $"player={player.NetId}");
    }

    /// <summary>
    /// ③b 记下「这一屏属于哪一席」（交给 ⑤ 在 <c>RelicsSelected</c> 时绑定到界面实例上），
    /// 并给**普通符文界面**标题补上「· 角色N」（符文逐席位，不标注就分不清在给谁选）。
    ///
    /// <para>⚠ **r267 修：登记归属与"改不改标题"必须解耦**。原来这一处的守卫是
    /// <c>if (owner == null || titleOverride != null || !IsLocalCoopRun()) return;</c> ——
    /// 「自带标题」的屏（**锻造选择** / 敌人 hex 预览 / 「无可选符文」提示）会被提前 return，
    /// 连**席位都没登记** ⇒ ⑤ 不代答 ⇒ 用户 2026-10-11 实测现象：
    /// 「瓦库在商店买了海克斯符文的**随机属性锻造器**后，弹出的符文界面不会自动选」。
    /// 现在：判"该不该管"只看 owner / 会话 / 本地席位；**归属一律登记**；
    /// 只有 <c>titleOverride == null</c> 时才动标题（原意图：不覆盖第三方自带标题）。</para>
    ///
    /// <para>误代答风险很小：登记后若那屏其实没有候选（敌人 hex 预览 / 提示屏），
    /// <c>WakuuRuneAutoAnswer</c> 会"读不到候选 ⇒ 交回真人"（已有保护），真人席位也一律不代答。</para>
    /// </summary>
    private static void CreateRuneSelectionScreenPrefix(ref string? titleOverride)
    {
        Player? owner = _pendingScreenOwner;
        _pendingScreenOwner = null; // 消费掉：只有紧随其后的这块屏算这个席位的

        if (owner == null || !MainModBridge.IsLocalCoopRun())
        {
            return;
        }

        if (!MainModBridge.IsLocalSeat(owner.NetId))
        {
            return;
        }

        // ★ 先登记归属（与标题无关）：自带标题的屏（如锻造选择）也要能被 ⑤ 代答。
        _seatForNextScreen = owner.NetId;

        if (titleOverride != null)
        {
            // 自带标题：不动它的标题，但归属已登记 ⇒ 瓦库席位仍会被自动作答。
            LogOnce(
                $"seat-without-title#{owner.NetId}",
                $"符文界面已登记归属（自带标题的屏，如锻造选择：不改标题但要能自动作答）: player={owner.NetId}");
            return;
        }

        titleOverride = $"{DefaultSelectionTitle()} · {MainModBridge.SeatLabel(owner)}";
        LogOnce(
            $"title#{owner.NetId}",
            $"符文界面已标注归属（符文逐席位，靠标题区分在给谁选）: player={owner.NetId}, title={titleOverride}");
    }

    private static string DefaultSelectionTitle()
    {
        try
        {
            return new LocString(SelectionTitleTable, SelectionTitleKey).GetRawText();
        }
        catch (Exception exception)
        {
            Log.Warn($"{Entry.LogPrefix} 读取符文界面默认标题失败，使用内置文案: {exception.GetType().Name}: {exception.Message}");
            return "选择符文";
        }
    }

    /// <summary>
    /// 逐个挂补丁：**失败只 WARN，不抛**（海克斯改名 / 更新只应让本补丁退化为「不干预」，
    /// 绝不能把游戏启动或海克斯本身拖垮）。
    /// </summary>
    private static void Patch(
        Harmony harmony,
        Type type,
        string methodName,
        int parameterCount,
        List<string> patched,
        List<string> missing,
        string? prefix = null,
        string? postfix = null)
    {
        try
        {
            List<MethodInfo> candidates = type
                .GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public
                    | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == methodName)
                .ToList();

            // 先按参数个数精确匹配；海克斯改了参数个数（但没改名/没重载）时退化为唯一候选。
            MethodInfo? target = candidates.FirstOrDefault(m => m.GetParameters().Length == parameterCount)
                ?? (candidates.Count == 1 ? candidates[0] : null);

            if (target == null)
            {
                missing.Add($"{type.Name}.{methodName}/{parameterCount}");
                Log.Warn($"{Entry.LogPrefix} 目标方法未找到（海克斯可能已更新），跳过: "
                    + $"{type.Name}.{methodName}/{parameterCount}，候选={candidates.Count}");
                return;
            }

            if (target.GetParameters().Length != parameterCount)
            {
                Log.Warn($"{Entry.LogPrefix} 目标方法参数个数已变（按唯一候选挂补丁，参数名对不上会被 Harmony 拒绝）: "
                    + $"{type.Name}.{methodName} 期望 {parameterCount}，实际 {target.GetParameters().Length}");
            }

            harmony.Patch(
                target,
                prefix: prefix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(HextechRuneSelectionCompatPatch), prefix)),
                postfix: postfix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(HextechRuneSelectionCompatPatch), postfix)));
            patched.Add($"{type.Name}.{methodName}");
        }
        catch (Exception exception)
        {
            missing.Add($"{type.Name}.{methodName}/{parameterCount}");
            Log.Warn($"{Entry.LogPrefix} 挂补丁失败: {type.Name}.{methodName}/{parameterCount}: "
                + $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void LogOnce(string key, string message)
    {
        lock (LoggedKeys)
        {
            if (!LoggedKeys.Add(key))
            {
                return;
            }
        }

        Log.Info($"{Entry.LogPrefix} {message}");
    }
}
