using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 瓦库托管席位遇到**第三方自绘选牌界面**时由其自动作答（BUG-23 方案 B 第二步）。
///
/// 方案 B 的两半：
/// <list type="number">
/// <item><see cref="LocalContextThirdPartyIsMePatch"/>：在"我们正在为这一席自动化"的窗口内让第三方把它
///   当成同机玩家 ⇒ 第三方走自己的**本地分支**、调用 <c>NChooseACardSelectionScreen.ShowScreen</c> 弹界面
///   （沙耶 mod 的 `CommonActions.SelectCenteredBranchCards` 就是这个形状）；</item>
/// <item>本补丁：界面出现后按瓦库策略选一张，并通过**游戏自己的点击路径**完成（发
///   <c>NCardHolder.SignalName.Pressed</c> ⇒ 屏幕自己的 <c>SelectHolder</c>），于是
///   `await screen.CardsSelected()` 拿到牌、整条效果正常结算 —— 而不是卡死或整段跳过。</item>
/// </list>
///
/// 判据 = <see cref="WakuuSelfDrawnChoicePolicy.ShouldAutoAnswerScreen"/>（纯逻辑、有单测）：
/// 该席位此刻**正被我们的自动化驱动**（登记着托管选择器）+ 界面上确实有候选 + 这个界面没被我们作答过。
/// ⇒ 真人亲自操作该席位（无托管选择器）时我们**绝不**代点。
///
/// 时序要点（踩过即失效）：
/// <list type="bullet">
/// <item>屏幕对点击有 **350ms 保护窗**（`_openedTicks`）⇒ 延迟作答要留够余量；</item>
/// <item>候选 holder 是屏幕在 `_Ready` 里建的，`ShowScreen` 返回那一刻**还没建**
///   （候选数只能读私有的 `_cards` 字段）⇒ 点击必须等一帧以后再做；</item>
/// <item>多选/控制器改写（如沙耶的 `CenteredBranchSelectionPatch`）不由我们模拟 —— 若单击没让界面关闭，
///   本补丁会打一条 WARN 点名（"交回真人"），作为下一轮适配的实证锚点。</item>
/// </list>
/// </summary>
[HarmonyPatch(typeof(NChooseACardSelectionScreen), nameof(NChooseACardSelectionScreen.ShowScreen))]
internal static class NChooseACardSelectionScreenAutoAnswerPatch
{
    /// <summary>点击保护窗（350ms）之后 + 等 `_Ready` 建好 holder 的余量。</summary>
    private const float AnswerDelaySeconds = 0.6f;

    /// <summary>候选 holder 还没建好时的重试间隔 / 次数（正常一次就够；只是别把"晚了半拍"误判成交回真人）。</summary>
    private const float RetryDelaySeconds = 0.35f;

    private const int MaxAnswerAttempts = 3;

    /// <summary>作答后再等这么久核对界面是否真的关了（没关 ⇒ 大概率是多选，交回真人）。</summary>
    private const float VerifyDelaySeconds = 1.2f;

    /// <summary>已被本补丁接手的屏幕（弱引用键，防重复驱动；随屏幕被回收自然失效）。</summary>
    private static readonly ConditionalWeakTable<NChooseACardSelectionScreen, object> Marked = new();

    private static readonly object MarkValue = new();

    [HarmonyPostfix]
    private static void Postfix(NChooseACardSelectionScreen? __result)
    {
        if (__result == null || !GodotObject.IsInstanceValid(__result))
        {
            return;
        }

        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (RunManager.Instance?.NetService is not LocalLoopbackHostGameService)
        {
            return;
        }

        Player? chooser = TryResolveChooser(__result);
        if (chooser == null)
        {
            return;
        }

        bool automatedSeatInPlay = WakuuSelfDrawnChoicePolicy.IsAutomatedSeatInPlay(
            enabled: true,
            singleAdventureMode: true,
            loopbackSession: true,
            isLocalSeat: LocalSeatSource.IsLocalSeat(chooser.NetId),
            backgroundMode: LocalWakuuAutopilotConfig.BackgroundMode,
            vakuuFormMode: LocalWakuuRelicRuntime.IsVakuuFormMode(chooser),
            hasManagedSelector: WakuuSelectorRegistry.TryGet(chooser.NetId, out _));
        if (!automatedSeatInPlay)
        {
            return;
        }

        // 此刻 holder 还没建 ⇒ 候选数只能读私有 `_cards`（`ShowScreen` 在 Push 之前就已赋值）。
        int optionCount = TryReadCardsField(__result)?.Count ?? 0;
        bool alreadyAnswered = Marked.TryGetValue(__result, out _);
        if (!WakuuSelfDrawnChoicePolicy.ShouldAutoAnswerScreen(automatedSeatInPlay, optionCount, alreadyAnswered))
        {
            LocalMultiControlLogger.Info(
                $"瓦库自绘选牌界面出现但本次不代答: chooser={chooser.NetId}, options={optionCount}, "
                + $"alreadyAnswered={alreadyAnswered}, screen=NChooseACardSelectionScreen");
            return;
        }

        Marked.Add(__result, MarkValue);

        SceneTree? tree = NGame.Instance?.GetTree();
        if (tree == null)
        {
            return;
        }

        ulong chooserId = chooser.NetId;
        NChooseACardSelectionScreen screen = __result;
        tree.CreateTimer(AnswerDelaySeconds).Timeout += () => TryAnswer(screen, chooserId, attempt: 1);
    }

    /// <summary>私有 `_cards`（`ShowScreen` 里在 Push 之前赋值）—— 界面刚建出来时唯一可读的候选来源。</summary>
    private static List<CardModel>? TryReadCardsField(NChooseACardSelectionScreen screen)
    {
        try
        {
            object? raw = AccessTools.Field(typeof(NChooseACardSelectionScreen), "_cards")?.GetValue(screen);
            if (raw is IReadOnlyList<CardModel> cards)
            {
                return cards.Where((card) => card != null).ToList();
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库自绘选牌读取候选失败: {exception.Message}");
        }

        return null;
    }

    /// <summary>
    /// 候选 holder（屏幕 `_Ready` 里建）；既是候选顺序，也是触发点击信号要用的节点。
    ///
    /// ⚠ **不要用 `Node.FindChildren("*", "NGridCardHolder", …)`**（r182 实机踩过）：Godot 的 `type` 过滤器
    /// 按**原生 ClassDB 类名**匹配，而 `NGridCardHolder` 是 C# 脚本类（原生类是 `Control`）
    /// ⇒ 恒返回空、我们打"无候选节点"就把界面交给真人了。这里用 <see cref="LocalNodeTree.EnumerateDescendants"/>
    /// 按 C# 类型遍历（R2 的单点化设施，正是为这类场景收的）。
    /// </summary>
    private static List<NGridCardHolder> ReadHolders(NChooseACardSelectionScreen screen)
    {
        List<NGridCardHolder> holders = new();
        foreach (Node node in LocalNodeTree.EnumerateDescendants(screen))
        {
            if (node is NGridCardHolder holder && holder.CardModel != null)
            {
                holders.Add(holder);
            }
        }

        return holders;
    }

    private static Player? TryResolveChooser(NChooseACardSelectionScreen screen)
    {
        List<CardModel>? cards = TryReadCardsField(screen);
        if (cards != null && cards.Count > 0 && cards[0].Owner != null)
        {
            return cards[0].Owner;
        }

        foreach (NGridCardHolder holder in ReadHolders(screen))
        {
            if (holder.CardModel?.Owner != null)
            {
                return holder.CardModel.Owner;
            }
        }

        return null;
    }

    private static void TryAnswer(NChooseACardSelectionScreen screen, ulong chooserId, int attempt)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree())
            {
                return;
            }

            if (!RunManager.Instance.IsInProgress)
            {
                return;
            }

            // 这中间作用域可能已经结束（动作被取消 / 局面变了）⇒ 交回真人，不代点。
            if (!WakuuSelectorRegistry.TryGet(chooserId, out _))
            {
                LocalMultiControlLogger.Info(
                    $"瓦库自绘选牌界面出现但自动化作用域已结束，本次不代答: chooser={chooserId}, "
                    + "screen=NChooseACardSelectionScreen");
                return;
            }

            List<NGridCardHolder> holders = ReadHolders(screen);
            if (holders.Count == 0)
            {
                // 候选是屏幕在 `_Ready` 里建的：偶尔会晚半拍 ⇒ 重试几次再判定"交回真人"。
                if (attempt < MaxAnswerAttempts)
                {
                    SceneTree? retryTree = NGame.Instance?.GetTree();
                    if (retryTree != null)
                    {
                        retryTree.CreateTimer(RetryDelaySeconds).Timeout += () => TryAnswer(screen, chooserId, attempt + 1);
                        return;
                    }
                }

                LocalMultiControlLogger.Warn(
                    $"瓦库自绘选牌界面无候选节点，本次不代答: chooser={chooserId}, attempt={attempt}");
                return;
            }

            List<CardModel> options = holders.Select((holder) => holder.CardModel!).ToList();
            Task<IEnumerable<CardModel>> pickTask = LocalWakuuStrategySelector.Shared.GetSelectedCards(options, 1, 1);
            CardModel? picked = pickTask.IsCompletedSuccessfully ? pickTask.Result.FirstOrDefault() : null;
            if (picked == null)
            {
                LocalMultiControlLogger.Warn(
                    $"瓦库自绘选牌未选出候选，本次不代答: chooser={chooserId}, options={options.Count}");
                return;
            }

            NGridCardHolder? target = holders.FirstOrDefault((holder) => ReferenceEquals(holder.CardModel, picked));
            if (target == null)
            {
                LocalMultiControlLogger.Warn(
                    $"瓦库自绘选牌选中项不在界面上，本次不代答: chooser={chooserId}, picked={picked.Id.Entry}");
                return;
            }

            LocalMultiControlLogger.Info(
                $"瓦库自绘选牌自动作答: chooser={chooserId}, options={options.Count}, "
                + $"picked={picked.Id.Entry}, mode={LocalWakuuAutopilotConfig.CardPickMode}, "
                + "screen=NChooseACardSelectionScreen");
            // 走游戏自己的点击路径（屏幕 `_Ready` 把 Pressed 连到自己的 SelectHolder）：
            // 不碰任何私有方法，将来屏幕内部改动也不会绕过它的副作用。
            target.EmitSignal(NCardHolder.SignalName.Pressed, target);

            ScheduleVerify(screen, chooserId);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库自绘选牌自动作答失败，交回真人: chooser={chooserId}, error={exception.Message}");
        }
    }

    /// <summary>作答后核对界面是否真的关闭：没关 ⇒ 大概率是多选/第三方改写过的界面，留下证据并交回真人。</summary>
    private static void ScheduleVerify(NChooseACardSelectionScreen screen, ulong chooserId)
    {
        SceneTree? tree = NGame.Instance?.GetTree();
        if (tree == null)
        {
            return;
        }

        tree.CreateTimer(VerifyDelaySeconds).Timeout += () =>
        {
            if (!GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree())
            {
                return;
            }

            LocalMultiControlLogger.Warn(
                $"瓦库自绘选牌单击后界面仍未关闭（可能是多选/被第三方改写），交回真人: chooser={chooserId}, "
                + "screen=NChooseACardSelectionScreen");
        };
    }
}
