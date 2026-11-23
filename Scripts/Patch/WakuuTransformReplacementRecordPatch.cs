using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Random;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 变化牌登记（2026-11-23，TODO §瓦库托管：变化牌处置 —— 「变成什么」那一半）。
///
/// <para><b>口径</b>（用户 2026-11-11 拍板）：变化成**指定的一张牌**（如储君仆从 <c>MINION_*</c>）时，
/// 这张牌"约等于已经被消耗" ⇒ 出牌侧**直接套用消耗牌的既有优先级**（评分 <c>-5</c>）；
/// 变化成**同角色随机牌**则维持原有处理、不打任何标记。判据 = <c>CardTransformation.Replacement != null</c>
/// （随机变化走 <c>CardFactory.CreateRandomCardForTransform</c>，候选稀有度过滤在 Common/Uncommon/Rare
/// ⇒ 随机变化永远不会产出 <c>CardRarity.Token</c>）。</para>
///
/// <para><b>挂点选型</b>：只挂在 <c>CardCmd.Transform</c> 的前缀上，直接登记
/// <c>transformation.Replacement</c> 这个实例 —— 原版在数据阶段原样把它放回原牌堆
/// （<c>GetReplacement</c> 对 <c>Replacement != null</c> 就是 return 它），所以"手牌/出牌堆里的牌"
/// 与登记实例**是同一个对象**，按引用查得到。这样写的好处是**与游戏小版本无关**：
/// 0.113 的 <c>CardModel.AfterThisWasTransformedFrom</c> / 0.111 的 <c>AfterTransformedTo()</c> 是两个不同 API，
/// 而这里一个都不用碰。</para>
///
/// <para><b>为什么要把入参物化一次</b>：原版 <c>Transform</c> 内部本来就会 <c>ToArray()</c>。若我们只"读一遍"
/// 再交回原序列，碰上有副作用的懒序列（Pandora's Box 那种
/// <c>Select(c =&gt; new CardTransformation(c, CardFactory.CreateRandomCardForTransform(...)))</c>）就会被枚举两次
/// ⇒ **多消耗一份 RNG、多造一张牌**。所以前缀把它 <c>ToArray()</c> 后**把数组交回给原版**。</para>
///
/// <para><b>已知不覆盖</b>：牌组里的变化会先过 <c>Hook.ModifyCardBeingAddedToDeck</c>，蛋类遗物
/// （Molten/Frozen/Toxic Egg）在那里把牌 <c>CloneCard</c> 成升级版 ⇒ 进牌组的不是登记的那个实例，
/// 这张牌不会被标成"按消耗处理"（= 维持旧行为，不是错标）。要覆盖它只能再挂"最终实例"那条 API，
/// 而那条 API 在两个小版本里名字不同 ⇒ 本版刻意不碰（保双版本同一份源码）。</para>
/// </summary>
[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.Transform), new[]
{
    typeof(IEnumerable<CardTransformation>),
    typeof(Rng),
    typeof(CardPreviewStyle),
})]
internal static class WakuuTransformReplacementRecordPatch
{
    private static int _warnCount;

    [HarmonyPrefix]
    private static void Prefix(ref IEnumerable<CardTransformation>? transformations)
    {
        try
        {
            if (transformations == null)
            {
                return;
            }

            // 只物化一次并把数组交回原版（见类注释：防止懒序列被枚举两次）。
            CardTransformation[] batch = transformations.ToArray();
            transformations = batch;

            if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
            {
                return;
            }

            foreach (CardTransformation transformation in batch)
            {
                // Replacement != null ⇔ 「变化成指定的一张牌」；ReplacementOptions / 无参构造都是随机变化。
                if (transformation.Replacement == null)
                {
                    continue;
                }

                WakuuTransformReplacementRegistry.TryRegister(transformation.Replacement);
            }
        }
        catch (Exception exception)
        {
            WarnOnce(exception);
        }
    }

    private static void WarnOnce(Exception exception)
    {
        if (_warnCount >= 3)
        {
            return;
        }

        _warnCount++;
        LocalMultiControlLogger.Warn(
            "[变化牌] 登记变化牌时异常（已吞掉，不影响变换本身）: " + $"{exception.GetType().Name}: {exception.Message}");
    }
}
