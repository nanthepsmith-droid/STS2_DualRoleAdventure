#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Models.Relics;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「炼化」（瓦库四功能之三）的「注入 + 目标枚举 + 收益枚举」编排层（一切游戏对象访问集中在这里，
/// 数值换算在纯逻辑 <see cref="WakuuRefinePolicy"/>）。
///
/// 功能：休息处给**真人席位**注入一条自定义选项 —— 选中后弹「选哪个瓦库」选择器，
/// 再依次弹原版卡组选牌界面（张数上限按配置）与自建选遗物弹层，随后收编血量并炼掉目标。
///
/// 关键纪律（与净化同源，核验报告 §3.2）：
/// <list type="bullet">
/// <item>**只注入真人席位**：绝不能让本选项出现在瓦库自己的选项列表里 —— 否则会被
///   <see cref="LocalWakuuRestAutoChoice"/> 当"第三方额外选项"随机自动点掉；</item>
/// <item>**注入必须幂等**：<c>Generate</c> 可能被多次调用（重进房间 / 刷新），重复注入会出现两条炼化；</item>
/// <item>**候选判定复用瓦库身份入口** <see cref="LocalWakuuRelicRuntime.IsVakuuFormMode(Player)"/>，
///   不自己写"谁是瓦库"的判据。</item>
/// </list>
/// </summary>
internal static class RefineWakuuRestSiteRuntime
{
    /// <summary>由 <c>RestSiteOptionPatch</c>（<c>RestSiteOption.Generate</c> 后缀）调用。</summary>
    internal static void TryInjectRefineOption(Player player, List<RestSiteOption> options)
    {
        if (player == null || options == null)
        {
            return;
        }

        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (!LocalWakuuAutopilotConfig.RefineVakuu)
        {
            return;
        }

        // 只给本地真人席位；瓦库席位与第三方席位一律不注入。
        if (!LocalSelfCoopContext.IsLocalSessionSeat(player.NetId)
            || LocalWakuuRelicRuntime.IsVakuuFormMode(player))
        {
            return;
        }

        if (options.Any(option => option is RefineWakuuRestSiteOption))
        {
            return;
        }

        if (!HasAnyCandidate(player))
        {
            return;
        }

        // 图标必须在房间预加载**之前**注册好（注入发生在 BeginRestSite，早于 Preloading 'RestSite Room'）：
        // 否则缺失的 png 会被 AssetCache 记成 failed，之后 RestSiteOption.Icon 抛异常 ⇒ 按钮连名字都设不上。
        RefineWakuuRestSiteOption.EnsureIconRegistered();

        options.Add(new RefineWakuuRestSiteOption(player));
        LocalMultiControlLogger.Info(
            $"休息区已注入瓦库炼化选项: owner={player.NetId}, candidates={CollectCandidates(player).Count}, "
            + $"比例={LocalModText.RefineHpRatioLabel(LocalWakuuAutopilotConfig.RefineHpRatio)}, "
            + $"卡数档位={LocalModText.RefineCardLimitLabel(LocalWakuuAutopilotConfig.RefineCardLimit)}, "
            + $"收编牌与遗物={LocalWakuuAutopilotConfig.RefineTakeVakuuAssets}, "
            + $"收编药水={LocalWakuuAutopilotConfig.RefineTakeVakuuPotions}");
    }

    /// <summary>是否存在可炼化的瓦库（= 选项是否可用）。</summary>
    internal static bool HasAnyCandidate(Player realPlayer)
    {
        return CollectCandidates(realPlayer).Count > 0;
    }

    /// <summary>
    /// 枚举可炼化的瓦库：本地回环局里**处于瓦库形态、存活、血上限 &gt; 0** 的其他玩家
    /// （血上限已清零的瓦库不再作为目标，避免重复炼化）。
    /// 任何异常一律吞掉并返回空表（宁可不显示选项，也不要让休息区流程炸掉）。
    /// </summary>
    internal static List<Player> CollectCandidates(Player realPlayer)
    {
        List<Player> candidates = new();
        try
        {
            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            if (runState?.Players == null || realPlayer == null)
            {
                return candidates;
            }

            foreach (Player candidate in runState.Players)
            {
                if (candidate == null || candidate.NetId == realPlayer.NetId)
                {
                    continue;
                }

                if (!LocalWakuuRelicRuntime.IsVakuuFormMode(candidate))
                {
                    continue;
                }

                if (candidate.Creature == null
                    || !WakuuRefinePolicy.IsEligibleTarget(candidate.Creature.IsAlive, candidate.Creature.MaxHp))
                {
                    continue;
                }

                candidates.Add(candidate);
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"枚举可炼化瓦库失败: {exception.Message}");
        }

        return candidates;
    }

    /// <summary>
    /// 目标瓦库身上**可被收编**的遗物（收编后瓦库身上就**没有了** —— 走原版 <c>RelicCmd.Remove</c>
    /// 真移除、不是复制；用户口径「防止无限强力遗物」靠这条 + <c>TransferRelicAsync</c> 的移除校验保证）。
    /// 只排除【永久低语耳环】/【瓦库形态】（瓦库身份定义，且移除会被
    /// <c>PlayerRemoveRelicInternalGuardPatch</c> 拦下）。
    /// **重复件不排除**：玩家身上已有同一件也照常可拿（2026-10-06 用户明确「重复件没问题，这也是一种正常玩法」）。
    /// </summary>
    internal static List<RelicModel> CollectTransferableRelics(Player target)
    {
        List<RelicModel> relics = new();
        try
        {
            if (target?.Relics == null)
            {
                return relics;
            }

            foreach (RelicModel relic in target.Relics)
            {
                if (relic == null)
                {
                    continue;
                }

                if (WakuuRefinePolicy.IsExcludedRelic(
                        isWhisperingEarring: relic is LocalWakuuStarterRelic,
                        isVakuuFormRelic: relic is LocalWakuuFormRelic))
                {
                    continue;
                }

                relics.Add(relic);
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"枚举可收编遗物失败: {exception.Message}");
        }

        return relics;
    }

    /// <summary>目标瓦库主卡组张数（炼化转移的是主卡组，不是战斗牌堆）。</summary>
    internal static int CountDeckCards(Player target)
    {
        return PileType.Deck.GetPile(target).Cards.Count;
    }

    /// <summary>本次可自选的最大牌数（配置档位 × 目标卡组张数）。</summary>
    internal static int ResolveCardLimit(Player target)
    {
        return WakuuRefinePolicy.ResolveCardLimit(
            LocalWakuuAutopilotConfig.RefineCardLimit,
            CountDeckCards(target));
    }

    /// <summary>选择器里的候选标签：角色席位 + 血量 + 卡组张数（只读展示）。</summary>
    internal static string DescribeCandidate(Player candidate)
    {
        string slotLabel = LocalSelfCoopContext.GetSlotLabel(candidate.NetId);
        decimal currentHp = candidate.Creature?.CurrentHp ?? 0m;
        decimal maxHp = candidate.Creature?.MaxHp ?? 0m;
        return $"{LocalModText.RoleSlot(slotLabel)} · HP {currentHp:0}/{maxHp:0} · 卡组 {CountDeckCards(candidate)}";
    }
}
