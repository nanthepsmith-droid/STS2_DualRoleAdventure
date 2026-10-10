#nullable enable

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「托管自动出牌」的**席位判据**与**出牌上限**（纯逻辑，可单测）。
///
/// Phase 3（战灵召唤 R11）：战神（临时席位）**没有**瓦库接管遗物，但同样要交给同一条托管出牌链
/// （看门狗 → <c>ExecuteBeforePlayPhaseStartAsync</c>）。判据必须**显式认临时席位** ——
/// 不能靠"持有接管遗物"蕴含"本地席位"：瓦库名单兜底走的 <c>IsWakuuEnabled</c> 蕴含
/// `_localPlayerIds`，而临时席位是**刻意不进那张全表**的（见 references 坑 AE-1/AE-3）。
///
/// 也把"每回合出牌上限"一并收进来：战神与瓦库形态一样按"**全量出牌**"（护栏 60），
/// 否则先古/稀有档那一大把牌打不完，档位强度体现不出来（Phase 2b 的门面）。
/// </summary>
internal static class WakuuAutoplaySeatPolicy
{
    /// <summary>默认每回合出牌上限（原版低语耳环口径）。</summary>
    internal const int MaxCardsDefault = 13;

    /// <summary>"全量出牌"护栏上限（正常牌组远达不到，只为防御异常效果导致的死循环）。</summary>
    internal const int MaxCardsFull = 60;

    /// <summary>该席位是否由托管链自动出牌：持有接管遗物，**或**是临时席位（战神）。</summary>
    internal static bool ShouldAutoplay(bool hasTakeoverRelic, bool isTemporarySeat)
        => hasTakeoverRelic || isTemporarySeat;

    /// <summary>
    /// 是否按"全量出牌"上限：瓦库形态**或临时席位**，且玩家开了「打光所有手牌」
    /// （关掉时一律沿用原版 13 张上限）。
    /// </summary>
    internal static bool UsesFullPlayLimit(bool vakuuFormMode, bool isTemporarySeat, bool playAllCards)
        => (vakuuFormMode || isTemporarySeat) && playAllCards;

    /// <summary>按"是否全量"取每回合出牌上限。</summary>
    internal static int ResolveMaxCardsPerTurn(bool fullPlay)
        => fullPlay ? MaxCardsFull : MaxCardsDefault;

    /// <summary>
    /// 该席位是否按**瓦库托管出牌路径**（队列 / 并发 / 跳过演出）：瓦库形态 **或临时席位（战神）**。
    ///
    /// ⚠ 这条决定"要不要抢全局 `SelectorScopeGate`、要不要把 `LocalContext.NetId` 钉在这个席位身上"
    /// （见 `LocalWakuuRelicRuntime.UsesHostedPlayPath` 的注释）—— 临时席位**必须**为 true，
    /// 否则它出牌的那几秒里原版会把"本机玩家"当成战神 ⇒ **真人点不出牌**（r251 实机）。
    /// </summary>
    internal static bool UsesHostedPlayPath(bool vakuuFormMode, bool isTemporarySeat)
        => vakuuFormMode || isTemporarySeat;
}
