using System;
using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 止损修复（r37）：拦截「非古明地恋玩家」抽到本我牌（ID_label）后的自动打出。
///
/// 根因（r35/r36 牌堆诊断 + Koishi 源码确认）：本地双人下 527（蕾忍）的抽牌堆/手牌里出现了
/// owner=527 的本我牌（UNCONSCIOUS_RESERVE / CONDITIONAL_TELEPORT 等，生成来源待查）。
/// Koishi 的 IdAfterCardDrawnPatch 在「任意玩家」抽到带 ID_label 的牌时都会自动打出并
/// `card.SpendResources()`（扣 owner 能量）——它不检查 owner 是否为本我状态/古明地恋，
/// 于是 527 抽到本我牌就自动打出、扣 527 能量、进 527 弃牌堆。
///
/// 修复：通过反射 patch Koishi 的 IdAfterCardDrawnPatch.ExecuteAfterDrawn（csproj 不引用 Koishi.dll，
/// 只能用字符串定位），在「非古明地恋角色」抽到本我牌时直接跳过自动打出逻辑（不扣能量、不打出，
/// 牌留在手牌）。古明地恋自己的本我牌自动打出不受影响。
/// 定位到「本我牌进 527 抽牌堆」的确切生成来源后，此止损可替换为根治方案。
///
/// r38 加固（修复 r37 导致的 mod 初始化崩溃）：Koishi.dll 可能在 PatchAll 时尚未加载，
/// TargetMethod() 返回 null 会让 Harmony 抛异常、中断整个 PatchAll（含读档自动就绪等补丁）。
/// 现在用 Prepare() 保护：找不到就跳过，并在进入本地多控（LocalLoopbackHostGameService 构造，
/// Koishi 必然已加载）时经 TryApplyLate() 延迟补挂。
/// </summary>
[HarmonyPatch]
internal static class IdAfterCardDrawnOwnerGuardPatch
{
    /// <summary>Koishi 的 ID_label 关键词运行值（缓存，反射失败退回日志实测值 -1018609373）。</summary>
    private static CardKeyword? _idLabelKeyword;

    /// <summary>PatchAll 时定位到的 ExecuteAfterDrawn（Prepare 与 TargetMethod 共用，保证非 null）。</summary>
    private static MethodBase? _koishiExecuteAfterDrawn;

    /// <summary>守卫是否已挂载（PatchAll 直接挂载或延迟补挂任一生效）。</summary>
    private static bool _applied;

    private static readonly Harmony _lateHarmony = new("sts2.dualroleadventure.late");

    /// <summary>Koishi 未加载时跳过整个补丁类，避免 PatchAll 抛异常。</summary>
    private static bool Prepare()
    {
        _koishiExecuteAfterDrawn = AccessTools.Method("Koishi.KoishiCode.Patch.IdAfterCardDrawnPatch:ExecuteAfterDrawn");
        if (_koishiExecuteAfterDrawn == null)
        {
            LocalMultiControlLogger.Warn("[本我牌守卫] PatchAll 时未找到 Koishi 的 IdAfterCardDrawnPatch（可能加载顺序较晚），守卫暂缓挂载，进入本地多控时将自动补挂。");
            return false;
        }

        _applied = true;
        return true;
    }

    private static MethodBase? TargetMethod()
    {
        return _koishiExecuteAfterDrawn;
    }

    /// <summary>
    /// 延迟补挂守卫（在 LocalLoopbackHostGameService 构造时调用，此时 Koishi 必然已加载）。
    /// 已挂载则跳过；未找到 Koishi 方法则静默返回，下次进入本地多控再试。
    /// </summary>
    public static void TryApplyLate()
    {
        if (_applied)
        {
            return;
        }

        MethodBase? method;
        try
        {
            method = AccessTools.Method("Koishi.KoishiCode.Patch.IdAfterCardDrawnPatch:ExecuteAfterDrawn");
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
            MethodInfo? prefix = typeof(IdAfterCardDrawnOwnerGuardPatch).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic);
            if (prefix == null)
            {
                return;
            }

            _lateHarmony.Patch(method, prefix: new HarmonyMethod(prefix));
            _applied = true;
            LocalMultiControlLogger.Info("[本我牌守卫] 已延迟挂载守卫补丁（Koishi 就绪）。");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[本我牌守卫] 延迟挂载失败: {exception.Message}");
        }
    }

    private static bool TryGetIdLabelKeyword(out CardKeyword keyword)
    {
        if (_idLabelKeyword.HasValue)
        {
            keyword = _idLabelKeyword.Value;
            return true;
        }

        try
        {
            FieldInfo? field = AccessTools.Field("Koishi.KoishiCode.CanonicalKeywords.KoishiKeywords:ID_label");
            if (field != null && field.GetValue(null) is CardKeyword idLabelValue)
            {
                keyword = idLabelValue;
                _idLabelKeyword = keyword;
                return true;
            }
        }
        catch
        {
            // 反射失败走日志实测值
        }

        keyword = (CardKeyword)(-1018609373);
        _idLabelKeyword = keyword;
        return true;
    }

    /// <summary>
    /// r39：默认关闭。实测「拦截自动打出」会让 AfterCardDrawn hook 链（MintyHooks.CardDrawHook 等）
    /// 因牌停留在手牌而 NRE，导致战斗循环死亡软卡死（28066）。改为在定位到
    /// 「本我牌进 527 抽牌堆」的确切生成来源后根治，本守卫届时删除。
    /// </summary>
    private static bool Enabled => false;

    [HarmonyPrefix]
    private static bool Prefix(CardModel card, ref Task __result)
    {
        if (!Enabled)
        {
            return true; // r39 起守卫关闭，不拦截
        }

        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return true;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return true;
        }

        if (card == null || card.Owner == null)
        {
            return true;
        }

        if (!TryGetIdLabelKeyword(out CardKeyword idLabel) || !card.Keywords.Contains(idLabel))
        {
            return true; // 非本我牌
        }

        Player? ownerPlayer = card.Owner;
        if (ownerPlayer.Character?.GetType().Name == "Koishi")
        {
            return true; // 古明地恋自己的本我牌正常自动打出
        }

        // 非古明地恋玩家（本地双人下的队友）抽到本我牌 → 跳过自动打出（不扣能量、不打出）
        LocalMultiControlLogger.Warn(
            $"[本我牌守卫] 拦截非古明地恋玩家的本我牌自动打出: card={card.Id.Entry}, owner={card.Owner.NetId}");
        __result = Task.CompletedTask; // 原方法是 async Task，拦截必须补已完成任务，避免调用方 await 到 null
        return false;
    }
}
