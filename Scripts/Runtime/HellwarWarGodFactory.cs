#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using LocalMultiControl.Scripts.Models.Powers;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Unlocks;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「战神」（战斗内临时玩家）的**低层工厂** —— 生成 / 接入主 run / 战斗态 / 立绘 / 清理的**唯一实现**。
///
/// 两个上层入口共用它（避免两条链各写一份、日后只修一处）：
/// <list type="bullet">
/// <item><see cref="HellwarPocRuntime"/>：Phase 0 POC（环境变量 / 标志文件开关，每场 1 个哑战神，调试用）；</item>
/// <item><see cref="HellwarSummonCombatRuntime"/>：Phase 2 正式召唤（`hellwarGod` 开关 + 回合开始选择 UI + 消耗计数）。</item>
/// </list>
///
/// 建"有立绘的临时玩家"的正确顺序（源码依据：设计文档 #21 §5.1 / §5.3 / §11.3）：
/// <code>
/// Player.CreateForNewRun(character, unlockState, tempSeatId)   // 内含 PopulateStartingInventory（初始卡组 + 初始遗物 +
///   → AttachToRun(player, state)           // ★ 接入主 run：RunState + 种子 + **卡组 Owner**（漏了后者 ⇒ 洗牌即 NRE）
///   → combatState.AddPlayer(player)        // = AttachCreature + AddCreature（**数据层**，此刻还【没有】UI）
///   → player.ResetCombatState() + player.PopulateCombatState(...)   // ★ 战斗态（漏了 ⇒ **真人**抽牌就 NRE）
///   → CombatManager.AddCreature(creature)  // 订阅 + CreaturesChanged
///   → NCombatRoom.AddCreature(creature)    // ★ 建 NCreature ⇒ **立绘 / 血条就在这里出现**
///   → await CombatManager.AfterCreatureAdded(...) + Hook.AfterCreatureAddedToCombat(...)
/// </code>
/// ⚠ **不能用 <c>CreatureCmd.Add</c>**：它第一步就调 <c>CombatState.AddCreature</c>，而我们已经通过
/// <c>AddPlayer</c> 把它加进战斗 ⇒ 会撞 <c>InvalidOperationException("Creature is already in this combat…")</c>。
/// ⚠ 清理时**必须**自己摘视觉节点（<c>NCombatRoom</c> 不订阅 <c>CreaturesChanged</c>）+ 自己退订
/// <c>StateTracker</c>（<c>CombatManager.RemoveCreature</c>），照 <c>CreatureCmd.Escape</c> 的做法。
/// </summary>
internal static class HellwarWarGodFactory
{
    /// <summary>
    /// 一个**在册战神**的登记项（Phase 4 起 `_warGods` 存它而不是裸 `Player`）：
    /// 除了 Player 本身，还要记住**绑主**（R8：绑主死 ⇒ 战神立刻死）与**档位**
    /// （R9：复活按档位成本计费），这两个信息事后无法从 `Player` 反推。
    /// </summary>
    private sealed class Registration
    {
        internal Registration(Player player, ulong summonerNetId, HellwarWarGodTier tier)
        {
            Player = player;
            SummonerNetId = summonerNetId;
            Tier = tier;
        }

        /// <summary>战神本体。</summary>
        internal Player Player { get; }

        /// <summary>绑主（召唤者）的 NetId。POC 哑战神为 0 = 无绑主（不参与 R8）。</summary>
        internal ulong SummonerNetId { get; }

        /// <summary>召唤时的档位（R9 复活按它计费）。</summary>
        internal HellwarWarGodTier Tier { get; }

        /// <summary>R8 已处理过"绑主死亡"（防每帧重复击杀 —— `CreatureCmd.Kill` 是异步的）。</summary>
        internal bool SummonerBondHandled { get; set; }

        /// <summary>R9 已复活过（日志用；不阻止多次复活）。</summary>
        internal int ReviveCount { get; set; }

        /// <summary>"战神已死亡"锚点是否已经打过（复活时复位 ⇒ "死→复活→再死"各打一条）。</summary>
        internal bool DeathLogged { get; set; }

        /// <summary>
        /// **R10 修订（2026-10-10 用户口径）**：这个战神**出场**时所在的 `CombatState.RoundNumber`
        /// —— 它只有在**更晚的 round**才有资格当肉盾（<see cref="HellwarSummonPolicy.IsGuardEligible"/>）。
        /// 出场 = 召唤 **或** 复活（复活同样自带一层【无实体】）⇒ 两条路径都要刷新这个值。
        /// </summary>
        internal int SpawnRound { get; set; }
    }

    /// <summary>已生成的战神（清理 / 退 run 卡牌表 / 席位号段分配的唯一持有者；不跨局残留）。</summary>
    private static readonly List<Registration> _warGods = new();

    /// <summary>本场已给哪些绑主挂过 R10「战灵守卫」Power（避免重复 Apply；战斗结束 / 退局清空）。</summary>
    private static readonly HashSet<ulong> _guardAppliedSummoners = new();

    /// <summary>当前在册的战神（供诊断 / 日志）。</summary>
    internal static IReadOnlyList<Player> SpawnedWarGods
        => _warGods.Select((registration) => registration.Player).ToList();

    /// <summary>
    /// 按 NetId 取**在册战神**。
    ///
    /// 用途：`LocalContext.GetMe(IPlayerCollection)` 的口子只拿到 id（`playerCollection` 里查不到战神
    /// —— 它刻意不进 `runState.Players`），必须由我们按 id 反查 Player 实例（见
    /// <see cref="Patch.LocalContextTemporarySeatGetMePatch"/>）。
    /// </summary>
    internal static Player? FindSpawnedWarGod(ulong netId)
        => FindRegistration(netId)?.Player;

    /// <summary>按 NetId 取在册战神的**登记项**（Phase 4：R8/R9 要读绑主与档位）。</summary>
    private static Registration? FindRegistration(ulong netId)
    {
        foreach (Registration registration in _warGods)
        {
            if (registration.Player.NetId == netId)
            {
                return registration;
            }
        }

        return null;
    }

    /// <summary>战斗内还活着的临时席位玩家（按战斗态反查，最可靠）。</summary>
    internal static List<Player> CollectWarGods(CombatState? state)
    {
        if (state == null)
        {
            return new List<Player>();
        }

        return state.PlayerCreatures
            .Where((creature) => creature.Player != null && LocalSelfCoopContext.IsTemporarySeat(creature.Player.NetId))
            .Select((creature) => creature.Player!)
            .ToList();
    }

    /// <summary>战斗内临时席位玩家数。</summary>
    internal static int CountWarGods(CombatState? state) => CollectWarGods(state).Count;

    /// <summary>R9：某战神登记项对应的档位（复活计费用；查不到兜普通档）。</summary>
    internal static HellwarWarGodTier TierOf(Player warGod)
        => FindRegistration(warGod.NetId)?.Tier ?? HellwarWarGodTier.Common;

    /// <summary>
    /// R9：某绑主名下**已死但尸体还留在战斗里**的战神（回合开始的召唤选择给它们"复活"选项）。
    /// 返回的是登记表里的 Player（按绑主过滤 + 仍在 `state.Players` 里 + `IsAlive == false`）。
    /// </summary>
    internal static List<Player> CollectDeadWarGods(CombatState? state, ulong summonerNetId)
    {
        List<Player> dead = new();
        if (state == null || summonerNetId == 0UL)
        {
            return dead;
        }

        foreach (Registration registration in _warGods)
        {
            if (registration.SummonerNetId != summonerNetId)
            {
                continue;
            }

            if (!state.Players.Contains(registration.Player))
            {
                continue;   // 已经不在本场战斗里（清理过）⇒ 不是"留着尸体等复活"
            }

            Creature? creature = registration.Player.Creature;
            if (creature != null && !creature.IsAlive)
            {
                dead.Add(registration.Player);
            }
        }

        return dead;
    }

    /// <summary>
    /// **R10（Phase 4）**：取某绑主名下**第一个还活着、且已有资格承伤的战神** —— 就是"替它吃那一半伤害"的那个承接者。
    /// 查不到（没有战神 / 都死了 / 已被清理 / 绑主 NetId 为 0 / **刚出场还没满一个回合**）⇒ 返回 <c>null</c>
    /// ⇒ <see cref="HellwarWarGodGuardPower"/> 的两个钩子都**原样放行**（等价于没有守卫）。
    ///
    /// ⚠ "资格"= <see cref="HellwarSummonPolicy.IsGuardEligible"/>（**R10 修订，2026-10-10 用户口径**）：
    /// 战神**出场那一回合不能当肉盾** —— 它那个回合自带 1 层【无实体】（打不死），
    /// 若同时还能替绑主吃一半伤害，「打不死 + 白挡」叠加作为肉盾太强。
    /// 资格过滤放在这里（而不是扩散到 Power 的两个钩子）⇒ 钩子判据不用改、
    /// `hasAliveGuard` 自动变成"有已满一个回合的活战神"。
    /// </summary>
    /// <param name="currentRound">当前 `CombatState.RoundNumber`；拿不到（0 / 异常）时**一律判为无资格**
    /// （保守：状态不明时宁可不变肉盾，也不乱转伤害）。</param>
    internal static Creature? FindGuardCreature(ulong summonerNetId, int currentRound)
    {
        if (summonerNetId == 0UL)
        {
            return null;
        }

        foreach (Registration registration in _warGods)
        {
            if (registration.SummonerNetId != summonerNetId)
            {
                continue;
            }

            if (!HellwarSummonPolicy.IsGuardEligible(registration.SpawnRound, currentRound))
            {
                continue;
            }

            Creature? creature = registration.Player.Creature;
            if (creature != null && creature.IsAlive)
            {
                return creature;
            }
        }

        return null;
    }

    /// <summary>
    /// 日志用：某绑主名下**在册且活着**的战神席位号（**不带** R10 的"满一个回合"资格过滤），
    /// 例如 <c>4096(本回合起承伤), 4097(下一回合起)</c> —— 用来解释"为什么现在还不挡"。
    /// </summary>
    internal static string DescribeGuardCandidates(ulong summonerNetId, int currentRound)
    {
        if (summonerNetId == 0UL)
        {
            return "无";
        }

        List<string> parts = new();
        foreach (Registration registration in _warGods)
        {
            if (registration.SummonerNetId != summonerNetId)
            {
                continue;
            }

            if (registration.Player.Creature?.IsAlive != true)
            {
                continue;
            }

            parts.Add(HellwarSummonPolicy.IsGuardEligible(registration.SpawnRound, currentRound)
                ? $"seat{registration.Player.NetId}(可承伤)"
                : $"seat{registration.Player.NetId}(出场回合 {registration.SpawnRound}，下一回合起可承伤)");
        }

        return parts.Count == 0 ? "无" : string.Join(", ", parts);
    }

    /// <summary>
    /// 当前战斗的 `CombatState.RoundNumber`（拿不到时 0 ⇒ 一律判"无承伤资格"，保守）。
    /// 与 <see cref="HellwarWarGodGuardPower"/> 里那条 `Owner.Creature.CombatState?.RoundNumber` 同源。
    /// </summary>
    private static int ResolveCurrentRound()
        => CombatManager.Instance?.DebugOnlyGetState()?.RoundNumber ?? 0;

    /// <summary>
    /// **R10（Phase 4）：给绑主挂上「战灵守卫」**（<see cref="HellwarWarGodGuardPower"/>）——
    /// 效果 = 它受到的**攻击**伤害先减半、再转给战神（D4 拍板：玩家零伤害 + 战神吃一半）。
    ///
    /// 幂等：同一场战斗里同一个绑主只挂一次（Power 不保存"是哪个战神"，每个钩子当场按绑主 NetId 查
    /// ⇒ 战神换人 / 死而复生 都不用重挂）。失败只 WARN，**不影响召唤本身**。
    /// </summary>
    private static async Task EnsureGuardPowerAsync(Player summoner, string tag)
    {
        try
        {
            if (!_guardAppliedSummoners.Add(summoner.NetId))
            {
                return;   // 本场已经挂过
            }

            Creature? creature = summoner.Creature;
            if (creature == null || !creature.IsAlive)
            {
                return;
            }

            HookPlayerChoiceContext choiceContext = new(summoner, summoner.NetId, GameActionType.CombatPlayPhaseOnly);
            await PowerCmd.Apply<HellwarWarGodGuardPower>(choiceContext, creature, 1m, creature, null);

            // R10 修订（2026-10-10 用户口径）：Power 照旧在召唤时就挂上（幂等、每场战斗一次），
            // 但**承伤资格从下一回合起**才成立（见 FindGuardCreature / IsGuardEligible）——
            // 所以这条日志要把"谁在册 / 谁已经有资格"讲清楚，别让人以为挂了就等于在挡。
            int round = ResolveCurrentRound();
            LocalMultiControlLogger.Info(
                $"{tag} 已给绑主挂上【战灵守卫】（R10：战神替它吃一半攻击伤害；"
                + $"**肉盾资格自下一回合起** —— 出场回合自带【无实体】，不当肉盾）: 绑主={summoner.NetId}, "
                + $"round={round}, 在册杀手={DescribeGuardCandidates(summoner.NetId, round)}");
        }
        catch (Exception exception)
        {
            _guardAppliedSummoners.Remove(summoner.NetId);   // 允许下次重试
            LocalMultiControlLogger.Warn(
                $"{tag} 给绑主挂【战灵守卫】失败（已忽略，不影响召唤）: 绑主={summoner.NetId}, error={exception.Message}");
        }
    }

    /// <summary>
    /// Phase 4 观测锚点：登记项"活着 → 死了"时打一条。
    ///
    /// 为什么需要它：**"战神被敌人打死"原本没有任何日志锚点** —— r256 判读时只能从
    /// `已复活战神（R9）` 反推"它死过"（明确被绑主连坐那条有 `战神已随绑主死亡`）。
    /// 复活成功会复位标记 ⇒ "死 → 复活 → 再死"各打一条；每帧调用、只打一次。
    /// </summary>
    internal static void LogWarGodDeaths(CombatState? state, string tag = "[地狱战神]")
    {
        if (state == null || _warGods.Count == 0)
        {
            return;
        }

        foreach (Registration registration in _warGods)
        {
            try
            {
                Creature? creature = registration.Player.Creature;
                if (creature == null || creature.IsAlive || registration.DeathLogged)
                {
                    continue;
                }

                registration.DeathLogged = true;
                LocalMultiControlLogger.Info(
                    $"{tag} 战神已死亡（**尸体留场**，绑主可在自己回合开始复活它）: seat={registration.Player.NetId}, "
                    + $"档位={registration.Tier}, 绑主={registration.SummonerNetId}, "
                    + $"血量={creature.CurrentHp}/{creature.MaxHp}, 已复活过={registration.ReviveCount} 次");
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn(
                    $"{tag} 战神死亡锚点检查失败（已跳过该席位）: seat={registration.Player.NetId}, error={exception.Message}");
            }
        }
    }

    /// <summary>
    /// **R8（Phase 4）：绑主已死 ⇒ 把它的战神立刻杀掉**（每帧 tick；由
    /// <c>LocalMultiControlRuntime.TryAutoEndTurnForRelicControlledPlayer</c> 在**玩家侧与敌方回合都**调用）。
    ///
    /// ⚠ 设计文档 §4 R8 原本的理由（"战神在 `CombatState.Players` 里 ⇒ 战斗不会判负"）**不成立**：
    /// 判负看的是 `runState.Players`（`CreatureCmd.cs:476`），而战神刻意不进那张表 ⇒ R8 **不是**"防挂机"的
    /// 必需保障。真正的价值是**一致性**：绑主都死了，它的战神不该继续替它挨打 / 输出
    /// （否则死掉那位玩家的战神就成了给队友白嫖的肉盾）。
    ///
    /// 做法 = **每帧 tick 检查**，不是挂 `AfterDeath` 钩子：那条链要求 `LocalContext.NetId` 有值、且会走
    /// `HookPlayerChoiceContext` 的暂停语义（`Hook.cs:462-476`），在"临时席位"语境下不够省心；而本文件
    /// 已经有一条每帧 tick（`TryEndIdleWarGodTurns` 同款驱动）⇒ 复用即可，延迟 ≤ 1 帧。
    /// </summary>
    internal static void EnforceSummonerBond(CombatState? state, string source, string tag = "[地狱战神]")
    {
        if (state == null || _warGods.Count == 0)
        {
            return;
        }

        foreach (Registration registration in _warGods.ToList())
        {
            try
            {
                if (registration.SummonerBondHandled || registration.SummonerNetId == 0UL)
                {
                    continue;
                }

                Creature? warGodCreature = registration.Player.Creature;
                if (warGodCreature == null || !warGodCreature.IsAlive)
                {
                    continue;   // 战神自己已死 / 已不在 ⇒ 没什么可收的
                }

                if (IsSummonerAlive(state, registration.SummonerNetId))
                {
                    continue;
                }

                // 标记**先置**：`CreatureCmd.Kill` 是异步的，下一次 tick 会再进来（否则会重复走整条死亡链）。
                registration.SummonerBondHandled = true;
                LocalMultiControlLogger.Info(
                    $"{tag} 绑主已死 ⇒ 立刻击杀战神（R8）: seat={registration.Player.NetId}, "
                    + $"绑主={registration.SummonerNetId}, 档位={registration.Tier}, source={source}");
                TaskHelper.RunSafely(KillBoundWarGodAsync(registration, tag));
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn(
                    $"{tag} 绑主死亡联动检查失败（已跳过该席位）: seat={registration.Player.NetId}, error={exception.Message}");
            }
        }
    }

    private static async Task KillBoundWarGodAsync(Registration registration, string tag)
    {
        try
        {
            await CreatureCmd.Kill(registration.Player.Creature, force: true);
            LocalMultiControlLogger.Info(
                $"{tag} 战神已随绑主死亡（**尸体留场**，绑主可在自己回合开始复活它）: seat={registration.Player.NetId}");
        }
        catch (Exception exception)
        {
            registration.SummonerBondHandled = false;   // 允许下一帧重试
            LocalMultiControlLogger.Warn(
                $"{tag} 击杀绑主的战神失败（下帧会重试）: seat={registration.Player.NetId}, error={exception.Message}");
        }
    }

    /// <summary>绑主是否还活着（读 `runState.Players`；查不到 = 按"已死"处理，把战神收掉更安全）。</summary>
    private static bool IsSummonerAlive(CombatState state, ulong summonerNetId)
    {
        try
        {
            Player? summoner = state.RunState.GetPlayer(summonerNetId);
            return summoner?.Creature is { IsAlive: true };
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// **R9（Phase 4）：复活一个已死的战神**（由绑主在**回合开始的召唤选择**里点"复活"触发；
    /// 计数由调用方**先扣**、失败由调用方退回 —— 见 `HellwarSummonCombatRuntime.RunReviveAsync`）。
    ///
    /// 步骤（源码依据 `OstyCmd.Summon` / `Player.ReviveBeforeCombatEnd`）：
    /// <list type="number">
    /// <item>`CreatureCmd.Heal(creature, MaxHp)` —— **"复活"就是把它治到 &gt; 0**：`Creature.IsDead` 是
    ///   `CurrentHp &lt;= 0` 的派生量，`HealInternal` 在"死→活"那一刻会自动 `ActivateHooks()` + 抛
    ///   `Creature.Revived`（`Creature.cs:478-487`），顺带播复活动画（`CreatureCmd.cs:772-775`）。
    ///   ⚠ `CreatureCmd.Heal` 的第三个参数是 **`playAnim`**（不是 `isReviving`）—— 别被别的文档写法误导。</item>
    /// <item>`ResetCombatState()` + `PopulateCombatState(...)` **必须重填**：绑主死亡时原版
    ///   `CombatManager.HandlePlayerDeath` 会把它的 5 个牌堆的牌全部 `RemoveFromCombat`
    ///   ⇒ 不复填就是"空手复活"（与召唤时那条链同款做法）。</item>
    /// <item>`MarkReadyToEndTurnIfMidPlayerTurn`：复活发生在**本回合开始之后**（`StartTurn` 那一圈早跑完），
    ///   不补登就会让「结束回合」的分母多一个 ⇒ 真人点不动结束回合（r244 同款，坑 AE-3）。</item>
    /// <item>再给一层【无实体】保命（与召唤一致，用户 2026-10-10 口径）：刚爬起来别再立刻死。</item>
    /// </list>
    /// </summary>
    /// <returns>是否真的复活成功（false ⇒ 调用方应把已扣的计数退回）。</returns>
    internal static async Task<bool> ReviveWarGodAsync(Player warGod, CombatState state, string tag)
    {
        Registration? registration = FindRegistration(warGod.NetId);
        if (registration == null)
        {
            LocalMultiControlLogger.Warn($"{tag} 复活失败：该席位不在册（可能已被清理）: seat={warGod.NetId}");
            return false;
        }

        Creature? creature = warGod.Creature;
        if (creature == null || creature.IsAlive)
        {
            LocalMultiControlLogger.Warn(
                $"{tag} 复活失败：战神并不处于死亡状态: seat={warGod.NetId}, 存活={creature?.IsAlive}");
            return false;
        }

        try
        {
            decimal maxHp = creature.MaxHp;
            await CreatureCmd.Heal(creature, maxHp);
            warGod.ResetCombatState();
            warGod.PopulateCombatState(warGod.RunState.Rng.Shuffle, state);
            MarkReadyToEndTurnIfMidPlayerTurn(state, warGod, tag);
            await ApplySummonProtectionAsync(creature, tag);
            registration.ReviveCount++;
            registration.DeathLogged = false;   // 复活 ⇒ 复位死亡锚点（"死→复活→再死"各打一条）
            // R10 修订：复活也算"重新出场"（同样补一层【无实体】）⇒ 同样要等下一回合才能当肉盾。
            registration.SpawnRound = state.RoundNumber;

            LocalMultiControlLogger.Info(
                $"{tag} 已复活战神（R9）: seat={warGod.NetId}, 档位={registration.Tier}, "
                + $"血量={creature.CurrentHp}/{creature.MaxHp}, 第 {registration.ReviveCount} 次复活, "
                + $"抽牌堆={warGod.PlayerCombatState?.DrawPile.Cards.Count ?? 0} 张, "
                + $"战斗内玩家={state.Players.Count}, "
                + $"肉盾资格=下一回合起（R10 修订：出场回合不当肉盾）");
            return true;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"{tag} 复活战神异常（计数由调用方退回）: seat={warGod.NetId}, error={exception}");
            return false;
        }
    }

    /// <summary>按 Player 实例摘掉登记项（多处清理共用）。</summary>
    private static void RemoveRegistration(Player warGod)
    {
        for (int i = _warGods.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_warGods[i].Player, warGod))
            {
                _warGods.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// 分配下一个空闲的临时席位号（<see cref="HellwarPocPolicy.TempSeatBase"/> 起，跳过**已被确切占用**的号）。
    /// Phase 2b 要一次召唤多个 ⇒ 不能再用"当前个数"当索引（会有空洞）。
    ///
    /// 🔴 **别用 `LocalSelfCoopContext.IsTemporarySeat` 当"占用判断"**：它是范围谓词（`0x1000..0x1FFF` 全 true）
    /// ⇒ 逐个试号会**全部**判成占用 ⇒ 兜底返回段起点 ⇒ **每个战神都拿到同一个 NetId**。
    /// r245 实机踩中（日志铁证：`本地多控自动补齐敌方回合就绪: mirrored=…,4096,4096`），
    /// 表现 = "第二个战神不会被打"（按 NetId 取数的路径永远解析到第一个）。
    /// 占用集合 = 已登记的临时席位 ∪ 在册战神 ∪ **战斗内现有玩家**（防御性）。
    /// </summary>
    internal static ulong NextSeatId(CombatState? state)
    {
        HashSet<ulong> occupied = new(LocalSelfCoopContext.TemporarySeatIds);
        foreach (Registration registration in _warGods)
        {
            occupied.Add(registration.Player.NetId);
        }

        if (state != null)
        {
            foreach (Player player in state.Players)
            {
                occupied.Add(player.NetId);
            }
        }

        return HellwarPocPolicy.ResolveFreeSeatId(occupied);
    }

    /// <summary>
    /// 生成一个战神并接进战斗（**同步**完成到"立绘已出现"这一步，收尾是后台任务）。
    /// 任何一步炸了都会**尽力回滚**（半成品战神留在 <c>state.Players</c> 里 = 全场卡死）。
    /// </summary>
    /// <param name="tag">日志前缀（`[地狱战神POC]` / `[地狱战神]`）—— 判读锚点靠它区分两条入口。</param>
    /// <param name="preset">
    /// D7 预设档（Phase 2b）：决定"卡组要不要全升级 / 额外血上限 / 预设遗物"。**角色皮由调用方先解析好**传成
    /// <paramref name="character"/>（普通档 = 召唤者自身角色；其余档 = 预设表里的原版角色）。
    /// ⚠ 预设必须在**接进 run 之前**应用（<see cref="AttachToRun"/> 会登记卡组每张牌的 `Owner` 与 run 卡牌表，
    /// 之后 `PopulateCombatState` 再把它克隆进抽牌堆 ⇒ 改晚了就进不了抽牌堆）。
    /// </param>
    /// <param name="summoner">
    /// **绑主**（Phase 4 / R8：绑主死 ⇒ 战神立刻死；R9：只有绑主能复活它）。
    /// 传 <c>null</c> = 无绑主（POC 哑战神），此时不参与 R8/R9。
    /// </param>
    internal static bool TrySpawn(
        CombatState state,
        CharacterModel character,
        HellwarWarGodPreset preset,
        string tag,
        Player? summoner,
        out Player? warGod)
    {
        warGod = null;

        Player? rollbackCandidate = null;
        try
        {
            ulong seatId = NextSeatId(state);
            UnlockState unlock = state.RunState.UnlockState;
            Player player = Player.CreateForNewRun(character, unlock, seatId);
            rollbackCandidate = player;

            // ★ 预设档（Phase 2b / 预设卡组）：必须在 AttachToRun / PopulateCombatState **之前**做（见 preset 注释）。
            int upgraded = ApplyPresetToInventory(player, preset, tag);

            LocalMultiControlLogger.Info(
                $"{tag} ① 已建临时玩家: seat={seatId}(0x{seatId:X}), character={character.Id}, "
                + $"hp={player.Creature.CurrentHp}/{player.Creature.MaxHp}, "
                + $"遗物={player.Relics.Count} 件, 卡组={player.Deck.Cards.Count} 张"
                + $"({HellwarWarGodPresets.Describe(preset)}, 升级了={upgraded} 张)");

            // ①b ★★ 必做（r237 实测漏了它 = NullReferenceException ⇒ 全场卡死）：见 AttachToRun 注释。
            AttachToRun(player, state, tag);

            // ①c ★ 预设遗物（② 预设遗物，r255）：必须在 AttachToRun **之后**（那时 `player.RunState`
            // 不再是 `NullRunState`、`TotalFloor` 可读）。只发"不依赖开战时机"的遗物（A 方案，见
            // `HellwarWarGodRelics` 的类注释与 references 坑 AE-5b）；"获得时"钩子在生成收尾阶段补调。
            IReadOnlyList<RelicModel> presetRelics = HellwarWarGodRelics.GrantTo(
                player,
                preset.Tier,
                tag,
                preset.RelicEntries,
                preset.IsBuiltin ? "内置默认" : $"{preset.DisplayName}({preset.PresetId})");

            state.AddPlayer(player);
            LocalMultiControlLogger.Info(
                $"{tag} ② 已加入战斗（数据层）: seat={seatId}, Players={state.Players.Count}, "
                + $"Allies={state.Allies.Count}（此刻还没有立绘节点）");

            // ★★ 必做（r236 实测漏了它就 turn loop died）：照抄游戏开战时的两件事建立**战斗态** ——
            // `PlayerCombatState`（手牌/抽牌堆/能量 …）是 `ResetCombatState` 建的，卡组克隆进抽牌堆是
            // `PopulateCombatState` 做的。**漏掉的后果不是"战神自己不好用"**，而是
            // `CombatHistoryEntry` 构造时遍历**全体玩家**读 `player.PlayerCombatState.TurnNumber`
            // ⇒ 战神那个 null 会让**真人抽牌**直接 `NullReferenceException` ⇒ `turn loop died`（全场卡死）。
            player.ResetCombatState();
            player.PopulateCombatState(player.RunState.Rng.Shuffle, state);
            LocalMultiControlLogger.Info(
                $"{tag} ②b 已建立战斗态（PlayerCombatState + 抽牌堆）: seat={seatId}, "
                + $"抽牌堆={player.PlayerCombatState?.DrawPile.Cards.Count ?? 0} 张（= 预设档卡组，D7：{preset.Tier}）");

            AddTemporarySeatToSession(seatId, tag);

            // ③a r266：给这条临时席位**补一条本地动作队列**（原版队列开局按 runState.Players 建，
            //   战神是战斗内才出现的 ⇒ 它入队 hook 会在 `ActionQueueSet.GetQueue` 抛
            //   "Tried to get local action queue for nonexistent player"；见 references AE-24）。
            LocalActionQueueSetBridge.EnsureQueueForTemporarySeat(seatId, tag);

            CombatManager.Instance!.AddCreature(player.Creature);
            NCombatRoom.Instance?.AddCreature(player.Creature);
            LocalMultiControlLogger.Info(
                $"{tag} ③ 已建战斗内节点（**立绘/血条应已出现**）: seat={seatId}, "
                + $"房间可用={NCombatRoom.Instance != null} —— 角色皮取该预设的 skin（{preset.SkinKey}）："
                + "普通档默认 = 召唤者自身角色（画面上与真人相同，这是预期、不是重复席位的 bug），"
                + "在第二层「选预设」里选了别的角色时就用那套的皮；"
                + "它的回合由本功能自动收口（日志见 `战神已自动结束回合`）");

            // ③b ★★ 必做（r244 实机"召唤战神后**不能结束回合**"的真因）：
            // 回合开始那一帧，`CombatManager.StartTurn` 会把「不在 `playersStartingTurn` 里」的玩家
            // **预先标记为"本回合已结束回合"**（`CombatManager.cs:802-813`）。我们是在**玩家回合进行中**
            // 才把人加进 `State.Players` 的 ⇒ 那个循环早跑完了 ⇒ 战神**永远不进 `PlayersReadyToEndTurn`**，
            // 而「结束回合」完成判据是 `PlayersReadyToEndTurn.Count == State.Players.Count`
            // （`CombatManager.cs:1063`；本仓不是 `NetGameType.Singleplayer`，拿不到 `:1073` 那条 SP 旁路）
            // ⇒ 分母永远多一个 ⇒ **真人怎么点都结束不了回合**。
            // ⚠ 这跟 r238 踩的是**两道不同的门**：r238 是 gate ②（`PlayersReadyToBeginEnemyTurn`，
            //   `SetReadyToBeginEnemyTurn`，已由 `CombatManagerReadyEnemyTurnPatch` 认临时席位），
            //   这里是 gate ①（`PlayersReadyToEndTurn`，`SetReadyToEndTurn`）—— **两道都要认临时席位**。
            MarkReadyToEndTurnIfMidPlayerTurn(state, player, tag);

            TaskHelper.RunSafely(FinishSpawnAsync(state, player.Creature, seatId, tag, presetRelics));
            _warGods.Add(new Registration(player, summoner?.NetId ?? 0UL, preset.Tier)
            {
                // R10 修订：记下"出场回合" ⇒ 本回合（含紧接的敌方回合）它还不能当肉盾。
                SpawnRound = state.RoundNumber,
            });

            // ★ R10（Phase 4）：给**绑主**挂「战灵守卫」（它受到的攻击伤害减半后转给战神）。
            // 放在登记**之后**：Power 的钩子要按绑主 NetId 反查"当前守卫"（FindGuardCreature）。
            if (summoner != null)
            {
                TaskHelper.RunSafely(EnsureGuardPowerAsync(summoner, tag));
            }

            warGod = player;
            return true;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"{tag} 生成异常（本次召唤失败）: error={exception}");
            RollbackSpawn(state, rollbackCandidate, tag);
            warGod = null;
            return false;
        }
    }

    /// <summary>
    /// 把 D7 预设档应用到刚建好的玩家身上（**必须在 <see cref="AttachToRun"/> 之前**调用）：
    /// <list type="number">
    /// <item><b>预设卡组替换</b>（2026-10-10 用户拍板：手工 id 列表 + 开放到配置文件）：
    ///   见 <see cref="ApplyPresetDeck"/>。内置默认用**类型引用**写死（编译期可校验），
    ///   配置里可用字符串 id 覆盖；普通档默认不替换（= 召唤者自身角色初始卡组，自动兼容 mod 角色）。</item>
    /// <item><b>卡组全升级</b>（<paramref name="preset"/>.<c>UpgradeDeck</c>）：逐张 `IsUpgradable` 判定后
    ///   `UpgradeInternal()` + `FinalizeUpgradeInternal()`（顺序不能反，后者收尾预览态）。
    ///   已在配置里单独标了 <c>+</c> 的牌此时已到满级 ⇒ `IsUpgradable` 为假，**不会重复升级**。</item>
    /// <item><b>额外血上限</b>（`BonusHp`）：`SetMaxHpInternal` 之后再 `SetCurrentHpInternal`（把当前血补满到新上限，
    ///   否则会显示成"半血出生"）。</item>
    /// </list>
    /// 返回实际升级的张数（打日志用）。
    /// </summary>
    private static int ApplyPresetToInventory(Player player, HellwarWarGodPreset preset, string tag)
    {
        int upgraded = ApplyPresetDeck(player, preset, tag);

        if (preset.UpgradeDeck)
        {
            foreach (CardModel card in player.Deck.Cards.ToList())
            {
                if (!card.IsUpgradable)
                {
                    continue;
                }

                card.UpgradeInternal();
                card.FinalizeUpgradeInternal();
                upgraded++;
            }
        }

        if (preset.BonusHp > 0 && player.Creature != null)
        {
            decimal maxHp = player.Creature.MaxHp + preset.BonusHp;
            player.Creature.SetMaxHpInternal(maxHp);
            player.Creature.SetCurrentHpInternal(maxHp);
        }

        if (player.Deck.Cards.Count == 0)
        {
            // D7 口径：某角色 `StartingDeck` 为空（原版 `Deprived` 那类占位）⇒ 按「空卡组战神」处理，**不崩、不阻塞召唤**。
            LocalMultiControlLogger.Warn(
                "预设档卡组为空（该角色 StartDeck 为空？）⇒ 按「空卡组战神」处理，不阻塞召唤。"
                + $"seat={player.NetId}, 皮肤={preset.SkinKey}");
        }

        return upgraded;
    }

    /// <summary>
    /// **把角色初始卡组换成预设卡组**（2026-10-10 用户拍板的手工预设卡组）。
    ///
    /// 落点为什么在这里：`CreateForNewRun` 已经把 `Character.StartingDeck` 灌进 `player.Deck`
    /// （`Player.PopulateStartingDeck`），而 `AttachToRun` 会给**此刻的**卡组牌补 `Owner` + 登记进 run 卡牌表，
    /// `PopulateCombatState` 再把**此刻的**卡组克隆进抽牌堆 ⇒ **本方法必须在 AttachToRun 之前**改完卡组，
    /// 否则新牌不会进抽牌堆 / 老牌留下幽灵登记。
    ///
    /// 做法照抄 `PopulateStartingDeck` 的原语：`canonical.ToMutable()` → `Deck.AddInternal(..., silent: true)`
    /// （**Owner / 登记仍交给 <see cref="AttachToRun"/>**，别在这里自己调 `runState.AddCard`——
    /// 此刻 `player.RunState` 还是 `NullRunState`）。`Deck.Clear(silent: true)` 是安全的：
    /// 卡组不是战斗牌堆（不动 `StateTracker`），且此刻还没登记进 run 的卡牌表。
    ///
    /// 返回"逐张标记升级"的张数（档位的**全升级**开关在调用方另外跑一遍）。
    /// </summary>
    private static int ApplyPresetDeck(Player player, HellwarWarGodPreset preset, string tag)
    {
        // 预设战神 v2（r269 起地基；r273 订正语义）：预设**自带卡组文本**（hellwar_presets.json，格式与 hellwarDeck* 一致）优先；
        // 预设没写（null = 继承）⇒ 走旧链：hellwarDeck* 配置键 → 内置默认。
        // ⚠ 预设**显式写空串 = 「不替换」**（保留该角色初始卡组）—— 这一条必须在这里拦住：
        //   `DeckPlan.Parse("")` 返回 null，会一路落到"该档内置默认卡组"（罕见=静默猎手 / 稀有=铁甲战士 / 先古=摄政王），
        //   对"角色预设"（普通档的**所有角色**）就是错的 —— 会把别的角色的内置卡组套到所选角色身上。
        if (preset.DeckText != null && preset.DeckText.Trim().Length == 0)
        {
            LocalMultiControlLogger.Info(
                $"{tag} ①a 预设卡组不替换（预设显式留空 ⇒ 沿用该角色初始卡组）: seat={player.NetId}, "
                + $"档位={preset.Tier}, 来源=预设({preset.DisplayName}({preset.PresetId}))");
            return 0;
        }

        string deckOverride = preset.DeckText ?? LocalWakuuAutopilotConfig.HellwarDeckOverride(preset.Tier);
        HellwarWarGodDeckResolution deck = HellwarWarGodDecks.Resolve(preset.Tier, deckOverride);

        if (deck.Cards is not { Count: > 0 })
        {
            // 不替换（普通档内置默认 / 配置全认不出时回落）⇒ 保留角色初始卡组。
            return 0;
        }

        player.Deck.Clear(silent: true);
        int upgraded = 0;
        int enchanted = 0;
        int enchantSkipped = 0;
        List<string> enchantDetail = new();
        foreach (HellwarWarGodDeckCard entry in deck.Cards)
        {
            CardModel card = entry.Canonical.ToMutable();
            card.FloorAddedToDeck = 1;

            // 附魔必须在 `Deck.AddInternal` **之前**施加（r277，见 ApplyPresetEnchantment 的注释：
            // 此刻 Pile == null ⇒ 绕开"按 Owner 记账"的那段，而战神此刻 Owner 还是 null）。
            if (!string.IsNullOrEmpty(entry.EnchantEntry))
            {
                if (ApplyPresetEnchantment(card, entry, tag, enchantDetail))
                {
                    enchanted++;
                }
                else
                {
                    enchantSkipped++;
                }
            }

            player.Deck.AddInternal(card, -1, silent: true);

            if (entry.Upgraded)
            {
                // 顺序照 `CardCmd.Upgrade`：先 UpgradeInternal 再 FinalizeUpgradeInternal（后者收尾预览态）。
                // ⚠ 升级在后也没关系：`CardModel.UpgradeInternal` 自己会回调 `Enchantment?.ModifyCard()`
                //   （游戏源码注释：降级 / 反序列化后要"重放"附魔的修改逻辑），所以附魔的改值不会被升级冲掉。
                card.UpgradeInternal();
                card.FinalizeUpgradeInternal();
                upgraded++;
            }
        }

        string source = preset.DeckText != null
            ? $"预设({preset.DisplayName}({preset.PresetId}))"
            : deck.FromConfig ? "配置文件" : "内置默认";
        LocalMultiControlLogger.Info(
            $"{tag} ①a 预设卡组已替换: seat={player.NetId}, 档位={preset.Tier}, 来源={source}, "
            + $"张数={player.Deck.Cards.Count}（逐张标记升级={upgraded} 张, 之后全升级开关={preset.UpgradeDeck}）"
            + (deck.FromConfig
                ? $", 配置文本={deckOverride}"
                : $", 内置默认={HellwarWarGodDecks.DescribeBuiltinDeck(preset.Tier)}"));

        if (enchanted > 0 || enchantSkipped > 0)
        {
            // 只在"真的写了附魔"时打这一行（老配置 / 老预设 ⇒ 这条锚点不出现 = 零噪声）。
            LocalMultiControlLogger.Info(
                $"{tag} ①d 预设附魔: seat={player.NetId}, 档位={preset.Tier}, 施加={enchanted} 张, "
                + $"跳过={enchantSkipped} 张, 明细=[{string.Join(", ", enchantDetail)}]");
        }

        return upgraded;
    }

    /// <summary>
    /// 给战神卡组里的一张牌**静默施加预设附魔**（r277）。返回是否施加成功。
    ///
    /// <para><b>为什么必须在 <c>Deck.AddInternal</c> 之前调</b>：<c>CardCmd.Enchant</c> 结尾有一段
    /// "牌在牌堆里且是 Deck ⇒ 往 <c>CurrentMapPointHistoryEntry.GetEntry(card.Owner.NetId)</c> 记一笔"；
    /// 而战神的 <c>Owner</c> 要到 <see cref="AttachToRun"/> 才设，此刻是 null ⇒ 会 NRE。
    /// 不打牌堆时（<c>Pile == null</c>）那段整段跳过 ⇒ 天然规避，且不必碰任何"按玩家 / 席位记账"的东西
    /// （这正是临时席位那条线最容易炸的地方）。</para>
    ///
    /// <para>校验用游戏自己的 <c>EnchantmentModel.CanEnchant</c>（默认排除 状态 / 诅咒 / 任务牌，
    /// 个别附魔更严，如 Sharp 只给攻击牌）；认不出 / 不能带 ⇒ **只跳过那个附魔**，卡照给（不阻断召唤）。</para>
    /// </summary>
    private static bool ApplyPresetEnchantment(
        CardModel card,
        HellwarWarGodDeckCard entry,
        string tag,
        List<string> detail)
    {
        string enchantEntry = entry.EnchantEntry!;
        try
        {
            EnchantmentModel? canonical = HellwarWarGodDecks.LookupEnchantmentByEntry(enchantEntry);
            if (canonical == null)
            {
                LocalMultiControlLogger.Warn(
                    $"{tag} 预设附魔认不出（只跳过这个附魔，卡照给）: 卡={entry.Canonical.Id.Entry}, "
                    + $"附魔={enchantEntry}（要写 ModelId.Entry，如 SHARP / TEZCATARAS_EMBER）");
                detail.Add($"{entry.Canonical.Id.Entry}←{enchantEntry}(认不出)");
                return false;
            }

            if (!canonical.CanEnchant(card))
            {
                LocalMultiControlLogger.Warn(
                    $"{tag} 这张牌不能带该附魔（游戏判据 CanEnchant=false，只跳过附魔）: "
                    + $"卡={entry.Canonical.Id.Entry}, 附魔={canonical.Id.Entry}");
                detail.Add($"{entry.Canonical.Id.Entry}←{canonical.Id.Entry}(不适用)");
                return false;
            }

            EnchantmentModel mutable = canonical.ToMutable();
            CardCmd.Enchant(mutable, card, entry.EnchantAmount);
            detail.Add($"{entry.Canonical.Id.Entry}←{canonical.Id.Entry}:{entry.EnchantAmount}");
            return true;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"{tag} 施加预设附魔失败（跳过这个附魔，卡照给）: 卡={entry.Canonical.Id.Entry}, "
                + $"附魔={enchantEntry}, error={exception.GetType().Name}: {exception.Message}");
            detail.Add($"{entry.Canonical.Id.Entry}←{enchantEntry}(异常)");
            return false;
        }
    }

    /// <summary>
    /// 中途加进来的战神**补登「本回合已结束回合」**（它这一轮本来就不该有回合，见 <see cref="TrySpawn"/> 的 ③b 注释）。
    /// 只在"当前是玩家侧回合、战斗进行中、它还没 ready"时做一次（<c>SetReadyToEndTurn</c> 自身幂等）。
    /// </summary>
    private static void MarkReadyToEndTurnIfMidPlayerTurn(CombatState state, Player warGod, string tag)
    {
        try
        {
            CombatManager? manager = CombatManager.Instance;
            if (manager == null || !manager.IsInProgress || state.CurrentSide != CombatSide.Player)
            {
                // 非玩家侧（理论上进不来：召唤选择只在玩家回合开始时弹）⇒ 下一轮开始时会自然被 StartTurn 的那一圈收掉。
                return;
            }

            if (manager.IsPlayerReadyToEndTurn(warGod))
            {
                return;
            }

            manager.SetReadyToEndTurn(warGod, canBackOut: false);
            LocalMultiControlLogger.Info(
                $"{tag} ③b 战神已补登「本回合已结束回合」（否则结束回合门的分母永远多一个）: "
                + $"seat={warGod.NetId}, 战斗内玩家={state.Players.Count}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"{tag} 补登战神「本回合已结束回合」失败（可能表现为不能结束回合）: seat={warGod.NetId}, error={exception.Message}");
        }
    }

    /// <summary>
    /// 把临时玩家**接进主 run**（`RunState` / 种子 / 卡组 Owner），**绝不进 `RunState.Players`**
    /// （设计文档 #21 §5.3「让战神复用主 run 的 `RunState`」；H1：绝不能进 `runState.Players`）。
    /// 这里照抄 `RunState.AddPlayerDebug`（`sts2src/src/Core/Runs/RunState.cs:604-633`）里**除「入 run 玩家表」之外**
    /// 的全部与本链相关的步骤 —— 其中卡组 `Owner` 那一步是 r237 实测的真凶：
    /// <list type="number">
    /// <item>`player.RunState = runState` —— 否则 `Player.RunState` 恒为 `NullRunState`（卡牌的 `RunState` 跟着 null）。</item>
    /// <item>`player.InitializeSeed(runState.Rng.StringSeed)` —— 建 `PlayerRng` / `PlayerOdds`。</item>
    /// <item>卡组每张牌 `AfterCreated()` + **`Owner`**（顺序照游戏：先 `AfterCreated` 再 `AddCard(card, player)`）。
    ///   🔴 **漏了随后的 `Owner` = 抽牌堆一洗牌就 NRE**：`CombatState.Contains(AbstractModel)` 对战斗里的每张牌读
    ///   `card.Owner.IsActiveForHooks`，而**卡组牌的 `Owner` 只有 `AddPlayerDebug` 会赋值**
    ///   （`PopulateStartingDeck` → `Deck.AddInternal` **不设 Owner**）⇒ 我们不走那条路 ⇒ 克隆进抽牌堆的牌
    ///   `Owner == null` ⇒ `ModifyShuffleOrder` → `CombatState.IterateHookListeners` → `Contains` ⇒
    ///   `NullReferenceException` ⇒ `turn loop died` ⇒ **战神一出场就全场卡死**（r237 实测，见 references 坑 AC-6）。</item>
    /// <item>**本局当前地图点的 `PlayerStats` 里登记这一席**（`runState.CurrentMapPointHistoryEntry?.PlayerStats.Add(...)`，
    ///   与 `AddPlayerDebug` 同名那一步）。
    ///   🔴 **漏了它 = 战神"不会被打"**（r245 实机）：`CreatureCmd.Damage` 会在**受击结算**里
    ///   `CurrentMapPointHistoryEntry.GetEntry(受击者.NetId)` 记 `DamageTaken`（`RunState.cs:1721` 一带），
    ///   而 `GetEntry` 找不到就**抛** `InvalidOperationException("Player with ID 4096 not found in player stats
    ///   for this run history!")` ⇒ 那次攻击整体失败（第三方兜住后日志里只有一句 `怪物招式 … 执行失败（已兜住）`）
    ///   ⇒ 敌方**打不到战神**。⇒ 与"进 `runState.Players`"**不同**：历史统计表是可以（也必须）登记临时席位的，
    ///   清理时按 `PlayerId` 摘掉即可（不进玩家表、不进存档终态）。</item>
    /// </list>
    /// ⚠ 不做 `AddPlayerDebug` 的 `PopulateRelicGrabBagIfNecessary` / `ApplyAscensionEffects`
    /// （那两项是"正式 run 成员"语义；战神不进 `runState.Players`，也不该被算进遗物袋 / 飞升效果）。
    /// </summary>
    private static void AttachToRun(Player player, CombatState state, string tag)
    {
        IRunState runState = state.RunState;
        player.RunState = runState;

        if (runState is NullRunState)
        {
            // 理论上进不来（战斗一定有 run）。真进来了**别碰 `RunState` 的卡牌表**（`NullRunState.AddCard` 会抛），
            // 只把 Owner 补上，别让 `Contains` 拿到 null。
            foreach (CardModel card in player.Deck.Cards)
            {
                card.Owner = player;
            }

            LocalMultiControlLogger.Warn(
                $"{tag} ①b 无 run 状态（NullRunState），只补了卡组 Owner: seat={player.NetId}, "
                + $"卡组={player.Deck.Cards.Count} 张");
            return;
        }

        player.InitializeSeed(runState.Rng.StringSeed);
        foreach (CardModel card in player.Deck.Cards)
        {
            card.AfterCreated();
        }

        foreach (CardModel card in player.Deck.Cards)
        {
            // = `AddPlayerDebug` 的 `AddCard(card2, player)`：设 Owner + 登记进 run 的卡牌表（`ContainsCard` 才为真）。
            runState.AddCard(card, player);
        }

        // ★★ 必做（r245 实机"战神不会被打"）：本局当前地图点的玩家统计表也要有这一席，
        //    否则受击结算时 `MapPointHistoryEntry.GetEntry(seatId)` 直接抛（详见方法注释）。
        bool historyRegistered = RegisterInMapPointHistory(runState, player.NetId);

        LocalMultiControlLogger.Info(
            $"{tag} ①b 已接入主 run（**不进 runState.Players**）: seat={player.NetId}, "
            + $"RunState={runState.GetType().Name}, 卡组={player.Deck.Cards.Count} 张"
            + $"（已补：RunState 指向 / 种子 / Owner / run 卡牌表登记 / 地图点统计={historyRegistered}）");
    }

    /// <summary>
    /// 把临时席位登记进**本局当前地图点的玩家统计表**（= `RunState.AddPlayerDebug` 的那一步）。
    /// 受击 / 遗物 / 药水等一堆路径都会 `CurrentMapPointHistoryEntry.GetEntry(NetId)`，**查不到就抛**。
    /// 返回是否真的登记上了（没有当前地图点时为 false —— 那种情况下 `GetEntry` 那条链自己也会走 `?.` 短路）。
    /// </summary>
    private static bool RegisterInMapPointHistory(IRunState runState, ulong seatId)
    {
        try
        {
            MapPointHistoryEntry? entry = runState.CurrentMapPointHistoryEntry;
            if (entry == null)
            {
                return false;
            }

            if (entry.PlayerStats.All((stats) => stats.PlayerId != seatId))
            {
                entry.PlayerStats.Add(new PlayerMapPointHistoryEntry { PlayerId = seatId });
            }

            return true;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"注册临时席位到地图点统计表失败（战神可能会「不会被打」）: seat={seatId}, error={exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// `AttachToRun` 的镜像清理：把战神卡组牌从 run 的卡牌表除名（`RemoveCard` 会同时把 `Owner` 置回 null），
    /// 并把这一席从**本局当前地图点的玩家统计表**里摘掉（别把"战神"留进 run 历史 / 存档）。
    /// </summary>
    private static void DetachFromRun(Player player, string tag)
    {
        if (player.RunState is NullRunState)
        {
            return;
        }

        int detached = 0;
        foreach (CardModel card in player.Deck.Cards.ToList())
        {
            player.RunState.RemoveCard(card);
            detached++;
        }

        int historyRemoved = 0;
        try
        {
            historyRemoved = player.RunState.CurrentMapPointHistoryEntry?.PlayerStats
                .RemoveAll((stats) => stats.PlayerId == player.NetId) ?? 0;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"{tag} 从地图点统计表摘除临时席位失败（已忽略）: seat={player.NetId}, {exception.Message}");
        }

        LocalMultiControlLogger.Info(
            $"{tag} 已从 run 卡牌表除名: seat={player.NetId}, 卡组={detached} 张, 地图点统计摘除={historyRemoved}");
    }

    /// <summary>生成失败时的尽力回滚：把战神撤出战斗（别让它半死不活地留在 `state.Players` 里）。</summary>
    private static void RollbackSpawn(CombatState state, Player? player, string tag)
    {
        if (player == null)
        {
            return;
        }

        try
        {
            if (state.PlayerCreatures.Contains(player.Creature))
            {
                CombatManager.Instance?.RemoveCreature(player.Creature);
                state.RemoveCreature(player.Creature);
            }

            DetachFromRun(player, tag);
            RemoveRegistration(player);
            LocalMultiControlLogger.Warn(
                $"{tag} 已回滚半成品战神: seat={player.NetId}, 剩余 Players={state.Players.Count}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"{tag} 回滚半成品战神失败（战神可能仍留在战斗里）: error={exception}");
        }
    }

    /// <summary>
    /// 是不是"临时席位（战神）的回合"。是则**延后一帧**替它结束回合并返回 <c>true</c>
    /// （调用方应立即 `return`，别把它当前台切）。
    /// </summary>
    internal static bool TryRequestWarGodTurnEnd(Player? player, string tag, string source)
    {
        if (player == null || !LocalSelfCoopContext.IsTemporarySeat(player.NetId))
        {
            return false;
        }

        Player warGod = player;
        Callable.From(() => EndWarGodTurn(warGod, tag, source)).CallDeferred();
        return true;
    }

    /// <summary>
    /// 战神自己的回合：它（Phase 2 阶段）没有 AI ⇒ **必须有人替它结束回合** ——
    /// 否则 `state.Players` 里这一席永远不 ready ⇒ 回合链卡死（实测：不能出牌 / 不能结束回合 /
    /// 不能切人 / 瓦库不打牌 / 开局只抽 1 张，全是这一个根因）。
    /// ⚠ 调用方必须先 `CallDeferred`（等回合建立完成再收），本方法只做"能收就收"。
    /// </summary>
    internal static void EndWarGodTurn(Player warGod, string tag, string source)
    {
        try
        {
            Creature? creature = warGod.Creature;
            if (creature == null || creature.CombatState == null || !creature.IsAlive)
            {
                return;
            }

            if (CombatManager.Instance?.IsPlayerReadyToEndTurn(warGod) ?? false)
            {
                return;
            }

            // 标记"这次收口是模组自己发起的"：收口后若又拿到可出牌（罕见），托管链会被放行继续打
            // （与瓦库 `TryEndAllPlayersWhenNoCards` 同款语义，见 WakuuTurnEndOrigin）。
            WakuuTurnEndOrigin.BeginModIssuedEnd();
            try
            {
                PlayerCmd.EndTurn(warGod, canBackOut: false);
            }
            finally
            {
                WakuuTurnEndOrigin.EndModIssuedEnd();
            }

            LocalMultiControlLogger.Info(
                $"{tag} 战神已自动结束回合: seat={warGod.NetId}, source={source}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"{tag} 战神结束回合失败: seat={warGod.NetId}, error={exception}");
        }
    }

    /// <summary>
    /// Phase 3（R11）**托管收口**：把"当前是玩家侧回合、活着、还没 ready、且手牌再无可出牌"的
    /// 临时席位（战神）逐个结束回合。
    ///
    /// 为什么必须有人做（r244 的教训）：战神进了 `state.Players` 就计入「结束回合」的分母
    /// （`PlayersReadyToEndTurn.Count == State.Players.Count`），没有驱动者的席位永远不 ready ⇒ **回合链停住**。
    /// Phase 2 是"回合开始立即收口"；Phase 3 改成"先由托管链出牌、没牌可打时才收口" ——
    /// 所以收口时机从 <see cref="TryRequestWarGodTurnEnd"/>（回合开始）挪到了这里（每帧 tick）。
    /// ⚠ 收口用 <see cref="WakuuTurnEndOrigin.BeginModIssuedEnd"/> 包裹：这样"收口后又拿到新牌"
    /// 会被托管链放行继续打（与瓦库 <c>TryEndAllPlayersWhenNoCards</c> 同款语义）。
    /// </summary>
    /// <returns>本次收口的战神个数。</returns>
    internal static int TryEndIdleWarGodTurns(CombatState? state, string source, string tag = "[地狱战神]")
    {
        if (state == null || state.CurrentSide != CombatSide.Player)
        {
            return 0;
        }

        int ended = 0;
        foreach (Player warGod in CollectWarGods(state))
        {
            try
            {
                Creature? creature = warGod.Creature;
                if (creature == null || !creature.IsAlive)
                {
                    continue;
                }

                if (CombatManager.Instance?.IsPlayerReadyToEndTurn(warGod) ?? false)
                {
                    continue;
                }

                // 还有**托管能打**的牌 ⇒ 交给托管出牌（下一帧 tick 会再判）；没有 ⇒ 这一席当刻没事做，替它 ready。
                // ⚠ r263：判据必须是**托管口径**（`LocalWakuuRelicRuntime.HasHostedPlayableCards`），不能用裸 `CanPlay()` ——
                // 一张"数据上能打、托管一打就卡"的牌（需要目标却没目标 / 效果内部抛）会让这里永远认为
                // "还有牌可出" ⇒ 战神永不 ready ⇒ 真人「结束回合」被挡（r262 实机死锁的收口那一跳）。
                if (LocalWakuuRelicRuntime.HasHostedPlayableCards(warGod, state.RoundNumber))
                {
                    continue;
                }

                EndWarGodTurn(warGod, tag, source);
                ended++;
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn(
                    $"{tag} 托管收口战神回合失败（已跳过该席位）: seat={warGod.NetId}, error={exception.Message}");
            }
        }

        return ended;
    }

    private static async Task FinishSpawnAsync(
        CombatState state,
        Creature creature,
        ulong seatId,
        string tag,
        IReadOnlyList<RelicModel> presetRelics)
    {
        try
        {
            await CombatManager.Instance!.AfterCreatureAdded(creature);
            await Hook.AfterCreatureAddedToCombat(state, creature);

            // r255（② 预设遗物）：补调"获得时"钩子（`AddRelicInternal` 不会替你调，见 HellwarWarGodRelics 注释）。
            await HellwarWarGodRelics.RunAfterObtainedAsync(presetRelics, tag);

            // r253（2026-10-10 用户要求）：召唤当回合给战神一层【无实体】保命。
            await ApplySummonProtectionAsync(creature, tag);

            LocalMultiControlLogger.Info(
                $"{tag} ④ 生成收尾完成: seat={seatId}, "
                + $"是玩家={creature.IsPlayer}, 存活={creature.IsAlive}, "
                + $"被 LocalContext 当成我={MegaCrit.Sts2.Core.Context.LocalContext.IsMe(creature)}（**期望 False**）");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"{tag} 生成收尾异常: seat={seatId}, error={exception}");
        }
    }

    /// <summary>
    /// 召唤当回合给战神一层【无实体】（原版 <see cref="IntangiblePower"/>）。
    ///
    /// **为什么**（r253 用户口径）：战神召唤当回合**不出牌**（它的手牌是空的 —— 中途加入没有起手抽牌），
    /// 高压环境里它很容易在"还没动手"时就被打死、白费计数。⇒ 召唤时先给一层保命。
    ///
    /// 语义核对（`IntangiblePower.cs`）：`ModifyHpLostAfterOsty` 把该 creature 受到的 HP 损失压到 **1**
    /// （**立即生效**，与回合时机无关）；层数在**敌方回合结束时** `Decrement`
    /// （`AfterSideTurnEnd(side == Enemy)`）⇒ 1 层刚好覆盖"召唤当回合 + 接下来的那个敌方回合"。
    ///
    /// 失败只记 WARN，**不影响召唤本身**（保命是附加收益，不该把召唤拖下水）。
    /// </summary>
    private static async Task ApplySummonProtectionAsync(Creature creature, string tag)
    {
        try
        {
            Player? warGod = creature.Player;
            if (warGod == null || !creature.IsAlive)
            {
                return;
            }

            HookPlayerChoiceContext choiceContext = new(warGod, warGod.NetId, GameActionType.CombatPlayPhaseOnly);
            await PowerCmd.Apply<IntangiblePower>(choiceContext, creature, 1m, creature, null);
            LocalMultiControlLogger.Info(
                $"{tag} 已给战神一层【无实体】（召唤当回合不死，保到敌方回合结束）: seat={warGod.NetId}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"{tag} 给战神加【无实体】失败（已忽略，不影响召唤）: seat={creature.Player?.NetId}, error={exception.Message}");
        }
    }

    /// <summary>
    /// 把一个战神从战斗里摘干净（视觉节点 + `StateTracker` 订阅 + 战斗态 + run 卡牌表）。
    /// ⚠ 视觉节点 + `StateTracker` 订阅都要自己收（`NCombatRoom` 不订阅 `CreaturesChanged`，
    /// 只有"怪物死亡"那条路会自动摘节点）—— 照 `CreatureCmd.Escape` 的做法。
    /// </summary>
    private static void RemoveWarGodFromCombat(CombatState state, Creature creature, string tag)
    {
        // ⚠ **顺序铁律（r261 修）**：先把**数据层**摘干净，再收**视觉节点**（节点引用要**提前抓**）。
        //
        // 原来顺序相反（先 `NCombatRoom.RemoveCreatureNode`），而**第三方布局补丁挂在那条链上** ——
        // r259 / r261 实机铁证：NinjaSlayer 的 `YamotoKokiAllyLayoutPatch`（`PositionPlayersAndPets` 后置）
        // 在 `RemoveCreatureNode` 链上抛 `Sequence contains no matching element`
        // ⇒ 异常一出，后面的**数据摘除全部没执行** ⇒ 战神留在 `CombatState.Players` 里、一路留到发奖时刻
        // （r260 的"逐项容错"只保证"不中断别的战神"，这一个自己仍然没摘干净；实战靠 R12 闸门兜住）。
        // 现在：① 数据（必须成功、不包 try）② 会话/登记/战斗态（各自 try）③ 视觉节点（best-effort）。
        NCreature? node = NCombatRoom.Instance?.GetCreatureNode(creature);   // ★ 数据还在时先抓住节点

        CombatManager.Instance.RemoveCreature(creature);   // StateTracker 退订 + 广播 CreaturesChanged
        state.RemoveCreature(creature);                    // 从 `_allies` 摘掉 ⇒ `state.Players` 收缩

        Player? warGod = creature.Player;
        if (warGod != null)
        {
            try
            {
                DetachFromRun(warGod, tag);
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"{tag} 战神从 run 卡牌表除名失败（已忽略）: seat={warGod.NetId}, {exception.Message}");
            }

            RemoveRegistration(warGod);   // 纯内存表，不会抛

            // r266：动作队列收尾（取消它队列里还没执行的动作；队列本体由原版 `ActionQueueSet.Reset()`
            // 在战斗结束/换局时统一清空）。best-effort，失败只记一条。
            LocalActionQueueSetBridge.CancelPendingForTemporarySeat(warGod.NetId);

            // 战斗态也清掉（房间退出时原版只按 `RunState.Players` 复位，战神不在其中 ⇒ 否则污染下一场）。
            try
            {
                warGod.ResetCombatState();
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"{tag} 战神战斗态复位失败（已忽略）: seat={warGod.NetId}, {exception.Message}");
            }
        }

        // ③ 视觉节点：**best-effort**（数据层已经干净 ⇒ 这里失败最多留一个画面残留，
        // 不会再污染 `CombatState.Players` / 发奖）。
        // ⚠ **先藏再摘**（r262）：第三方布局补丁会在 `RemoveCreatureNode` 里抛（本环境 19 次/局），
        // 一抛后面两行就不执行 ⇒ 画面上会留一个"还站着的空位"。所以把"不可交互 + 隐藏"放到调用**之前**。
        if (node != null)
        {
            try
            {
                node.ToggleIsInteractable(on: false);
                node.Visible = false;
                NCombatRoom.Instance!.RemoveCreatureNode(node);
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn(
                    $"{tag} 战神视觉节点摘除失败（节点已隐藏；数据层已摘干净）："
                    + $"第三方布局补丁常在这一链上抛: seat={warGod?.NetId}, error={exception.Message}");
            }
        }

        LocalMultiControlLogger.Info(
            $"{tag} 已从战斗移除战神: seat={warGod?.NetId}, 剩余 Players={state.Players.Count}");
    }

    /// <summary>
    /// 战后奖励**发放之前**清理：把所有临时席位（战神）摘出战斗，并注销会话里的临时席位登记。
    /// 必须早于奖励生成：原版会遍历 `CombatState.Players` 发奖（设计文档 §5.8 / §13.3.1）。
    /// 幂等（第二次调用时已无战神，直接返回）。
    /// </summary>
    /// <returns>本次移除的战神个数。</returns>
    internal static int CleanupCombat(CombatState? state, string tag)
    {
        if (state == null)
        {
            return 0;
        }

        int removed = 0;
        try
        {
            List<Creature> warGods = state.PlayerCreatures
                .Where((creature) => creature.Player != null && LocalSelfCoopContext.IsTemporarySeat(creature.Player.NetId))
                .ToList();
            foreach (Creature creature in warGods)
            {
                // ⚠ **逐项容错（r260 修）**：`RemoveWarGodFromCombat` 链上会调 `NCombatRoom.RemoveCreatureNode`，
                // 而**第三方布局补丁**会在那条链上抛 —— 2026-10-10（r259 实机）铁证：
                //   `[地狱战神] 战后清理异常: error=System.InvalidOperationException: Sequence contains no matching element`
                //   `at NinjaSlayer.Code.Patches.YamotoKokiAllyLayoutPatch.Reflow(NCombatRoom room)`
                //   `at NCombatRoom.RemoveCreatureNode_Patch1(...)` ← 第三方（NinjaSlayer）的 `PositionPlayersAndPets` 后置
                // ⇒ 原来一个 catch 包整圈：**只要炸一次，循环就整体中断**，后面的战神全部没摘掉、
                //   一直留到发奖时刻（那局就靠 R12 的排除闸门兜住了：`R12 战后奖励遇到临时席位并已排除…player=4097`）。
                // ⇒ 改成"一个一个来，谁炸谁记，别影响剩下的"。
                // 注：临时席位登记列表清空不影响判据 —— `IsTemporarySeat` 还认 POC 号段（0x1000..0x1FFF），
                //     所以"没摘成功、还留在战斗里"的那个依然是临时席位。
                try
                {
                    RemoveWarGodFromCombat(state, creature, tag);
                    removed++;
                }
                catch (Exception exception)
                {
                    LocalMultiControlLogger.Warn(
                        $"{tag} 摘除战神失败（已跳过它、继续摘剩下的 —— 第三方布局补丁常在这一链上抛）: "
                        + $"seat={creature.Player?.NetId}, error={exception.Message}");
                }
            }

            if (removed > 0)
            {
                RemoveAllTemporarySeatsFromSession("combat-cleanup", tag);
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"{tag} 战后清理异常: error={exception}");
        }

        // R10：本场已挂过守卫 Power 的绑主名单一并清空（Power 自身会在战斗结束那刻摘掉；
        // 这里只清"我们这边的去重账"）。
        _guardAppliedSummoners.Clear();

        return removed;
    }

    /// <summary>退局兜底：清掉所有在册战神与会话里的临时席位登记（由 <c>LocalMultiControlRuntime.OnRunCleanup</c> 调用）。</summary>
    internal static void ResetRun(string tag, string reason)
    {
        if (_warGods.Count == 0 && LocalSelfCoopContext.TemporarySeatIds.Count == 0)
        {
            return;
        }

        try
        {
            // 战斗结束那条路会清（`CleanupCombat`）；这里兜"中途退局 / 战斗没走完奖励就没了的"漏，
            // 顺手把战神卡组牌从 run 卡牌表除名（`_warGods` 是那批 Player 的唯一持有者）。
            foreach (Registration registration in _warGods.ToList())
            {
                DetachFromRun(registration.Player, tag);
            }

            _warGods.Clear();
            _guardAppliedSummoners.Clear();
            RemoveAllTemporarySeatsFromSession(reason, tag);
            LocalMultiControlLogger.Info($"{tag} 已复位: reason={reason}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"{tag} 复位异常: reason={reason}, error={exception}");
        }
    }

    /// <summary>是否还有在册战神（退局复位时判"要不要打日志"用）。</summary>
    internal static bool HasTrackedWarGods => _warGods.Count > 0;

    private static void AddTemporarySeatToSession(ulong seatId, string tag)
    {
        // ⚠ **只登记进独立集合**，绝不塞进 `_localPlayerIds`（那张表是全表消费者的取数点：
        // 切人/前台、第三方 IsMe 放行、大厅、存档；2026-10-08 实测把临时席位追加进去会卡死回合链）。
        LocalSelfCoopContext.AddTemporarySeat(seatId);
        LocalMultiControlLogger.Info(
            $"{tag} 临时席位已登记（独立集合，**不进正式席位表**）: seat={seatId}, "
            + $"临时席位=[{string.Join(",", LocalSelfCoopContext.TemporarySeatIds)}], "
            + $"正式席位表=[{string.Join(",", LocalSelfCoopContext.LocalPlayerIds)}]");
    }

    private static void RemoveAllTemporarySeatsFromSession(string reason, string tag)
    {
        int removed = LocalSelfCoopContext.RemoveAllTemporarySeats();
        if (removed > 0)
        {
            LocalMultiControlLogger.Info(
                $"{tag} 临时席位已注销: reason={reason}, 移除={removed}, "
                + $"正式席位表=[{string.Join(",", LocalSelfCoopContext.LocalPlayerIds)}]");
        }
    }
}
