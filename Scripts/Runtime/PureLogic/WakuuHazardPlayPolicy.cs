using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 托管出牌时的**手牌分档**（2026-11-10 用户口径：瓦库托管要处理诅咒牌与状态牌）。
///
/// 背景（原版 `CardModel.CanPlay()` 只看"能不能打"，不看种类）：
/// 诅咒/状态牌里有一小部分**可以打出**，托管以前把它们和普通牌混在一起按"最左/评分"处理
/// ⇒ ① 能量被普通牌打空后，本来该救命的牌打不出（狂乱逃离：沙坑倒计时归零会被一口吞掉）；
/// ② 或者反过来，把"打出去只是白耗 1 费"的牌提前用掉，没给【烧牌/弃牌】留下消耗它的机会。
/// </summary>
internal enum WakuuHandHazardTier
{
    /// <summary>普通牌（攻击/技能/能力/任务等）：按原有逻辑走。</summary>
    Normal = 0,

    /// <summary>
    /// **兜底档**：可打出的诅咒/状态牌，打出去只是清手/白耗能量（如 `SPORE_MIND` / `SLIMED`）。
    /// 排在所有普通牌之后 —— 先打普通牌能给"消耗它"的效果（消耗/弃牌）留机会，省下这份能量。
    /// </summary>
    Deferred = 1,

    /// <summary>
    /// **保命档**：留在手里会掉血/会去世的牌（原版 `TOXIC` / `BECKON` 这类带
    /// `HasTurnEndInHandEffect` 的，以及沙坑里的 `FRANTIC_ESCAPE`）。
    /// 必须**先打**：拖到最后一旦能量被打空、或回合被熔断收口，就真的掉血/去世。
    /// </summary>
    Survival = 2,
}

/// <summary>单张手牌的分档判据（全部由运行层从游戏模型读出，纯逻辑只做判定）。</summary>
internal readonly struct WakuuHandTierEntry
{
    public WakuuHandTierEntry(WakuuHandHazardTier tier, bool canPlay)
    {
        Tier = tier;
        CanPlay = canPlay;
    }

    public WakuuHandHazardTier Tier { get; }

    /// <summary>当前能量下**能不能打**（只有 <see cref="WakuuHandHazardTier.Survival"/> 档需要这个字段）。</summary>
    public bool CanPlay { get; }
}

/// <summary>
/// 诅咒/状态牌的分档与出牌次序（纯逻辑，可单测）。
///
/// 三档（用户 2026-11-10 拍板 B 方案）：
/// <list type="number">
/// <item><b>保命档 = 立即打</b>：判据不硬编码卡名 —— 游戏自己有公开属性
///   `CardModel.HasTurnEndInHandEffect`（`Toxic` / `Beckon` / `Doubt` 等都覆写它），
///   第三方 mod 的"不打出就去世"诅咒只要按这个惯例实现就能被认出来；
///   唯一的名单项是 `FRANTIC_ESCAPE`（狂乱逃离）：它没有回合结束效果，危险来自
///   `SandpitPower` 倒计时（归零时 `AfterRemoved` 直接 `Kill(force:true)` 吞人），
///   所以判据是"名单内 + 自己身上确实有沙坑"。</item>
/// <item><b>兜底档 = 延后</b>：其它可打出的诅咒/状态牌。排在普通牌之后，**不做能量硬预留**
///   —— 打不出它没有代价，而硬预留会把有价值的牌挤掉（能量 3 + 手里 3 费大招 + 1 费 `SLIMED`
///   ⇒ 大招反而打不出）。"先打其它牌"通过排序实现，效果等价且不会牺牲真牌。</item>
/// <item><b>普通档</b>：原有逻辑完全不变。</item>
/// </list>
/// </summary>
internal static class WakuuHazardPlayPolicy
{
    /// <summary>
    /// 保命名单：**id 全大写**（游戏 `StringHelper.Slugify(类型名)` 的产物）。
    /// 目前只有狂乱逃离（沙坑倒计时）；新增时务必同时给出"触发条件"的判据，别做成无条件优先。
    /// </summary>
    public const string FranticEscapeId = "FRANTIC_ESCAPE";

    /// <summary>
    /// 分档。
    /// </summary>
    /// <param name="isCurseOrStatus">是不是诅咒/状态牌（`CardType.Curse` / `CardType.Status`）。</param>
    /// <param name="hasTurnEndInHandEffect">游戏 `CardModel.HasTurnEndInHandEffect`（留在手里会有回合结束效果）。</param>
    /// <param name="isSurvivalListed">是否在保命名单里（目前只有狂乱逃离）。</param>
    /// <param name="survivalConditionMet">名单项自己的触发条件是否成立（狂乱逃离 = 自己被沙坑抓、且倒计时已经不多）。</param>
    public static WakuuHandHazardTier Classify(
        bool isCurseOrStatus,
        bool hasTurnEndInHandEffect,
        bool isSurvivalListed = false,
        bool survivalConditionMet = false)
    {
        // 非诅咒/状态牌一律普通牌 —— 第 0 条快速路径（性能：绝大多数手牌走这里）。
        if (!isCurseOrStatus)
        {
            return WakuuHandHazardTier.Normal;
        }

        // 保命档 ①：名单项且条件成立（狂乱逃离 + 沙坑）。
        if (isSurvivalListed && survivalConditionMet)
        {
            return WakuuHandHazardTier.Survival;
        }

        // 保命档 ②：留手里有回合结束效果（掉血/失去生命）。打不打得出去由运行层用 `CanPlay` 判。
        if (hasTurnEndInHandEffect)
        {
            return WakuuHandHazardTier.Survival;
        }

        return WakuuHandHazardTier.Deferred;
    }

    /// <summary>
    /// 【沙坑倒计时】≤ 这个值 ⇒ 狂乱逃离按**保命档**处理；超过则归兜底档。
    ///
    /// 2026-11-10 用户口径：「计数够多的时候保底档，计数少的时候才保命档」。
    /// 定 3 的理由：
    /// <list type="bullet">
    /// <item>沙坑每**敌方回合开始**扣 1，计数归零即刻被吞（`SandpitPower.AfterRemoved` 直接 `Kill`）；
    ///   所以"本回合不补"的安全余量 = 计数 − 1 个自己的回合；</item>
    /// <item>【狂乱逃离】**没有 Retain**（打完/回合结束就进弃牌堆）⇒ 想"下一回合再补"要重新抽到，
    ///   不能指望它一定在手；</item>
    /// <item>计数 ≤ 3 时再拖着补，一旦这回合没抽到就直接被吞 —— 不值得为 1 点能量冒团灭风险。</item>
    /// </list>
    /// 计数 4（该 BOSS 的初始值）时还有 3 个回合余量 ⇒ 归兜底档，先打普通牌、能量有富余再顺手补。
    /// </summary>
    public const int SandpitUrgentTurns = 3;

    /// <summary>
    /// 沙坑倒计时是否已经"该补了"（`turnsRemaining` = 当前计数；0/负数 = 没被沙坑抓）。
    /// </summary>
    public static bool IsSandpitUrgent(int turnsRemaining)
    {
        return turnsRemaining > 0 && turnsRemaining <= SandpitUrgentTurns;
    }

    /// <summary>
    /// 这一手牌里**有没有"现在就能打"的保命牌**。
    /// 有 ⇒ 本轮候选集只留保命牌（先把它打出去，再谈别的）。
    /// </summary>
    public static bool ShouldPrioritizeSurvival(IReadOnlyList<WakuuHandTierEntry> hand)
    {
        for (int i = 0; i < hand.Count; i++)
        {
            if (hand[i].Tier == WakuuHandHazardTier.Survival && hand[i].CanPlay)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 给出本轮的**出牌候选顺序**（返回手牌下标）。
    /// <list type="bullet">
    /// <item>有能打的保命牌 ⇒ 只返回保命牌（其余牌这一轮不给大脑看）；</item>
    /// <item>否则 ⇒ 普通牌（保持原顺序）在前，兜底牌在后。</item>
    /// </list>
    /// 未分档的手牌（全普通牌）返回 `null`，调用方直接复用原列表（零拷贝、行为不变）。
    /// </summary>
    public static IReadOnlyList<int>? ResolvePlayOrder(IReadOnlyList<WakuuHandTierEntry> hand)
    {
        bool anyHazard = false;
        for (int i = 0; i < hand.Count; i++)
        {
            if (hand[i].Tier != WakuuHandHazardTier.Normal)
            {
                anyHazard = true;
                break;
            }
        }

        if (!anyHazard)
        {
            return null; // 全普通牌：交给原逻辑，零开销
        }

        var order = new List<int>(hand.Count);

        if (ShouldPrioritizeSurvival(hand))
        {
            for (int i = 0; i < hand.Count; i++)
            {
                if (hand[i].Tier == WakuuHandHazardTier.Survival)
                {
                    order.Add(i);
                }
            }

            return order;
        }

        // 普通牌在前（保持原顺序），兜底牌在后
        for (int i = 0; i < hand.Count; i++)
        {
            if (hand[i].Tier == WakuuHandHazardTier.Normal)
            {
                order.Add(i);
            }
        }

        for (int i = 0; i < hand.Count; i++)
        {
            if (hand[i].Tier == WakuuHandHazardTier.Deferred)
            {
                order.Add(i);
            }
        }

        // 走到这里说明"保命牌在手里但现在的能量打不动"：仍然留在候选集里（排在最后），
        // 万一中途有加能量的效果让它变得能打，也不至于被我们吞掉。
        for (int i = 0; i < hand.Count; i++)
        {
            if (hand[i].Tier == WakuuHandHazardTier.Survival)
            {
                order.Add(i);
            }
        }

        return order;
    }
}
