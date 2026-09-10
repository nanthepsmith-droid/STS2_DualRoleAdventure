using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 第三方兼容（RitsuLib「次级资源」战斗UI，如 LexNinja2 的**蕾克拉**）：把「战斗UI归属玩家」
/// 强制为**当前前台（受控）玩家**（r102）。
///
/// 为什么必须拦这里：RitsuLib 的次级资源计数器把「本地玩家」等同于 `LocalContext.GetMe(...)`
/// （`SecondaryResourceCombatUiStateTracker.OnCombatStateChanged` 就是这么调
/// `SecondaryResourceUiRuntime.UpdateCombatUi(parent, LocalContext.GetMe(state))` 的），
/// 而本 mod 的 `LocalContext.NetId` 会为了「瓦库后台出牌的动作归属」临时漂移到瓦库身上
/// （见 `LocalMultiControlRuntime.AlignContextForActionOwner`）。
/// 于是瓦库（蕾忍）出牌期间 RitsuLib 会把计数器重绑到瓦库 → 蕾克拉显示；
/// 出牌结束上下文回到真人 → 下次状态变化时又隐藏 —— 正是实机看到的
/// 「进战斗后仍有蕾克拉，瓦库打完牌后自动消失」。
///
/// 修法：在 RitsuLib 的公共刷新入口上挂前缀，把归属玩家改成**前台玩家**
/// （`Session.CurrentControlledPlayerId`，即屏幕上正在显示的那位），
/// 让「次级资源 UI 跟随前台」成为唯一事实来源；取不到前台玩家时保持原样不动。
/// 第三方类型不存在（未装 RitsuLib）时 `Prepare()` 直接跳过，不影响 PatchAll。
/// </summary>
[HarmonyPatch]
internal static class SecondaryResourceCombatUiOwnerPatch
{
    private const string TargetSignature =
        "STS2RitsuLib.Combat.SecondaryResources.SecondaryResourceUiRuntime:UpdateCombatUi";

    /// <summary>日志去重（同一次「A→B」校正只报一次）。</summary>
    private static readonly HashSet<string> _loggedCorrections = new();

    private static readonly Harmony _lateHarmony = new("sts2.dualroleadventure.late.secondaryresource");

    private static MethodBase? _target;
    private static bool _applied;

    private static bool Prepare()
    {
        _target = AccessTools.Method(TargetSignature);
        if (_target == null)
        {
            LocalMultiControlLogger.Warn(
                "[次级资源归属] 未找到 RitsuLib 的 SecondaryResourceUiRuntime.UpdateCombatUi（未装 RitsuLib 或加载顺序较晚/改名），暂缓挂载。");
            return false;
        }

        _applied = true;
        LocalMultiControlLogger.Info("[次级资源归属] 已挂载 RitsuLib 次级资源战斗UI 归属校正前缀。");
        return true;
    }

    private static MethodBase? TargetMethod()
    {
        return _target;
    }

    /// <summary>
    /// RitsuLib 加载晚于本 mod 的 PatchAll 时（Prepare 找不到目标）在运行期补挂。
    /// 由 <see cref="LocalThirdPartySecondaryResourceBridge"/> 在首次用到次级资源UI时调用。
    /// </summary>
    internal static void TryApplyLate()
    {
        if (_applied)
        {
            return;
        }

        MethodBase? target = AccessTools.Method(TargetSignature);
        if (target == null)
        {
            return;
        }

        try
        {
            MethodInfo? prefix = AccessTools.Method(typeof(SecondaryResourceCombatUiOwnerPatch), nameof(Prefix));
            if (prefix == null)
            {
                return;
            }

            _lateHarmony.Patch(target, prefix: new HarmonyMethod(prefix));
            _applied = true;
            LocalMultiControlLogger.Info("[次级资源归属] 已延迟挂载 RitsuLib 次级资源战斗UI 归属校正前缀。");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[次级资源归属] 延迟挂载失败（已忽略）: {exception.Message}");
        }
    }

    /// <summary>
    /// 参数用位序 `__1`（= 原方法第二个参数 player）而不是参数名，避免上游改名导致补丁静默失效；
    /// 类型仍是 `Player`（可空注解不影响运行时类型）。
    /// </summary>
    private static void Prefix(ref Player? __1)
    {
        try
        {
            Player? foreground = LocalMultiControlRuntime.TryGetForegroundPlayer();
            if (foreground == null || __1 == null || __1.NetId == foreground.NetId)
            {
                return;
            }

            string key = $"{__1.NetId}->{foreground.NetId}";
            if (_loggedCorrections.Add(key))
            {
                LocalMultiControlLogger.Info(
                    $"次级资源战斗UI归属已按前台校正: {__1.NetId} -> {foreground.NetId}");
            }

            __1 = foreground;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[次级资源归属] 校正失败（已忽略）: {exception.Message}");
        }
    }
}
