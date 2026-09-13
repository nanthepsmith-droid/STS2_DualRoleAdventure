using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 个人偏好记录器（三级决策链第①级数据源，可行性分析 §8.4.1）运行时层：
/// 负责把「真人可见并做出的决策」写入磁盘（%APPDATA%\SlayTheSpire2\personal_stats.json），
/// 并在整局结束时补写结果（win/loss），供纯逻辑层 WakuuPersonalQuery 聚合查询。
///
/// 数据口径（用户拍板）：
/// - 只记真人决策——真人领奖走 RewardsSetSynchronizer.SelectLocalReward、真人选事件走
///   NEventRoom.OptionButtonClicked（UI 点击），瓦库自动走 SelectUnsynchronized / 直调 Chosen，
///   天然不触发这两个入口 → 瓦库数据整批不记。
/// - 卡牌 offer 只在真人实际点选的批次里计；真人整批跳过（含选替代项）不记。
/// - runKey = 种子 + 玩家数（state.Rng.StringSeed:Players.Count），跨存档稳定；
///   记录逐条落盘，局结束（OnEnded）时补写 run 结果，跨会话存档也能归因。
/// - abandon 局数据直接丢弃（既不计 offer 也不计胜负），进行中局的数据保留但不参与统计
///   （聚合只认 runs 表里有 win/loss 结果的局），stale 超期由 PruneStale 清理。
/// </summary>
internal static class LocalPersonalRecorder
{
    private const string ConfigFileName = "personal_stats.json";

    private static readonly object _lock = new();

    private static PersonalStore _store = new();

    public static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SlayTheSpire2",
            ConfigFileName);

    /// <summary>记录器开关（personalRecorder 默认开）。关闭时所有记录入口直接返回。</summary>
    public static bool IsEnabled => LocalWakuuAutopilotConfig.PersonalRecorder;

    /// <summary>入口初始化：读取磁盘数据 + 清理 stale。幂等，可重复调用。</summary>
    public static void Reload(string source)
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    _store = new PersonalStore();
                    LocalMultiControlLogger.Info($"个人偏好记录器启动，无历史数据: {FilePath}, source={source}");
                    return;
                }

                string json = File.ReadAllText(FilePath);
                PersonalStore? parsed = WakuuPersonalJson.Parse(json);
                if (parsed == null)
                {
                    LocalMultiControlLogger.Warn($"个人偏好记录器配置为空或损坏，重新开始: {FilePath}, source={source}");
                    _store = new PersonalStore();
                    return;
                }

                WakuuPersonalQuery.PruneStale(parsed, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                _store = parsed;
                LocalMultiControlLogger.Info(
                    $"个人偏好记录器已加载: source={source}, runs={_store.runs.Count}, "
                    + $"cardOffers={_store.cardOffers.Count}, eventChoices={_store.eventChoices.Count}");
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"个人偏好记录器加载异常，沿用空数据: {exception.Message}");
                _store = new PersonalStore();
            }
        }
    }

    /// <summary>供决策链查询的只读快照（锁内拷贝列表，行对象追加后不再修改）。</summary>
    public static PersonalStore Snapshot()
    {
        lock (_lock)
        {
            return new PersonalStore
            {
                runs = _store.runs.ToList(),
                cardOffers = _store.cardOffers.ToList(),
                eventChoices = _store.eventChoices.ToList(),
                shopPurchases = _store.shopPurchases.ToList(),
                cardRemovals = _store.cardRemovals.ToList(),
            };
        }
    }

    /// <summary>
    /// 真人点选的卡牌奖励批次入账。offers 为真人看到的那批候选（含没选的），
    /// pickedIds 为实际点中的（正常 1 张）；每张候选各记一行。
    /// </summary>
    public static void RecordHumanCardRewardBatch(
        Player owner,
        IReadOnlyList<(string CardId, bool IsRepeat)> offers,
        IReadOnlyList<string> pickedIds)
    {
        AppendCardOfferBatch(owner, offers, pickedIds, "卡牌奖励批次");
    }

    /// <summary>
    /// 真人点选的「事件网格选 N 张入卡组」批次入账（脑蛭「分享知识」等 FromSimpleGridForRewards 路径，
    /// 候选是事件当场生成的 N 张，点中的会进牌组——与卡牌奖励同构，统一记进 cardOffers，
    /// 供同一张抓取率/胜负归因表使用）。
    /// </summary>
    public static void RecordHumanGridCardBatch(
        Player owner,
        IReadOnlyList<(string CardId, bool IsRepeat)> offers,
        IReadOnlyList<string> pickedIds)
    {
        AppendCardOfferBatch(owner, offers, pickedIds, "网格入卡组批次");
    }

    /// <summary>卡牌 offer/pick 批次通用入账（卡牌奖励与事件网格入卡组共用一张表）。</summary>
    private static void AppendCardOfferBatch(
        Player owner,
        IReadOnlyList<(string CardId, bool IsRepeat)> offers,
        IReadOnlyList<string> pickedIds,
        string logLabel)
    {
        if (!IsEnabled || offers == null || offers.Count == 0 || owner == null)
        {
            return;
        }

        if (!TryGetRunContext(owner, out string runKey, out bool isMulti, out int act, out string character))
        {
            return;
        }

        HashSet<string> picked = new(pickedIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        lock (_lock)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            // 写时幂等（r120）：同一批牌（batchKey 相同）只保留**最后一次** —— SL 后重选同一批牌会覆盖，
            // 避免抓取率（Picked / Offered）被重复计数。批次按"卡集合"区分，重 roll 出不同的牌 → 新批次。
            string batchKey = WakuuPersonalDedupe.BuildBatchKey(offers.Select((o) => o.CardId));
            int replaced = WakuuPersonalDedupe.RemoveCardBatch(_store, runKey, batchKey);
            foreach ((string cardId, bool isRepeat) in offers)
            {
                if (string.IsNullOrEmpty(cardId))
                {
                    continue;
                }

                _store.cardOffers.Add(new PersonalCardOfferRecord
                {
                    runKey = runKey,
                    isMulti = isMulti,
                    character = character,
                    act = act,
                    card = cardId.ToUpperInvariant(),
                    isRepeat = isRepeat,
                    picked = picked.Contains(cardId),
                    batch = batchKey,
                    ts = now,
                });
            }

            LocalMultiControlLogger.Info(
                $"个人记录-{logLabel}: char={character}, run={runKey}, act={act}, "
                + $"offers={offers.Count}, picked={picked.Count}, 覆盖旧批次={replaced}");
        }

        Save();
    }

    /// <summary>
    /// 真人删牌入账（删牌统计，Phase 4）：把一张牌从牌库删掉一次 = 一行。
    /// 只记真人决策；瓦库自动删牌（Phase 4 商店/事件自动）由调用侧作用域排除。
    /// </summary>
    public static void RecordHumanCardRemoval(Player owner, string cardId)
    {
        if (!IsEnabled || owner == null || string.IsNullOrEmpty(cardId))
        {
            return;
        }

        if (!TryGetRunContext(owner, out string runKey, out bool isMulti, out int act, out string character))
        {
            return;
        }

        lock (_lock)
        {
            // 写时幂等（r120）：同一局**同一幕**同一张牌只保留最后一次（带 act —— 跨幕的两次合法删牌不该合并）。
            int replaced = WakuuPersonalDedupe.RemoveCardRemoval(_store, runKey, act, cardId.ToUpperInvariant());
            _store.cardRemovals.Add(new PersonalCardRemovalRecord
            {
                runKey = runKey,
                isMulti = isMulti,
                character = character,
                act = act,
                card = cardId.ToUpperInvariant(),
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });

            LocalMultiControlLogger.Info(
                $"个人记录-删牌: char={character}, run={runKey}, act={act}, "
                + $"card={cardId.ToUpperInvariant()}, 覆盖旧行={replaced}");
        }

        Save();
    }

    /// <summary>
    /// 真人商店购买入账（用户口径：只记「买了」）。kind 取 WakuuPersonalQuery.ShopKind*。
    /// 瓦库自动购买由调用侧把 LocalWakuuMerchantAuto.PurchaseOwnerId 置位（作用域），此处不记。
    /// </summary>
    public static void RecordShopPurchase(Player owner, string kind, string itemId, int goldSpent)
    {
        if (!IsEnabled || owner == null || string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(itemId))
        {
            return;
        }

        if (!TryGetRunContext(owner, out string runKey, out bool isMulti, out int act, out string character))
        {
            return;
        }

        lock (_lock)
        {
            // 写时幂等（r120）：同一局同幕同类别同一件只保留最后一次（SL 后重买会覆盖）。
            int replaced = WakuuPersonalDedupe.RemoveShopPurchase(
                _store, runKey, act, kind, itemId.ToUpperInvariant());
            _store.shopPurchases.Add(new PersonalShopPurchaseRecord
            {
                runKey = runKey,
                isMulti = isMulti,
                character = character,
                act = act,
                kind = kind,
                item = itemId.ToUpperInvariant(),
                goldSpent = Math.Max(0, goldSpent),
                ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });

            LocalMultiControlLogger.Info(
                $"个人记录-商店购买: char={character}, run={runKey}, act={act}, "
                + $"kind={kind}, item={itemId.ToUpperInvariant()}, gold={Math.Max(0, goldSpent)}, 覆盖旧行={replaced}");
        }

        Save();
    }

    /// <summary>
    /// 真人点选事件的一页入账：pageOptions 为该页可见的全部选项，chosen 为点中的那个；
    /// 每个选项各记一行（chosen=true 只标中选者）。
    /// </summary>
    public static void RecordHumanEventPage(
        EventModel eventModel,
        EventOption chosen,
        IReadOnlyList<EventOption> pageOptions)
    {
        if (!IsEnabled || eventModel == null || chosen == null || pageOptions == null || pageOptions.Count == 0)
        {
            return;
        }

        Player? owner = eventModel.Owner;
        if (owner == null || !TryGetRunContext(owner, out string runKey, out bool isMulti, out int act, out string character))
        {
            return;
        }

        lock (_lock)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string eventId = eventModel.Id.Entry.ToUpperInvariant();
            // 写时幂等（r120）：同一事件只保留**最后一次**抉择 —— SL 后重选会覆盖旧页，
            // 既不会重复累加选择率分母，也不会残留"SL 前选过的那一项 chosen=true"。
            int replaced = WakuuPersonalDedupe.RemoveEventPage(_store, runKey, eventId);
            foreach (EventOption option in pageOptions)
            {
                if (option == null || string.IsNullOrEmpty(option.TextKey))
                {
                    continue;
                }

                _store.eventChoices.Add(new PersonalEventOptionRecord
                {
                    runKey = runKey,
                    isMulti = isMulti,
                    character = character,
                    act = act,
                    eventId = eventId,
                    optionKey = option.TextKey,
                    optionText = SafeRawText(option.Title),
                    chosen = ReferenceEquals(option, chosen),
                    ts = now,
                });
            }

            LocalMultiControlLogger.Info(
                $"个人记录-事件选择: event={eventId}, char={character}, run={runKey}, act={act}, "
                + $"page={pageOptions.Count}, chosen={chosen.TextKey}, 覆盖旧页={replaced}");
        }

        Save();
    }

    /// <summary>整局结束：补写结果并落盘。abandon 局的数据丢弃（不参与任何统计）。</summary>
    public static void RecordRunEnded(bool isVictory, bool isAbandoned)
    {
        lock (_lock)
        {
            if (_store.cardOffers.Count == 0 && _store.eventChoices.Count == 0)
            {
                return; // 本局没有任何记录（通常是没有真人决策），无需落盘
            }

            string? runKey = CurrentRunKeyOrNull();
            if (runKey == null)
            {
                return;
            }

            if (isAbandoned)
            {
                int removedCards = _store.cardOffers.RemoveAll((r) => r.runKey == runKey);
                int removedEvents = _store.eventChoices.RemoveAll((r) => r.runKey == runKey);
                LocalMultiControlLogger.Info(
                    $"个人记录-本局被放弃，丢弃其数据: run={runKey}, cards={removedCards}, events={removedEvents}");
            }
            else
            {
                _store.runs.Add(new PersonalRunRecord
                {
                    runKey = runKey,
                    isMulti = RunManager.Instance?.DebugOnlyGetState()?.Players.Count > 1,
                    result = isVictory ? WakuuPersonalQuery.ResultWin : WakuuPersonalQuery.ResultLoss,
                    ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                });
                LocalMultiControlLogger.Info(
                    $"个人记录-整局结束: run={runKey}, result={(isVictory ? "win" : "loss")}");
            }
        }

        Save();
    }

    /// <summary>从玩家/运行状态推导 run 上下文；不在局内（state 缺失/种子为空）返回 false。</summary>
    private static bool TryGetRunContext(
        Player owner,
        out string runKey,
        out bool isMulti,
        out int act,
        out string character)
    {
        runKey = string.Empty;
        isMulti = false;
        act = 1;
        character = string.Empty;

        try
        {
            RunState? state = RunManager.Instance?.DebugOnlyGetState();
            if (state == null || state.Players.Count == 0)
            {
                return false;
            }

            string seed = state.Rng?.StringSeed ?? string.Empty;
            if (string.IsNullOrEmpty(seed))
            {
                return false;
            }

            runKey = $"{seed}:{state.Players.Count}";
            isMulti = state.Players.Count > 1;
            act = Math.Max(1, state.CurrentActIndex + 1);
            character = owner.Character?.Id?.Entry?.ToUpperInvariant() ?? string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"个人记录器取运行上下文失败(忽略): {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// 由 RunState 构造 runKey（种子 + 玩家数）；与 <see cref="TryGetRunContext"/> 同一口径。
    /// 种子缺失返回空串（调用方按"取不到"处理）。
    /// **凡是手边已有 RunState 的调用方都应该直接用这个**，不要绕 `RunManager.DebugOnlyGetState()`
    /// （进局早期它还没就绪，r118 的回滚就是因此静默失效的）。
    /// </summary>
    private static string BuildRunKey(RunState? state)
    {
        if (state == null)
        {
            return string.Empty;
        }

        string seed = state.Rng?.StringSeed ?? string.Empty;
        return string.IsNullOrEmpty(seed) ? string.Empty : $"{seed}:{state.Players.Count}";
    }

    private static string? CurrentRunKeyOrNull()
    {
        try
        {
            RunState? state = RunManager.Instance?.DebugOnlyGetState();
            if (state == null || state.Players.Count == 0)
            {
                return null;
            }

            string key = BuildRunKey(state);
            return string.IsNullOrEmpty(key) ? null : key;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"个人记录器取当前 runKey 失败(忽略): {exception.Message}");
            return null;
        }
    }

    private static string SafeRawText(LocString? title)
    {
        try
        {
            return title?.GetRawText() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>把当前内存数据原子写盘（临时文件 + 移动），失败只留 WARN 不打断游戏。</summary>
    private static void Save()
    {
        lock (_lock)
        {
            try
            {
                string? directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, WakuuPersonalJson.Serialize(_store));
                File.Move(temp, FilePath, overwrite: true);
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"个人偏好记录器写入失败(忽略): {exception.Message}");
            }
        }
    }
}
