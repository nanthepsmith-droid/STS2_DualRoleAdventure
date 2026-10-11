#nullable enable
using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// **特效播放入口的 null 目标护栏**（r271，实机 2026-10-11 判读）。
///
/// <para><b>病灶（实机栈）</b>：第三方 mod 的怪**场景资源加载失败**（ArknightsBoss 的
/// <c>arknights_boss_monster_eblana.tscn</c> 加载 ERROR ⇒ 怪没生成全，敌列表留下 null 条目），
/// 另一个第三方（海克斯大乱斗的【残暴】敌 hex）开战给怪上格挡
/// （<c>HextechMayhemModifier.ApplyMonsterCombatStartHexesToEnemy → CreatureCmd.GainBlock →
/// VfxCmd.PlayOnCreatureCenter</c>）⇒ <c>target.IsDead</c> 对 null 抛 NRE ⇒ 异常冒穿
/// <c>Hook.BeforeCombatStart</c> ⇒ 原版打 <c>turn loop died … combat is stuck until the room is restarted</c>
/// ⇒ **整场战斗卡死**（没怪 / 不抽牌 / 不能换人 / 不能结束回合）。</para>
///
/// <para><b>为什么在原版方法上挂护栏而不是修第三方</b>：① 原版实现里
/// <c>GetCreatureNode() == null</c> 本来就有判空（只漏了 <c>target == null</c>），这是游戏自身的健壮性缺口；
/// ② 播放特效是**纯表现层** —— 吞掉 NRE 的代价只是"那一下没特效"，格挡 / 伤害等数值逻辑照常继续
/// （异常被吞后调用方 <c>GainBlock</c> 正常返回）；③ 触发源不止一家（任何第三方对 null / 坏怪播特效都走这几个入口）。
/// 取向与 <see cref="HoveredModelTrackerForeignSeatGuardPatch"/>（AE-24）一致：**护栏在表现层入口，防回合循环被打死**。</para>
///
/// <para><b>做法</b>：给 <c>VfxCmd</c> 的四个播放入口挂 **finalizer**（只吞 <see cref="NullReferenceException"/>，
/// 其它异常原样重抛）—— 单数版吞掉后，复数版的循环自然继续下一只；复数版再兜一层 <c>targets == null</c>。
/// 每个命中点去重打一条 WARN（同族噪音只报一次，不抬全局 ERROR 计数）。</para>
///
/// <para>⚠ 本补丁**不修**第三方资源缺失本身（那局怪可能仍显示异常）—— 那是 ArknightsBoss mod 自己的事；
/// 本补丁只保证"它坏的时候，把整场战斗拖死的是不是我们"。</para>
/// </summary>
[HarmonyPatch]
internal static class VfxCmdNullTargetGuardPatch
{
    private static readonly object Lock = new();
    private static readonly HashSet<string> Warned = new();

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(VfxCmd), nameof(VfxCmd.PlayOnCreatureCenter),
        new[] { typeof(Creature), typeof(string) })]
    private static Exception? PlayOnCreatureCenterFinalizer(Exception __exception, Creature? target)
        => Swallow(__exception, nameof(VfxCmd.PlayOnCreatureCenter), Describe(target));

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(VfxCmd), nameof(VfxCmd.PlayOnCreature),
        new[] { typeof(Creature), typeof(string) })]
    private static Exception? PlayOnCreatureFinalizer(Exception __exception, Creature? target)
        => Swallow(__exception, nameof(VfxCmd.PlayOnCreature), Describe(target));

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(VfxCmd), nameof(VfxCmd.PlayOnCreatureCenters),
        new[] { typeof(IEnumerable<Creature>), typeof(string) })]
    private static Exception? PlayOnCreatureCentersFinalizer(Exception __exception)
        => Swallow(__exception, nameof(VfxCmd.PlayOnCreatureCenters), "targets=null");

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(VfxCmd), nameof(VfxCmd.PlayOnCreatures),
        new[] { typeof(IEnumerable<Creature>), typeof(string) })]
    private static Exception? PlayOnCreaturesFinalizer(Exception __exception)
        => Swallow(__exception, nameof(VfxCmd.PlayOnCreatures), "targets=null");

    private static string Describe(Creature? target)
    {
        if (target == null)
        {
            return "target=null";
        }

        try
        {
            // Creature 没有 NetId（那是 Player 的）；名字最够用 —— WARN 有方法名，这里只为区分是哪只怪。
            return $"name={target.Name}";
        }
        catch (Exception)
        {
            return "target=<已失效>";
        }
    }

    /// <summary>只吞 NRE（bad-target 场景），其它异常原样重抛；命中点去重 WARN。</summary>
    private static Exception? Swallow(Exception? exception, string method, string targetDescribe)
    {
        if (exception == null)
        {
            return null;
        }

        if (exception is not NullReferenceException)
        {
            return exception;
        }

        lock (Lock)
        {
            if (Warned.Add(method))
            {
                Log.Warn(
                    $"[特效护栏] 对坏目标播特效的 NRE 已拦截（第三方在怪物未生成 / 资源加载失败时仍调用；"
                    + $"仅损失该次特效，战斗流程照常）: {method}, {targetDescribe}。"
                    + "同类命中不再重复打印。");
            }
        }

        return null;
    }
}
