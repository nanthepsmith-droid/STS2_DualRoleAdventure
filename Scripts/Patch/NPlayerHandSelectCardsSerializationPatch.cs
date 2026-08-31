using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 本地双角色模式下，两个本地角色可能在同一个同步点（例如双方同时持有灵乌路空「炉心融解」时回合开始
/// 各自触发一次 FromHand 选牌）先后调用 NPlayerHand.SelectCards。NPlayerHand 只有单个
/// _selectionCompletionSource（NPlayerHand.cs 的 SelectCards 里会被每次调用覆盖），第二次调用会覆盖第一次
/// 正在等待的 TCS，导致第一个角色的选择永远无法完成（软锁：角色无法出牌/结束回合）。
///
/// 本类把战斗内手牌选牌串行化：同一时刻只允许一个 SelectCards 进行中，后到的选牌先异步等待前一个结束
/// （SemaphoreSlim 闸门），并在展示前把前台/控制上下文切到本次选择所属角色，保证弹窗展示的是正确角色的手牌，
/// 且两个角色的选牌都能依次完成。
///
/// 重入判定使用 AsyncLocal 而非静态标志：包装任务在「重入原始 SelectCards 等待玩家选择」期间，AsyncLocal
/// 只沿自身异步链向下流动；来自 ActionExecutor 的兄弟调用链看不到该值，因此会被正确拦下并等待闸门。
/// 若用静态标志，第一个包装任务等待玩家选择期间标志恒为 true，第二个 SelectCards 会绕过串行化直接执行，
/// 重新覆盖第一个的 TCS，死锁原样复现。
/// </summary>
[HarmonyPatch(typeof(NPlayerHand), nameof(NPlayerHand.SelectCards))]
internal static class NPlayerHandSelectCardsSerializationPatch
{
    /// <summary>全局选牌闸门：同一时刻只允许一个 SelectCards 进行中。</summary>
    private static readonly System.Threading.SemaphoreSlim _selectionGate = new System.Threading.SemaphoreSlim(1, 1);

    /// <summary>
    /// 标记「本次 SelectCards 是包装任务重入原始实现的调用」。为 true 时前缀直接放行原始逻辑，
    /// 避免无限递归；兄弟调用链（ActionExecutor 发起的下一次选牌）看不到该值，会走串行化等待。
    /// </summary>
    private static readonly System.Threading.AsyncLocal<bool> _inSerialized = new System.Threading.AsyncLocal<bool>();

    /// <summary>
    /// 当前进行中的选牌所属角色（选牌开始设置、选牌结束清除）。
    /// 供 NPlayerHandAddOwnerGuardPatch 判断「选牌期间加入手牌 UI 的卡牌是否属于本次选牌玩家」，
    /// 防止对家后台操作（Exhaust/枯木树枝生成牌）把非选牌玩家的牌节点加进当前选牌界面。
    /// </summary>
    internal static ulong? CurrentSelectionOwnerId { get; private set; }

    [HarmonyPriority(Priority.High)]
    [HarmonyPrefix]
    private static bool SelectCardsPrefix(
        NPlayerHand __instance,
        CardSelectorPrefs prefs,
        Func<CardModel, bool>? filter,
        AbstractModel? source,
        NPlayerHand.Mode mode,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return true;
        }

        if (_inSerialized.Value)
        {
            LocalMultiControlLogger.Info($"战斗内手牌选牌串行化: 重入原始实现(包装内), mode={mode}");
            return true;
        }

        __result = SerializedSelectCardsAsync(__instance, prefs, filter, source, mode);
        return false;
    }

    private static async Task<IEnumerable<CardModel>> SerializedSelectCardsAsync(
        NPlayerHand hand,
        CardSelectorPrefs prefs,
        Func<CardModel, bool>? filter,
        AbstractModel? source,
        NPlayerHand.Mode mode)
    {
        bool gateAcquired = false;
        ulong? previousNetId = null;
        bool netIdPinned = false;
        try
        {
            await _selectionGate.WaitAsync();
            gateAcquired = true;

            ulong? ownerId = ResolveSelectionOwnerId(source);
            if (ownerId.HasValue)
            {
                bool switched = LocalMultiControlRuntime.TryEnsureForegroundForPlayerId(ownerId.Value, "select-serialized");
                if (switched)
                {
                    LocalMultiControlLogger.Info($"选牌展示前已切换前台到所属角色: player={ownerId.Value}");
                }

                // 修复：选牌期间把 LocalContext.NetId 钉到本次选牌所属角色，并保持到选牌结束。
                // 根因：本场 527 的 MiniHakkero 选牌期间，526 的 MiniHakkero Exhaust 触发枯木树枝
                // （CardPileCmd.AddGeneratedCardsToCombat 进 526 手牌），而 CardPileCmd.GetTweenForCardsChangingPiles
                // 的视觉门用 LocalContext.IsMe(card.Owner) 判断是否创建卡牌节点；若 NetId 此刻不是选牌 owner，
                // 526 的牌会被误判为"本人"创建节点并加进当前（527 的）选牌 UI，导致 527 能选到 526 手牌里的牌。
                previousNetId = LocalContext.NetId;
                LocalContext.NetId = ownerId.Value;
                netIdPinned = true;

                CurrentSelectionOwnerId = ownerId.Value;
            }

            LocalMultiControlLogger.Info(
                $"战斗内手牌选牌串行化: 已进入选牌, mode={mode}, owner={ownerId}, source={source?.GetType().Name}");

            // 临时诊断（r32）：记录当前手牌 UI 实际显示的卡牌实例，定位「选牌界面出现其他玩家手牌」的串台是数据层还是 UI 层。
            try
            {
                string uiCards = string.Join(",",
                    hand.CardHolderContainer.GetChildren()
                        .OfType<MegaCrit.Sts2.Core.Nodes.Cards.Holders.NCardHolder>()
                        .Select(h => h.CardNode?.Model)
                        .Where(c => c != null)
                        .Select(c => $"{c!.Id.Entry}#{RuntimeHelpers.GetHashCode(c)}"));
                LocalMultiControlLogger.Info(
                    $"[选牌诊断] NPlayerHand UI holder: owner={ownerId}, LocalContext.NetId={LocalContext.NetId}, uiCards=[{uiCards}]");
            }
            catch (Exception uiEx)
            {
                LocalMultiControlLogger.Warn($"[选牌诊断] 记录 UI holder 失败: {uiEx.Message}");
            }

            _inSerialized.Value = true;
            try
            {
                return await hand.SelectCards(prefs, filter, source, mode);
            }
            finally
            {
                _inSerialized.Value = false;
            }
        }
        finally
        {
            CurrentSelectionOwnerId = null;

            if (netIdPinned)
            {
                LocalContext.NetId = previousNetId;
            }

            if (gateAcquired)
            {
                _selectionGate.Release();
            }
        }
    }

    /// <summary>
    /// 解析本次选牌所属角色 NetId：优先取当前异步链的选牌角色（由 CardSelectForegroundSwitchPatch 在
    /// FromHand 等入口记录，沿链流动，能正确处理双方同时选牌的交错场景），再退回 source 模型的 owner。
    /// </summary>
    private static ulong? ResolveSelectionOwnerId(AbstractModel? source)
    {
        ulong? ownerId = CardSelectForegroundSwitchPatch.CurrentChoicePlayerId.Value;
        if (!ownerId.HasValue || !LocalSelfCoopContext.LocalPlayerIds.Contains(ownerId.Value))
        {
            ownerId = ResolveOwnerIdFromSource(source);
        }

        if (ownerId.HasValue && LocalSelfCoopContext.LocalPlayerIds.Contains(ownerId.Value))
        {
            return ownerId;
        }

        return null;
    }

    private static ulong? ResolveOwnerIdFromSource(AbstractModel? source)
    {
        if (source == null)
        {
            return null;
        }

        Player? owner = source switch
        {
            CardModel card => card.Owner,
            RelicModel relic => relic.Owner,
            PotionModel potion => potion.Owner,
            AfflictionModel affliction => affliction.Card?.Owner,
            EnchantmentModel enchantment => enchantment.Card?.Owner,
            PowerModel power => power.Owner?.Player,
            _ => null
        };

        return owner?.NetId;
    }
}
