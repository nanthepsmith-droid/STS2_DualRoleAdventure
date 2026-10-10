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
    /// **已实装（可以在召唤选择里出现）的档位**（成本从低到高）。
    /// Phase 2 = 只有「普通」；**Phase 2b 起四档齐全**（罕见 / 稀有 / 先古的预设档见
    /// <see cref="HellwarWarGodPresets"/>；先古档同时是【终极九连环】的前置，D19）。
    /// </summary>
    internal static readonly HellwarWarGodTier[] SelectableTiers =
    {
        HellwarWarGodTier.Common,
        HellwarWarGodTier.Rare,
        HellwarWarGodTier.Epic,
        HellwarWarGodTier.Ancient,
    };

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
    /// **R10（Phase 4）"战神替绑主吃一半"的拦伤判据** —— 三个条件**同时**满足才拦：
    /// <list type="number">
    /// <item>这是**攻击伤害**（`props.IsPoweredAttack()`）：中毒 / 灼烧 / 直接掉血等非攻击来源**不拦**
    ///   （与 `DieForYouPower` / `GuardedPower` / 本仓【我挡】同口径）；</item>
    /// <item>被打的目标就是**绑主本人**（战神挨打、别人挨打都不算）；</item>
    /// <item>绑主名下**有活着的战神**可以承接 —— 没有就**一点都不该改**，
    ///   否则会出现"伤害被减半、却没人承接"= 绑主白赚 50% 减伤（这是最容易写错的一处）。</item>
    /// </list>
    /// 抽成纯函数：两个钩子（`ModifyHpLostBeforeOsty` 减半 / `ModifyUnblockedDamageTarget` 改目标）
    /// **必须用同一个判据**，否则会出现"减半了但没转移"或"转移了但没减半"的半吊子状态。
    /// </summary>
    internal static bool ShouldGuardIncomingDamage(bool isPoweredAttack, bool targetIsSummoner, bool hasAliveGuard)
        => isPoweredAttack && targetIsSummoner && hasAliveGuard;

    /// <summary>
    /// **R10 修订（2026-10-10 用户口径）：「战神刚出场那一回合还不能当肉盾」** —— 一个战神只有在
    /// **出场回合之后**才有资格替绑主承伤（<paramref name="currentRound"/> &gt; <paramref name="spawnRound"/>）。
    ///
    /// **为什么**：战神出场时自带 1 层【无实体】（召唤保命，<c>ApplySummonProtectionAsync</c>），
    /// 那一层覆盖"召唤当回合 + 紧接着的敌方回合"；若同一窗口里它还能替绑主吃伤害，
    /// 「打不死 + 白挡一半伤害」叠加 ⇒ 作为肉盾太强（用户 2026-10-10 实机体感：
    /// "战神刚出来有 1 回合无实体保护，同时可以替代本体玩家承受伤害，作为肉盾疑似有点太强了"）。
    ///
    /// **为什么用 round 比较而不是另加状态机**：`CombatState.RoundNumber` 本来就是**按
    /// "玩家回合 + 敌方回合"一整个 round 递增**的（`CombatManager.cs:1878`：敌方回合结束、切回玩家侧时 +1）
    /// ⇒ `currentRound > spawnRound` **天然等于**"下一个（玩家）回合起生效"，而且**每个战神各算各的**
    /// （死了再召一个新的 ⇒ 新战神同样要等一个回合，不需要额外复位逻辑）。
    ///
    /// ⚠ 判据落在**查"当前守卫"那一步**（`HellwarWarGodFactory.FindGuardCreature` 带 round 过滤）
    /// ⇒ `ShouldGuardIncomingDamage` 的第三个条件 `hasAliveGuard` 自动升级为
    /// "有**已满一个回合**的活战神"，两个钩子仍然共用同一判据（不会出现"减半了但没人承接"）。
    /// </summary>
    internal static bool IsGuardEligible(int spawnRound, int currentRound) => currentRound > spawnRound;

    /// <summary>
    /// **R10：绑主本次要掉的 HP 减半**（D4 拍板口径 = **玩家零伤害 + 战神吃一半**）。
    ///
    /// 减半点在 `HpLossHookPhase.BeforeOsty` —— 即**绑主自己的格挡已经结算完**之后（所以"绑主的格挡照常生效"）；
    /// 减半后的量紧接着被 `ModifyUnblockedDamageTarget` 转给战神 ⇒ 绑主最终**掉 0**、战神掉**一半**
    /// （另一半消失 = 旧【我挡】"你只承受一半"的语义）。
    /// </summary>
    internal static decimal ResolveGuardedHpLoss(decimal amount) => amount / 2m;

    /// <summary>
    /// **R9 复活一个战神的花费**（Phase 4）= **该战神原档位的召唤成本**（本场预算，非永久）。
    ///
    /// 口径依据：复活**保留**它原有的卡组 / 预设遗物 / 召唤保命层 ⇒ 相对"再召一个同档新的"
    /// （成本相同、也是满血 + 预设内容）不该更贵；但也不该更便宜（否则玩家会故意让战神送死来"刷新"）。
    /// 抽成纯函数便于单测与日后调参（要改只改这里）。
    /// </summary>
    internal static int ResolveReviveCost(HellwarWarGodTier tier) => ResolveSummonCost(tier);

    /// <summary>
    /// 某档**预设遗物的件数**（② 预设遗物的强度阶梯口径）：= 召唤成本 − 1
    /// ⇒ 普通 **0** / 罕见 **1** / 稀有 **2** / 先古 **3**（与"1/2/3/4"成本阶梯一一对应）。
    ///
    /// 单一事实源：**内容表**在游戏侧 <c>HellwarWarGodRelics</c>（`ModelDb.Relic&lt;T&gt;()` 类型引用），
    /// 启动自检会拿内容表的实际件数与本函数**对账**（不符打 WARN）—— 所以改内容表时不会悄悄破坏阶梯。
    /// </summary>
    internal static int ResolvePresetRelicCount(HellwarWarGodTier tier) => ResolveSummonCost(tier) - 1;

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

    // ───────────────────────── Phase 2：战斗内"计数预算"（R3 口径 A） ─────────────────────────
    //
    // R3 的原文是「消耗一个计数（**仅战斗内消耗，战斗结束后自动补满**）」⇒ 计数是**每场战斗的预算**：
    // 休息处 / 每幕累计出来的是**上限**（存档字段 `SpiritCount`），战斗中花掉的是**本场已花**（不存档），
    // 战后把"已花"清零 = 自动补满。所以：**可用 = 上限 − 本场已花**。

    /// <summary>本场战斗的可用计数（= 累计上限 − 本场已花；两个入参都先 clamp ≥0）。</summary>
    internal static int ResolveAvailableSpirit(int spiritCount, int spentThisCombat)
    {
        int cap = Math.Max(0, spiritCount);
        int spent = Math.Max(0, spentThisCombat);
        return Math.Max(0, cap - spent);
    }

    /// <summary>本场战斗还够不够花 <paramref name="cost"/> 个计数。</summary>
    internal static bool CanSpendSpirit(int spiritCount, int spentThisCombat, int cost)
    {
        return cost > 0 && ResolveAvailableSpirit(spiritCount, spentThisCombat) >= cost;
    }

    /// <summary>已花的计数开销（下限 0），用于累加。</summary>
    internal static int AddSpentSpirit(int spentThisCombat, int cost)
    {
        return Math.Max(0, spentThisCombat) + Math.Max(0, cost);
    }

    /// <summary>
    /// **回合开始是否该弹「召唤选择」**（Phase 2 / R3 / R7；全部条件都满足才弹）。
    ///
    /// <list type="bullet">
    /// <item><paramref name="featureEnabled"/>：功能总开关（`hellwarGod`，默认关）；</item>
    /// <item><paramref name="humanSeat"/>：是**真人本地席位**（瓦库托管席位 / 第三方席位不弹 —— R14 首版不做）；</item>
    /// <item><paramref name="hasEarring"/>：这一席身上有【地狱耳环】（没有就没有"计数"这个概念）；</item>
    /// <item><paramref name="alreadyOfferedThisTurn"/>：本回合已经弹过一次（D6「每回合最多 1 个」的落点）；</item>
    /// <item><paramref name="availableSpirit"/> / <paramref name="minCost"/>：还买得起最低档（买不起**安静跳过**，不弹空弹层）；</item>
    /// <item>未满员（R6：战斗内玩家数 &lt; 12）。</item>
    /// </list>
    /// </summary>
    internal static bool ShouldOfferSummon(
        bool featureEnabled,
        bool humanSeat,
        bool hasEarring,
        bool alreadyOfferedThisTurn,
        int availableSpirit,
        int minCost,
        int combatPlayerCount,
        int maxPlayerCount)
    {
        return featureEnabled
            && humanSeat
            && hasEarring
            && !alreadyOfferedThisTurn
            && minCost > 0
            && availableSpirit >= minCost
            && combatPlayerCount < maxPlayerCount;
    }

    // ───────────────── Phase 2b：两个"大招"召唤（R16【无尽战神】/ R17【终极九连环】） ─────────────────
    //
    // 拍板口径（doc §7.3 + §7.3.1）：
    //  ·【无尽战神】整包 **8**，从**本场战斗预算**扣（`CombatSpentSpirit`，战后补满）⇒ 每场都能开（只要那场够）；
    //    **随机**取「普通 / 罕见 / 稀有」三档之一（先古不进随机池，D17）补满到 12 席，超员截断（D18）。
    //  ·【终极九连环】整包 **12** 但**扣的是计数上限本身**（`SpiritCount`，**永久消耗、不补满**）
    //    ⇒ 一轮总产出 9~15 ⇒ 实际整个 run 只开得起一次（2026-10-09 用户二轮修订）；
    //    **2026-10-09 三轮修订（用户口径）**：「**固定召唤 9 个，除非到栏位上限**；想要用这招，
    //    计数至少能召唤 9 个（⇒ 门槛 = 整包价 12），然后**按实际召唤的数量**计算消耗的量」
    //    ⇒ 取消原"每 3 点 1 个"的部分成功，改成：固定 9 个（仅按栏位截断）+ 消耗 = `round(12 × 实际 / 9)`。
    //  · 两者**每场战斗各限 1 次**（D15），且与"每回合 1 个"的正常召唤并存（D6 只约束正常召唤）。

    /// <summary>【无尽战神】的整包价（计数，从本场预算扣）。</summary>
    internal const int EndlessWarGodBundleCost = 8;

    /// <summary>
    /// 【终极九连环】的整包价（计数，**永久扣上限**）。**它同时是开启门槛**
    /// （2026-10-09 三轮修订：用户口径「计数至少能召唤 9 个」⇒ 先攒到整包价，这一项才会出现）。
    /// </summary>
    internal const int UltimateNineChainBundleCost = 12;

    /// <summary>【九连环】固定召唤的先古个数（**只有剩余栏位不足才截断**，2026-10-09 三轮修订）。</summary>
    internal const int NineChainMaxAncients = 9;

    /// <summary>【九连环】的开启门槛 = 整包价（"至少够召唤 9 个"）。</summary>
    internal const int NineChainMinSpirit = UltimateNineChainBundleCost;

    /// <summary>剩余可召唤栏位（`MaxLocalPlayerCount` − 当前战斗内玩家数；下限 0）。</summary>
    internal static int ResolveFreeSlots(int combatPlayerCount, int maxPlayerCount)
        => Math.Max(0, maxPlayerCount - Math.Max(0, combatPlayerCount));

    /// <summary>D18：一次召唤多个时**截断到剩余栏位**（返回实际会召唤的个数）。</summary>
    internal static int ResolveTruncatedSummonCount(int desiredCount, int freeSlots)
        => Math.Max(0, Math.Min(Math.Max(0, desiredCount), Math.Max(0, freeSlots)));

    /// <summary>【无尽战神】实际会召唤几个 = 把空位补满（D18 截断天然成立）。</summary>
    internal static int ResolveEndlessSummonCount(int combatPlayerCount, int maxPlayerCount)
    {
        int freeSlots = ResolveFreeSlots(combatPlayerCount, maxPlayerCount);
        return ResolveTruncatedSummonCount(freeSlots, freeSlots);
    }

    /// <summary>
    /// 【九连环】实际会召唤几个先古（**固定 9 个**，只有剩余栏位不足才截断；2026-10-09 三轮修订）：
    /// 用户口径「固定召唤 9 个，除非到栏位上限」。
    /// ⚠ 门槛（`上限 ≥ 12`）由 <see cref="CanOpenNineChain"/> 判，不在这里表达 ⇒ 本函数只回答"栏位够几个"。
    /// </summary>
    internal static int ResolveNineChainSummonCount(int freeSlots)
        => ResolveTruncatedSummonCount(NineChainMaxAncients, freeSlots);

    /// <summary>
    /// 【九连环】**永久扣掉多少计数上限**（按**实际召唤数**折算整包价；2026-10-09 三轮修订）：
    /// `round(12 × 实际数量 / 9)`（四舍五入；9 个 = 12 点，被栏位截断到 k 个就只扣 `round(12k/9)`）。
    /// 返回 0 表示"没召唤 ⇒ 不扣"。
    /// </summary>
    internal static int ResolveNineChainSpiritCost(int summonedCount)
    {
        int count = Math.Clamp(summonedCount, 0, NineChainMaxAncients);
        if (count <= 0)
        {
            return 0;
        }

        decimal raw = UltimateNineChainBundleCost * (decimal)count / NineChainMaxAncients;
        return Math.Clamp(
            (int)Math.Round(raw, MidpointRounding.AwayFromZero),
            1,
            UltimateNineChainBundleCost);
    }

    /// <summary>能否开【无尽战神】：本场可用计数够整包价 + 还有空位 + 本场没用过（D15）。</summary>
    internal static bool CanOpenEndlessWarGod(int availableSpirit, int freeSlots, bool usedThisCombat)
        => !usedThisCombat
        && availableSpirit >= EndlessWarGodBundleCost
        && freeSlots > 0;

    /// <summary>
    /// 能否开【终极九连环】：**计数上限**够整包价（= "至少够召唤 9 个"）+ 还有空位 + 本场没用过（D15）。
    /// </summary>
    internal static bool CanOpenNineChain(int capSpirit, int freeSlots, bool usedThisCombat)
        => !usedThisCombat
        && capSpirit >= NineChainMinSpirit
        && freeSlots > 0;

    /// <summary>大招不能开时的一句话原因（打日志用）。</summary>
    internal static string DescribeUltimateBlockReason(
        bool isNineChain, int spirit, int freeSlots, bool usedThisCombat)
    {
        if (usedThisCombat) { return "already-used-this-combat"; }
        if (freeSlots <= 0) { return "party-full"; }
        int required = isNineChain ? NineChainMinSpirit : EndlessWarGodBundleCost;
        if (spirit < required) { return $"not-enough-spirit({spirit}<{required})"; }
        return "ok";
    }

    /// <summary>不弹「召唤选择」时的一句话原因（打日志用 ⇒ 实机能一眼看出卡在哪一步）。</summary>
    internal static string DescribeOfferSkipReason(
        bool featureEnabled,
        bool humanSeat,
        bool hasEarring,
        bool alreadyOfferedThisTurn,
        int availableSpirit,
        int minCost,
        int combatPlayerCount,
        int maxPlayerCount)
    {
        if (!featureEnabled) { return "feature-disabled"; }
        if (!humanSeat) { return "not-human-seat"; }
        if (!hasEarring) { return "no-earring"; }
        if (alreadyOfferedThisTurn) { return "already-offered-this-turn"; }
        if (minCost <= 0) { return "bad-cost"; }
        if (availableSpirit < minCost) { return $"not-enough-spirit({availableSpirit}<{minCost})"; }
        if (combatPlayerCount >= maxPlayerCount) { return $"party-full({combatPlayerCount}>={maxPlayerCount})"; }
        return "offer";
    }
}
