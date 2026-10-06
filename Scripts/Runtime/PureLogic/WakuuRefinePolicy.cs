#nullable enable

using System;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「炼化」（瓦库四功能之三）的**纯逻辑**：数值档位归一 + 收编量计算 + 选项可见性判据。
///
/// 功能（提案《瓦库炼化净化联合地狱战神-功能提案与可行性分析》§2，2026-10-05 拍板数值照原案）：
/// 休息处真人可对某个瓦库执行炼化 —— 代价是把该瓦库的当前血量与血上限清零（<c>SetMaxHpInternal(0)</c>
/// + <c>Kill(force:true)</c>，见核验报告 §2.1），收益是：
/// <list type="bullet">
/// <item>按比例收编它的**血量与血上限**（<see cref="ResolveHpTransfer"/>，默认全部）；</item>
/// <item>从它卡组**自选**若干张牌（上限档位 不限 / 20 / 5，<see cref="ResolveCardLimit"/>）；</item>
/// <item>从它身上**自选**若干件遗物（件数档位 1 / 3 / 5 / 不限，<see cref="ResolveRelicLimit"/>；
///   排除【永久低语耳环】与【瓦库形态】，<see cref="IsExcludedRelic"/>）。</item>
/// </list>
///
/// **席位归宿（2026-10-05 用户拍板）**：不摘名单、不做切人过滤、不做存档标记 ——
/// 炼化 = 清血 + 清上限 + 转移资源，席位留在名单里当"死者滞留"，
/// 且接受"日后被加回血上限就堂堂复活"（用户原话「炼化了≠死透了」）。
/// 核验报告 §2.2 原本要求的三件配套（名单摘除 / 切换过滤 / 存档标记）**全部取消**。
///
/// 本类只做可单测的换算，一切游戏对象访问在 <see cref="RefineWakuuRestSiteRuntime"/> 里。
/// </summary>
internal static class WakuuRefinePolicy
{
    // ---- 血量/血上限收编比例档位 ----

    /// <summary>收编全部（100%）。</summary>
    public const string HpRatioAll = "all";

    /// <summary>收编一半（50%）。</summary>
    public const string HpRatioHalf = "half";

    /// <summary>收编四分之一（25%）。</summary>
    public const string HpRatioQuarter = "quarter";

    // ---- 卡组自选张数上限档位 ----

    /// <summary>不限张数（上限 = 该瓦库卡组当前张数）。</summary>
    public const string CardLimitAny = "any";

    /// <summary>最多 20 张。</summary>
    public const string CardLimit20 = "20";

    /// <summary>最多 5 张。</summary>
    public const string CardLimit5 = "5";

    // ---- 遗物自选件数档位（r219；用户：只让拿 1 件则炼化收益太低，"完全不如让瓦库活着"）----

    /// <summary>最多 1 件遗物（= r216~r218 的旧行为）。</summary>
    public const string RelicLimit1 = "1";

    /// <summary>最多 3 件遗物。</summary>
    public const string RelicLimit3 = "3";

    /// <summary>最多 5 件遗物。</summary>
    public const string RelicLimit5 = "5";

    /// <summary>不限件数（上限 = 该瓦库身上可收编的遗物数）。</summary>
    public const string RelicLimitAny = "any";

    /// <summary>
    /// 卡组自选界面的最小选取张数 = **0**（r220，用户口径：「炼化瓦库时不能一张牌都不要」会
    /// **妨碍玩小卡组**）。
    /// ⚠ 代价：`Min=0` 时"确认 0 张"与"取消/关闭界面"都会返回空表、**无法区分** ⇒
    /// 选牌这一步不再有"取消 = 中止炼化"的能力（空返回一律按"不拿牌、继续炼化"处理）。
    /// 想中止整次炼化请在**选目标 / 选遗物 / 选药水**三步点取消（那三步都能区分取消与确认）。
    /// </summary>
    public const int MinCardsToPick = 0;

    /// <summary>归一血量比例档位（all / half / quarter）；空值 / 非法一律回 <see cref="HpRatioAll"/>。</summary>
    public static string NormalizeHpRatio(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return HpRatioAll;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            HpRatioHalf => HpRatioHalf,
            HpRatioQuarter => HpRatioQuarter,
            _ => HpRatioAll,
        };
    }

    /// <summary>归一卡组档位（any / 20 / 5）；空值 / 非法一律回 <see cref="CardLimitAny"/>。</summary>
    public static string NormalizeCardLimit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CardLimitAny;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            CardLimit20 => CardLimit20,
            CardLimit5 => CardLimit5,
            _ => CardLimitAny,
        };
    }

    /// <summary>档位 → 收编系数（0.25 / 0.5 / 1）。非法档位按全部处理。</summary>
    public static decimal ResolveHpRatioFactor(string? ratioKey)
    {
        return NormalizeHpRatio(ratioKey) switch
        {
            HpRatioHalf => 0.5m,
            HpRatioQuarter => 0.25m,
            _ => 1m,
        };
    }

    /// <summary>
    /// 收编的血量/血上限增量 = 该瓦库**血上限** × 系数，向下取整且不小于 0。
    ///
    /// 用血上限（而非当前血量）做基数：收益走 <c>CreatureCmd.GainMaxHp</c>，它同时加血上限并回复等量当前血量
    /// （核验报告 §2.3），所以"获得多少血"与"获得多少上限"是同一个数字。
    /// </summary>
    public static decimal ResolveHpTransfer(decimal sourceMaxHp, string? ratioKey)
    {
        if (sourceMaxHp <= 0m)
        {
            return 0m;
        }

        decimal amount = Math.Floor(sourceMaxHp * ResolveHpRatioFactor(ratioKey));
        return amount > 0m ? amount : 0m;
    }

    /// <summary>
    /// 档位 + 卡组张数 → 卡组自选界面的**最大可选张数**（至少 0；不会超过卡组实际张数）。
    /// </summary>
    public static int ResolveCardLimit(string? limitKey, int deckCardCount)
    {
        if (deckCardCount <= 0)
        {
            return 0;
        }

        int limit = NormalizeCardLimit(limitKey) switch
        {
            CardLimit20 => 20,
            CardLimit5 => 5,
            _ => deckCardCount,
        };

        return Math.Min(limit, deckCardCount);
    }

    /// <summary>归一遗物件数档位（1 / 3 / 5 / any）；空值 / 非法一律回 <see cref="RelicLimitAny"/>。</summary>
    public static string NormalizeRelicLimit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return RelicLimitAny;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            RelicLimit1 => RelicLimit1,
            RelicLimit3 => RelicLimit3,
            RelicLimit5 => RelicLimit5,
            _ => RelicLimitAny,
        };
    }

    /// <summary>
    /// 档位 + 可收编遗物数 → 遗物多选弹层的**最大可选件数**（至少 0；不会超过实际候选数）。
    /// 默认档 <see cref="RelicLimitAny"/> = 不限（可全拿）—— 用户口径：只让拿 1 件则炼化收益太低，
    /// 「完全不如让瓦库活着」。
    /// </summary>
    public static int ResolveRelicLimit(string? limitKey, int candidateCount)
    {
        if (candidateCount <= 0)
        {
            return 0;
        }

        int limit = NormalizeRelicLimit(limitKey) switch
        {
            RelicLimit1 => 1,
            RelicLimit3 => 3,
            RelicLimit5 => 5,
            _ => candidateCount,
        };

        return Math.Min(limit, candidateCount);
    }

    /// <summary>
    /// 炼化选项是否可用：功能开关开 + 本地单机冒险会话 + 至少一个候选瓦库。
    /// （与净化同一套可见性口径；目标枚举在运行时层。）
    /// </summary>
    public static bool ShouldShowOption(bool featureEnabled, bool coopSessionActive, int candidateCount)
    {
        return featureEnabled && coopSessionActive && candidateCount > 0;
    }

    /// <summary>
    /// 是否属于**不可被收编**的遗物：【永久低语耳环】（<c>LocalWakuuStarterRelic</c>）与
    /// 【瓦库形态】（<c>LocalWakuuFormRelic</c>）—— 它们是"谁是瓦库"的身份定义
    /// （核验报告 §2.4 / 提案 §2.4），且移除会被 mod 的托管守卫拦下。
    /// </summary>
    public static bool IsExcludedRelic(bool isWhisperingEarring, bool isVakuuFormRelic)
    {
        return isWhisperingEarring || isVakuuFormRelic;
    }

    /// <summary>
    /// 炼化是否成立：目标存活 且 血上限 &gt; 0（提案 §2.1 目标筛选；已清过血上限的瓦库不再作为目标）。
    /// </summary>
    public static bool IsEligibleTarget(bool isAlive, int targetMaxHp)
    {
        return isAlive && targetMaxHp > 0;
    }

    /// <summary>
    /// 是否要跑"收编卡牌 / 收编遗物"两步（开关 `refineTakeVakuuAssets`）。
    /// 关掉 ⇒ 炼化只做"按比例收编血量 + 把它炼掉"，瓦库的卡组与遗物原样留在它身上 ——
    /// 给"日后复活瓦库"的玩法留活路（2026-10-06 用户要求，动机：核心牌被拿走 ⇒ 复活也白费）。
    /// </summary>
    public static bool ShouldTakeAssets(bool takeAssetsEnabled)
    {
        return takeAssetsEnabled;
    }

    /// <summary>
    /// 是否要跑"药水收编"这一步：开关开 + 瓦库有药水 + **真人药水栏有空位**。
    /// 没空位就整步跳过、**不动瓦库药水**（拿不到还要删是纯亏；用户动机是"收编"，不是惩罚）。
    /// </summary>
    public static bool ShouldOfferPotionStep(bool takePotionsEnabled, int vakuuPotionCount, int freePotionSlots)
    {
        return takePotionsEnabled && vakuuPotionCount > 0 && freePotionSlots > 0;
    }

    /// <summary>
    /// 本次最多能拿几瓶 = min(瓦库药水数, 真人药水栏空位数)，不小于 0。
    /// 口径与"战斗奖励拿药水"一致：**没空位就拿不了**（多选弹层会把这个上限显示出来并卡住勾选）。
    /// </summary>
    public static int ResolvePotionTakeLimit(int vakuuPotionCount, int freePotionSlots)
    {
        return Math.Max(Math.Min(vakuuPotionCount, freePotionSlots), 0);
    }
}
