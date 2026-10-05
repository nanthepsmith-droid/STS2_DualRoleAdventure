using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LocalMultiControl.Scripts.Patch;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 净化 —— 休息处的自定义选项：删除一个瓦库玩家卡组里的牌（每次最多 <see cref="MaxCards"/> 张）。
///
/// 流程：真人点选本选项 → <see cref="LocalWakuuPlayerPicker"/> 选目标瓦库 →
/// 原版删牌界面（<c>CardSelectCmd.FromDeckForRemoval</c>）弹在真人屏幕上 →
/// 逐张 <c>CardPileCmd.RemoveFromDeck</c>。取消 / 一张都没选 ⇒ 返回 false（选项不被消费，可重选）。
///
/// 蓝本：游戏 <c>CookRestSiteOption</c>（营火烹饪：删 2 张换血上限）——本类只把"删几张"改成
/// "最多 5 张、可取消、且删的是**别人的**卡组"。
///
/// ⚠ 时序坑（核验报告 §3.2 ⚠）：<c>FromDeckGeneric</c> 不在选牌归属者前缀的挂钩列表里
/// （它是 `legacyFallback`，见 `WakuuSelectorRouteAudit`），因此并发跑着的瓦库火堆选项若正好压了
/// 托管选择器，真人这次净化选牌**会被瓦库替答**。本类的对策是在调用前后用
/// <see cref="CardSelectForegroundSwitchPatch.PushChoiceOwner"/> 临时把归属者钉成真人 ——
/// 这样选择器守卫会把托管选择器摘掉、改走正常 UI；无选择器时该设置无副作用。
/// </summary>
internal sealed class PurifyWakuuRestSiteOption : RestSiteOption
{
    /// <summary>选项 id：转成 rest_site_ui 的键 <c>OPTION_LMC_PURIFY.name/.description</c>。</summary>
    internal const string PurifyOptionId = "LMC_PURIFY";

    /// <summary>单次最多删几张（与提案「每次最多 5 张」一致）。</summary>
    internal const int MaxCards = 5;

    public PurifyWakuuRestSiteOption(Player owner)
        : base(owner)
    {
    }

    public override string OptionId => PurifyOptionId;

    public override LocString Description
    {
        get
        {
            LocString description = new("rest_site_ui", $"OPTION_{PurifyOptionId}.description");
            description.Add("Amount", MaxCards);
            return description;
        }
    }

    /// <summary>有可净化的瓦库才可用（无目标时按钮置灰，不弹任何界面）。</summary>
    public override bool IsEnabled => PurifyWakuuRestSiteRuntime.HasAnyCandidate(Owner);

    public override async Task<bool> OnSelect()
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return false;
        }

        List<Player> candidates = PurifyWakuuRestSiteRuntime.CollectCandidates(Owner);
        if (candidates.Count == 0)
        {
            LocalMultiControlLogger.Info($"净化中止：没有可净化的瓦库。owner={Owner.NetId}");
            return false;
        }

        List<LocalPlayerPickerEntry> entries = candidates
            .Select(candidate => new LocalPlayerPickerEntry(
                candidate.NetId,
                PurifyWakuuRestSiteRuntime.DescribeCandidate(candidate)))
            .ToList();

        ulong? targetId = await LocalWakuuPlayerPicker.PickAsync(LocalModText.PurifyPickTitle, entries);
        if (!targetId.HasValue)
        {
            LocalMultiControlLogger.Info($"净化取消：未选择目标瓦库。owner={Owner.NetId}");
            return false;
        }

        Player? target = candidates.FirstOrDefault(candidate => candidate.NetId == targetId.Value);
        if (target == null)
        {
            LocalMultiControlLogger.Warn($"净化中止：选择的目标已不可用。owner={Owner.NetId}, target={targetId.Value}");
            return false;
        }

        List<CardModel> removed = await RemoveCardsWithOriginalUiAsync(target);
        if (removed.Count == 0)
        {
            LocalMultiControlLogger.Info($"净化取消：未选择任何卡牌。owner={Owner.NetId}, target={target.NetId}");
            return false;
        }

        LocalMultiControlLogger.Info(
            $"净化完成: owner={Owner.NetId}, target={target.NetId}, count={removed.Count}, "
            + $"cards=[{string.Join(", ", removed.Select(card => card.Id.Entry))}], "
            + $"剩余可删牌={PurifyWakuuRestSiteRuntime.CountRemovableCards(target)}");
        return true;
    }

    /// <summary>
    /// 弹原版牌库删牌界面并执行删除。返回真正被删掉的牌（空 = 取消 / 无可删）。
    /// </summary>
    private async Task<List<CardModel>> RemoveCardsWithOriginalUiAsync(Player target)
    {
        // 0~MaxCards 的语义：确认键 0 张可点、关闭返回空 ⇒ 调用方返回 false，选项不被消费（可重进）。
        // MaxSelect(5) != MinSelect(0) ⇒ 构造里自动置 RequireManualConfirmation = true，恒走真人 UI。
        CardSelectorPrefs prefs = new(CardSelectorPrefs.RemoveSelectionPrompt, 0, MaxCards)
        {
            Cancelable = true,
        };

        List<CardModel> selected;
        // 归属者临时钉成真人（本次净化由真人手选）——防并发瓦库托管选择器抢答，见类注释。
        using (CardSelectForegroundSwitchPatch.PushChoiceOwner(Owner.NetId))
        {
            IEnumerable<CardModel> picked = await CardSelectCmd.FromDeckForRemoval(target, prefs);
            selected = picked?.ToList() ?? new List<CardModel>();
        }

        if (selected.Count == 0)
        {
            return new List<CardModel>();
        }

        await CardPileCmd.RemoveFromDeck(selected);
        return selected;
    }
}
