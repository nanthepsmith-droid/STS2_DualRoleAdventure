> 本文件是 CodeBuddy skill `dualroleadventure-development/references/` 同名文件的仓库内只读副本，
> 供纯人工维护者阅读；请与 skills 目录对应文件保持同步更新。

# 游戏源码位置（只读参考）

反编译源码目录以仓库里实际存在的为准。工作区里是 **`D:\Download\pain\sts2src\src`**
（旧文档写的 `D:\Download\sts2beta111\src` 可能已不存在，用前先确认）。

## 常用位置

- `Core\Nodes\Combat\NPlayerHand.cs`：SelectCards `:604`，共享 TCS `:628`。
- `Core\Commands\CardSelectCmd.cs`：FromHand `:856`，SyncLocalChoice `:857`。
- `Core\Nodes\Combat\NCombatUi.cs`：Hand 属性 `:70`（类型是 `NPlayerHand`）。
- `Core\GameActions\ActionExecutor.cs`：ExecuteActions `:123`
  （逐个动作 await 到暂停/完成再取下一个；**每条动作执行完会自动调 `CheckWinCondition`**：`:170`）。
- `Core\Combat\CombatManager.cs`：
  - `StartTurn` `:688`（回合主干）
  - `SetupPlayerTurn` `:880`（每个存活角色的回合开始，必触发）
  - `TurnStarted?.Invoke` `:837`（玩家回合真正开始的事件）
  - 回合开始处把**死亡角色**直接置 ready：`:802-813`
- `Core\Nodes\Combat\NEndTurnButton.cs`：`SetState` `:195` 附近、`PlayerCanTakeAction` `:195`、
  `OnTurnStarted` `:248`、`OnAboutToSwitchToEnemyTurn` `:243`。
- `Core\Rewards\RewardsSet.cs` / `Core\Multiplayer\Game\RewardsSetSynchronizer.cs`：
  `SelectLocalReward` 要求 `reward.Player == LocalPlayer`（`_localPlayerId` 决定）。
- `Core\CardSelection\CardSelectorPrefs.cs`：**struct**。
- 游戏 Harmony：`<游戏>\data_sts2_windows_x86_64\0Harmony.dll` = **2.4.2.0**。

## 游戏更新后的适配流程（仓库 AGENTS.md §5）

1. 从 `<game>\release_info.json` 记下新版本号。
2. 重新生成反编译源码：
   ```bash
   dotnet tool install -g ilspycmd --version 9.1.0.7988   # 更新的大版本可能装不上
   ilspycmd -p --nested-directories -o ~/sts2-src "<game>/data_sts2_windows_x86_64/sts2.dll"
   cp -r ~/sts2-src/MegaCrit/Sts2/. src/
   ```
3. 构建，用反编译源码作为事实来源修编译错误（编译错误 = 成员改名/被删）。
4. **逐个校验字符串式 Harmony / `AccessTools` 目标**——这些是**运行时**才失败，编译期发现不了。
5. 改法惯例：先调**新**成员名，再反射回退到旧名（参考
   `Scripts/Patch/LoadRunLobbyPatch.cs` 的 `InvokeBeginRunIfAllPlayersReady`）。
6. 每处适配都记进 `CHANGELOG.md`。
