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
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
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
        // 本次访问中已证实不可用（失败/抛异常）的选项，重试时排除
        HashSet<string> brokenOptionIds = new(StringComparer.OrdinalIgnoreCase);

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

            // 等待真正进入休息区房间：BeginRestSite 在转场期间就会触发，
            // 此时房间尚未加载完成，立即选择会踩空引用（r9 实测异常交还真人）
            waitedMs = 0;
            while (RunManager.Instance.IsInProgress
                   && RunManager.Instance.DebugOnlyGetState()?.CurrentRoom is not RestSiteRoom
                   && waitedMs < OptionsReadyTimeoutMs)
            {
                await Task.Delay(150);
                waitedMs += 150;
            }

            // 等待 UI 房间节点就绪：AfterPlayerOptionChosen 等"角色头顶选择气泡"
            // 依赖的事件在 NRestSiteRoom._Ready 里才订阅，过早选择会选成功但不显示
            waitedMs = 0;
            while (RunManager.Instance.IsInProgress
                   && (NRestSiteRoom.Instance == null || NRestSiteRoom.Instance.GetCharacterForPlayer(player) == null)
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

                RestSiteOption? choice = Decide(player, options, brokenOptionIds);
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

                // 选择前：角色头顶先显示"正在考虑"气泡（与真人悬停时的表现一致）
                ShowCharacterBubble(player, choice, selecting: true);

                bool success;
                try
                {
                    using (PushSelectorFor(choice, player))
                    {
                        IsAwaitingOptionExecution = true;
                        try
                        {
                            object? result = _chooseOptionMethod?.Invoke(
                                RunManager.Instance.RestSiteSynchronizer, new object?[] { player, index });
                            success = await (Task<bool>)(result ?? Task.FromResult(false));
                        }
                        finally
                        {
                            IsAwaitingOptionExecution = false;
                        }
                    }
                }
                catch (Exception pickException)
                {
                    // 单个选项执行失败（转场期依赖未就绪 / mod 选项内部异常）：
                    // 标记为不可用并换下一个，不再整轮放弃
                    brokenOptionIds.Add(choice.OptionId);
                    LocalMultiControlLogger.Warn(
                        $"瓦库火堆选项执行异常，换下一个: player={ownerId}, option={choice.OptionId}, "
                        + $"error={pickException.Message}");
                    continue;
                }

                LocalMultiControlLogger.Info(
                    $"瓦库火堆已自动选择: player={ownerId}, option={choice.OptionId}, "
                    + $"hp={player.Creature?.CurrentHp}/{player.Creature?.MaxHp}, success={success}");
                if (!success)
                {
                    // OnSelect 返回 false（选项自身判定不可用，如 CHH_MUTUAL_AID）：排除后换下一个
                    brokenOptionIds.Add(choice.OptionId);
                    continue;
                }

                // 选择成功：显式驱动角色头顶确认图标。正常由 AfterPlayerOptionChosen
                // 事件驱动，但多张升级等长流程下可能被时序吞掉，这里兜底重画一次
                //（重复调用只是叠加一个同图标的确认节点，视觉上无差异）。
                ShowCharacterBubble(player, choice, selecting: false);

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

    /// <summary>决策规则：返回要选的选项；null 表示交还真人。brokenOptionIds 为本次已证实不可用的选项。</summary>
    private static RestSiteOption? Decide(
        Player player, IReadOnlyList<RestSiteOption> options, HashSet<string> brokenOptionIds)
    {
        List<RestSiteOption> enabled = options
            .Where((o) => o.IsEnabled && !brokenOptionIds.Contains(o.OptionId))
            .ToList();

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
    internal static bool IsBasicStrikeOrDefend(CardModel card)
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

    /// <summary>
    /// 显式驱动瓦库角色头顶的选项气泡（selecting=true 为"考虑中"思考泡，false 为确认图标）。
    /// 游戏本应由 AfterPlayerOptionChosen 等事件驱动，这里兜底保证多控下表现一致。
    /// </summary>
    private static void ShowCharacterBubble(Player player, RestSiteOption option, bool selecting)
    {
        try
        {
            NRestSiteCharacter? character = NRestSiteRoom.Instance?.GetCharacterForPlayer(player);
            if (character == null)
            {
                return;
            }

            if (selecting)
            {
                character.SetSelectingRestSiteOption(option);
            }
            else
            {
                character.SetSelectingRestSiteOption(null);
                character.ShowSelectedRestSiteOption(option);
            }
        }
        catch (Exception exception)
        {
            // 纯表现层兜底，失败不影响选择本身
            LocalMultiControlLogger.Warn($"驱动瓦库火堆气泡失败: option={option.OptionId}, error={exception.Message}");
        }
    }

    /// <summary>
    /// 仅在瓦库火堆选项执行期间为 true：供 WaitForRemoteChoice 拦截补丁判定
    /// 该远端等待属于本流程（避免误伤事件/其他系统的同名等待）。
    /// </summary>
    internal static bool IsAwaitingOptionExecution { get; private set; }

    /// <summary>
    /// 为瓦库的"选一个队友"类等待指定目标：优先另一个存活玩家（即真人）。
    /// 返回 null 表示没有可用队友（调用方可回退为无目标结果）。
    /// </summary>
    internal static ulong? GetPreferredTeammateNetId(Player owner)
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        Player? teammate = runState?.Players.FirstOrDefault((p) =>
            p != null && p.NetId != owner.NetId && p.Creature?.IsDead != true);
        return teammate?.NetId;
    }
}

/// <summary>
/// 火堆锻造专用作答选择器：优先升级"打击/防御以外"的牌并取最后 SmithCount 张；
/// 若这类牌不足 SmithCount 张（如只剩 1 张但本次可升 2 张），剩余槽位用打击/防御补齐
/// （同样取最后）。全部都无候选时退化为纯取最后（此时外层规则通常已改选睡觉）。
/// </summary>
internal sealed class LocalWakuuSmithSelector : ICardSelector
{
    private readonly int _smithCount;

    public LocalWakuuSmithSelector(int smithCount)
    {
        _smithCount = Math.Max(1, smithCount);
    }

    public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
    {
        List<CardModel> list = options.ToList();
        List<CardModel> preferred = list.Where((c) => !LocalWakuuRestAutoChoice.IsBasicStrikeOrDefend(c)).ToList();
        List<CardModel> fallback = list.Where(LocalWakuuRestAutoChoice.IsBasicStrikeOrDefend).ToList();

        // 主选：非打击/防御的最后 N 张；不足的槽位用打击/防御的最后几张补齐
        List<CardModel> selected = preferred.Skip(Math.Max(0, preferred.Count - _smithCount)).ToList();
        int remaining = _smithCount - selected.Count;
        if (remaining > 0)
        {
            selected.AddRange(fallback.Skip(Math.Max(0, fallback.Count - remaining)));
        }

        return Task.FromResult((IEnumerable<CardModel>)selected);
    }

    public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives)
    {
        return new CardRewardSelection
        {
            card = options.FirstOrDefault()?.Card,
        };
    }
}
