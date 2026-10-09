#nullable enable
using System;

// 命名空间与同目录的 WakuuRefinePolicy / WakuuDaddyPolicy 保持一致（本仓 `PureLogic/` 下多数纯逻辑类
// 都用扁平命名空间 `...Runtime`），调用方无需额外 using。
namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 战神档位（D7 草案的三档 + 先古档）。成本表见 <see cref="HellwarSummonPolicy.ResolveSummonCost"/>。
/// 普通 / 罕见 / 稀有 / 先古 = 强度递增 ⇒ 计数成本递增（R5）。
/// </summary>
internal enum HellwarWarGodTier
{
    /// <summary>普通档（默认档，Phase 1/2 先做）：运行期读角色自身的初始遗物 + 初始卡组。</summary>
    Common = 0,

    /// <summary>罕见档：预设卡组（后续轮次补）。</summary>
    Rare = 1,

    /// <summary>稀有档：预设卡组（更强，后续轮次补）。</summary>
    Epic = 2,

    /// <summary>先古档：预设卡组（最强，后续轮次补；【终极九连环】依赖它）。</summary>
    Ancient = 3,
}

/// <summary>
/// 战灵召唤（原 ④「地狱战神」）#21 的**纯逻辑**：计数（遗物【地狱耳环】的战力预算）相关的
/// 数值换算与判据 —— 强度→成本（R5）/ 是否可召唤（R6）/ 休息处【分裂精神】的扣血档位（R2b / D13）。
///
/// 本类**不碰任何 Godot / 游戏类型**，便于单测（见 `tests/LocalMultiControl.Tests/HellwarSummonPolicyTests.cs`）。
///
/// 口径出处（design doc #21）：
/// <list type="bullet">
/// <item>D7 成本表：普通 1 / 罕见 2 / 稀有 3 / 先古 4；</item>
/// <item>R6：计数耗尽或玩家已满（<c>MaxLocalPlayerCount = 12</c>）⇒ 不能召唤；</item>
/// <item>D13：扣血基数 = **当前血量**、向下取整、**clamp ≥1（不可扣死）**、档位 10% / 15%（默认）/ 20%。</item>
/// </list>
/// ⚠ 两个"大招"召唤（R16【无尽战神】/ R17【终极九连环】）**尚未拍板**（doc §7.2 D14~D19）⇒ 本类**先不落**它们的成本。
/// </summary>
internal static class HellwarSummonPolicy
{
    /// <summary>遗物【地狱耳环】的计数名（显示在遗物格子上的数字）。</summary>
    internal const int InitialSpiritCount = 0;

    /// <summary>休息处【分裂精神】每次使用给地狱耳环增加的计数（R2）。</summary>
    internal const int SplitSpiritGain = 1;

    /// <summary>【分裂精神】扣血档位：**不扣血**（2026-10-09 用户追加："再加一个不扣血的档位"）。</summary>
    internal const string SplitHpRatioNone = "0";

    /// <summary>【分裂精神】扣血档位：当前血量的 10%。</summary>
    internal const string SplitHpRatio10 = "10";

    /// <summary>【分裂精神】扣血档位：当前血量的 15%（**默认**，见 D13）。</summary>
    internal const string SplitHpRatio15 = "15";

    /// <summary>【分裂精神】扣血档位：当前血量的 20%。</summary>
    internal const string SplitHpRatio20 = "20";

    /// <summary>
    /// 「每进入新的一幕自动获得计数」的最低 / 最高获得量（2026-10-09 用户追加："可设置获得多少计数。1~3"）。
    /// </summary>
    internal const int ActGainMin = 1;

    /// <summary>「每进入新的一幕自动获得计数」的最高档（1~3）。</summary>
    internal const int ActGainMax = 3;

    /// <summary>默认获得量（取中档 2：1 跟"没开"差不多，3 又偏大 ⇒ 用户可自行循环）。</summary>
    internal const string DefaultActGainAmount = "2";

    /// <summary>【分裂精神】扣血档位默认值（D13：默认 15%）。</summary>
    internal const string DefaultSplitHpRatio = SplitHpRatio15;

    /// <summary>【分裂精神】扣血后血量下限（D13：clamp ≥1 —— 休息处把自己扣死很怪）。</summary>
    internal const decimal MinHpAfterSplit = 1m;

    /// <summary>普通档的计数成本（D7）。</summary>
    internal const int CommonCost = 1;

    /// <summary>罕见档的计数成本（D7）。</summary>
    internal const int RareCost = 2;

    /// <summary>稀有档的计数成本（D7）。</summary>
    internal const int EpicCost = 3;

    /// <summary>先古档的计数成本（D7）。</summary>
    internal const int AncientCost = 4;

    /// <summary>
    /// 某席位是否应获得遗物【地狱耳环】= 功能开关开 **且** 属于本地多控会话 **且** 不是瓦库托管席位。
    ///
    /// 为什么只发给真人席位（与 <c>WakuuDaddyPolicy.ShouldGrantRelic</c> 同口径）：瓦库席位身上已有托管遗物，
    /// 而 D8 已拍板"首版瓦库不能玩地狱战神" ⇒ 给它发耳环只会白占格子。
    /// </summary>
    internal static bool ShouldGrantEarring(bool featureEnabled, bool isLocalSessionSeat, bool isWakuuSeat)
    {
        return featureEnabled && isLocalSessionSeat && !isWakuuSeat;
    }

    /// <summary>强度 → 计数成本（R5 / D7 成本表）。</summary>
    internal static int ResolveSummonCost(HellwarWarGodTier tier)
    {
        return tier switch
        {
            HellwarWarGodTier.Common => CommonCost,
            HellwarWarGodTier.Rare => RareCost,
            HellwarWarGodTier.Epic => EpicCost,
            HellwarWarGodTier.Ancient => AncientCost,
            _ => CommonCost,
        };
    }

    /// <summary>
    /// 宽松归一【分裂精神】扣血档位（10 / 15 / 20）；空值 / 非法一律兜回默认档
    /// （读入口要"兜底"，与设置页写入口的"拒绝"分开 —— 与 <c>WakuuRefinePolicy.NormalizeHpRatio</c> 同款）。
    /// </summary>
    internal static string NormalizeSplitHpRatio(string? raw)
    {
        return raw?.Trim() switch
        {
            SplitHpRatioNone => SplitHpRatioNone,
            SplitHpRatio10 => SplitHpRatio10,
            SplitHpRatio15 => SplitHpRatio15,
            SplitHpRatio20 => SplitHpRatio20,
            _ => DefaultSplitHpRatio,
        };
    }

    /// <summary>
    /// 严格归一【分裂精神】档位；空值 / 非法返回 <c>false</c>（供设置页写入口拒绝非法值）。
    /// </summary>
    internal static bool TryNormalizeSplitHpRatio(string? raw, out string normalized)
    {
        string? value = raw?.Trim();
        if (value is SplitHpRatioNone or SplitHpRatio10 or SplitHpRatio15 or SplitHpRatio20)
        {
            normalized = value;
            return true;
        }

        normalized = DefaultSplitHpRatio;
        return false;
    }

    /// <summary>档位 → 百分比数值（0 / 10 / 15 / 20；0 = 不扣血）。</summary>
    internal static decimal ResolveSplitHpPercent(string? ratioKey)
    {
        return NormalizeSplitHpRatio(ratioKey) switch
        {
            SplitHpRatioNone => 0m,
            SplitHpRatio10 => 10m,
            SplitHpRatio20 => 20m,
            _ => 15m,
        };
    }

    /// <summary>档位是不是"不扣血"（0%）。</summary>
    internal static bool IsNoCostSplitRatio(string? ratioKey)
    {
        return NormalizeSplitHpRatio(ratioKey) == SplitHpRatioNone;
    }

    /// <summary>
    /// 宽松归一「每幕自动获得计数」的档位（1 / 2 / 3）；空值 / 非法一律兜回默认档
    /// （读入口"兜底"，与设置页写入口的"拒绝"分开）。
    /// </summary>
    internal static string NormalizeActGainAmount(string? raw)
    {
        return TryNormalizeActGainAmount(raw, out string normalized) ? normalized : DefaultActGainAmount;
    }

    /// <summary>严格归一「每幕自动获得计数」档位（1 / 2 / 3）；空值 / 非法返回 <c>false</c>。</summary>
    internal static bool TryNormalizeActGainAmount(string? raw, out string normalized)
    {
        string? value = raw?.Trim();
        if (value is "1" or "2" or "3")
        {
            normalized = value;
            return true;
        }

        normalized = DefaultActGainAmount;
        return false;
    }

    /// <summary>「每幕自动获得计数」档位 → 数值（1~3，非法兜默认档）。</summary>
    internal static int ResolveActGainAmount(string? raw)
    {
        return int.TryParse(NormalizeActGainAmount(raw), out int amount)
            ? Math.Clamp(amount, ActGainMin, ActGainMax)
            : int.Parse(DefaultActGainAmount);
    }

    /// <summary>
    /// 进入第 <paramref name="actIndex"/> 幕时是否该发计数：
    /// 开关开 **且** 幕号有效（≥0） **且** **这一幕还没发过**（<paramref name="lastRewardedActIndex"/> 是本局已发过的最大幕号）。
    ///
    /// 为什么要记"已发过"：进局（`RunLaunch`）与"进入第 1 幕"（`EnterAct(0)`）在时序上会**两次**经过这条路
    /// （谁先谁后取决于异步完成顺序），而且读档后也会重进当前幕 ⇒ 没有这条幂等判据就会**重复发**。
    /// 用**存档字段**而不是进程内标记：读档续局时同一幕也不该补发。
    /// </summary>
    internal static bool ShouldGrantActCharges(bool featureEnabled, int actIndex, int lastRewardedActIndex)
    {
        return featureEnabled && actIndex >= 0 && actIndex > lastRewardedActIndex;
    }

    /// <summary>
    /// 【分裂精神】要扣掉的血量（D13：基数 = **当前血量**、向下取整、结果血量 clamp ≥1）。
    /// 当前血量 ≤1 时返回 0（扣不动）。
    /// </summary>
    internal static decimal ResolveSplitHpLoss(decimal currentHp, string? ratioKey)
    {
        if (currentHp <= MinHpAfterSplit)
        {
            return 0m;
        }

        decimal raw = Math.Floor(currentHp * ResolveSplitHpPercent(ratioKey) / 100m);
        if (raw <= 0m)
        {
            return 0m;
        }

        decimal maxLoss = currentHp - MinHpAfterSplit;
        return raw > maxLoss ? maxLoss : raw;
    }

    /// <summary>【分裂精神】使用后的血量（= 当前血量 - <see cref="ResolveSplitHpLoss"/>，恒 ≥1）。</summary>
    internal static decimal ResolveHpAfterSplit(decimal currentHp, string? ratioKey)
    {
        return currentHp - ResolveSplitHpLoss(currentHp, ratioKey);
    }

    /// <summary>是否可以消耗 <paramref name="cost"/> 个计数召唤一个战神（R6：计数够 + 未满员）。</summary>
    internal static bool CanSummon(int spiritCount, int cost, int combatPlayerCount, int maxPlayerCount)
    {
        return cost > 0
            && spiritCount >= cost
            && combatPlayerCount < maxPlayerCount;
    }

    /// <summary>不能召唤时的一句话原因（打日志用 ⇒ 实机能一眼看出卡在哪一步）。</summary>
    internal static string DescribeBlockReason(int spiritCount, int cost, int combatPlayerCount, int maxPlayerCount)
    {
        if (cost <= 0) { return "bad-cost"; }
        if (spiritCount < cost) { return $"not-enough-spirit({spiritCount}<{cost})"; }
        if (combatPlayerCount >= maxPlayerCount) { return $"party-full({combatPlayerCount}>={maxPlayerCount})"; }
        return "ok";
    }
}
