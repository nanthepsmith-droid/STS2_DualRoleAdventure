#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
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
    /// <summary>已生成的战神（清理 / 退 run 卡牌表 / 席位号段分配的唯一持有者；不跨局残留）。</summary>
    private static readonly List<Player> _warGods = new();

    /// <summary>当前在册的战神（供诊断 / 日志）。</summary>
    internal static IReadOnlyList<Player> SpawnedWarGods => _warGods;

    /// <summary>
    /// 按 NetId 取**在册战神**。
    ///
    /// 用途：`LocalContext.GetMe(IPlayerCollection)` 的口子只拿到 id（`playerCollection` 里查不到战神
    /// —— 它刻意不进 `runState.Players`），必须由我们按 id 反查 Player 实例（见
    /// <see cref="Patch.LocalContextTemporarySeatGetMePatch"/>）。
    /// </summary>
    internal static Player? FindSpawnedWarGod(ulong netId)
    {
        foreach (Player warGod in _warGods)
        {
            if (warGod.NetId == netId)
            {
                return warGod;
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
        foreach (Player warGod in _warGods)
        {
            occupied.Add(warGod.NetId);
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
    /// D7 预设档（Phase 2b）：决定"卡组要不要全升级 / 额外血上限"。**角色皮由调用方先解析好**传成
    /// <paramref name="character"/>（普通档 = 召唤者自身角色；其余档 = 预设表里的原版角色）。
    /// ⚠ 预设必须在**接进 run 之前**应用（<see cref="AttachToRun"/> 会登记卡组每张牌的 `Owner` 与 run 卡牌表，
    /// 之后 `PopulateCombatState` 再把它克隆进抽牌堆 ⇒ 改晚了就进不了抽牌堆）。
    /// </param>
    internal static bool TrySpawn(
        CombatState state,
        CharacterModel character,
        HellwarWarGodPreset preset,
        string tag,
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

            // ★ 预设档（Phase 2b）：必须在 AttachToRun / PopulateCombatState **之前**做（见 preset 注释）。
            int upgraded = ApplyPresetToInventory(player, preset);

            LocalMultiControlLogger.Info(
                $"{tag} ① 已建临时玩家: seat={seatId}(0x{seatId:X}), character={character.Id}, "
                + $"hp={player.Creature.CurrentHp}/{player.Creature.MaxHp}, "
                + $"遗物={player.Relics.Count} 件, 卡组={player.Deck.Cards.Count} 张"
                + $"({HellwarWarGodPresets.Describe(preset)}, 升级了={upgraded} 张)");

            // ①b ★★ 必做（r237 实测漏了它 = NullReferenceException ⇒ 全场卡死）：见 AttachToRun 注释。
            AttachToRun(player, state, tag);

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

            CombatManager.Instance!.AddCreature(player.Creature);
            NCombatRoom.Instance?.AddCreature(player.Creature);
            LocalMultiControlLogger.Info(
                $"{tag} ③ 已建战斗内节点（**立绘/血条应已出现**）: seat={seatId}, "
                + $"房间可用={NCombatRoom.Instance != null} —— ⚠ 普通档用**召唤者的角色皮**（画面上会看到与真人相同的角色，"
                + "这是预期、不是重复席位的 bug）；罕见/稀有/先古用预设表里的原版角色皮；"
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

            TaskHelper.RunSafely(FinishSpawnAsync(state, player.Creature, seatId, tag));
            _warGods.Add(player);
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
    /// <item><b>卡组全升级</b>（<paramref name="preset"/>.<c>UpgradeDeck</c>）：逐张 `IsUpgradable` 判定后
    ///   `UpgradeInternal()` + `FinalizeUpgradeInternal()`（顺序不能反，后者收尾预览态）。
    ///   这是首版**唯一"零硬编码"的强度手段**：不用写死任何卡牌 id，改卡池 / 加 mod 都不会失效。</item>
    /// <item><b>额外血上限</b>（`BonusHp`）：`SetMaxHpInternal` 之后再 `SetCurrentHpInternal`（把当前血补满到新上限，
    ///   否则会显示成"半血出生"）。</item>
    /// </list>
    /// 返回实际升级的张数（打日志用）。
    /// </summary>
    private static int ApplyPresetToInventory(Player player, HellwarWarGodPreset preset)
    {
        int upgraded = 0;
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
            _warGods.Remove(player);
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

                // 还有可打牌 ⇒ 交给托管出牌（下一帧 tick 会再判）；没有 ⇒ 这一席当刻没事做，替它 ready。
                if (PileType.Hand.GetPile(warGod).Cards.Any((card) => card.CanPlay()))
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

    private static async Task FinishSpawnAsync(CombatState state, Creature creature, ulong seatId, string tag)
    {
        try
        {
            await CombatManager.Instance!.AfterCreatureAdded(creature);
            await Hook.AfterCreatureAddedToCombat(state, creature);

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
        NCreature? node = NCombatRoom.Instance?.GetCreatureNode(creature);
        if (node != null)
        {
            NCombatRoom.Instance!.RemoveCreatureNode(node);
            node.ToggleIsInteractable(on: false);
            node.Visible = false;
        }

        CombatManager.Instance.RemoveCreature(creature);   // StateTracker 退订 + 广播 CreaturesChanged
        state.RemoveCreature(creature);                    // 从 `_allies` 摘掉 ⇒ `state.Players` 收缩

        Player? warGod = creature.Player;
        if (warGod != null)
        {
            DetachFromRun(warGod, tag);
            _warGods.Remove(warGod);
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
                RemoveWarGodFromCombat(state, creature, tag);
                removed++;
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
            foreach (Player warGod in _warGods.ToList())
            {
                DetachFromRun(warGod, tag);
            }

            _warGods.Clear();
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
