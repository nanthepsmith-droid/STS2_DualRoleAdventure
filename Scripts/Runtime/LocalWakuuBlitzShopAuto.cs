using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Patch;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// **极速模式（Blitz）商店自动购买**（2026-11-23 用户口径，开关 <c>blitzShopAutoBuy</c> 默认关）。
///
/// <para><b>为什么不能复用普通商店那套</b>（<see cref="LocalWakuuMerchantAuto"/>）：极速商店是另一套库存
/// —— <c>MerchantInventory.CreateForBlitzMerchant</c> 只填 <c>BlitzEntries</c>（6 个条目、全部 Cost = 0），
/// 每次进店只有 <c>BlitzChoicesRemaining = 3</c> 次购买机会，「诅咒选择」再 +2，减到 0 全店置灰。
/// 所以这里有独立的判定表 <see cref="BlitzShopBuyPolicy"/> 与"按优先级买满次数"的循环。</para>
///
/// <para><b>触发时机</b>：商店房的 <c>NMerchantInventory.Initialize</c>（= 进店那一刻，不需要真人打开商人界面），
/// 由 <see cref="NMerchantInventoryPatch"/> 调 <see cref="OnShopRoomReady"/>。一次进店只跑一轮，
/// 替**所有瓦库形态席位**各自买自己的库存（每席位独立 RNG / 独立 3 次机会）。</para>
///
/// <para><b>购买链</b>：走 <c>MerchantEntry.OnTryPurchaseUnsynchronized</c>（与普通商店同一手法：
/// 本 mod 单进程回环、不走广播），并用 <c>TryBeginPurchase</c> 复刻原版"一次只允许一笔在飞"。</para>
///
/// <para><b>选牌</b>：升级 / 变化 / 删牌 / 附魔 四条会拉起选牌界面，这里按归属者压入托管选择器
/// （<see cref="WakuuSelectorRegistry"/>）自动作答 —— 与火堆锻造 / 商店删牌同一套。</para>
///
/// <para><b>奖励</b>：卡牌/遗物/药水奖励由 <c>RewardsCmdOfferCustomPatch</c> 接管自动结算，
/// 前提是本类的作用域（<see cref="IsAutoBuyingFor"/>）让它在"前台正是瓦库"时也接管；
/// 因此要求 <c>autoClaimCards / autoClaimGoldRelics / autoClaimPotions</c> 三个开关全开，
/// 否则会弹奖励屏等人点、购买链挂起。</para>
/// </summary>
internal static class LocalWakuuBlitzShopAuto
{
    /// <summary>等商店界面就绪的延迟（房间切换 / 库存绑定后给一帧缓冲）。</summary>
    private const int ReadyDelayMs = 600;

    /// <summary>单席位最多尝试几次购买（3 次 + 「诅咒选择」可能叠两次，留足护栏防死循环）。</summary>
    private const int MaxPurchaseAttemptsPerSeat = 12;

    /// <summary>同一（房间）只跑一轮：进店时房节点可能因切席位重建，但库存与次数只有一份。</summary>
    private static readonly HashSet<MerchantRoom> _handledRooms = new();

    /// <summary>本局各席位「已买过几次诅咒遗物」（规则：一局最多 2 次，防诅咒堆积）。</summary>
    private static readonly Dictionary<ulong, int> _cursedRelicBuys = new();

    /// <summary>这些计数只对当前这一局有效：RunState 实例一换（新局/读档）就整表清空。</summary>
    private static IRunState? _trackedRun;

    private static readonly object _lock = new();

    /// <summary>
    /// 当前正在极速商店自动购买的归属者（沿异步链流动）。供
    /// <c>RewardsCmdOfferCustomPatch</c> 区分「真人前台真的是瓦库」与「是我们的自动化把上下文对齐过去了」——
    /// 与事件自动选择 / 奖励自动领取两个作用域同一个坑（见 r143 的注释）。
    /// </summary>
    private static readonly AsyncLocal<ulong?> _autoBuyOwner = new();

    internal static ulong? AutoBuyOwnerId => _autoBuyOwner.Value;

    /// <summary>当前是否处于「为这一位玩家自动买极速商店」的作用域内。</summary>
    internal static bool IsAutoBuyingFor(Player? player)
    {
        return player != null && _autoBuyOwner.Value == player.NetId;
    }

    /// <summary>退局清理（由 <c>LocalMultiControlRuntime.OnRunCleanup</c> 调）。</summary>
    internal static void Reset(string reason)
    {
        lock (_lock)
        {
            int rooms = _handledRooms.Count;
            int seats = _cursedRelicBuys.Count;
            _handledRooms.Clear();
            _cursedRelicBuys.Clear();
            _trackedRun = null;
            if (rooms > 0 || seats > 0)
            {
                LocalMultiControlLogger.Info(
                    $"极速商店自动购买状态已清理: 房间={rooms}, 席位={seats}, reason={reason}");
            }
        }
    }

    /// <summary>由 <see cref="NMerchantInventoryPatch"/>（商店界面 Initialize）调用。</summary>
    public static void OnShopRoomReady(Player? shownPlayer)
    {
        try
        {
            if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
            {
                return;
            }

            if (!LocalWakuuAutopilotConfig.BlitzShopAutoBuy)
            {
                return;
            }

            if (!RunManager.Instance.IsInProgress)
            {
                return;
            }

            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            if (runState == null || runState.GameMode != GameMode.Blitz)
            {
                return;
            }

            if (runState.CurrentRoom is not MerchantRoom room)
            {
                return;
            }

            // 奖励自动结算补丁要求多人回环（单人局原版会弹屏等人点 ⇒ 购买链挂起）。
            if (runState.Players.Count <= 1)
            {
                LocalMultiControlLogger.Info(
                    $"极速商店自动购买跳过：单人局（奖励会弹屏等人点）。player={shownPlayer?.NetId}");
                return;
            }

            // 奖励结算靠 autoClaim* 三个开关；关着一个就会弹屏（见类注释）。
            if (!LocalWakuuAutopilotConfig.AutoClaimCards
                || !LocalWakuuAutopilotConfig.AutoClaimGoldRelics
                || !LocalWakuuAutopilotConfig.AutoClaimPotions)
            {
                LocalMultiControlLogger.Info(
                    "极速商店自动购买跳过：奖励自动领取开关没有全开（autoClaimCards / autoClaimGoldRelics / "
                    + "autoClaimPotions），购买会弹奖励屏等人点。");
                return;
            }

            bool anyWakuu = runState.Players.Any(
                (p) => p != null && p.Creature?.IsDead != true && LocalWakuuRelicRuntime.IsVakuuFormMode(p));
            if (!anyWakuu)
            {
                return;
            }

            lock (_lock)
            {
                EnsureRunTracking(runState);
                if (!_handledRooms.Add(room))
                {
                    return; // 本店已跑过（切席位会重建房节点，但库存只有一份）
                }
            }

            LocalMultiControlLogger.Info(
                $"极速商店自动购买启动: 房间玩家={runState.Players.Count}, 触发席位={shownPlayer?.NetId}");
            TaskHelper.RunSafely(RunAsync(runState, room));
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"极速商店自动购买启动失败: {exception.Message}");
        }
    }

    private static async Task RunAsync(RunState runState, MerchantRoom room)
    {
        try
        {
            await Task.Delay(ReadyDelayMs);
            if (!RunManager.Instance.IsInProgress || !ReferenceEquals(runState.CurrentRoom, room))
            {
                return;
            }

            int seatCount = 0;
            int boughtTotal = 0;
            foreach (Player player in runState.Players.ToList())
            {
                if (!RunManager.Instance.IsInProgress || !ReferenceEquals(runState.CurrentRoom, room))
                {
                    break;
                }

                if (player == null)
                {
                    continue;
                }

                if (player.Creature == null || player.Creature.IsDead)
                {
                    LocalMultiControlLogger.Info($"极速商店自动购买跳过已死亡席位: player={player.NetId}");
                    continue;
                }

                if (!LocalWakuuRelicRuntime.IsVakuuFormMode(player))
                {
                    continue;
                }

                MerchantInventory? inventory = ResolveBlitzInventory(room, player);
                if (inventory == null || !inventory.IsBlitzShop)
                {
                    LocalMultiControlLogger.Warn(
                        $"极速商店自动购买跳过：拿不到该席位的极速库存。player={player.NetId}");
                    continue;
                }

                seatCount++;
                boughtTotal += await BuyForSeatAsync(runState, player, inventory);
            }

            LocalMultiControlLogger.Info(
                $"极速商店自动购买完成: 席位={seatCount}, 共买={boughtTotal}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"极速商店自动购买异常: {exception.Message}");
        }
    }

    /// <summary>
    /// 取该席位的极速库存 —— **必须走 <see cref="LocalMerchantInventoryRuntime"/> 的按房缓存**
    /// （与 <see cref="NMerchantInventoryPatch"/> 里"切席位时绑定哪一份"是同一条路）。
    ///
    /// <para>⚠ r395 实机教训（用户报「除前台外，其它席位自动买完切过去货物还在」）：
    /// 第一版这里优先用 `MerchantSynchronizer.GetInventoryFor(player)`（= 原版进店时批量建的那份），
    /// 于是自动购买买的是**原版那份**；而用户切到该席位时，prefix 走 `GetOrCreateInventory`
    /// 按缓存**又新建了一份**（那张席位当时不在缓存里）并绑定 ⇒ 自动买过的那份被顶掉，
    /// 切过去看到的是全新的 6 格 + 3 次机会，真人还能再买一遍。
    /// 只有"进店时正显示的那一位"（prefix 已经建过缓存）没有这个问题 —— 与用户描述完全一致。</para>
    /// </summary>
    private static MerchantInventory ResolveBlitzInventory(MerchantRoom room, Player player)
    {
        MerchantInventory inventory = LocalMerchantInventoryRuntime.GetOrCreateInventory(room, player);
        LocalMerchantInventoryRuntime.BindInventoryToRoom(room, player, inventory);
        return inventory;
    }

    private static async Task<int> BuyForSeatAsync(RunState runState, Player player, MerchantInventory inventory)
    {
        ulong? previousNetId = LocalSeatSource.ContextSeatId();
        int bought = 0;
        try
        {
            AlignContext(player.NetId);
            _autoBuyOwner.Value = player.NetId;

            // 本店内购买失败过的条目（选牌取消 / 无目标牌 / 另一笔在飞）不再重试，防死循环。
            HashSet<uint> failedIds = new();
            HashSet<uint> loggedSkips = new();

            for (int attempt = 0; attempt < MaxPurchaseAttemptsPerSeat; attempt++)
            {
                if (!RunManager.Instance.IsInProgress || inventory.BlitzChoicesRemaining <= 0)
                {
                    break;
                }

                BlitzShopContext baseContext = BuildContext(player, inventory);
                baseContext.DesirableUnboughtCount = CountDesirableUnbought(inventory, baseContext);

                BlitzMerchantEntry? bestEntry = null;
                BlitzShopEntryKind bestKind = BlitzShopEntryKind.CardUpgrade;
                int bestPriority = int.MaxValue;
                string bestReason = string.Empty;

                foreach (BlitzMerchantEntry entry in inventory.BlitzEntries)
                {
                    if (entry == null || entry.Status != BlitzMerchantEntry.BlitzEntryStatus.Available)
                    {
                        continue;
                    }

                    if (entry.Id.HasValue && failedIds.Contains(entry.Id.Value))
                    {
                        continue;
                    }

                    if (!TryMapKind(entry.MerchantEntryType, out BlitzShopEntryKind kind))
                    {
                        LocalMultiControlLogger.Warn(
                            $"极速商店自动购买：未适配的条目类型 {entry.MerchantEntryType}（跳过，不改动其它条目）");
                        if (entry.Id.HasValue)
                        {
                            failedIds.Add(entry.Id.Value);
                        }

                        continue;
                    }

                    BlitzShopContext entryContext = EnrichForEntry(baseContext, player, entry, kind);
                    BlitzShopDecision decision = BlitzShopBuyPolicy.Evaluate(kind, entryContext);
                    if (!decision.Buy)
                    {
                        if (entry.Id.HasValue && loggedSkips.Add(entry.Id.Value))
                        {
                            LocalMultiControlLogger.Info(
                                $"极速商店不买: 条目={BlitzShopBuyPolicy.Describe(kind)}, 原因={decision.Reason}, "
                                + $"player={player.NetId}, 剩余次数={inventory.BlitzChoicesRemaining}, "
                                + $"诅咒数={entryContext.CurseCount}, 牌组={entryContext.DeckSize}/{entryContext.DeckLimit}, "
                                + $"HP={entryContext.HpPercent:P0}");
                        }

                        continue;
                    }

                    int priority = BlitzShopBuyPolicy.Priority(kind, entryContext);
                    if (priority < bestPriority)
                    {
                        bestPriority = priority;
                        bestEntry = entry;
                        bestKind = kind;
                        bestReason = decision.Reason;
                    }
                }

                if (bestEntry == null)
                {
                    LocalMultiControlLogger.Info(
                        $"极速商店自动购买: 没有符合规则的条目，停止。player={player.NetId}, "
                        + $"剩余次数={inventory.BlitzChoicesRemaining}, 已买={bought}");
                    break;
                }

                LocalMultiControlLogger.Info(
                    $"极速商店决定购买: 条目={BlitzShopBuyPolicy.Describe(bestKind)}, 优先级={bestPriority}, "
                    + $"原因={bestReason}, player={player.NetId}, 剩余次数={inventory.BlitzChoicesRemaining}");

                bool success = await PurchaseAsync(player, inventory, bestEntry, bestKind);
                if (success)
                {
                    bought++;
                    if (bestKind == BlitzShopEntryKind.TwoRelics)
                    {
                        IncrementCursedRelicBuys(player.NetId);
                    }
                }
                else if (bestEntry.Id.HasValue)
                {
                    failedIds.Add(bestEntry.Id.Value);
                }
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"极速商店自动购买异常（本席位停止）: player={player.NetId}, error={exception.Message}");
        }
        finally
        {
            _autoBuyOwner.Value = null;
            RestoreContext(previousNetId);
        }

        return bought;
    }

    private static async Task<bool> PurchaseAsync(
        Player player,
        MerchantInventory inventory,
        BlitzMerchantEntry entry,
        BlitzShopEntryKind kind)
    {
        ulong? previousChoiceOwner = CardSelectForegroundSwitchPatch.CurrentChoicePlayerId.Value;
        CardSelectForegroundSwitchPatch.CurrentChoicePlayerId.Value = player.NetId;
        try
        {
            using (IDisposable? selectorScope = OpenSelectorIfNeeded(player, entry, kind))
            {
                // 复刻原版「一次只允许一笔购买在飞」（原版这条闸在 MerchantSynchronizer.PurchaseLocalEntry 里）。
                if (!inventory.TryBeginPurchase())
                {
                    LocalMultiControlLogger.Info(
                        $"极速商店购买跳过：另一笔购买正在进行。player={player.NetId}, "
                        + $"条目={BlitzShopBuyPolicy.Describe(kind)}");
                    return false;
                }

                bool success;
                try
                {
                    success = await entry.OnTryPurchaseUnsynchronized(inventory, ignoreCost: true);
                }
                finally
                {
                    inventory.EndPurchase();
                }

                LocalMultiControlLogger.Info(
                    $"极速商店购买结果: 条目={BlitzShopBuyPolicy.Describe(kind)}, 成功={success}, "
                    + $"player={player.NetId}, 剩余次数={inventory.BlitzChoicesRemaining}");
                return success;
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"极速商店购买失败: 条目={BlitzShopBuyPolicy.Describe(kind)}, player={player.NetId}, "
                + $"error={exception.GetType().Name}: {exception.Message}");
            return false;
        }
        finally
        {
            CardSelectForegroundSwitchPatch.CurrentChoicePlayerId.Value = previousChoiceOwner;
        }
    }

    /// <summary>会拉起选牌界面的四类条目：按归属者压入托管选择器；其余返回 null（不需要作答）。</summary>
    private static IDisposable? OpenSelectorIfNeeded(
        Player player,
        BlitzMerchantEntry entry,
        BlitzShopEntryKind kind)
    {
        switch (kind)
        {
            case BlitzShopEntryKind.CardUpgrade:
                return WakuuSelectorRegistry.Open(
                    player.NetId,
                    new LocalWakuuTargetedCardSelector(
                        (cards, minSelect, maxSelect) => PickUpgradeTarget(cards, minSelect, maxSelect)));

            case BlitzShopEntryKind.CardTransform:
                return WakuuSelectorRegistry.Open(
                    player.NetId,
                    new LocalWakuuStrategySelector(WakuuPickScenario.Transform) { LogLabel = "极速商店变化" });

            case BlitzShopEntryKind.CardRemoval:
                return WakuuSelectorRegistry.Open(
                    player.NetId,
                    new LocalWakuuStrategySelector(WakuuPickScenario.Remove) { LogLabel = "极速商店删牌" });

            case BlitzShopEntryKind.CardEnchant:
                return OpenEnchantSelector(player, TryReadEnchantment(entry), "极速商店附魔");

            case BlitzShopEntryKind.Glam:
                return OpenEnchantSelector(player, ModelDb.Enchantment<Glam>(), "极速商店华彩");

            default:
                return null;
        }
    }

    private static IDisposable OpenEnchantSelector(
        Player player,
        EnchantmentModel? enchantment,
        string label)
    {
        if (enchantment == null)
        {
            // 拿不到具体附魔（反射面变了）：退回既有 cardPickMode 策略，至少要能作答。
            return WakuuSelectorRegistry.Open(
                player.NetId,
                new LocalWakuuStrategySelector(WakuuPickScenario.Unknown) { LogLabel = label });
        }

        return WakuuSelectorRegistry.Open(
            player.NetId,
            new LocalWakuuTargetedCardSelector(
                (cards, minSelect, maxSelect) =>
                    LocalWakuuEnchantPicker.TryPick(player, cards, enchantment, minSelect, maxSelect)
                    ?? TakeTail(cards, minSelect, maxSelect)));
    }

    /// <summary>
    /// 升级服务的目标牌：**非打击/防御优先**（用户口径），再按「费用最高 → 稀有度最高」。
    /// 全是基础牌时才回退打击/防御（总比不作答好）。
    /// </summary>
    private static List<CardModel> PickUpgradeTarget(
        IReadOnlyList<CardModel> cards,
        int minSelect,
        int maxSelect)
    {
        if (cards == null || cards.Count == 0)
        {
            return new List<CardModel>();
        }

        int take = Math.Clamp(Math.Max(minSelect, 1), 1, cards.Count);
        if (maxSelect > 0)
        {
            take = Math.Min(take, maxSelect);
        }

        return cards
            .Where((c) => c != null)
            .OrderBy((c) => IsBasicCard(c) ? 1 : 0)
            .ThenByDescending((c) => c.EnergyCost.Canonical)
            .ThenByDescending((c) => (int)c.Rarity)
            .Take(take)
            .ToList();
    }

    private static List<CardModel> TakeTail(IReadOnlyList<CardModel> cards, int minSelect, int maxSelect)
    {
        if (cards == null || cards.Count == 0)
        {
            return new List<CardModel>();
        }

        int take = Math.Clamp(Math.Max(minSelect, 1), 1, cards.Count);
        if (maxSelect > 0)
        {
            take = Math.Min(take, maxSelect);
        }

        return cards.Skip(cards.Count - take).ToList();
    }

    private static bool IsBasicCard(CardModel card)
    {
        WakuuCardKind kind = WakuuPriorityPicking.ClassifyCard(
            card.Id.Entry, (int)card.Type, card.IsSlyThisTurn);
        return kind is WakuuCardKind.BasicStrike or WakuuCardKind.BasicDefend;
    }

    /// <summary>读 `BlitzMerchantCardEnchantEntry._enchantment`（`(EnchantmentModel, int)` 元组，私有字段）。</summary>
    private static EnchantmentModel? TryReadEnchantment(BlitzMerchantEntry entry)
    {
        try
        {
            FieldInfo? field = entry.GetType().GetField(
                "_enchantment", BindingFlags.Instance | BindingFlags.NonPublic);
            object? value = field?.GetValue(entry);
            if (value is ITuple tuple && tuple.Length >= 1 && tuple[0] is EnchantmentModel enchantment)
            {
                return enchantment;
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"极速商店读取附魔失败（改走 cardPickMode 兜底）: {entry.GetType().Name}, error={exception.Message}");
        }

        return null;
    }

    /// <summary>把游戏枚举映射到纯逻辑枚举（两者顺序必须一致，由单测钉住）。</summary>
    private static bool TryMapKind(BlitzMerchantEntryType type, out BlitzShopEntryKind kind)
    {
        int value = (int)type;
        if (value < 0 || value > (int)BlitzShopEntryKind.FullHealAndMaxHp)
        {
            kind = default;
            return false;
        }

        kind = (BlitzShopEntryKind)value;
        return true;
    }

    private static BlitzShopContext BuildContext(Player player, MerchantInventory inventory)
    {
        List<CardModel> deck = ReadDeck(player);
        int curseCount = 0;
        int upgradableNonBasic = 0;
        int changeableBasic = 0;
        int removableJunk = 0;
        int enchantableNonBasic = 0;
        int glamTargets = 0;

        foreach (CardModel card in deck)
        {
            if (card?.Id == null)
            {
                continue;
            }

            WakuuCardKind kind = WakuuPriorityPicking.ClassifyCard(
                card.Id.Entry, (int)card.Type, card.IsSlyThisTurn);
            bool basic = kind is WakuuCardKind.BasicStrike or WakuuCardKind.BasicDefend;
            bool junk = kind is WakuuCardKind.Curse or WakuuCardKind.Status || basic;

            if (card.Type == CardType.Curse)
            {
                curseCount++;
            }

            if (junk)
            {
                removableJunk++;
            }

            if (basic)
            {
                changeableBasic++;
            }

            if (basic || kind is WakuuCardKind.Curse or WakuuCardKind.Status)
            {
                continue;
            }

            enchantableNonBasic++;
            if (card.IsUpgradable)
            {
                upgradableNonBasic++;
            }

            if (IsGlamTarget(card))
            {
                glamTargets++;
            }
        }

        List<PotionModel> potions = player.Potions.ToList();
        int lowestPotionRarity = potions.Count == 0
            ? -1
            : potions.Min((p) => (int)p.Rarity);

        return new BlitzShopContext
        {
            RemainingChoices = inventory.BlitzChoicesRemaining,
            Hp = player.Creature?.CurrentHp ?? 0,
            MaxHp = player.Creature?.MaxHp ?? 0,
            DeckSize = deck.Count,
            DeckLimit = WakuuCardPickPolicy.DeckLimitOf(LocalWakuuAutopilotConfig.CardPickTierValue),
            CurseCount = curseCount,
            UpgradableNonBasicCount = upgradableNonBasic,
            ChangeableBasicCount = changeableBasic,
            RemovableJunkCount = removableJunk,
            NonBasicEnchantableCount = enchantableNonBasic,
            GlamTargetCount = glamTargets,
            HasOpenPotionSlot = player.HasOpenPotionSlots,
            HasBloodPotion = player.Potions.Any((p) => p is BloodPotion),
            LowestPotionRarity = lowestPotionRarity,
            CursedRelicBuysThisRun = ReadCursedRelicBuys(player.NetId),
            DesirableUnboughtCount = 0,
        };
    }

    /// <summary>该条目的附加上下文（附魔目标数按具体附魔算，拿不到就留 -1 走兜底）。</summary>
    private static BlitzShopContext EnrichForEntry(
        BlitzShopContext baseContext,
        Player player,
        BlitzMerchantEntry entry,
        BlitzShopEntryKind kind)
    {
        if (kind != BlitzShopEntryKind.CardEnchant)
        {
            return baseContext;
        }

        EnchantmentModel? enchantment = TryReadEnchantment(entry);
        if (enchantment == null)
        {
            return baseContext;
        }

        BlitzShopContext copy = baseContext.Clone();
        copy.EnchantTargetCount = CountEnchantTargets(player, enchantment);
        return copy;
    }

    /// <summary>还符合规则、没买的条目数（供「诅咒选择」判断值不值得拿 1 次换 2 次）。</summary>
    private static int CountDesirableUnbought(MerchantInventory inventory, BlitzShopContext context)
    {
        int count = 0;
        foreach (BlitzMerchantEntry entry in inventory.BlitzEntries)
        {
            if (entry == null || entry.Status != BlitzMerchantEntry.BlitzEntryStatus.Available)
            {
                continue;
            }

            if (!TryMapKind(entry.MerchantEntryType, out BlitzShopEntryKind kind))
            {
                continue;
            }

            if (kind == BlitzShopEntryKind.TwoMoreChoices)
            {
                continue; // 不把自己算进"值得为它换次数"的名单
            }

            if (BlitzShopBuyPolicy.Evaluate(kind, context).Buy)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountEnchantTargets(Player player, EnchantmentModel enchantment)
    {
        try
        {
            return ReadDeck(player).Count((c) => c != null && enchantment.CanEnchant(c));
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"极速商店统计附魔目标失败（改走兜底）: {exception.Message}");
            return -1;
        }
    }

    private static bool IsGlamTarget(CardModel card)
    {
        if (card.Type == CardType.Power)
        {
            return true;
        }

        if (card.EnergyCost.CostsX)
        {
            return true;
        }

        return card.EnergyCost.Canonical >= 3;
    }

    private static List<CardModel> ReadDeck(Player player)
    {
        try
        {
            IReadOnlyList<CardModel>? cards = player.Deck?.Cards;
            return cards == null ? new List<CardModel>() : cards.ToList();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"极速商店读取牌组失败（按空牌组处理）: {exception.Message}");
            return new List<CardModel>();
        }
    }

    private static void EnsureRunTracking(IRunState runState)
    {
        if (ReferenceEquals(_trackedRun, runState))
        {
            return;
        }

        _trackedRun = runState;
        _cursedRelicBuys.Clear();
    }

    private static int ReadCursedRelicBuys(ulong netId)
    {
        return _cursedRelicBuys.TryGetValue(netId, out int count) ? count : 0;
    }

    private static void IncrementCursedRelicBuys(ulong netId)
    {
        lock (_lock)
        {
            _cursedRelicBuys[netId] = ReadCursedRelicBuys(netId) + 1;
        }
    }

    /// <summary>
    /// 采购期间把「本地玩家」上下文对齐到该席位：奖励归属、选牌归属都靠 `LocalContext` 定位，
    /// 不对齐会落到前台那个人身上。结束后由 <see cref="RestoreContext"/> 恢复。
    /// </summary>
    private static void AlignContext(ulong playerId)
    {
        try
        {
            LocalContext.NetId = playerId;
            LocalSelfCoopContext.NetService?.SetCurrentSenderId(playerId);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"极速商店上下文对齐失败(忽略): {exception.Message}");
        }
    }

    private static void RestoreContext(ulong? previousNetId)
    {
        if (!previousNetId.HasValue)
        {
            return;
        }

        try
        {
            LocalContext.NetId = previousNetId.Value;
            LocalSelfCoopContext.NetService?.SetCurrentSenderId(previousNetId.Value);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"极速商店上下文恢复失败(忽略): {exception.Message}");
        }
    }
}
