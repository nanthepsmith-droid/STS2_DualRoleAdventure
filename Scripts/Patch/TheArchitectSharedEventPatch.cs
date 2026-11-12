using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 本地多控会话内把【建筑师】(THE_ARCHITECT) 当作**共享事件**（2026-11-12，r349）。
///
/// <para>问题（用户 2026-11-12）：全瓦库局里建筑师是**非共享**事件 ⇒ 每个席位各有一份实例、
/// 各答各的 ⇒ ① 瓦库替每个席位随机作答，真人（前台）插不上手；
/// ② 同一轮里可能连续进入两个整幕、两次 <c>EnterAct</c> 竞态（2026-11-11 实机：明灭顶被新宿顶掉）。</para>
///
/// <para>为什么 shared 就够：瓦库的自动作答**明确跳过共享事件**
/// （<see cref="LocalWakuuEventAutoChoice"/> 里 <c>EventSynchronizer.IsShared</c> / <c>EventModel.IsShared</c> 直接 return），
/// 而 <c>EventSynchronizerPatch</c> 的「共享事件自动补齐投票」会把前台真人选的那一项填给所有席位、
/// 再触发一次结算 ⇒ 全票一致、结果**等于真人点的那个**，且只会追加一个幕。
/// 原版 <c>ChooseOptionForSharedEvent</c> 会对每个玩家实例执行同一 index 的选项，重复进入由
/// Act4Coordinator 侧的 <c>_transitionLaunched</c> 闸门兜住。</para>
///
/// <para>为什么这样改语义是对的：原版 <c>EventModel.IsShared</c> 的注释写着
/// 「**会切换到别的房间的事件必须是 shared**」—— 建筑师正是这类（选项会追加并进入整幕），
/// 只是它原本只在单人终局出现，vanilla 没给它 shared。</para>
///
/// <para>⚠ 只在我们自己的本地多控会话里改写：官方 标准/每日/自定义 入口、单人局、真联机全部保持原版。</para>
/// </summary>
[HarmonyPatch(typeof(EventModel), nameof(EventModel.IsShared), MethodType.Getter)]
internal static class TheArchitectSharedEventPatch
{
    private static bool _logged;

    [HarmonyPostfix]
    private static void Postfix(EventModel __instance, ref bool __result)
    {
        if (__result || !LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        if (__instance is not TheArchitect)
        {
            return;
        }

        RunState? runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null || runState.Players.Count < 2)
        {
            return;
        }

        __result = true;
        if (!_logged)
        {
            _logged = true;
            LocalMultiControlLogger.Info(
                "建筑师事件已按【共享事件】处理：瓦库不自动作答，由前台真人选择后自动补齐全员投票");
        }
    }
}
