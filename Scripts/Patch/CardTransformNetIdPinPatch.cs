using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 修复「手牌变换后 UI 与实际不同步」（铁甲战士用效果变化所有手牌时偶发看起来没生效）。
///
/// 根因：CardCmd.Transform 的视觉门 `if (!LocalContext.IsMine(cardAdded2)) continue;` 用全局
/// LocalContext.NetId 判断「牌是否属于本地玩家」。本地双角色下 NetId 会在两个本地角色间切换，
/// 当变换发生的瞬间 NetId 不是牌主人时（实测 prevNetId=527、owner=526），该视觉分支被跳过：
/// 数据层已把新牌加入手牌堆，但手牌 UI 的卡牌节点没替换 → 看起来没生效，
/// 切角色重建手牌 UI 后才正常。
///
/// 修复（r42 重写）：执行 CardCmd.Transform 期间把 LocalContext.NetId 钉到变换牌的主人，结束后恢复。
/// ⚠ 必须用 void 前缀 + postfix 包装恢复，**不能 return false 跳过原方法**——
/// 若跳过原方法，其它 mod（如 RitsuLib CardCmdTransformPatch）在本方法上的 Prefix 也不会执行，
/// 其 __state 保持 null、Postfix 访问 __state.Snapshots 会 NRE，导致出牌动作失败、
/// 牌卡在屏幕中间不进弃牌堆（实测 PlayCardAction PRIMAL_FORCE 卡死）。
/// </summary>
[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Transform), new[]
{
    typeof(IEnumerable<CardTransformation>),
    typeof(Rng),
    typeof(CardPreviewStyle),
})]
internal static class CardTransformNetIdPinPatch
{
    /// <summary>钉住前的 NetId（当前异步链）。</summary>
    private static readonly System.Threading.AsyncLocal<ulong?> _previousNetId = new System.Threading.AsyncLocal<ulong?>();

    /// <summary>当前异步链是否已钉住 NetId（postfix 据此包装恢复）。</summary>
    private static readonly System.Threading.AsyncLocal<bool> _pinActive = new System.Threading.AsyncLocal<bool>();

    [HarmonyPriority(Priority.First)]
    [HarmonyPrefix]
    private static void Prefix(IEnumerable<CardTransformation> transformations)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return;
        }

        Player? owner = ResolveOwner(transformations);
        bool isOwnerLocal = owner != null && LocalSeatSource.IsLocalSeat(owner.NetId);
        if (!isOwnerLocal)
        {
            return;
        }

        // 前台/后台 × NetId 是否已等于牌主人 × 原牌节点是否存在 × 是否托管席位 → 用纯函数判定，避免逻辑散落。
        // R3：受控位与上下文两个判定取自同一份席位快照
        SeatRegistry seats = LocalSeatSource.CurrentSeats();
        bool isOwnerAutomated = seats.IsWakuuDriven(owner!.NetId);
        bool isOwnerForeground = seats.IsControlled(owner.NetId);
        bool currentNetIdIsOwner = seats.IsContext(owner.NetId);
        CardModel? firstMissingOriginal = null;
        bool? handNodeExists = null;
        if (!isOwnerAutomated)
        {
            // 托管席位不看探针（r199：探针看到节点也不代表视觉阶段还查得到，见策略注释）。
            handNodeExists = ProbeHandNodeExists(transformations, out firstMissingOriginal);
        }

        switch (CardTransformNetIdPolicy.Decide(
                    isOwnerLocal,
                    isOwnerForeground,
                    currentNetIdIsOwner,
                    handNodeExists,
                    isOwnerAutomated))
        {
            case CardTransformNetIdAction.PinToOwner:
                // 只钉 NetId、不跳过原方法：保证其它 mod 在本方法上的 Prefix/__state 照常执行。
                // R3 B2b：钉扎前保存的"原值"读侧走唯一取数入口（逐字等价；写侧与还原照旧）。
                _previousNetId.Value = LocalSeatSource.ContextSeatId();
                LocalContext.NetId = owner.NetId;
                _pinActive.Value = true;

                LocalMultiControlLogger.Info(
                    $"[手牌同步修复] 变换期间钉 NetId 到牌主人: owner={owner.NetId}, prevNetId={_previousNetId.Value}");
                break;

            case CardTransformNetIdAction.ShiftAwayFromOwner:
            {
                // 后台角色的手牌变换 / 原牌节点缺失（BUG-25）：原版视觉分支会在前台手牌里找原卡节点，
                // 找不到就抛 "Couldn't get hand node for original card ..."（实机：瓦库打「数据链」/酒狐
                // 「不等价交换」、回合结束触发「唯我」诅咒牌），异常抛穿异步链 → 出牌中断/回合循环死亡、
                // 牌停在屏幕中间不生效不消耗。必须显式把 NetId 让开，让 vanilla 按 IsMine=false 跳过视觉。
                // 数据层在视觉分支之前就已生效，不受影响；UI 由 RestoreNetIdAfterAsync 的顺序自愈兜底。
                //
                // ⚠ 安全值**不能**是牌主人自己：r185 实机（唯我）里 owner=前台=受控位，
                // 旧实现取受控位会让 NetId 原地不动、IsMine 仍为 true ⇒ 照抛。所以受控位==主人时让到 null。
                SeatRegistry seatsShifted = LocalSeatSource.CurrentSeats();
                ulong? controlledId = seatsShifted.ControlledSeatId;
                ulong? safeNetId = controlledId.HasValue
                    && controlledId.Value != owner.NetId
                    && seatsShifted.IsLocalSeat(controlledId.Value)
                    ? controlledId
                    : null;

                _previousNetId.Value = LocalSeatSource.ContextSeatId();
                LocalContext.NetId = safeNetId;
                _pinActive.Value = true;

                if (isOwnerAutomated)
                {
                    // r199（BUG-29）：托管席位的变换一律跳过原版视觉。原版把变换分成
                    // 「数据阶段（中间有 await 挂钩点）→ 视觉阶段」，视觉阶段才查原牌手牌节点；
                    // 托管席位的手牌 UI 根本不存在 ⇒ 一旦被当成"我的牌"就必抛 Couldn't get hand node
                    // ⇒ 出牌以异常结束、牌停在屏幕中央、只换了一半（实机：猪猪 mod【猪猪王】换 3 张只换 1 张）。
                    // ⚠ 只靠"把 NetId 让开一次"挡不住（异步窗口里会被写回，r199/r201 实机两次证实）
                    // ⇒ 这里只是保底层，真正把关的是 CardTransformAutomatedSeatContextGuardPatch 的 transpiler
                    //（把视觉阶段那句 IsMine 调用点改写掉，见 AutomatedSeatTransformVisualGate）。
                    LocalMultiControlLogger.Info(
                        $"[手牌同步修复] 托管席位（瓦库）的变换一律跳过原版视觉（防异步窗口内原牌节点消失导致抛异常卡屏）: "
                        + $"owner={owner.NetId}, netId={_previousNetId.Value?.ToString() ?? "null"} -> {safeNetId?.ToString() ?? "null"}");
                }
                else if (handNodeExists == false)
                {
                    // BUG-25 实机锚点：点名是"节点缺失"这一路，便于后续统计与回归。
                    LocalMultiControlLogger.Info(
                        $"[手牌同步修复] 变换原牌的手牌节点不存在，让开 NetId 跳过原版视觉（防回合结束软锁）: "
                        + $"owner={owner.NetId}, original={firstMissingOriginal}, "
                        + $"netId={_previousNetId.Value?.ToString() ?? "null"} -> {safeNetId?.ToString() ?? "null"}");
                }
                else
                {
                    LocalMultiControlLogger.Info(
                        $"[手牌同步修复] 后台角色手牌变换：临时让开 NetId 以跳过前台动画查找: owner={owner.NetId}, "
                        + $"controlled={controlledId?.ToString() ?? "none"}, "
                        + $"netId={_previousNetId.Value?.ToString() ?? "null"} -> {safeNetId?.ToString() ?? "null"}");
                }

                break;
            }

            default:
                if (!isOwnerForeground && currentNetIdIsOwner)
                {
                    // 理论不可达（该组合会走 ShiftAwayFromOwner），保留便于日后核对。
                    LocalMultiControlLogger.Warn(
                        $"[手牌同步修复] 后台角色手牌变换未被处理: owner={owner.NetId}");
                }

                break;
        }
    }

    /// <summary>
    /// 原牌的手牌节点探针（BUG-25，r185）：与原版视觉分支同一判定源
    /// （<see cref="NCard.FindOnTable(CardModel, MegaCrit.Sts2.Core.Entities.Cards.PileType?)"/>，
    /// 内部自带 TestMode / 非战斗 / UI 未建的 null 守卫，场外调用安全）。
    /// 返回：null = 没有可探的原牌；false = **至少一张**原牌找不到手牌节点
    /// （原版循环走到它就必然抛）；true = 全部都有。
    /// </summary>
    private static bool? ProbeHandNodeExists(IEnumerable<CardTransformation>? transformations, out CardModel? firstMissing)
    {
        firstMissing = null;
        if (transformations == null)
        {
            return null;
        }

        bool any = false;
        foreach (CardTransformation transformation in transformations)
        {
            CardModel? original = transformation.Original;
            if (original == null)
            {
                continue;
            }

            any = true;
            if (NCard.FindOnTable(original, MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand) == null)
            {
                firstMissing = original;
                return false;
            }
        }

        return any ? true : null;
    }

    /// <summary>
    /// 防泄漏兜底（BUG-25，r185）：原方法抛异常时 Postfix 不会执行，
    /// Prefix 钉住/让开的 <c>LocalContext.NetId</c> 会沿这条（回合循环这类）长命异步链永久泄漏
    /// ⇒ 后续所有 IsMine 判定级联错乱。这里在异常路径上把 NetId 恢复回去。
    /// 正常路径上 Postfix 已恢复并清掉标记，本 Finalizer 是零操作。
    /// </summary>
    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception)
    {
        if (!_pinActive.Value)
        {
            return __exception;
        }

        ulong? previous = _previousNetId.Value;
        _pinActive.Value = false;
        _previousNetId.Value = null;
        if (previous.HasValue)
        {
            LocalContext.NetId = previous.Value;
        }

        if (__exception != null)
        {
            LocalMultiControlLogger.Warn(
                $"[手牌同步修复] CardCmd.Transform 抛异常，已恢复被钉住的 NetId（防异步链泄漏）: error={__exception.Message}");
        }

        return __exception;
    }

    [HarmonyPostfix]
    private static void Postfix(ref Task<IEnumerable<CardPileAddResult>> __result)
    {
        if (_pinActive.Value)
        {
            ulong? previous = _previousNetId.Value;
            _pinActive.Value = false;
            _previousNetId.Value = null;

            if (previous.HasValue)
            {
                __result = RestoreNetIdAfterAsync(__result, previous.Value);
            }
        }
    }

    private static async Task<IEnumerable<CardPileAddResult>> RestoreNetIdAfterAsync(
        Task<IEnumerable<CardPileAddResult>> task, ulong previousNetId)
    {
        try
        {
            return await task;
        }
        finally
        {
            LocalContext.NetId = previousNetId;
            // r111（BUG-7）：后台角色手牌变换会**故意跳过**原版视觉（见上面 ShiftAwayFromOwner 注释），
            // 若此刻屏幕上显示的正是该角色的手牌（切人后的延后重建窗口等），手牌 UI 的顺序/内容不会跟进，
            // 表现为「牌都对但顺序不对，切一次角色才恢复」。变换结束后排一次顺序自愈（内部有严格守卫，
            // 一致时零操作）。前台角色变换（PinToOwner）走原版视觉，这里同样是零操作。
            LocalMultiControlRuntime.ScheduleReconcileDisplayedHandOrder("hand-transform");
        }
    }

    /// <summary>取变换组中第一张牌的归属者（同一组变换牌应属同一玩家）。</summary>
    private static Player? ResolveOwner(IEnumerable<CardTransformation>? transformations)
    {
        if (transformations == null)
        {
            return null;
        }

        foreach (CardTransformation transformation in transformations)
        {
            if (transformation.Original?.Owner != null)
            {
                return transformation.Original.Owner;
            }
        }

        return null;
    }
}

/// <summary>
/// 托管席位（瓦库）的换牌视觉门兜底（r203 定案层）+ 上下文拉回（r201 保底层）。
///
/// 挂点是编译器为 <c>CardCmd.Transform</c> 生成的状态机 `<c>MoveNext</c>`
/// （异步方法本体所在、异常栈里也写着 `CardCmd+&lt;Transform&gt;d__13.MoveNext_Patch1`；解析见
/// <see cref="AsyncStateMachineTargetResolver"/>），做两件事：
/// <list type="number">
/// <item><b>transpiler（r203，主修法）</b>：把视觉阶段那句 <c>LocalContext.IsMine(cardAdded2)</c>
///   改写成 <see cref="AutomatedSeatTransformVisualGate.IsMineForTransformVisual"/> —— 托管席位的牌一律不当"我的牌"。
///   这是唯一不受"谁在什么时候写 <c>LocalContext.NetId</c>""JIT 会不会内联极小方法"影响的姿势。</item>
/// <item><b>prefix（r201 保底层）</b>：每次状态机推进一步之前，若上下文被钉在托管席位就拉回安全值
///   （修的是同一根因的另一面：被钉住的上下文会带偏其它原版视觉门）。</item>
/// </list>
///
/// **四轮实机教训（详见 references 坑 S 第 4~7 条）**：
/// r199 只在 <c>Transform</c> 的 Prefix 让开一次 ⇒ 异步窗口里被写回，照抛（日志 `netId=…326 -> …326` 已让开却仍抛）；
/// r200 给 <c>LocalContext.IsMine</c> 挂补丁 ⇒ 极小方法被 JIT 内联，补丁形同不存在（补丁已挂 `optional=16/16`，兜底日志 0 条）；
/// r201/r202 挂 MoveNext 每步拉回 ⇒ 实机证明**写回发生在同一个状态机步内**（前缀一次都没命中：`已拉回安全值` 0 条），照抛；
/// ⇒ 所以主修法必须落在"判定本身"上（transpiler 换调用点），而不是"改变量/拦方法"。
/// </summary>
[HarmonyPatch]
internal static class CardTransformAutomatedSeatContextGuardPatch
{
    private static MethodBase? _target;

    private static bool Prepare()
    {
        _target = ResolveTarget(out string description, out bool fellBack);
        if (_target == null)
        {
            LocalMultiControlLogger.Warn(
                "[手牌同步修复] 未找到 CardCmd.Transform（或其状态机 MoveNext），托管席位变换的上下文兜底未挂载"
                + "（BUG-29 可能复发）。");
            return false;
        }

        if (fellBack)
        {
            // 降级要显眼：只挂 kickoff = 只在进入时让开一次，挡不住异步窗口里的写入（r201 就是这么失效的）。
            LocalMultiControlLogger.Warn(
                $"[手牌同步修复] 托管席位变换的上下文兜底**降级**（未解析到状态机 MoveNext，只挂 kickoff 单次让开）: {description}");
        }
        else
        {
            LocalMultiControlLogger.Info($"[手牌同步修复] 已挂载托管席位变换的上下文兜底: {description}");
        }

        return true;
    }

    private static MethodBase? TargetMethod()
    {
        return _target;
    }

    /// <summary>
    /// 解析目标：优先取 <c>CardCmd.Transform(IEnumerable&lt;CardTransformation&gt;, Rng, CardPreviewStyle)</c>
    /// 的状态机 <c>MoveNext</c>；解析不到状态机时退回 kickoff（前缀仍会执行一次"让开"，
    /// 行为等价于 r199 而不会更差），并显式标注已降级。
    /// </summary>
    private static MethodBase? ResolveTarget(out string description, out bool fellBack)
    {
        description = "none";
        fellBack = false;
        MethodInfo? transform = AccessTools.Method(typeof(CardCmd), nameof(CardCmd.Transform), new[]
        {
            typeof(IEnumerable<CardTransformation>),
            typeof(Rng),
            typeof(CardPreviewStyle),
        });
        if (transform == null)
        {
            return null;
        }

        // ⚠ 状态机的 MoveNext 是 **private**（元数据实见 Private/Final/Virtual）——r201 就是只查了
        // BindingFlags.Public 才解析失败、静默降级成 kickoff 单次让开（实机日志：
        // "已挂载…: CardCmd.Transform（未解析到状态机，已降级为 kickoff 单次让开）"）。
        // 解析统一走 AsyncStateMachineTargetResolver（可离线单测）。
        MethodInfo? moveNext = AsyncStateMachineTargetResolver.FindMoveNext(transform);
        if (moveNext != null)
        {
            description = $"{moveNext.DeclaringType?.FullName}.{moveNext.Name}";
            return moveNext;
        }

        description = $"{transform.DeclaringType?.Name}.{transform.Name}"
                      + "（未解析到状态机，已降级为 kickoff 单次让开）";
        fellBack = true;
        return transform;
    }

    /// <summary>
    /// r203 主修法：把视觉阶段的 <c>LocalContext.IsMine(cardAdded2)</c> 调用点换成
    /// <see cref="AutomatedSeatTransformVisualGate.IsMineForTransformVisual"/>（签名一致 ⇒ 只换操作数，栈不变）。
    /// </summary>
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo? replacement = AccessTools.Method(
            typeof(AutomatedSeatTransformVisualGate),
            nameof(AutomatedSeatTransformVisualGate.IsMineForTransformVisual));
        if (replacement == null)
        {
            LocalMultiControlLogger.Warn(
                "[手牌同步修复] 变换视觉门改写目标缺失（AutomatedSeatTransformVisualGate），本次未改写。");
            return instructions;
        }

        List<CodeInstruction> result = instructions.ToList();
        int patched = 0;
        foreach (CodeInstruction instruction in result)
        {
            if (instruction.operand is not MethodInfo method
                || !IsCardIsMineCall(method))
            {
                continue;
            }

            instruction.operand = replacement;
            patched++;
        }

        if (patched > 0)
        {
            LocalMultiControlLogger.Info(
                $"[手牌同步修复] 已改写变换视觉门 {patched} 处 IsMine 调用点"
                + "（托管席位一律跳过换牌视觉：它的手牌 UI 不存在，找节点必抛 ⇒ 牌停屏）");
        }
        else
        {
            LocalMultiControlLogger.Warn(
                "[手牌同步修复] 变换视觉门改写 0 处：CardCmd.Transform 里找不到 IsMine(CardModel) 调用点（游戏更新？）"
                + " ⇒ 视觉兜底未生效，BUG-29 可能复发。");
        }

        return result;
    }

    /// <summary>是不是 <c>LocalContext.IsMine(CardModel)</c>（按名字 + 声明类型 + 参数类型判定，避免受 opcode/别名影响）。</summary>
    private static bool IsCardIsMineCall(MethodInfo method)
    {
        if (method.DeclaringType != typeof(LocalContext) || method.Name != nameof(LocalContext.IsMine))
        {
            return false;
        }

        ParameterInfo[] parameters = method.GetParameters();
        return parameters.Length == 1 && parameters[0].ParameterType == typeof(CardModel);
    }

    [HarmonyPrefix]
    private static void Prefix()
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        ulong? current = LocalContext.NetId;
        if (!current.HasValue)
        {
            return;
        }

        SeatRegistry seats = LocalSeatSource.CurrentSeats();
        ulong? controlled = seats.ControlledSeatId;
        bool controlledIsLocal = controlled.HasValue && seats.IsLocalSeat(controlled.Value);
        if (!AutomatedSeatTransformContextGuard.TryResolveSafeNetId(
                current,
                seats.IsWakuuDriven(current.Value),
                controlled,
                controlledIsLocal,
                out ulong? safeNetId))
        {
            return;
        }

        LocalContext.NetId = safeNetId;
        LocalMultiControlLogger.Info(
            "[手牌同步修复] 变换期间上下文被钉回托管席位，已拉回安全值"
            + "（否则原版视觉会去前台手牌找托管席位的牌节点并抛 Couldn't get hand node）: "
            + $"netId={current} -> {safeNetId?.ToString() ?? "null"}");
    }
}
