# Patch Coverage — 补丁目标覆盖清单

> 生成时间：2026-08-29 20:02
> 扫描目录：`Scripts/Patch/`（96 个文件，148 个补丁类）
> 反编译参考：D:\Download\pain\sts2src\src（3539 个 .cs）
> 用途：游戏更新适配与「静默跳过」排查的核对基线。由 `Scripts/Tools/patch_coverage.py` 生成，勿手改。

## 统计

- 补丁类总数：**148**
- 目标行总数：**166**
- 状态分布：verified **157** / WARN-METHOD-ONLY **8** / container **1**

## ⚠️ 方法级-only 补丁类（PatchAll 会静默跳过）

以下类的 `[HarmonyPatch]` 只写在方法上、类上没有类级标记，**`PatchAll` 不会应用它们**，
补丁永不触发且无任何报错（本 mod 坑 1，已踩过 `adae5aa` 与 `CardSelectManualConfirmationPatch` 两次）。

| 补丁类 | 文件 |
|---|---|
| `CardSelectManualConfirmationPatch` | 见下方目标行 |
| `NEndTurnButtonLifecyclePatch` | 见下方目标行 |

## 覆盖清单

| # | 补丁类 | 目标类型 | 目标方法 | 字符串目标 | 状态 | 反编译锚点 |
|---|---|---|---|---|---|---|
| 1 | `ActChangeSynchronizerPatch` | `ActChangeSynchronizer` | `SetLocalPlayerReady` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\ActChangeSynchronizer.cs (L37)` |
| 2 | `NCardPlayQueueOnActionEnqueuedFailSafePatch` | `NCardPlayQueue` | `OnActionEnqueued` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Combat\NCardPlayQueue.cs (L92)` |
| 3 | `ActionQueueSynchronizerRequestEnqueueFailSafePatch` | `ActionQueueSynchronizer` | `RequestEnqueue` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\GameActions\Multiplayer\ActionQueueSynchronizer.cs (L124)` |
| 4 | `CardManualPlayContextPatch` | `CardModel` | `EnqueueManualPlay` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Models\CardModel.cs (L1791)` |
| 5 | `CardPileAddForegroundContextPinPatch` | `CardPileCmd` | `Add` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\CardPileCmd.cs (L235)` |
| 6 | `CardPileHandVisualOwnerGuardPatch` | `CardPileCmd` | `CreateCardNodeAndUpdateVisuals` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\CardPileCmd.cs (L865)` |
| 7 | `CardRewardPatch` | `CardReward` | `OnSelect` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Rewards\CardReward.cs (L182)` |
| 8 | `CardSelectCmdPatch` | `CardSelectCmd` | `ShouldSelectLocalCard` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\CardSelectCmd.cs (L215)` |
| 9 | `CardSelectForegroundSwitchPatch` | `CardSelectCmd` | `FromHand` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\CardSelectCmd.cs (L817)` |
| 10 | `CardSelectForegroundSwitchPatch` | `CardSelectCmd` | `FromHandForDiscard` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\CardSelectCmd.cs (L883)` |
| 11 | `CardSelectForegroundSwitchPatch` | `CardSelectCmd` | `FromHandForUpgrade` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\CardSelectCmd.cs (L908)` |
| 12 | `CardSelectForegroundSwitchPatch` | `CardSelectCmd` | `FromSimpleGrid` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\CardSelectCmd.cs (L388)` |
| 13 | `CardSelectForegroundSwitchPatch` | `CardSelectCmd` | `FromChooseACardScreen` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\CardSelectCmd.cs (L252)` |
| 14 | `CardSelectForegroundSwitchPatch` | `CardSelectCmd` | `FromCombatPile` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\CardSelectCmd.cs (L443)` |
| 15 | `CardSelectForegroundSwitchPatch` | `CardSelectCmd` | `FromCombatPile` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\CardSelectCmd.cs (L443)` |
| 16 | `CardSelectCmdSelectorGuardPatch` | `CardSelectCmd` | `Selector` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\CardSelectCmd.cs (L128)` |
| 17 | `CardSelectManualConfirmationPatch` | `CardSelectCmd` | `FromDeckForUpgrade` | nameof | **WARN-METHOD-ONLY** 方法级-only(会被 PatchAll 静默跳过) | — |
| 18 | `CardSelectManualConfirmationPatch` | `CardSelectCmd` | `FromDeckForTransformation` | nameof | **WARN-METHOD-ONLY** 方法级-only(会被 PatchAll 静默跳过) | — |
| 19 | `CardSelectManualConfirmationPatch` | `CardSelectCmd` | `FromDeckGeneric` | nameof | **WARN-METHOD-ONLY** 方法级-only(会被 PatchAll 静默跳过) | — |
| 20 | `CardSelectWakuuTurnStartAutoAnswerPatch` | `CardSelectCmd` | `FromChooseACardScreen` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\CardSelectCmd.cs (L252)` |
| 21 | `CombatManagerPatch` | `CombatManager` | `SetReadyToEndTurn` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Combat\CombatManager.cs (L807)` |
| 22 | `CombatManagerReadyEnemyTurnPatch` | `CombatManager` | `SetReadyToBeginEnemyTurn` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Combat\CombatManager.cs (L1006)` |
| 23 | `CombatManagerSetupPlayerTurnForegroundPatch` | `CombatManager` | `SetupPlayerTurn` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Combat\CombatManager.cs (L880)` |
| 24 | `CombatManagerDoTurnEndForegroundPatch` | `CombatManager` | `DoTurnEnd` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Combat\CombatManager.cs (L1602)` |
| 25 | `CombatManagerFlushPlayerHandForegroundPatch` | `CombatManager` | `FlushPlayerHand` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Combat\CombatManager.cs (L1779)` |
| 26 | `CombatRoomOfferRoomEndRewardsPatch` | `CombatRoom` | `OfferRoomEndRewards` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Rooms\CombatRoom.cs (L253)` |
| 27 | `CreatureCmdKillWinCheckPatch` | `CreatureCmd` | `Kill` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\CreatureCmd.cs (L431)` |
| 28 | `CrystalSpherePaymentPlanPatch` | `CrystalSphere` | `PaymentPlan` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Models\Events\CrystalSphere.cs (L74)` |
| 29 | `CrystalSphereUncoverFuturePatch` | `CrystalSphere` | `UncoverFuture` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Models\Events\CrystalSphere.cs (L66)` |
| 30 | `CrystalSphereMinigameProceedGuardPatch` | `NCrystalSphereScreen` | `OnProceedButtonPressed` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Events\Custom\CrystalSphere\NCrystalSphereScreen.cs (L254)` |
| 31 | `EndPlayerTurnActionPatch` | `EndPlayerTurnAction` | `ExecuteAction` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\GameActions\EndPlayerTurnAction.cs (L34)` |
| 32 | `EntropyPowerPatch` | `EntropyPower` | `AfterPlayerTurnStart` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Models\Powers\EntropyPower.cs (L20)` |
| 33 | `EventSynchronizerBeginEventPatch` | `EventSynchronizer` | `BeginEvent` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\EventSynchronizer.cs (L98)` |
| 34 | `EventSynchronizerPatch` | `EventSynchronizer` | `ChooseLocalOption` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\EventSynchronizer.cs (L230)` |
| 35 | `EventSynchronizerChooseOptionForEventPatch` | `EventSynchronizer` | `ChooseOptionForEvent` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\EventSynchronizer.cs (L220)` |
| 36 | `FoulPotionMerchantTargetPatch` | `FoulPotion` | `GetFoulPotionMerchantTarget` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Models\Potions\FoulPotion.cs (L77)` |
| 37 | `FoulPotionOnUsePatch` | `FoulPotion` | `OnUse` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Models\Potions\FoulPotion.cs (L85)` |
| 38 | `NCombatRoomGhostHandsPatch` | `NCombatRoom` | `_Ready` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Rooms\NCombatRoom.cs (L151)` |
| 39 | `GhostHandsHotkeysPatch` | `NGame` | `_Input` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\NGame.cs (L809)` |
| 40 | `HookEnqueueForegroundPatch` | `ActionQueueSynchronizer` | `EnqueueHookAction` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\GameActions\Multiplayer\ActionQueueSynchronizer.cs (L208)` |
| 41 | `HookPlayerChoiceContextLocalPatch` | `HookPlayerChoiceContext` | — | — | **verified** | `D:\Download\pain\sts2src\src\Core\GameActions\Multiplayer\HookPlayerChoiceContext.cs` |
| 42 | `LavaRockPatch` | `LavaRock` | `TryModifyRewards` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Models\Relics\LavaRock.cs (L37)` |
| 43 | `LoadRunLobbyPatch` | `LoadRunLobby` | `SetReady` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\Lobby\LoadRunLobby.cs (L305)` |
| 44 | `LocalMultiControlPatch` | `RunManager` | `Launch` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Runs\RunManager.cs (L710)` |
| 45 | `LocalMultiControlPatch` | `RunManager` | `CleanUp` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Runs\RunManager.cs (L1569)` |
| 46 | `LocalMultiControlPatch` | `RunManager` | `CleanUp` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Runs\RunManager.cs (L1569)` |
| 47 | `LocalPlayerLimitNetworkPatch` | — | — | — | **container** 纯容器类（补丁在嵌套类中），正常 | — |
| 48 | `StartENetHostPatch` | `NetHostGameService` | `StartENetHost` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\NetHostGameService.cs (L65)` |
| 49 | `StartSteamHostPatch` | `NetHostGameService` | `StartSteamHost` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\NetHostGameService.cs (L70)` |
| 50 | `LobbyPlayerSerializePatch` | `StartRunLobbyPlayer` | `Serialize` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Entities\Multiplayer\StartRunLobbyPlayer.cs (L22)` |
| 51 | `LobbyPlayerDeserializePatch` | `StartRunLobbyPlayer` | `Deserialize` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Entities\Multiplayer\StartRunLobbyPlayer.cs (L33)` |
| 52 | `ClientLobbyJoinResponseSerializePatch` | `ClientLobbyJoinResponseMessage` | `Serialize` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Messages\Lobby\ClientLobbyJoinResponseMessage.cs (L50)` |
| 53 | `ClientLobbyJoinResponseDeserializePatch` | `ClientLobbyJoinResponseMessage` | `Deserialize` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Messages\Lobby\ClientLobbyJoinResponseMessage.cs (L71)` |
| 54 | `LobbyBeginRunSerializePatch` | `LobbyBeginRunMessage` | `Serialize` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Messages\Lobby\LobbyBeginRunMessage.cs (L31)` |
| 55 | `LobbyBeginRunDeserializePatch` | `LobbyBeginRunMessage` | `Deserialize` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Messages\Lobby\LobbyBeginRunMessage.cs (L43)` |
| 56 | `MapSelectionSynchronizerPatch` | `MapSelectionSynchronizer` | `PlayerVotedForMapCoord` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\MapSelectionSynchronizer.cs (L51)` |
| 57 | `MerchantRoomEnterFoulThrowPatch` | `MerchantRoom` | `EnterInternal` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Rooms\MerchantRoom.cs (L51)` |
| 58 | `MoveToMapCoordRestSiteCompletionPatch` | `MoveToMapCoordAction` | `ExecuteAction` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\GameActions\MoveToMapCoordAction.cs (L34)` |
| 59 | `NCardCreateHiddenCardGuardPatch` | `NCard` | `Create` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Cards\NCard.cs (L323)` |
| 60 | `NCardRewardSelectionScreenAutoClaimPatch` | `NCardRewardSelectionScreen` | `ShowScreen` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CardSelection\NCardRewardSelectionScreen.cs (L88)` |
| 61 | `NCharacterSelectButtonSelectPatch` | `NCharacterSelectButton` | `Select` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CharacterSelect\NCharacterSelectButton.cs (L285)` |
| 62 | `NCharacterSelectButtonOnPressPatch` | `NCharacterSelectButton` | `OnPress` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CharacterSelect\NCharacterSelectButton.cs (L174)` |
| 63 | `NCharacterSelectLocalCountButtonsOpenPatch` | `NCharacterSelectScreen` | `OnSubmenuOpened` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CharacterSelect\NCharacterSelectScreen.cs (L302)` |
| 64 | `NCharacterSelectLocalCountButtonsProcessPatch` | `NCharacterSelectScreen` | `_Process` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CharacterSelect\NCharacterSelectScreen.cs (L407)` |
| 65 | `NCharacterSelectLocalCountButtonsClosePatch` | `NCharacterSelectScreen` | `OnSubmenuClosed` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CharacterSelect\NCharacterSelectScreen.cs (L365)` |
| 66 | `NCharacterSelectScreenOpenPatch` | `NCharacterSelectScreen` | `OnSubmenuOpened` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CharacterSelect\NCharacterSelectScreen.cs (L302)` |
| 67 | `NCharacterSelectScreenSelectCharacterPatch` | `NCharacterSelectScreen` | `SelectCharacter` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CharacterSelect\NCharacterSelectScreen.cs (L545)` |
| 68 | `NCharacterSelectScreenPatch` | `NCharacterSelectScreen` | `PlayerChanged` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CharacterSelect\NCharacterSelectScreen.cs (L695)` |
| 69 | `NCombatCardPilePatch` | `NCombatCardPile` | `Initialize` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Combat\NCombatCardPile.cs (L101)` |
| 70 | `NCombatRoomPatch` | `NCombatRoom` | `OnCombatSetUp` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Rooms\NCombatRoom.cs (L232)` |
| 71 | `NCombatUiReadyPatch` | `NCombatUi` | `_Ready` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Combat\NCombatUi.cs (L99)` |
| 72 | `NCombatUiActivatePatch` | `NCombatUi` | `Activate` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Combat\NCombatUi.cs (L130)` |
| 73 | `NCombatUiEnablePatch` | `NCombatUi` | `Enable` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Combat\NCombatUi.cs (L326)` |
| 74 | `NCombatUiDisablePatch` | `NCombatUi` | `Disable` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Combat\NCombatUi.cs (L356)` |
| 75 | `NCombatUiDeactivatePatch` | `NCombatUi` | `Deactivate` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Combat\NCombatUi.cs (L150)` |
| 76 | `NCombatUiExitPatch` | `NCombatUi` | `_ExitTree` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Combat\NCombatUi.cs (L124)` |
| 77 | `NMultiplayerHostSubmenuCustomRunPatch` | `NMultiplayerHostSubmenu` | `StartHost` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\MainMenu\NMultiplayerHostSubmenu.cs (L82)` |
| 78 | `NCustomRunScreenLocalPlayersPatch` | `NCustomRunScreen` | `OnSubmenuOpened` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CustomRun\NCustomRunScreen.cs (L217)` |
| 79 | `NCustomRunScreenLocalPlayersProcessPatch` | `NCustomRunScreen` | `_Process` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CustomRun\NCustomRunScreen.cs (L330)` |
| 80 | `NCustomRunEmbarkGuardPatch` | `NCustomRunScreen` | `OnEmbarkPressed` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CustomRun\NCustomRunScreen.cs (L288)` |
| 81 | `NCustomRunLocalCountButtonsOpenPatch` | `NCustomRunScreen` | `OnSubmenuOpened` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CustomRun\NCustomRunScreen.cs (L217)` |
| 82 | `NCustomRunLocalCountButtonsProcessPatch` | `NCustomRunScreen` | `_Process` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CustomRun\NCustomRunScreen.cs (L330)` |
| 83 | `NCustomRunLocalCountButtonsClosePatch` | `NCustomRunScreen` | `OnSubmenuClosed` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CustomRun\NCustomRunScreen.cs (L248)` |
| 84 | `NCustomRunSelectionSyncOpenPatch` | `NCustomRunScreen` | `OnSubmenuOpened` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CustomRun\NCustomRunScreen.cs (L217)` |
| 85 | `NCustomRunSelectionSyncPlayerChangedPatch` | `NCustomRunScreen` | `PlayerChanged` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CustomRun\NCustomRunScreen.cs (L461)` |
| 86 | `NCustomRunSelectionSyncProcessPatch` | `NCustomRunScreen` | `_Process` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CustomRun\NCustomRunScreen.cs (L330)` |
| 87 | `NEndTurnButtonLifecyclePatch` | `NEndTurnButton` | `SetState` | 字符串 | **WARN-METHOD-ONLY** 方法级-only(会被 PatchAll 静默跳过) | — |
| 88 | `NEndTurnButtonLifecyclePatch` | `NEndTurnButton` | `OnTurnStarted` | 字符串 | **WARN-METHOD-ONLY** 方法级-only(会被 PatchAll 静默跳过) | — |
| 89 | `NEndTurnButtonLifecyclePatch` | `NEndTurnButton` | `OnAboutToSwitchToEnemyTurn` | 字符串 | **WARN-METHOD-ONLY** 方法级-only(会被 PatchAll 静默跳过) | — |
| 90 | `NEndTurnButtonLifecyclePatch` | `CombatManager` | `AfterAllPlayersReadyToBeginEnemyTurn` | 字符串 | **WARN-METHOD-ONLY** 方法级-only(会被 PatchAll 静默跳过) | — |
| 91 | `NEndTurnButtonLifecyclePatch` | `NCombatUi` | `Activate` | 字符串 | **WARN-METHOD-ONLY** 方法级-only(会被 PatchAll 静默跳过) | — |
| 92 | `NEndTurnButtonPatch` | `NEndTurnButton` | `CallReleaseLogic` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Combat\NEndTurnButton.cs (L321)` |
| 93 | `NEventRoomPatch` | `NEventRoom` | `RefreshEventState` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Rooms\NEventRoom.cs (L248)` |
| 94 | `NEventRoomOptionButtonPatch` | `NEventRoom` | `OptionButtonClicked` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Rooms\NEventRoom.cs (L209)` |
| 95 | `NGameInputPatch` | `NGame` | `_Input` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\NGame.cs (L809)` |
| 96 | `NHandImageCollectionUpdateVisibilityPatch` | `NHandImageCollection` | `UpdateHandVisibility` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\TreasureRoomRelic\NHandImageCollection.cs (L70)` |
| 97 | `NInputManagerPatch` | `NInputManager` | `_UnhandledInput` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\CommonUi\NInputManager.cs (L566)` |
| 98 | `NMerchantInventoryPatch` | `NMerchantInventory` | `Initialize` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\Shops\NMerchantInventory.cs (L124)` |
| 99 | `NMultiplayerHostSubmenuPatch` | `NMultiplayerHostSubmenu` | `_Ready` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\MainMenu\NMultiplayerHostSubmenu.cs (L49)` |
| 100 | `NMultiplayerLoadGameScreenPatch` | `NMultiplayerLoadGameScreen` | `ShouldAllowRunToBegin` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\CharacterSelect\NMultiplayerLoadGameScreen.cs (L194)` |
| 101 | `NMultiplayerPlayerIntentHandlerPatch` | `NMultiplayerPlayerIntentHandler` | `Create` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Multiplayer\NMultiplayerPlayerIntentHandler.cs (L76)` |
| 102 | `NMultiplayerPlayerStateReadyPatch` | `NMultiplayerPlayerState` | `_Ready` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Multiplayer\NMultiplayerPlayerState.cs (L112)` |
| 103 | `NMultiplayerSubmenuHostRoutePatch` | `NMultiplayerSubmenu` | `OnHostPressed` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\MainMenu\NMultiplayerSubmenu.cs (L156)` |
| 104 | `NMultiplayerSubmenuPatch` | `NMultiplayerSubmenu` | `StartLoad` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\MainMenu\NMultiplayerSubmenu.cs (L138)` |
| 105 | `NOverlayStackPatch` | `NOverlayStack` | `Remove` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\Overlays\NOverlayStack.cs (L108)` |
| 106 | `NPauseMenuRestartRoomPatch` | `NPauseMenu` | `_Ready` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\PauseMenu\NPauseMenu.cs (L64)` |
| 107 | `NPlayerHandSelectCardsSerializationPatch` | `NPlayerHand` | `SelectCards` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Combat\NPlayerHand.cs (L603)` |
| 108 | `NPotionContainerPatch` | `NPotionContainer` | `AnimatePotion` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Potions\NPotionContainer.cs (L181)` |
| 109 | `NRelicInventoryPatch` | `NRelicInventory` | `AnimateRelic` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Relics\NRelicInventory.cs (L174)` |
| 110 | `NRemoteLobbyPlayerReadyPatch` | `NRemoteLobbyPlayer` | `_Ready` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Multiplayer\NRemoteLobbyPlayer.cs (L73)` |
| 111 | `NRemoteMouseCursorContainerPatch` | `NRemoteMouseCursorContainer` | `GetCursorPosition` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Multiplayer\NRemoteMouseCursorContainer.cs (L85)` |
| 112 | `NRestSiteCharacterBubbleDiagnosticPatch` | `NRestSiteCharacter` | `ShowSelectedRestSiteOption` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\RestSite\NRestSiteCharacter.cs (L243)` |
| 113 | `NRestSiteCharacterBubbleDiagnosticPatch` | `NRestSiteCharacter` | `SetSelectingRestSiteOption` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\RestSite\NRestSiteCharacter.cs (L201)` |
| 114 | `NRewardButtonLabelPatch` | `NRewardButton` | `Reload` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Rewards\NRewardButton.cs (L74)` |
| 115 | `NRewardButtonMergedRewardSelectPatch` | `NRewardButton` | `GetReward` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Rewards\NRewardButton.cs (L104)` |
| 116 | `NSettingsScreenConfigMenuPatch` | `NSettingsScreen` | `_Ready` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\Settings\NSettingsScreen.cs (L53)` |
| 117 | `MainMenuStackRegisterWakuuConfigSubmenuPatch` | `NMainMenuSubmenuStack` | `GetSubmenuType` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\MainMenu\NMainMenuSubmenuStack.cs (L96)` |
| 118 | `RunStackRegisterWakuuConfigSubmenuPatch` | `NRunSubmenuStack` | `GetSubmenuType` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\NRunSubmenuStack.cs (L73)` |
| 119 | `OneOffSynchronizerSpoilsMapPatch` | `OneOffSynchronizer` | `DoLocalTreasureRoomRewards` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\OneOffSynchronizer.cs (L108)` |
| 120 | `PaelsWingPatch` | `PaelsWing` | `OnSacrifice` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Models\Relics\PaelsWing.cs (L82)` |
| 121 | `PlatformUtilGetPlayerNamePatch` | `PlatformUtil` | `GetPlayerName` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Platform\PlatformUtil.cs (L52)` |
| 122 | `PlayerChoiceSynchronizerPatch` | `PlayerChoiceSynchronizer` | `SyncLocalChoice` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\GameActions\Multiplayer\PlayerChoiceSynchronizer.cs (L90)` |
| 123 | `GameActionPlayerChoiceContextPatch` | `GameActionPlayerChoiceContext` | `SignalPlayerChoiceEnded` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\GameActions\Multiplayer\GameActionPlayerChoiceContext.cs (L48)` |
| 124 | `HookPlayerChoiceContextPatch` | `HookPlayerChoiceContext` | `SignalPlayerChoiceEnded` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\GameActions\Multiplayer\HookPlayerChoiceContext.cs (L203)` |
| 125 | `PlayerChoiceSynchronizerWaitForRemoteChoicePatch` | `PlayerChoiceSynchronizer` | `WaitForRemoteChoice` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\GameActions\Multiplayer\PlayerChoiceSynchronizer.cs (L114)` |
| 126 | `PlayerGainGoldMirrorPatch` | `PlayerCmd` | `GainGold` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\PlayerCmd.cs (L141)` |
| 127 | `PlayerLoseGoldMirrorPatch` | `PlayerCmd` | `LoseGold` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\PlayerCmd.cs (L178)` |
| 128 | `PotionManualUseTargetPatch` | `PotionModel` | `EnqueueManualUse` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Models\PotionModel.cs (L241)` |
| 129 | `PotionProcuredAutoDrinkPatch` | `PotionCmd` | `TryToProcure` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\PotionCmd.cs (L18)` |
| 130 | `RelicCmdObtainPatch` | `RelicCmd` | `Obtain` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\RelicCmd.cs (L35)` |
| 131 | `RelicCmdRemovePatch` | `RelicCmd` | `Remove` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\RelicCmd.cs (L61)` |
| 132 | `RelicSelectCmdPatch` | `RelicSelectCmd` | `ShouldSelectLocalRelic` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\RelicSelectCmd.cs (L18)` |
| 133 | `RestSiteOptionPatch` | `RestSiteOption` | `Generate` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Entities\RestSite\RestSiteOption.cs (L53)` |
| 134 | `HealRestSiteOptionPatch` | `HealRestSiteOption` | `OnSelect` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Entities\RestSite\HealRestSiteOption.cs (L69)` |
| 135 | `RestSiteSynchronizerChooseLocalOptionPatch` | `RestSiteSynchronizer` | `ChooseLocalOption` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\RestSiteSynchronizer.cs (L163)` |
| 136 | `NRestSiteRoomAfterSelectingOptionPatch` | `NRestSiteRoom` | `AfterSelectingOptionAsync` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Rooms\NRestSiteRoom.cs (L377)` |
| 137 | `NRestSiteRoomHoverGuardPatch` | `NRestSiteRoom` | `OnPlayerChangedHoveredRestSiteOption` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Rooms\NRestSiteRoom.cs (L315)` |
| 138 | `NRestSiteButtonSelectGuardPatch` | `NRestSiteButton` | `SelectOption` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\RestSite\NRestSiteButton.cs (L138)` |
| 139 | `NRestSiteRoomReadyPatch` | `NRestSiteRoom` | `_Ready` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Rooms\NRestSiteRoom.cs (L114)` |
| 140 | `RestSiteSynchronizerBeginRestSitePatch` | `RestSiteSynchronizer` | `BeginRestSite` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\RestSiteSynchronizer.cs (L89)` |
| 141 | `RewardCardMirrorPatch` | `RewardSynchronizer` | `SyncLocalObtainedCard` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\RewardSynchronizer.cs (L71)` |
| 142 | `RewardPotionMirrorPatch` | `RewardSynchronizer` | `SyncLocalObtainedPotion` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\RewardSynchronizer.cs (L123)` |
| 143 | `RewardsCmdOfferCustomPatch` | `RewardsCmd` | `OfferCustom` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\RewardsCmd.cs (L47)` |
| 144 | `RewardsCmdPatch` | `RewardsCmd` | `OfferForRoomEnd` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Commands\RewardsCmd.cs (L20)` |
| 145 | `RewardsSetPatch` | `RewardsSet` | `Offer` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Rewards\RewardsSet.cs (L153)` |
| 146 | `RewardsSetSynchronizerSelectLocalRewardPatch` | `RewardsSetSynchronizer` | `SelectLocalReward` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\RewardsSetSynchronizer.cs (L207)` |
| 147 | `NRestSiteRoomReadyGuardPatch` | `NRestSiteRoom` | `_Ready` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Rooms\NRestSiteRoom.cs (L114)` |
| 148 | `NTreasureRoomRelicCollectionFocusGuardPatch` | `NTreasureRoomRelicCollection` | `get_DefaultFocusedControl` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\TreasureRoomRelic\NTreasureRoomRelicCollection.cs (L61)` |
| 149 | `NTreasureRoomRelicHolderFocusGuardPatch` | `NTreasureRoomRelicHolder` | `OnFocus` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Nodes\Screens\TreasureRoomRelic\NTreasureRoomRelicHolder.cs (L82)` |
| 150 | `RunLobbyPatch` | `RunLobby` | `AbandonRun` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\Lobby\RunLobby.cs (L174)` |
| 151 | `SpoilsMapPatch` | `SpoilsMap` | `OnQuestComplete` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Models\Cards\SpoilsMap.cs (L117)` |
| 152 | `StaleCombatActionJanitorTriggerPatch` | `ActionQueueSet` | `CombatEnded` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\GameActions\Multiplayer\ActionQueueSet.cs (L376)` |
| 153 | `StartRunLobbyPatch` | `StartRunLobby` | `BeginRunForAllPlayers` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\Lobby\StartRunLobby.cs (L440)` |
| 154 | `StartRunLobbyReadyFixPatch` | `StartRunLobby` | `IsAboutToBeginGame` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\Lobby\StartRunLobby.cs (L738)` |
| 155 | `StartRunLobbySetReadyPatch` | `StartRunLobby` | `SetReady` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\Lobby\StartRunLobby.cs (L701)` |
| 156 | `SynchronizationOwnershipLogPatch` | `RewardSynchronizer` | `SyncLocalObtainedCard` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\RewardSynchronizer.cs (L71)` |
| 157 | `SynchronizationOwnershipLogPatch` | `RewardSynchronizer` | `SyncLocalObtainedRelic` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\RewardSynchronizer.cs (L97)` |
| 158 | `SynchronizationOwnershipLogPatch` | `RewardSynchronizer` | `SyncLocalObtainedPotion` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\RewardSynchronizer.cs (L123)` |
| 159 | `SynchronizationOwnershipLogPatch` | `RewardSynchronizer` | `SyncLocalObtainedGold` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\RewardSynchronizer.cs (L149)` |
| 160 | `SynchronizationOwnershipLogPatch` | `OneOffSynchronizer` | `DoLocalMerchantCardRemoval` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\OneOffSynchronizer.cs (L66)` |
| 161 | `ThievingHopperPatch` | `ThievingHopper` | `ThieveryMove` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\Models\Monsters\ThievingHopper.cs (L183)` |
| 162 | `ToolboxPatch` | `Toolbox` | `BeforeHandDraw` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Models\Relics\Toolbox.cs (L21)` |
| 163 | `TreasureRoomRelicSynchronizerPatch` | `TreasureRoomRelicSynchronizer` | `OnPicked` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\TreasureRoomRelicSynchronizer.cs (L178)` |
| 164 | `TreasureRoomRelicSynchronizerBeginPatch` | `TreasureRoomRelicSynchronizer` | `BeginRelicPicking` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Multiplayer\Game\TreasureRoomRelicSynchronizer.cs (L89)` |
| 165 | `UsePotionActionWatchdogPatch` | `UsePotionAction` | `ExecuteAction` | 字符串 | **verified** | `D:\Download\pain\sts2src\src\Core\GameActions\UsePotionAction.cs (L102)` |
| 166 | `WhisperingEarringPatch` | `WhisperingEarring` | `AfterAutoPrePlayPhaseEnteredLate` | nameof | **verified** | `D:\Download\pain\sts2src\src\Core\Models\Relics\WhisperingEarring.cs (L41)` |

> 状态含义：`verified` 类型与方法（或属性）均在反编译源码中找到；`STALE-TYPE` / `STALE-METHOD` 疑似失效（游戏更新后常见）；
> `UNKNOWN` 目标无法解析；`unverified` 未做反编译核对；`WARN-METHOD-ONLY` 该类会被 PatchAll 跳过；
> `container` 纯容器类（补丁全部在嵌套类中，PatchAll 正常处理，无需关注）。
> 反编译锚点为近似定位（按类型简名 + 方法/属性定义行匹配），仅供人工核对参考。