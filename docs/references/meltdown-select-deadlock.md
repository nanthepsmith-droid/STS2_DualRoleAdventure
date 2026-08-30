> 本文件是 CodeBuddy skill `dualroleadventure-development/references/` 同名文件的仓库内只读副本，
> 供纯人工维护者阅读；请与 skills 目录对应文件保持同步更新。

# 灵乌路空「炉心融解」选牌卡死（完整复盘）

## 现象与根因

双方本地角色都带灵乌路空 Boss「炉心融解」（GensokyoSpire 的 `MeltdownPower`）时，回合开始
生成两个 `GenericHookGameAction`（各属一个角色）顺序执行；各自在 `CardSelectCmd.FromHand`
（`CardSelectCmd.cs:856`）调 `NCombatRoom.Instance.Ui.Hand.SelectCards(...)`。

`NCombatUi.Hand` 就是 `NPlayerHand`（`NCombatUi.cs:70`）；`NPlayerHand.SelectCards` 只有
**单个共享 `_selectionCompletionSource`**（`NPlayerHand.cs:628`），第二次调用覆盖第一次 → 第一个
角色的 await 永远不完成 → 软锁（角色不能出牌/结束回合）。

## 方案：串行化 + 切前台

- `NPlayerHandSelectCardsSerializationPatch`：`SemaphoreSlim(1,1)` 闸门，同一时刻只弹一个选牌；
  用 `AsyncLocal<bool> _inSerialized` 标记「包装任务重入原始实现」放行防递归（兄弟调用链看不到此值，
  会排队等闸门）。进选牌前用 `TryEnsureForegroundForPlayerId` 把前台切到所属角色。
- `CardSelectForegroundSwitchPatch`：在 FromHand / FromHandForDiscard / FromHandForUpgrade /
  FromSimpleGrid / FromChooseACardScreen / FromCombatPile 等入口前缀里记录
  `AsyncLocal<ulong?> CurrentChoicePlayerId`（本次选择所属角色，沿链流动）+ 切前台。
- owner 解析顺序：`CurrentChoicePlayerId` 优先（能处理双方交错选牌），source 模型 owner 兜底。
  **不要用「当前正在执行的 Action 的 OwnerId」兜底**——「A 出牌触发 B 选牌」会切错前台。

## 验证要点（正确日志的样子）

- 启动：`marker=2026-08-19-r5`；无 `HarmonyException` / `InvalidProgramException` /
  `TargetInvocationException`；`Harmony 补丁统计` 清单含 `NPlayerHand.SelectCards` 与
  `CardSelectCmd.FromHand`。
- 选牌：对每个角色依次出现
  `选牌展示前已切换前台到所属角色: player=…` →
  `战斗内手牌选牌串行化: 已进入选牌, mode=SimpleSelect, owner=…, source=MeltdownPower` →
  `战斗内手牌选牌串行化: 重入原始实现(包装内), mode=SimpleSelect`。
- 双角色必须**严格串行**：角色 A 的三条日志先完整出现，再角色 B。
