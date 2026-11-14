using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>一件遗物的归属：给哪个席位、是第几号遗物、来自玩家点选还是自动补齐。</summary>
internal readonly record struct BlitzTreasureAssignment(int PlayerSlot, int RelicIndex, bool FromPick);

/// <summary>
/// 极速模式（Blitz）宝箱「每人 2 件」的**纯逻辑**派发判据（r352；2026-11-14 起只在开关打开时使用）。
///
/// <para><b>为什么不默认用</b>：单人极速宝箱是 <c>_roundsLeft = 2</c> + 「1 件固定 + 1 件额外」，
/// 一个人选两轮拿到 2 件；而**多人分支的「<c>_roundsLeft = 1</c>、遗物 = 人数 + 2、每人只选 1 件」
/// 本来就是原版设计**（2026-11-14 用户实机联机确认），不是 bug。本地多控走多人分支，
/// 所以默认档就应该原样照原版的"每人 1 件"。</para>
///
/// <para>只有把开关 <c>blitzTreasureDoublePick</c> 打开时，才由我们自己按下面的口径派发每人 2 件。</para>
///
/// <para><b>口径</b>（纯函数，可单测）：
/// ① 每个席位最多 <see cref="PicksPerPlayer"/> 件；
/// ② 先认「玩家自己点的那件」（第一阶段 + 第二阶段各一次）；抢同一件的按**席位顺序**先到先得
///   （本地多控是同一台机器，不需要原版的石头剪刀布）；</para>
/// ③ 点完后仍有空位的席位，按席位顺序用**没被任何人选中**的遗物补齐（原版的"安慰奖"语义）；
/// ④ 遗物不够分时，靠后的席位拿不到 —— 不凭空造遗物。
/// </summary>
internal static class BlitzTreasurePickPolicy
{
    /// <summary>
    /// 原版**多人**宝箱每人可选的件数（`_roundsLeft = 1`）。开关关（默认）时就是这个口径，
    /// 也是"用户实机联机确认过的原版设计"。日志里用它把口径写清楚。
    /// </summary>
    internal const int VanillaPicksPerPlayer = 1;

    /// <summary>开关打开时每个席位在宝箱里应得的遗物件数（与单人极速一致）。</summary>
    internal const int PicksPerPlayer = 2;

    internal static List<BlitzTreasureAssignment> BuildAssignments(
        int playerCount,
        int relicCount,
        IReadOnlyList<int?> firstPhasePicks,
        IReadOnlyList<int?> secondPhasePicks)
    {
        List<BlitzTreasureAssignment> assignments = new();
        if (playerCount <= 0 || relicCount <= 0)
        {
            return assignments;
        }

        bool[] used = new bool[relicCount];
        int[] owned = new int[playerCount];

        ClaimPicks(firstPhasePicks);
        ClaimPicks(secondPhasePicks);
        FillRemaining();

        assignments.Sort((left, right) => left.RelicIndex.CompareTo(right.RelicIndex));
        return assignments;

        void ClaimPicks(IReadOnlyList<int?> picks)
        {
            if (picks == null)
            {
                return;
            }

            int count = Math.Min(playerCount, picks.Count);
            for (int slot = 0; slot < count; slot++)
            {
                if (picks[slot] is not int relicIndex || owned[slot] >= PicksPerPlayer)
                {
                    continue;
                }

                if (relicIndex < 0 || relicIndex >= relicCount || used[relicIndex])
                {
                    continue;
                }

                used[relicIndex] = true;
                owned[slot]++;
                assignments.Add(new BlitzTreasureAssignment(slot, relicIndex, FromPick: true));
            }
        }

        void FillRemaining()
        {
            for (int target = 1; target <= PicksPerPlayer; target++)
            {
                for (int slot = 0; slot < playerCount; slot++)
                {
                    if (owned[slot] >= target)
                    {
                        continue;
                    }

                    int relicIndex = NextUnused();
                    if (relicIndex < 0)
                    {
                        return;
                    }

                    used[relicIndex] = true;
                    owned[slot]++;
                    assignments.Add(new BlitzTreasureAssignment(slot, relicIndex, FromPick: false));
                }
            }
        }

        int NextUnused()
        {
            for (int i = 0; i < used.Length; i++)
            {
                if (!used[i])
                {
                    return i;
                }
            }

            return -1;
        }
    }

    /// <summary>没被派出去的遗物（要把它们收进"跳过"结果，UI 才会淡出，不会留在屏上）。</summary>
    internal static List<int> ResolveUnusedRelicIndexes(int relicCount, IReadOnlyList<BlitzTreasureAssignment> assignments)
    {
        bool[] used = new bool[Math.Max(relicCount, 0)];
        foreach (BlitzTreasureAssignment assignment in assignments)
        {
            if (assignment.RelicIndex >= 0 && assignment.RelicIndex < used.Length)
            {
                used[assignment.RelicIndex] = true;
            }
        }

        List<int> unused = new();
        for (int i = 0; i < used.Length; i++)
        {
            if (!used[i])
            {
                unused.Add(i);
            }
        }

        return unused;
    }
}
