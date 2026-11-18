using System.Runtime.CompilerServices;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Rewards;

namespace LocalMultiControl.Scripts.Rewards;

/// <summary>
/// 记录每个 Reward 实例对应的席位标签（`botN` / `playerN`，2026-11-18 §排期 6）。
/// 在战利品汇总时写入，在 NRewardButton 展示时读取。
/// </summary>
internal static class RewardPlayerLabelRegistry
{
    private static readonly ConditionalWeakTable<Reward, string> Labels = new();

    internal static void Register(Reward reward, ulong playerNetId)
    {
        Labels.AddOrUpdate(reward, LocalSeatLabels.Describe(playerNetId));
    }

    internal static bool TryGetLabel(Reward reward, out string? label)
    {
        return Labels.TryGetValue(reward, out label);
    }
}
