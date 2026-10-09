#nullable enable
using System;
using System.Threading.Tasks;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace LocalMultiControl.Scripts.Models.Relics;

/// <summary>
/// 战灵召唤（原 ④「地狱战神」，#21）的核心遗物【**地狱耳环**】—— 计数载体。
///
/// <list type="bullet">
/// <item><b>R2 / Phase 1</b>：休息处的自定义选项【分裂精神】每次使用给它 **+1 计数**
///   （<see cref="SpiritCount"/>，含存档往返与遗物格子上的数字显示）；</item>
/// <item><b>R3 / Phase 2（未实装）</b>：战斗中每回合消耗计数召唤临时玩家「战神」。</item>
/// </list>
///
/// 计数**原生支持**（照抄原版 <c>HappyFlower</c> 的三件套）：
/// <c>ShowCounter => true</c> + <c>DisplayAmount</c> + <c>[SavedProperty]</c>；
/// 数值变化时**必须**调 <c>InvokeDisplayAmountChanged()</c>，否则遗物格子上的数字不刷新。
/// 存档往返由 <c>SerializableRelic.Props → SavedProperties.From(model)</c> 扫描 <c>[SavedProperty]</c> 完成。
///
/// 还兼 **D5 档位**（休息处"免费额外选项"↔"占用整次休息"）的落点：原版 <c>MiniatureTent</c> 就是靠
/// <see cref="ShouldDisableRemainingRestSiteOptions"/> 返回 <c>false</c> 实现"还能再选一次"的。
/// 我们的选项用完只把 <see cref="PendingFreeRestAction"/> 置 true（**一次性**），
/// 所以只有【分裂精神】这一条是免费的，之后选 MEND / SMITH 照常消耗整次休息。
/// </summary>
internal sealed class LocalWakuuHellwarEarring : RelicModel
{
    private int _spiritCount = HellwarSummonPolicy.InitialSpiritCount;

    private int _lastRewardedActIndex = -1;

    public override RelicRarity Rarity => RelicRarity.Event;

    // 一期借原版【低语耳环】图标（与本 mod 其它自制遗物同一张图；本项目没有 PCK，正式美术待定）。
    protected override string IconBaseName => "whispering_earring";

    /// <summary>遗物格子上显示的数字（= 当前计数）。</summary>
    public override bool ShowCounter => true;

    public override int DisplayAmount => SpiritCount;

    /// <summary>
    /// 战力预算"计数"（R2：休息处【分裂精神】每次 +1；R3：战斗中召唤战神时消耗）。
    /// 带 <c>[SavedProperty]</c> ⇒ 随存档往返；setter 内必须刷显示数字。
    /// </summary>
    [SavedProperty]
    public int SpiritCount
    {
        get => _spiritCount;
        set
        {
            AssertMutable();
            _spiritCount = value;
            InvokeDisplayAmountChanged();
        }
    }

    /// <summary>
    /// 「每进入新的一幕自动获得计数」的**幂等水位**：本局已经发过计数到的最大幕号（初始 -1 = 一幕都没发过）。
    ///
    /// 为什么要存档字段：进局（`RunLaunch` 时发耳环）与"进入第 1 幕"（`EnterAct(0)` 的 `AfterActEntered`）
    /// 会**两次**经过同一条路，且**读档后会重进当前幕** ⇒ 用进程内标记会导致"读档补发"或"进局双发"。
    /// </summary>
    [SavedProperty]
    public int LastRewardedActIndex
    {
        get => _lastRewardedActIndex;
        set
        {
            AssertMutable();
            _lastRewardedActIndex = value;
        }
    }

    /// <summary>
    /// D5 档位用的一次性标记：本次休息处**刚用过【分裂精神】**且配置是"免费额外选项"。
    /// **不存档**（普通属性，不标 <c>[SavedProperty]</c>）—— 它只在一次选择的前后有效。
    /// 由 <see cref="HellwarSplitSpiritOption"/> 在成功路径置位、由本类的
    /// <see cref="ShouldDisableRemainingRestSiteOptions"/> 消费（读一次即清）。
    /// </summary>
    internal bool PendingFreeRestAction { get; set; }

    /// <summary>增加计数（下限 0）并刷显示。</summary>
    internal void AddSpirit(int amount)
    {
        SpiritCount = Math.Max(0, SpiritCount + amount);
    }

    /// <summary>
    /// 原版钩子：**进入新的一幕之后**（`RunManager.EnterAct` 末尾 `Hook.AfterActEntered`）。
    /// 用于「每到一个新的幕就自动获得计数」（2026-10-09 用户追加的开关项）——
    /// 挂在这里的好处是**零新增 Harmony 目标**（遗物本身就是 hook listener）。
    /// </summary>
    public override Task AfterActEntered()
    {
        TryGrantActCharges(Owner?.RunState, "after-act-entered");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 「每进入新的一幕自动获得计数」的落地点（幂等，见 <see cref="LastRewardedActIndex"/>）。
    /// 由 <see cref="AfterActEntered"/> 与"发耳环"那条路（`GrantHellwarEarringAsync`）**两处**调用 ⇒
    /// 无论异步时序谁先到，每个幕只会发一次。返回本次实际发出的计数（0 = 没发）。
    /// </summary>
    internal int TryGrantActCharges(IRunState? runState, string source)
    {
        try
        {
            int actIndex = runState?.CurrentActIndex ?? -1;
            if (!HellwarSummonPolicy.ShouldGrantActCharges(
                    LocalWakuuAutopilotConfig.HellwarActGain,
                    actIndex,
                    LastRewardedActIndex))
            {
                return 0;
            }

            int amount = HellwarSummonPolicy.ResolveActGainAmount(LocalWakuuAutopilotConfig.HellwarActGainAmount);
            int before = SpiritCount;
            AddSpirit(amount);
            LastRewardedActIndex = actIndex;
            LocalMultiControlLogger.Info(
                $"[地狱战神] 进入新的一幕，已自动获得计数（{source}）: player={Owner?.NetId}, "
                + $"幕={actIndex + 1}, 计数={before}→{SpiritCount}(+{amount})");
            return amount;
        }
        catch (Exception exception)
        {
            // 这一幕的发放在这里失败不该影响进幕主流程 ⇒ 只记 WARN（水位不动 ⇒ 下次仍会重试）。
            LocalMultiControlLogger.Warn($"[地狱战神] 每幕自动计数失败（已忽略）: {exception.Message}");
            return 0;
        }
    }

    /// <summary>把"免费额外选项"标记复位（每次进入休息处重新投放本遗物的选项时调用，防残留）。</summary>
    internal void ResetPendingFreeRestAction()
    {
        PendingFreeRestAction = false;
    }

    /// <summary>
    /// 原版钩子：某个玩家选完一个休息处选项后，是否**清空其余选项**。
    /// <c>true</c> = 占用整次休息（原版默认）；<c>false</c> = 保留其余选项（原版 <c>MiniatureTent</c> 的"还能再选"用法）。
    ///
    /// 我们只在"刚用完【分裂精神】且配置为免费档"时返回 <c>false</c>，且该标记**读一次即清**
    /// （否则之后选 MEND 也会被当成免费动作 ⇒ 一次休息能连做两件事）。
    /// </summary>
    public override bool ShouldDisableRemainingRestSiteOptions(Player player)
    {
        if (player != Owner || !PendingFreeRestAction)
        {
            return true;
        }

        PendingFreeRestAction = false;
        LocalMultiControlLogger.Info(
            $"[地狱战神] 免费额外选项生效：用完【分裂精神】后保留其余休息处选项（不消耗整次休息）。player={player.NetId}");
        return false;
    }
}
