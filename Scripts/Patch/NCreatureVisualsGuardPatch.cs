#nullable enable
using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// **坏怪视觉坐标的护栏**（r272，实机 2026-10-11 第二炸点；与 <see cref="VfxCmdNullTargetGuardPatch"/> 同族）。
///
/// <para><b>背景</b>：第三方 mod 的怪**场景资源加载失败**（ArknightsBoss 的
/// <c>arknights_boss_monster_eblana.tscn</c> 等）⇒ 怪的数据层在、<b>视觉层（<c>NCreature.Visuals</c>）没建</b>
/// ⇒ 它在场上是一只"隐形怪"。r271 兜住了**特效入口**（<c>VfxCmd</c>），本轮实机又从**伤害数字**炸：
/// 猪猪 mod 遗物对它扣上限 → <c>CreatureCmd.Damage → NDamageNumVfx.Create → NCreature.VfxSpawnPosition</c>
/// ⇒ <c>Visuals.VfxSpawnPosition.GlobalPosition</c> 对 null 链抛 NRE ⇒ 再把 turn loop 打死。</para>
///
/// <para><b>做法（刻意收窄）</b>：给 <see cref="NCreature"/> 的两个**返回值类型（Vector2）**的 getter 挂
/// finalizer —— 吞掉 NRE 后 <c>__result</c> 落 <c>Vector2.Zero</c>（伤害数字/特效画在原点，无害），
/// 整条调用链能走通。<b>刻意不兜引用类型返回</b>（如 <c>Body</c> = <c>Visuals.GetCurrentBody()</c>）：
/// 吞掉后调用方拿 null 再 <c>.PlayAnim()</c> 只会把炸点转移到下游。</para>
///
/// <para>⚠ **根因在第三方**（ArknightsBoss 的资源缺失 / 版本未跟上游戏 0.111）—— 更新或重装那个 mod 才是
/// 真正的解法；本护栏是逐点兜底，后续若再冒出新的视觉访问炸点（死亡动画 / 意图 / 血条），按同一模式补。</para>
/// </summary>
[HarmonyPatch]
internal static class NCreatureVisualsGuardPatch
{
    private static readonly object Lock = new();
    private static readonly HashSet<string> Warned = new();

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(NCreature), "get_VfxSpawnPosition")]
    private static Exception? VfxSpawnPositionFinalizer(Exception __exception, ref Vector2 __result)
    {
        if (Swallow(__exception, nameof(NCreature.VfxSpawnPosition)))
        {
            __result = Vector2.Zero;
            return null;
        }

        return __exception;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(NCreature), "get_PowerAppliedVfxSpawnPosition")]
    private static Exception? PowerAppliedVfxSpawnPositionFinalizer(Exception __exception, ref Vector2 __result)
    {
        if (Swallow(__exception, nameof(NCreature.PowerAppliedVfxSpawnPosition)))
        {
            __result = Vector2.Zero;
            return null;
        }

        return __exception;
    }

    /// <summary>只吞 NRE（null Visuals 链），其它异常原样重抛；命中去重 WARN。</summary>
    private static bool Swallow(Exception? exception, string member)
    {
        if (exception == null)
        {
            return false;
        }

        if (exception is not NullReferenceException)
        {
            return false;
        }

        lock (Lock)
        {
            if (Warned.Add(member))
            {
                Log.Warn(
                    "[特效护栏] 无视觉怪（场景加载失败的第三方怪）的视觉坐标访问已兜底为原点 "
                    + $"（伤害数字/特效会画在 (0,0)，战斗流程照常）: {member}。同类命中不再重复打印。"
                    + "根因是那只怪的 mod 场景资源缺失，建议更新/重装该 mod。");
            }
        }

        return true;
    }
}
