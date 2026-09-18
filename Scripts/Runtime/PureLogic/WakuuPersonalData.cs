using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 个人偏好记录器的磁盘数据模型与聚合查询纯函数（可行性分析 §8.4.1 的第①级数据源）。
/// 设计原则：本文件不依赖任何游戏类型与第三方类型，全部纯数据 + 基础类型，可直接单测。
///
/// 数据口径（用户拍板）：
/// - 只记录「真人可见并做出的决策」：真人点选的卡牌奖励批次 / 真人点选的事件选项。
///   瓦库自动领奖/自动选事件走 `SelectUnsynchronized` 与直调 Chosen（不经 UI/同步器），整批不记。
/// - 卡牌 offer 只在真人实际点选的批次里计：该批所有候选各记 1 次 offer，点中的记 pick；
///   真人整批跳过（含选替代项）的批次不记。
/// - 首次/重复：offer 发生时牌组里是否已有同 id 卡（重复抓取 = 牌组已有同牌再抓）。
/// - 胜负归因：整局结果（win/loss）按 runKey 关联到该局全部记录；abandon 局不计胜负，
///   只保留 offer/pick 计数。record 全部落盘后到 OnEnded 再补 run 结果，跨会话存档也能归因。
/// </summary>
internal enum PersonalRunResult
{
    Ongoing,
    Win,
    Loss,
    Abandon,
}

/// <summary>整局结果记录（runKey 唯一；一局只会有一条有结果的记录）。</summary>
internal sealed class PersonalRunRecord
{
    /// <summary>持久 run 标识 = 种子 + 玩家数（State.Rng.Seed:Players.Count），跨存档稳定。</summary>
    public string runKey { get; set; } = string.Empty;

    /// <summary>多人局（玩家数 &gt; 1）？单人多控视为 multi。</summary>
    public bool isMulti { get; set; }

    /// <summary>结果：win / loss / abandon / ongoing（详见 PersonalRunResult）。</summary>
    public string result { get; set; } = "ongoing";

    /// <summary>记录时刻（unix 毫秒）。</summary>
    public long ts { get; set; }
}

/// <summary>
/// 卡牌 offer/pick 记录：一个真人点选过的奖励批次里，每个候选卡各一行。
/// picked=true 表示本批点中了这张；false 表示同批展示了但没选。
/// </summary>
internal sealed class PersonalCardOfferRecord
{
    public string runKey { get; set; } = string.Empty;

    public bool isMulti { get; set; }

    /// <summary>卡池归属角色的字符 id（Character.Id.Entry 大写），决策时按角色切片查表。</summary>
    public string character { get; set; } = string.Empty;

    /// <summary>幕数（1/2/3）。</summary>
    public int act { get; set; } = 1;

    /// <summary>卡牌裸 id（不含升级后缀）。</summary>
    public string card { get; set; } = string.Empty;

    /// <summary>offer 时牌组是否已持有同 id 卡（重复抓取）。</summary>
    public bool isRepeat { get; set; }

    /// <summary>本批是否点中了这张卡。</summary>
    public bool picked { get; set; }

    /// <summary>
    /// 批次标识（r120，"写时幂等"用）：本批 offer 的卡 id 去重排序拼接（见
    /// <see cref="WakuuPersonalDedupe.BuildBatchKey"/>）。同一次卡牌奖励被 SL 重选时用它覆盖旧批次，
    /// 避免抓取率被重复计数。旧数据无此字段（空串）→ 不参与去重。
    /// </summary>
    public string batch { get; set; } = string.Empty;

    public long ts { get; set; }
}

/// <summary>
/// 事件选项记录：真人点选事件的一页时，该页每个选项各一行，chosen=true 表示本页点中。
/// eventId 用原版事件 id（EventModel.Id.Entry）；optionKey 用稳定 loc key（TextKey），
/// optionText 存展示原文兜底（语言相关，仅排查/展示用）。
/// </summary>
internal sealed class PersonalEventOptionRecord
{
    public string runKey { get; set; } = string.Empty;

    public bool isMulti { get; set; }

    public string character { get; set; } = string.Empty;

    public int act { get; set; } = 1;

    public string eventId { get; set; } = string.Empty;

    public string optionKey { get; set; } = string.Empty;

    public string optionText { get; set; } = string.Empty;

    public bool chosen { get; set; }

    public long ts { get; set; }
}

/// <summary>
/// 商店购买记录（用户口径①：只记「买了」这一种真人决策；「离店没买但钱够」成本高后置不做）。
/// kind = card / relic / potion 三个购买落点；删卡服务（花钱删牌）不算"买了什么"，不记
/// （删牌走牌组选牌，若以后要记，套手牌 Remove 优先级表的场景记录更合适）。
/// </summary>
internal sealed class PersonalShopPurchaseRecord
{
    public string runKey { get; set; } = string.Empty;

    public bool isMulti { get; set; }

    public string character { get; set; } = string.Empty;

    public int act { get; set; } = 1;

    /// <summary>购买类别：card / relic / potion（见 WakuuPersonalQuery.ShopKind*）。</summary>
    public string kind { get; set; } = string.Empty;

    /// <summary>买的物品裸 id（CardModel/RelicModel/PotionModel 的 Id.Entry，大写）。</summary>
    public string item { get; set; } = string.Empty;

    /// <summary>实际支付金币（ignoreCost / 免单时为 0）。</summary>
    public int goldSpent { get; set; }

    public long ts { get; set; }
}

/// <summary>
/// 删牌记录（真人把某张牌从牌库删掉一次 = 一行，Phase 4 商店删牌与事件/营地删牌共用）。
/// 只记真人决策；瓦库自动删牌（Phase 4 起）在作用域内排除，与购买记录口径一致。
/// 「被删概率」的口径：一张卡被删的次数 ÷ 同类切片内删牌总次数（删牌偏好占比），
/// 另可结合整局胜负归因（删掉它的局 vs 没删）给出删了更优/更差的信号。
/// </summary>
internal sealed class PersonalCardRemovalRecord
{
    public string runKey { get; set; } = string.Empty;

    public bool isMulti { get; set; }

    public string character { get; set; } = string.Empty;

    public int act { get; set; } = 1;

    /// <summary>被删的卡裸 id（CardModel.Id.Entry，大写）。</summary>
    public string card { get; set; } = string.Empty;

    public long ts { get; set; }
}

/// <summary>持久化根（json 顶层）。</summary>
internal sealed class PersonalStore
{
    public List<PersonalRunRecord> runs { get; set; } = new();

    public List<PersonalCardOfferRecord> cardOffers { get; set; } = new();

    public List<PersonalEventOptionRecord> eventChoices { get; set; } = new();

    public List<PersonalShopPurchaseRecord> shopPurchases { get; set; } = new();

    public List<PersonalCardRemovalRecord> cardRemovals { get; set; } = new();
}

/// <summary>个人卡牌统计切片结果（纯数据，供决策与单测断言）。</summary>
internal readonly struct PersonalCardSlice
{
    public PersonalCardSlice(long offered, long picked)
    {
        Offered = offered;
        Picked = picked;
    }

    public long Offered { get; }

    public long Picked { get; }

    public double PickRate => Offered > 0 ? (double)Picked / Offered : 0.0;
}

/// <summary>个人事件选项统计切片结果。</summary>
internal readonly struct PersonalEventSlice
{
    public PersonalEventSlice(long offered, long chosen)
    {
        Offered = offered;
        Chosen = chosen;
    }

    public long Offered { get; }

    public long Chosen { get; }

    public double ChosenRate => Offered > 0 ? (double)Chosen / Offered : 0.0;
}

/// <summary>
/// 胜负归因结果：held=这局点过该牌/选项，skipped=这局展示了但整局没点过。
/// abandon 局从胜率分母剔除（只影响胜率，不影响 offer/pick 计数）。
/// </summary>
internal readonly struct PersonalWinSlice
{
    public PersonalWinSlice(long heldRuns, long heldWins, long skippedRuns, long skippedWins)
    {
        HeldRuns = heldRuns;
        HeldWins = heldWins;
        SkippedRuns = skippedRuns;
        SkippedWins = skippedWins;
    }

    public long HeldRuns { get; }

    public long HeldWins { get; }

    public double WinRateHeld => HeldRuns > 0 ? (double)HeldWins / HeldRuns : 0.0;

    public long SkippedRuns { get; }

    public long SkippedWins { get; }

    public double WinRateSkipped => SkippedRuns > 0 ? (double)SkippedWins / SkippedRuns : 0.0;
}

/// <summary>
/// 个人偏好统计查询纯函数：从 PersonalStore 切片 + 归因。
/// 所有查询方法都要求 runKey 出现在 runs 里（result 为 win/loss）才计入胜负归因；
/// offer/pick 计数只按行过滤，不要求局已结束（跨会话存档中的记录仍可用于计数，
/// 只有胜负归因会等到局结束）。
/// </summary>
internal static class WakuuPersonalQuery
{
    /// <summary>
    /// 决策用最小样本量：个人数据量级远小于社区统计（SkadaHelper 为 200），
    /// 到 3 即可认为"对该卡有个人倾向"，不足视为无数据回退下一级。
    /// </summary>
    public const long DefaultMinPersonalCount = 3;

    /// <summary>判定为 win 的结果字符串。</summary>
    public const string ResultWin = "win";

    /// <summary>判定为 loss 的结果字符串。</summary>
    public const string ResultLoss = "loss";

    /// <summary>判定为 abandon 的结果字符串。</summary>
    public const string ResultAbandon = "abandon";

    /// <summary>商店购买记录类别：卡牌。</summary>
    public const string ShopKindCard = "card";

    /// <summary>商店购买记录类别：遗物。</summary>
    public const string ShopKindRelic = "relic";

    /// <summary>商店购买记录类别：药水。</summary>
    public const string ShopKindPotion = "potion";

    /// <summary>个人统计偏好档位：角色优先（默认，= 现状偏好链，行为零变化）。</summary>
    public const string PersonalTierCharacterFirst = "characterFirst";

    /// <summary>个人统计偏好档位：总量优先（跳过跨模式单角色档，样本集中在模式内与全量上）。</summary>
    public const string PersonalTierVolumeFirst = "volumeFirst";

    /// <summary>个人统计偏好档位：只看角色（绝不用别的角色的数据兜底，适合角色专属牌）。</summary>
    public const string PersonalTierCharacterOnly = "characterOnly";

    /// <summary>偏好链逐档放宽顺序：档位决定先放松哪一个轴（模式 / 角色），命中即停。</summary>
    private static IEnumerable<(bool UseMode, bool UseChar)> DecisionTiers(string? tier)
    {
        switch (tier)
        {
            case PersonalTierVolumeFirst:
                // 总量优先：①匹配模式×本角色 → ②匹配模式×任意角色 → ③全量。
                // 跨模式单角色档样本往往最稀疏，跳过它，样本集中在模式内与全量上。
                yield return (true, true);
                yield return (true, false);
                yield return (false, false);
                break;
            case PersonalTierCharacterOnly:
                // 只看角色：只信本角色数据（匹配模式 → 任意模式），不足即 null，
                // 绝不用别的角色的数据兜底（角色专属牌不该被别的角色的选择污染）。
                yield return (true, true);
                yield return (false, true);
                break;
            default:
                // characterFirst（默认，= v1 现状）：①匹配模式×本角色 → ②匹配模式×任意角色
                // → ③任意模式×本角色 → ④全量。角色先于全量，「模式」先于「角色」放宽。
                yield return (true, true);
                yield return (true, false);
                yield return (false, true);
                yield return (false, false);
                break;
        }
    }

    /// <summary>
    /// 决策用卡牌信号（偏好链第一档命中即返回）。
    /// 档位（tierPreference）决定放宽顺序，见 DecisionTiers：
    /// characterFirst（默认）= ①模式+角色 → ②模式 → ③角色 → ④全量（v1 现状，行为零变化）；
    /// volumeFirst = 跳过跨模式单角色档；characterOnly = 只信本角色数据、不足即 null。
    /// 多人局优先参考多人切片；角色不足再放宽。act/首次-重复维度完整记录在案，
    /// 但个人样本量不足以支撑到逐格切片做决策，故决策用放宽后的聚合信号。
    /// 全部档位都达不到 minOffered 时返回 null（无个人倾向）。
    /// </summary>
    public static WakuuCardSignal? TryGetCardDecisionSignal(
        PersonalStore store,
        string cardId,
        bool isMultiPreference,
        string? characterPreference,
        long minOffered = DefaultMinPersonalCount,
        string? tierPreference = PersonalTierCharacterFirst)
    {
        if (store == null || string.IsNullOrEmpty(cardId) || store.cardOffers.Count == 0)
        {
            return null;
        }

        PersonalCardSlice? best = null;
        bool bestUseMode = true;
        bool bestUseChar = true;
        foreach ((bool useMode, bool useChar) in DecisionTiers(tierPreference))
        {
            PersonalCardSlice slice = CountCardOffers(
                store, cardId,
                isMulti: useMode ? isMultiPreference : (bool?)null,
                character: useChar ? characterPreference : null);
            if (slice.Offered >= minOffered)
            {
                best = slice;
                bestUseMode = useMode;
                bestUseChar = useChar;
                break;
            }
        }

        if (best == null)
        {
            return null;
        }

        PersonalWinSlice win = CountCardWinSlice(store, cardId,
            isMulti: bestUseMode ? isMultiPreference : (bool?)null,
            character: bestUseChar ? characterPreference : null);
        PersonalCardSlice s = best.Value;
        return new WakuuCardSignal(cardId, s.PickRate, win.WinRateHeld, win.WinRateSkipped, s.Offered);
    }

    /// <summary>
    /// 统计某张卡在给定切片下的 offer/pick。
    /// 统计口径：只计入「已结束且非 abandon」的局（runs 表里有 win/loss 结果的行），
    /// 进行中/半途退出的局不计——保证与胜负归因同一批数据、无未完成局污染抓取率。
    /// </summary>
    public static PersonalCardSlice CountCardOffers(
        PersonalStore store,
        string cardId,
        bool? isMulti = null,
        string? character = null,
        int? act = null,
        bool? isRepeat = null)
    {
        if (store == null)
        {
            return new PersonalCardSlice(0, 0);
        }

        Dictionary<string, bool?> outcomeByRun = BuildRunOutcomeMap(store);
        IEnumerable<PersonalCardOfferRecord> rows = store.cardOffers
            .Where((r) => IsFinishedRun(outcomeByRun, r));
        rows = rows.Where((r) => string.Equals(r.card, cardId, StringComparison.OrdinalIgnoreCase));
        if (isMulti.HasValue)
        {
            rows = rows.Where((r) => r.isMulti == isMulti.Value);
        }

        if (!string.IsNullOrEmpty(character))
        {
            rows = rows.Where((r) => string.Equals(r.character, character, StringComparison.OrdinalIgnoreCase));
        }

        if (act.HasValue)
        {
            rows = rows.Where((r) => r.act == act.Value);
        }

        if (isRepeat.HasValue)
        {
            rows = rows.Where((r) => r.isRepeat == isRepeat.Value);
        }

        long offered = 0;
        long picked = 0;
        foreach (PersonalCardOfferRecord row in rows)
        {
            offered++;
            if (row.picked)
            {
                picked++;
            }
        }

        return new PersonalCardSlice(offered, picked);
    }

    /// <summary>该行所属局是否已结束（runs 表存在 win/loss 结果）。</summary>
    private static bool IsFinishedRun(Dictionary<string, bool?> outcomeByRun, PersonalCardOfferRecord row)
    {
        return row != null
               && !string.IsNullOrEmpty(row.runKey)
               && outcomeByRun.TryGetValue(row.runKey, out bool? outcome)
               && outcome.HasValue;
    }

    /// <summary>
    /// 卡牌胜负归因：以「局」为单位。held=该角色某局点过这张卡；skipped=该角色某局展示过但整局没点。
    /// 胜负结果取自 runs 表；abandon 局不进入任何一方的胜率分母。
    /// </summary>
    public static PersonalWinSlice CountCardWinSlice(
        PersonalStore store,
        string cardId,
        bool? isMulti = null,
        string? character = null)
    {
        if (store == null || store.runs.Count == 0)
        {
            return new PersonalWinSlice(0, 0, 0, 0);
        }

        Dictionary<string, bool?> resultByRun = BuildRunOutcomeMap(store);
        IEnumerable<PersonalCardOfferRecord> rows = store.cardOffers
            .Where((r) => string.Equals(r.card, cardId, StringComparison.OrdinalIgnoreCase));
        if (isMulti.HasValue)
        {
            rows = rows.Where((r) => r.isMulti == isMulti.Value);
        }

        if (!string.IsNullOrEmpty(character))
        {
            rows = rows.Where((r) => string.Equals(r.character, character, StringComparison.OrdinalIgnoreCase));
        }

        HashSet<string> heldRuns = new();
        HashSet<string> skippedRuns = new();
        foreach (PersonalCardOfferRecord row in rows)
        {
            bool? outcome = resultByRun.TryGetValue(row.runKey, out bool? value) ? value : null;
            if (outcome == null)
            {
                continue; // 局未结束或 abandon，不进胜负归因
            }

            if (row.picked)
            {
                heldRuns.Add(row.runKey);
            }
            else
            {
                skippedRuns.Add(row.runKey);
            }
        }

        // 一局内既点过也跳过 → 归入 held（点过是更强的信号）；从 skipped 里剔除
        foreach (string held in heldRuns)
        {
            skippedRuns.Remove(held);
        }

        long heldWins = heldRuns.Count((key) => resultByRun.TryGetValue(key, out bool? o) && o == true);
        long skippedWins = skippedRuns.Count((key) => resultByRun.TryGetValue(key, out bool? o) && o == true);
        return new PersonalWinSlice(heldRuns.Count, heldWins, skippedRuns.Count, skippedWins);
    }

    /// <summary>
    /// 决策用事件信号（档位放宽顺序与卡牌同一套 DecisionTiers；命中即返回）：
    /// characterFirst（默认）= ①模式+角色 → ②模式 → ③角色 → ④全量。
    /// </summary>
    public static WakuuEventSignal? TryGetEventDecisionSignal(
        PersonalStore store,
        string eventId,
        string optionKey,
        bool isMultiPreference,
        string? characterPreference,
        long minOffered = DefaultMinPersonalCount,
        string? tierPreference = PersonalTierCharacterFirst)
    {
        if (store == null || string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(optionKey)
            || store.eventChoices.Count == 0)
        {
            return null;
        }

        foreach ((bool useMode, bool useChar) in DecisionTiers(tierPreference))
        {
            PersonalEventSlice slice = CountEventOptionSlice(
                store, eventId, optionKey,
                isMulti: useMode ? isMultiPreference : (bool?)null,
                character: useChar ? characterPreference : null);
            if (slice.Offered < minOffered)
            {
                continue;
            }

            PersonalWinSlice win = CountEventWinSlice(
                store, eventId, optionKey,
                isMulti: useMode ? isMultiPreference : (bool?)null,
                character: useChar ? characterPreference : null);

            // 改进-3：选择率（slice.ChosenRate）原先被整条丢弃，导致事件侧只剩胜率单信号。
            // 「没选它的局」胜率只在**确实存在没选的局**时回填——SkippedRuns=0 时其值为 0.0，
            // 直接当基准会把该选项算成强正增益（假信号）。
            double? winRateSkipped = win.SkippedRuns > 0 ? win.WinRateSkipped : null;
            return new WakuuEventSignal(
                optionKey,
                win.WinRateHeld,
                slice.Offered,
                chosenRate: slice.ChosenRate,
                winRateSkipped: winRateSkipped);
        }

        return null;
    }

    /// <summary>事件选项 offer/chosen 计数（每人点选的一页里，每选项一行；只计已结束局）。</summary>
    public static PersonalEventSlice CountEventOptionSlice(
        PersonalStore store,
        string eventId,
        string optionKey,
        bool? isMulti = null,
        string? character = null,
        int? act = null)
    {
        if (store == null)
        {
            return new PersonalEventSlice(0, 0);
        }

        Dictionary<string, bool?> outcomeByRun = BuildRunOutcomeMap(store);
        IEnumerable<PersonalEventOptionRecord> rows = store.eventChoices
            .Where((r) => r != null && !string.IsNullOrEmpty(r.runKey)
                          && outcomeByRun.TryGetValue(r.runKey, out bool? outcome) && outcome.HasValue);
        rows = rows.Where((r) => string.Equals(r.eventId, eventId, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(r.optionKey, optionKey, StringComparison.Ordinal));
        if (isMulti.HasValue)
        {
            rows = rows.Where((r) => r.isMulti == isMulti.Value);
        }

        if (!string.IsNullOrEmpty(character))
        {
            rows = rows.Where((r) => string.Equals(r.character, character, StringComparison.OrdinalIgnoreCase));
        }

        if (act.HasValue)
        {
            rows = rows.Where((r) => r.act == act.Value);
        }

        long offered = 0;
        long chosen = 0;
        foreach (PersonalEventOptionRecord row in rows)
        {
            offered++;
            if (row.chosen)
            {
                chosen++;
            }
        }

        return new PersonalEventSlice(offered, chosen);
    }

    /// <summary>
    /// 事件选项胜负归因：held=该角色某局点过此选项；skipped=某局展示过但整局未点。
    /// 单局内点过即 held，abandon 不进分母。
    /// </summary>
    public static PersonalWinSlice CountEventWinSlice(
        PersonalStore store,
        string eventId,
        string optionKey,
        bool? isMulti = null,
        string? character = null)
    {
        if (store == null || store.runs.Count == 0)
        {
            return new PersonalWinSlice(0, 0, 0, 0);
        }

        Dictionary<string, bool?> resultByRun = BuildRunOutcomeMap(store);
        IEnumerable<PersonalEventOptionRecord> rows = store.eventChoices
            .Where((r) => string.Equals(r.eventId, eventId, StringComparison.OrdinalIgnoreCase)
                          && string.Equals(r.optionKey, optionKey, StringComparison.Ordinal));
        if (isMulti.HasValue)
        {
            rows = rows.Where((r) => r.isMulti == isMulti.Value);
        }

        if (!string.IsNullOrEmpty(character))
        {
            rows = rows.Where((r) => string.Equals(r.character, character, StringComparison.OrdinalIgnoreCase));
        }

        HashSet<string> heldRuns = new();
        HashSet<string> skippedRuns = new();
        foreach (PersonalEventOptionRecord row in rows)
        {
            bool? outcome = resultByRun.TryGetValue(row.runKey, out bool? value) ? value : null;
            if (outcome == null)
            {
                continue;
            }

            if (row.chosen)
            {
                heldRuns.Add(row.runKey);
            }
            else
            {
                skippedRuns.Add(row.runKey);
            }
        }

        foreach (string held in heldRuns)
        {
            skippedRuns.Remove(held);
        }

        long heldWins = heldRuns.Count((key) => resultByRun.TryGetValue(key, out bool? o) && o == true);
        long skippedWins = skippedRuns.Count((key) => resultByRun.TryGetValue(key, out bool? o) && o == true);
        return new PersonalWinSlice(heldRuns.Count, heldWins, skippedRuns.Count, skippedWins);
    }

    /// <summary>
    /// **商店购买胜负归因**（Phase 4 增量，2026-09-18）：held = 买了该商品（同 kind + item）的**局**；
    /// baseline（放在 <see cref="PersonalWinSlice.SkippedRuns"/> 里复用）= 同切片内**没买过它**的已结束局。
    ///
    /// ⚠ 与卡牌 / 事件的 skipped 不同：`shopPurchases` **只记"买了什么"、不记"商店摆出过什么"**，
    /// 所以算不出"摆出来但没买"（那要 offer 记录），基准只能取「其他已结束局」——
    /// 语义是「买过它的局 vs 其他局」，用于**负面否决**足够，别拿它做精细排序。
    /// 角色维度只作用于 held（`shopPurchases` 有 character）；`runs` 表没有角色字段，
    /// 故指定 character 时基准仍是该模式下的全量。
    /// abandon / 进行中的局不进任何一侧分母（与卡牌 / 事件同一口径）。
    /// </summary>
    public static PersonalWinSlice CountShopWinSlice(
        PersonalStore store,
        string kind,
        string item,
        bool? isMulti = null,
        string? character = null)
    {
        if (store == null || store.runs.Count == 0
            || string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(item))
        {
            return new PersonalWinSlice(0, 0, 0, 0);
        }

        Dictionary<string, bool?> resultByRun = BuildRunOutcomeMap(store);

        HashSet<string> heldRuns = new();
        foreach (PersonalShopPurchaseRecord row in store.shopPurchases)
        {
            if (row == null || string.IsNullOrEmpty(row.runKey))
            {
                continue;
            }

            if (!string.Equals(row.kind, kind, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(row.item, item, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (isMulti.HasValue && row.isMulti != isMulti.Value)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(character)
                && !string.Equals(row.character, character, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (resultByRun.TryGetValue(row.runKey, out bool? outcome) && outcome.HasValue)
            {
                heldRuns.Add(row.runKey);
            }
        }

        HashSet<string> baselineRuns = new();
        foreach (PersonalRunRecord run in store.runs)
        {
            if (run == null || string.IsNullOrEmpty(run.runKey))
            {
                continue;
            }

            if (isMulti.HasValue && run.isMulti != isMulti.Value)
            {
                continue;
            }

            if (!resultByRun.TryGetValue(run.runKey, out bool? outcome) || !outcome.HasValue)
            {
                continue; // 进行中 / abandon，不进分母
            }

            baselineRuns.Add(run.runKey);
        }

        // 买过它的局从基准里剔除：held 与 baseline 必须互斥
        foreach (string held in heldRuns)
        {
            baselineRuns.Remove(held);
        }

        long heldWins = heldRuns.Count((key) => resultByRun.TryGetValue(key, out bool? o) && o == true);
        long baselineWins = baselineRuns.Count((key) => resultByRun.TryGetValue(key, out bool? o) && o == true);
        return new PersonalWinSlice(heldRuns.Count, heldWins, baselineRuns.Count, baselineWins);
    }

    /// <summary>
    /// 决策用商店信号（Phase 4 增量，2026-09-18）：档位放宽顺序与卡牌 / 事件同一套
    /// <see cref="DecisionTiers"/>；命中条件 = **买过它的已结束局数 ≥ <paramref name="minBought"/>**
    /// **且基准局数 &gt; 0**（没有基准就没有增益可言，宁可当无数据）。
    /// 全部档位都不足样本 → null（调用方回退"价格 + 金币保底"）。
    /// </summary>
    public static WakuuShopSignal? TryGetShopDecisionSignal(
        PersonalStore store,
        string kind,
        string item,
        bool isMultiPreference,
        string? characterPreference,
        long minBought = DefaultMinPersonalCount,
        string? tierPreference = PersonalTierCharacterFirst)
    {
        if (store == null || string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(item)
            || store.shopPurchases.Count == 0)
        {
            return null;
        }

        foreach ((bool useMode, bool useChar) in DecisionTiers(tierPreference))
        {
            PersonalWinSlice win = CountShopWinSlice(
                store, kind, item,
                isMulti: useMode ? isMultiPreference : (bool?)null,
                character: useChar ? characterPreference : null);
            if (win.HeldRuns < minBought || win.SkippedRuns == 0)
            {
                continue;
            }

            return new WakuuShopSignal(
                kind, item, win.HeldRuns, win.WinRateHeld, win.SkippedRuns, win.WinRateSkipped);
        }

        return null;
    }

    /// <summary>
    /// 统计某张卡在给定切片下被真人删掉过多少次（删牌统计，Phase 4）。
    /// 只计已结束且非 abandon 的局（与 offer/pick 同一批数据，避免未完成局污染）。
    /// </summary>
    public static long CountCardRemovals(
        PersonalStore store,
        string cardId,
        bool? isMulti = null,
        string? character = null,
        int? act = null)
    {
        if (store == null || string.IsNullOrEmpty(cardId) || store.cardRemovals.Count == 0)
        {
            return 0;
        }

        Dictionary<string, bool?> outcomeByRun = BuildRunOutcomeMap(store);
        IEnumerable<PersonalCardRemovalRecord> rows = store.cardRemovals
            .Where((r) => r != null && !string.IsNullOrEmpty(r.runKey) && r.card != null
                          && outcomeByRun.TryGetValue(r.runKey, out bool? outcome) && outcome.HasValue);
        rows = rows.Where((r) => string.Equals(r.card, cardId, StringComparison.OrdinalIgnoreCase));
        if (isMulti.HasValue)
        {
            rows = rows.Where((r) => r.isMulti == isMulti.Value);
        }

        if (!string.IsNullOrEmpty(character))
        {
            rows = rows.Where((r) => string.Equals(r.character, character, StringComparison.OrdinalIgnoreCase));
        }

        if (act.HasValue)
        {
            rows = rows.Where((r) => r.act == act.Value);
        }

        long count = 0;
        foreach (PersonalCardRemovalRecord row in rows)
        {
            count++;
        }

        return count;
    }

    /// <summary>同类切片内的删牌总次数（「被删概率」的分母：某张卡被删次数 ÷ 该切片删牌总数 = 删牌偏好占比）。</summary>
    public static long CountAllCardRemovals(
        PersonalStore store,
        bool? isMulti = null,
        string? character = null,
        int? act = null)
    {
        if (store == null || store.cardRemovals.Count == 0)
        {
            return 0;
        }

        Dictionary<string, bool?> outcomeByRun = BuildRunOutcomeMap(store);
        IEnumerable<PersonalCardRemovalRecord> rows = store.cardRemovals
            .Where((r) => r != null && !string.IsNullOrEmpty(r.runKey) && r.card != null
                          && outcomeByRun.TryGetValue(r.runKey, out bool? outcome) && outcome.HasValue);
        if (isMulti.HasValue)
        {
            rows = rows.Where((r) => r.isMulti == isMulti.Value);
        }

        if (!string.IsNullOrEmpty(character))
        {
            rows = rows.Where((r) => string.Equals(r.character, character, StringComparison.OrdinalIgnoreCase));
        }

        if (act.HasValue)
        {
            rows = rows.Where((r) => r.act == act.Value);
        }

        long count = 0;
        foreach (PersonalCardRemovalRecord row in rows)
        {
            count++;
        }

        return count;
    }

    /// <summary>构建 runKey → 是否胜利 的映射；未结束/abandon 的局为 null（不进胜率分母）。</summary>
    public static Dictionary<string, bool?> BuildRunOutcomeMap(PersonalStore store)
    {
        Dictionary<string, bool?> map = new();
        if (store?.runs == null)
        {
            return map;
        }

        // 一局只留最新一条 run 记录（正常情况下每局只有一条）
        foreach (PersonalRunRecord run in store.runs)
        {
            if (run == null || string.IsNullOrEmpty(run.runKey))
            {
                continue;
            }

            bool? outcome = run.result switch
            {
                ResultWin => true,
                ResultLoss => false,
                _ => null, // ongoing / abandon 不进胜率分母
            };
            map[run.runKey] = outcome;
        }

        return map;
    }

    /// <summary>
    /// 清理 stale：删除「未在 runs 表中结束（win/loss）」且最后一条记录早于 maxAgeDays 天的孤立数据。
    /// 保留最近未结束局的数据用于跨会话归因；只清理长时间无结果的旧数据，防止 JSON 无限膨胀。
    /// </summary>
    public static void PruneStale(PersonalStore store, long nowMs, int maxAgeDays = 60)
    {
        if (store == null)
        {
            return;
        }

        long cutoff = nowMs - maxAgeDays * 24L * 60 * 60 * 1000;
        HashSet<string> finished = new(store.runs
            .Where((r) => r != null && !string.IsNullOrEmpty(r.runKey)
                          && (r.result == ResultWin || r.result == ResultLoss))
            .Select((r) => r.runKey));

        HashSet<string> activeRuns = new();
        foreach (PersonalCardOfferRecord row in store.cardOffers)
        {
            if (row != null && !string.IsNullOrEmpty(row.runKey))
            {
                activeRuns.Add(row.runKey);
            }
        }

        foreach (PersonalEventOptionRecord row in store.eventChoices)
        {
            if (row != null && !string.IsNullOrEmpty(row.runKey))
            {
                activeRuns.Add(row.runKey);
            }
        }

        foreach (PersonalShopPurchaseRecord row in store.shopPurchases)
        {
            if (row != null && !string.IsNullOrEmpty(row.runKey))
            {
                activeRuns.Add(row.runKey);
            }
        }

        foreach (PersonalCardRemovalRecord row in store.cardRemovals)
        {
            if (row != null && !string.IsNullOrEmpty(row.runKey))
            {
                activeRuns.Add(row.runKey);
            }
        }

        foreach (string runKey in activeRuns.ToList())
        {
            if (finished.Contains(runKey))
            {
                continue;
            }

            long maxTs = 0;
            foreach (PersonalCardOfferRecord row in store.cardOffers)
            {
                if (row != null && row.runKey == runKey)
                {
                    maxTs = Math.Max(maxTs, row.ts);
                }
            }

            foreach (PersonalEventOptionRecord row in store.eventChoices)
            {
                if (row != null && row.runKey == runKey)
                {
                    maxTs = Math.Max(maxTs, row.ts);
                }
            }

            foreach (PersonalShopPurchaseRecord row in store.shopPurchases)
            {
                if (row != null && row.runKey == runKey)
                {
                    maxTs = Math.Max(maxTs, row.ts);
                }
            }

            foreach (PersonalCardRemovalRecord row in store.cardRemovals)
            {
                if (row != null && row.runKey == runKey)
                {
                    maxTs = Math.Max(maxTs, row.ts);
                }
            }

            if (maxTs < cutoff)
            {
                store.cardOffers.RemoveAll((row) => row != null && row.runKey == runKey);
                store.eventChoices.RemoveAll((row) => row != null && row.runKey == runKey);
                store.shopPurchases.RemoveAll((row) => row != null && row.runKey == runKey);
                store.cardRemovals.RemoveAll((row) => row != null && row.runKey == runKey);
            }
        }
    }
}

/// <summary>个人统计 JSON 解析/序列化纯函数（无 IO；镜像 WakuuConfigJson 风格）。</summary>
internal static class WakuuPersonalJson
{
    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static PersonalStore? Parse(string json)
    {
        return JsonSerializer.Deserialize<PersonalStore>(json, ParseOptions);
    }

    public static string Serialize(PersonalStore store)
    {
        return JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
    }
}
