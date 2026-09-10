using System;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Patch;

// 多目标容器：类级裸 [HarmonyPatch] 让 PatchAll 处理本类，
// 具体目标由各方法级 [HarmonyPatch(...)] 指定（本 mod 坑 1：缺类级标记会被静默跳过）。
/// <summary>
/// 牌组类选牌入口 → 智能选牌场景的对应关系（r94）。
///
/// 背景：遗物「获得时触发选牌」的作用域（<see cref="LocalWakuuRelicEffectAutoChoice"/>）在压栈时
/// 还不知道这个遗物会触发"变化"还是"删除"，只能给一个兜底场景；
/// 但两者优先级表差别很大——Transform 表**硬排除**诅咒/状态/任务（变牌时留着它们也算合理），
/// 而 Remove 表恰恰**优先删诅咒**。smartPick 开启时用错表会明显变差。
/// 因此在这里按实际入口前缀写入场景，选择器作答时动态读取。
///
/// 只在入口前缀写值、不清理：该值是 AsyncLocal 且只有遗物作用域内的选择器会读它，
/// 作用域一结束选择器就出栈，残留值无消费方。
/// 注意不要给 FromDeckGeneric 加前缀——FromDeckForRemoval 内部就是转调它，
/// 加了会把 Remove 覆盖成别的值。
/// </summary>
[HarmonyPatch]
internal static class CardSelectDeckScenarioPatch
{
    /// <summary>「从牌组删一张」类效果（商店删牌服务 / 拾取时删除一张卡的遗物等）→ 用 Remove 优先级表。</summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForRemoval), new[]
    {
        typeof(Player),
        typeof(CardSelectorPrefs),
        typeof(Func<CardModel, bool>),
    })]
    [HarmonyPrefix]
    private static void FromDeckForRemovalPrefix()
    {
        LocalWakuuRelicEffectAutoChoice.DeckScenarioOverride.Value = WakuuPickScenario.Remove;
    }

    /// <summary>
    /// 「从牌组升级一张」→ 没有对应的优先级表，退回既有 cardPickMode 策略（Unknown），
    /// 避免拿 Transform 表去选升级目标（Transform 会硬排除诅咒/状态，升级场景无此语义）。
    /// </summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForUpgrade), new[]
    {
        typeof(Player),
        typeof(CardSelectorPrefs),
    })]
    [HarmonyPrefix]
    private static void FromDeckForUpgradePrefix()
    {
        LocalWakuuRelicEffectAutoChoice.DeckScenarioOverride.Value = WakuuPickScenario.Unknown;
    }
}
