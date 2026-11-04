using System;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 战斗中途「两人立绘左右对调」的**布局口径固定**（2026-11-04 用户实机复报）。
///
/// <para><b>现象（日志实证）</b>：同一场战斗里先 <c>[角色2 | 角色1]</c>，22.7s 后变成
/// <c>[角色1 | 角色2]</c>（探针 `节点索引 1 -> 0 / 0 -> 1`），换一场战斗又恢复。</para>
///
/// <para><b>成因</b>：原版 <c>NCombatRoom.PositionPlayersAndPets</c> 的唯一排序键是
/// <c>LocalContext.IsMe(entity)</c> —— "我"那一项 <c>list.Insert(0, …)</c>（= 最靠右），其余按入参顺序。
/// 原版只在**开战** <c>CreateAllyNodes</c> 调一次；战斗**中途**会再调它的只有第三方布局补丁
/// （NinjaSlayer 的 YamotoKoki reflow：任何友方单位加入/移除都会触发）。
/// 本地多控下 <c>LocalContext</c> 会随切前台漂移 ⇒ 同样的重排在不同的 POV 下会得出**不同的左右顺序**
/// ⇒ 玩家看到"打一半两人立绘换了位置"。</para>
///
/// <para><b>修法（只钉口径，不抢布局）</b>：记住本场战斗**第一次**调用时
/// <c>LocalContext.NetId</c> 的值（= 原版开局排布用的那一席），之后每次
/// <c>PositionPlayersAndPets</c> 调用期间临时把 <c>LocalContext.NetId</c> 钉回它，
/// 结束后（含异常路径，用 Finalizer）立刻还原。
/// <list type="bullet">
/// <item><b>不重排任何节点</b>：第三方对宠物/召唤物的有意布局一律照旧，只是"谁是 me"恒等于开局那一席；</item>
/// <item>开局时 <c>LocalContext.NetId</c> 为空 ⇒ 不钉（保持原版"没有 me"的顺序）；</item>
/// <item>只在本地多控会话 + 战斗进行中生效，真实联机 / 单人零影响。</item>
/// </list></para>
/// </summary>
[HarmonyPatch(
    typeof(NCombatRoom),
    nameof(NCombatRoom.PositionPlayersAndPets),
    new[] { typeof(System.Collections.Generic.List<NCreature>), typeof(float), typeof(bool) })]
internal static class NCombatRoomPositionSeatPinPatch
{
    /// <summary>本场战斗开局排布用的"me"（0 = 没有/不钉）。</summary>
    private static ulong _layoutSeatId;

    /// <summary>本场战斗的身份（换战斗就重记）。</summary>
    private static int _combatIdentity;

    private static bool _reflowLogged;

    /// <summary>"取不到场次身份"的日志只打一次（每场战斗重置）。</summary>
    private static bool _skipLogged;

    [HarmonyPrefix]
    private static void Prefix(out ulong? __state)
    {
        __state = LocalContext.NetId;

        try
        {
            if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
            {
                return;
            }

            if (!RunManager.Instance.IsInProgress)
            {
                return;
            }

            // ⚠ 场次身份**用战斗房间**（`RunState.CurrentRoom`），**不能**用 `CombatManager.DebugOnlyGetState()`：
            // 2026-11-04 20:18 实机（r335）证明——开战那次 `CreateAllyNodes` 调用时战斗态还是 null
            // ⇒ 用战斗态做身份会把「布局那次调用」整个跳过，第一次被我们看到的反而是一次 mid-combat reflow，
            // 于是把当时已经漂移的 POV（角色2）当成"开局席位"钉死 ⇒ 换位照旧发生
            // （日志：`已记住… seat=…327` 之后紧接 `节点索引 1 -> 0 / 0 -> 1` 对调）。
            // 房间对象在整场战斗里稳定存在、布局那一刻就有 ⇒ 一定能捕捉到开局那一席。
            int identity = ResolveCombatIdentity();
            if (identity == 0)
            {
                LogSkipOnce("场次身份不可得（无 CurrentRoom / CombatState）");
                return;
            }

            if (identity != _combatIdentity)
            {
                _combatIdentity = identity;
                _layoutSeatId = LocalContext.NetId ?? 0UL;
                _reflowLogged = false;
                _skipLogged = false;
                LocalMultiControlLogger.Info(
                    $"立绘站位：已记住本场战斗的开局布局席位: seat={_layoutSeatId}（0 = 开局无 me，不钉）");
                return;
            }

            // 只在战斗真的进行中才钉（开战前那次调用就是布局本身，不需要也不该改口径）。
            if (!CombatManager.Instance.IsInProgress)
            {
                return;
            }

            if (_layoutSeatId == 0UL)
            {
                return;
            }

            if (LocalContext.NetId == _layoutSeatId)
            {
                return;
            }

            // 中途重排：把"me"钉回开局那一席，避免按当前 POV 重新算出相反的左右顺序。
            LocalContext.NetId = _layoutSeatId;
            if (!_reflowLogged)
            {
                _reflowLogged = true;
                LocalMultiControlLogger.Info(
                    $"立绘站位：战斗中重排已按开局席位固定，避免左右对调（当前 POV ≠ seat={_layoutSeatId}）");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"立绘站位固定失败（本次不干预）: {exception.Message}");
        }
    }

    /// <summary>
    /// 本场战斗的身份：**优先房间对象**（`RunState.CurrentRoom`，布局那一刻就存在且整场稳定），
    /// 房间取不到才退到 `CombatState` 实例。都拿不到返回 0 ⇒ 本次不干预（下一帧/下一次再试）。
    /// </summary>
    private static int ResolveCombatIdentity()
    {
        try
        {
            RunState? runState = RunManager.Instance?.DebugOnlyGetState();
            if (runState?.CurrentRoom != null)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(runState.CurrentRoom);
            }
        }
        catch (Exception)
        {
            // 落到下面的兜底
        }

        try
        {
            CombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
            if (combatState != null)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(combatState);
            }
        }
        catch (Exception)
        {
            // 取不到 ⇒ 返回 0
        }

        return 0;
    }

    private static void LogSkipOnce(string reason)
    {
        if (_skipLogged)
        {
            return;
        }

        _skipLogged = true;
        LocalMultiControlLogger.Info($"立绘站位：本次未固定（{reason}），按原版行为布局。");
    }

    /// <summary>异常路径也必须还原上下文（否则后续所有权判定会停在开局席位）。</summary>
    [HarmonyFinalizer]
    private static void Finalizer(ulong? __state)
    {
        try
        {
            if (LocalContext.NetId != __state)
            {
                LocalContext.NetId = __state;
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"立绘站位收尾还原上下文失败: {exception.Message}");
        }
    }
}
