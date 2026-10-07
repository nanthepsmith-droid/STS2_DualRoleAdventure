using System;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace NinjaSlayerBossGreetingFix.Scripts.Patch;

/// <summary>
/// 补丁 <c>NinjaSlayer.Code.ExternalAnimations.BossGreetingSync.get_Participants</c>。
///
/// 原实现（NinjaSlayer 1.0.12）：
/// <code>
/// private IEnumerable&lt;ulong&gt; Participants =&gt; _players.Where(id =&gt; {
///     if (_disconnected.Contains(id)) return false;
///     if (id == _network.NetId) return true;
///     var host = _network as INetHostGameService;
///     if (host != null) return host.NetHost.ConnectedPeerIds.Contains(id);  // ← NetHost 可能为 null
///     return false;
/// });
/// </code>
///
/// 该 lambda 假设「NetGameType.Host ⇒ NetHost != null」。本 mod 的本地回环
/// <c>LocalLoopbackHostGameService</c> 恰好是「Type = Host，但没有真实网络主机（NetHost = null）」，
/// 而参与者列表来自 <c>combatState.Players</c>，本地双角色下含第二个席位（非本机 NetId）
/// ⇒ 走进最后一行 ⇒ <c>NullReferenceException</c> ⇒ 冒穿 <c>Hook.BeforeCombatStart</c>
/// ⇒ <c>CombatManager.StartCombatInternal</c> 抛异常 ⇒ 回合循环死亡 ⇒ 战斗停在 NotPlayPhase。
///
/// 单人模式下 <c>_players</c> 只有自己（<c>id == _network.NetId</c>）⇒ 从不触碰 NetHost ⇒ 无异常。
///
/// 本后缀在「NetHost 为 null」时把参与者收缩为网络服务自身的 NetId —— 即唯一的真实进程。
/// 这样既不触发 NRE，也不会让问候动画去等一个永远不会回 Ready/Done 的第二席位
/// （本地回环的 SendMessage 不会真的投递消息给第二席位）。真实联机（NetHost != null）时完全不动。
/// </summary>
public static class BossGreetingParticipantsPatch
{
    /// <summary>由 Entry 在打补丁时注入（BossGreetingSync 是 internal，无法编译期引用）。</summary>
    internal static FieldInfo? NetworkField;

    public static void Postfix(object __instance, ref IEnumerable<ulong> __result)
    {
        try
        {
            if (__instance == null || NetworkField == null)
            {
                return;
            }

            if (NetworkField.GetValue(__instance) is not INetGameService network)
            {
                return;
            }

            // 只有「声称是 Host 却没有网络主机」的假联机服务才需要兜底；真实联机保持原逻辑。
            if (network is not INetHostGameService host || host.NetHost != null)
            {
                return;
            }

            // 注意：不要枚举 __result —— 它是延迟 LINQ，枚举会重新执行那条会抛 NRE 的 lambda。
            __result = new ulong[] { network.NetId };
            Log.Info($"[NinjaSlayerBossGreetingFix] 本地回环（Host 但 NetHost=null）下收缩 Boss 问候参与者为网络服务自身: netId={network.NetId}");
        }
        catch (Exception ex)
        {
            // 兜底不能反过来把流程搞崩：失败就保留原值（最坏情况与不打补丁一致）。
            Log.Warn($"[NinjaSlayerBossGreetingFix] 收缩 Boss 问候参与者失败，保留原值: {ex.Message}");
        }
    }
}
