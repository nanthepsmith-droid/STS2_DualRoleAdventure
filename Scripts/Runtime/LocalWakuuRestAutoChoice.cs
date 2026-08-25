using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库火堆自动选择（可开关 autoRestChoice）。决策规则（用户拍板）：
/// - 血量 &lt; 50%：优先睡觉（HEAL）；
/// - 血量 ≥ 50%：
///   · 有遗物提供的其他选项（举重/挖掘等，非 HEAL/SMITH/MEND 基础项）→ 在睡觉以外的选项里随机；
///   · 无遗物选项 → 锻造升级"除打击/防御外"的最后一张牌；
///     若除打击/防御外都已升级（无可升级候选）→ 睡觉；
/// - 帐篷类效果允许多选时：循环把所有选项按上述规则逐一选完。
///
/// 实现要点：
/// - 选择落点用私有 RestSiteSynchronizer.ChooseOption(Player, int)（反射缓存）——
///   公开的 ChooseLocalOption 绑定的是构造时的 _localPlayerId，永远指向真人；
/// - 锻造的选牌子流程（FromDeckForUpgrade，支持一次升 2 张）通过压入
///   LocalWakuuSmithSelector 自动作答：过滤打击/防御后取最后 SmithCount 张，
///   RequireManualConfirmation 会被选择器分支绕过（游戏测试模式同款机制）。
/// </summary>
internal static class LocalWakuuRestAutoChoice
{
    private const string HealOptionId = "HEAL";
    private const string SmithOptionId = "SMITH";
    private const string MendOptionId = "MEND";

    private const int OptionsReadyTimeoutMs = 5000;
    private const int MaxPicksPerRest = 10;

    /// <summary>正在自动选择的玩家（防 BeginRestSite postfix 与翻页刷新重复触发）。</summary>
    private static readonly HashSet<ulong> _inFlightOwners = new();
    private static readonly object _flightLock = new();

    private static readonly MethodInfo? _chooseOptionMethod = AccessTools.Method(
        AccessTools.TypeByName("MegaCrit.Sts2.Core.Multiplayer.Game.RestSiteSynchronizer"),
        "ChooseOption", new[] { typeof(Player), typeof(int) });

    private static readonly Random _random = new();
    private static readonly object _randomLock = new();

    /// <summary>由 RestSiteSynchronizerBeginRestSitePatch postfix 调用。</summary>
    public static void TryBeginPending()
    {
        try
        {
            if (!LocalSelfCoopContext.IsEnabled
                || !LocalWakuuAutopilotConfig.AutoRestChoice
                || !RunManager.Instance.IsInProgress)
            {
                return;
            }

            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            if (runState?.Players == null || runState.Players.Count <= 1)
            {
                return;
            }

            foreach (Player player in runState.Players.ToList())
            {
                if (player != null && LocalWakuuRelicRuntime.IsVakuuFormMode(player))
                {
                    TryBeginFor(player);
                }
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"扫描瓦库休息区失败: {exception.Message}");
        }
    }

    private static void TryBeginFor(Player player)
    {
        ulong ownerId = player.NetId;
        lock (_flightLock)
        {
            if (!_inFlightOwners.Add(ownerId))
            {
                return;
            }
        }

        LocalMultiControlLogger.Info($"瓦库火堆自动选择启动: player={ownerId}");
        TaskHelper.RunSafely(RunAsync(player, ownerId));
    }

    private static async Task RunAsync(Player player, ulong ownerId)
    {
        try
        {
            // 等待选项就绪（进房瞬间可能尚未生成）
            int waitedMs = 0;
            while (RunManager.Instance.IsInProgress
                   && GetOptions(player.NetId).Count == 0
                   && waitedMs < OptionsReadyTimeoutMs)
            {
                await Task.Delay(150);
                waitedMs += 150;
            }

            for (int pick = 0; pick < MaxPicksPerRest; pick++)
            {
                if (!RunManager.Instance.IsInProgress)
                {
                    return;
                }

                IReadOnlyList<RestSiteOption> options = GetOptions(ownerId);
                if (options.Count == 0)
                {
                    break; // 选完/被跳过补完，正常结束
                }

                RestSiteOption? choice = Decide(player, options);
                if (choice == null)
                {
                    LocalMultiControlLogger.Info(
                        $"瓦库火堆无可自动选择的选项，停住等真人处理: player={ownerId}");
                    return;
                }

                int index = options.ToList().IndexOf(choice);
                if (index < 0)
                {
                    return;
                }

                bool success;
                using (PushSelectorFor(choice, player))
                {
                    ulong? previousNetId = MegaCrit.Sts2.Core.Context.LocalContext.NetId;
                    AlignLocalContext(ownerId);
                    try
                    {
                        object? result = _chooseOptionMethod?.Invoke(
                            RunManager.Instance.RestSiteSynchronizer, new object?[] { player, index });
                        success = await (Task<bool>)(result ?? Task.FromResult(false));
                    }
                    finally
                    {
                        if (previousNetId != null)
                        {
                            AlignLocalContext(previousNetId.Value);
                        }
                    }
                }

                LocalMultiControlLogger.Info(
                    $"瓦库火堆已自动选择: player={ownerId}, option={choice.OptionId}, "
                    + $"hp={player.Creature?.CurrentHp}/{player.Creature?.MaxHp}, success={success}");
                if (!success)
                {
                    return;
                }

                await Task.Delay(400);
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"瓦库火堆自动选择异常，剩余交还真人: player={ownerId}, error={exception.Message}");
        }
        finally
        {
            lock (_flightLock)
            {
                _inFlightOwners.Remove(ownerId);
            }
        }
    }

    /// <summary>决策规则：返回要选的选项；null 表示交还真人。</summary>
    private static RestSiteOption? Decide(Player player, IReadOnlyList<RestSiteOption> options)
    {
        List<RestSiteOption> enabled = options.Where((o) => o.IsEnabled).ToList();

        RestSiteOption? heal = enabled.FirstOrDefault((o) => o.OptionId == HealOptionId);
        List<RestSiteOption> others = enabled.Where((o) => o.OptionId != HealOptionId).ToList();
        List<RestSiteOption> relicExtras = others
            .Where((o) => o.OptionId != SmithOptionId && o.OptionId != MendOptionId)
            .ToList();

        decimal maxHp = player.Creature?.MaxHp ?? 1m;
        decimal currentHp = player.Creature?.CurrentHp ?? 0m;
        bool lowHp = currentHp * 2m < maxHp; // 血量 < 50%

        if (lowHp && heal != null)
        {
            return heal; // 低血优先睡觉
        }

        if (relicExtras.Count > 0)
        {
            lock (_randomLock)
            {
                return others[_random.Next(others.Count)]; // 有遗物选项：睡觉以外随机
            }
        }

        RestSiteOption? smith = others.FirstOrDefault((o) => o.OptionId == SmithOptionId);
        if (smith != null && HasPreferredUpgradeCandidate(player))
        {
            return smith; // 高血且还有"非打击/防御"的可升级牌
        }

        if (heal != null)
        {
            return heal; // 没得升了（或锻造不可用）→ 睡觉
        }

        return others.Count > 0 ? others[0] : null;
    }

    /// <summary>牌库里是否存在"打击/防御以外"的可升级牌。</summary>
    private static bool HasPreferredUpgradeCandidate(Player player)
    {
        return player.Deck.Cards.Any((c) => c.IsUpgradable && !IsBasicStrikeOrDefend(c));
    }

    /// <summary>基础打击/防御识别：卡 id 含 STRIKE/DEFEND（覆盖各角色变体与酒狐等 mod 命名）。</summary>
    private static bool IsBasicStrikeOrDefend(CardModel card)
    {
        string id = card.Id.Entry.ToUpperInvariant();
        return id.Contains("STRIKE") || id.Contains("DEFEND");
    }

    private static IDisposable PushSelectorFor(RestSiteOption option, Player player)
    {
        if (option is SmithRestSiteOption smith)
        {
            return CardSelectCmd.PushSelector(new LocalWakuuSmithSelector(smith.SmithCount));
        }

        return CardSelectCmd.PushSelector(new LocalWakuuStrategySelector());
    }

    private static IReadOnlyList<RestSiteOption> GetOptions(ulong playerId)
    {
        return RunManager.Instance.RestSiteSynchronizer.GetOptionsForPlayer(playerId);
    }

    private static void AlignLocalContext(ulong playerId)
    {
        if (MegaCrit.Sts2.Core.Context.LocalContext.NetId == playerId)
        {
            return;
        }

        MegaCrit.Sts2.Core.Context.LocalContext.NetId = playerId;
        LocalSelfCoopContext.NetService?.SetCurrentSenderId(playerId);
    }
}

/// <summary>
/// 火堆锻造专用作答选择器：把游戏提供的可升级列表先滤掉打击/防御，
/// 再取最后 SmithCount 张（默认升最后一张；一次升两张时取最后两张）。
/// 过滤后为空则退化为纯取最后（此时外层规则通常已经改选睡觉，不会走到这）。
/// </summary>
internal sealed class LocalWakuuSmithSelector : ICardSelector
{
    private readonly int _smithCount;

    public LocalWakuuSmithSelector(int smithCount)
    {
        _smithCount = smithCount;
    }

    public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
    {
        List<CardModel> list = options.ToList();
        List<CardModel> preferred = list.Where((c) =>
        {
            string id = c.Id.Entry.ToUpperInvariant();
            return !id.Contains("STRIKE") && !id.Contains("DEFEND");
        }).ToList();

        IEnumerable<CardModel> ordered = (preferred.Count > 0 ? preferred : list).AsEnumerable().Reverse();
        return Task.FromResult(ordered.Take(Math.Max(_smithCount, minSelect)));
    }

    public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives)
    {
        return new CardRewardSelection
        {
            card = options.FirstOrDefault()?.Card,
        };
    }
}
