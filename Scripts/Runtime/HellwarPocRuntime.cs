#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 战灵召唤 **Phase 0 POC** 运行时：在战斗里塞一个"哑战神"（战斗内临时玩家），**只验证三件事** ——
/// ① 立绘 / 血条出现；② 回合能正常推进（临时席位被"自动补齐敌方回合就绪"认领）；③ 战斗结束能清理干净、
/// 且不会被 <c>LocalContext</c> 当成"我"。
///
/// ⚠ 这是 **POC**：不含遗物【地狱耳环】/ 计数 / 休息处【分裂精神】 / 召唤选择 UI / 预设卡组
/// （那些是 Phase 1 / Phase 2，见设计文档 #21 §8）。开关 = 环境变量 <c>LMC_HELLWAR_POC=1</c>
/// （**默认关**，不开时本类完全静默、不影响正常游玩）。
///
/// 建"有立绘的临时玩家"的正确顺序（源码依据：设计文档 #21 §5.1 / §5.3 / §11.3）：
/// <code>
/// Player.CreateForNewRun(character, unlockState, tempSeatId)   // 内含 PopulateStartingInventory
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
internal static class HellwarPocRuntime
{
    /// <summary>POC 已生成的战神（用于清理：退战斗 / 退 run 卡牌表 / 席位号段分配；不跨局残留）。</summary>
    private static readonly List<Player> _spawnedWarGods = new();

    /// <summary>本场战斗的 <see cref="CombatState"/> 引用（换战斗 = 引用变 ⇒ 复位"本场已生成"标记）。</summary>
    private static CombatState? _lastState;

    /// <summary>本场战斗是否已生成过（POC = 每场战斗一次）。</summary>
    private static bool _spawnedThisCombat;

    /// <summary>开关解析结果缓存（进程级，改完需重启游戏才变）。</summary>
    private static bool? _resolvedEnabled;

    /// <summary>开关来源（打日志用）。</summary>
    private static string _enabledSource = "未开启";

    /// <summary>标志文件路径：`%APPDATA%\SlayTheSpire2\hellwar_poc.enabled`（**与 Steam 启动方式无关**）。</summary>
    internal static string FlagFilePath => Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "SlayTheSpire2",
        HellwarPocPolicy.FlagFileName);

    /// <summary>POC 开关：**环境变量 <c>LMC_HELLWAR_POC</c>** 或 **标志文件存在** 任一为真即开。</summary>
    internal static bool Enabled => _resolvedEnabled ??= ResolveEnabled();

    /// <summary>当前开关来源（"环境变量 …" / "标志文件 …" / "未开启"）。</summary>
    internal static string EnabledSource => _enabledSource;

    private static bool ResolveEnabled()
    {
        bool byEnv = HellwarPocPolicy.ParseEnvFlag(
            System.Environment.GetEnvironmentVariable(HellwarPocPolicy.EnvVarName));

        string? fileContent = null;
        try
        {
            string path = FlagFilePath;
            if (File.Exists(path))
            {
                fileContent = File.ReadAllText(path);
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[地狱战神POC] 读标志文件失败，按未开启处理: error={exception.Message}");
        }

        bool byFile = HellwarPocPolicy.ParseFlagFile(fileContent);
        if (byEnv)
        {
            _enabledSource = $"环境变量 {HellwarPocPolicy.EnvVarName}";
        }
        else if (byFile)
        {
            _enabledSource = $"标志文件 {FlagFilePath}";
        }

        return byEnv || byFile;
    }

    /// <summary>
    /// 启动时把开关状态打进日志。
    /// ⚠ 这条**必须常打**（开关关着也打）：否则"变量没配对"与"功能没生效"在日志上完全一样，
    /// 实机根本分不清（POC 阶段就这么判读）。
    /// </summary>
    internal static void LogSwitchStateAtStartup()
    {
        if (Enabled)
        {
            LocalMultiControlLogger.Info(
                $"[地狱战神POC] 开关状态: on（来源={EnabledSource}）"
                + " —— POC 已启用：本地多控会话的战斗里会生成「哑战神」");
            return;
        }

        LocalMultiControlLogger.Info(
            $"[地狱战神POC] 开关状态: off（环境变量 {HellwarPocPolicy.EnvVarName} 未设置/off；标志文件不存在）"
            + $" —— 要开 POC：新建空文件 {FlagFilePath}（删掉即关）");
    }

    /// <summary>
    /// 玩家回合开始时的入口（由既有补丁 <c>CombatManagerSetupPlayerTurnForegroundPatch</c> 调用 ⇒ **不新增 Harmony 目标**）。
    /// 不在这里直接生成：回合建立过程中往 <c>CombatState.Players</c> 加人会扰动回合循环 ⇒ 延后一帧。
    /// </summary>
    /// <returns>
    /// true = 「这是**临时席位**（战神）的回合，调用方应当**立即返回**」—— 战神没有手牌 UI、也永远不进前台，
    /// 别让随后那段前台/能量/视角逻辑把它当前台切来切去（`TryEnsureForegroundForPlayer` 只认本地席位，
    /// 但"要不要 peek / 要不要重评按钮"那些判断没必要让它跑）。false = 正常席位（或功能没开）。
    /// </returns>
    internal static bool OnPlayerTurnStart(Player? player, string source)
    {
        if (!Enabled)
        {
            return false;
        }

        // 战神自己的回合：POC 阶段它不抽牌不出牌，**必须有人替它结束回合** ——
        // 否则 `state.Players` 里这一席永远不 ready ⇒ 回合链卡死（实测：不能出牌 / 不能结束回合 /
        // 不能切人 / 瓦库不打牌 / 开局只抽 1 张，全是这一个根因）。延后一帧，等回合建立完成再收。
        if (player != null && LocalSelfCoopContext.IsTemporarySeat(player.NetId))
        {
            Player warGod = player;
            Callable.From(() => EndTemporarySeatTurn(warGod, source)).CallDeferred();
            return true;
        }

        CombatState? state = CombatManager.Instance?.DebugOnlyGetState();
        if (state == null)
        {
            return false;
        }

        if (!ReferenceEquals(state, _lastState))
        {
            // 换了一场战斗 ⇒ 复位"本场已生成"（POC 每场最多一个）。
            _lastState = state;
            _spawnedThisCombat = false;
            LocalMultiControlLogger.Info(
                $"[地狱战神POC] 进入新战斗，已复位本场生成标记: source={source}, 已有战神={CountWarGods(state)} 个");
        }

        if (_spawnedThisCombat)
        {
            return false;
        }

        bool live = CombatManager.Instance?.IsInProgress ?? false;
        int alive = CountWarGods(state);
        if (!HellwarPocPolicy.ShouldSpawn(Enabled, LocalSelfCoopContext.IsEnabled, live, _spawnedThisCombat, alive))
        {
            LocalMultiControlLogger.Info(
                $"[地狱战神POC] 本轮不生成: source={source}, player={player?.NetId}, "
                + $"reason={HellwarPocPolicy.DescribeSkipReason(Enabled, LocalSelfCoopContext.IsEnabled, live, _spawnedThisCombat, alive)}");
            return false;
        }

        Callable.From(() => SpawnDeferred(state, source)).CallDeferred();
        return false;
    }

    /// <summary>延后一帧执行真正的生成（此时回合已建立、战斗必然在进行中）。</summary>
    private static void SpawnDeferred(CombatState state, string source)
    {
        // 异常回滚用：**半成品战神留在 `state.Players` 里 = 全场卡死**（r233~r237 连踩），
        // 所以任何一步炸了都要尽力把它撤出战斗，而不是留在里面。
        Player? rollbackCandidate = null;
        try
        {
            if (_spawnedThisCombat || CountWarGods(state) > 0)
            {
                LocalMultiControlLogger.Info($"[地狱战神POC] 生成已跳过（延后期间已存在）: source={source}");
                return;
            }

            if (!(CombatManager.Instance?.IsInProgress ?? false))
            {
                LocalMultiControlLogger.Warn($"[地狱战神POC] 生成失败：战斗已不在进行中: source={source}");
                return;
            }

            ulong seatId = HellwarPocPolicy.SeatIdAt(_spawnedWarGods.Count);
            CharacterModel? character = ResolveCharacterForPoc(state);
            if (character == null)
            {
                LocalMultiControlLogger.Warn($"[地狱战神POC] 生成失败：拿不到可用角色模型: source={source}");
                return;
            }

            UnlockState unlock = state.RunState.UnlockState;
            Player player = Player.CreateForNewRun(character, unlock, seatId);
            rollbackCandidate = player;
            LocalMultiControlLogger.Info(
                $"[地狱战神POC] ① 已建临时玩家: seat={seatId}(0x{seatId:X}), character={character.Id}, "
                + $"hp={player.Creature.CurrentHp}/{player.Creature.MaxHp}");

            // ①b ★★ 必做（r237 实测漏了它 = NullReferenceException ⇒ 全场卡死）：见 AttachToRun 注释。
            AttachToRun(player, state);

            state.AddPlayer(player);
            LocalMultiControlLogger.Info(
                $"[地狱战神POC] ② 已加入战斗（数据层）: seat={seatId}, Players={state.Players.Count}, "
                + $"Allies={state.Allies.Count}（此刻还没有立绘节点）");

            // ★★ 必做（r236 实测漏了它就 turn loop died）：照抄游戏开战时的两件事建立**战斗态** ——
            // `PlayerCombatState`（手牌/抽牌堆/能量 …）是 `ResetCombatState` 建的，卡组克隆进抽牌堆是
            // `PopulateCombatState` 做的。**漏掉的后果不是"战神自己不好用"**，而是
            // `CombatHistoryEntry` 构造时遍历**全体玩家**读 `player.PlayerCombatState.TurnNumber`
            // ⇒ 战神那个 null 会让**真人抽牌**直接 `NullReferenceException` ⇒ `turn loop died`（全场卡死）。
            player.ResetCombatState();
            player.PopulateCombatState(player.RunState.Rng.Shuffle, state);
            LocalMultiControlLogger.Info(
                $"[地狱战神POC] ②b 已建立战斗态（PlayerCombatState + 抽牌堆）: seat={seatId}, "
                + $"卡组={player.Deck.Cards.Count} 张（= 角色初始卡组，D7「普通档」）");

            AddTemporarySeatToSession(seatId);

            CombatManager.Instance!.AddCreature(player.Creature);
            NCombatRoom.Instance?.AddCreature(player.Creature);
            LocalMultiControlLogger.Info(
                $"[地狱战神POC] ③ 已建战斗内节点（**立绘/血条应已出现**）: seat={seatId}, "
                + $"房间可用={NCombatRoom.Instance != null} —— ⚠ POC 故意沿用同一角色皮，"
                + "所以画面上会看到**两个相同角色**（一个是战神），这是预期、不是重复席位的 bug；"
                + "它的回合由本功能自动收口（日志见 `战神已自动结束回合`）");

            TaskHelper.RunSafely(FinishSpawnAsync(state, player.Creature, seatId));
            _spawnedWarGods.Add(player);
            _spawnedThisCombat = true;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[地狱战神POC] 生成异常（POC 失败，功能未实机通过）: source={source}, error={exception}");
            RollbackSpawn(state, rollbackCandidate, source);
        }
    }

    /// <summary>
    /// 把临时玩家**接进主 run**（`RunState` / 种子 / 卡组 Owner），**绝不进 `RunState.Players`**
    /// （设计文档 #21 §5.3「让战神复用主 run 的 `RunState`」；H1：绝不能进 `runState.Players`）。
    /// 这里照抄 `RunState.AddPlayerDebug`（`sts2src/src/Core/Runs/RunState.cs:604-633`）里**除「入 run 玩家表」之外**
    /// 的全部步骤 —— 每一步漏了都炸，其中第 ③ 步是 r237 实测的真凶：
    /// <list type="number">
    /// <item>`player.RunState = runState` —— 否则 `Player.RunState` 恒为 `NullRunState`（卡牌的 `RunState` 跟着 null）。</item>
    /// <item>`player.InitializeSeed(runState.Rng.StringSeed)` —— 建 `PlayerRng` / `PlayerOdds`。</item>
    /// <item>卡组每张牌 `AfterCreated()` + **`Owner`**（顺序照游戏：先 `AfterCreated` 再 `AddCard(card, player)`）。
    ///   🔴 **漏了随后的 `Owner` = 抽牌堆一洗牌就 NRE**：`CombatState.Contains(AbstractModel)` 对战斗里的每张牌读
    ///   `card.Owner.IsActiveForHooks`，而**卡组牌的 `Owner` 只有 `AddPlayerDebug` 会赋值**
    ///   （`PopulateStartingDeck` → `Deck.AddInternal` **不设 Owner**）⇒ 我们不走那条路 ⇒ 克隆进抽牌堆的牌
    ///   `Owner == null` ⇒ `ModifyShuffleOrder` → `CombatState.IterateHookListeners` → `Contains` ⇒
    ///   `NullReferenceException` ⇒ `turn loop died` ⇒ **战神一出场就全场卡死**（r237 实测，见 references 坑 AC-6）。</item>
    /// </list>
    /// </summary>
    private static void AttachToRun(Player player, CombatState state)
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
                $"[地狱战神POC] ①b 无 run 状态（NullRunState），只补了卡组 Owner: seat={player.NetId}, "
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

        LocalMultiControlLogger.Info(
            $"[地狱战神POC] ①b 已接入主 run（**不进 runState.Players**）: seat={player.NetId}, "
            + $"RunState={runState.GetType().Name}, 卡组={player.Deck.Cards.Count} 张"
            + "（已补：RunState 指向 / 种子 / Owner / run 卡牌表登记）");
    }

    /// <summary>`AttachToRun` 的镜像清理：把战神卡组牌从 run 的卡牌表除名（`RemoveCard` 会同时把 `Owner` 置回 null）。</summary>
    private static void DetachFromRun(Player player)
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

        LocalMultiControlLogger.Info(
            $"[地狱战神POC] 已从 run 卡牌表除名: seat={player.NetId}, 卡组={detached} 张");
    }

    /// <summary>生成失败时的尽力回滚：把战神撤出战斗（别让它半死不活地留在 `state.Players` 里）。</summary>
    private static void RollbackSpawn(CombatState state, Player? player, string source)
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

            DetachFromRun(player);
            LocalMultiControlLogger.Warn(
                $"[地狱战神POC] 已回滚半成品战神: seat={player.NetId}, source={source}, "
                + $"剩余 Players={state.Players.Count}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[地狱战神POC] 回滚半成品战神失败（战神可能仍留在战斗里）: error={exception}");
        }
    }

    /// <summary>POC：替战神结束它自己的回合（它没有 AI ⇒ 由我们收口，锚点便于实机核对）。</summary>
    private static void EndTemporarySeatTurn(Player warGod, string source)
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

            PlayerCmd.EndTurn(warGod, canBackOut: false);
            LocalMultiControlLogger.Info(
                $"[地狱战神POC] 战神已自动结束回合（POC 不出牌）: seat={warGod.NetId}, source={source}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[地狱战神POC] 战神结束回合失败: seat={warGod.NetId}, error={exception}");
        }
    }

    private static async Task FinishSpawnAsync(CombatState state, Creature creature, ulong seatId)
    {
        try
        {
            await CombatManager.Instance!.AfterCreatureAdded(creature);
            await Hook.AfterCreatureAddedToCombat(state, creature);
            LocalMultiControlLogger.Info(
                $"[地狱战神POC] ④ 生成收尾完成: seat={seatId}, "
                + $"是玩家={creature.IsPlayer}, 存活={creature.IsAlive}, "
                + $"被 LocalContext 当成我={MegaCrit.Sts2.Core.Context.LocalContext.IsMe(creature)}（**期望 False**）");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[地狱战神POC] 生成收尾异常: seat={seatId}, error={exception}");
        }
    }

    /// <summary>
    /// 战后奖励发放**之前**清理（由既有补丁 <c>CombatRoomOfferRoomEndRewardsPatch</c> 调用 ⇒ 不新增 Harmony 目标）。
    /// 必须早于奖励生成：原版会遍历 <c>CombatState.Players</c> 发奖（设计文档 §5.8 / §13.3.1）。
    /// </summary>
    internal static void CleanupBeforeRewards(CombatState? state)
    {
        if (!Enabled || state == null)
        {
            return;
        }

        try
        {
            List<Creature> warGods = state.PlayerCreatures
                .Where((creature) => creature.Player != null && HellwarPocPolicy.IsTemporarySeat(creature.Player.NetId))
                .ToList();
            foreach (Creature creature in warGods)
            {
                Player? warGod = creature.Player;

                // ⚠ 视觉节点 + `StateTracker` 订阅都要自己收（`NCombatRoom` 不订阅 `CreaturesChanged`，
                // 只有"怪物死亡"那条路会自动摘节点）—— 照 `CreatureCmd.Escape` 的做法。
                NCreature? node = NCombatRoom.Instance?.GetCreatureNode(creature);
                if (node != null)
                {
                    NCombatRoom.Instance!.RemoveCreatureNode(node);
                    node.ToggleIsInteractable(on: false);
                    node.Visible = false;
                }

                CombatManager.Instance.RemoveCreature(creature);   // StateTracker 退订 + 广播 CreaturesChanged
                state.RemoveCreature(creature);                    // 从 `_allies` 摘掉 ⇒ `state.Players` 收缩

                if (warGod != null)
                {
                    DetachFromRun(warGod);
                }

                LocalMultiControlLogger.Info(
                    $"[地狱战神POC] 已从战斗移除战神: seat={warGod?.NetId}, 剩余 Players={state.Players.Count}");
            }

            if (warGods.Count > 0)
            {
                _spawnedWarGods.Clear();
                RemoveAllTemporarySeatsFromSession("combat-cleanup");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[地狱战神POC] 战后清理异常: error={exception}");
        }
    }

    /// <summary>退局兜底（由 <c>LocalMultiControlRuntime.OnRunCleanup</c> 调用）：清掉所有临时席位与标记。</summary>
    internal static void ResetForRun(string reason)
    {
        if (_lastState == null && _spawnedWarGods.Count == 0)
        {
            return;
        }

        try
        {
            // 战斗结束那条路会清（`CleanupBeforeRewards`）；这里兜"中途退局 / 战斗没走完奖励就没了的"漏，
            // 顺手把战神卡组牌从 run 卡牌表除名（`_spawnedWarGods` 是那批 Player 的唯一持有者）。
            foreach (Player warGod in _spawnedWarGods)
            {
                DetachFromRun(warGod);
            }

            RemoveAllTemporarySeatsFromSession(reason);
            _spawnedWarGods.Clear();
            _spawnedThisCombat = false;
            _lastState = null;
            if (Enabled)
            {
                LocalMultiControlLogger.Info($"[地狱战神POC] 已复位: reason={reason}");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[地狱战神POC] 复位异常: reason={reason}, error={exception}");
        }
    }

    private static int CountWarGods(CombatState state)
        => state.PlayerCreatures.Count(
            (creature) => creature.Player != null && HellwarPocPolicy.IsTemporarySeat(creature.Player.NetId));

    /// <summary>POC 用的角色皮：优先沿用本局已有玩家的角色（立绘资源必然已加载），兜底铁甲战士。</summary>
    private static CharacterModel? ResolveCharacterForPoc(CombatState state)
    {
        CharacterModel? existing = state.Players.FirstOrDefault()?.Character;
        if (existing != null)
        {
            return existing;
        }

        try
        {
            return ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[地狱战神POC] 兜底角色模型取用失败: error={exception.Message}");
            return null;
        }
    }

    private static void AddTemporarySeatToSession(ulong seatId)
    {
        // ⚠ **只登记进独立集合**，绝不塞进 `_localPlayerIds`（那张表是全表消费者的取数点：
        // 切人/前台、第三方 IsMe 放行、大厅、存档；2026-10-08 实测把临时席位追加进去会卡死回合链）。
        LocalSelfCoopContext.AddTemporarySeat(seatId);
        LocalMultiControlLogger.Info(
            $"[地狱战神POC] 临时席位已登记（独立集合，**不进正式席位表**）: seat={seatId}, "
            + $"临时席位=[{string.Join(",", LocalSelfCoopContext.TemporarySeatIds)}], "
            + $"正式席位表=[{string.Join(",", LocalSelfCoopContext.LocalPlayerIds)}]");
    }

    private static void RemoveAllTemporarySeatsFromSession(string reason)
    {
        int removed = LocalSelfCoopContext.RemoveAllTemporarySeats();
        if (removed > 0)
        {
            LocalMultiControlLogger.Info(
                $"[地狱战神POC] 临时席位已注销: reason={reason}, 移除={removed}, "
                + $"正式席位表=[{string.Join(",", LocalSelfCoopContext.LocalPlayerIds)}]");
        }
    }
}
