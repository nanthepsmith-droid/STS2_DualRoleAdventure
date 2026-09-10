using System;
using System.Threading;
using LocalMultiControl.Scripts.Patch;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 遗物「获得时触发选牌」的瓦库自动作答作用域（r94，修 r81 覆盖不全）。
///
/// 背景：r81 只在 <see cref="LocalWakuuRewardAutoClaim"/> 的 RelicReward 自动领取分支压入策略选择器，
/// 而**控制台授予 / 事件授予 / 商店购买 / 第三方效果授予**都不走 RelicReward ——
/// 于是「获得遗物时把一张卡变化」（如 YUI「灵草丹」RELIC.YUI_SPIRE_EXPANSION_RELIC_LING_CAO_DAN）这类
/// 效果走 CardSelectCmd.FromDeckForTransformation 时栈上没有选择器，又被
/// CardSelectManualConfirmationPatch 强制 RequireManualConfirmation，最终弹牌组界面停住等真人。
///
/// 修法：下沉到 RelicCmd.Obtain（所有获得遗物路径的公共入口）。获得者是瓦库托管角色时，
/// 在整个 Obtain（含 relic.AfterObtained）期间压入 Transform 场景的策略选择器。
/// 原版 FromDeckForTransformation 的分支顺序是「Selector != null」优先于 RequireManualConfirmation，
/// 所以压栈后直接自动作答、不弹屏。
///
/// 两个时序要点（踩过即失效）：
/// 1. 必须在 **前缀** 压栈：RelicCmd.Obtain 是 async 方法，但 `await relic.AfterObtained()` 里
///    AfterObtained() 的**调用**（含其内部同步跑到 FromDeckForTransformation 读取 Selector）
///    发生在 Obtain 返回 Task 之前，后缀（首个 await 之后才跑）会晚一步、完全兜不住。
/// 2. 选择器栈是 CardSelectCmd 的**静态 Stack**（不是 AsyncLocal），所以前缀压栈对整条异步链都有效；
///    但 CardSelectCmdSelectorGuardPatch 依据 AsyncLocal 的 CurrentChoicePlayerId 判断"谁在选"，
///    该值会沿异步链残留（可能是上一场战斗里真人的 NetId）从而把我们的选择器摘掉，
///    因此进入作用域时同步写入归属者 NetId。
/// </summary>
internal static class LocalWakuuRelicEffectAutoChoice
{
    /// <summary>前缀压入、后缀取走的选择器作用域（前缀与后缀在同一同步帧，AsyncLocal 可靠）。</summary>
    private static readonly AsyncLocal<IDisposable?> PendingScope = new();

    /// <summary>
    /// 本次遗物效果实际走到的选牌入口对应的场景（由 CardSelectDeckScenarioPatch 的各 From* 前缀写入）。
    /// 压栈时还不知道遗物会触发"变化"还是"删除"，只能到入口才知道——两者优先级表不同
    /// （Transform 硬排除诅咒/状态/任务；Remove 恰恰优先删诅咒），答错场景会明显变差。
    /// </summary>
    internal static readonly AsyncLocal<WakuuPickScenario?> DeckScenarioOverride = new();

    /// <summary>
    /// 进入遗物获取作用域：获得者是瓦库托管角色时压入策略选择器。必须在 RelicCmd.Obtain 的前缀调用。
    /// </summary>
    internal static void Enter(Player? player)
    {
        if (player == null)
        {
            return;
        }

        if (!ShouldAutoAnswer(
                LocalSelfCoopContext.IsEnabled,
                LocalSelfCoopContext.UseSingleAdventureMode,
                LocalWakuuRelicRuntime.IsVakuuFormMode(player),
                IsCombatInProgress()))
        {
            return;
        }

        // 写入选牌归属者，避免 CardSelectCmdSelectorGuardPatch 按异步链上残留的旧归属者
        // （多半是真人）把本次选择器摘掉。
        CardSelectForegroundSwitchPatch.CurrentChoicePlayerId.Value = player.NetId;

        PendingScope.Value = CardSelectCmd.PushSelector(
            new LocalWakuuStrategySelector(WakuuPickScenario.Transform)
            {
                LogLabel = "遗物效果触发选牌",
                ScenarioProvider = () => DeckScenarioOverride.Value,
            });
    }

    /// <summary>取走前缀压入的作用域（由后缀调用，交给后续异步链在合适时机释放）。</summary>
    internal static IDisposable? TakePendingScope()
    {
        IDisposable? scope = PendingScope.Value;
        PendingScope.Value = null;
        return scope;
    }

    /// <summary>异常兜底释放（Finalizer 调用，防止 RelicCmd.Obtain 同步抛异常时泄漏选择器）。</summary>
    internal static void DisposePendingScope()
    {
        TakePendingScope()?.Dispose();
    }

    /// <summary>
    /// 是否应该为「遗物获得时触发的选牌」自动作答（纯逻辑，便于单测）。
    /// 战斗内不接：① 战斗期另有瓦库出牌循环自己的选择器作用域；
    /// ② 避开「开局遗物二选一」等依赖真人处理、实测自动作答会导致流程异常的战斗期选牌。
    /// </summary>
    internal static bool ShouldAutoAnswer(
        bool localSelfCoopEnabled,
        bool useSingleAdventureMode,
        bool isVakuuForm,
        bool combatInProgress)
    {
        return localSelfCoopEnabled
            && useSingleAdventureMode
            && isVakuuForm
            && !combatInProgress;
    }

    /// <summary>
    /// 场景解算（纯逻辑，便于单测）：入口覆盖优先，无覆盖时用入口无关的兜底场景。
    /// 覆盖值允许为 <see cref="WakuuPickScenario.Unknown"/>（表示退回 cardPickMode），
    /// 因此不能用 null 合并以外的写法把 Unknown 当"未设置"吃掉。
    /// </summary>
    internal static WakuuPickScenario ResolveScenario(WakuuPickScenario? entryOverride, WakuuPickScenario fallback)
    {
        return entryOverride ?? fallback;
    }

    private static bool IsCombatInProgress()
    {
        try
        {
            return CombatManager.Instance?.IsInProgress == true;
        }
        catch
        {
            // 无战斗实例 / 访问异常一律按「非战斗」处理：宁可多兜一次，也不要吞掉真实错误。
            return false;
        }
    }
}
