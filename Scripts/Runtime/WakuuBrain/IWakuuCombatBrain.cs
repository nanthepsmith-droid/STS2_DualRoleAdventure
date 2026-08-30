using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 可插拔「瓦库大脑」：把"下一步做什么"抽象成接口，默认实现是轻量启发式，
/// 将来任何可用求解器（如 CombatSolver）只需加一个适配器（瓦库托管优化可行性分析 21.3）。
///
/// 设计约束（全部来自已踩过的坑）：
/// 1. 只出决策、不执行：大脑禁止调用 TryManualPlay / AutoPlay / EnqueueManualUse；
/// 2. 快路径（<see cref="TryDecideNext"/>）必须同步、无 UI 依赖、绝不阻塞主线程；
/// 3. 三级降级：大脑异常 → HeuristicWakuuBrain → 最左兜底；
/// 4. 不改第三方 dll、不编译期引用（适配器用反射，失效即静默降级）；
/// 5. 归属者显式化：所有入参带 Player，禁止实现内部用 LocalContext.GetMe。
/// </summary>
internal interface IWakuuCombatBrain
{
    /// <summary>大脑标识（heuristic / solver 等），仅用于日志。</summary>
    string Id { get; }

    /// <summary>依赖是否就绪（求解器未安装/不支持多人时恒 false）。</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// 快路径：主循环每轮迭代前调用。必须同步、无副作用、不产生 UI。
    /// 返回 true 时 action 为决策结果；返回 false 表示无法决策（主循环按 EndTurn 处理）。
    /// </summary>
    bool TryDecideNext(in WakuuDecisionContext ctx, out WakuuPlannedAction action);

    /// <summary>
    /// 计划路径：整回合批量计划。实现可先同步返回 false（=不支持），将来异步求解器走这里。
    /// planFingerprint 用廉价字符串刻画计划所基于的战场状态，执行前重算指纹不一致即作废。
    /// </summary>
    bool TryPlanTurn(in WakuuDecisionContext ctx, out IReadOnlyList<WakuuPlannedAction> plan, out string planFingerprint);

    /// <summary>
    /// 派生选牌作答：给出这一步期望选中的牌。返回 false 则交既有选择器策略
    /// （LocalWakuuStrategySelector 等）。
    /// </summary>
    bool TryAnswerCardChoice(in WakuuDecisionContext ctx, IReadOnlyList<CardModel> options, int minSelect, int maxSelect, out IReadOnlyList<CardModel> chosen);

    void OnCombatBegin(Player wakuu);

    void OnTurnBegin(in WakuuDecisionContext ctx);

    void OnCombatEnd();
}
