# Changelog

Notable versions and key changes of `LocalMultiControl` / `DualRoleAdventure`. Entries up to v1.30 are translated from the original author's Chinese changelog; the fuller day-by-day history lives in `docs/archive/player-update-history.zh.md`.

## [Unreleased]

> 维护性改进 **Phase 1（1.1~1.5）+ Phase 2（2.1~2.4）已全部合并到 `master`**（2026-08-31，部署位 marker r46）。
> 本系列改动不改变任何既有运行时行为默认值，均为**工具化 / 自检 / 文档 / 结构性改进**，
> 外加实证发现的存量修复与瓦库自动化功能（r28 / r41~r43）。

### Added
- **补丁目标覆盖清单（维护性改进任务 1.1）**：`Scripts/Tools/patch_coverage.py` 扫描全部
  `Scripts/Patch/*.cs` 的 HarmonyPatch 目标并与反编译源码（sts2src）交叉核对，输出
  `../maintenance-docs/patch-coverage.md`（148 补丁类 / 166 目标行）——「哪些补丁打在哪、是否已核实」从此可查询、可再生成。
- **启动自检（维护性改进任务 1.2）**：`Entry.cs` 新增期望补丁清单（25 个关键目标），
  初始化时与 `GetPatchedMethods()` 实际结果比对，缺失即 `Log.Error` 醒目报错——
  终结「方法级-only 被 `PatchAll` 静默跳过」这类无声失败，游戏更新后启动日志即暴露断档。
- **游戏更新适配脚本化（维护性改进任务 1.3）**：
  - `Scripts/Tools/regenerate_src.ps1`：一条命令重生成反编译参考源码 `sts2src/src`
    （读 `release_info.json` → ilspycmd 反编译 → 覆盖拷贝 `.cs` → 打印 diff 统计）；
  - `Scripts/Tools/check_string_targets.py`：核对全部**字符串式**目标（HarmonyPatch 字符串 /
    `AccessTools.*` / 反射 `GetXxx("...")`），输出 `../maintenance-docs/string-targets.md`，失效即退出码 1（可进 CI）；
    识别「新名优先 + 旧名回退」的 `LEGACY-FALLBACK` 不算失效。
- **文档整理（维护性改进任务 1.4）**：根目录 8 份分析/方案文档归档 `../maintenance-docs/decision-records/`；
  skill 的 7 份 references 副本入 `../maintenance-docs/references/`；`../maintenance-docs/维护现状分析.md` 刷新。
- **纯逻辑单元测试（维护性改进任务 2.1）**：
  - 新增纯逻辑层 `Scripts/Runtime/PureLogic/`：药水规则判定 `WakuuPotionDecision`（相位/范围/
    首回合/条件/昏眩）、选牌策略 `WakuuStrategyPicking`（first/last/random/洗牌/火堆锻造）、
    配置 JSON 纯函数 `WakuuConfigJson`、卡牌 id 判定 `WakuuCardId`——全部从运行时调用点
    **原样搬移，行为零变化**；
  - 新增 `tests/LocalMultiControl.Tests/`（nunit，net9.0）：147 个用例覆盖配置解析/规范化、
    选择器策略、药水判定组合、60 条药水规则表完整性（元数据导出校验，含 Match 目标类型 IL 提取）；
  - `build_all_mods.ps1` 构建主 mod 后先跑 `dotnet test`，**0 失败才允许部署**；
  - 修复：主项目 csproj 未排除 `tests/**` 会把测试文件编进 mod 程序集的问题。
- **瓦库大脑决策接口（维护性改进任务 2.2，分支 `feat/decision-interface`，marker r31）**：
  - 新增 `Scripts/Runtime/WakuuBrain/`：`IWakuuCombatBrain`（快路径 `TryDecideNext` / 计划路径 /
    派生选牌 / 生命周期钩子）+ `WakuuDecisionContext` + `WakuuPlannedAction` +
    `HeuristicWakuuBrain`（现有「第一张可打牌 + ResolveTarget」逻辑原样搬移）+ `WakuuBrainFactory`；
  - 出牌主循环改为向大脑要「下一步」，**默认 heuristic 行为与 v1.38 完全一致**；
  - 新增 `wakuuBrain` 开关（heuristic/auto，默认 heuristic；auto 预留求解器探测，未命中回退启发式）。
- **修复仓库统一（维护性改进任务 1.5）**：新增 `Scripts/Tools/build_all_mods.ps1` 一键构建+部署+校验
  全部 7 个 mod（主 + 6 fix，`-BuildOnly`/`-DeployOnly`/`-CheckOnly` 三模式）；修复
  `Act4FinalAscentFixes.csproj` 指向已删除目录的构建问题；6 个 fix 仓库补 README。
- **补丁隔离（维护性改进任务 2.3，marker r46）**：`PatchAll` 改为按域分组 try-catch——
  新增 `Scripts/Patch/PatchDomainMap.cs` 将 **144 个顶层补丁类归入 7 域**（Core 37 / Lobby 28 /
  Combat 30 / Rewards 8 / Wakuu 5 / Ui 34 / ThirdParty 2，嵌套类继承容器域）；`Entry.cs` 分组应用：
  **Core（回环/同步器/选牌串行化）失败即停**，其余组失败打 Error 跳过继续，未登记类 Warn + 隔离组兜底；
  回滚开关 `UseGroupedPatchAll`；新增分组完整性单测 6 例（175 用例全绿）。
- **发布自动化（维护性改进任务 2.4）**：新增 `Scripts/Tools/release_build.ps1` 一条命令产出发布包——
  semver 校验 → 三处版本同步（根 / `workshop\content` 的 json + `mod_manifest.json`，UTF-8 带 BOM
  字节保真正则替换）→ marker 建议串 → `dotnet build -warnaserror` 门禁 → 拷贝 `workshop\content` →
  打 zip 到 `release\` → SHA256（源 dll / zip / zip 内 dll 核对一致）。
- **SkadaHelper 社区统计反射适配器（瓦库托管优化 Phase 3.1，marker r47，分支 `feat/skada-assist`）**：
  - 新增 `Scripts/Runtime/WakuuSkadaAdapter.cs`：以**可选依赖**方式接入创意工坊 mod
    「皮皮军师: SkadaHelper」的 40 万局社区统计（不做编译期引用，运行时反射探测
    `SkadaHelper.Scripts.Lite.DataProvider.Data`），反射全部集中在单一文件；
    未安装 / 内部结构改名 / 数据包未就绪一律**静默失效**并回退既有策略，启动时打探测日志。
    查表用**瓦库玩家自己的角色 id**，不走其 `CurrentCharacterId`（跟随前台，多控下语义漂移）。
  - 新增纯逻辑 `Scripts/Runtime/PureLogic/WakuuSignalPicking.cs`：卡牌主信号 `PickRate`
    叠加因果增益（`WinRateHeld − WinRateSkipped`），样本量 `OfferCount` 不足视为无数据；
    事件选项按 `WinRate` 选优 + `Count` 阈值；同分保留最左/最上；含量纲归一（0~1 与 0~100 混用）
    与文本模糊匹配归一化。
  - 新增 `skadaAssist` 开关（`vakuu_autopilot.json`，**默认关**）：开启后卡牌奖励与事件选项
    优先参考社区统计；关闭或任何一环无数据时，行为与本次改动前**完全一致**（卡牌奖励仍领最左，
    事件选项仍按 `eventChoiceMode`）。设置页新增对应勾选行。
  - 已知限制：`SkadaHelper` 自身按**文本**关联事件选项，中文界面下大概率整体 miss
    （已在日志中打出命中率供核对），miss 时无害降级；其数据包为 v0.107 口径且只含原版内容，
    mod 卡牌/事件查不到即回退。
  - 测试：新增 `SignalPickingTests` 26 例（合计 201 例全绿）。
- **卡牌统计加因果增益门槛（Phase 3.1 实测修复，marker r48）**：
  - 实机日志复现：`DEADLY_POISON, pickRate=0.127, gain=-0.115, offerCount=45834` 作为三张候选里
    唯一有数据的卡被选中（还跳过了最左选第 3 张）——但 `gain=-0.115` 表示"拿了它的局胜率反而
    低 11.5%"，是明确负面信号。根因：此前只校验样本量（`OfferCount`）门槛，未校验信号方向；
  - 修：`WakuuSignalPicking.PickBestCardIndex` 新增 `minGain` 门槛（默认 0），作用于**加权后**的
    因果增益，增益为负的候选直接出局、不参与竞选；全部出局则返回 -1 回退默认策略。
    作用在加权增益上，故 `gainWeight=0` 时决策退化为纯 `PickRate` 排序，参数语义自洽；
  - 配套：回退日志区分"无数据"与"信号为负"；单测 205 例全绿（新增 4 例，含实机复现用例）。
- **智能选牌优先级（瓦库托管优化 Phase 3，marker r49）**：
  - 新增纯逻辑 `Scripts/Runtime/PureLogic/WakuuPriorityPicking.cs`：场景枚举（Remove/Copy/Transform/
    Unknown）、卡牌类别（Curse/Status/Quest/BasicStrike/BasicDefend/Other）与稳定降序排序函数，
    实现 §9.2 规则表：删除优先 诅咒→状态→任务→打击→防御→其余；复制首选非坏牌、候选全坏时
    倒序 防御→打击→任务→状态→诅咒（已实现、待手牌场景识别后接入）；变化优先 打击→防御→其余，
    硬排除诅咒/状态/任务；
  - 场景识别（§9.1）：`WakuuEventEnchantAutoAnswerPatch` 新增 `FromDeckForRemoval` 专用入口拦截
    （FieldOfManSizedHoles / DoorsOfLightAndDark / LuminousChoir / 商店删牌 / 删牌遗物等全部走此入口）
    → Remove 场景；`FromDeckForTransformation` → Transform 场景；`FromDeckGeneric` 被木雕等
    "变化选保留牌"共用，明确不归入删除、维持既有策略；
  - `LocalWakuuStrategySelector` 升级为带场景上下文的优先级选择器：`smartPick` 开启且场景明确时
    套优先级表，否则走既有 `cardPickMode`；任一步异常回退既有策略，不因智能选牌失败卡住瓦库；
  - 新增 `smartPick` 开关（`vakuu_autopilot.json`，**默认关**，关=行为与改动前完全一致）+ 设置页勾选行；
  - 测试：新增 `PriorityPickingTests` 23 例（合计 228 例全绿）；主项目 Release 0 警告 0 错误。
- **手牌选牌场景识别接入 Copy/Remove/Transform（Phase 3 智能选牌补全，marker r50）**：
  - 纯逻辑 `WakuuPriorityPicking.ClassifyHandScenario(source 类型名, prefs 标题 loc key)`：
    prefs 预设（`TO_EXHAUST`/`TO_REMOVE` → Remove、`TO_TRANSFORM` → Transform）确定性最高优先，
    source 类型名兜底（原版 `DualWield` 复制用自定义标题，按类型名识别为 Copy；mod 复制/镜像类
    按 Copy/Duplicate/Echo/Clone/Double/Mirror 关键词推断；类型名含 Exhaust → Remove）；
    未知（弃牌/附魔/升级等）→ Unknown，维持既有 `cardPickMode` 策略、不越权；
  - 新增 `Scripts/Patch/CardSelectHandScenarioPatch.cs`：战斗内瓦库出牌作用域（栈上全局选择器为
    本 mod 策略选择器）拦截 `CardSelectCmd.FromHand`，场景明确时用带场景的 `LocalWakuuStrategySelector`
    作答——复制类卡（双重挥砍）选非坏牌、消耗类卡（印记/保暖手套/暴政之力）优先消耗坏牌、
    变化类卡（熵/离去）优先变打击/防御；**硬约束沿用 r6 教训：作用域外选牌一律不代答**；
  - 事件作用域内 FromHand 同步接入场景判定（`WakuuEventEnchantAutoAnswerPatch` 增加
    `scenarioOverride`，按 source/prefs 判定而非固定 Unknown）；
  - 测试：`PriorityPickingTests` 新增 `ClassifyHandScenario` 13 例（合计 241 例全绿）；
    主项目 Release 0 警告 0 错误。
- **维护性改进文档移出主仓库**：`patch-coverage.md`/`string-targets.md`/`维护现状分析.md`/
  `decision-records/`/`references/` 移到 `pain/maintenance-docs/`（无 git，不会进 github）；
  主仓库 `docs/` 只保留原作者文档（`archive/`、`design/`、`architecture.md`、`console-commands.md`）。
- **瓦库事件中「选择卡牌附魔」自动按策略作答（marker r42，分支 `feat/wakuu-event-enchant-autopick`）**：
  - 新增 `rare`（稀有度最高）选牌策略：Ancient > Rare > Uncommon > Common > Basic，
    同稀有度保持原序（`WakuuStrategyPicking` 支持 rankSelector 稳定降序）；
  - 事件自动选择执行选项期间（`LocalWakuuEventAutoChoice` 作用域标记），选项触发的卡牌选牌
    （附魔/手牌选牌）由新增 `WakuuEventEnchantAutoAnswerPatch` 自动按 `cardPickMode` 策略作答，
    不再弹出选牌界面停住等真人；
  - 设置面板「战斗内选牌策略」新增第 4 档「稀有度最高」（`cardPickMode` 开放 rare；
    事件选项策略 `eventChoiceMode` 仍为 3 档，事件选项无稀有度概念）；
  - 配置：`NormalizeCardPickMode` 开放 rare；`WakuuConfigJson`/纯逻辑单测同步扩充（169 用例全绿）。
- **手牌变换后 UI 与实际不同步的修复（marker r42）**：
  - `CardTransformNetIdPinPatch`：执行 `CardCmd.Transform` 期间把 `LocalContext.NetId` 钉到变换牌主人，
    修复「铁甲战士用效果变化所有手牌时偶发看起来没生效，切角色再切回才生效」——
    根因是 `CardCmd.Transform` 的视觉门 `LocalContext.IsMine` 用全局 NetId 判断归属，
    本地双角色下 NetId 瞬时非牌主人时手牌节点不替换、数据层已更新。

### Fixed
- **原始力量打完后卡在屏幕中间不进弃牌堆（marker r43）**：
  - 根因：r42 的 `CardTransformNetIdPinPatch` 用 `return false` 跳过 `CardCmd.Transform` 原方法，
    导致第三方框架 RitsuLib 挂在该方法上的 `CardCmdTransformPatch` **前缀未执行、`__state` 保持 null**，
    其后缀访问 `__state.Snapshots` 抛 NRE → `PlayCardAction` 失败 → 牌留在 Play 位（实测日志
    `PRIMAL_FORCE` NRE + 本 mod `prevNetId=527`、`owner=526`）。
  - 修复：改为 **void 前缀只钉 NetId + postfix 包装任务恢复**（不再跳过原方法），
    RitsuLib 前缀照常执行、`__state` 有效，钉 NetId 的视觉修复保留。
- **瓦库事件升级/变化/删除/通用选牌自动作答（marker r43）**：
  - `WakuuEventEnchantAutoAnswerPatch` 从仅附魔扩展到 `FromDeckForUpgrade` /
    `FromDeckForTransformation` / `FromDeckGeneric`（删除与 WoodCarvings 等通用选牌兜底），
    与附魔共用 `cardPickMode` 策略（最前/最后/随机/稀有度最高）；`FromDeckForRemoval` 内部走
    `FromDeckGeneric` 已覆盖。

### Fixed
- **古明地恋本我牌串台到队友（marker r41）**：`IdLiberationBeforeHandDrawFixPatch` 拦截
  「本我解放力量」对非力量主人的抽牌钩子生成（根因：`Hook.BeforeHandDraw` 把当前抽牌玩家传入
  所有力量，Koishi 不检查 owner）；选牌期间钉 `LocalContext.NetId` 到选牌 owner +
  `NPlayerHandAddOwnerGuardPatch` 拦截非选牌 owner 的牌节点进手牌 UI；`Entry.PatchAll` 加
  try-catch（单补丁异常不中断初始化）；Koishi 补丁用 `Prepare`/`TryApplyLate` 延迟挂载。
- **两个「方法级-only `[HarmonyPatch]`」补丁类被 `PatchAll` 静默跳过、从未生效**（本 mod 坑 1）：
  - `CardSelectManualConfirmationPatch`：补类级裸 `[HarmonyPatch]`。此补丁自原作者加入起
    就因缺类级标记从未被应用，本地多控下「删牌/升级/变化强制弹出背包手动确认」实际从未生效，
    现正式启用（瓦库自动选牌走选择器分支，不受 `RequireManualConfirmation` 影响）。
  - `NEndTurnButtonLifecyclePatch`：补类级裸 `[HarmonyPatch]`。诊断探针此前从未触发，
    现可正常记录按钮生命周期日志（含 `CombatManager.AfterAllPlayersReadyToBeginEnemyTurn`、
    `NCombatUi.Activate` 等挂点）。
- **`RestSitePatch.cs` 失效的 `NRestSiteRoom.UpdateNavigation` 反射调用**（任务 1.3 核对实证）：
  v0.111.0 的 `NRestSiteRoom` 已无此方法（焦点邻居导航在 `UpdateRestSiteOptions` 创建按钮时完成），
  该调用自加入起就被 `?.` 容错静默跳过、从未生效，现已删除。

## [v1.38] - 2026-08-29

> 版本号 1.38.0（`mod_manifest.json` / `DualRoleAdventure.json` / `workshop/content/DualRoleAdventure.json` 三处已同步），
> DLL marker `2026-08-28-r27`。本版为**纯 Bug 修复**：不新增功能、不改变任何既有行为默认值，
> 仅收紧战斗/奖励生命周期的时序与角色绑定，并修掉瓦库设置页的首次显示问题。

### Fixed
- **打赢后到奖励面板跳出的莫名延迟**（由 v1.37「击杀后战斗不结束」的方案 A 延迟结算引入）：
  `CreatureCmdKillWinCheckPatch` 的延迟结算轮询由 `150ms × 60` 轮（最坏 9 秒）收紧为 `30ms × 20` 轮
  （最坏 600ms）。原值即便在理想情况（击杀动作链几帧内收敛，或游戏自身 `ActionExecutor.ExecuteActions`
  在每条动作后已调 `CheckWinCondition` 自动结算）也要按固定 150ms 粒度白等，观感就是「打赢要等一会」。
  轮询在检测到战斗已结算或敌人复活时仍会立即提前返回，不再增加额外等待。
- **本地双人中一名玩家死亡后，另一名玩家的「结束回合」按钮消失**（经典问题，需切走再切回才恢复）：
  - 根因核实（日志确认）：本地多控下 `NEndTurnButton.SetState` / `OnTurnStarted` 的 Harmony 探针
    **在日志中从未触发**，原版 `TurnStarted` 事件路径在该场景下不可靠，死亡玩家干扰了存活玩家的按钮判定。
  - 修复：改挂在**确认每次存活玩家回合开始都会触发**的 `CombatManager.SetupPlayerTurn` Prefix 上，
    调用 `LocalMultiControlRuntime.ReevaluateEndTurnButtonForControlledPlayer`，按「当前控制角色是否存活
    且未 ready」兜底重评按钮状态；存活玩家回合开始必然拿到 Enabled 按钮，死亡玩家不再参与判定。
    该重评刻意放在瓦库前台抑制判断之前，确保真人角色不受瓦库托管开关影响。
  - 注：r26 曾尝试挂 `NEndTurnButton.OnTurnStarted`，经日志验证无效，r27 按上述方案重写。
- **事件中卡牌奖励归属角色与实际领取角色不一致导致软锁死**（经典问题）：新增
  `RewardsSetSynchronizerSelectLocalRewardPatch`。领取时把 `RewardsSetSynchronizer._localPlayerId`、
  `LocalContext.NetId` 与回环 sender 统一临时改绑到「奖励的归属角色」，领取完成后（含异常路径）恢复原值。
  这样无论控制权当前在谁手上，真人点击领取都能命中归属角色的奖励栈与完成源，
  事件里 `await RewardsCmd.OfferCustom` 不再被永久挂起。
- **瓦库托管设置面板首次点进去内容不显示**（只有退出按钮和滚动条，退出重进一次才显示）：
  根因是子菜单实例在栈下懒建并缓存，创建时 `Visible=false`，`_Ready()` / `BuildContent()` 阶段
  clipper 尚未完成布局（FullRect 尺寸为 0），`OnScrollContentResized` 会把滚动内容宽度压成 1px，
  内容列被裁剪到不可见；重进时缓存实例已带上一轮布局好的尺寸，故能正常显示。
  修复：`LocalWakuuConfigSubmenu` 重写 `OnSubmenuShown()`，每次显示（含首次）用 `CallDeferred`
  延迟一帧重算滚动内容尺寸，保证首次进入内容即可见。
- **击杀后战斗不结束（治本，2026-08-28 r24→r25 初版，随 v1.38 发布）**：`CreatureCmd.Kill` 后
  不再立即 `CheckWinCondition()` 翻转战斗（击杀牌 PlayCardAction 仍 Executing 时提前翻转会跳过
  执行中动作、破坏战斗/玩家状态），改为**有界延迟结算**——敌全灭后轮询等待击杀动作链走完
  再兜底结算，敌人复活或战斗结束立即收工；后续又将轮询粒度收紧（见本版首条 Fixed）。
- **奖励生成容错**：单个玩家卡牌奖励生成失败（如空池）不再整体中止流程、也不丢弃该玩家所有
  奖励；只丢弃未就绪的空池卡牌，金币/药水/遗物照常展示；奖励生成前增加诊断埋点。
- **`LocalLoopbackHostGameService.GetVersionInfoForPeer` 硬化**：返回本地版本信息而非 null
  （上游 v1.32 同款），防止未来游戏更新把三条大厅加入路径路由进本地合作。
- README：GuyGinat 的社区 Workshop 条目（[3772900244](https://steamcommunity.com/sharedfiles/filedetails/?id=3772900244)）
  恢复更新，现与原条目（3747538947）并列推荐。

## [v1.37] - 2026-08-26

> 分支 `feat/vakuu-config-menu`（2026-08-25 → 08-26，marker r1–r22）。所有新功能集中在
> 设置 → 常规页新增的「瓦库托管」子菜单中开关（即时写回 `%APPDATA%\SlayTheSpire2\vakuu_autopilot.json`），
> 总开关仍为 `useVakuuForm`（默认关 = 与 v1.36 行为一致）。

### Added
- **设置页子菜单**：设置 → 常规注入「瓦库托管」按钮行，推入原生子菜单栈（原生观感，
  无黑幕浮窗），勾选框复刻游戏原生外观；策略类配置用循环切换按钮行。
- **战后奖励自动领取**：卡牌奖励领最左（`autoClaimCards`）、金币与遗物自动领
  （`autoClaimGoldRelics`）、药水奖励自动领（`autoClaimPotions`）——有空位直接领；
  满栏且栏内有鲜血药水先喝掉腾位；否则奖励稀有度高于栏内最低才丢弃栏内最低者换领。
- **非共享事件自动选择**（`autoChooseEvents`）：按策略 first/last/random（`eventChoiceMode`）
  逐页选择，直调 `EventOption.Chosen()` 绕开消息层；复刻原版联机死亡拦截
  （致死选项剔除、整页死路停住等真人）；触发战斗/小游戏弹层即停；水晶球绝对排除、涅奥默认关。
- **事件/遗物自定义奖励后台直接结算**：后台瓦库的 `RewardsCmd.OfferCustom` 奖励
  （未来药水/药水快递/坩埚/召唤铃等）不再依赖无人能点的弹屏，按开关逐件结算或跳过放行。
- **火堆自动选择**（`autoRestChoice`）：血量 <50% 睡觉；有遗物选项在睡觉以外随机；
  否则锻造升级"打击/防御以外"的最后一张牌（不足用打击/防御补齐），全升完则睡觉；
  未满血且没得锻时愈合队友；全员满血时锻造候选放宽到含打击/防御；帐篷多选全拿。
- **战斗内自动用药引擎**（`autoUsePotions` 默认关）：65 种原版药水逐条规则表驱动，
  支持回合开始/回合结束前两个评估相位、敌人意图伤害估算、定向作答选择器：
  - 治疗/资源类：血液/再生低血自用；果汁到手立刻喝；混沌药水（EntropicBrew）栏内只剩它
    且有空位时喝；迅捷/异蛇之油剩能量抽牌；能量药水救"仅因能量不足打不出"的高费牌；
    稳定血清保留能力牌手；龙涎香 Boss 残血吃额外回合；
  - 精英/Boss 首回合：力量/敏捷/集中/异鱼之油/流动铜液/马萨雷斯赠礼/明耀酊剂/宇宙药剂/
    精炼混沌/明晰提取物自用，攻击技能能力无色药水使用，火焰/毒素/灾厄/易伤/虚弱/
    消亡粉末对第一个敌人，爆炸安瓿敌数≥3 或首回合全体；
  - 意图触发：甲虫汁/镣铐=有敌人意图攻击；铁心=敌人攻击意图或已有覆甲；速度=有技能牌；
  - 回合结束前防御兜底：格挡（≥敌伤+10 或致死）、固化（格挡×3<敌伤）、罐装幽灵/
    幸运补剂（≥+30 或致死）、瓶中船（精英/Boss ≥+15 或致死）；
  - 角色路由：集中/扩容/黑暗精华→故障机器人队友，星星/王之勇气→储君，骨头酿/尸鬼瓮→
    亡灵契约师，士兵炖汤→铁甲战士（仅Boss）；自己是该职业则自用；
  - 优先给真人：复制、超巨化；
  - 手牌构成条件：灰水只消耗状态/诅咒、赌徒特酿/瓶装潜能/发光水只洗坏牌、
    癫狂之触免费最高费牌、预知之滴取最便宜能力/稀有牌、液态记忆取弃牌堆能力牌；
  - **昏眩**（RingingPower）时不使用抽牌/能量类药水；
  - **污浊药水**绝不在战斗中使用，遇商人自动投掷换 100 金币；果汁经领取链路即刻入队饮用；
  - mod 药水（非游戏内置）：普通战斗随机回合（预掷 1~3）消耗。

### Fixed
- 火堆后出发永久黑屏：多控下一方未选完火堆就出发，`MoveToMapCoordAction` 在
  `RestSiteSynchronizer.AfterAllRestSitesCompleted` 处永久挂起（原版注释明确警告过该挂起）。
  出发前把未完成的休息区选择按跳过补完（与断线处理同款语义）；被跳过方拿不到火堆收益。
- 地图点节点不跟投/不出发：击杀最后一敌的卡牌动作仍在 Executing 时立即胜利结算翻转战斗状态，
  战斗结束清理跳过执行中的动作，其随后暂停等待玩家选择、恢复后已不在战斗而**永久卡死队列头部**，
  堵死后续全部投票动作。新增战斗外残留战斗动作清道夫（CombatEnded 后延迟多轮扫描，
  按游戏原语义 Cancel+移除非执行中的战斗类残留；新战斗开始即停止）。
- 后台瓦库的事件/遗物自定义奖励挂起：`OfferCustom` 在 `IsMe=false` 时不弹屏但完成任务
  无人满足，事件选项 await 它会永久卡住（SetEventFinished 排在其后）。改为后台直接结算放行。
- 托管上下文窗口吞掉前台玩家自己的弃牌/抽牌演出（切人后自愈）：`CardPileCmd.Add` 大重载
  执行期间把 NetId 钉扎到前台角色。
- 合成/抽牌视觉节点串进前台玩家手牌（数据层一直正确）：进手牌演出改按前台角色判定。
- 火堆选择成功但角色头顶不显示气泡：等 UI 房间节点就绪后再决策；显式驱动头顶气泡。

### Changed
- 托管效果选牌改用自建策略选择器（`cardPickMode`: first/last/random，默认 last，
  解决酒狐合成永远拿到排最前的牌）；作用域外选牌一律不代答（回滚验证：代答会导致开局黑屏）。
- 【瓦库形态】旧"永久低语耳环"路径完整保留，`useVakuuForm=false` 时行为与 v1.35 一致。

## [v1.36] - 2026-08-24

### Added
- 瓦库形态托管（默认关闭）：新增独立遗物【瓦库形态】（+1 能量 + 接管所有回合自动出牌），
  与旧的"永久低语耳环"路径并存。总开关集中在 `%APPDATA%\SlayTheSpire2\vakuu_autopilot.json`
  （`useVakuuForm` 默认 false = 完全保持原有瓦库行为；每次开局重新加载）：
  - `playAllCards`：打光所有手牌（60 张护栏防死循环）；
  - `backgroundMode`：后台托管——回合钩子/自动出牌不再把前台切给瓦库角色，
    选牌在自动出牌作用域内自动作答免切换，作用域外保留切换兜底；
  - `suppressVanillaEarring`：压制原版低语耳环的自动出牌钩子（+1 能量保留）；
  - 交互安全网：后台瓦库被弹层卡住（战斗滞留 12 秒 / 事件滞留 8 秒）时自动切前台
    并全屏提示，交由人工处理。

### Fixed
- 读档后奖励界面永久黑屏（v1.34 起）：读档链路 `StartRun: FadeOut → LoadRun（内部进入
  PreFinished 房间并弹出战后奖励）→ FadeIn` 中，合并奖励补丁同步等待玩家领奖，阻塞了配对
  的 FadeIn——全屏转场黑幕（NTransition SimpleA=1）永不撤除。现读档重放期间奖励改为后台
  弹出、入口立即放行，与原版 fire-and-forget 语义一致；实时战斗路径行为不变。
- 自定义遗物描述页异常（显示未解锁且无法退出/打开）：未加入任何遗物池的遗物在
  `RelicModel.Pool` 处抛 `InvalidOperationException`，中断悬停提示构建。托管遗物现经
  `ModHelper.AddModelToPool` 注册进事件遗物池（该池无随机奖励引用）。
- 真人选牌被托管抢答：瓦库自动出牌进行中（全局选择器在栈上）时，真人打出的需要选牌的卡
  （如酒狐合成）会被瞬间自动应答为第一张。现在 `CardSelectCmd.Selector` 加守卫——选牌归属者
  非瓦库形态角色时强制走正常选牌 UI；另加"任何弹层打开即暂停自动出牌/看门狗"护栏。

### Changed
- 【瓦库形态】文案贴合实际效果（接管每回合从左到右出牌 + 最大能量 +1），flavor 为
  "让瓦库玩算你赢了。"
- 新增诊断日志三件套（可见性链/转场黑幕扫描/奖励遮挡检查），便于未来排查同类黑屏。

## [v1.35] - 2026-08-23

### Fixed
- Fake Merchant (商人？？？) event: purchases were always charged to and granted to the character
  that entered the room (usually character 1), no matter who was browsing the rug. The shop UI
  binds to that instance's `MerchantInventory`, whose entries hard-wire their buyer at creation.
  Shared custom-layout events are now rebuilt per foreground character on switch
  (`RefreshEventRoomForControlledPlayer`), so each character browses and buys from their own
  stock with their own gold, matching vanilla multiplayer semantics.
- Fake Merchant event: only the first character could throw the Foul Potion (浑浊药水)
  to start the fight; with the second character controlled, the potion popup's throw button stayed
  disabled and a forced use would consume the potion with no effect. Root cause: character
  switching re-points `EventSynchronizer._localPlayerId` at the foreground player, but this shared
  custom-layout event attaches its `NFakeMerchant` UI node only to the event instance of whoever
  entered the room, so `EventRoom.LocalMutableEvent.Node` was null for the other character.
  New `FoulPotionPatch` falls back to the live custom-event screen / sibling instances when
  resolving the merchant button (`GetFoulPotionMerchantTarget`) and completes the throw on behalf
  of the vanilla branch in `FoulPotion.OnUse`; combat and real-shop branches are untouched.
  Character switching is also ignored while a throw settlement is in flight so the fight can
  never lose a combat-ready signal.

## [v1.34] - 2026-08-22

### Fixed
- Relic "Silken Tress" (华美发束): the promised Glam enchantment never reached the card rewards
  you actually see, while the relic itself was already marked as used up after the first combat.
  Root cause: vanilla `CombatRoom.OfferRoomEndRewards` pre-generates a reward set per character,
  and one-shot relic hooks (Silken Tress's `IsUsed`, egg upgrade counters, Silver Crucible, …) were
  consumed there — enchanting cards that were then thrown away. The mod's merged-rewards screen
  regenerated fresh sets afterwards with every relic already spent. A new intercept on
  `CombatRoom.OfferRoomEndRewards` (`CombatRoomOfferRoomEndRewardsPatch`) now generates each
  character's rewards exactly once in local self-coop and preserves the vanilla
  `Hook.BeforeCombatRewardOffered` step, so Silken Tress enchants the displayed rewards and other
  modify-once relics apply correctly too.
- Crystal Sphere event: only the first character could complete their divination — finishing it
  ended the event for everyone. Two causes: the divination overlay stayed on the overlay stack
  after completion (the event auto-switch chain defers while overlays are open), and the overlay's
  own PROCEED button called `ProceedFromTerminalRewardsScreen`, opening the map directly. The event
  option handlers now close finished divination overlays when they complete so the existing
  auto-switch chain walks to the next character, and the minigame PROCEED is intercepted while any
  character still has an unfinished Crystal Sphere (switching to them instead of leaving the room).

### Changed
- Crystal Sphere settlement is now strictly per-character: paying "Uncover Future", the Payment
  Plan Debt curse, and revealed gold/relic/potion/card rewards stay with the revealing character.
  This supersedes the original author's mirror-settlement design (everyone pays together, loot is
  copied to everyone); all cross-player mirroring for this event is disabled behind the
  `CrystalSphereMirrorRuntime.CrossPlayerMirroringEnabled` switch (= false) if shared settlement
  is ever wanted back.
- Character hotkey switching (Tab / Shift+Tab / legacy keys) is ignored while a divination
  minigame is in progress. Switching away previously left the minigame completing under another
  character's context, failing its owner check in `DoLocalCrystalSphereRewards`, so the event could
  never finish (softlock).

## [v1.33] - 2026-08-21

### Added
- Built-in Oddmelt compatibility: Oddmelt's hidden Gauge input cards (GaugeSummonActionCard /
  GaugeUltimateActionCard / GaugeBurstActionCard) are deliberately registered in no card pool, so
  rebuilding the combat hand UI on character switch called `NCard.Create` on them and hit
  "is not in any card pool!" (`InvalidProgramException`), rolling the switch back. A guard prefix on
  the game's `NCard.Create` (`NCardCreateHiddenCardGuardPatch`) now returns null for cards whose
  pool cannot be resolved; every caller already null-checks, so such cards are skipped exactly as
  Oddmelt intends. Without Oddmelt installed the guard is a no-op. This supersedes the separate
  `OddmeltGaugeCardRenderFix` mod, which is no longer needed.

## [v1.32] - 2026-08-21

### Fixed
- Local multiplayer: `[HarmonyPatch]` attributes placed only on methods (inside a class without a
  class-level `[HarmonyPatch]`) were silently skipped by `PatchAll`, so several patches never ran —
  including the combat card-selection foreground switch and the serialized hand selection. All patch
  classes now carry a class-level `[HarmonyPatch]` marker/target (`CardSelectForegroundSwitchPatch`,
  `NPlayerHandSelectCardsSerializationPatch`). Init now logs the patched-method census.
- Local multiplayer: `NPlayerHand.SelectCards` prefix no longer uses `ref bool __runOriginal`
  (Harmony 2.4.2 generated invalid wrapper IL for this method, crashing mod init with
  `InvalidProgramException`); it now uses the proven bool-return + `ref Task<...> __result` pattern.
- Local multiplayer: when two locally-controlled characters both need a hand-card choice at the same
  sync point (e.g. both characters holding the GensokyoSpire boss Utsuho's "Meltdown" buff select a
  card to exhaust at turn start), the second `NPlayerHand.SelectCards` overwrote the single shared
  `_selectionCompletionSource`, permanently orphaning the first choice and softlocking that character
  (unable to play cards or end turn). Combat hand selections are now serialized: only one selection is
  active at a time, the later one asynchronously waits its turn, and the foreground is switched to the
  selecting character right before its prompt is shown. The selection's owner is tracked per async
  chain from the `FromHand`/`FromSimpleGrid` entry (an `AsyncLocal` in
  `CardSelectForegroundSwitchPatch`), falling back to the triggering model's owner, so interleaved
  selections from both characters are each shown against the right hand. The wrapper's re-entry guard
  also uses an `AsyncLocal` so sibling `SelectCards` calls from the action executor are still serialized
  while the first prompt is on screen (`NPlayerHandSelectCardsSerializationPatch`).
- Local multiplayer: turn-end / turn-start card-choice effects belonging to a backgrounded
  character no longer hang or get skipped. The game builds `HookPlayerChoiceContext` with
  `LocalContext.NetId` as `_localPlayerId`, so when the last-visible character differs from the
  character whose choice is running, `_gameAction.OwnerId != _localPlayerId` and the hook action is
  never enqueued locally (`HookPlayerChoiceContext.cs:194/206`). The mod now:
  - Forces `_localPlayerId` to the choice owner inside every `HookPlayerChoiceContext` constructor
    (`HookPlayerChoiceContextLocalPatch`), so the choice is enqueued and runs on the loopback.
  - Auto-aligns the controlled foreground (hand / local context / top bar) to each character in the
    turn loop before `SetupPlayerTurn`, `DoTurnEnd` and `FlushPlayerHand`, and synchronously before
    `CardSelectCmd.FromHand`/`FromHandForUpgrade`/`FromSimpleGrid`/`FromChooseACardScreen`/
    `FromCombatPile`. The previous deferred (`CallDeferred`) foreground switch could miss the
    synchronous wait for the choice, so it has been replaced with a synchronous switch guarded by the
    same in-play/in-selection/target-selection checks (`CardSelectForegroundSwitchPatch`,
    `CombatManagerTurnHookForegroundPatch`, `LocalMultiControlRuntime.TryEnsureForegroundForPlayer`).
- Character switching during an in-progress card play / card selection no longer rolls back the
  control context; the combat UI refresh is deferred until the flow finishes (bounded retries), and a
  stuck off-screen end-turn button is re-animated in (`LocalMultiControlRuntime`).
- Hook actions (e.g. Mini-Hakkero's turn-end hand selection for a backgrounded character) switch the
  foreground to their owner at enqueue time, before the selection UI appears
  (`HookEnqueueForegroundPatch`).

### Adapted
- Game v0.111.0 (beta111, 2026-08-13). The mod previously failed to load with
  `ReflectionTypeLoadException`. Changes:
  - `INetGameService`/`INetHostGameService` gained net-new members in v0.111.0. The local
    loopback host service (`LocalLoopbackHostGameService`) now implements `LocalVersion`,
    `ClientConnectionFailed` and `GetVersionInfoForPeer`.
  - `LoadRunLobbyPlayer` replaced its `versionInfo` field with a flat `isModded` bool; the
    load-lobby auto-ready patch now writes `isModded` from the net service's local version.
  - `StartRunLobby.MaxPlayers` was replaced by a constructor-injected readonly `_maxPlayers`
    (no auto-property backing field). Local self-coop lobbies are now created at the full 12-player
    capacity up front and the reflection-based `EnsureLobbyMaxCapacity` resize was removed.
  - `CombatManager`'s ready-to-begin-enemy-turn set moved onto the turn state; the ready-set lookup
    tries the new location first and falls back to the pre-beta110 field.

## [v1.31] - 2026-07-27

First community-maintained release. The original author (liwenhao0427) discontinued
maintenance at v1.30 and gave written permission (2026-07-27, email) for this fork to
take over maintenance and distribution; they will cross-link this version from the
original Workshop item and video.

> Note (2026-08-21): the paragraph above is preserved from the v1.31 release notes written by
> the previous maintainer (GuyGinat). The current maintainer has not verified that
> authorization and makes no such claim.

### Added
- Optional "ghost hands" combat overlay: shows every backgrounded character's current hand as rows of non-interactive cards behind and above the active character's hand. Toggle with `F8`; move the display at runtime with `Ctrl+Arrows` (`Ctrl+Shift+Arrows` for fine 4px steps). State, position and scale persist to `user://dual_role_adventure_settings.json` (edit `ghostHandsScale` there to resize; default 0.5). Card nodes are borrowed from and returned to the game's own `NodePool`.

### Changed
- Character switching is now bound to `Tab` (next) and `Shift+Tab` (previous). The legacy keys — `]` / `R` / `/` for next, `[` / `T` for previous — still work as aliases.
- Documentation translated to English; original Chinese documents archived under `docs/archive/`.

### Fixed
- Adapted to game v0.109.0 (2026-07-17); five compile-level API breaks since the v1.30 baseline:
  - `VoteToMoveToNextActAction` gained a required `currentActIndex` parameter; the act-change auto-ready patch now passes `RunState.CurrentActIndex`, mirroring the game's own call site.
  - `PotionFactory.CreateRandomPotionsOutOfCombat` now returns `IEnumerable<PotionModel>`; the local merchant inventory rebuild materializes it with `.ToList()` like the game does.
  - `Controller` input constants were renamed (`joystick*` → `lStick*`, `dPad{East,West,North,South}` → `dPad{Right,Left,Up,Down}`); updated the gamepad axis router and the character-select hotkey hint icons.
  - `EventModel.GenerateInternalCombatState(runState)` was removed; rebuilding a non-shared combat-layout event room on character switch now calls `EventSynchronizer.GenerateInternalCombatStateIfNecessary(event)` instead.
- Local Multi-Control entry unreachable on fresh profiles: since v0.109.0, pressing Host with `Progress.NumberOfRuns == 0` skips the host submenu and immediately hosts a Standard online game, so the injected card never appeared. A new prefix on `NMultiplayerSubmenu.OnHostPressed` routes fresh profiles through the host submenu (its Standard card keeps the one-click hosting behavior).

### Notes
- `NRestSiteRoom.UpdateNavigation` no longer exists in v0.109.0; the rest-site controller focus recovery already guarded that lookup with a null-conditional call, so it degrades to the mod's own focus-grab fallback.
- Static sweep of all string-based Harmony/reflection targets against decompiled v0.109.0 source: everything else resolves (the two `BeginRunIfAllPlayersReady` misses are the intentional legacy-name fallback legs).

## [v1.30] - 2026-06-19

Final release by the original author. (Summarized from the archived player-update history.)

### Fixed
- Adapted to the game's 1.0 release: treasure rooms no longer black-screen (the removed `IsSinglePlayerOrFakeMultiplayer` API is no longer called; the chest gesture layer works with the new vote structure, with a focus guard for first-frame relic nodes).
- Shop inventory switching adapted to the new `Inventories` structure; ESC quick-restart moved to the release version's load method.
- Loading a multi-character save resumes correctly (`BeginRunForAllPlayersIfAllReady` with a reflection fallback to the legacy `BeginRunIfAllPlayersReady`).
- Reward synchronizer local-player checks (`RewardsSetSynchronizer`) are re-synced on character switch, fixing "clicked a relic reward, nothing happened" in the beta.

## [v1.17] - 2026-03-28

### Changed
- Reworked the loot phase: rewards are generated independently per character, then presented as one combined list with a per-character prefix label.
- Relic-doubling effects, shop-quality factors, and hunt-style card effects now apply per owning character.
- Removed the old resource-mirroring logic; relic/potion/gold mirroring in loot scenes is suppressed by the aggregated flow.

## [v1.10] - 2026-03-22

### Fixed
- Vakuu auto-switch could miss its trigger: added a per-frame fallback that switches to the next controllable non-Vakuu character when no Vakuu character has a playable card.
- Standardized the English spelling `Vakuu`.

### Changed
- Vakuu toggle hint text unified across mouse and controller paths, localized in Chinese and English (single: "Vakuu will control Player X"; all: "Vakuu will control all characters").

## [v1.09] - 2026-03-21

### Added
- Controller combos on the character-select screen: `Y` toggles Vakuu for the highlighted character, `LT + Y` toggles all.
- Controller hotkey hint icons on the select screen (`LT + D-pad`, `Y`, `LT + Y`), shown only in controller mode like the base game.
- Full LT-combo input chain: while LT is held, native controller input is intercepted; on release, the original LT action replays only if no combo was used.

## [v1.06] - 2026-03-20

### Changed
- Potions are now maintained per character instead of being pinned to slot 1.
- Removed the special "+2 initial potion slots" rule; potion slots match online multiplayer behavior.
- The top potion bar follows the currently controlled character after switching and stays directly usable.

## [v1.05] - 2026-03-20

### Fixed
- Removed a duplicate copy path in the treasure chest patch that could double-copy in a single settlement.
- Chest relic copying now de-duplicates against already-owned relics.
- Treasure-map settlement handled once for the whole party, with automatic view switching during Vakuu flows.

## [v0.1.9] - 2026-03-15

### Fixed
- Rest sites no longer occasionally auto-upgrade a random card and end without manual selection; manual card selection outside combat restored.
- Rest sites run strictly serially: each character chooses once before the site ends — no more skipped characters.
- Event branches that upgrade/remove cards process every unfinished character with the same option, one by one.
- Mouse visibility restored after switching characters at a treasure chest.

## [v0.1.3] - 2026-03-15

### Changed
- Combat character-switch buttons redesigned as pure icons with scaling and right-side mirroring.
- Character-select 2x2 button group rearranged to match combat-screen interaction style.
- Multiple rounds of screenshot-driven tuning: arrow positions, margins, horizontal spacing, readability.

## [v0.1.2] - 2026-03-14

### Changed
- Release process upgraded to support "fast releases" (publish directly from existing artifacts in the project root).

## [v0.1.1-clearable] - 2026-03-14

### Fixed
- Combat top-bar display glitch on entering combat.
- Shared-event auto-voting flow blockage.

## [v0.1.0-initial-usable] - 2026-03-13

### Added
- Minimal usable loop for local multi-control.
- Basic input switching, key synchronization chains, and the core patch framework.

[Unreleased]: https://github.com/nanthepsmith-droid/STS2_DualRoleAdventure/compare/v1.32...HEAD
[v1.32]: https://github.com/nanthepsmith-droid/STS2_DualRoleAdventure/releases/tag/v1.32
[v1.31]: https://github.com/GuyGinat/STS2_DualRoleAdventure/releases/tag/v1.31
[v1.30]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v1.30
[v1.17]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v1.17
[v1.10]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v1.10
[v1.09]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v1.09
[v1.06]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v1.06
[v1.05]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v1.05
[v0.1.9]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v0.1.9
[v0.1.3]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v0.1.3
[v0.1.2]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v0.1.2
[v0.1.1-clearable]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v0.1.1-clearable
[v0.1.0-initial-usable]: https://github.com/liwenhao0427/STS2_DualRoleAdventure/releases/tag/v0.1.0-initial-usable
