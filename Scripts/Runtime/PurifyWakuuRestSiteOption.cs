using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using LocalMultiControl.Scripts.Patch;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Helpers;
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

    /// <summary>本选项的图标路径（与基类硬拼规则一致：<c>ui/rest_site/option_&lt;id 小写&gt;.png</c>）。</summary>
    internal static string IconPath =>
        ImageHelper.GetImagePath($"ui/rest_site/option_{PurifyOptionId.ToLowerInvariant()}.png");

    /// <summary>可借用的原版休息区图标（按语义优先：烹饪 = 删牌换血上限，最贴近"净化"；退而求其次用休息）。</summary>
    private static readonly string[] BorrowedIconInnerPaths =
    {
        "ui/rest_site/option_cook.png",
        "ui/rest_site/option_heal.png",
    };

    private static bool _iconRegistered;

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

    /// <summary>
    /// 返回**空**：本选项的图标是 dll 资源盲区（基类按 OptionId 硬拼 png 路径，我们无法往游戏 pck 里补图）。
    /// 若把那个不存在的路径交给房间预加载，<c>AssetCache</c> 会把它记进 `_failedAssets`，
    /// 之后 `RestSiteOption.Icon` 会**抛 `AssetLoadException`** —— 而 <c>NRestSiteButton.Reload</c> 里
    /// 「设图标」在「设按钮名字」**之前**（游戏源码 `NRestSiteButton.cs:115-116`）⇒ 名字永远不会被赋值，
    /// 按钮显示场景占位文字（r208 实机就是这么变成 "Dig" 的）。
    /// 真正的图标由 <see cref="EnsureIconRegistered"/> 借用原版图标注册到 <see cref="IconPath"/>。
    /// </summary>
    public override IEnumerable<string> AssetPaths => Array.Empty<string>();

    /// <summary>有可净化的瓦库才可用（无目标时按钮置灰，不弹任何界面）。</summary>
    public override bool IsEnabled => PurifyWakuuRestSiteRuntime.HasAnyCandidate(Owner);

    /// <summary>
    /// 把原版【烹饪】图标（语义最接近"删牌"）注册到本选项的图标路径下，让
    /// <c>PreloadManager.Cache.GetTexture2D(<see cref="IconPath"/>)</c> 命中缓存而**不去真的加载文件**。
    /// 幂等；失败只记 WARN（退化为空图标 —— 那时 `Icon` 返回 null 只是不显示图，**不会抛异常**）。
    /// 由 <see cref="PurifyWakuuRestSiteRuntime.TryInjectPurifyOption"/> 在注入前调用（早于房间预加载）。
    /// </summary>
    internal static void EnsureIconRegistered()
    {
        if (_iconRegistered)
        {
            return;
        }

        try
        {
            if (PreloadManager.Cache.ContainsKey(IconPath))
            {
                _iconRegistered = true;
                return;
            }

            foreach (string innerPath in BorrowedIconInnerPaths)
            {
                Texture2D? texture = PreloadManager.Cache.GetTexture2D(ImageHelper.GetImagePath(innerPath));
                if (texture == null)
                {
                    continue;
                }

                PreloadManager.Cache.SetAsset(IconPath, texture);
                _iconRegistered = true;
                LocalMultiControlLogger.Info($"净化选项图标已复用原版图标: {innerPath} -> {IconPath}");
                return;
            }

            LocalMultiControlLogger.Warn($"净化选项图标注册失败：原版备用图标都取不到，本项将不显示图标。path={IconPath}");
            _iconRegistered = true;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"注册净化选项图标异常: {exception.Message}");
        }
    }

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
        // 两道防护（缺一不可，r208 实机各踩了一次）：
        //   ① 归属者临时钉成真人：防**并发**跑着的瓦库托管选择器（栈顶）抢答；
        //   ② 关掉「瓦库作用域外自动作答」：否则 CardSelectWakuuTurnStartAutoAnswerPatch 会
        //      对 FromDeckGeneric 按 cardPickMode 直接把牌选了 —— 真人根本看不到选牌界面
        //      （r208 实机：`瓦库作用域外牌组选牌自动作答 … source=FromDeckGeneric`，一次删掉 5 张）。
        using (CardSelectForegroundSwitchPatch.PushChoiceOwner(Owner.NetId))
        using (CardSelectWakuuTurnStartAutoAnswerPatch.SuppressForHumanChoice())
        {
            LocalMultiControlLogger.Info(
                $"净化选牌交给真人（已压制瓦库自动作答与归属者抢答）: target={target.NetId}, "
                + $"可删牌={PurifyWakuuRestSiteRuntime.CountRemovableCards(target)}");
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
