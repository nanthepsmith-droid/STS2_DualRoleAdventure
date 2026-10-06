// r221：本文件显式开启可空引用检查 —— r220 的 NRE 就是"漏删 `return null` + 上层读 `.Count`"，
// 而项目级 `Nullable` 未开、编译器不会提醒。开这一行后这类"声明非空却返回 null"会被编译器点名
// （本仓库构建门禁要求 0 警告 ⇒ 等于给这类回归上了道闸）。新写的游戏交互文件建议照此办理。
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LocalMultiControl.Scripts.Patch;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 炼化 —— 休息处的自定义选项：对一个瓦库执行「收编 + 炼掉」。
///
/// 流程（真人点选本选项后）：
/// <list type="number">
/// <item><see cref="LocalWakuuPlayerPicker"/> 选目标瓦库（取消 ⇒ 选项不被消费）；</item>
/// <item>原版卡组选牌界面（<c>CardSelectCmd.FromDeckGeneric</c>）选走若干张牌
///   （张数上限按配置档位；**至少 1 张**，取消 ⇒ 中止；卡组为空则跳过该步）；</item>
/// <item><see cref="LocalWakuuRelicPicker"/> 选走 1 件遗物
///   （排除【永久低语耳环】/【瓦库形态】；目标没有可收编遗物则跳过该步；取消 ⇒ 中止）；</item>
/// <item>按配置比例收编目标的血量与血上限（<c>CreatureCmd.GainMaxHp</c>，同时加血上限并回血）；</item>
/// <item>把目标**炼掉**：<c>SetMaxHpInternal(0)</c> + <c>Kill(force: true)</c> —— 见下。</item>
/// </list>
///
/// ⚠ 炼掉的实现（核验报告 §2.1）：<c>MaxHp=0</c> 会让 <c>IsAlive=false</c> 且封死一切治疗
/// （<c>SetCurrentHpInternal</c> 被 <c>Min(amount, MaxHp)</c> 钳死，战斗结束的自动复活也被钳住）。
/// <c>CreatureCmd.SetMaxHp(0)</c> 内部会再调一次**不带 force** 的 <c>Kill</c>，而非战斗环境下
/// <c>KillWithoutCheckingWinCondition</c> 对「玩家 + 多人 + !force」会打一条 <c>Log.Error</c>
/// 并原地 Heal 1 再返回（会抬高全局 ERROR 计数、且死亡处理没跑完）—— 所以这里**先**用
/// <c>SetMaxHpInternal(0)</c> 清血上限（它不含 Kill），**再**显式 <c>Kill(force: true)</c>，
/// 让死亡只处理一次、也不触发那条保护错误。
///
/// 席位归宿（2026-10-05 用户拍板）：**不摘名单、不缩队、不做存档标记** —— 死者滞留占位，
/// 日后若血上限被加回来还能复活（「炼化了≠死透了」）。
/// </summary>
internal sealed class RefineWakuuRestSiteOption : RestSiteOption
{
    /// <summary>选项 id：转成 rest_site_ui 的键 <c>OPTION_LMC_REFINE.name/.description</c>。</summary>
    internal const string RefineOptionId = "LMC_REFINE";

    /// <summary>本选项的图标路径（与基类硬拼规则一致：<c>ui/rest_site/option_&lt;id 小写&gt;.png</c>）。</summary>
    internal static string IconPath => LocalRestSiteOptionIcon.IconPathFor(RefineOptionId);

    /// <summary>可借用的原版休息区图标（按语义优先：点燃/熔炼最贴近"炼化"；退而求其次举重、休息）。</summary>
    private static readonly string[] BorrowedIconInnerPaths =
    {
        "ui/rest_site/option_kindle.png",
        "ui/rest_site/option_lift.png",
        "ui/rest_site/option_heal.png",
    };

    public RefineWakuuRestSiteOption(Player owner)
        : base(owner)
    {
    }

    public override string OptionId => RefineOptionId;

    public override LocString Description
    {
        get
        {
            LocString description = new("rest_site_ui", $"OPTION_{RefineOptionId}.description");
            description.Add("Cards", LocalModText.RefineCardLimitLabel(LocalWakuuAutopilotConfig.RefineCardLimit));
            description.Add("Relics", LocalModText.RefineRelicLimitLabel(LocalWakuuAutopilotConfig.RefineRelicLimit));
            description.Add("Ratio", LocalModText.RefineHpRatioLabel(LocalWakuuAutopilotConfig.RefineHpRatio));
            return description;
        }
    }

    /// <summary>
    /// ⚠ **本类刻意不覆写 <c>AssetPaths</c>**（保持基类行为 = 返回 <see cref="IconPath"/>）——
    /// 理由见 <see cref="LocalRestSiteOptionIcon"/>（摘掉路径 = 注册好的图标被差集卸载）。
    /// </summary>

    /// <summary>
    /// 有可炼化的瓦库才可用（无目标时按钮置灰，不弹任何界面）。
    /// 顺带在**每次求值时补一次图标注册**（游戏建按钮时先读 <c>IsEnabled</c>、之后才取 <c>Icon</c>）。
    /// </summary>
    public override bool IsEnabled
    {
        get
        {
            EnsureIconRegistered();
            return RefineWakuuRestSiteRuntime.HasAnyCandidate(Owner);
        }
    }

    /// <summary>
    /// 让本选项的图标可用（实现与坑见 <see cref="LocalRestSiteOptionIcon"/>）。
    /// 由 <see cref="RefineWakuuRestSiteRuntime.TryInjectRefineOption"/> 在注入前调用。
    /// </summary>
    internal static void EnsureIconRegistered()
    {
        LocalRestSiteOptionIcon.EnsureRegistered(RefineOptionId, BorrowedIconInnerPaths);
    }

    /// <summary>
    /// 入口即兜异常（r221 教训）：r220 有一处 NRE 冒穿到 <c>RestSiteSynchronizer.ChooseOption</c>，
    /// 实机表现为"点了炼化、选完就没反应了"，而日志里只有一条不带上下文的 `NullReferenceException`
    /// （调用栈只有本方法名），极难定性 ⇒ 这里统一接住、把异常全文（含栈）打出来并返回 false（选项不被消费）。
    /// </summary>
    public override async Task<bool> OnSelect()
    {
        try
        {
            return await RunRefineAsync();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"炼化执行异常（已中止，选项未消费）: {exception}");
            return false;
        }
    }

    private async Task<bool> RunRefineAsync()
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return false;
        }

        List<Player> candidates = RefineWakuuRestSiteRuntime.CollectCandidates(Owner);
        if (candidates.Count == 0)
        {
            LocalMultiControlLogger.Info($"炼化中止：没有可炼化的瓦库。owner={Owner.NetId}");
            return false;
        }

        List<LocalPlayerPickerEntry> entries = candidates
            .Select(candidate => new LocalPlayerPickerEntry(
                candidate.NetId,
                RefineWakuuRestSiteRuntime.DescribeCandidate(candidate)))
            .ToList();

        ulong? targetId = await LocalWakuuPlayerPicker.PickAsync(LocalModText.RefinePickTitle, entries);
        if (!targetId.HasValue)
        {
            LocalMultiControlLogger.Info($"炼化取消：未选择目标瓦库。owner={Owner.NetId}");
            return false;
        }

        Player? target = candidates.FirstOrDefault(candidate => candidate.NetId == targetId.Value);
        if (target?.Creature == null)
        {
            LocalMultiControlLogger.Warn($"炼化中止：选择的目标已不可用。owner={Owner.NetId}, target={targetId.Value}");
            return false;
        }

        LocalMultiControlLogger.Info(
            $"炼化已选定目标: owner={Owner.NetId}, target={target.NetId}, "
            + $"血={target.Creature.CurrentHp:0}/{target.Creature.MaxHp:0}, "
            + $"卡组={RefineWakuuRestSiteRuntime.CountDeckCards(target)}, "
            + $"遗物={target.Relics.Count}(可收编={RefineWakuuRestSiteRuntime.CollectTransferableRelics(target).Count})");

        // ① 选要收编的牌 —— **这一步只选不动**：后续任一步取消时目标完全不受影响（好取消语义）。
        //   `refineTakeVakuuAssets` 关掉则整步跳过（只收血量并炼掉，牌留给瓦库）。
        bool takeAssets = WakuuRefinePolicy.ShouldTakeAssets(LocalWakuuAutopilotConfig.RefineTakeVakuuAssets);
        List<CardModel> selectedCards;
        if (takeAssets)
        {
            // 空表 = 不拿牌（r220 起这一步**没有**"取消 = 中止"语义，见 SelectCardsAsync 注释）。
            selectedCards = await SelectCardsAsync(target);
            if (selectedCards.Count == 0)
            {
                LocalMultiControlLogger.Info($"炼化未拿走任何牌（可一张不选，继续炼化）。owner={Owner.NetId}");
            }
        }
        else
        {
            selectedCards = new List<CardModel>();
            LocalMultiControlLogger.Info($"炼化跳过收编牌与遗物：开关已关（牌与遗物留在瓦库身上）。target={target.NetId}");
        }

        // ② 选要收编的遗物（**多选**，件数档位见 `refineRelicLimit`：1 / 3 / 5 / 不限，默认不限
        //   —— 用户：只让拿 1 件则炼化收益太低，"完全不如让瓦库活着"）。
        //   开关关 / 无可收编遗物则跳过那一步。
        List<RelicModel> relics = takeAssets
            ? RefineWakuuRestSiteRuntime.CollectTransferableRelics(target)
            : new List<RelicModel>();
        int relicLimit = WakuuRefinePolicy.ResolveRelicLimit(
            LocalWakuuAutopilotConfig.RefineRelicLimit,
            relics.Count);
        List<RelicModel> takenRelics = new();
        if (relicLimit > 0)
        {
            IReadOnlyList<RelicModel>? picked = await LocalWakuuRelicPicker.PickManyAsync(
                LocalModText.RefineRelicPickTitle, relics, relicLimit);
            if (picked == null)
            {
                LocalMultiControlLogger.Info($"炼化取消：未选择遗物（目标未受影响）。owner={Owner.NetId}");
                return false;
            }

            takenRelics = picked.ToList();
            if (takenRelics.Count == 0)
            {
                LocalMultiControlLogger.Info($"炼化未拿走任何遗物（已确认「不拿」，继续炼化）。owner={Owner.NetId}");
            }
        }
        else if (takeAssets)
        {
            LocalMultiControlLogger.Info($"炼化跳过选遗物：目标没有可收编的遗物。target={target.NetId}");
        }

        // ③ 选要收编的药水（**多选：想拿几瓶拿几瓶**，上限 = 自己药水栏空位；
        //   开关关 / 瓦库没药水 / 真人没空位则整步跳过，且**不动瓦库药水**）。
        List<PotionModel> vakuuPotions = target.Potions.ToList();
        int freePotionSlots = Owner.MaxPotionCount - Owner.Potions.Count();
        int potionLimit = WakuuRefinePolicy.ResolvePotionTakeLimit(vakuuPotions.Count, freePotionSlots);
        List<PotionModel> takenPotions = new();
        bool potionStepRan = false;
        if (WakuuRefinePolicy.ShouldOfferPotionStep(
                LocalWakuuAutopilotConfig.RefineTakeVakuuPotions,
                vakuuPotions.Count,
                freePotionSlots))
        {
            IReadOnlyList<PotionModel>? picked = await LocalWakuuPotionPicker.PickManyAsync(
                LocalModText.RefinePotionPickTitle, vakuuPotions, potionLimit);
            if (picked == null)
            {
                LocalMultiControlLogger.Info($"炼化取消：未选择药水（目标未受影响）。owner={Owner.NetId}");
                return false;
            }

            takenPotions = picked.ToList();
            potionStepRan = true;
        }
        else
        {
            LocalMultiControlLogger.Info(
                $"炼化跳过药水收编: 开关={LocalWakuuAutopilotConfig.RefineTakeVakuuPotions}, "
                + $"瓦库药水={vakuuPotions.Count}, 真人空位={freePotionSlots}, target={target.NetId}");
        }

        // ④ 开始落地 —— 到这里已没有取消口，下面几步全部生效。
        //   收编血量与血上限（基数必须在炼掉之前读）。
        decimal hpTransfer = WakuuRefinePolicy.ResolveHpTransfer(
            target.Creature.MaxHp,
            LocalWakuuAutopilotConfig.RefineHpRatio);
        if (hpTransfer > 0m && Owner.Creature != null)
        {
            await CreatureCmd.GainMaxHp(Owner.Creature, hpTransfer);
        }

        int movedCount = await MigrateCardsAsync(selectedCards);

        // 转移遗物（逐件"先摘后授 + **校验移除已生效**"：原版 RelicCmd 不允许直接把带 owner 的实例换人，
        // 故授一份新的可变实例；见 TransferRelicAsync 的防无限复制说明）。
        List<RelicModel> movedRelics = new();
        foreach (RelicModel candidate in takenRelics)
        {
            if (await TransferRelicAsync(candidate, target))
            {
                movedRelics.Add(candidate);
            }
        }

        // 收编药水：**先移除、再授**（与遗物同一条"先确认拿走、再给新的"纪律，防复制），
        // 且**只有真的拿走了至少 1 瓶**才移除瓦库药水 —— 跳过 / 一瓶不拿都**不动**它的药水
        // （r217 首测的缺陷就是"跳过也照删"）。
        int potionsTaken = 0;
        int potionsRemoved = 0;
        if (takenPotions.Count > 0)
        {
            List<PotionModel> removedOk = await DiscardVakuuPotionsAsync(target, vakuuPotions);
            potionsRemoved = removedOk.Count;

            // 只授"确实已从瓦库身上移除"的那些（移除没生效的绝不授副本 ⇒ 不会白送一瓶）。
            List<PotionModel> grantable = takenPotions.Where(removedOk.Contains).ToList();
            potionsTaken = await TakePotionsAsync(grantable);
        }
        else if (potionStepRan && vakuuPotions.Count > 0)
        {
            // 只有"弹层里确认了 0 瓶"才走这里；"整步跳过"（满栏 / 开关关 / 没药水）上面已单独记过日志。
            LocalMultiControlLogger.Info(
                $"炼化未拿走任何药水（已确认「不拿」）：瓦库 {vakuuPotions.Count} 瓶药水保持不动。target={target.NetId}");
        }

        // ⑤ 炼掉目标：清血上限（封死复活）+ 显式 force 击杀（见类注释）。
        target.Creature.SetMaxHpInternal(0m);
        await CreatureCmd.Kill(target.Creature, force: true);

        LocalMultiControlLogger.Info(
            $"炼化完成: owner={Owner.NetId}, target={target.NetId}, "
            + $"收编血量={hpTransfer:0}, 收编牌数={movedCount}, "
            + $"收编遗物=[{string.Join(", ", movedRelics.Select(item => item.Id.Entry))}]"
            + $"(拿走 {movedRelics.Count} 件), 目标遗物数={target.Relics.Count}(瓦库已无这些遗物), "
            + $"收编药水=[{string.Join(", ", takenPotions.Select(item => item.Id.Entry))}]"
            + $"(拿走 {potionsTaken} 瓶), 移除瓦库药水={potionsRemoved}, "
            + $"收编后真人血={Owner.Creature?.CurrentHp:0}/{Owner.Creature?.MaxHp:0}, "
            + $"目标已死={target.Creature.IsDead}, 目标血上限={target.Creature.MaxHp}");
        return true;
    }

    /// <summary>
    /// 把选中的药水逐瓶交给真人：`PotionModel.Owner` 与遗物同款（"已有 owner 且换人"会抛）
    /// ⇒ 走 <c>ModelDb.GetById&lt;PotionModel&gt;(id).ToMutable()</c> 授一份**新实例**
    /// （与 <c>PotionCmd.TryToProcure&lt;T&gt;</c> 同款）+ 原版 <c>PotionCmd.TryToProcure</c> 入栏。
    /// 返回**成功进栏**的瓶数（`success=false` = 满栏 / Hook 拒绝，只打 WARN、不打断炼化）。
    /// </summary>
    private async Task<int> TakePotionsAsync(IReadOnlyList<PotionModel> chosen)
    {
        int taken = 0;
        foreach (PotionModel potion in chosen)
        {
            try
            {
                PotionModel granted = ModelDb.GetById<PotionModel>(potion.Id).ToMutable();
                PotionProcureResult result = await PotionCmd.TryToProcure(granted, Owner);
                if (result.success)
                {
                    taken++;
                }
                else
                {
                    LocalMultiControlLogger.Warn(
                        $"炼化收编药水未进栏（药水栏已满或 Hook 拒绝）: potion={potion.Id.Entry}, owner={Owner.NetId}");
                }
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"炼化收编药水失败: potion={potion.Id.Entry}, error={exception.Message}");
            }
        }

        return taken;
    }

    /// <summary>
    /// 把瓦库**全部**药水移除（含没被拿走的那些），**并逐瓶校验移除是否真的生效**
    /// （`PotionCmd.Discard` 失败 / 被第三方拦下时不授副本 ⇒ 不会出现"白送一瓶"）。
    /// 返回**确实已从瓦库身上移除**的药水列表。
    /// </summary>
    private static async Task<List<PotionModel>> DiscardVakuuPotionsAsync(
        Player target,
        IReadOnlyList<PotionModel> vakuuPotions)
    {
        List<PotionModel> removed = new();
        foreach (PotionModel potion in vakuuPotions)
        {
            try
            {
                await PotionCmd.Discard(potion);
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"炼化移除瓦库药水失败: potion={potion.Id.Entry}, error={exception.Message}");
                continue;
            }

            if (target.Potions.Contains(potion))
            {
                LocalMultiControlLogger.Warn(
                    $"炼化放弃该瓶药水的收编：移除未生效（拒绝授副本）。potion={potion.Id.Entry}, target={target.NetId}");
                continue;
            }

            removed.Add(potion);
        }

        return removed;
    }

    /// <summary>
    /// 弹原版卡组选牌界面，让**真人**从目标卡组**选出**若干张牌（**只选不动**，返回选中列表）。
    /// **可以一张都不选**（r220：不影响小卡组玩法）⇒ 空表 = "不拿牌、继续炼化"；
    /// 该步**没有**"取消 = 中止"语义（详见 <see cref="WakuuRefinePolicy.MinCardsToPick"/>）。
    /// </summary>
    private async Task<List<CardModel>> SelectCardsAsync(Player target)
    {
        int limit = RefineWakuuRestSiteRuntime.ResolveCardLimit(target);
        if (limit <= 0)
        {
            // 卡组为空：这一步自动跳过（仍可收编血量与遗物）。
            LocalMultiControlLogger.Info($"炼化跳过选牌：目标卡组为空。target={target.NetId}");
            return new List<CardModel>();
        }

        // Min=0 ⇒ 可"一张都不选"（用户：不能一张都不要会妨碍玩小卡组）；代价是空返回无法再区分
        // "取消"与"确认 0 张"，故两者一律按"不拿牌"处理。
        // 显式 RequireManualConfirmation=true：Min=Max 时构造里是 false，会走"直选"路径而不弹界面。
        CardSelectorPrefs prefs = new(
            LocalWakuuRefineLocalization.ResolveCardPrompt(CardSelectorPrefs.UpgradeSelectionPrompt),
            WakuuRefinePolicy.MinCardsToPick,
            limit)
        {
            Cancelable = true,
            RequireManualConfirmation = true,
        };

        List<CardModel> selected;
        // 两道防护（缺一不可，净化 r208 实机各踩了一次）：
        //   ① 归属者临时钉成真人：防**并发**跑着的瓦库托管选择器（栈顶）抢答；
        //   ② 关掉「瓦库作用域外自动作答」：否则 CardSelectWakuuTurnStartAutoAnswerPatch 会对
        //      deck 选牌按 cardPickMode 直接把牌选了 —— 真人根本看不到选牌界面。
        using (CardSelectForegroundSwitchPatch.PushChoiceOwner(Owner.NetId))
        using (CardSelectWakuuTurnStartAutoAnswerPatch.SuppressForHumanChoice())
        {
            LocalMultiControlLogger.Info(
                $"炼化选牌交给真人（已压制瓦库自动作答与归属者抢答）: target={target.NetId}, "
                + $"可选牌={RefineWakuuRestSiteRuntime.CountDeckCards(target)}, 上限={limit}");
            IEnumerable<CardModel> picked = await CardSelectCmd.FromDeckGeneric(target, prefs);
            selected = picked?.ToList() ?? new List<CardModel>();
        }

        // ⚠ 空表**就是**"不拿牌"，直接返回（r220 曾漏删一段 `return null` ⇒ 0 张时上层读 .Count 抛 NRE，
        // 表现为"点炼化、选完 0 张后什么都没发生"，见 r221 修复说明）。
        return selected;
    }

    /// <summary>把选中的牌逐张搬进真人卡组；返回成功搬走的张数。</summary>
    private async Task<int> MigrateCardsAsync(List<CardModel> cards)
    {
        int moved = 0;
        foreach (CardModel card in cards)
        {
            try
            {
                // 跨玩家转移：走原版 GiveToAnotherPlayer（内部 = 摘出当前牌堆 → 改 owner →
                // 以 isChangingOwners 加进新主人的 Deck）。牌随主人走，不复制。
                await CardPileCmd.GiveToAnotherPlayer(card, Owner, PileType.Deck);
                moved++;
            }
            catch (Exception exception)
            {
                // 单张失败不打断整次炼化（后面还要清血/清上限），打 WARN 便于核对。
                LocalMultiControlLogger.Warn($"炼化搬牌失败: card={card.Id.Entry}, error={exception.Message}");
            }
        }

        return moved;
    }

    /// <summary>
    /// 把遗物从目标**拿给**真人（真·转移，**不是复制**）。原版 <c>RelicCmd</c> 只接受"新的可变实例"
    /// （<c>RelicModel.Owner</c> setter 在"已有 owner 且换人"时抛 <c>InvalidOperationException</c>），
    /// 所以姿势 = 先 <c>Remove</c> 摘掉目标身上那件、再授一份 <c>CanonicalInstance.ToMutable()</c>。
    ///
    /// ⚠ **防"无限强力遗物"的关键一步**：**必须确认移除真的生效了，才授副本**。
    /// 万一 <c>Remove</c> 被第三方前缀拦下（`Player.RemoveRelicInternal` 上有别人的补丁），
    /// 继续授就等于**白送一件** ⇒ 反复炼化同一件强力遗物即可无限复制。
    /// 所以这里先校验目标身上已无该遗物；没生效就**放弃本次收编**并打 WARN（用户口径：
    /// 「要防止的是无限强力遗物；既然已经是拿走而不是复制就没问题」）。
    /// </summary>
    private async Task<bool> TransferRelicAsync(RelicModel relic, Player target)
    {
        RelicModel? canonical = relic.CanonicalInstance;
        if (canonical == null)
        {
            LocalMultiControlLogger.Warn($"炼化中止遗物收编：取不到规范模型。relic={relic.Id.Entry}");
            return false;
        }

        int targetRelicsBefore = target.Relics.Count;
        await RelicCmd.Remove(relic);

        if (target.Relics.Contains(relic))
        {
            LocalMultiControlLogger.Warn(
                $"炼化放弃遗物收编：移除未生效（拒绝授副本，防无限复制）。relic={relic.Id.Entry}, "
                + $"target={target.NetId}, 目标遗物数={targetRelicsBefore}");
            return false;
        }

        RelicModel granted = canonical.ToMutable();
        await RelicCmd.Obtain(granted, Owner);

        LocalMultiControlLogger.Info(
            $"炼化遗物已转移（瓦库身上已无该遗物）: relic={relic.Id.Entry}, target={target.NetId}, "
            + $"目标遗物数={targetRelicsBefore}→{target.Relics.Count}, 真人遗物数={Owner.Relics.Count}");
        return true;
    }
}
