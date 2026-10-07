#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace LocalMultiControl.Scripts.Models.Powers;

/// <summary>
/// ④「瓦库的爹」【我挡】的承伤状态：**挂在被挡的队友身上**，把它的攻击伤害转给施牌者、且只算一半。
///
/// 实现照抄两条原版先例（r228 复核 `sts2src`）+ 一条 r229 补丁：
/// <list type="bullet">
/// <item><b>伤害重定向</b> = <c>ModifyUnblockedDamageTarget</c>，与 <c>DieForYouPower</c>（Osty 替主人挨打）
///   同款 —— 那是原版**唯一**的伤害重定向机制（`AbstractModel.cs:1693/1713` 明写 "Only DieForYouPower
///   causes damage redirection"）。⚠ 这个钩子**只返回新目标、改不了数值**（`Hook.cs:2057`），
///   所以「只担一半」必须另走一条：</item>
/// <item><b>减半</b> = <c>ModifyDamageMultiplicative</c> 返回 <c>0.5</c>，与 <c>GuardedPower</c>（坦克掩护队友
///   时队友受击 ×0.5）同款。两条合起来正好是「转移 + 减半」。</item>
/// <item><b>承伤者的格挡</b> = <c>ModifyHpLostAfterOsty</c>（r229 实机反馈后补）—— 重定向发生在被挡者格挡结算
///   **之后** ⇒ 承伤者天然是"直接掉血"、自己的格挡完全不参与；在重定向之后的那条钩子里手动补一次
///   <c>Creature.DamageBlockInternal</c>（原版主流程同一个调用），让这一击"真的打在你身上"。</item>
/// </list>
///
/// 时序（照 <c>CoveredPower</c> / <c>InterceptPower</c>）：<c>AfterSideTurnEnd(side == Enemy)</c> 自删 ——
/// 玩家回合打出、覆盖**接下来的敌方回合**，敌方回合结束时消失（这才是「本回合受到伤害」的语义）。
///
/// 注意本状态**只对「攻击伤害」生效**（<c>props.IsPoweredAttack()</c>，与 GuardedPower / DieForYouPower
/// 同口径）：中毒、直接掉血等非攻击来源既不减半也不转移。
/// </summary>
internal sealed class LocalWakuuDaddyShieldPower : PowerModel
{
    /// <summary>承伤方承受的倍率（提案 §5.1「只担 1/2」）。</summary>
    private const decimal HalfMultiplier = 0.5m;

    /// <summary>「刚发生了一次转给承伤者的重定向」的一次性标记（详见 <see cref="ModifyHpLostAfterOsty"/>）。</summary>
    private bool _redirectPending;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    /// <summary>每次施加都是独立实例（照 <c>GuardedPower</c> / <c>CoveredPower</c> 的写法；
    /// 否则同一个队友被第二个人挡时会叠在旧实例上、把承伤者错记成第一个人）。</summary>
    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    /// <summary>
    /// **故意不可见**（r228 的取舍，别顺手改成 true）：
    /// 状态的图标固定取 <c>atlases/power_atlas.sprites/&lt;entry&gt;.tres</c>（<c>PowerModel.PackedIconPath</c>
    /// 不是 virtual ⇒ 借不到原版图，本项目也没有 PCK），而**可见**状态下
    /// <c>NPowerContainer.Add</c> 会建节点、<c>NPower.Reload</c> 直接
    /// <c>ResourceLoader.Load</c> 那个不存在的资源 ⇒ 每次施加都在 godot.log 里留一条引擎级 ERROR，
    /// 把"期望 0 错误"的日志卫生搞脏（会看起来像回归）。
    /// 不可见 ⇒ 不建节点、不读图标；<see cref="ShouldPlayVfx"/> 一并关掉（增幅/移除/闪动的 VFX 同样取图标）。
    /// 代价 = 玩家看不到这个状态图标；效果本身有日志锚点 `[瓦库的爹] 我挡`，且它只持续到敌方回合结束。
    /// </summary>
    protected override bool IsVisibleInternal => false;

    /// <summary>见 <see cref="IsVisibleInternal"/>：不可见状态下把 VFX 一并关掉（VFX 也取图标）。</summary>
    public override bool ShouldPlayVfx => false;

    /// <summary>施加者 = 承伤者（打【我挡】的那个人）。可能为 null（理论上不会）。</summary>
    private Creature? Protector => Applier;

    private bool IsProtectorAvailable(out Creature protector)
    {
        Creature? candidate = Protector;
        if (candidate == null || candidate.IsDead)
        {
            // 承伤者没了 ⇒ 既不减半也不转移（等同这张牌白打；状态本身会在敌方回合结束时被摘掉）。
            protector = null!;
            return false;
        }

        protector = candidate;
        return true;
    }

    /// <summary>「只有 1/2」：被挡者受到的攻击伤害先减半，再谈格挡与转移。</summary>
    public override decimal ModifyDamageMultiplicative(
        Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (target != Owner || !props.IsPoweredAttack())
        {
            return 1m;
        }

        return IsProtectorAvailable(out _) ? HalfMultiplier : 1m;
    }

    /// <summary>「换成由你承受」：被挡者本来要吃的**未被格挡**伤害改由承伤者掉血。</summary>
    public override Creature ModifyUnblockedDamageTarget(
        Creature target, decimal amount, ValueProp props, Creature? dealer)
    {
        if (target != Owner || !props.IsPoweredAttack())
        {
            return target;
        }

        if (!IsProtectorAvailable(out Creature protector))
        {
            return target;
        }

        // 给紧接着的 AfterOsty 阶段留个一次性标记：只有**我们刚转过来的这一笔**才该走下面的格挡，
        // 否则承伤者被敌人直接打的那一笔会把格挡算两遍（`CreatureCmd.Damage:290→291` 之间没有 await）。
        _redirectPending = true;
        return protector;
    }

    /// <summary>
    /// 承伤者**用自己的格挡**吃掉转移过来的伤害（2026-10-07 实机反馈的修复）：
    /// 原版 <c>ModifyUnblockedDamageTarget</c> 只换目标、**改不了数值**，而重定向发生在
    /// 「被挡者的格挡结算」之后 ⇒ 承伤者天然是直接掉血（<c>LoseHpInternal</c>）、格挡完全不参与
    /// （Osty 那条路也没这问题，因为 Osty 本来就不攒格挡）。
    /// 所以在这条**重定向之后**的钩子里补一次格挡结算 —— 用的就是原版主流程同一个
    /// <c>Creature.DamageBlockInternal</c>（见 <c>CreatureCmd.Damage:287</c>），
    /// 语义与"这一击真的打在你身上"一致（<c>ValueProp.Unblockable</c> 照样不吃格挡）。
    /// </summary>
    public override decimal ModifyHpLostAfterOsty(
        Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (!_redirectPending)
        {
            return amount;
        }

        _redirectPending = false;
        if (!IsProtectorAvailable(out Creature protector) || !ReferenceEquals(target, protector))
        {
            return amount;
        }

        decimal blocked = protector.DamageBlockInternal(amount, props);
        return Math.Max(amount - blocked, 0m);
    }

    /// <summary>敌方回合结束即消失（照 <c>CoveredPower</c> / <c>InterceptPower</c>）。</summary>
    public override async Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        _redirectPending = false;
        if (side == CombatSide.Enemy)
        {
            await PowerCmd.Remove(this);
        }
    }
}
