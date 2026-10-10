#nullable enable

using System;
using System.Threading.Tasks;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace LocalMultiControl.Scripts.Models.Powers;

/// <summary>
/// 战灵召唤 **R10（Phase 4）「战灵守卫」**：挂在**绑主（召唤者）**身上，让它的战神替它吃一半攻击伤害
/// （D4 拍板口径 = **玩家零伤害 + 战神吃一半**）。
///
/// 两条钩子（**必须用同一个判据**，见 <see cref="HellwarSummonPolicy.ShouldGuardIncomingDamage"/>）：
/// <list type="number">
/// <item><b>减半</b> = <c>ModifyHpLostBeforeOsty</c> 返回 <c>amount / 2</c> —— 这一步在**绑主自己的格挡已经结算完**
///   之后（`CreatureCmd.Damage:287` 结算格挡 → `:288` BeforeOsty）⇒ 绑主的格挡照常生效；</item>
/// <item><b>转移</b> = <c>ModifyUnblockedDamageTarget</c> 返回**战神**（把减半后的量转过去；
///   `:293` 只有新目标 `LoseHpInternal`）⇒ 绑主最终掉 **0**、战神掉**一半**（另一半消失 = 旧【我挡】语义）。</item>
/// </list>
/// ⚠ **刻意不做**（与 D4/§4 R10 的原样口径一致，别顺手加）：
/// <list type="bullet">
/// <item>**不加"战神用自己的格挡"** —— 原版重定向后的 `LoseHpInternal` 不吃承伤者格挡（与 Osty 同款）；
///   要加得再挂一条 `ModifyHpLostAfterOsty` + 一次性标记（抄 <see cref="LocalWakuuDaddyShieldPower"/> 的
///   `_redirectPending` 写法，`LocalWakuuDaddyShieldPower.cs:113-144`）—— 那是另一套语义，先不混进来。</item>
/// <item>**不保存"是哪个战神"**：每个钩子当场按绑主 NetId 查 <see cref="HellwarWarGodFactory.FindGuardCreature"/>
///   ⇒ 战神换人 / 死而复生 / 全部死光 都不需要重挂或摘除（查不到就原样放行，等价于没有守卫）。</item>
/// <item>**R10 修订（2026-10-10 用户口径）**：查询带**当前 round** ⇒ 战神**出场那一回合不当肉盾**
///   （它那回合自带 1 层【无实体】：打不死 + 白挡一半 = 太强）。所以"挂上了"≠"开始挡了"，
///   真正开始挡的那一刻由 `_effectLogged` 锚点 `战灵守卫已生效` 打出来。</item>
/// </list>
///
/// 已知边界（**别当 bug 修**，见设计文档 §4 R10）：`DieForYouPower`（亡灵契约师的 Osty）是**另一个**重定向源，
/// 两个源在 `Hook.ModifyUnblockedDamageTarget` 里是**链式**的 —— 谁先跑取决于监听器遍历顺序；
/// 若 Osty 那条先跑，目标已从绑主变成 Osty ⇒ 本守卫看到 `target != Owner` 直接放行（退回原版 Osty 行为，无害）。
///
/// ⚠ 可见性**故意关掉**（照抄 <see cref="LocalWakuuDaddyShieldPower"/> 的教训，别顺手改回 true）：
/// 可见状态下原版要给状态建节点并 `ResourceLoader.Load` 图标资源，而本项目没有 PCK
/// ⇒ 每次施加都会在 godot.log 留一条引擎级 ERROR，把"期望 0 错误"的日志卫生搞脏。
/// </summary>
internal sealed class HellwarWarGodGuardPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    /// <summary>每个绑主只该有一份（效果与层数无关，纯"开关"）。</summary>
    public override PowerStackType StackType => PowerStackType.Single;

    protected override bool IsVisibleInternal => false;

    public override bool ShouldPlayVfx => false;

    /// <summary>
    /// 当前该替绑主挨打的那个战神（查不到 = null ⇒ 两个钩子都原样放行）。
    ///
    /// **R10 修订（2026-10-10 用户口径）**：查询带上**当前 round** —— 战神**出场那一回合不当肉盾**
    /// （它那回合自带【无实体】，打不死还白挡一半 = 太强；用户实机体感）。
    /// `RoundNumber` 拿不到时传 0 ⇒ `IsGuardEligible` 恒假 ⇒ 保守地不挡。
    /// </summary>
    private Creature? Guard
    {
        get
        {
            Player? summoner = Owner.Player;
            if (summoner == null)
            {
                return null;
            }

            int round = Owner.CombatState?.RoundNumber ?? 0;
            return HellwarWarGodFactory.FindGuardCreature(summoner.NetId, round);
        }
    }

    /// <summary>"守卫真的开始挡了"的锚点是否已打过（每场战斗一次 —— 复测时要能一眼看到"从哪一回合起生效"）。</summary>
    private bool _effectLogged;

    /// <summary>绑主本次要掉的 HP **减半**（减半后紧接着被下面那条钩子转给战神）。</summary>
    public override decimal ModifyHpLostBeforeOsty(
        Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (!HellwarSummonPolicy.ShouldGuardIncomingDamage(
                props.IsPoweredAttack(), object.ReferenceEquals(target, Owner), Guard != null))
        {
            return amount;
        }

        return HellwarSummonPolicy.ResolveGuardedHpLoss(amount);
    }

    /// <summary>把这一笔**转给战神**（返回新目标；`LoseHpInternal` 只在它身上结算）。</summary>
    public override Creature ModifyUnblockedDamageTarget(
        Creature target, decimal amount, ValueProp props, Creature? dealer)
    {
        Creature? guard = Guard;
        if (!HellwarSummonPolicy.ShouldGuardIncomingDamage(
                props.IsPoweredAttack(), object.ReferenceEquals(target, Owner), guard != null))
        {
            return target;
        }

        if (!_effectLogged)
        {
            // 复测锚点：R10 修订后"挂上 ≠ 在挡"（出场回合不当肉盾）⇒ 需要一条"真的开始挡了"的证据。
            _effectLogged = true;
            LocalMultiControlLogger.Info(
                $"[地狱战神] 战灵守卫已生效（自本次受击起战神替绑主承伤）: 绑主={Owner.Player?.NetId}, "
                + $"守卫=seat{guard!.Player?.NetId}, round={Owner.CombatState?.RoundNumber.ToString() ?? "?"}");
        }

        return guard!;
    }

    /// <summary>
    /// 战斗结束就自己摘掉（本 Power 是"本场战斗的守卫关系"，不该跨战斗留着）。
    /// 空转也不怕：战斗结束时 `FindGuardCreature` 早就查不到战神，两个钩子本来就放行。
    /// </summary>
    public override async Task AfterCombatEnd(CombatRoom room)
    {
        try
        {
            await PowerCmd.Remove(this);
        }
        catch (Exception)
        {
            // 战斗收尾阶段摘状态失败无所谓（下一场开始时它已经是"查不到战神 ⇒ 空转"）。
        }
    }
}
