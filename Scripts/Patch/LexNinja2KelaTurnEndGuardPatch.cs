#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 第三方 mod「LexNinja2 / 蕾忍」的兼容守卫（r222 实机：**炼化瓦库后结束回合、敌方回合不开始**）。
///
/// **症状链（实机日志）**：`Combat #2 turn loop died while its combat is in progress; the combat is stuck
/// until the room is restarted: KeyNotFoundException: The given key 'MegaCrit.Sts2.Core.Entities.Players.Player'
/// was not present in the dictionary.` ← `LexNinja2.LexNinja2Code.Singleton.LexKelaSingleton.AfterSideTurnEnd`
/// ← `CombatManager.EndPlayerTurnPhaseTwoInternal` → `AfterAllPlayersReadyToBeginEnemyTurn`
/// ⇒ 回合循环死掉 ⇒ 玩家观感「结束回合后敌方回合不会开始」。
///
/// **根因（反编译确认）**：它的 <c>AfterSideTurnEnd</c> 是这么写的 ——
/// <code>
/// foreach (Player player in CurrentCombatState.Players)   // 遍历"本场所有玩家"
/// {
///     if (_usedLexKela[player]) …                         // 直接索引两个 Dictionary&lt;Player,bool&gt;
///     else if (_isActive[player]) await LexKela.Gain(player, 1, this);
/// }
/// </code>
/// 而这两个字典的键**只在"该玩家开始过回合"时写入**（<c>AfterPlayerTurnStart</c>）⇒
/// 被炼化的瓦库**死在战斗外**（休息处），下一场战斗它一直在 <c>CurrentCombatState.Players</c> 里
/// 但**永远轮不到它开始回合** ⇒ 键不存在 ⇒ 首次侧回合结束就 KeyNotFound。
/// （同等条件下 vanilla 多人里"上一场死掉的队友"也会踩到，所以这是它的健壮性缺口；
/// 但**是本 mod 的炼化把这条路径变成单人可复现**，故按「主 mod↔第三方交互」口径在本 mod 内兜住。）
///
/// **守卫做法（最小侵入，不动它的逻辑）**：在它的 <c>AfterSideTurnEnd</c> 前挂一个前缀，
/// 把**本场战斗所有玩家**在两个字典里补齐缺失的键（补 <c>false</c> = "没在用/没启用"，
/// 与它自己给非蕾忍角色写的默认值一致）⇒ 它的循环能正常跑完，不需要理解/改写它的判定。
///
/// ⚠ 只在**本地多控会话**里生效（官方联机保持原样）；未装该 mod / 改名 / 字段漂移一律**只记日志、绝不致命**，
/// 与 <see cref="CoopBotsShopAckPatch"/> / <c>WakuuSkadaAdapter</c> 同款「可选依赖」纪律。
/// </summary>
internal static class LexNinja2KelaTurnEndGuardPatch
{
    private const string TargetTypeName = "LexNinja2.LexNinja2Code.Singleton.LexKelaSingleton";

    private const string TargetMethodName = "AfterSideTurnEnd";

    /// <summary>它的两个 <c>Dictionary&lt;Player,bool&gt;</c> 字段名（版本漂移时两个都找不到 ⇒ 放弃守卫）。</summary>
    private const string IsActiveFieldName = "_isActive";

    private const string UsedFieldName = "_usedLexKela";

    /// <summary>补键日志上限（每场战斗一次即可，避免刷屏）。</summary>
    private const int MaxRepairLogs = 8;

    private static readonly Harmony _lateHarmony = new("sts2.dualroleadventure.late.lexninja2");

    private static FieldInfo? _isActiveField;

    private static FieldInfo? _usedField;

    private static bool _applied;

    private static bool _reportedMissing;

    private static int _repairLogCount;

    private static bool _prefixFailed;

    /// <summary>
    /// 找目标并挂前缀。**幂等**；找不到（未装 / 加载晚于本 mod）只打一条 INFO 并留给下次调用重试。
    /// 调用点：<c>Entry</c> 阶段 3 探测、<c>LocalMultiControlRuntime.OnRunLaunched</c>、战斗房间就绪。
    /// </summary>
    internal static void TryApplyLate()
    {
        if (_applied)
        {
            return;
        }

        try
        {
            Type? targetType = AccessTools.TypeByName(TargetTypeName);
            if (targetType == null)
            {
                if (!_reportedMissing)
                {
                    _reportedMissing = true;
                    LocalMultiControlLogger.Info(
                        "[LexKela守卫] 未找到 LexNinja2 的 LexKelaSingleton（未装蕾忍 mod 或它的程序集晚于本 mod 加载），"
                        + "暂不挂载；每次进局与战斗就绪时会再试。");
                }

                return;
            }

            MethodBase? target = AccessTools.Method(targetType, TargetMethodName);
            FieldInfo? isActive = AccessTools.Field(targetType, IsActiveFieldName);
            FieldInfo? used = AccessTools.Field(targetType, UsedFieldName);
            if (target == null || isActive == null || used == null)
            {
                if (!_reportedMissing)
                {
                    _reportedMissing = true;
                    LocalMultiControlLogger.Info(
                        $"[LexKela守卫] 目标结构不符（method={target != null}, {IsActiveFieldName}={isActive != null}, "
                        + $"{UsedFieldName}={used != null}），跳过守卫（可能在「死过的席位」上复现 KeyNotFound）。");
                }

                return;
            }

            MethodInfo? prefix = AccessTools.Method(typeof(LexNinja2KelaTurnEndGuardPatch), nameof(Prefix));
            if (prefix == null)
            {
                return;
            }

            _isActiveField = isActive;
            _usedField = used;
            _lateHarmony.Patch(target, prefix: new HarmonyMethod(prefix));
            _applied = true;
            LocalMultiControlLogger.Info(
                "[LexKela守卫] 已挂载「侧回合结束按玩家补键」前缀（防「死在战斗外的席位」让它 KeyNotFound 打断回合循环）。");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[LexKela守卫] 延迟挂载失败（已忽略）: {exception.Message}");
        }
    }

    /// <summary>
    /// 前缀：把本场战斗**所有玩家**在它那两个字典里补齐缺失的键（补 <c>false</c>）。
    /// 任何异常都吞掉（守卫失败也不能影响回合推进 —— 那正是它本来要炸的地方）。
    /// </summary>
    private static void Prefix(object __instance)
    {
        try
        {
            if (!LocalSelfCoopContext.IsEnabled || __instance == null)
            {
                return;
            }

            IReadOnlyList<Player>? players = CombatManager.Instance.DebugOnlyGetState()?.Players;
            if (players == null || players.Count == 0)
            {
                return;
            }

            int repaired = 0;
            List<string> repairedSeats = new();
            repaired += EnsureKeys(__instance, _isActiveField, players, repairedSeats);
            repaired += EnsureKeys(__instance, _usedField, players, repairedSeats);

            if (repaired > 0 && _repairLogCount < MaxRepairLogs)
            {
                _repairLogCount++;
                LocalMultiControlLogger.Info(
                    $"[LexKela守卫] 已为未登记玩家补键（防守卫目标 KeyNotFound 打断回合循环）: "
                    + $"补键处数={repaired}, 涉及席位=[{string.Join(", ", repairedSeats)}]");
            }
        }
        catch (Exception exception)
        {
            if (!_prefixFailed)
            {
                _prefixFailed = true;
                LocalMultiControlLogger.Warn($"[LexKela守卫] 补键失败（已忽略，回合推进以原逻辑为准）: {exception.Message}");
            }
        }
    }

    /// <summary>给一个 <c>Dictionary&lt;Player,bool&gt;</c> 字段补齐本场所有玩家的键；返回补了几处。</summary>
    private static int EnsureKeys(
        object instance,
        FieldInfo? field,
        IReadOnlyList<Player> players,
        List<string> repairedSeats)
    {
        if (field?.GetValue(instance) is not IDictionary dictionary)
        {
            return 0;
        }

        int repaired = 0;
        foreach (Player player in players)
        {
            if (player == null || dictionary.Contains(player))
            {
                continue;
            }

            dictionary[player] = false;
            repaired++;
            repairedSeats.Add(player.NetId.ToString());
        }

        return repaired;
    }
}
