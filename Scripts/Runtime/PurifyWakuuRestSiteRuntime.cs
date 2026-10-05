using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 净化（瓦库四功能之二）的「注入 + 目标枚举」纯编排层。
///
/// 功能：休息处给**真人席位**注入一条自定义选项，选中后先弹「选哪个瓦库」选择器，
/// 再对该瓦库的卡组弹原版删牌界面（每次最多 <see cref="PurifyWakuuRestSiteOption.MaxCards"/> 张）。
///
/// 关键纪律（核验报告 §3.2）：
/// <list type="bullet">
/// <item>**只注入真人席位**：绝不能让本选项出现在瓦库自己的选项列表里 —— 否则会被
///   <see cref="LocalWakuuRestAutoChoice"/> 当"第三方额外选项"随机自动点掉；</item>
/// <item>**注入必须幂等**：<c>Generate</c> 可能被多次调用（重进房间 / 刷新），重复注入会出现两条净化；</item>
/// <item>**候选判定复用瓦库身份入口** <see cref="LocalWakuuRelicRuntime.IsVakuuFormMode(Player)"/>，
///   不自己写"谁是瓦库"的判据。</item>
/// </list>
/// </summary>
internal static class PurifyWakuuRestSiteRuntime
{
    /// <summary>由 <c>RestSiteOptionPatch</c>（<c>RestSiteOption.Generate</c> 后缀）调用。</summary>
    internal static void TryInjectPurifyOption(Player player, List<RestSiteOption> options)
    {
        if (player == null || options == null)
        {
            return;
        }

        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (!LocalWakuuAutopilotConfig.PurifyWakuu)
        {
            return;
        }

        // 只给本地真人席位；瓦库席位与第三方席位一律不注入。
        if (!LocalSelfCoopContext.IsLocalSessionSeat(player.NetId)
            || LocalWakuuRelicRuntime.IsVakuuFormMode(player))
        {
            return;
        }

        if (options.Any(option => option is PurifyWakuuRestSiteOption))
        {
            return;
        }

        if (!HasAnyCandidate(player))
        {
            return;
        }

        // 图标必须在房间预加载**之前**注册好（注入发生在 BeginRestSite，早于 Preloading 'RestSite Room'）：
        // 否则缺失的 png 会被 AssetCache 记成 failed，之后 RestSiteOption.Icon 抛异常 ⇒ 按钮连名字都设不上。
        PurifyWakuuRestSiteOption.EnsureIconRegistered();

        options.Add(new PurifyWakuuRestSiteOption(player));
        LocalMultiControlLogger.Info(
            $"休息区已注入瓦库净化选项: owner={player.NetId}, candidates={CollectCandidates(player).Count}");
    }

    /// <summary>是否存在可净化的瓦库（= 选项是否可用）。</summary>
    internal static bool HasAnyCandidate(Player realPlayer)
    {
        return CollectCandidates(realPlayer).Count > 0;
    }

    /// <summary>
    /// 枚举可净化的瓦库：本地回环局里**处于瓦库形态、存活、且卡组还有可删牌**的其他玩家。
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

                if (candidate.Creature == null || candidate.Creature.IsDead)
                {
                    continue;
                }

                if (CountRemovableCards(candidate) == 0)
                {
                    continue;
                }

                candidates.Add(candidate);
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"枚举可净化瓦库失败: {exception.Message}");
        }

        return candidates;
    }

    /// <summary>卡组里当前可被删除的牌数（与游戏删牌界面同一判据 <c>IsRemovable</c>）。</summary>
    internal static int CountRemovableCards(Player player)
    {
        return PileType.Deck.GetPile(player).Cards.Count(card => card.IsRemovable);
    }

    /// <summary>选择器里的候选标签：角色席位 + 当前血量（只读展示，不含玩家个人信息）。</summary>
    internal static string DescribeCandidate(Player candidate)
    {
        string slotLabel = LocalSelfCoopContext.GetSlotLabel(candidate.NetId);
        decimal currentHp = candidate.Creature?.CurrentHp ?? 0m;
        decimal maxHp = candidate.Creature?.MaxHp ?? 0m;
        return $"{LocalModText.RoleSlot(slotLabel)} · HP {currentHp:0}/{maxHp:0}";
    }
}
