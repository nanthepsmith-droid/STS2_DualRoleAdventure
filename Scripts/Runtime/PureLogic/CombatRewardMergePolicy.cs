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
    /// 🔴 **临时席位（战灵召唤召唤出来的战神）永远不参与战斗奖励** —— 战灵召唤 R12
    /// （设计文档 §4 R12 / §5.8「奖励排除的三个入口」）。
    ///
    /// 判据 = 临时席位 id 段（<c>HellwarPocPolicy.IsTemporarySeat</c>，`0x1000..0x1FFF`）。
    /// 这条判据喂给下面**每一个**奖励席位判定 + 奖励镜像。
    ///
    /// ⚠ **为什么必须显式排除，而不是"靠它不在 `runState.Players` 里"**：
    /// <list type="number">
    /// <item>`IsLocalSeat(4096) == false`（它不在本地席位表里）⇒ 一旦它出现在奖励候选集里，
    ///   会被当成**第三方席位**走进"交回原版流程"那条分支 ⇒ 真人奖励界面上出现「[未知角色]」的奖励
    ///   （`LocalRewardMirror` 家族 2026-09-25 的实机现象，见 <c>ShouldOfferBackToVanilla</c>）。</item>
    /// <item>"不在 `runState.Players`" 是**上游清理**（`CleanupBeforeRewards` 把战神摘出战斗）的副作用，
    ///   不是这条流程自己的保证；清理一旦失败 / 顺序变化 / 将来有人把战神登记进 run 玩家表，
    ///   这里就会**静默**漏掉 —— 所以做成显式闸门 + 可单测。</item>
    /// </list>
    /// 今天的实际行为（**别误以为这里在改行为**）：三条路本来就被上游清理 + "不在 `runState.Players`" 关死，
    /// 本判据是**兜底与自证**；真正会响的是配套的异常锚点（`战后奖励里仍有临时席位`）。
    /// </summary>
    public static bool ShouldExcludeFromRewards(bool isTemporarySeat) => isTemporarySeat;

    /// <summary>
    /// 该席位是否进我们的**合并展示集**：活着 + 是本地会话席位 + **不是临时席位（战神）**。
    /// 第三方席位（Co-op Bots 的 Bot）不进 —— 交回原版流程由它自己接管
    /// （2026-09-25 实机：并进来会出现「[未知角色]」的奖励且 CB 侧零日志）。
    /// </summary>
    public static bool ShouldIncludeInMergedSet(bool isDead, bool isLocalSessionSeat, bool isTemporarySeat)
    {
        return !ShouldExcludeFromRewards(isTemporarySeat) && !isDead && isLocalSessionSeat;
    }

    /// <summary>该席位是否把奖励**交回原版流程**：活着 + 非本地会话席位 + **不是临时席位（战神）**。</summary>
    public static bool ShouldOfferBackToVanilla(bool isDead, bool isLocalSessionSeat, bool isTemporarySeat)
    {
        return !ShouldExcludeFromRewards(isTemporarySeat) && !isDead && !isLocalSessionSeat;
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
