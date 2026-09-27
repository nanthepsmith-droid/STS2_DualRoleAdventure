using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 战后奖励「汇总展示」的**纯判定**（R1b：从 <c>CombatRoomOfferRoomEndRewardsPatch</c> 抽出）。
///
/// 边界说明（别误解这一层）：这个补丁里真正肥的是**编排**（生成 → 结算钩子 → 自动领取 → 展示 → 等 Completed），
/// 那部分归 R4 抽服务做；本条只把「哪些席位进合并集 / 哪些交回原版 / 用谁当展示者 / 跨角色卡池取谁」
/// 这些**判定**拿成可单测的纯函数（原实现混在 140 行 async 流程里，只能靠实机回归）。
/// 背景：`maintenance-docs/decision-records/runtime架构分层重构评估.md`（R1）。
/// </summary>
internal static class CombatRewardMergePolicy
{
    /// <summary>
    /// 这一场战斗是否该发奖励：`Encounter == null`（如某些房间型结算）视为发；
    /// 否则跟游戏的 <c>Encounter.ShouldGiveRewards</c> 一致。
    /// </summary>
    public static bool ShouldGiveRewards(bool? encounterShouldGiveRewards)
    {
        return encounterShouldGiveRewards != false;
    }

    /// <summary>
    /// 该席位是否进我们的**合并展示集**：活着 + 是本地会话席位。
    /// 第三方席位（Co-op Bots 的 Bot）不进 —— 交回原版流程由它自己接管
    /// （2026-09-25 实机：并进来会出现「[未知角色]」的奖励且 CB 侧零日志）。
    /// </summary>
    public static bool ShouldIncludeInMergedSet(bool isDead, bool isLocalSessionSeat)
    {
        return !isDead && isLocalSessionSeat;
    }

    /// <summary>该席位是否把奖励**交回原版流程**：活着 + 非本地会话席位。</summary>
    public static bool ShouldOfferBackToVanilla(bool isDead, bool isLocalSessionSeat)
    {
        return !isDead && !isLocalSessionSeat;
    }

    /// <summary>
    /// 用哪个玩家作为奖励界面的展示者：**第一个存活者**，全死则第一个。
    /// 返回下标；<paramref name="isDead"/> 为空时返回 -1（调用方须保证非空列表 —— 上游已有 `Count == 0` 早退）。
    /// </summary>
    public static int SelectDisplayPlayerIndex(IReadOnlyList<bool> isDead)
    {
        if (isDead.Count == 0)
        {
            return -1;
        }

        for (int i = 0; i < isDead.Count; i++)
        {
            if (!isDead[i])
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// 「跨角色卡组」（`extraCrossCharacterCardReward`，默认关）要取哪些席位的卡池：
    /// 不是自己 + 活着 + **是本地席位** + 有角色（卡池）—— 顺序保持入参顺序，去重留给调用方
    /// （卡池 `.Distinct()` 的语义与原实现一致）。
    /// </summary>
    public static List<int> SelectCrossCharacterPoolCandidateIndices(
        IReadOnlyList<(bool IsSelf, bool IsDead, bool IsLocalSeat, bool HasPool)> candidates)
    {
        List<int> indices = new();
        for (int i = 0; i < candidates.Count; i++)
        {
            (bool isSelf, bool isDead, bool isLocalSeat, bool hasPool) = candidates[i];
            if (isSelf || isDead || !isLocalSeat || !hasPool)
            {
                continue;
            }

            indices.Add(i);
        }

        return indices;
    }
}
