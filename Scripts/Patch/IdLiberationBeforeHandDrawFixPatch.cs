using System;
using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 根治修复（r40）：本我牌「随机出现在其他玩家抽牌堆」的根源。
///
/// 根因（r39 牌堆诊断 + Koishi 反编译确认）：
///   游戏 Hook.BeforeHandDraw 会遍历「所有玩家的所有力量/遗物」，把「当前抽牌的玩家」作为
///   player 参数传入。Koishi 的 IdLiberationPower.BeforeHandDraw 不检查 player 是否等于力量主人，
///   直接用 player 参数 CreateCard + 塞进 player 抽牌堆。
///   于是：526（古明地恋）持有本我解放力量时，527（蕾忍）回合开始抽牌会触发 526 的力量，
///   给 527 生成一张 owner=527 的本我牌（CONDITIONAL_TELEPORT/UNCONSCIOUS_RESERVE 等），
///   527 抽到后触发本我自动打出、扣 527 能量、进 527 弃牌堆。
///   单人模式只有 1 个玩家（player 恒为力量主人）所以永不触发；本地双人（2 玩家）暴露。
///
/// 修复：patch Koishi 的 IdLiberationPower.BeforeHandDraw（反射字符串定位，csproj 不引用 Koishi.dll），
/// 当「钩子传入的抽牌玩家」不是「力量主人」时直接跳过（不生成）。力量主人的正常生成不受影响。
/// </summary>
[HarmonyPatch]
internal static class IdLiberationBeforeHandDrawFixPatch
{
    private static MethodBase? _target;
    private static bool _applied;

    private static readonly Harmony _lateHarmony = new("sts2.dualroleadventure.late.idlib");

    private static bool Prepare()
    {
        _target = AccessTools.Method("Koishi.KoishiCode.Powers.IdLiberationPower:BeforeHandDraw");
        if (_target == null)
        {
            LocalMultiControlLogger.Warn("[本我解放修复] PatchAll 时未找到 IdLiberationPower.BeforeHandDraw，进入本地多控时自动补挂。");
            return false;
        }

        _applied = true;
        return true;
    }

    private static MethodBase? TargetMethod()
    {
        return _target;
    }

    /// <summary>延迟补挂（进入本地多控时调用，Koishi 必然已加载）。</summary>
    public static void TryApplyLate()
    {
        if (_applied)
        {
            return;
        }

        MethodBase? method;
        try
        {
            method = AccessTools.Method("Koishi.KoishiCode.Powers.IdLiberationPower:BeforeHandDraw");
        }
        catch
        {
            return;
        }

        if (method == null)
        {
            return;
        }

        try
        {
            MethodInfo? prefix = typeof(IdLiberationBeforeHandDrawFixPatch).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
            {
                return;
            }

            _lateHarmony.Patch(method, prefix: new HarmonyMethod(prefix));
            _applied = true;
            LocalMultiControlLogger.Info("[本我解放修复] 已延迟挂载（Koishi 就绪）。");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[本我解放修复] 延迟挂载失败: {exception.Message}");
        }
    }

    [HarmonyPrefix]
    private static bool Prefix(PowerModel __instance, Player player, ref Task __result)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return true;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return true;
        }

        if (__instance == null)
        {
            return true;
        }

        Creature? powerOwnerCreature = __instance.Owner;
        Player? ownerPlayer = powerOwnerCreature?.Player;
        if (ownerPlayer == null)
        {
            return true;
        }

        if (player == ownerPlayer)
        {
            return true; // 力量主人自己的回合，正常生成
        }

        // 本地双人：钩子因队友抽牌而触发 → 不给队友生成本我牌。
        // 注意：原方法是 async Task，拦截必须把 __result 设为已完成任务，
        // 否则调用方（GensokyoSpire.HookBeforeHandDrawPatch 等）await 到 null 会 NRE、回合循环死亡。
        LocalMultiControlLogger.Warn(
            $"[本我解放修复] 拦截非力量主人的本我牌生成: powerOwner={ownerPlayer.NetId}, handDrawPlayer={player.NetId}");
        __result = Task.CompletedTask;
        return false;
    }
}
