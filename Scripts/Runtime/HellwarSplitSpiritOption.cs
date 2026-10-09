#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LocalMultiControl.Scripts.Models.Relics;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Localization;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 战灵召唤（原 ④「地狱战神」，#21）休息处的自定义选项【**分裂精神**】（R2 / R2b / D13 / D5）：
/// **消耗当前血量的一定百分比**，为持有者自己的【地狱耳环】**增加 1 个计数**。
///
/// 与炼化 / 净化两个老选项的差别：
/// <list type="bullet">
/// <item>**不选目标** —— 它作用于 <see cref="RestSiteOption.Owner"/> 自己；</item>
/// <item>**先扣血、再加计数**（用户口径）—— 扣血失败/不合法时整次不消费（返回 false）；</item>
/// <item>扣血基数 = **当前血量**、向下取整、**clamp ≥1（不可扣死）**（D13 已拍板）；</item>
/// <item>用到**原版钩子**实现 D5 的"免费额外选项"档：成功路径给耳环置一次
///   <see cref="LocalWakuuHellwarEarring.PendingFreeRestAction"/>，
///   由 <see cref="LocalWakuuHellwarEarring.ShouldDisableRemainingRestSiteOptions"/> 消费
///   （原版 <c>MiniatureTent</c> 同款手法）⇒ 用完仍能休息 / 锻造。</item>
/// </list>
///
/// ⚠ 扣血**不用** <c>CreatureCmd.SetMaxHp(0)</c>（非战斗环境会打 <c>Log.Error</c>，同炼化教训），
/// 也不用 <c>CreatureCmd.Damage</c>（要战斗上下文 + <c>PlayerChoiceContext</c>）；
/// 这里用 <c>CreatureCmd.SetCurrentHp</c>（只改血量、恒 ≥1 故不会触发 <c>Kill</c>）。
/// </summary>
internal sealed class HellwarSplitSpiritOption : RestSiteOption
{
    /// <summary>选项 id：转成 rest_site_ui 的键 <c>OPTION_LMC_SPLIT_SPIRIT.name/.description</c>。</summary>
    internal const string SplitSpiritOptionId = "LMC_SPLIT_SPIRIT";

    /// <summary>本选项的图标路径（与基类硬拼规则一致：<c>ui/rest_site/option_&lt;id 小写&gt;.png</c>）。</summary>
    internal static string IconPath => LocalRestSiteOptionIcon.IconPathFor(SplitSpiritOptionId);

    /// <summary>可借用的原版休息区图标（按语义优先：点燃最贴近"燃烧精神换力量"；退而求其次举重、休息）。</summary>
    private static readonly string[] BorrowedIconInnerPaths =
    {
        "ui/rest_site/option_kindle.png",
        "ui/rest_site/option_lift.png",
        "ui/rest_site/option_heal.png",
    };

    public HellwarSplitSpiritOption(Player owner)
        : base(owner)
    {
    }

    public override string OptionId => SplitSpiritOptionId;

    public override LocString Description
    {
        get
        {
            LocString description = new("rest_site_ui", $"OPTION_{SplitSpiritOptionId}.description");
            decimal currentHp = Owner.Creature?.CurrentHp ?? 0m;
            description.Add("Hp", HellwarSummonPolicy.ResolveSplitHpLoss(
                currentHp, LocalWakuuAutopilotConfig.HellwarSplitHpRatio).ToString("0"));
            description.Add("Ratio", LocalModText.HellwarSplitHpRatioLabel(
                LocalWakuuAutopilotConfig.HellwarSplitHpRatio));
            return description;
        }
    }

    /// <summary>
    /// ⚠ **本类刻意不覆写 <c>AssetPaths</c>**（保持基类行为 = 返回 <see cref="IconPath"/>）——
    /// 理由见 <see cref="LocalRestSiteOptionIcon"/>（摘掉路径 = 注册好的图标被差集卸载）。
    /// </summary>

    /// <summary>
    /// 持有【地狱耳环】才可用（没有耳环时按钮置灰，不弹任何界面）。
    /// 顺带在**每次求值时补一次图标注册**（游戏建按钮时先读 <c>IsEnabled</c>、之后才取 <c>Icon</c>）。
    /// </summary>
    public override bool IsEnabled
    {
        get
        {
            EnsureIconRegistered();
            return HellwarSummonRuntime.FindEarring(Owner) != null;
        }
    }

    /// <summary>
    /// 让本选项的图标可用（实现与坑见 <see cref="LocalRestSiteOptionIcon"/>）。
    /// 由 <see cref="HellwarSummonRuntime.TryInjectSplitSpiritOption"/> 在注入前调用。
    /// </summary>
    internal static void EnsureIconRegistered()
    {
        LocalRestSiteOptionIcon.EnsureRegistered(SplitSpiritOptionId, BorrowedIconInnerPaths);
    }

    /// <summary>入口即兜异常（与炼化同款 r221 教训）：返回 false = 选项不被消费。</summary>
    public override async Task<bool> OnSelect()
    {
        try
        {
            return await RunSplitAsync();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[地狱战神] 【分裂精神】执行异常（已中止，选项未消费）: {exception}");
            return false;
        }
    }

    private async Task<bool> RunSplitAsync()
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return false;
        }

        if (!LocalWakuuAutopilotConfig.HellwarGod)
        {
            return false;
        }

        LocalWakuuHellwarEarring? earring = HellwarSummonRuntime.FindEarring(Owner);
        if (earring == null)
        {
            LocalMultiControlLogger.Info($"[地狱战神] 【分裂精神】中止：持有者没有【地狱耳环】。owner={Owner.NetId}");
            return false;
        }

        if (Owner.Creature == null)
        {
            LocalMultiControlLogger.Warn($"[地狱战神] 【分裂精神】中止：读取不到持有者的生物实体。owner={Owner.NetId}");
            return false;
        }

        string ratio = LocalWakuuAutopilotConfig.HellwarSplitHpRatio;
        decimal currentHp = Owner.Creature.CurrentHp;
        decimal hpAfter = HellwarSummonPolicy.ResolveHpAfterSplit(currentHp, ratio);
        decimal hpLoss = currentHp - hpAfter;

        // ① 先扣血（失败 / 不合法 ⇒ 整次不消费，后面不会加计数）。
        if (hpAfter < currentHp)
        {
            await CreatureCmd.SetCurrentHp(Owner.Creature, hpAfter);
        }
        else
        {
            // 两种"没扣血"的原因要分开写（档位=不扣血 / 血量已在下限），否则日志判读会误以为档位没生效。
            string reason = HellwarSummonPolicy.IsNoCostSplitRatio(ratio) ? "档位=不扣血(0%)" : "当前血量已在下限";
            LocalMultiControlLogger.Info(
                $"[地狱战神] 【分裂精神】不扣血（{reason}）: owner={Owner.NetId}, 血量={currentHp:0}/{Owner.Creature.MaxHp:0}");
        }

        // ② 再加计数（R2：每次 +1）。
        int before = earring.SpiritCount;
        earring.AddSpirit(HellwarSummonPolicy.SplitSpiritGain);

        // ③ D5 档位：免费额外选项 ⇒ 置一次性标记，让耳环的 ShouldDisableRemainingRestSiteOptions 返回 false。
        bool freeExtraAction = LocalWakuuAutopilotConfig.HellwarSplitFree;
        if (freeExtraAction)
        {
            earring.PendingFreeRestAction = true;
        }

        LocalMultiControlLogger.Info(
            $"[地狱战神] 【分裂精神】已完成: owner={Owner.NetId}, 扣血={hpLoss:0}"
            + $"（{currentHp:0}→{Owner.Creature.CurrentHp:0}/{Owner.Creature.MaxHp:0}, 档位={ratio}%）, "
            + $"计数={before}→{earring.SpiritCount}, 免费额外选项={freeExtraAction}");
        return true;
    }
}
