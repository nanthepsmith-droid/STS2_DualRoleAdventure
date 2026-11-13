using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>一件遗物的归属：给哪个席位、是第几号遗物、来自玩家点选还是自动补齐。</summary>
internal readonly record struct BlitzTreasureAssignment(int PlayerSlot, int RelicIndex, bool FromPick);

/// <summary>
/// 极速模式（Blitz）宝箱「每人 2 件」的**纯逻辑**派发判据（r352）。
///
/// <para><b>为什么需要</b>：单人极速宝箱是 <c>_roundsLeft = 2</c> + 「1 件固定 + 1 件额外」，
/// 一个人选两轮拿到 2 件；而多人分支把 <c>_roundsLeft</c> 写死 1、额外塞 2 件 ⇒
/// 每人只能投一次票，第二件永远没人选（实机：2 人局 4 件遗物只发出去 2 件）。
/// 本地多控走的是多人分支，所以「每人 2 件」必须由我们自己派发。</para>
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
    /// <summary>每个席位在宝箱里应得的遗物件数（与单人极速一致）。</summary>
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
