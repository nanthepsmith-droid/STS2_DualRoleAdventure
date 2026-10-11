#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using LocalMultiControl.Scripts.Models.Relics;
using LocalMultiControl.Scripts.Patch;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 战灵召唤（原 ④「地狱战神」，#21）**Phase 2 = 召唤闭环**（R3 / R4 / R7）+ **Phase 2b = 两个大招**
/// （R16【无尽战神】/ R17【终极九连环】）的编排层。
///
/// 一条回合开始的路：
/// <list type="number">
/// <item><b>挂点</b>：既有补丁 <c>CombatManagerSetupPlayerTurnForegroundPatch</c> 调
///   <see cref="OnPlayerTurnStart"/> ⇒ **不新增 Harmony 目标**；</item>
/// <item><b>判据</b>：真人席位 + 有耳环 + 本回合没弹过 + **至少有一个买得起的选项** + 未满 12 人
///   ⇒ 延后一帧弹<b>选择 UI</b>（复用 <see cref="LocalWakuuChoiceOverlay"/>，
///   并压制瓦库自动作答 + 钉住归属者两道）；</item>
/// <item><b>选项</b>（见 <see cref="BuildSummonOptions"/>）：**R9 复活**（Phase 4：本席位名下已死的战神，
///   成本 = 原档位召唤成本）→ 四档单点召唤（普通 1 / 罕见 2 / 稀有 3 / 先古 4）
///   +【无尽战神】(整包 8·本场预算·随机补满) +【终极九连环】(门槛 12·**永久扣计数上限**·固定 9 个、按实际数量计费)；</item>
/// <item><b>生成</b>：<see cref="HellwarWarGodFactory"/>（D7 预设档：普通 = 召唤者角色初始遗物 + 卡组；
///   罕见 / 稀有 / 先古 = 预设表里的原版角色皮 + 卡组全升级，见 <see cref="HellwarWarGodPresets"/>）；</item>
/// <item><b>战后清理 + 自动补满</b>：<see cref="CleanupBeforeRewards"/> 把战神摘出战斗（必须早于发奖）
///   并把"本场已花"清零（R3：仅战斗内消耗、战后恢复）；【九连环】扣掉的上限**不回补**（永久）。</item>
/// </list>
///
/// ⚠ **Phase 3（R11）起战神由瓦库托管链自动出牌**（`LocalWakuuRelicRuntime.IsAutoplayHosted` 认临时席位）：
/// 回合开始不再立即收口，手牌打光后由 <see cref="HellwarWarGodFactory.TryEndIdleWarGodTurns"/>（每帧 tick）ready。
/// （POC 入口 <see cref="HellwarPocRuntime"/> 仍是"回合开始立即收口"的哑战神。）
/// ⚠ 开关 = 设置页「战灵召唤」（`hellwarGod`，**默认关**）+ 本地多控单副本会话；关着一律立即返回。
/// </summary>
internal static class HellwarSummonCombatRuntime
{
    /// <summary>Phase 2 日志前缀（与 POC 的 `[地狱战神POC]` 区分判读）。</summary>
    private const string LogTag = "[地狱战神]";

    /// <summary>召唤选择里"一个可选项"的种类。</summary>
    private enum SummonKind
    {
        /// <summary>四档里的某一档，消耗"本场预算"召唤 1 个。</summary>
        SingleTier,

        /// <summary>R16【无尽战神】：整包价、随机取档、补满空位。</summary>
        Endless,

        /// <summary>R17【终极九连环】：整包价（**永久扣上限**）、固定先古、部分成功。</summary>
        NineChain,

        /// <summary>R9（Phase 4）：把本席位名下**已死的战神复活**（消耗该档位成本、恢复满血）。</summary>
        Revive,
    }

    /// <summary>一个可选项（下标与弹层里那一行一一对应）。</summary>
    private sealed record SummonOption(
        SummonKind Kind,
        HellwarWarGodTier Tier,
        int Cost,
        int Count,
        bool PermanentCost,
        string Label,
        ulong TargetSeatId = 0UL);

    /// <summary>判定"本回合已经弹过选择"的键（战斗实例 + 席位 + 该席位的回合号）。</summary>
    private static CombatState? _offerContext;

    private static ulong _offerSeatId;

    private static int _offerTurnNumber;

    /// <summary>上一次见到的 <see cref="CombatState"/>（换引用 = 换了一场战斗 ⇒ 兜底复位）。</summary>
    private static CombatState? _lastCombatState;

    /// <summary>本场战斗【无尽战神】是否已用过（D15：每场各限 1 次）。</summary>
    private static bool _endlessUsedThisCombat;

    /// <summary>本场战斗【终极九连环】是否已用过（D15：每场各限 1 次）。</summary>
    private static bool _nineChainUsedThisCombat;

    /// <summary>功能是否生效（设置页开关 + 本地多控单副本会话）。</summary>
    internal static bool Enabled
        => LocalWakuuAutopilotConfig.HellwarGod
        && LocalSelfCoopContext.IsEnabled
        && LocalSelfCoopContext.UseSingleAdventureMode;

    /// <summary>
    /// 玩家回合开始时（由既有补丁调用）。返回 true = 这是**临时席位（战神）的回合**，
    /// 调用方应立即返回、别把它当前台（与 POC 共用同一收口实现）。
    /// </summary>
    internal static bool OnPlayerTurnStart(Player? player, string source)
    {
        // Phase 3（R11）：战神的回合**不再在这里立即收口** —— 先由瓦库托管链出牌，
        // 手牌打光后再由 <see cref="HellwarWarGodFactory.TryEndIdleWarGodTurns"/>（每帧 tick）替它 ready。
        // 这里只负责"别把它当前台"（返回 true ⇒ 调用方立即 return）。
        if (player != null && LocalSelfCoopContext.IsTemporarySeat(player.NetId))
        {
            LocalMultiControlLogger.Info(
                $"{LogTag} 战神回合开始：交由瓦库托管链出牌（Phase 3）: seat={player.NetId}, source={source}");
            return true;
        }

        CombatState? state = CombatManager.Instance?.DebugOnlyGetState();
        if (state == null)
        {
            return false;
        }

        // 换战斗的兜底复位：正常路径由 `CleanupBeforeRewards` 在发奖前清干净（幂等），
        // 这里兜"上一场没走奖励路径就结束了"（残留在册战神 / 临时席位登记 / 没补满的计数预算 / 大招次数）。
        if (!ReferenceEquals(state, _lastCombatState))
        {
            _lastCombatState = state;
            HellwarWarGodFactory.ResetRun(LogTag, "new-combat");
            RefillAllLocalEarrings(state.RunState, "new-combat");
            ResetOfferState();
            ResetUltimateUsage();
        }

        if (!Enabled || player == null)
        {
            return false;
        }

        if (!(CombatManager.Instance?.IsInProgress ?? false))
        {
            return false;
        }

        // 只给**真人本地席位**：瓦库托管席位首版不玩（R14 / D8），第三方席位不归我们管。
        if (!LocalSelfCoopContext.IsLocalSessionSeat(player.NetId)
            || LocalSelfCoopContext.IsWakuuEnabled(player.NetId))
        {
            return false;
        }

        LocalWakuuHellwarEarring? earring = HellwarSummonRuntime.FindEarring(player);
        int available = earring?.AvailableSpirit ?? 0;
        int minCost = HellwarSummonPolicy.ResolveSummonCost(HellwarSummonPolicy.SelectableTiers[0]);
        int combatPlayers = state.Players.Count;
        bool offered = WasOfferedThisTurn(state, player);
        bool alive = player.Creature != null && player.Creature.IsAlive;

        // 「至少有一个买得起的选项」= 四档单点 + 两个大招（大招要考虑"本场已用"与**上限**）；
        // 一条都没有就**安静跳过**（不弹空弹层，见 references 坑 AE-2）。
        List<SummonOption> options = earring != null && alive
            ? BuildSummonOptions(player, state, earring)
            : new List<SummonOption>();
        // 预设战神 r270 修（实机 2026-10-11）：**满员（12/12）时只保留「复活」项**。
        // 病灶：R9 的尸体**留场**（仍在 `state.Players` 里占名额）⇒ 无尽战神召满 12 后战神死一堆，
        // `combatPlayers >= MaxLocalPlayerCount` 把**整个弹层**（含复活选项）挡死 ⇒「有计数但复活选项不弹」。
        // 复活是把尸体回填、**不新增席位** ⇒ 不该受满员限制；四档/无尽/九连环会新增席位 ⇒ 满员时照旧挡。
        bool partyFull = combatPlayers >= LocalSelfCoopContext.MaxLocalPlayerCount;
        List<SummonOption> offerable = FilterOfferable(options, partyFull);

        string skipReason = !alive
            ? "creature-dead"
            : offerable.Count == 0
                ? options.Count > 0 && partyFull
                    ? $"party-full({combatPlayers}>={LocalSelfCoopContext.MaxLocalPlayerCount}, 无可复活尸体)"
                    : HellwarSummonPolicy.DescribeOfferSkipReason(
                        Enabled, true, earring != null, offered, available, minCost,
                        combatPlayers, LocalSelfCoopContext.MaxLocalPlayerCount)
                : offered
                    ? "already-offered-this-turn"
                    : partyFull
                        ? "party-full-but-revive"
                        : "offer";

        if (!alive
            || !Enabled
            || earring == null
            || offered
            || offerable.Count == 0)
        {
            if (earring != null)
            {
                LocalMultiControlLogger.Info(
                    $"{LogTag} 本回合不弹召唤选择: player={player.NetId}, source={source}, reason={skipReason}, "
                    + $"可用={available}/上限={earring.SpiritCount}, 战斗内玩家={combatPlayers}, "
                    + $"无尽已用={_endlessUsedThisCombat}, 九连环已用={_nineChainUsedThisCombat}");
            }

            return false;
        }

        MarkOfferedThisTurn(state, player);
        Callable.From(() => PromptSummonDeferred(player, state, source, partyFull)).CallDeferred();
        return false;
    }

    /// <summary>
    /// 满员（<c>Players.Count ≥ MaxLocalPlayerCount</c>）时，把会**新增席位**的选项（四档单点 / 【无尽战神】/
    /// 【九连环】）过滤掉，只留 **R9 复活**（尸体回填，不新增名额）。不满员时原样返回。
    /// </summary>
    private static List<SummonOption> FilterOfferable(List<SummonOption> options, bool partyFull)
    {
        if (!partyFull)
        {
            return options;
        }

        return options.Where((option) => option.Kind == SummonKind.Revive).ToList();
    }

    /// <summary>
    /// 组出这次可以点的选项（下标 = 弹层里的行号）。
    /// 顺序：**R9 复活**（止损优先）→ 四档单点（成本从低到高）→【无尽战神】→【终极九连环】。
    /// </summary>
    private static List<SummonOption> BuildSummonOptions(
        Player owner, CombatState state, LocalWakuuHellwarEarring earring)
    {
        List<SummonOption> options = new();
        int available = earring.AvailableSpirit;
        int cap = earring.SpiritCount;
        int combatPlayers = state.Players.Count;
        int maxPlayers = LocalSelfCoopContext.MaxLocalPlayerCount;
        int freeSlots = HellwarSummonPolicy.ResolveFreeSlots(combatPlayers, maxPlayers);

        // R9（Phase 4）：本席位名下"已死但尸体还留场"的战神 ⇒ 复活选项（放在最上面：它是"止损"动作）。
        // 成本 = 该战神**原档位**的召唤成本（本场预算）；买不起就不出现（与四档同口径，坑 AE-2）。
        List<Player> deadWarGods = HellwarWarGodFactory.CollectDeadWarGods(state, owner.NetId);
        bool multipleDead = deadWarGods.Count > 1;
        foreach (Player dead in deadWarGods)
        {
            HellwarWarGodTier deadTier = HellwarWarGodFactory.TierOf(dead);
            int reviveCost = HellwarSummonPolicy.ResolveReviveCost(deadTier);
            if (available < reviveCost)
            {
                continue;
            }

            options.Add(new SummonOption(
                SummonKind.Revive, deadTier, reviveCost, 1, PermanentCost: false,
                LocalModText.HellwarReviveOptionLabel(
                    LocalModText.HellwarTierName(deadTier), reviveCost, multipleDead ? dead.NetId : 0UL),
                dead.NetId));
        }

        foreach (HellwarWarGodTier tier in HellwarSummonPolicy.SelectableTiers)
        {
            int cost = HellwarSummonPolicy.ResolveSummonCost(tier);
            if (available < cost)
            {
                continue;
            }

            options.Add(new SummonOption(
                SummonKind.SingleTier, tier, cost, 1, PermanentCost: false,
                LocalModText.HellwarSummonOptionLabel(LocalModText.HellwarTierName(tier), cost)));
        }

        if (HellwarSummonPolicy.CanOpenEndlessWarGod(available, freeSlots, _endlessUsedThisCombat))
        {
            int count = HellwarSummonPolicy.ResolveEndlessSummonCount(combatPlayers, maxPlayers);
            if (count > 0)
            {
                options.Add(new SummonOption(
                    SummonKind.Endless, HellwarWarGodTier.Common,
                    HellwarSummonPolicy.EndlessWarGodBundleCost, count, PermanentCost: false,
                    LocalModText.HellwarEndlessOptionLabel(count, HellwarSummonPolicy.EndlessWarGodBundleCost)));
            }
        }

        if (HellwarSummonPolicy.CanOpenNineChain(cap, freeSlots, _nineChainUsedThisCombat))
        {
            // 固定 9 个（只有栏位不足才截断）；消耗按**实际召唤数**折算整包价（2026-10-09 三轮修订）。
            int count = HellwarSummonPolicy.ResolveNineChainSummonCount(freeSlots);
            int cost = HellwarSummonPolicy.ResolveNineChainSpiritCost(count);
            if (count > 0)
            {
                options.Add(new SummonOption(
                    SummonKind.NineChain, HellwarWarGodTier.Ancient, cost, count, PermanentCost: true,
                    LocalModText.HellwarNineChainOptionLabel(count, cost)));
            }
        }

        return options;
    }

    /// <summary>延后一帧弹层（等本回合建立完成；期间玩家可能已经切前台 / 出牌）。</summary>
    private static void PromptSummonDeferred(Player owner, CombatState state, string source, bool partyFull)
    {
        TaskHelper.RunSafely(PromptSummonAsync(owner, state, source, partyFull));
    }

    private static async Task PromptSummonAsync(Player owner, CombatState state, string source, bool partyFull)
    {
        try
        {
            if (!Enabled || !(CombatManager.Instance?.IsInProgress ?? false))
            {
                return;
            }

            LocalWakuuHellwarEarring? earring = HellwarSummonRuntime.FindEarring(owner);
            if (earring == null)
            {
                return;
            }

            // 延迟一帧后重算满员（期间战神可能死 / 复活），并再次过滤 —— 与回合开始判据同一口径。
            partyFull = state.Players.Count >= LocalSelfCoopContext.MaxLocalPlayerCount;
            List<SummonOption> options = FilterOfferable(BuildSummonOptions(owner, state, earring), partyFull);
            if (options.Count == 0)
            {
                LocalMultiControlLogger.Info(
                    $"{LogTag} 召唤选择未弹出：没有可选项。player={owner.NetId}, "
                    + $"可用={earring.AvailableSpirit}/上限={earring.SpiritCount}, 满员={partyFull}");
                return;
            }

            List<string> labels = options.Select((option) => option.Label).ToList();
            int before = earring.AvailableSpirit;
            LocalMultiControlLogger.Info(
                $"{LogTag} 回合开始可召唤，弹出选择: player={owner.NetId}, source={source}, "
                + $"选项={options.Count} 个, 可用={before}/上限={earring.SpiritCount}, "
                + $"战斗内玩家={state.Players.Count}/{LocalSelfCoopContext.MaxLocalPlayerCount}, "
                + $"无尽已用={_endlessUsedThisCombat}, 九连环已用={_nineChainUsedThisCombat}"
                + (partyFull ? "（满员：只提供复活选项）" : ""));

            int? index;
            // 两道防护（与炼化 / 净化同款，坑 V/K）：归属者钉成召唤者、压制瓦库作用域外自动作答。
            using (CardSelectForegroundSwitchPatch.PushChoiceOwner(owner.NetId))
            using (CardSelectWakuuTurnStartAutoAnswerPatch.SuppressForHumanChoice())
            {
                index = await LocalWakuuChoiceOverlay.PickIndexAsync(LocalModText.HellwarSummonPickTitle, labels);
            }

            if (index == null || index.Value < 0 || index.Value >= options.Count)
            {
                LocalMultiControlLogger.Info(
                    $"{LogTag} 召唤选择已取消：本回合不召唤。player={owner.NetId}, index={index?.ToString() ?? "null"}");
                return;
            }

            SummonOption chosen = options[index.Value];
            LocalMultiControlLogger.Info(
                $"{LogTag} 召唤选择已确定: player={owner.NetId}, 种类={chosen.Kind}, 档位={chosen.Tier}, "
                + $"成本={chosen.Cost}(永久={chosen.PermanentCost}), 预计召唤={chosen.Count} 个");

            switch (chosen.Kind)
            {
                case SummonKind.SingleTier:
                    // 预设战神 v2（Phase B，r273）：单点召唤在选完**档位**之后，再弹**第二层「选角色预设」**。
                    HellwarWarGodPreset? pickedPreset = await PickTierPresetAsync(owner, state, chosen.Tier);
                    if (pickedPreset == null)
                    {
                        LocalMultiControlLogger.Info(
                            $"{LogTag} 预设选择已取消：本次召唤不执行（计数未扣）。player={owner.NetId}, 档位={chosen.Tier}");
                        break;
                    }

                    await RunSingleSummonAsync(owner, state, earring, chosen, pickedPreset);
                    break;
                case SummonKind.Endless:
                    await RunEndlessWarGodAsync(owner, state, earring, chosen);
                    break;
                case SummonKind.NineChain:
                    await RunNineChainAsync(owner, state, earring, chosen);
                    break;
                case SummonKind.Revive:
                    await RunReviveAsync(owner, state, earring, chosen);
                    break;
                default:
                    LocalMultiControlLogger.Warn($"{LogTag} 未适配的召唤种类（已忽略）: {chosen.Kind}");
                    break;
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"{LogTag} 召唤流程异常（已中止）: player={owner.NetId}, error={exception}");
        }
    }

    /// <summary>
    /// 预设战神 v2（Phase B，r273）：单点召唤选定**档位**之后，弹**第二层「选角色预设」**，
    /// 返回玩家点名的那套预设。取消 / 拿不到 ⇒ <c>null</c>（调用方**不扣计数**、整次召唤不执行）。
    ///
    /// <para>口径（用户拍板 ①②③④）：</para>
    /// <list type="bullet">
    /// <item>候选 = 「自动」+（候选 &gt; 1 时）「随机」+ 具体预设（普通档 = **全部角色**；其余档 = 内置默认 + 文件预设）；</item>
    /// <item>具体候选 ≤ 1 条 ⇒ **不弹**（直接返回该档默认预设，避免"每次都要多点一下"）；</item>
    /// <item>随机 = 从该档**全部具体候选**里取（含文件预设）。</item>
    /// </list>
    /// </summary>
    private static async Task<HellwarWarGodPreset?> PickTierPresetAsync(
        Player owner, CombatState state, HellwarWarGodTier tier)
    {
        HellwarWarGodPreset fallback = LocalHellwarPresetStore.ResolveDefault(tier);
        IReadOnlyList<HellwarWarGodPreset> concrete = HellwarPresetCatalog.ConcreteFor(tier);
        if (!HellwarPresetChoices.ShouldOfferPicker(concrete))
        {
            return fallback;
        }

        IReadOnlyList<HellwarPresetPickOption> options = HellwarPresetChoices.BuildPickOptions(fallback, concrete);
        List<string> labels = options
            .Select((option) => HellwarPresetCatalog.DescribePickOption(option))
            .ToList();
        LocalMultiControlLogger.Info(
            $"{LogTag} 弹出角色预设选择（第 2 步）: player={owner.NetId}, 档位={tier}, "
            + $"候选={concrete.Count} 套, 默认={HellwarPresetCatalog.DescribePresetShort(fallback)}");

        int? index;
        // 两道防护与第一层同款（归属者钉成召唤者、压制瓦库作用域外自动作答）。
        using (CardSelectForegroundSwitchPatch.PushChoiceOwner(owner.NetId))
        using (CardSelectWakuuTurnStartAutoAnswerPatch.SuppressForHumanChoice())
        {
            index = await LocalWakuuChoiceOverlay.PickIndexAsync(LocalModText.HellwarPresetPickTitle, labels);
        }

        if (index == null || index.Value < 0 || index.Value >= options.Count)
        {
            return null;
        }

        HellwarPresetPickOption chosen = options[index.Value];
        HellwarWarGodPreset preset = chosen.Kind switch
        {
            HellwarPresetPickKind.Random => RollRandomPreset(state, concrete, fallback),
            _ => chosen.Preset,
        };

        LocalMultiControlLogger.Info(
            $"{LogTag} 角色预设已选定: player={owner.NetId}, 档位={tier}, 种类={chosen.Kind}, "
            + $"{HellwarWarGodPresets.Describe(preset)}");
        return preset;
    }

    /// <summary>从本档全部候选里随机取一个（池为空 ⇒ 回落该档默认预设）。</summary>
    private static HellwarWarGodPreset RollRandomPreset(
        CombatState state, IReadOnlyList<HellwarWarGodPreset> concrete, HellwarWarGodPreset fallback)
    {
        try
        {
            int roll = state.RunState.Rng.CombatTargets.NextInt(Math.Max(concrete.Count, 1));
            return HellwarPresetChoices.PickRandom(concrete, roll) ?? fallback;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"{LogTag} 随机预设取值异常（回落默认预设）: {exception.Message}");
            return fallback;
        }
    }

    /// <summary>四档单点召唤：**先扣本场预算 → 生成 1 个 → 生成失败退回**。</summary>
    private static async Task RunSingleSummonAsync(
        Player owner, CombatState state, LocalWakuuHellwarEarring earring, SummonOption option,
        HellwarWarGodPreset preset)
    {
        if (!earring.TrySpendCombatSpirit(option.Cost, "summon"))
        {
            LocalMultiControlLogger.Warn(
                $"{LogTag} 召唤中止：消耗计数失败（可用不足）。player={owner.NetId}, "
                + $"档位={option.Tier}, 花费={option.Cost}, 可用={earring.AvailableSpirit}");
            return;
        }

        // 预设战神 v2（r269 起地基、r273 起由第二层「选预设」传入）：这一档要用哪套预设。
        int spawned = await SpawnWarGodsAsync(state, owner, new[] { preset }, option.Tier.ToString());
        if (spawned == 0)
        {
            earring.RefundCombatSpirit(option.Cost, "spawn-failed");
            LocalMultiControlLogger.Warn(
                $"{LogTag} 召唤失败：战神生成异常，计数已退回。player={owner.NetId}, 花费={option.Cost}");
            return;
        }

        LocalMultiControlLogger.Info(
            $"{LogTag} 已召唤战神: owner={owner.NetId}, 档位={option.Tier}, 花费={option.Cost}, "
            + $"可用={earring.AvailableSpirit}/上限={earring.SpiritCount}, "
            + $"战斗内玩家={state.Players.Count}/{LocalSelfCoopContext.MaxLocalPlayerCount}");
    }

    /// <summary>
    /// **R9（Phase 4）复活**：把绑主名下已死的战神救回来。
    /// 纪律与召唤同款（坑 AE-2）：**先扣本场预算 → 复活 → 失败退回**（含"找不到席位 / 复活抛异常"）。
    /// 具体复活动作在 <see cref="HellwarWarGodFactory.ReviveWarGodAsync"/>（治疗到满 + 重填战斗态 + 补登就绪 + 保命）。
    /// </summary>
    private static async Task RunReviveAsync(
        Player owner, CombatState state, LocalWakuuHellwarEarring earring, SummonOption option)
    {
        Player? warGod = HellwarWarGodFactory.FindSpawnedWarGod(option.TargetSeatId);
        if (warGod == null)
        {
            LocalMultiControlLogger.Warn(
                $"{LogTag} 复活中止：找不到该席位（可能已被清理）。player={owner.NetId}, 目标={option.TargetSeatId}");
            return;
        }

        if (!earring.TrySpendCombatSpirit(option.Cost, "revive"))
        {
            LocalMultiControlLogger.Warn(
                $"{LogTag} 复活中止：消耗计数失败（可用不足）。player={owner.NetId}, "
                + $"seat={warGod.NetId}, 花费={option.Cost}, 可用={earring.AvailableSpirit}");
            return;
        }

        bool revived = await HellwarWarGodFactory.ReviveWarGodAsync(warGod, state, LogTag);
        if (!revived)
        {
            earring.RefundCombatSpirit(option.Cost, "revive-failed");
            LocalMultiControlLogger.Warn(
                $"{LogTag} 复活失败：计数已退回。player={owner.NetId}, seat={warGod.NetId}, 花费={option.Cost}");
            return;
        }

        LocalMultiControlLogger.Info(
            $"{LogTag} 已复活战神: owner={owner.NetId}, seat={warGod.NetId}, 档位={option.Tier}, "
            + $"花费={option.Cost}, 可用={earring.AvailableSpirit}/上限={earring.SpiritCount}, "
            + $"战斗内玩家={state.Players.Count}/{LocalSelfCoopContext.MaxLocalPlayerCount}");
    }

    /// <summary>
    /// R16【无尽战神】：整包 8（**本场预算**）⇒ **随机**取「普通 / 罕见 / 稀有」三档之一（D17，先古不进池），
    /// **补满空位**（D18 截断）。生成的战神数量 = 选项里算好的 <c>Count</c>。
    /// </summary>
    private static async Task RunEndlessWarGodAsync(
        Player owner, CombatState state, LocalWakuuHellwarEarring earring, SummonOption option)
    {
        if (!earring.TrySpendCombatSpirit(option.Cost, "endless-war-god"))
        {
            LocalMultiControlLogger.Warn(
                $"{LogTag} 【无尽战神】中止：消耗计数失败。player={owner.NetId}, 可用={earring.AvailableSpirit}");
            return;
        }

        _endlessUsedThisCombat = true;

        // D17：每个补位各掷一次（"随机取档"而不是"整批同一档"）。
        // 随机池：默认 普通/罕见/稀有（先古是【九连环】专属）；开「无尽战神允许开出先古」则四档全进（2026-10-10）。
        // 预设战神 v2（r273）：池 = 内置档位表 ∪ hellwar_presets.json 挂到这几档的预设 —— 没文件时与旧行为逐条一致。
        bool includeAncient = LocalWakuuAutopilotConfig.HellwarEndlessAncient;
        IReadOnlyList<HellwarWarGodPreset> pool = HellwarPresetCatalog.EndlessPool(includeAncient);
        List<HellwarWarGodPreset> presets = new();
        for (int i = 0; i < option.Count; i++)
        {
            int roll = state.RunState.Rng.CombatTargets.NextInt(Math.Max(pool.Count, 1));
            presets.Add(
                HellwarPresetChoices.PickRandom(pool, roll)
                ?? HellwarWarGodPresets.PickRandom(roll, includeAncient));
        }

        int spawned = await SpawnWarGodsAsync(state, owner, presets, "endless-war-god");
        if (spawned == 0)
        {
            earring.RefundCombatSpirit(option.Cost, "endless-none-spawned");
            _endlessUsedThisCombat = false;
            LocalMultiControlLogger.Warn(
                $"{LogTag} 【无尽战神】失败：一个战神都没生成，计数已退回、本场次数已退还。player={owner.NetId}");
            return;
        }

        LocalMultiControlLogger.Info(
            $"{LogTag} 已开【无尽战神】: owner={owner.NetId}, 花费={option.Cost}（本场预算）, "
            + $"预计={option.Count} 实际={spawned} 个, 档位=[{string.Join(",", presets.Select((preset) => preset.Tier))}], "
            + $"随机池={(includeAncient ? "含先古" : "不含先古")}({pool.Count} 套), "
            + $"可用={earring.AvailableSpirit}/上限={earring.SpiritCount}, "
            + $"战斗内玩家={state.Players.Count}/{LocalSelfCoopContext.MaxLocalPlayerCount}");
    }

    /// <summary>
    /// R17【终极九连环】：**永久扣计数上限**（2026-10-09 用户二轮修订）⇒ 召唤**固定 9 个**先古
    /// （只有栏位不足才截断），**消耗按实际召唤数折算整包价**（三轮修订，见
    /// <see cref="HellwarSummonPolicy.ResolveNineChainSpiritCost"/>）。
    /// </summary>
    private static async Task RunNineChainAsync(
        Player owner, CombatState state, LocalWakuuHellwarEarring earring, SummonOption option)
    {
        int capBefore = earring.SpiritCount;
        if (!earring.TrySpendPermanentSpirit(option.Cost, "nine-chain"))
        {
            LocalMultiControlLogger.Warn(
                $"{LogTag} 【终极九连环】中止：永久扣计数上限失败。player={owner.NetId}, "
                + $"花费={option.Cost}, 上限={capBefore}");
            return;
        }

        _nineChainUsedThisCombat = true;

        // 预设战神 v2（r273）：池 = 先古档内置默认 ∪ hellwar_presets.json 的先古预设
        // （没文件时池里就一条内置先古 ⇒ 9 个全同，与旧行为一致）。
        IReadOnlyList<HellwarWarGodPreset> pool = HellwarPresetCatalog.NineChainPool();
        List<HellwarWarGodPreset> presets = new(option.Count);
        for (int i = 0; i < option.Count; i++)
        {
            int roll = state.RunState.Rng.CombatTargets.NextInt(Math.Max(pool.Count, 1));
            presets.Add(HellwarPresetChoices.PickRandom(pool, roll) ?? HellwarWarGodPresets.Ancient);
        }

        int spawned = await SpawnWarGodsAsync(state, owner, presets, "nine-chain");
        if (spawned == 0)
        {
            earring.RefundPermanentSpirit(option.Cost, "nine-chain-none-spawned");
            _nineChainUsedThisCombat = false;
            LocalMultiControlLogger.Warn(
                $"{LogTag} 【终极九连环】失败：一个先古都没生成，上限已退回、本场次数已退还。player={owner.NetId}");
            return;
        }

        // 少生了几个（生成失败 / 中途满员）⇒ **按实际数量**退差价（消耗口径 = round(12×实际/9)）。
        if (spawned < option.Count)
        {
            int actualCost = HellwarSummonPolicy.ResolveNineChainSpiritCost(spawned);
            int refund = option.Cost - actualCost;
            if (refund > 0)
            {
                earring.RefundPermanentSpirit(refund, "nine-chain-spawned-fewer");
            }

            LocalMultiControlLogger.Warn(
                $"{LogTag} 【终极九连环】少生了 {option.Count - spawned} 个（已按实际数量重新计费）: "
                + $"player={owner.NetId}, 花费={option.Cost}→{actualCost}, 退回={refund}");
        }

        LocalMultiControlLogger.Info(
            $"{LogTag} 已开【终极九连环】: owner={owner.NetId}, "
            + $"永久花费={option.Cost}, 上限={capBefore}→{earring.SpiritCount}, "
            + $"预计={option.Count} 实际={spawned} 个先古, 池={pool.Count} 套, 本场已花={earring.CombatSpentSpirit}, "
            + $"战斗内玩家={state.Players.Count}/{LocalSelfCoopContext.MaxLocalPlayerCount}");
    }

    /// <summary>
    /// 按 <paramref name="presets"/> 逐个生成战神（每步都重新校验"战斗还在进行 / 还有空位"），
    /// 返回实际成功个数。多个之间**错开一点点时间**（一次建 N 个立绘节点会掉帧，也让玩家看得见）。
    /// </summary>
    private static async Task<int> SpawnWarGodsAsync(
        CombatState state, Player owner, IReadOnlyList<HellwarWarGodPreset> presets, string source)
    {
        int spawned = 0;
        for (int i = 0; i < presets.Count; i++)
        {
            if (!(CombatManager.Instance?.IsInProgress ?? false))
            {
                LocalMultiControlLogger.Warn(
                    $"{LogTag} 批量召唤中断：战斗已不在进行中。source={source}, 已生成={spawned}/{presets.Count}");
                break;
            }

            int freeSlots = HellwarSummonPolicy.ResolveFreeSlots(
                state.Players.Count, LocalSelfCoopContext.MaxLocalPlayerCount);
            if (freeSlots <= 0)
            {
                LocalMultiControlLogger.Warn(
                    $"{LogTag} 批量召唤中断：已满 {LocalSelfCoopContext.MaxLocalPlayerCount} 席。"
                    + $"source={source}, 已生成={spawned}/{presets.Count}");
                break;
            }

            HellwarWarGodPreset preset = presets[i];
            CharacterModel? skin = ResolveSkin(preset, owner);
            if (skin == null)
            {
                LocalMultiControlLogger.Warn(
                    $"{LogTag} 跳过第 {i + 1} 个战神：拿不到角色模型。skin={preset.SkinKey}, source={source}");
                continue;
            }

            if (HellwarWarGodFactory.TrySpawn(state, skin, preset, LogTag, owner, out Player? warGod))
            {
                spawned++;
                LocalMultiControlLogger.Info(
                    $"{LogTag} 批量召唤进度: {i + 1}/{presets.Count}, seat={warGod?.NetId}, "
                    + $"档位={preset.Tier}, 战斗内玩家={state.Players.Count}");
            }

            if (i < presets.Count - 1)
            {
                await Cmd.CustomScaledWait(0.05f, 0.12f);
            }
        }

        return spawned;
    }

    /// <summary>
    /// 预设档的"角色皮" → 运行时 <see cref="CharacterModel"/>：
    /// <list type="bullet">
    /// <item><c>summoner</c> = 召唤者自身角色（普通档默认 ⇒ **自动兼容 mod 角色**）；</item>
    /// <item>三个历史别名（silent / ironclad / regent）= 内置预设表的原版角色（向后兼容旧配置）；</item>
    /// <item>**其余任意键 = 按 <c>CharacterModel.Id.Entry</c> 在 <see cref="ModelDb.AllCharacters"/> 里查**
    ///   （大小写不敏感；预设战神 v2 的"档位 × 角色预设"就靠这条吃任意角色）。
    ///   ⚠ 原版 <c>AllCharacters</c> 是硬编码数组，**mod 角色**要等摸清它们的注册机制后接入（TODO，
    ///   见 TODO.md §预设构筑器）；查不到 ⇒ 回退召唤者角色并 WARN（不崩、不中断召唤）。</item>
    /// </list>
    /// </summary>
    private static CharacterModel? ResolveSkin(HellwarWarGodPreset preset, Player owner)
    {
        if (preset.SkinKey == HellwarWarGodPresets.SkinOfSummoner)
        {
            return owner.Character;
        }

        try
        {
            switch (preset.SkinKey)
            {
                case HellwarWarGodPresets.SkinSilent:
                    return ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Silent>();
                case HellwarWarGodPresets.SkinIronclad:
                    return ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>();
                case HellwarWarGodPresets.SkinRegent:
                    return ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Regent>();
            }

            // 任意角色 id（大小写不敏感；预设表里写 NECROBINDER / DEFECT 这类原版角色）。
            CharacterModel? matched = ModelDb.AllCharacters.FirstOrDefault(
                (CharacterModel character) => string.Equals(
                    character.Id.Entry, preset.SkinKey, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                return matched;
            }

            LocalMultiControlLogger.Warn(
                $"{LogTag} 预设皮肤「{preset.SkinKey}」在角色表里查不到（mod 角色暂未接入），回退到召唤者角色: "
                + $"preset={preset.DisplayName}({preset.PresetId}), 可选原版="
                + $"{string.Join("/", ModelDb.AllCharacters.Select((CharacterModel character) => character.Id.Entry))}");
            return owner.Character;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"{LogTag} 预设皮肤取用失败，回退到召唤者角色: skin={preset.SkinKey}, error={exception.Message}");
            return owner.Character;
        }
    }

    /// <summary>
    /// 战后奖励发放**之前**（由既有补丁 <c>CombatRoomOfferRoomEndRewardsPatch</c> 调用 ⇒ 不新增 Harmony 目标）：
    /// ① 把战神摘出战斗（必须早于发奖，否则原版会遍历 `CombatState.Players` 也给战神发一份）；
    /// ② 把每个真人席位的"本场已花"清零 = **自动补满**（R3；【九连环】永久扣掉的上限**不**回补）；
    /// ③ 清掉两个大招的"本场已用"标记。
    /// </summary>
    internal static void CleanupBeforeRewards(CombatState? state)
    {
        if (!Enabled && !HellwarWarGodFactory.HasTrackedWarGods)
        {
            return;
        }

        int removed = HellwarWarGodFactory.CleanupCombat(state, LogTag);
        if (removed > 0)
        {
            LocalMultiControlLogger.Info($"{LogTag} 战后已清理战神: 移除={removed}, 剩余 Players={state?.Players.Count}");
        }

        RefillAllLocalEarrings(state?.RunState, "combat-cleanup");
        ResetOfferState();
        ResetUltimateUsage();
    }

    /// <summary>退局兜底（由 <c>LocalMultiControlRuntime.OnRunCleanup</c> 调用）。</summary>
    internal static void ResetForRun(string reason)
    {
        HellwarWarGodFactory.ResetRun(LogTag, reason);
        RefillAllLocalEarrings(null, reason);
        ResetOfferState();
        ResetUltimateUsage();
        _lastCombatState = null;
    }

    /// <summary>把每个本地真人席位的【地狱耳环】"本场已花"清零（战后 / 退局）。</summary>
    private static void RefillAllLocalEarrings(IRunState? runState, string source)
    {
        if (runState == null)
        {
            return;
        }

        try
        {
            foreach (ulong seatId in LocalSelfCoopContext.LocalPlayerIds)
            {
                Player? player = runState.GetPlayer(seatId);
                HellwarSummonRuntime.FindEarring(player)?.RefillCombatSpirit(source);
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"{LogTag} 自动补满计数失败（已忽略）: {exception.Message}");
        }
    }

    /// <summary>该席位在本回合是否已经弹过选择（键 = 战斗实例 + 席位 + 该席位的回合号）。</summary>
    private static bool WasOfferedThisTurn(CombatState state, Player player)
    {
        return ReferenceEquals(_offerContext, state)
            && _offerSeatId == player.NetId
            && _offerTurnNumber == ResolveTurnNumber(player);
    }

    private static void MarkOfferedThisTurn(CombatState state, Player player)
    {
        _offerContext = state;
        _offerSeatId = player.NetId;
        _offerTurnNumber = ResolveTurnNumber(player);
    }

    private static void ResetOfferState()
    {
        _offerContext = null;
        _offerSeatId = 0;
        _offerTurnNumber = 0;
    }

    private static void ResetUltimateUsage()
    {
        _endlessUsedThisCombat = false;
        _nineChainUsedThisCombat = false;
    }

    /// <summary>该席位自己的回合号（`PlayerCombatState.TurnNumber` 从 1 起；读不到兜 0 ⇒ 每回合都会重判）。</summary>
    private static int ResolveTurnNumber(Player player)
    {
        try
        {
            return player.PlayerCombatState?.TurnNumber ?? 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
