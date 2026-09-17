# Changelog

Notable versions and key changes of `LocalMultiControl` / `DualRoleAdventure`. Entries up to v1.30 are translated from the original author's Chinese changelog; the fuller day-by-day history lives in `docs/archive/player-update-history.zh.md`.

## [Unreleased]

### Added
- **瓦库自动用药补 3 条规则（2026-09-17，r136）**：覆盖率对账（`tools/coverage_digest.py`，报告
  `maintenance-docs/combat-hook-coverage.md`）抓出「一览表写了使用时机、但规则表与代码里都没有」的
  三种原版药水 ⇒ 按一览表写的时机补实现（`LocalWakuuPotionAutoUse` 规则表 +3、单测 +3）：
  **肌肉药水**（`FlexPotion`：精英/Boss **手牌有攻击牌**时）、**缚魂药水**（`PotionOfBinding`：
  精英/Boss 首回合对敌）、**欧洛巴斯之酸**（`OrobicAcid`：精英/Boss 首回合）。
  此前这三瓶走「未收录原版药水保守跳过」分支，瓦库**永远不会自动使用**（真人手动用药不受影响）。
- **离线静态自检 + 静态层 CI（2026-09-16）**：新增 `Scripts/Tools/static_checks.py` —— **不需要游戏安装**的
  6 项检查：① 产物/反编译源码未入库（`git ls-files`）；② 含非 ASCII 的 `.ps1` 必须带 UTF-8 BOM；
  ③ 根 json / `workshop\content` json / `mod_manifest.json` 三处 `version` 一致；④ 补丁类级
  `[HarmonyPatch]`（复用 `patch_coverage` 口径，方法级-only 必须为 0 —— 本 mod 坑 1 的静默跳过）；
  ⑤ csproj 的 `src/**`、`sts2src/**` 源码隔离；⑥ `Entry.cs` 的 `BuildMarker` 身份。
  同时新增 `.github/workflows/static-checks.yml`（push / PR / 手动触发）。
  ⚠ 构建、单测、CLR/ABI、部署校验**仍必须本机跑**（要游戏安装里的 `sts2.dll` / `GodotSharp.dll` 等），
  不在 CI 范围内。
- **本机一键门禁 `Scripts/Tools/preflight.ps1`（2026-09-16）**：把 AGENTS.md §9 全链串成一条命令 ——
  默认只跑静态层（G1 离线静态自检、G2 字符串/反射目标核对），`-Build` 加 G4 全仓库构建 + 主 mod 单测门槛，
  `-Deploy` 加 G5 构建 + 部署 + 槽位身份 + 字节校验，`-WithLogs` 加 G6 初始化终态 / G7 健康度计数，
  `-Lint` 加 G3 diff 预审；输出**单一 PASS/FAIL 表 + 退出码**（不再是"一堆日志"）。
- **运行期目标基线（static_checks S7，2026-09-16）**：新增 `Scripts/Tools/baselines/targets.baseline.txt`
  （**336 个语义标识** = 197 补丁目标行 + 183 字符串/反射目标去重）与 `--update-baseline` / `--strict` 两个开关。
  静态层把当前目标集合与基线比对，**新增/消失都打 WARN 并列出明细**（`-P…` / `+P…`），
  这是"游戏更新把某目标改没了 / 误改目标字符串"在**没有游戏安装的 CI 上**唯一的暴露点；
  基线缺失 = FAIL（不允许跳过即绿），`--strict` 时 WARN 也判失败。
  基线只记**语义标识**（补丁类 / 目标类型 / 成员 / 种类），不含文件与行号 ⇒ 换文件、挪行号无噪声。

### Removed
- **删除 `copy_pck_to_game.ps1`**：它把仓库根的 `DualRoleAdventure.dll` **连同 `DualRoleAdventure.json`**
  一起拷进槽位，而本机槽位用的是改名后的 `DualRoleAdventurefixed.json`（id 同名契约）⇒ 同一目录会出现
  两个不同 id 的 json，游戏会把同一个 mod 加载两遍。部署请**只拷 dll**，或用
  `Scripts/Tools/build_all_mods.ps1`（含全部门禁）/ `..\tools\deploy_dll.ps1`（只部署）。
- **删除 `Scripts/Tools/BuildRelease.ps1`**：能力已并入 `release_build.ps1`（新增 `-PublishGitHub` / `-PushGit` /
  `-ReleaseNotes`：提交三处版本 json → 打 tag `v{major}.{minor}` → 可选推 `origin` → `gh release`），
  发布由此仍是**一条命令**（AGENTS §7 第 5 步已同步）。

### Changed
- **维护工具与文档路径纠正（2026-09-16）**：`patch_coverage.py` docstring 里的示例路径从仓库外的
  `tools\` 改回 `Scripts/Tools/`（该脚本一直只存在于 `Scripts/Tools/`，历史文档里的旧路径会误导）；
  `AGENTS.md` §2 与 `README.md` / `README.zh-CN.md` 的部署说明改为"只拷 dll、不要往已有槽位塞第二个 json"。
- **门禁脚本随仓库（2026-09-16）**：`dll_check.py`（门禁 6）与 `log_parser.py`（门禁 9）从仓库外的
  `..\tools\` 迁入 `Scripts/Tools/`，AGENTS §2/§9、`build_all_mods.ps1`、`tools/README`、references 与 skill 同步；
  同一改动里把 `build_all_mods.ps1` 两处「脚本缺失就跳过」改为 **FAIL**（AGENTS §9：
  「缺文件 = FAIL，不允许跳过即绿」——原先缺 `clr_compat_check.py` / `dll_check.py` 会静默变绿）。
  另：原 `BuildRelease.ps1` 含中文注释却缺 UTF-8 BOM（PowerShell 5.1 下会按 GBK 解析报语法错），
  该文件已随本次合并删除。
- **设置页全面中英文双语 + 瓦库出牌设置归位（r135，2026-09-15）**：
  - `LocalWakuuConfigSubmenu` 全量本地化：标题、三节页眉、所有开关/策略/档位行的标题与描述、
    底部提示均改为 `LocalModText.Select(中文, English)`，英文界面（`eng`）下显示英文，
    中文与其他语言照旧显示中文。档位按钮显示文本（第一个/最后/随机/稀有度最高、角色优先/总量优先/
    只看角色、不跟随/仅关键节点/全程跟随、仅个人/个人+社区兜底/融合、左下/右下/右上/左上）同步本地化。
  - **修复 `NextChoiceMode` 按显示文本循环的隐藏 bug**：原先按按钮的中文显示文本取「下一个」档位，
    英文界面下会落到错误分支（永远回到「第一个」等）；改为按语言无关的配置常量取值循环，
    中文/英文行为一致。其余 Next* 切换器本就按取值切换，未受影响。
  - **瓦库出牌 3 项设置移入「瓦库托管」区**：`fastWakuuPlay`（瓦库出牌加速）、`wakuuPlayQueue`
    （实验·走动作队列）、`wakuuPlayOverlap`（实验·并发出牌）从「其它设置」移动到「瓦库托管」页，
    紧随「打光所有手牌」之后；这三个开关只在开启托管时有意义，归位后用「其它设置」区不再混入托管相关项，
    页眉说明同步调整为「以下功能与总管开关无关」。配置键与存储位置不变。
  - **mod 元数据 JSON 修复双重编码乱码并补英文**：根目录 `DualRoleAdventure.json`、
    `workshop\content\DualRoleAdventure.json`、`mod_manifest.json` 从「UTF-8 双重编码乱码 +
    未转义引号」重写为干净的双语 UTF-8（游戏侧 `ModManifest` 无 per-language 字段，中英混排一行），
    `name`/`description`/`tag`/`detail`/`remark`/`social_url` 等字段均有英文；
    保留 `id`/`author`/`version`/`min_game_version`/`has_pck`/`has_dll`/`dependencies`/
    `affects_gameplay` 与飞书链接字段。`BuildRelease.ps1` / `release_build.ps1` 的
    「不可 ConvertFrom-Json 解析」过时注释同步修正（仍保持正则替换以稳定字段排版与 BOM）。
  - **设置入口按钮本地化**：`NSettingsScreenConfigMenuPatch` 注入到「设置 → 常规」页的两处文案
    「瓦库托管 / 打开设置面板」同步走 `LocalModText.Select`（英文界面显示 Vakuu Autopilot /
    Open Settings Panel）。✅ 2026-09-15 实机确认（英文界面）：设置页全英文、出牌 3 项已就位、
    档位按钮循环正确，玩家已验收后合并分支。
- **队列路径出牌加速（r132，2026-09-14）**：给 `CardModel.OnPlayWrapper` 打前缀补丁
  （`CardPlayVisualsSkipPatch`），瓦库**走动作队列**（`wakuuPlayQueue`）的那张牌也强制
  `skipCardPileVisuals: true`。此前「瓦库出牌加速」只对 inline `CardCmd.AutoPlay` 生效 ——
  队列路径由 `PlayCardAction` 自己以 `isAutoPlay: false` 调 `OnPlayWrapper`，**没有**这个形参可传
  ⇒ 实机上「两个实验档 + 加速都开」会表现为加速失效（队列出牌单张回到约 1 秒）。
  现在队列出牌同样跳过**收尾固定等待**与**结算堆**（弃牌/消耗/移出战斗）的移动补间 ——
  后者才是大头：原版 `CardPileCmd.Add` 结尾是 `await tween.AwaitFinished(...)`，即"卡牌飞向弃牌堆"
  那段是要等的（实机体感"快了很多"即来自这里）；"牌从手牌飞出"那段真人分支动画仍省不掉。
  判定只认「**我们替瓦库入队**的那张牌」（`LocalWakuuRelicRuntime.HasPendingQueuePlay`）——
  真人手动替瓦库出牌不加速；关掉 `fastWakuuPlay` 或队列档即行为与上一版完全一致。
  设置页文案同步改正（原先写的「本项会盖过加速、二者取一」已不成立）。

### Fixed
- **队列路径出牌加速导致卡面滞留在出牌区（r133，2026-09-14）**：`skipCardPileVisuals` 是一票两用 ——
  它既跳过收尾的固定等待，也跳过**"把卡牌节点从出牌区收走"的换堆补间**，而原版正是靠那条补间
  `QueueFree` 节点。inline 路径（r117）没这个问题（它的节点从没建过）；队列路径的节点由
  `CardPileCmd.AddDuringManualCardPlay` 建（没有跳过形参），于是建了却没人收 ⇒ 卡面留在出牌区。
  修法：动作真正跑完后（`await action.CompletionTask`）由
  `LocalWakuuRelicRuntime.ReleaseLeftoverPlayedCardNode` 收回滞留节点（能量牌不动、仍在出牌区/抽牌堆不动、
  去手牌则交还手牌容器、其余直接释放），找不到节点即空操作。**不影响加速本身**。
- **战后「卡牌奖励」不再漏领（BUG-13，r134，2026-09-14）**：瓦库的金币/药水奖励照常自动领取，
  但卡牌奖励会失败并留在奖励界面（日志 `瓦库奖励自动领取失败，保留为人工领取: reward=CardReward,
  error=Card selector unset during test!`）。
  根因：`CardReward.OnSelect` 读 `CardSelectCmd.Selector`，而该选择器会被归属守卫按 AsyncLocal
  `CurrentChoicePlayerId` 决定是否"摘成 null"（真人选牌时跳过我们压的托管选择器）；该 AsyncLocal 会沿异步链
  残留，而**卡牌奖励这条链不经过任何 `CardSelectCmd.From*` 入口**、没人写它 ⇒ 残留的旧值（多半是真人）
  让守卫判成"真人选牌" ⇒ 选择器为 null ⇒ 抛异常 ⇒ 自动领取失败（金币/药水不读它，所以没事）。
  修法：领取前写入选牌归属者（保存/恢复），与事件自动选择 / 遗物效果自动作答同一套既有做法；
  命中该坑时会多一条 `卡牌奖励自动领取：选牌归属者在异步链上残留为其他角色，已改写为奖励归属者` 便于确认。
  ✅ 2026-09-14 实机确认（marker r134，2 场战斗 × 5 瓦库）：卡牌奖励自动领取 10/10、失败 0。

## [1.41.0] - 2026-09-13

> v1.41 = r105~r130（2026-09-10 起）：本地多控体验优化（回合开始演出跳过 / 瓦库托管视角三档 /
> 多瓦库出牌队列与并发出牌 / 真人插队 / 瓦库出牌加速）+ 瓦库事件选项接入个人统计 + 
> 个人记录器四张表的写时幂等与删牌/商店统计修复（BUG-9/10/11/12）。

### Added
- **「跳过他人回合开始抽牌演出」开关（改进-1，r105，2026-09-10，默认关）**：本地多控下回合开始会
  **依次把前台切到每个真人玩家、逐个播完其自动抽牌动画再切下一个**（`CombatManager.SetupPlayerTurn`
  前缀里的 `TryEnsureForegroundForPlayer`），2 人尚可、满员 10+ 人时要等很久。
  设置页「其 它 设 置」区新增开关（配置键 `skipTurnStartDrawAnim`）。开启后回合开始**只保留
  「当前正在看的那位」**的抽牌演出，其他人的回合开始不再切前台 —— 依据反编译
  `CardPileCmd.GetTweenForCardsChangingPiles` 的既有门禁
  （`if (!LocalContext.IsMe(owner) && 不涉及 Play 堆) continue;`），原版对非本地玩家的 Draw→Hand
  **本来就不建卡牌节点、不做补间**，所以这些抽牌瞬时生效、数据完全照常；之后切到该角色时
  `RefreshCombatUiForControlledPlayer` 会按手牌区重建 UI，手牌完整可见。
  判定口径抽为纯函数 `TurnStartDrawAnimPolicy.ShouldSkipSwitch`（+5 单测），只在回合开始这一条
  路径生效（回合结束 / 弃牌不适用，保持既有观感）；跳过时打一条
  `已跳过回合开始抽牌演出（非前台玩家，改进-1）: player=…, foreground=…, round=…`。
  默认关 = 与既有观感完全一致。357 单测全绿，marker r105。
  ✅ **2026-09-10 用户实机确认**（日志实证 9 条 `已跳过回合开始抽牌演出（非前台玩家，改进-1）`，覆盖 round 1~4）。
- **「瓦库托管视角」三档策略（改进-2 Phase 0，r107，2026-09-10，默认不跟随）**：多瓦库托管时，
  真人视角会被反复抢过去（回合开始/结束、Hook 入队、出牌前各自为政）。新增配置键 `wakuuViewMode`
  （设置页「瓦库托管」区新增循环按钮）：**不跟随**（默认）= 瓦库全程不抢视角；**仅关键节点** =
  只在瓦库回合开始时切过去一次（看到轮到谁、抽了什么）；**全程跟随** = 上面四处都跟随
  （≈ 关闭「后台托管」的观感）。判定收敛为纯函数 `WakuuViewPolicy.ShouldSuppressSwitch`
  （档位 × 触发场景 × 三条不变式），`CombatManagerTurnHookForegroundPatch` / `HookEnqueueForegroundPatch`
  / `CardSelectForegroundSwitchPatch` / `EnsureWakuuPerspective` / 安全网全部改走它，不再各自为政。
  **两处防软锁兜底不受档位影响**（用户拍板：「不然软锁死我就炸了」）——作用域外需要真人处理的选牌、
  安全网超时救援，该切就切；「后台托管」关闭时档位不生效（等价全程跟随，向后兼容）。
  安全网候选甄别改用与档位无关的 `IsBackgroundHostedWakuu`。+12 单测（`WakuuViewPolicyTests`）
  → **369 全绿**，marker r107。
- **瓦库「作用域外」选牌自动作答补齐 `FromSimpleGrid`（r108，2026-09-10）**：实机日志（marker r107）显示
  战斗开始时【工具箱】类遗物给的无色牌三选一走的是 `CardSelectCmd.FromSimpleGrid`，而
  `CardSelectWakuuTurnStartAutoAnswerPatch` **只覆盖了 `FromChooseACardScreen`** → 后台瓦库的这次选牌
  没人自动作答，只能切前台让真人替它选。补上同款前缀（复用同一 `ShouldAutoAnswer` 判据 +
  `prefs.MinSelect/MaxSelect`），瓦库直接按 `cardPickMode` / 智能选牌作答；`ShouldAutoAnswer` 同时被
  `CardSelectForegroundSwitchPatch` 复用，命中时**跳过无意义的切前台**（顺带消灭「跳到瓦库又跳回来」的闪一下）。
- **「仅关键节点」档位改为 peek（看一眼自动切回）+ 与「改进-1」解耦（r108，2026-09-10）**：
  r107 的 keyNodes 靠「回合开始不抑制切换」实现，但紧接着被同一钩子里的 `改进-1`（`skipTurnStartDrawAnim`）
  再拦一次 → 整段 keyNodes 只有跳过日志、**档位实际无效**（实机 marker r107 实证）。
  修法：`ShouldSkipTurnStartDrawAnimationFor` 跳过「后台托管的瓦库形态角色」（瓦库的回合开始视角完全由
  档位决定，改进-1 只管真人）；keyNodes 放行后由新增
  `LocalMultiControlRuntime.ScheduleReturnToForegroundAfterPeek` 在约 1.2 秒后自动切回原真人视角
  （用户已手动切走、或有进行中的出牌/选牌/瞄准则不抢）。纯逻辑新增
  `WakuuViewPolicy.ShouldPeekAtTurnStart`（+3 单测）。**372 单测全绿**，marker r108。
- **瓦库打出「手牌变换」类牌会中断（r109，2026-09-10）**：实机（marker r108）瓦库打出 YUI「数据链」
  （把旁边的牌变成数据链）与酒狐「不等价交换」等**手牌变换**牌时，日志
  `瓦库选择器作用域异常退出: player=…327, round=1, error=Couldn't get hand node for original card CARD.INJURY (50470768)!`
  → 牌停在屏幕中间、效果没跑完、也没消耗（本局共 18 次，3 种原牌）。
  根因：原版 `CardCmd.Transform` 视觉分支在 `if (!LocalContext.IsMine(cardAdded)) continue;` 之后会
  `NCard.FindOnTable(original, PileType.Hand)`，找不到就抛异常；而瓦库自动出牌期间
  `RunWatchdogAsync` **已把 `LocalContext.NetId` 钉在瓦库身上** → 瓦库自己的手牌被判成"我的牌"，
  于是去**前台手牌**（显示的是真人手牌）找原卡节点 → 必然找不到 → 异常抛穿异步链 → 出牌中断。
  `CardTransformNetIdPinPatch`（r59）只覆盖了「要不要**钉** NetId」，没覆盖「NetId **已经**是牌主人」。
  修法：判定抽为纯函数 `CardTransformNetIdPolicy.Decide(...)`，补第三种处置
  **`ShiftAwayFromOwner`** —— 后台角色变换时把 NetId 临时让到当前前台玩家（一个合法本地玩家），
  让原版按 `IsMine=false` 跳过视觉；数据层在视觉分支之前就已生效，不受影响。+6 单测 → **378 全绿**，marker r109。
- **两条历史小尾巴清理（r110，2026-09-10）**：
  ① **清理期异常不再误报**：退出这一局/回主菜单时 `RunManager.CleanUp` 会拆掉游戏状态，而此时
  瓦库自动出牌作用域可能仍在飞（探针 `source=run-cleanup, …, inFlight=1`），异步链里访问已失效的
  Nullable 会抛 `Nullable object must have a value.`，日志出现 `瓦库选择器作用域异常退出` +
  `瓦库看门狗重启失败` 两条 WARN。判定抽为纯函数
  `WakuuTeardownPolicy.ShouldTreatAsExpectedAbort(runInProgress, hasRunState)`，命中即降级为 INFO
  且**不上抛**（上抛会让看门狗再报一条失败，把一次正常退出记成两个问题）。
  ② **磁盘历史脏值自愈**：旧版（r84 之前）会把 `statBadgeCorner` 的值误写进 `wakuuBrain`，
  盘上留下 `"wakuuBrain": "bottomRight"`（被 `Normalize` 兜成 heuristic，无功能影响但日志/排查误导）。
  加载配置时用 `LocalWakuuAutopilotConfig.TryRepairHistoricalValues` 把所有字符串型策略字段归一
  并写回，**只改一次**（已合法则不写盘）。+10 单测 → **388 全绿**，marker r110。
- **✅ 2026-09-11 实机确认（marker r110 日志实证，r107~r110 全部闭环）**：
  ① 脏值自愈 WARN **仅 1 次**，随后两个配置快照均为 `wakuuBrain=heuristic`（原 `bottomRight` 已消失），
  第二次加载不再告警 → 「只写一次」成立；② 全文 `Nullable` 仅剩 2 处且**均为 BaseLib 第三方**
  `SavedProperty…System.Nullable`1[System.Int32]`，我们那条清理期异常**已彻底消失**；
  ③ **r108** 作用域外自动作答命中 3 次（`FromChooseACardScreen`×1 + **`FromSimpleGrid`×2**）
  → 真人不再需要替瓦库选牌；④ **r109** 后台手牌变换 3 次 `[手牌同步修复] … NetId …327 -> …326`，
  **再无** `Couldn't get hand node for original card`；⑤ **r107** 视角档 `never` 生效，出场各点
  均按 `WakuuViewPolicy` 跳过切前台。启动日志
  `BUILD_IDENTITY commit=5c295c3 state=clean` 反证部署位二进制就是那份干净提交。
  **补 2026-09-11**：唯一未验证项「仅关键节点 peek」也已实机确认 —— 切过去看一眼后约 1.2s
  自动切回真人，round 1 / round 2 各一次完整闭环（日志实证见 `TODO.md` ⑫）。至此本轮验证项全部出清。
- **已知项（暂不修，完整记录见 `TODO.md` § BUG-8）**：当局出现 **1 次**
  `瓦库选择器作用域异常退出 / 瓦库看门狗重启失败 … error=OrbQueue is full`。追查确认这是
  **游戏原生异常**（`Core/Entities/Orbs/OrbQueue.cs` 在 `Orbs.Count >= Capacity` 时抛
  `InvalidOperationException`；`OrbCmd.Channel` 先 `EvokeNext` 腾位再 `TryEnqueue`，第二步仍可能抛），
  由**真人自己**打出充能 orb 的牌（`CARD.IGNITION, owner=…326`）触发、冒泡到瓦库作用域。
  本次出牌 pass 被中断，但看门狗随即 `重新进入全量出牌模式` → **自愈、未软锁**，整局 3 回合仅 1 次。

### Added
- **期望补丁清单升级到完整类型名 + 签名级，并纳入单测门禁（r92，2026-09-08）**：
  `Entry.CriticalPatchTargets` / `OptionalPatchTargets` 由 `"Type.Method"` 简写改为
  `"MegaCrit.Sts2.Core.Commands.CardSelectCmd.FromHand"` 这类**完整类型名**，
  消除同名类型歧义；确有重载的三个目标钉死参数个数——
  `CardSelectCmd.FromCombatPile/4` 与 `/5`（两个重载本 mod 都打了补丁，r91 实机日志实证）、
  `PotionCmd.TryToProcure/3`、`CardSelectCmd.FromDeckForEnchantment/4`。
  配套新增 `tests/.../ExpectedPatchTargetsTests.cs`：用 `System.Reflection.Metadata` 读 sts2.dll
  元数据逐条核对「类型/方法存在 + 参数个数一致 + 格式合规 + 无重复」，
  **游戏更新或手误时单测先红**，不再等到实机才误报 Critical 缺失（那会 INIT_FAILED 让 mod 报红）。
  为让测试能读 internal 清单，新增 `Scripts/AssemblyInfo.cs` 的 `InternalsVisibleTo`
  （Godot.NET.Sdk 下 csproj `<AssemblyAttribute>` 不生效，见 BuildIdentity 的 r89/r90 踩坑）。

### Added
- **部署槽位身份门禁（r93，2026-09-08）**：游戏按槽位 json 的 `id` 认 mod、按 `<id>.dll` 加载，
  目录名可以与 id 不同（如 `mods\DualRoleAdventure\` 里其实是 `DualRoleAdventurefixed.dll`）。
  `build_all_mods.ps1` 新增 `Test-SlotIdentity`：**本仓库已知 mod 的「槽位 json id ≠ 部署 dll 主文件名」
  或「id 对应的 dll 不存在」= FAIL**（典型成因：部署到错槽位 / 改了 dll 名没同步 json / json 被别的 mod 覆盖）；
  另加 `Find-SlotIdDllMismatch` 全槽位扫描（含备份槽与第三方 mod）只 WARN，避免误杀不同名写法。
  `-List` / 构建部署 / `-CheckOnly` 三种模式都会跑。

### Fixed
- **手牌顺序与数据不同步（BUG-7，r111，2026-09-11）**：实机（marker r109）打出 YUI「数据链」/酒狐
  「不等价交换」等**手牌变换**牌后，牌面内容都对但**手牌顺序**与数据不一致（数据链只换**相邻**的牌，
  所以错位很明显），**切一次角色**（重建手牌 UI）才恢复。原有的机理假设（「数据层把替换牌加到
  手牌堆尾部」）经源码核对**不成立**：`CardCmd.cs:438-448` 走 `pile2.AddInternal(replacement2, 原索引)`、
  `CardPile.cs:90-97` 是 `_cards.Insert(index, card)` → **数据层保序**。真正脱节的是「屏幕上显示的手牌
  属于**非前台**角色」的窗口（切人后延后重建窗口等）——此时后台角色变换**故意跳过视觉**（r109），
  这份手牌 UI 就不跟随数据了。修法按一条不变量收口：*显示的手牌 UI 顺序必须等于前台角色手牌堆顺序*——
  ① 纯函数 `HandUiOrderPolicy.Decide(pileOrder, uiOrder)`（多重集比较，同名重复牌按次数算）；
  ② `LocalMultiControlRuntime.ReconcileDisplayedHandOrder`（250ms 节流，选牌/拖牌/出牌/有牌等待打出一律
  不干预；顺序不同 → 按数据重排）；③ 调用点 = `CardTransformNetIdPinPatch` 变换异步收尾（下一帧）+
  战斗逐帧兜底。+11 单测 → **399 全绿**，marker r111（⚠ 该版有回归，见下条）。
- **手牌变换后出牌中断回归修复（r112，2026-09-11）**：r111 实机立刻出问题 ——
  **打出「数据链」后会看到手牌重排一次，而打出的那张牌停在屏幕中间不消耗**。根因是 r111 判定过激：
  原版 `NCardTransformShineVfx.PlayUntilCardUpdate` 的视觉更新**故意延迟约 0.9s**
  （先等 `_overlayShowDuration(0.75) + _overlayIdleDuration(0.125)` 才 `UpdateCard`），
  这段窗口里「数据已是新牌、UI 还是旧牌」是**正常动画态**，却被判成「UI 有多余 + 数据有缺失 → 整体不一致」，
  于是排了一次**整表重建**，重建又在出牌流程刚解除时执行（`RefreshCombatUiForControlledPlayer` 会
  `hand.CancelAllCardPlay()` + 全量重建手牌）→ 打断数据链的出牌链（该局触发 5 次）。
  收口：`HandUiOrderPolicy` **只保留 `None` / `Reorder`**——只有「同一批牌、仅顺序不同」才重排
  （只 `MoveChildSafely` 调次序 + `ForceRefreshCardIndices()`，**不建节点、不删节点、不触发任何重建**）；
  「陈旧显示 / 多余 / 缺失」一律不处理，改为纯诊断：同一差异**持续 ≥1.5s**（远超 0.9s 动画窗口）才记一条 WARN，
  差异消失即重置。+11 单测（含回归用例「变换动画窗口内不干预」）→ **399 全绿**，marker r112。
  ✅ **2026-09-11 实机确认（BUG-7 关单）**：14 次「数据链」不再卡牌；`整表重建` / `战斗UI刷新顺延` **0 条**
  （r111 那局为 5+5）；`手牌UI顺序已按数据自愈` 命中 **3 次**（均 `重排=1`），
  前后对比实证「数据把新牌插在原位、UI 把它排在末尾」，重排后完全对齐；
  `存在差异但未处理` / `显示归属与前台不一致` 均 0 条（无误报）。
- **两个角色都带【工具箱】时，非前台那位被静默自动选卡（r106，2026-09-10）**：实机日志（marker r105）
  `工具箱自动接管已命中: player=…327, reason=background-player` → `工具箱已自动选择首张卡: card=RALLY`，
  即**后台那位真人的三选一被 mod 直接吞掉**，静默塞了首张无色牌；只有前台那位能看到选择界面。
  根因：`ToolboxPatch` 里那条 `background-player` 自动选卡是 **2026-03（d2c2c31）**为了绕开
  当时的「后台三选一阻塞」加的临时手段；2026-08 之后本 mod 的后台选牌链路已成熟
  （`CardSelectForegroundSwitchPatch` 把前台切到牌主人 + `ShouldSelectLocalCard` 强制本地手选，
  见 b949dfa 实证「作用域外选牌一律切前台交真人，改成自动作答会导致进战斗黑屏」），
  这条自动选卡就变成**有害**了：它把真人的选择静默吃掉。
  修法：`ShouldAutoPickFirstCard` 只对**瓦库托管角色**接管（`IsWakuuEnabled`），
  真人（含后台那位）走原版 `FromChooseACardScreen` → 自动切前台 → 真人自己选。
  （注：该现象在「跳过他人回合开始抽牌演出」开启时最容易撞上，因为回合开始不再逐个切前台，
  非前台玩家成了常态；但即使不开关它也是错的。）
  ✅ **2026-09-10 用户实机确认**（`FromChooseACardScreen` 对后台角色正常切前台 ×2；
  `工具箱自动接管已命中: reason=background-player` / `工具箱已自动选择首张卡` 均已 0 条）。
  用户拍板：**真人一律弹界面自己选，暂不做开关**。marker r106，357 单测全绿。
- **「真人先结束回合后，切到瓦库点结束回合无效」（r104，2026-09-10）**：真人先结束回合 → 自动切到瓦库 →
  点结束回合没反应，**切回自己再切到瓦库**才能点（BUG-2）。
  根因是**结束回合按钮的归属取错来源**：原版 `NEndTurnButton.CallReleaseLogic` 用
  `LocalContext.GetMe(...)` 决定「这次点击结束谁的回合」，而本 mod 的 `LocalContext` 会为
  **瓦库后台出牌的动作归属**临时漂移；一旦漂移到「已经结束回合的角色」身上，
  点击就被当成「撤销结束回合」处理（本 mod 的补丁还会直接把这种点击拦掉）→ 表现为点了没反应；
  切换角色会重新对齐上下文，所以「切回自己再切回来」就好了。
  修法（三条一起收口）：
  ① `NEndTurnButtonPatch` 的点击目标改为**前台玩家**（`Session.CurrentControlledPlayerId`，
  新增 `LocalMultiControlRuntime.AlignLocalContextToForegroundForEndTurn()` 在点击瞬间把上下文校正到前台，
  保证后续原版逻辑结算的正是玩家正在看的角色），不再直接信会漂移的 `LocalContext`；
  ② 新增**按钮自愈**：`LocalMultiControlRuntime.ReconcileEndTurnButtonForForeground()` 在战斗逐帧
  （`LocalCombatSwitchTracker._Process`，内部 500ms 节流）检查——前台角色确认可操作却按钮处于
  禁用/隐藏时重评一次。原版按钮只在 `TurnStarted`/`PlayerEndedTurn` 里切状态，而自动切人 /
  瓦库自动结束回合会绕开这些事件，按钮可能被留在旧状态（`Disable()` 后点击事件根本不会派发）；
  判定口径抽为纯函数 `EndTurnButtonReconcilePolicy.ShouldReconcile`（+8 单测）；
  ③ 按钮**文字与目标玩家对齐**：切到瓦库后不再残留「撤销结束回合」文案。
  另加 `结束回合点击门禁` 诊断日志（一次性打出 target/foreground/context/state/inputEnabled/focused/handMode），
  下次复现可直接定位是「按钮被禁用（连日志都进不来）」还是「判定取错玩家」。
  ✅ **2026-09-10 用户实机确认已修复**。marker r104，352 单测全绿。
- **「进入战斗后第一次切角色会被弹回自己」（r103，2026-09-10）**：战斗开始前停在瓦库视角时，
  进战斗后第一次切角色只看到一次刷新动画、人还停在原地，第二次才真的切到瓦库。
  实机日志（r102）实证：第一次热键确实切到了瓦库（`切换操控角色(指定): 326 -> 327`、
  `战斗UI已刷新到当前角色 327，手牌数量=0`），紧接着被
  `source=wakuu-no-playable-cards-tick` 的自动化**立刻弹回 326**——
  该自动化是「每回合一次」的（弹回后会把回合登记进 `_wakuuToNonWakuuSwitchedRounds`），
  所以第一次弹回把名额用掉了，第二次才停住，表现为"差一次"。
  修法：新增 `NoteManualSwitchToWakuu(source)`，在**手动**（`hotkey*` / `player-state*`）
  切到瓦库角色且该角色**当前确实没有可出的牌**时，直接把本回合登记为已处理，
  从第一次起就不再弹（有牌可出时不登记——那种情况自动化本来也不会切走，不能白占名额）。
  ✅ **2026-09-10 用户实机确认已修复**（第一次切角色即停在瓦库）。marker r103，344 单测全绿。
- **r102：次级资源战斗UI归属改为「跟随前台」——拦 RitsuLib 的刷新入口**：r101 的实机日志显示，
  入战时计数器**本来就绑在 1 号位且已隐藏**（`counters=1, bound=326, material=False, visible=False`），
  说明它是**之后**才被改回瓦库的。根因：RitsuLib 的 `SecondaryResourceCombatUiStateTracker` 把
  「本地玩家」等同于 `LocalContext.GetMe(...)`，而本 mod 的 `LocalContext.NetId` 会为**瓦库后台出牌的动作归属**
  临时漂移（`AlignContextForActionOwner`）→ 瓦库出牌期间 RitsuLib 把计数器重绑到瓦库（蕾克拉显示），
  出牌结束上下文回到真人 → 下次状态变化又隐藏，正是「进战斗后仍有蕾克拉、瓦库打完牌后自动消失」。
  修法：新增 `SecondaryResourceCombatUiOwnerPatch`（**ThirdParty 域**，反射定位字符串目标
  `STS2RitsuLib.Combat.SecondaryResources.SecondaryResourceUiRuntime:UpdateCombatUi`，`Prepare()` 找不到即跳过、
  并支持运行期延迟补挂），在它的**前缀**把归属玩家强制为**前台上场玩家**
  （新增 `LocalMultiControlRuntime.TryGetForegroundPlayer()`，只认 `Session.CurrentControlledPlayerId`，
  不受 LocalContext 漂移影响）；取不到前台玩家时保持原样。r101 的桥也统一改用同一前台事实来源。
  ✅ **2026-09-10 用户实机确认已修复**（蕾克拉不再残留）。marker r102，344 单测全绿。
- **r101 补强 r100：次级资源计数器必须「重绑」而不是「刷新」**：用户复测 r100 后仍有一种残留场景——
  蕾忍是瓦库且不是 1 号位、入战前停在瓦库视角时，进战斗后蕾克拉仍在，直到瓦库打完牌才自己消失。
  实机日志证明 r100 的桥确实生效（`已接入第三方次级资源战斗UI刷新入口: STS2-RitsuLib` +
  `第三方次级资源战斗UI已同步到当前玩家: player=326`），但可见性没跟着变：
  反编译可见 `NSecondaryResourceCounter.Refresh(player)` 里 `_hasBeenMaterial`（**曾经有过该资源就常显**）
  是粘滞标记，`Refresh` 不复位它——**只有公共 `Bind(player)` 在绑定玩家变化时才复位**，
  而挂载回调只调了 `Refresh`。修法：桥在调用官方入口之后，再遍历战斗UI子树里每个
  `NSecondaryResourceCounter` 显式 `Bind(player, autoRefresh: true)`；并打印一条带前后状态的诊断
  `第三方次级资源战斗UI已同步: player=…, counters=N, 详情=[bound=327, material=True, visible=True → ...]`。
  marker r101，344 单测全绿。
- **第三方「次级资源」战斗UI切换到别的角色后仍显示（r100，2026-09-10）**：LexNinja2 的**蕾克拉**计数器在切到
  其它角色后依旧显示。反编译定位：蕾克拉走 **RitsuLib 的次级资源框架**
  （`STS2RitsuLib.Combat.SecondaryResources`，挂载日志 `LEX_NINJA2_NODEATTACHMENT_LEX_KELA_COMBAT_COUNTER:
  NCombatUi -> NSecondaryResourceCounter`），其 `SecondaryResourceCombatUiStateTracker` **只在
  `CombatStateChanged` 时**用 `LocalContext.GetMe(state)` 刷新归属——在它看来「本地玩家」是固定的；
  本 mod 切前台不走该事件 → 计数器一直停留在上一个角色。
  修法：新增 `LocalThirdPartySecondaryResourceBridge`（**全反射 + 缓存 + try/catch**），
  每次重建战斗UI（切角色/入战）后调用 RitsuLib 自己的公共入口
  `SecondaryResourceUiRuntime.UpdateCombatUi(NCombatUi, Player)`，让它按新玩家重算可见性与数值
  （内部走 `NSecondaryResourceCounter.Bind/Refresh`，切玩家会复位「曾经有过资源就常显」的粘滞标记）。
  未安装 RitsuLib / 类型改名 / 反射失败都只记一次日志并静默跳过。marker r100，344 单测全绿。
- **辉星（储君/Regent 第二资源）在本地多控下完全不显示（r99，2026-09-10）**：r98 的实机日志显示归属其实是对的
  （`战斗能量归属核对: 能量=326, 辉星=326, 手牌=326(cardOwner)`，且无校正 WARN），
  用户复测也确认「单人储君正常、本地多控下**无论入战时是不是瓦库都不显示辉星**」。
  根因是**生命周期**而不是归属：原版把辉星计数器 `Reparent` 进能量球，而本 mod 每次切角色都会
  **重建能量球并 `QueueFree` 旧的**，辉星计数器作为其子节点被一并带走（单人模式不走我们的重建路径，所以正常）。
  修法：辉星计数器与能量球**彻底解耦**——新增 `EnsureStarCounterDisplay`
  （重建时直接从 `star_counter.tscn` 实例化，挂在**战斗UI**下 = 场景原始父节点，锚点相对全屏、
  位置即设计位置；不再塞进能量球、不再依赖反复 Reparent 后的偏移），每次切角色重绑 + 显式设置显隐，
  并打印一条 `辉星计数器就绪: player=…, alwaysShow=…, stars=…, visible=…, parent=…, pos=…` 诊断。
  ✅ **2026-09-10 用户实机确认已修复**（储君辉星在本地多控下正常显示）。marker r99，344 单测全绿。
- **辉星（储君/Regent 第二资源）不同步 + 入战切人时机提前（r98，2026-09-10）**：能量已同步但辉星仍串。
  ① **入战切人提前到 `NCombatRoom._Ready` 前缀**（新补丁 `NCombatRoomReadyForegroundPatch`，Combat 域已登记）：
  原先只在 `OnCombatSetUp` 之后才切回 1 号位，而原版 `NCombatUi.Activate` 与**第三方按 LocalContext 绑定本地玩家的
  mod**（如 RegentFX「万象辉星」在 `NCombatRoom._Ready` 里 `SetupStarRingForLocalPlayer`）都已经绑完了，
  于是能量球/辉星/星环全留在瓦库身上。前缀一定早于任何 mod 的后缀，第三方在 `_Ready` 后缀里做的绑定
  也能拿到正确的 me（只处理 `ActiveCombat`，战斗风事件房 VisualOnly 不抢事件归属）。
  ② **辉星纳入同一条不变量**：`EnsureCombatEnergyMatchesHand` 现在同时读辉星计数器归属，
  能量一致时也会单独校正辉星（日志 `辉星归属不一致已校正`），核对日志增加 `辉星=` 字段。
  ③ 修原版 `NStarCounter` 的订阅死角：`Initialize` 只有 `!_isListeningToCombatState` 才订阅 `StarsChanged`，
  该标志没人复位 → 重绑前改为「先退订旧玩家 + 复位标志」，否则重绑后辉星不再响应变化事件。
  marker r98，344 单测全绿。
- **r97 修正：r96 的修法两处都没落到实处（2026-09-10，BUG-1 复测仍未修复）**：r96 实机日志
  （marker r96，`PATCH_RESULT critical=25/25`、`INIT_OK`）实证两个错误：
  ① **入战刷新整体空转**——`RefreshCombatEnergyForCurrentPlayer` 的 `CombatManager.IsInProgress`
  门禁在 `NCombatRoom.OnCombatSetUp` 时还是 false（日志里「Combat started」发生在入战事件之后），
  三次调用（含两次延迟）**一条「入战能量显示已刷新」都没有**，能量球一直是原版 `NCombatUi.Activate`
  按入战瞬间 me（瓦库）建的那个；门禁放宽为「战斗房处于 `ActiveCombat` 也可刷新」。
  ② **归属基准取错**——r96 用入战瞬间的 `LocalContext`（=瓦库）当"手牌归属"，而真实手牌是开战后
  按**当前受控玩家**抽出来的，于是「基准=瓦库、能量球=瓦库」被判成一致，一次校正都没触发。
  改为**直接读手牌区里实际卡牌的持有者**（`NCardHolder.CardNode.Model.Owner`），
  手牌为空才退化到受控玩家；并去掉了不可靠的 `_lastCombatUiPlayerId` 追踪。
 ③ 回合开始核对除即时一次外，再排一次**帧末延迟**核对（手牌此刻才发得出来）；
  每场战斗打一条 `战斗能量归属核对: 能量=…, 手牌=…(cardOwner/emptyHand), 受控=…` 便于核对。
  marker r97，344 单测全绿。
- **战斗第一回合能量不同步（r96，2026-09-10，BUG-1）**：进战斗前把前台停在**瓦库托管角色**上时，
  第一回合会出现「手牌/UI 是真人玩家（战斗开始的默认第一个玩家）、能量球却是瓦库的
  （瓦库带【瓦库形态】+1 能量，数值也跟着串）」，手动切一次角色自愈。
  根因是**能量球与手牌分属两个玩家**：入战刷新/延迟刷新取的是「当前受控玩家」，
  而手牌归属是另一条链（原版 `NCombatUi.Activate` 按入战瞬间的 `LocalContext` 建，
  切前台时若 UI 刷新部分失败还可能出现回滚后「手牌新、能量旧」）。
  修法按一条不变量收口——**能量球必须与当前展示的手牌同属一个玩家**：
  ① 新增纯函数 `CombatEnergyOwnership.TryResolveMismatch`（手牌归属 &gt; 受控玩家兜底，
  取不到归属信息绝不动手），可单测；
  ② 战斗 UI 重建成功后记录手牌归属（`_lastCombatUiPlayerId`，写在能量球刷新**之前**，
  保证能量刷新抛异常时归属仍准），入战瞬间先记下原版 Activate 建好的那一版；
  ③ 入战的两次延迟刷新改为优先按手牌归属刷新，不再重读「当前受控玩家」
  （防瓦库在这 1~2 帧接走前台后把能量球单独刷走）；
  ④ 回合开始（`CombatManager.SetupPlayerTurn` 前缀）校正两次：切前台前一次、
  切完再来一次，只在真不一致时重建能量球并打 WARN
  （`战斗能量归属不一致已校正: 能量=…, 手牌=…, 受控=… → 重建为 …`）。
  ✅ **r97 起能量同步已实机确认**（2026-09-10）。正常回合零开销（归属一致直接返回）。单测 +7 → 344。marker r96。
- **瓦库拾遗物触发的「变化一张卡」等选牌仍弹屏等真人（r94，2026-09-09，修 r81 覆盖不全）**：
  r81 只在 `LocalWakuuRewardAutoClaim` 的 `RelicReward` 自动领取分支压入策略选择器，
  而**控制台授予 / 事件授予 / 商店购买 / 第三方效果授予**都不走 `RelicReward`
  ——实机日志实证：控制台给瓦库 YUI【灵草丹】后，`RelicCmd.Obtain` → `relic.AfterObtained()` →
  `CardSelectCmd.FromDeckForTransformation` 时选择器栈为空，又被
  `CardSelectManualConfirmationPatch` 强制 `RequireManualConfirmation`，弹
  `deck_transform_select_screen.tscn` 停住等真人。
  修法：把自动作答下沉到公共入口 `RelicCmd.Obtain`（`RelicCmdObtainPatch`），
  新增 `LocalWakuuRelicEffectAutoChoice`——获得者是瓦库托管角色时在整个 Obtain 期间压入
  Transform 场景策略选择器（原版 `FromDeckForTransformation` 里 `Selector != null`
  优先于 `RequireManualConfirmation`，压栈即自动作答、不弹屏）。
  两个时序要点：① 必须在**前缀**压栈（`await relic.AfterObtained()` 的调用发生在方法返回 Task 之前，
  兜不住）；② 选择器栈是静态 Stack，但 `CardSelectCmdSelectorGuardPatch` 按 AsyncLocal 的
  `CurrentChoicePlayerId` 判归属，故进入时同步写入瓦库 NetId，避免被异步链上残留的真人 NetId 摘掉。
  作用域在**原版 Obtain 完成后立即释放**，之后的「共享镜像给其它玩家」恢复真人手动。
  战斗进行中不接（战斗期另有出牌循环的选择器，且避开开局遗物二选一等依赖真人的场景）。
  r81 的窄分支已移除（功能被公共入口覆盖，避免重复作答日志）；新增 5 条单测（334 全绿）。
- **r94 场景细化（r95，2026-09-09）**：遗物触发的不止"变化一张卡"——删除/升级类遗物走的入口不同，
  smartPick 开启时用错优先级表会明显变差（Transform 表**硬排除**诅咒/状态/任务，
  而"拾取时删除一张卡"应优先删诅咒）。选择器新增**动态场景提供器**
  （`LocalWakuuStrategySelector.ScenarioProvider`，构造场景变兜底）；
  新增 `CardSelectDeckScenarioPatch`（Core 域已登记）按实际入口写场景覆盖：
  `FromDeckForRemoval` → `Remove` 表、`FromDeckForUpgrade` → `Unknown`（退回 cardPickMode，不套 Transform 表）、
  无覆盖（变化类）维持 `Transform`。单测 +3 → 337。marker r95。
- **`build_all_mods.ps1` 的 marker 解析同 r92 的 UTF-16 单对齐假阴性**：与 `dll_check.py`、
  `deploy_dll.ps1` 统一为扫两种字节对齐 + `YYYY-MM-DD-rNN` 形状优先匹配。
- **部署脚本 marker 解析假阴性（r92）**：`deploy_dll.ps1` 的 `Get-Marker` 只按偶对齐解码 UTF-16，
  而 #US 用户字符串堆的起始偏移可能是奇数 → 本次构建（新增字符串后）marker 落到奇偏移，
  部署后门禁 7 直接报「未能解析 marker，拒绝视为部署成功」。改为与 `dll_check.py` 一致扫两种对齐，
  并补 `marker=YYYY-MM-DD-rNN` 兜底正则。

### Added
- **「选牌选择器按归属者分发」（改进-2 Phase 1，r113，2026-09-11）**：多瓦库托管时，选牌请求
  按「谁的选牌」路由到归属者自己的选择器，不再全靠"栈顶是谁"。新增纯逻辑
  `WakuuOwnerSelectorMap`（归属者登记表）+ `WakuuSelectorDispatch.Decide`（路由真值表，+21 单测）；
  运行层 `WakuuSelectorRegistry.Open(ownerId, selector)` 统一「压栈 + 登记」，6 处压栈点全部迁移
  （战斗出牌 / 遗物效果 / 事件选项 / 火堆 / 药水 / 卡牌奖励）；新增启动自检
  `SELECTOR_ROUTE`（反射枚举 `CardSelectCmd.From*` 全部入口并分类，未分类新入口即 WARN，
  兼作游戏更新哨兵）。行为零变化（三条老路逐条保留），为后续并发出牌铺路由底座。
- **瓦库事件选项接入「我们自己的」选择率 + 胜率（改进-3，r114/r115，2026-09-11）**：
  `WakuuEventSignal` 扩展 `ChosenRate` / `WinRateSkipped`（社区链无选择率保持 null），
  `WakuuSignalPicking.PickBestEventOptionIndex` 与卡牌同口径（选择率为主、增益加权、负面出局、
  同分保留更靠前）；r115 把统计链的全部早退路径留痕，让「没生效」与「没数据」在日志里可区分。
  前置：手动打开「个人统计决策辅助」（默认关）、样本门槛 ≥3、只计已打完的局。
- **多瓦库耗时埋点 + B1 修正版（r116，2026-09-11）**：回合开始 hook 只做「用药 / 视角 / 闪光 /
  登记出牌意图」，出牌唯一实现交由 tick 驱动的看门狗；顺带消除每回合每瓦库 5 条 INFO 与一条
  误报 WARN 噪声，并加耗时埋点（hookMs / 回合开始→出牌启动延迟 delayMs / 出牌耗时）——
  实证多瓦库是串行（第 2/3 个瓦库的 delay ≈ 前序瓦库出牌耗时之和）。
- **「瓦库出牌加速」开关（`fastWakuuPlay`，r117，默认开）**：给 `CardCmd.AutoPlay` 传
  `skipCardPileVisuals`（官方给自动出牌场景的参数，跳过两段固定等待与牌堆补间/烟雾 VFX），
  每张牌 1.0~1.4s → 0.2~0.58s（约 3 倍），不改数据语义。
- **【实验】瓦库出牌走动作队列（`wakuuPlayQueue`，方案 D 第一步，r121~r125，默认关）**：
  开启后瓦库出牌不再用 inline 的 `CardCmd.AutoPlay`，而是 `PlayCardAction` 入**该瓦库自己的**
  动作队列并逐张 `await`（能量读数准确、不堆注定被取消的动作）；目标按真人出牌口径归一
  （只有 `AnyEnemy` / `AnyAlly` 传大脑解析的目标，其余传 null）、删掉外层 `SpendResources()`
  防双重扣费。同轮顺手修：删牌去重键补 `act`（跨幕的两次合法同名删牌不再被合并）。
  已知取舍（写进设置页文案）：队列路径会盖过「瓦库出牌加速」。
- **【实验】瓦库并发出牌（`wakuuPlayOverlap`，方案 D 第二步，r126，默认关、需先开队列档）**：
  出牌循环不再抢全局 `SelectorScopeGate`、看门狗不再把 `LocalContext.NetId` 钉在瓦库身上
  （出牌交给游戏全局单泵，归属走 r113 的选择器注册表）⇒ 多个瓦库的出牌时间窗真正重叠
  （实测 5 瓦库的出牌启动延迟 32~83ms，串行时代是 0.5~9.7s）。顺带堵掉一个实测才能发现的真坑：
  原版 `StackedSelectorScope.Dispose` 只在"自己是栈顶"时弹栈 ⇒ 作用域交错时先释放的选择器
  会**永久残留**在全局栈里；新增 `WakuuSelectorStackSurgery.RemoveByReference`（按引用摘除，+7 单测）
  无条件挂在作用域释放处兜底。已知代价：瓦库出牌的前台视觉演出更少；「全程跟随」视角档会来回抢视角。
- **真人插队（r127/r128）**：并发档下真人**按牌**或**点结束回合**的那一刻，把瓦库"已入队、
  尚未开始执行"的动作撤掉，让真人的动作成为队列里 ID 最小的一个（原版多人执行本就是
  全局单泵、按全局 action ID 一次跑一个）。被撤的动作从未执行 ⇒ 不扣能量、不结算、牌仍在手牌，
  由看门狗下一轮重新决策；正在执行 / 正在等选择的动作一律不撤。
  补丁点：`CardModel.EnqueueManualPlay`（出牌）+ `NEndTurnButton.CallReleaseLogic`（结束回合）。

### Fixed
- **SL（读档）后个人记录的抉择未回滚 → 选择率被污染（BUG-9，r120，2026-09-12 实机确认）**：
  真人一点选项就立即落盘，而 `runKey` 跨存档稳定 ⇒ 被回滚的那段抉择累积成重复行、
  SL 前点过的选项残留 `chosen=true`。改为**「写时幂等」**：写入前先删掉同一抉择标识的旧行
  （事件页 `(runKey,eventId)`、卡牌批次 `(runKey,batchKey)`、商店 `(runKey,act,kind,item)`、
  删牌 `(runKey,card)`），SL 多少次统计都稳定；r118/r119 的「按存档点 mtime 回滚」方案
  已整体删除（实测判据恒失效：读档动作本身会重写存档文件）。
- **瓦库在「回合被强行结束」后仍继续出牌（BUG-10，r122/r123，2026-09-13 实机确认）**：
  打出虚空形态后被强行结束回合，瓦库仍继续出牌。按**结束来源归因**：被卡牌效果 / 原版强行结束
  （来源不明也一律停手）→ 停手；被模组自己收口且未全员 ready → 放行（保留"收口后又因别人的
  效果拿到牌继续打"）。判定收敛为纯函数 `WakuuTurnEndOrigin.ShouldStopAutoplay`（+9 单测）。
- **`NCardPlayQueue.OnActionEnqueued` 对「本地非前台玩家出牌」必然空引用（BUG-11，r124，
  2026-09-13 实机确认）**：本 mod 主动关闭了远端意图 UI ⇒ `PlayerIntentHandler` 为 null，
  原版取飞牌起点必然抛异常（方案 D 让瓦库出牌也走动作队列后被放大）。新增前缀守卫
  先判后跳（等价于"这张牌不进 UI 出牌队列"，但没有异常、没有 WARN）；原 Finalizer 保留兜底。
- **真人删牌统计从未记录（BUG-12；r129 定位 → r130 修正，2026-09-13）**：`个人记录-删牌` 一条都没有，
  落盘 `personal_stats.json` 的 `cardRemovals` **全历史 0 行**。实机证据（marker r128）：
  商店删牌走了 3 次、选牌链路完整结束（`Player …326 chose cards [WATCHER-DEFEND_WATCHER]`），
  同局卡牌奖励 / 事件点选记录都正常 ⇒ 不是记录器开关。
  **真正根因（r130 定位）= 守卫拿 AsyncLocal 字段当值比较**：原守卫写的是
  `… && LocalWakuuMerchantAuto.PurchaseOwnerId == null`，而 `PurchaseOwnerId` 当时是
  **`AsyncLocal<ulong?>` 字段本身**（恒非 null）⇒ 该条件恒 false ⇒ **整块守卫恒不成立、永远不记录**。
  处理：
  ① 该字段收私有、改经值属性 `PurchaseOwnerId` 暴露（与 `AutoClaimCardOwnerId` 同一套写法），
     调用方不可能再把字段当值比；
  ② 记录锚点从入口 `CardSelectCmd.FromDeckForRemoval` 移到 `CardSelectCmd.FromDeckGeneric` ——
     前者只有 4 行、是纯包装方法，**有被 JIT 内联的风险**（「启动审计显示补丁挂上了 `[P2Po1T0F0]`」
     只证明挂上了、不证明会被调用；后者是 async、函数体大，r129 实机已确认会触发）。
     代价：该入口同时被 DollysMirror（复制）/ WoodCarvings（变化）复用 ⇒ 新增纯逻辑
     `WakuuRecordScopePolicy.IsDeckRemovalPrompt` 按 prefs 提示键只认 `TO_REMOVE`
     （`TO_EXHAUST`/`TO_DISCARD` 是手牌语义，同样判否）；
  ③ 自动化作用域（事件自动选择 / 奖励自动领取 / 商店自动采购）**一律按归属者比较**
     （`WakuuRecordScopePolicy.IsAutoScopeOwnedBy`）—— 只看"作用域非空"会把真人自己的操作一起吞掉
     （r129 实机：那个**别的角色**的自动采购残留值，把真人连删的 2 张同名牌整条吞掉）；
  ④ 原实现是整块 `if (五个条件) { 记录 }`、任一不满足就**无声返回**（r118 老毛病）⇒ 改成先算
     `RemovalRecordBlockReason`，每次命中都打一条
     `个人记录-删牌钩子命中: owner=…, prompt=…, 选中=N, 瓦库形态=…, 记录｜跳过: <原因>`；
  ⑤ 同一类"只看作用域非空"的判据一并收紧：卡牌奖励快照 / 事件网格入卡组 / 商店购买
     （商店购买记录长期只有 2 行也是同一个原因）。
  **+8 单测**（`WakuuRecordScopePolicyTests`，含「别人的自动化作用域不算本人」回归用例与
  「锚点不许挪回 `FromDeckForRemoval`」反射哨兵）。marker **r129 → r130**。
  ✅ **2026-09-13 用户实机确认（marker r130，日志 + 落盘双向验证）**：连删 2 张同名防御 →
  两次都是 `删牌钩子命中 … 记录` + `个人记录-删牌 … 覆盖旧行=0` / `=1`（同幕同名只留最后一行）；
  商店购买首次正常入账（2 张卡，`覆盖旧行=0`）；查盘 `cardRemovals` 0 → 1 行、
  `shopPurchases` 2 → 4 行。
- **默认档「手动出牌上下文漂移」WARN 噪音（r129）**：未开「【实验】瓦库并发出牌」时瓦库是内联出牌，
  出牌循环会把 `LocalContext.NetId` 钉在瓦库身上；真人此时按牌 / 点结束回合，
  `AlignContextForActionOwner` 每局刷十几条 `[WARN] 检测到手动出牌上下文漂移，已强制校正`
  （2026-09-13 实机默认档一局 **12 条**，并发档会话 0 条）。行为本就正确（上下文该让给真人），
  只改日志判据：**上一任是瓦库形态角色时记 INFO**
  （`后台瓦库出牌钉住的上下文已让给真人（默认档预期路径）`），其余情况仍保留 WARN。

## [1.40.0] - 2026-09-08

> v1.40 = r56~r84 全量（2026-09-02 起），自 v1.39（r55）以来最大的一版：
> ① 上游合入（ghost hands 逐帧轮询+夹取 / min_game_version / opt-in 跨角色卡组）；
> ② **个人偏好记录器**（三级决策链第①级：真人决策记录 → personal_stats.json → 个人统计决策辅助 + 跨角色档位）；
> ③ **商店自动化 Phase 4 v1**（自动买卡 + 删牌统计 + 无数据开关 + 不买 Null）+ 火堆愈合优先级；
> ④ **自有统计角标全套**（overlay 角标/悬停面板、来源三档、位置四档、奖励/商店/事件多挂载点）；
> ⑤ 稳定性修复：后台手牌变换卡死、击杀结算非阻塞、合并奖励屏 backend ERROR、藏宝图读档兜底、
>    托管遗物防吞噬（可开关）等。单测 307 → 321。

### Fixed
- **瓦库托管遗物被第三方"吞噬遗物"效果移除导致瓦库彻底停摆（r83，2026-09-08 实机日志定位）**：
  实证：TouhouAncients 遗物【无底之胃】（`TOUHOUANCIENTS-BOTTOMLESS_STOMACH`，描述"拾起时，
  吞噬你初始遗物与先古遗物以外的全部遗物"）被瓦库自动选中后，2 号玩家遗物栏 **19 → 4**，
  【瓦库形态】被一并吃掉；而托管判据只看"是否持有形态遗物"，遗物没了 → 瓦库不再自动操作。
  修复两层：① 新增 `PlayerRemoveRelicInternalGuardPatch`（Wakuu 域）拦截
  `Player.RemoveRelicInternal`（原版 `RelicCmd.Remove` 也只是转调它，覆盖所有第三方移除链路），
  待移除的遗物是【瓦库形态】/【永久低语耳环】且该玩家仍在瓦库名单时跳过移除并打 WARN；
  玩家已取消瓦库勾选则照常移除。② 判据兜底：`IsVakuuFormMode` / `IsVakuuFormModeById` /
  `HasWakuuRelic` 在遗物缺失时改按瓦库名单继续托管（WARN 一次），并调度一次补发
  （复用 `LocalMultiControlRuntime.GrantWakuuRelicsAsync`，已持有则跳过）以恢复 +1 能量与遗物栏显示。
  **r84 起整项由 `keepWakuuFormRelic` 开关控制（默认开）**，见下方 Added。
- **自有统计角标位置/数据来源改不动（r84，2026-09-08）**：设置页点「自有统计角标位置」只会在
  界面上短暂变化、重进即回到默认的**左下**；「统计角标数据来源」同样无效。根因在
  `LocalWakuuAutopilotConfig.TrySetAndSaveString`：写回 json 时用的是
  `eventChoiceMode / cardPickMode / personalTier / else → wakuuBrain` 的 if/else 链，
  `statBadgeCorner` 与 `statBadgeSource` 都掉进最后的 `else`，被写进了 **`wakuuBrain`** 字段，
  角标位置本身从未落盘。改为按 key 的 switch 各写回各自字段（`LocalStatBadgeUi._Process`
  本来就每帧读 `StatBadgeCorner`，修好后切档即时生效）。
- **合并奖励屏 backend ERROR 残留修复（r63，2026-09-06 实机回归发现）**：
  r60 的 `BeginDisplaySet/CompleteDisplaySet` 只覆盖「离房时未领完」的路径；实机发现
  `All rewards have been taken...` 仍有 4/5 发生在**最后一张奖励按钮被领走那一刻**
  （RewardCollectedFrom → UpdateScreenState），因为领奖走 `SelectUnsynchronized` 不会像原版
  `SelectLocalReward` 那样在每次领取后 `CompleteRewardsSetIfNecessary`。修复：新增
  `CombatRewardMergeContext.OnRewardClaimed`（真人领完一个奖励后若所属展示集已全部领完即完成后端），
  并在 `NRewardButtonMergedRewardSelectPatch` 成功落点调用；另把「完成时同步器本地玩家与展示集归属
  不一致」（领奖期间切过角色）从"放弃兜底"改为**临时对齐本地玩家后跳过栈顶再恢复**，消除离房路径的残留报错。
- **Ghost hands 位置调整改为逐帧轮询 + 偏移屏幕夹取**（移植上游 e055e8f）：
  部分节点会先于 `NGame._Input` 消费方向键事件（Workshop 反馈 Ctrl+Right 永远到不了），
  现改为 overlay 每帧轮询原始按键状态（`LocalGhostHandsOverlay.PollMoveKeys`，Shift+方向键为微调）；
  偏移在拖动与配置加载时夹取到可见区（`LocalGhostHandsRuntime.ClampOffsetsToScreen`），
  跑出屏幕的历史配置下次启动自动回正。`GhostHandsHotkeysPatch` 仅保留 F8 开关。
- **个人记录器三处修复（r58，2026-09-03 观者实机回归发现）**：
  ① 整局胜负**重入护栏**：原版 `WinRun` 在 `OnEnded(true)` 后 `GuaranteeKillAllPlayers()` 会触发
    `CreatureCmd.Kill → OnEnded(false)` 第二次调用（原版靠 `_runHistoryWasUploaded` 早退），我们的前缀
    在护栏前 → 一局被记成 win+loss 两条。改为前缀读取同一私有字段，重入即跳过。
  ② 卡牌奖励点选检测改**牌组对比**：原「快照候选 − 剩余候选」差分依赖 `CardReward.OnSelect` 把点中的
    卡从 `_cards` 移除，但移除的是 `CardPileCmd.Add` 返回的实例（实测不一定是同实例）→ 真人点的卡牌
    奖励一条都没记（观者局日志 8 次 `obtained ... from card reward`，记录器 0 条）。改为前缀快照牌组构成、
    后缀对比出「新增进主牌组的卡 = 点中的卡」，ground truth 可靠。
  ③ 事件页过滤**单选项/对话推进页**：`THE_ARCHITECT` 结局对话、探索者等待/前进等页只有一个"继续"按钮，
    无偏好信号却污染事件统计；现只记「过滤锁定/继续后可选 ≥2」的真实取舍页。
- **修复后台角色手牌变换导致战斗卡死（r59，2026-09-04 多人实测）**：
  `CardCmd.Transform` 的视觉分支会用 `NCard.FindOnTable(original, Hand)` 在**前台手牌**找原卡节点，
  找不到就抛 `InvalidOperationException`。本 mod 的 `CardTransformNetIdPinPatch` 把 `LocalContext.NetId`
  钉到变换牌主人后，会把**后台角色**（瓦库托管中/其他本地角色）的手牌变换也误判成"我的牌"而去前台找节点 →
  异常抛穿异步链（含 RitsuLib 桥）杀死回合循环：第一回合起无法出牌/切人/结束回合
  （实测：GensokyoSpire「不明妖怪之力」回合开始变换 LexNinja2「等离子之手」）。
  修复：仅当变换牌主人 == 当前前台角色（`SessionState.CurrentControlledPlayerId`）时才钉 NetId；
  后台手牌变换本就无需前台动画，让 vanilla 按 `IsMine=false` 跳过视觉即可（数据层照常生效）。

### Added
- **「防止瓦库形态丢失」开关（r84，2026-09-08，默认开）**：r83 的托管遗物防移除由
  `keepWakuuFormRelic` 控制，设置页位于「压制原版低语耳环」下方。开启 = 拦下对【瓦库形态】/
  【永久低语耳环】的移除 + 遗物缺失时按瓦库名单兜底并补发；**关闭 = 完全回到 r82 行为**
  （遗物可被第三方/引擎正常移除，移除后瓦库停止自动操作）。同时门禁补丁
  `PlayerRemoveRelicInternalGuardPatch` 与判据兜底 `IsTakeoverPlayerFallback`。
- **设置页新增「其它设置」分区（r84，2026-09-08）**：与瓦库托管无强关联的开关统一挪到页面末尾，
  用两条分隔线 + 小标题隔开并附说明「即使不开托管也按各自开关生效」。本次移入：
  **跨角色卡组（战后奖励）**、**自有统计角标**、**统计角标数据来源**、**自有统计角标位置**。
  瓦库区只保留「只有瓦库托管才会用到」的开关。
- **声明 `min_game_version: 0.111.0`**（移植上游 17feed7）：`DualRoleAdventure.json` /
  `mod_manifest.json` / `workshop/content/DualRoleAdventure.json` 三处补齐——
  游戏版本不符时由加载器清晰拒绝加载，而非 `ReflectionTypeLoadException`。
- **opt-in 跨角色卡组**（移植上游 69c7d99，配置键 `extraCrossCharacterCardReward`，默认关）：
  每个角色战后奖励追加一组从「其他角色」卡池抽取的 3 选 1 卡牌奖励——原作者未完成的 v1.30 设计。
  开关已接入瓦库托管设置页（`LocalWakuuConfigSubmenu` 新增「跨角色卡组（战后奖励）」勾选行，
  即时写回 `vakuu_autopilot.json`），无需手工编辑文件；
  `CombatRoomOfferRoomEndRewardsPatch` 在各角色奖励生成完毕后、`BeforeCombatRewardOffered`
  结算前追加；ghost hands 保存配置改为**合并写入**，不冲掉设置文件里其他功能写入的键。
- **个人偏好记录器 Phase 1**（可行性分析 §8.4.1 三级决策链第①级，分支 `feat/personal-recorder`）：
  - **记录（只记真人决策）**：真人领奖（`RewardsSetSynchronizer.SelectLocalReward`）与真人选事件
    （`NEventRoom.OptionButtonClicked`）两个入口都是瓦库自动路径不经过的——自动领奖走
    `SelectUnsynchronized`、自动选事件直调 `Chosen()`，因此瓦库数据天然排除，无需额外标记。
    记录字段：模式（单/多）/ 幕数 / 角色 / 首次-重复（牌组已持有同 id）/ 整局结果。
  - **存储**：`%APPDATA%\SlayTheSpire2\personal_stats.json`；runKey = 种子+玩家数，
    记录逐条落盘、整局结束补写结果，跨存档会话也能归因；只统计打完的局（win/loss），
    abandon 局丢弃、进行中局不计、stale 超 60 天清理。
  - **决策参考（personalAssist，默认关）**：瓦库选卡牌奖励/事件选项先查个人统计
    （多人局优先多人切片，按 ①模式+角色→②模式→③角色→④全量 放宽），样本达到
    `DefaultMinPersonalCount=3` 且非负面才采用，否则回退社区统计（skadaAssist）→ 最左/最上。
    纯逻辑 `PureLogic/WakuuPersonalData.cs` 全部可单测（新增 10 用例）。
  - 设置页新增「个人偏好记录」（默认开）与「个人统计决策辅助」（默认关）两行。

### 记录覆盖补全（Phase 1.5，r61，2026-09-06）
- **事件网格选 N 张入卡组**（`EventModel.SelectCardsToAddToDeckFromGrid` 钩子）：
  脑蛭「分享知识」（5 选 1）、满屋芝士（选 2）等不可跳过的 `FromSimpleGridForRewards` 路径
  此前不记——系统性漏掉"入卡组"决策，且"展示了但没选"的候选是高质量负信号。
  现与卡牌奖励同构记入 `cardOffers`（共用牌组差分 ground truth，`PersonalCardBatchTracker` 抽取共享）；
  瓦库事件自动选择（`InEventAutoChoiceScope`）与"无真实取舍（自动全选）"分支排除。
- **商店购买记录**（`MerchantEntry.OnTryPurchaseWrapper` 钩子，用户口径：只记「买了」）：
  卡/遗物/药水三个购买落点记入新增的 `shopPurchases` 表
  （runKey/模式/角色/幕/kind/item/实付金币），删卡服务（花钱删牌）不记；
  前缀在扣款清栏前快照商品（购买成功后条目会被 Clear/Restock），购买成功才入账。
- `PersonalStore` 增 `shopPurchases` 列表并接入过期清理；单测 +2（JSON 往返含商店记录、随局清理）。
  瓦库商店自动化（Phase 4）落地后需在此处补瓦库自动购买的作用域排除。

### 商店自动化 v1 与删牌统计（Phase 4 起步，r64，2026-09-06）
- **删牌统计记录**（个人记录器）：真人删牌（事件删牌 / 营地删牌 / 商店删牌服务，都走
  `CardSelectCmd.FromDeckForRemoval`）记入新 `cardRemovals` 表（runKey/模式/角色/幕/被删卡）；
  `WakuuPersonalQuery.CountCardRemovals / CountAllCardRemovals` 纯函数（只计已结束局），
  「被删概率」口径 = 某卡被删次数 ÷ 同类切片删牌总数（删牌偏好占比），接入过期清理。
- **商店自动买卡（shopAssist，默认关，Phase 4 v1）**：瓦库角色的商店视图打开
  （`NMerchantInventory.Initialize`）且库存绑定后触发 `LocalWakuuMerchantAuto`——
  对角色卡+无色卡按 `WakuuMerchantPicking.SelectCardBuys` 决策（社区统计胜率 ≥ 20% 且
  支付后保留 ≥ 50 金保底），经 entry 公共购买链路直接成交；同一房间×角色只自动采购一次。
  **r65（2026-09-06）**：买卡胜率门槛从 50% 下调到 **20%**——用户实测反馈绝大多数牌社区胜率
  集中在 20%~30%，原 50% 门槛导致几乎无牌可买。
  **r66（2026-09-06）**：新增「无统计数据也买」补充开关 `shopAssistBuyNoData`（默认关）——
  实测发现商店多为 mod 卡（SkadaHelper 无数据），降门槛后仍买不到；开启后无数据的卡也按
  「付后保留 ≥ 50 金」金币保底买入（`WakuuMerchantPicking.SelectCardBuys` 增 `buyNoData` 参数）。
- **作用域**：自动采购全程置位 `LocalWakuuMerchantAuto.PurchaseOwnerId`（AsyncLocal），
  个人记录器的商店购买 / 删牌两个钩子据此不把瓦库自动数据当真人记录。
- 设置页新增「商店自动买卡」开关与「商店买卡·无统计数据也买」开关。
  遗物/药水与删牌服务自动化留待后续增量（§9.3）。
- 单测：`WakuuMerchantPicking` 决策 7 例（+2 无数据购买语义、+1 Null 占位卡）+ 删牌统计 1 例；**315 用例全绿**。
- **r67（2026-09-07 实测修复）**：自动采购买过游戏内置的 Null 占位卡
  （`MegaCrit.Sts2.Core.Models.Cards.Null`，Id.Entry="NULL"，社区统计里有数据所以过了 0.2 门槛），
  白花金币。修复：运行侧按类型跳过该卡，纯函数侧加 `IsPlaceholderCardId` 兜底（空 id / "NULL" 一律不买）。

### 火堆选项优先级：全员血量 ≥50% 时愈合排最后（r68，2026-09-07）
- 用户拍板：全员（含瓦库自己与队友）当前血量占上限都 ≥50% 时，给队友「愈合」（MEND）意义不大，
  优先级压到睡觉（HEAL）之后——先按原顺序锻造/睡觉，都没得做了才愈合。
- 有人血量 <50% 时维持原行为：愈合仍优先于睡觉（回血比无意义的睡觉更有价值）。
- 判定抽为纯函数 `WakuuRestPicking.IsAllAboveHpRatio`（默认 50%，可自定义比例），运行时侧
  `LocalWakuuRestAutoChoice.IsEveryoneAboveHpRatio` 组装存活玩家血量后调用；单测 +4。
- 火堆选择日志增加 `全员≥50%=` 字段，便于核对优先级走向。
- 商店自动买卡「不买」日志补充 `候选数/金币/最便宜价`——此前无法区分"没候选"与"买完跌破 50 金保底"。

### 自有统计角标与悬停详情（statBadge，r69，2026-09-07）
- 需求（用户拍板）：把「我们的个人数据统计显示」与皮皮军师/SkadaHelper 的社区统计显示**分开、可并存**——
  只显示个人记录器算出的数据，社区数据不进本 UI。
- **角标**：奖励选牌卡 / 商店卡 / 事件选项按钮右下角常驻一个百分比——卡牌 = 总抓取率、事件 = 总选择率。
  挂载点：`NCardRewardSelectionScreen.RefreshOptions`、`NMerchantInventory.Initialize`（含伪商店子类）、
  `NEventRoom.RefreshEventState` 三个 postfix（re-roll / 换卡 / 事件状态切换都会重建并同步）。
- **悬停弹窗**：postfix 游戏 hover 统一入口 `NHoverTipSet.CreateAndShow`，向 hover tip 文本容器追加
  自绘统计块（随 hover 关闭整树销毁）——卡牌 = 1/2/3 幕「首抓/重复」抓取率 + 拿了/没拿的整体胜率；
  事件 = 分幕选择率 + 选了/没选胜率。
- 数据层纯函数 `WakuuStatBadgeQuery.BuildCard/BuildEvent` + 格式化 `WakuuStatBadgeFormat`
  （只读 `LocalPersonalRecorder.Snapshot`，口径与决策链一致：只计已结束且非 abandon 的局；
  isMulti = 当前是否本地双控 run，角色全部合并）。
- 视觉用 Godot 基础控件 + SystemFont（微软雅黑等兜底）自绘，不碰 MegaLabel/场景主题、不挡点击
  （MouseFilter=Ignore），不读任何社区数据；所有绘制异常 try/catch 降级 WARN 不影响游戏。
- 默认关，设置页新增「自有统计角标」开关；单测 +3（纯逻辑聚合/文本 3 例，config 断言 +2）→ **318 用例全绿**。
  **r70（2026-09-07 实机修正）**：涅奥/事件选项的角标跑到了选项**左侧**——事件按钮在
  `NEventRoom.RefreshEventState` 时刚被加入容器、`Size` 尚未由容器排布（=0），按当时几何锚定
  bottom-right 全落到按钮原点。改为角标「布局后自动重贴」：订阅 `area.Resized`（同一 area 只订一次）
  且初始零尺寸时 `CallDeferred` 延迟一帧重贴。
  **r71（同日复测仍错位后重构）**：放弃「子节点锚点跟随父布局」路线，改为**常驻全屏 overlay
  `StatBadgeOverlay` 每帧摆位**——读取目标节点实际 `GetGlobalRect()`，把角标钉到其右下角；
  不再依赖父节点何时排布尺寸/锚点是否重算，事件入场动画、晚排布、换页重建天然免疫。
  **r72（同日用户确认呈现后定稿）**：悬停详情不再追加进游戏 hover tip（那会跟随原生 hover 出现在
  选项左侧/卡片右侧），改为 overlay **自绘正下方面板**——`_Process` 用视口鼠标位置判定当前悬停目标，
  在其正下方（底部放不下时自动翻到上方）显示统计详情；右下角角标机制不变。移除原
  `StatBadgeHoverPatch`（`NHoverTipSet.CreateAndShow` 追加注入）及其域登记。
  **r73（2026-09-07 实机反馈：商店看不到角标）**：商店挂载点补充 `NMerchantCard.FillSlot`
  postfix（单卡填充完立即刷新，原只有 `NMerchantInventory.Initialize`）；商品条目
  （`CreationResult`）晚于填充生成时自动排一次 0.35s 延迟重试；`Initialize` 增加一条统计日志
  「卡槽数/已挂角标/未挂」，用于区分「没挂上（取不到卡或条目未生成）」与「该卡无个人记录」。
  **r74（2026-09-07 实机反馈：战斗后卡牌奖励无角标）**：奖励选牌原来依赖节点路径 `UI/CardRow`
  取卡容器，路径不匹配时会静默不出角标。改为**遍历整个选牌屏子树**找 `NCardHolder`
  （`FindChildren("*", recursive, owned:false)`），不再依赖路径；同样加一条统计日志
  「卡牌 holder 数/已挂角标/无个人数据」，完全没找到 holder 时额外 WARN 提示 UI 结构变化。
  **r75（2026-09-07 用户拍板：无数据显示 0%）**：日志实证 `卡牌holder=3，已挂角标=0，无个人数据=3`
  ——功能正常，是**个人样本太稀疏**（存档 6 局 / 240 种卡，多数卡仅 1~5 次 offer，很多卡历史上一张没见过）。
  按用户要求：**无个人记录的卡角标也显示 `0%`**（角标常显），悬停时给出「暂无该卡的个人记录（样本 0）」
  而不是什么都不弹。决策链的严格口径（只计已结束且非 abandon 局）不变，仅展示层改为常显。
  **r76（2026-09-07 用户反馈「我们的 mod 压制了皮皮军师自己的社区统计显示」）**：查证
  `SkadaHelper.dll`（workshop 3763804482，即皮皮军师）**自己会把社区统计标签画在卡上**
  ——它 patch 了 `NCardRewardSelectionScreen` / `NGridCardHolder`，dll 内有
  `StatsLabelWidth` / `GetStatsLabelX` / **`StatsRightInset`**（标签从卡的右侧内缩）。
  而我们的角标是挂在树根的顶层 overlay、又固定钉在卡右下角 → 正好把它盖住。
  修复：角标位置改为**可配置四档**（左下/右下/右上/左上，循环按钮在设置页「自有统计角标位置」），
  **默认改为左下**以避开皮皮军师的右侧标签；摆位抽为纯函数 `WakuuStatBadgeLayout.Resolve`
  （可单测，+1 例）。两者现在可以同时看到、互不遮挡。
- **统计角标可接入皮皮军师社区数据（r78，2026-09-07 用户拍板改需求）**：
  查证用户装的 `SkadaHelper 0.8.7「轻量版」` manifest 自述
  「**仅保留本地路线参考，不再提供选牌、商店等 AI 决策建议**」——它加载正常、数据包可用
  （996677 runs），但**卡牌/商店的社区统计它自己已经不画了**（所以不是被我们压制）。
  因此不再追求"同时显示两套数字"，改为**单一数字 + 可选社区兜底**：
  新增 `statBadgeCommunityFallback`（**默认关**），开启后**仅当某张卡没有个人记录时**，
  用皮皮军师的社区抓取率/胜率补足角标与悬停面板，面板标注「来源：社区·皮皮军师」；
  有个人记录时仍只显示个人统计。取数走既有 `WakuuSkadaAdapter.TryGetCardSignal`（按卡所属角色查表），
  未装/查无数据则退回 0%。单测 +1（社区兜底正文格式化）→ **320 用例全绿**。
- **统计角标数据来源改三档（r79，2026-09-07 用户拍板）**：把 r78 的布尔兜底开关升级为
  `statBadgeSource` 三档（设置页循环按钮，**默认 `personalOnly` 仅个人**），始终只显示一个百分比：
  ① **仅个人**：只用自己打出的统计（无记录显示 0%）；
  ② **个人+社区兜底**：该卡没有个人记录时才用社区抓取率补足；
  ③ **融合**：个人与社区按**伪计数加权**合成一个抓取率——
  `BlendPickRate = (picked + K·社区抓取率) / (offered + K)`，默认 `K=5`
  （个人样本越多越主导，个人 5 次以上即与社区平手以上；社区无数据退化为纯个人）。
  融合档悬停面板同时给出「融合抓取率 / 个人 / 社区（含样本）」三行 + 分幕与胜率明细。
  单测 +2（档位归一化 + 融合算式的三种退化/加权情形）→ **321 用例全绿**。

### 跨角色偏好档位（Phase 1.5，r62，2026-09-06）
- 配置键 **`personalTier`**（设置页三档按钮：「角色优先 → 总量优先 → 只看角色」循环，即时写回 json）：
  - `characterFirst` **角色优先（默认 = v1 现状，行为零变化）**：①模式+角色 → ②模式 → ③角色 → ④全量；
  - `volumeFirst` **总量优先**：跳过「跨模式单角色」档（该档样本往往最稀疏），样本集中在模式内与全量；
  - `characterOnly` **只看角色**：只信本角色数据（模式×角色 → 跨模式×角色），不足即回退社区统计，
    绝不用其他角色的数据兜底（适合角色专属牌）。
- `WakuuPersonalQuery.TryGetCardDecisionSignal / TryGetEventDecisionSignal` 加 `tierPreference` 参数
  （默认 characterFirst）；**瓦库卡牌奖励与事件选项两条决策链**均接入
  （`LocalWakuuStrategySelector` / `LocalWakuuEventAutoChoice`）。
- 纯逻辑档位用例 +3；**303 用例全绿**。

## [v1.39] - 2026-09-01

> 维护性改进 **Phase 1（1.1~1.5）+ Phase 2（2.1~2.4）+ Phase 3（r47~r54，瓦库智能选择）已全部合并到 `master`**
> （2026-09-01，部署位 marker r55）。v1.39 是攒批后的首个发版：Phase 1/2 为**工具化 / 自检 / 文档 / 结构性改进**
> （不改变既有运行时行为默认值），外加实证发现的存量修复与瓦库自动化功能（r28 / r41~r54）。

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
- **优先丢弃奇巧牌（弃牌场景智能选牌，marker r51）**：
  - 奇巧机制确认：`CardKeyword.Sly`（中文"奇巧"），`CardModel.IsSlyThisTurn` 的牌在
    `CardCmd.DiscardAndDraw` 中会被收集并以 `AutoPlayType.SlyDiscard` 自动打出——弃掉奇巧牌
    = 白嫖一次出牌 + 腾手牌；
  - 新增 `Discard` 弃牌场景优先级表：Sly(奇巧) > 诅咒 > 状态 > 任务 > 打击 > 基础防御 > 其余；
    `WakuuCardKind` 新增 `Sly` 类别（`ClassifyCard` 增加 `isSly` 参数，取 `IsSlyThisTurn`）；
  - 跨场景默认语义：Remove 视同 Other（不优先消耗正面牌）、Copy 视同 Other（可优先复制）、
    Transform 硬排除（变掉会失去奇巧白嫖机制）；
  - 场景识别：`ClassifyHandScenario` 增加 prefs 预设 `TO_DISCARD` → Discard（杂技/预谋/
    赌徒芯片/行商之手等全部走 `FromHandForDiscard` 内部即 FromHand）；战斗内补丁与事件内
    补丁自动生效，无需新拦截点；
  - 测试：`PriorityPickingTests` 新增 10 例（合计 251 例全绿）；主项目 Release 0 警告 0 错误。
- **附魔智能选牌（原版附魔一览表规则表，marker r52）**：
  - 用户按「原版附魔一览表.md」逐附魔填写了选牌规则（20 种附魔 + 克隆两套分支），本版本按表实现；
  - 新增纯逻辑 `Scripts/Runtime/PureLogic/WakuuEnchantPicking.cs`：附魔选牌规则引擎——
    阶段式（按顺序取第一个能筛出候选的条目）、`WakuuEnchantCardInfo` 卡牌特征快照、
    `WakuuEnchantPredicate` 谓词（类型位掩码/消耗/多段次数/抽牌数/格挡/费用/已升级）、
    `WakuuEnchantRuleEntry`（精确牌名 + 遗物持有/牌组持有条件；"没有 XX"按用户口径实现为
    无条件**降级位置**而非禁止持有——靠"第一个非空条目即返回"自然形成"有 XX 在前、没有时在后"）、
    排序键（费用/伤害/格挡/稀有度，X 费按费用=3 折算）；
  - 新增 `Scripts/Runtime/PureLogic/WakuuEnchantRules.cs`：规则数据表——每附魔一条有序规则，
    Clone 按是否持有不休陀螺分两套（有陀螺分支末尾回退无陀螺分支）；
    牌名/遗物名由中文本地化（zhs）反查得到游戏内部 id（如 吹哨→WHISTLE、烫嘴可可→VERY_HOT_COCOA）；
  - 新增 `Scripts/Runtime/LocalWakuuEnchantPicker.cs`：运行时把 CardModel 抽成纯数据 + 查规则 +
    取前 N 张；任一步异常/该附魔填"维持现状"（Goopy/Spiral/TezcatarasEmber）回退既有策略；
  - `WakuuEventEnchantAutoAnswerPatch.FromDeckForEnchantment` 接入 smartEnchant 路径；
    新增 `smartEnchant` 开关（**默认开**，设置页新增勾选行）；
  - 测试：新增 `WakuuEnchantPickingTests` 33 例（合计 284 例全绿）；Release 0 警告 0 错误。
- **附魔选牌修正（marker r53）**：
  - **Clone 不休陀螺分支停用**（用户要求）：沙漏 BOSS 下不休陀螺相关牌是死路，克隆恒走
    无陀螺分支；不休陀螺分支数据注释保留（含其专用条件组 卡戎之灰/抱抱先生/水银沙漏 移除定义，
    恢复时按注释补回）；
  - **华彩 Glam 优先级加 X 费**：能力牌 &gt; X 费牌（新增 `RequireCostsX` 谓词）&gt;
    费用最高（≥2）&gt; 愤怒 &gt; 不死 &gt; 适应打击 &gt; 其它；
  - 测试：Clone 分支语义用例更新 + Glam X 费 4 例（合计 289 例全绿）；Release 0 警告 0 错误。
- **事件内「选牌 + 领奖励」两处卡死修复（marker r54）**：
  - **事件内选牌卡死**：脑蛭「分享知识」走 `CardSelectCmd.FromSimpleGridForRewards`（从 5 张网格挑 1 张
    加入牌组），不在 `WakuuEventEnchantAutoAnswerPatch` 逐个拦截的 From* 清单里 → 弹 `NSimpleCardSelectScreen`
    → 自动选择判定"出现弹层"停住等真人。改为：事件选项执行期间 `CardSelectCmd.PushSelector`（与火堆
    `LocalWakuuRestAutoChoice` 同一套已验证做法）压入策略选择器，兜住**所有**未逐个拦截的选牌入口；
    逐入口的拦截保留，智能选牌照旧生效。同时把选牌归属者写入
    `CardSelectForegroundSwitchPatch.CurrentChoicePlayerId`，让 `CardSelectCmdSelectorGuardPatch` 认出
    "是瓦库在选"（真人自己的选牌仍走正常 UI，不被抢答）；
  - **事件奖励卡死（比停住更糟，是挂起）**：`RewardsCmdOfferCustomPatch` 原在 `LocalContext.IsMe(player)`
    时放手（假设真人会点），但「瓦库事件自动选择」推进事件时控制权常被切到瓦库身上（日志实证
    `控制上下文已更新: ... -> 瓦库, source=player-state-button`），于是原版弹奖励屏、无人点，
    `await RewardsCmd.OfferCustom(...)` 永久挂起、`SetEventFinished` 排在其后 → 事件完不成、Proceed 被拦截。
    新增 `LocalWakuuEventAutoChoice.AutoChoiceOwnerId`（AsyncLocal）+ `IsAutoChoosingFor(player)`，
    仅在该作用域内接管自动结算；真人自己玩的事件行为完全不变。
    已核对原版 13/13 个给奖励的事件全部走 `RewardsCmd.OfferCustom`，覆盖完整；
  - **开关门禁（不静默跳过）**：整批奖励里只要有一项不满足自动领取条件（开关关闭/未知类型），
    能交真人的就交真人（`IsMe=true` 时 `return true` 走原版弹屏由真人点）。静默跳过会让"开关关了"
    和"瓦库漏领/结算失败"在体感上一样，事后分不清是配置还是 bug。
    后台瓦库（原版本就不弹屏，交回会挂起）仍跳过，但升级为 WARN；结算期失败（非开关关闭）统一打 WARN 留痕；
  - `LocalWakuuStrategySelector` 新增可选 `LogLabel`，作答时打 `瓦库自动选牌作答` 日志（默认不打，
    避免战斗出牌刷屏）；"出现弹层"分支补 `screenCount`/`top` 屏幕类型诊断；事件已 `IsFinished` 时
    不再误报"出现弹层停住等真人"，走正常收尾；
  - 测试：289 例全绿（本次为接线改动，无新增纯逻辑用例）；Release 0 警告 0 错误。
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
> the previous maintainer (GuyGinat). The authorization it describes was indeed granted to
> GuyGinat: the original author (liwenhao0427) has confirmed that the project welcomes any
> developer to continue maintaining and improving the source, and that community developer
> GuyGinat has officially taken over maintenance and released subsequent versions. The
> current maintainer of this repository is NOT that officially authorized maintainer — this
> fork is an independent personal continuation and makes no claim of official authorization
> from the original author.

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
