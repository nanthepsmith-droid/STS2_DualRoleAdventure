using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 补丁域分组（维护性改进 2.3：PatchAll 分组隔离）。
///
/// 分组目标：单个补丁崩溃不再拖垮整个 mod 初始化——按域逐个 try-catch 应用，
/// 失败组打 Error 并跳过、其余组继续；Core（本地多控运行基座）失败即停
/// （不再应用后续补丁组，避免在错误基线上继续运行）。
/// </summary>
internal enum PatchDomain
{
    /// <summary>本地多控运行基座：网络回环/运行主控/同步器/归属钉住/选牌串行化与本地选牌判定。失败即停。</summary>
    Core,

    /// <summary>大厅/创建游戏/角色选择/地图。</summary>
    Lobby,

    /// <summary>战斗流程适配：回合切换/出牌弃牌/药水遗物效果/火堆房间流程。</summary>
    Combat,

    /// <summary>奖励流程。</summary>
    Rewards,

    /// <summary>瓦库托管（自动选择/自动出牌/自动领取/自动投掷）。</summary>
    Wakuu,

    /// <summary>纯 UI 表现层：节点显示/焦点/输入路由/按钮。</summary>
    Ui,

    /// <summary>第三方 mod 适配（反射字符串目标，如 Koishi 本我修复）。</summary>
    ThirdParty,
}

/// <summary>
/// 补丁类 → 域 的集中映射（KEY 为【顶层】补丁类简名 Type.Name）。
///
/// 嵌套补丁类（容器类内部，如 LocalPlayerLimitNetworkPatch 的网络回环嵌套类）
/// 通过 <see cref="ResolveFor"/> 向上沿 DeclaringType 继承容器的域，无需登记。
///
/// 新增补丁类必须在此登记；未登记类启动时打 Warn 并按「隔离组」兜底应用
/// （测试 PatchDomainMapTests 强制校验：所有 [HarmonyPatch] 类均可解析到域）。
/// </summary>
internal static class PatchDomainMap
{
    /// <summary>顶层补丁类简名 → 域。由影响分析（2026-08-31）逐个核对，勿随意改。</summary>
    internal static readonly IReadOnlyDictionary<string, PatchDomain> ByTypeName =
        new Dictionary<string, PatchDomain>
        {
            // ============ Core：本地多控运行基座（失败即停） ============
            // 网络回环 / 运行主控
            ["LocalPlayerLimitNetworkPatch"] = PatchDomain.Core,
            ["LocalMultiControlPatch"] = PatchDomain.Core,
            // 选牌串行化 / 归属钉住 / 本地选牌判定
            ["NPlayerHandSelectCardsSerializationPatch"] = PatchDomain.Core,
            ["CardSelectCmdPatch"] = PatchDomain.Core,
            ["CardSelectForegroundSwitchPatch"] = PatchDomain.Core,
            ["CardSelectCmdSelectorGuardPatch"] = PatchDomain.Core,
            ["CardSelectManualConfirmationPatch"] = PatchDomain.Core,
            ["CardPileAddForegroundContextPinPatch"] = PatchDomain.Core,
            ["CardTransformNetIdPinPatch"] = PatchDomain.Core,
            ["RelicSelectCmdPatch"] = PatchDomain.Core,
            ["RelicCmdObtainPatch"] = PatchDomain.Core,
            ["RelicCmdRemovePatch"] = PatchDomain.Core,
            ["PotionManualUseTargetPatch"] = PatchDomain.Core,
            // 同步器 / 镜像 / 选择上下文
            ["EventSynchronizerBeginEventPatch"] = PatchDomain.Core,
            ["EventSynchronizerPatch"] = PatchDomain.Core,
            ["EventSynchronizerChooseOptionForEventPatch"] = PatchDomain.Core,
            ["MapSelectionSynchronizerPatch"] = PatchDomain.Core,
            ["ActChangeSynchronizerPatch"] = PatchDomain.Core,
            ["RestSiteSynchronizerBeginRestSitePatch"] = PatchDomain.Core,
            ["RestSiteSynchronizerChooseLocalOptionPatch"] = PatchDomain.Core,
            ["TreasureRoomRelicSynchronizerPatch"] = PatchDomain.Core,
            ["TreasureRoomRelicSynchronizerBeginPatch"] = PatchDomain.Core,
            ["OneOffSynchronizerSpoilsMapPatch"] = PatchDomain.Core,
            ["RewardsSetSynchronizerSelectLocalRewardPatch"] = PatchDomain.Core,
            ["SynchronizationOwnershipLogPatch"] = PatchDomain.Core,
            ["RewardCardMirrorPatch"] = PatchDomain.Core,
            ["RewardPotionMirrorPatch"] = PatchDomain.Core,
            ["PlayerGainGoldMirrorPatch"] = PatchDomain.Core,
            ["PlayerLoseGoldMirrorPatch"] = PatchDomain.Core,
            ["PlayerChoiceSynchronizerPatch"] = PatchDomain.Core,
            ["GameActionPlayerChoiceContextPatch"] = PatchDomain.Core,
            ["HookPlayerChoiceContextPatch"] = PatchDomain.Core,
            ["HookPlayerChoiceContextLocalPatch"] = PatchDomain.Core,
            ["PlayerChoiceSynchronizerWaitForRemoteChoicePatch"] = PatchDomain.Core,
            ["HookEnqueueForegroundPatch"] = PatchDomain.Core,
            ["ActionQueueSynchronizerRequestEnqueueFailSafePatch"] = PatchDomain.Core,
            ["MoveToMapCoordRestSiteCompletionPatch"] = PatchDomain.Core, // 火堆后出发黑屏（同步补完）

            // ============ Lobby：大厅 / 创建 / 角色选择 / 地图 ============
            ["LoadRunLobbyPatch"] = PatchDomain.Lobby,
            ["RunLobbyPatch"] = PatchDomain.Lobby,
            ["StartRunLobbyPatch"] = PatchDomain.Lobby,
            ["StartRunLobbyReadyFixPatch"] = PatchDomain.Lobby,
            ["StartRunLobbySetReadyPatch"] = PatchDomain.Lobby,
            ["NCharacterSelectButtonOnPressPatch"] = PatchDomain.Lobby,
            ["NCharacterSelectButtonSelectPatch"] = PatchDomain.Lobby,
            ["NCharacterSelectLocalCountButtonsOpenPatch"] = PatchDomain.Lobby,
            ["NCharacterSelectLocalCountButtonsProcessPatch"] = PatchDomain.Lobby,
            ["NCharacterSelectLocalCountButtonsClosePatch"] = PatchDomain.Lobby,
            ["NCharacterSelectScreenOpenPatch"] = PatchDomain.Lobby,
            ["NCharacterSelectScreenPatch"] = PatchDomain.Lobby,
            ["NCharacterSelectScreenSelectCharacterPatch"] = PatchDomain.Lobby,
            ["NCustomRunEmbarkGuardPatch"] = PatchDomain.Lobby,
            ["NCustomRunLocalCountButtonsOpenPatch"] = PatchDomain.Lobby,
            ["NCustomRunLocalCountButtonsProcessPatch"] = PatchDomain.Lobby,
            ["NCustomRunLocalCountButtonsClosePatch"] = PatchDomain.Lobby,
            ["NCustomRunScreenLocalPlayersPatch"] = PatchDomain.Lobby,
            ["NCustomRunScreenLocalPlayersProcessPatch"] = PatchDomain.Lobby,
            ["NCustomRunSelectionSyncOpenPatch"] = PatchDomain.Lobby,
            ["NCustomRunSelectionSyncPlayerChangedPatch"] = PatchDomain.Lobby,
            ["NCustomRunSelectionSyncProcessPatch"] = PatchDomain.Lobby,
            ["NMultiplayerHostSubmenuCustomRunPatch"] = PatchDomain.Lobby,
            ["NMultiplayerHostSubmenuPatch"] = PatchDomain.Lobby,
            ["NMultiplayerLoadGameScreenPatch"] = PatchDomain.Lobby,
            ["NMultiplayerSubmenuHostRoutePatch"] = PatchDomain.Lobby,
            ["NMultiplayerSubmenuPatch"] = PatchDomain.Lobby,
            ["NRemoteLobbyPlayerReadyPatch"] = PatchDomain.Lobby,

            // ============ Combat：战斗流程 ============
            ["CardManualPlayContextPatch"] = PatchDomain.Combat,
            ["NCardPlayQueueOnActionEnqueuedFailSafePatch"] = PatchDomain.Combat,
            ["CombatManagerPatch"] = PatchDomain.Combat,
            ["CombatManagerReadyEnemyTurnPatch"] = PatchDomain.Combat,
            ["CombatManagerSetupPlayerTurnForegroundPatch"] = PatchDomain.Combat,
            ["CombatManagerDoTurnEndForegroundPatch"] = PatchDomain.Combat,
            ["CombatManagerFlushPlayerHandForegroundPatch"] = PatchDomain.Combat,
            ["CreatureCmdKillWinCheckPatch"] = PatchDomain.Combat,
            ["CrystalSpherePaymentPlanPatch"] = PatchDomain.Combat,
            ["CrystalSphereUncoverFuturePatch"] = PatchDomain.Combat,
            ["CrystalSphereMinigameProceedGuardPatch"] = PatchDomain.Combat,
            ["EndPlayerTurnActionPatch"] = PatchDomain.Combat,
            ["EntropyPowerPatch"] = PatchDomain.Combat,
            ["FoulPotionMerchantTargetPatch"] = PatchDomain.Combat,
            ["FoulPotionOnUsePatch"] = PatchDomain.Combat,
            ["LavaRockPatch"] = PatchDomain.Combat,
            ["NCombatRoomPatch"] = PatchDomain.Combat,
            ["NEndTurnButtonPatch"] = PatchDomain.Combat,
            ["PaelsWingPatch"] = PatchDomain.Combat,
            ["SpoilsMapPatch"] = PatchDomain.Combat,
            ["StaleCombatActionJanitorTriggerPatch"] = PatchDomain.Combat,
            ["ThievingHopperPatch"] = PatchDomain.Combat,
            ["ToolboxPatch"] = PatchDomain.Combat,
            ["UsePotionActionWatchdogPatch"] = PatchDomain.Combat,
            // 火堆流程（选项 / 守卫）
            ["RestSiteOptionPatch"] = PatchDomain.Combat,
            ["HealRestSiteOptionPatch"] = PatchDomain.Combat,
            ["NRestSiteRoomAfterSelectingOptionPatch"] = PatchDomain.Combat,
            ["NRestSiteRoomHoverGuardPatch"] = PatchDomain.Combat,
            ["NRestSiteButtonSelectGuardPatch"] = PatchDomain.Combat,
            ["NRestSiteRoomReadyPatch"] = PatchDomain.Combat,

            // ============ Rewards：奖励流程 ============
            ["CardRewardPatch"] = PatchDomain.Rewards,
            ["CombatRoomOfferRoomEndRewardsPatch"] = PatchDomain.Rewards,
            ["NCardRewardSelectionScreenAutoClaimPatch"] = PatchDomain.Rewards,
            ["NRewardButtonLabelPatch"] = PatchDomain.Rewards,
            ["NRewardButtonMergedRewardSelectPatch"] = PatchDomain.Rewards,
            ["RewardsCmdOfferCustomPatch"] = PatchDomain.Rewards,
            ["RewardsCmdPatch"] = PatchDomain.Rewards,
            ["RewardsSetPatch"] = PatchDomain.Rewards,

            // ============ Wakuu：瓦库托管 ============
            ["CardSelectHandScenarioPatch"] = PatchDomain.Wakuu,
            ["CardSelectWakuuTurnStartAutoAnswerPatch"] = PatchDomain.Wakuu,
            ["MerchantRoomEnterFoulThrowPatch"] = PatchDomain.Wakuu,
            ["PotionProcuredAutoDrinkPatch"] = PatchDomain.Wakuu,
            ["WakuuEventEnchantAutoAnswerPatch"] = PatchDomain.Wakuu,
            ["WhisperingEarringPatch"] = PatchDomain.Wakuu,

            // ============ Ui：纯 UI 表现层 ============
            ["CardPileHandVisualOwnerGuardPatch"] = PatchDomain.Ui,
            ["NCombatRoomGhostHandsPatch"] = PatchDomain.Ui,
            ["GhostHandsHotkeysPatch"] = PatchDomain.Ui,
            ["NCardCreateHiddenCardGuardPatch"] = PatchDomain.Ui,
            ["NCombatCardPilePatch"] = PatchDomain.Ui,
            ["NCombatUiReadyPatch"] = PatchDomain.Ui,
            ["NCombatUiActivatePatch"] = PatchDomain.Ui,
            ["NCombatUiDeactivatePatch"] = PatchDomain.Ui,
            ["NCombatUiEnablePatch"] = PatchDomain.Ui,
            ["NCombatUiDisablePatch"] = PatchDomain.Ui,
            ["NCombatUiExitPatch"] = PatchDomain.Ui,
            ["NEndTurnButtonLifecyclePatch"] = PatchDomain.Ui,
            ["NEventRoomPatch"] = PatchDomain.Ui,
            ["NEventRoomOptionButtonPatch"] = PatchDomain.Ui,
            ["NGameInputPatch"] = PatchDomain.Ui,
            ["NHandImageCollectionUpdateVisibilityPatch"] = PatchDomain.Ui,
            ["NInputManagerPatch"] = PatchDomain.Ui,
            ["NMerchantInventoryPatch"] = PatchDomain.Ui,
            ["NMultiplayerPlayerIntentHandlerPatch"] = PatchDomain.Ui,
            ["NMultiplayerPlayerStateReadyPatch"] = PatchDomain.Ui,
            ["NOverlayStackPatch"] = PatchDomain.Ui,
            ["NPauseMenuRestartRoomPatch"] = PatchDomain.Ui,
            ["NPlayerHandAddOwnerGuardPatch"] = PatchDomain.Ui,
            ["NPotionContainerPatch"] = PatchDomain.Ui,
            ["NRelicInventoryPatch"] = PatchDomain.Ui,
            ["NRemoteMouseCursorContainerPatch"] = PatchDomain.Ui,
            ["NRestSiteCharacterBubbleDiagnosticPatch"] = PatchDomain.Ui,
            ["NSettingsScreenConfigMenuPatch"] = PatchDomain.Ui,
            ["MainMenuStackRegisterWakuuConfigSubmenuPatch"] = PatchDomain.Ui,
            ["RunStackRegisterWakuuConfigSubmenuPatch"] = PatchDomain.Ui,
            ["PlatformUtilGetPlayerNamePatch"] = PatchDomain.Ui,
            ["NRestSiteRoomReadyGuardPatch"] = PatchDomain.Ui,
            ["NTreasureRoomRelicCollectionFocusGuardPatch"] = PatchDomain.Ui,
            ["NTreasureRoomRelicHolderFocusGuardPatch"] = PatchDomain.Ui,

            // ============ ThirdParty：第三方 mod 适配（反射字符串目标） ============
            ["IdAfterCardDrawnOwnerGuardPatch"] = PatchDomain.ThirdParty,
            ["IdLiberationBeforeHandDrawFixPatch"] = PatchDomain.ThirdParty,
        };

    /// <summary>
    /// 分组容错开关（回滚预案，实施方案 2.3）：true=分组 ApplyPatches（默认，Core 失败即停、其余组降级）；
    /// false=整体关闭分组容错，回到旧 PatchAll 直跑（单补丁失败仍会中断后续全部补丁）。
    /// 修改后重新构建部署即可。
    /// </summary>
    internal static readonly bool UseGroupedPatchAll = true;

    /// <summary>组应用顺序：Core 最先（失败即停），其余按故障域稳定顺序。</summary>
    internal static readonly PatchDomain[] ApplyOrder =
    {
        PatchDomain.Core,
        PatchDomain.Lobby,
        PatchDomain.Combat,
        PatchDomain.Rewards,
        PatchDomain.Wakuu,
        PatchDomain.Ui,
        PatchDomain.ThirdParty,
    };

    /// <summary>
    /// 解析补丁类型的域：自身未登记时沿 DeclaringType 向上继承容器域；均未命中返回 null。
    /// 容器类（如 LocalPlayerLimitNetworkPatch）登记一次，其嵌套补丁类自动继承。
    /// </summary>
    internal static PatchDomain? ResolveFor(Type patchType)
    {
        for (Type? current = patchType; current != null; current = current.DeclaringType)
        {
            if (ByTypeName.TryGetValue(current.Name, out PatchDomain domain))
            {
                return domain;
            }
        }

        return null;
    }
}
