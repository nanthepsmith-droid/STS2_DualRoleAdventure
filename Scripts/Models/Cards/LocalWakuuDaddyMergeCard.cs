#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace LocalMultiControl.Scripts.Models.Cards;

/// <summary>
/// ④「瓦库的爹」三张牌之三：**【合体】**（提案 §5.1 = 0 费技能，升级加「保留」）。
///
/// 效果（r228 实装）：选择一个**瓦库队友** ⇒
/// <list type="number">
/// <item>它的手牌（战斗牌，都是卡组克隆）**改归属**后进你的手牌；</item>
/// <item>它的能量全给你（<c>PlayerCmd.LoseEnergy</c> + <c>GainEnergy</c>，会走原版的能量增益钩子）；</item>
/// <item>**本回合**你抽牌时随机从「自己 / 它」的抽牌堆取、弃牌时随机进其中一方的弃牌堆
///   （登记在 <see cref="WakuuDaddyCombatState"/>，搬运见 <see cref="Patch.WakuuDaddyMergePilePatch"/>）；</item>
/// <item>随后抽 1 张牌（这一抽本身就吃上面的混抽规则）。</item>
/// </list>
///
/// 目标必须是瓦库托管席位（判定 <see cref="WakuuDaddyCardPolicy.CanMergeTarget"/>）：
/// <see cref="TargetType.AnyAlly"/> 没法把"队友"筛成"瓦库" ⇒ 点在真人队友上时**不发动效果**（只抽 1 张）。
///
/// 池子 / 卡图口径见 <see cref="LocalWakuuDaddyShieldCard"/>。
/// </summary>
internal sealed class LocalWakuuDaddyMergeCard : CardModel
{
    public LocalWakuuDaddyMergeCard()
        : base(0, CardType.Skill, CardRarity.Event, TargetType.AnyAlly)
    {
    }

    /// <summary>没有 PCK ⇒ 借原版事件牌【Stack】的卡图（与另两张牌区分开）。</summary>
    public override string PortraitPath => ModelDb.Card<Stack>().PortraitPath;

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }

    /// <summary>选一个瓦库队友 ⇒ 手牌与能量归你 + 本回合混抽混弃；随后抽 1 张牌。</summary>
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        Player? wakuu = cardPlay.Target?.Player;
        if (wakuu != null)
        {
            // R13b（2026-10-10 用户口径「【合体】覆盖战神」）：战灵召唤的战神是**托管席位**，
            // 但它**不在** `IsWakuuEnabled` 那张玩家指定的瓦库名单里 ⇒ 两个判据取并集。
            bool isWakuuSeat = LocalSelfCoopContext.IsWakuuEnabled(wakuu.NetId)
                || LocalSelfCoopContext.IsTemporarySeat(wakuu.NetId);
            if (WakuuDaddyCardPolicy.CanMergeTarget(isWakuuSeat, ReferenceEquals(wakuu, Owner)))
            {
                await MergeAsync(wakuu);
            }
            else
            {
                // ⚠ 战灵召唤的战神**不在** `IsWakuuEnabled` 名单里（它只是"托管席位"，不是玩家指定的瓦库）
                // ⇒ 点在战神身上会走到这里、**效果不发动**（只抽 1 张）。R13 只覆盖【我挡】/【你攻】，
                // 合体要不要也覆盖战神 = 待拍板（见设计文档 §8 Phase 5 / TODO）。
                LocalMultiControlLogger.Info(
                    $"[瓦库的爹] 合体目标不是瓦库托管席位，效果未发动（只抽 1 张）: target={wakuu.NetId}, "
                    + $"isWakuuSeat={isWakuuSeat}, 是临时席位(战神)={LocalSelfCoopContext.IsTemporarySeat(wakuu.NetId)}");
            }
        }

        await CardPileCmd.Draw(choiceContext, 1m, Owner);
    }

    /// <summary>手牌改归属转移 + 能量转移 + 登记本回合混抽混弃。</summary>
    private async Task MergeAsync(Player wakuu)
    {
        CardPile? hand = PileType.Hand.GetPile(wakuu);
        List<CardModel> cards = hand?.Cards.ToList() ?? new List<CardModel>();
        foreach (CardModel card in cards)
        {
            // 🔴 **顺序铁律**（r229 实机事故，务必照抄）：
            //   `CardModel.Pile` 不是自己的字段，而是**按 Owner 反查**出来的 ——
            //   `Pile => _owner?.Piles.FirstOrDefault(p => p.Cards.Contains(this))`。
            //   所以「先改归属、再 CardPileCmd.Add」会让 `card.Pile` 在改归属那一刻变成 **null**
            //   （牌还没进任何新牌堆），`Add` 于是以为"这牌不在任何牌堆里"、**跳过摘除** ⇒
            //   同一张牌**同时留在瓦库手里 + 进你手里**（一个实例挂在两个牌堆上）。
            //   后果（实机）：瓦库把这张"自己的牌"每回合重打 60 次（撞 `cap=60` 护栏上限）、
            //   卡牌节点暴涨（那局泄漏 6.1 万 CanvasItem）⇒ 严重卡顿。
            //   ✅ 正确顺序 = **先摘（此时 Owner 还是瓦库，Pile 正确指向瓦库手牌）→ 再改归属 → 再 Add**
            //   （= 原版 `CardPileCmd.GiveToAnotherPlayer` 内部的三步，只是它随后用"桌上已有节点"做动画、
            //   对瓦库手牌取不到节点所以什么都不画，见下）；`Add` 此时 `oldPile == null`，
            //   仍会建节点并挂进手牌（`GetTweenForCardsChangingPiles` 的 `pileType == Hand` 分支）。
            //   手牌已满时原版会把它落进接收方的弃牌堆（`CardPileCmd.Add` 的 isFullHandAdd 分支）。
            card.RemoveFromCurrentPile(silent: true);
            card.GiveToAnotherPlayer(Owner);
            await CardPileCmd.Add(card, PileType.Hand);
        }

        int energy = wakuu.PlayerCombatState?.Energy ?? 0;
        if (energy > 0)
        {
            await PlayerCmd.LoseEnergy(energy, wakuu);
            await PlayerCmd.GainEnergy(energy, Owner);
        }

        WakuuDaddyCombatState.RegisterMerge(Owner, wakuu);
        bool targetIsWarGod = LocalSelfCoopContext.IsTemporarySeat(wakuu.NetId);
        // 顺手把两侧抽牌堆张数打出来：混抽的日志只在"真的搬了 N 张（N>0）"时才有 —— 有了这一行，
        // 判读时就不会把"瓦库抽牌堆本来就空 ⇒ 混抽没得混"误读成"混抽没生效"。
        int wakuuDraw = PileType.Draw.GetPile(wakuu)?.Cards.Count ?? -1;
        int ownDraw = PileType.Draw.GetPile(Owner)?.Cards.Count ?? -1;
        LocalMultiControlLogger.Info(
            $"[瓦库的爹] 合体：收编手牌={cards.Count} 张、能量={energy}，施牌者 {Owner.NetId} ← "
            + $"瓦库 {wakuu.NetId}{(targetIsWarGod ? "(战神)" : string.Empty)}"
            + $"（混抽可用性：瓦库抽牌堆={wakuuDraw} 张, 我方={ownDraw} 张）"
            + (targetIsWarGod ? "；⚠ 战神是临时席位：本回合混抽/混弃走 TryGetMergedWakuu 的【在册战神】分支" : string.Empty));

        // 「空气牌」自检（见 ProbeMergedCards 注释）：只在真的搬了牌时排一次。
        if (cards.Count > 0)
        {
            List<CardModel> moved = cards;
            Callable.From(() => ProbeMergedCards(Owner, moved)).CallDeferred();
        }
    }

    /// <summary>
    /// 合体后**下一帧**的手牌节点自检（2026-10-10 用户实机反馈：「与战神合体有时候会出现空气牌，
    /// 这张牌没有样子但确实占了位置，切人刷新才显示出这张牌的样子」）。
    ///
    /// 为什么要先探针而不是直接修：这句现象对应**两种完全不同的成因**，修法完全相反 ——
    /// <list type="bullet">
    /// <item><b>(a) 手牌 UI 少建了节点</b>（牌在手牌堆、界面给了空位却没 holder）⇒ 要在合体链路里补建/补挂节点；</item>
    /// <item><b>(b) holder 与节点都在、只是卡面没渲染出来</b>（美术/异步那一层）⇒ 补建节点毫无作用，
    ///   得走"重刷该节点的视觉"或第三方美术链（本环境里 `SayaArtApi/SayaApiBridge` 会介入卡面解析）。</item>
    /// </list>
    /// 探针把这两种分开：打印每张搬运牌的 `holder 存在 / 在容器里 / 节点在树里 / 可见 / 缩放` +
    /// 手牌堆张数 vs 手牌 UI holder 张数；判定为 (a) 时打 WARN、否则打 INFO。
    ///
    /// ⚠ **只观测、不自动修**：出牌途中整表重建会打断出牌链（r111 实机回归教训，见
    /// `LocalMultiControlRuntime` 手牌对账那段注释）。拿到证据再决定修法。
    /// </summary>
    private static void ProbeMergedCards(Player receiver, IReadOnlyList<CardModel> moved)
    {
        try
        {
            NPlayerHand? hand = NCombatRoom.Instance?.Ui?.Hand;
            if (hand?.CardHolderContainer == null)
            {
                LocalMultiControlLogger.Info(
                    $"[瓦库的爹] 合体自检：战斗手牌 UI 当前不可用（hand=null）⇒ 无法核对节点，"
                    + $"接收方={receiver.NetId}, 搬运={moved.Count} 张");
                return;
            }

            int pileCount = PileType.Hand.GetPile(receiver)?.Cards.Count ?? -1;
            int holderCount = 0;
            foreach (Node child in hand.CardHolderContainer.GetChildren())
            {
                if (child is NCardHolder candidate && GodotObject.IsInstanceValid(candidate)
                    && candidate.CardNode?.Model != null)
                {
                    holderCount++;
                }
            }

            List<string> details = new(moved.Count);
            int missing = 0;
            foreach (CardModel card in moved)
            {
                NCardHolder? holder = hand.GetCardHolder(card);
                bool holderValid = holder != null && GodotObject.IsInstanceValid(holder);
                bool inContainer = holderValid && hand.CardHolderContainer.IsAncestorOf(holder!);
                NCard? node = holderValid ? holder!.CardNode : null;
                bool nodeValid = node != null && GodotObject.IsInstanceValid(node);
                if (!inContainer)
                {
                    missing++;
                }

                details.Add(
                    $"{card.Id.Entry}[holder={( holderValid ? "有" : "无")}, 在容器={(inContainer ? "是" : "否")}, "
                    + $"节点在树={(nodeValid && node!.IsInsideTree() ? "是" : "否")}, "
                    + $"可见={(nodeValid ? node!.Visible.ToString() : "-")}, "
                    + $"缩放={(nodeValid ? node!.Scale.X.ToString("0.###") : "-")}]");
            }

            string summary =
                $"[瓦库的爹] 合体自检: 接收方={receiver.NetId}, 手牌堆={pileCount} 张, "
                + $"手牌UI holder={holderCount} 个, 本次搬运={moved.Count} 张（缺 holder={missing} 张）";
            if (missing > 0)
            {
                LocalMultiControlLogger.Warn(
                    summary + " ⇒ **疑似空气牌**（占位但没手牌节点）: " + string.Join(" ", details));
            }
            else
            {
                LocalMultiControlLogger.Info(summary + " ⇒ 节点齐全（若画面上仍空白，则是卡面渲染那一层）: "
                    + string.Join(" ", details));
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[瓦库的爹] 合体自检异常（已忽略）: {exception.Message}");
        }
    }
}
