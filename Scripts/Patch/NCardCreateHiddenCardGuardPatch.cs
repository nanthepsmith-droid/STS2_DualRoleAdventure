using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 与 Oddmelt mod 的兼容守卫：其 Gauge 系列隐藏输入卡（GaugeSummonActionCard /
/// GaugeUltimateActionCard / GaugeBurstActionCard）刻意不注册到任何卡池，仅作为数据占位
/// 存在于手中，Oddmelt 通过多个 GaugeCard*VisualGuard 补丁拦截其可视化。
/// 而本地多控的 RefreshCombatUiForControlledPlayer 会遍历 handPile.Cards 并直接调用
/// NCard.Create，绕过这些拦截，命中 CardModel.Pool 的 "is not in any card pool!" 异常
/// （InvalidProgramException）并回滚角色切换。
///
/// 本补丁在游戏 NCard.Create 入口统一兜底：凡是 Pool 无法解析（抛 InvalidProgramException）
/// 的卡一律返回 null。调用方（本 mod 的战斗 UI 刷新、幽灵手牌、游戏自身各预览路径）
/// 均已对 null 判空，因此这类卡会像 Oddmelt 设计的那样被静默跳过，不再破坏战斗 UI 刷新。
/// 未安装 Oddmelt 时普通卡的 Pool 均可正常解析，本补丁直接放行原始实现，零影响。
/// </summary>
[HarmonyPatch(typeof(NCard), nameof(NCard.Create))]
internal static class NCardCreateHiddenCardGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CardModel card, ref NCard? __result)
    {
        try
        {
            if (card == null)
            {
                return true;
            }

            _ = card.Pool;
            return true;
        }
        catch (InvalidProgramException)
        {
            __result = null;
            return false;
        }
    }
}
