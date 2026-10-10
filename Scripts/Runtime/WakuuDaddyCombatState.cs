#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// ④「瓦库的爹」两张「本回合」牌的战斗期登记处（**非纯逻辑**：持有 <c>Creature</c> / <c>Player</c> 引用）。
/// 纯判定在 <see cref="WakuuDaddyCardPolicy"/>。
///
/// 登记两类东西：
/// <list type="number">
/// <item><b>集火目标</b>（【你攻】）：本回合所有瓦库席位出牌时优先打它 —— 大脑在
///   <c>HeuristicWakuuBrain</c> / <c>ScoredWakuuBrain</c> 里问 <see cref="TryGetFocus"/>；
///   **战灵召唤的临时席位（战神）也在"所有瓦库"里**（它走同一个大脑）⇒ 【你攻】对它自动生效，
///   由 <see cref="LogFocusConsumedByTemporarySeat"/> 打锚点自证（R13）；</item>
/// <item><b>合体链接</b>（【合体】）：<c>施牌者 → 瓦库</c>，本回合该施牌者抽/弃牌走双方牌堆 ——
///   由 <see cref="Patch.WakuuDaddyMergePilePatch"/> 问 <see cref="TryGetMergedWakuu"/>。</item>
/// </list>
///
/// 生命周期（为什么这么设计）：
/// <list type="bullet">
/// <item>两者都是「本回合」，而「回合」= **玩家侧回合**（瓦库在同侧稍后行动 ⇒ 集火必须活到那个时刻），
///   所以统一在**玩家侧回合结束**清空 —— 挂点 = <see cref="Models.Relics.LocalWakuuDaddyRelic"/> 的
///   <c>AfterSideTurnEnd(side == Player)</c>（遗物是这三张牌的唯一来源，一定在场）；</item>
/// <item>战斗结束也清一次（<c>AfterCombatEnd</c>）：中途全灭时玩家侧回合结束可能不再触发；
///   即便漏清，读取处也一律复核「目标还活着 / 玩家还在本局里」，不会指向上一场战斗的残骸；</item>
/// <item>玩家自己死/瓦库死 ⇒ 读取处直接失效（<see cref="TryGetMergedWakuu"/> 会跳过死者）。</item>
/// </list>
/// </summary>
internal static class WakuuDaddyCombatState
{
    private static readonly List<MergeLink> Merges = new();

    private static Creature? _focusTarget;

    /// <summary>
    /// 本回合「已经为哪个**临时席位（战神）**打过『你攻生效』这条日志」。
    /// R13（战灵召唤 Phase 5）：【你攻】必须对战神同样生效（战神在瓦库名单里 ⇒ 用同一个大脑），
    /// 而这条锚点就是"真的生效了"的**可验证证据**（大脑每张牌都可能问一次集火 ⇒ 每回合每个席位只打一行）。
    /// </summary>
    private static readonly HashSet<ulong> FocusConsumedLog = new();

    /// <summary>一条【合体】链接：谁把哪个瓦库合体了（只记 NetId ⇒ 跨会话残留也只会"查不到人"而不会错指）。</summary>
    private readonly struct MergeLink
    {
        internal MergeLink(ulong casterId, ulong wakuuId)
        {
            CasterId = casterId;
            WakuuId = wakuuId;
        }

        internal ulong CasterId { get; }

        internal ulong WakuuId { get; }
    }

    internal static void SetFocus(Creature target)
    {
        _focusTarget = target;
        LocalMultiControlLogger.Info(
            $"[瓦库的爹] 你攻：本回合瓦库集火目标已登记 target={Describe(target)}");
    }

    /// <summary>
    /// 取本回合的集火目标（给瓦库大脑用）：目标必须**仍然可打**（在候选集里且没死），
    /// 否则返回 null（大脑回落到它自己的目标选择）。
    ///
    /// <paramref name="consumer"/> = 正在解析目标的那位瓦库（调用方传）。
    /// 它是**临时席位（战神）**时打一条 `你攻生效` 锚点（每回合每人一次）—— R13 的验收证据。
    /// </summary>
    internal static Creature? TryGetFocus(IEnumerable<Creature> candidates, Player? consumer = null)
    {
        Creature? target = _focusTarget;
        if (target == null || target.IsDead)
        {
            return null;
        }

        foreach (Creature candidate in candidates)
        {
            if (ReferenceEquals(candidate, target))
            {
                LogFocusConsumedByTemporarySeat(consumer, target);
                return target;
            }
        }

        return null;
    }

    /// <summary>
    /// R13 锚点：**临时席位（战神）**真的按【你攻】打出了集火目标。
    /// 只对临时席位打、每回合每个席位最多一行（大脑每张牌都会问一次集火，不加限制会刷屏）。
    /// </summary>
    private static void LogFocusConsumedByTemporarySeat(Player? consumer, Creature target)
    {
        if (consumer == null || !LocalSelfCoopContext.IsTemporarySeat(consumer.NetId))
        {
            return;
        }

        if (!FocusConsumedLog.Add(consumer.NetId))
        {
            return;
        }

        LocalMultiControlLogger.Info(
            $"[瓦库的爹] 你攻生效: player={consumer.NetId}(战神) 本回合按集火打 target={Describe(target)}");
    }

    /// <summary>登记一条合体链接（同一施牌者重复使用 ⇒ 覆盖上一条）。</summary>
    internal static void RegisterMerge(Player caster, Player wakuu)
    {
        Merges.RemoveAll(link => link.CasterId == caster.NetId);
        Merges.Add(new MergeLink(caster.NetId, wakuu.NetId));
        LocalMultiControlLogger.Info(
            $"[瓦库的爹] 合体：已登记本回合混抽/混弃 caster={caster.NetId}, wakuu={wakuu.NetId}");
    }

    /// <summary>该玩家本回合是否处在合体状态（是则返回被合体的瓦库）。</summary>
    internal static bool TryGetMergedWakuu(Player player, out Player wakuu)
    {
        wakuu = null!;
        if (Merges.Count == 0)
        {
            return false;
        }

        foreach (MergeLink link in Merges)
        {
            if (link.CasterId != player.NetId)
            {
                continue;
            }

            foreach (Player candidate in player.RunState.Players)
            {
                if (candidate.NetId != link.WakuuId || candidate.Creature.IsDead)
                {
                    continue;
                }

                if (candidate.PlayerCombatState == null)
                {
                    continue;
                }

                wakuu = candidate;
                return true;
            }

            // **R13b（2026-10-10 用户口径「【合体】覆盖战神」）**：战灵召唤的**临时席位（战神）
            // 刻意不在 `runState.Players` 里**（H1 硬约束）⇒ 上面那个循环永远查不到它，
            // 必须在**在册战神表**里再查一次。这正是"结构外成员"家族的老坑：
            // 凡事按"成员集合"查人的公共设施，都要单独兜住（见 references AE-7 / AE-15 / AE-16）。
            // 校验条件与上面保持一致（活着 + 有 PlayerCombatState 才能搬手牌 / 混抽混弃）。
            if (LocalSelfCoopContext.IsTemporarySeat(link.WakuuId))
            {
                Player? warGod = HellwarWarGodFactory.FindSpawnedWarGod(link.WakuuId);
                if (warGod != null && warGod.Creature?.IsDead != true && warGod.PlayerCombatState != null)
                {
                    wakuu = warGod;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// 【合体】混抽：合体生效时，把「随机来源」顶上那张牌搬进施牌者的抽牌堆**顶部**，
    /// 之后原版 <c>CardPileCmd.Draw</c> 照常跑（历史 / 钩子 / 演出 / 手牌上限全是原版行为，
    /// 我们只换了它下一张取的是谁）。
    ///
    /// 只沿用**同步**的牌堆内部方法（<c>RemoveFromCurrentPile</c> / <c>GiveToAnotherPlayer</c> /
    /// <c>AddInternal</c>）：Harmony 前缀不能 await，而这个搬运本来就该是"静默"的
    /// （牌从瓦库的抽牌堆挪到你的抽牌堆顶部，不该有可见演出）。
    /// 跨玩家搬牌必须**改归属**（<c>GiveToAnotherPlayer</c>）—— 否则这张牌带着瓦库的 owner
    /// 进你的手牌会被 <see cref="Patch.NPlayerHandAddOwnerGuardPatch"/> 当幽灵牌拦掉。
    /// </summary>
    internal static void InjectMixedDrawSources(Player player, decimal count)
    {
        if (count <= 0m || CombatManager.Instance.IsOverOrEnding || player.PlayerCombatState == null)
        {
            return;
        }

        if (!TryGetMergedWakuu(player, out Player wakuu))
        {
            return;
        }

        CardPile? ownDraw = PileType.Draw.GetPile(player);
        CardPile? otherDraw = PileType.Draw.GetPile(wakuu);
        if (ownDraw == null || otherDraw == null)
        {
            return;
        }

        int draws = (int)Math.Ceiling(count);
        int injected = 0;
        for (int i = 0; i < draws; i++)
        {
            IReadOnlyList<int> sources =
                WakuuDaddyCardPolicy.ResolveMergeDrawSources(!ownDraw.IsEmpty, !otherDraw.IsEmpty);
            if (sources.Count == 0)
            {
                break;
            }

            int pick = sources[player.RunState.Rng.Shuffle.NextInt(sources.Count)];
            if (pick != WakuuDaddyCardPolicy.MergeDrawOther)
            {
                // 自己的抽牌堆：原版本来就从这儿取，什么都不用做。
                continue;
            }

            CardModel? card = otherDraw.Cards.FirstOrDefault();
            if (card == null)
            {
                break;
            }

            card.RemoveFromCurrentPile(silent: true);
            if (card.Owner != player)
            {
                card.GiveToAnotherPlayer(player);
            }

            ownDraw.AddInternal(card, 0, silent: true);
            injected++;
        }

        if (injected > 0)
        {
            LocalMultiControlLogger.Info(
                $"[瓦库的爹] 合体混抽：本次从瓦库抽牌堆移入 {injected} 张到 {player.NetId} 的抽牌堆顶部"
                + $"（round={player.Creature.CombatState?.RoundNumber.ToString() ?? "?"}）");
        }
    }

    /// <summary>
    /// 【合体】混弃：把**刚被弃掉**的那批牌重新安置到「自己 / 瓦库」其中一方的弃牌堆（掷硬币决定）。
    ///
    /// 为什么是**后置**搬牌，而不是"在弃牌前改归属让原版按新 owner 落堆"（r229 踩过）：
    /// `CardModel.Pile` 是**按 Owner 反查**的（`Pile => _owner?.Piles.…Contains(this)`）⇒
    /// 在牌**已经在真人弃牌堆里**的时候先改归属，`Pile` 立刻变 null，之后任何 `CardPileCmd.Add`
    /// 都会以为"这牌不在牌堆里"、跳过摘除 ⇒ 同一张牌挂两个牌堆（那次的实机事故）。
    /// 所以这里**先摘（Owner 还是真人 ⇒ Pile 正确）→ 再改归属 → 再进瓦库弃牌堆**，全程同步内部方法。
    ///
    /// 为什么改的是归属而不是纯搬牌：战斗牌是卡组的**克隆**（`Player.PopulateCombatState`：
    /// `state.CloneCard(item)`），改归属只影响本场战斗的这份克隆，战斗结束即散，不会真的把牌偷走；
    /// 而牌与牌堆归属必须一致，否则又会变成"外人的牌躺在某人的牌堆里"（下回合抽到就是死牌）。
    /// </summary>
    internal static void RandomizeDiscardedPile(IEnumerable<CardModel>? cards)
    {
        if (cards == null)
        {
            return;
        }

        List<CardModel> list = cards.ToList();
        if (list.Count == 0)
        {
            return;
        }

        Player? actor = list[0].Owner;
        if (actor == null || !TryGetMergedWakuu(actor, out Player wakuu))
        {
            return;
        }

        if (!WakuuDaddyCardPolicy.ShouldDiscardToOther(actor.RunState.Rng.Shuffle.NextInt(2)))
        {
            return;
        }

        CardPile? target = PileType.Discard.GetPile(wakuu);
        if (target == null)
        {
            return;
        }

        int moved = 0;
        foreach (CardModel card in list)
        {
            // 只搬"真的被弃掉了"的牌（原版在战斗收尾 / 空列表时会早退，那时这些牌还在手牌里）。
            if (card.Pile?.Type != PileType.Discard)
            {
                continue;
            }

            card.RemoveFromCurrentPile(silent: false);
            if (card.Owner != wakuu)
            {
                card.GiveToAnotherPlayer(wakuu);
            }

            target.AddInternal(card, -1, silent: false);
            moved++;
        }

        if (moved > 0)
        {
            LocalMultiControlLogger.Info(
                $"[瓦库的爹] 合体混弃：{moved} 张弃牌改归瓦库 {wakuu.NetId} 的弃牌堆（施牌者 {actor.NetId}, "
                + $"round={actor.Creature.CombatState?.RoundNumber.ToString() ?? "?"}）");
        }
    }

    /// <summary>
    /// 退局复位（R5 生命周期契约，与 <see cref="LocalWakuuUniteRuntime.ResetForRun"/> 同款）。
    ///
    /// 为什么必须挂在**退局枢纽**上：本登记处是**进程级静态**，而"战斗中途退出"（既没有玩家侧回合结束、
    /// 也没有战斗结束）**不会**走任何清空路径 ⇒ 残留的 (caster, wakuu) 链接会在**下一局**、
    /// 同一份档案同一台机器（NetId 不变）的席位对上重新成立，表现为"下一局第一场战斗一开始就在混抽"。
    /// 2026-10-07 r230 判读实机日志时发现（那局 r230 是中途退出，全程没有 `本回合登记已清空`）。
    /// </summary>
    internal static void ResetForRun(string source)
    {
        _focusTarget = null;
        Merges.Clear();
        FocusConsumedLog.Clear();
        LocalMultiControlLogger.Info($"[瓦库的爹] 本回合登记已复位: {source}");
    }

    /// <summary>玩家侧回合结束 / 战斗结束：清掉所有「本回合」登记。</summary>
    internal static void ClearTurnScoped()
    {
        if (_focusTarget != null || Merges.Count > 0)
        {
            LocalMultiControlLogger.Info(
                $"[瓦库的爹] 本回合登记已清空（集火={( _focusTarget == null ? "无" : Describe(_focusTarget))}, 合体={Merges.Count} 条）");
        }

        _focusTarget = null;
        Merges.Clear();
        FocusConsumedLog.Clear();
    }

    private static string Describe(Creature creature)
        => creature.Player?.NetId.ToString() ?? creature.Monster?.Id.Entry ?? "?";
}
