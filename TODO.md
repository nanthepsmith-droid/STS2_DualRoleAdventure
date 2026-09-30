# 已处理归档：原作者遗留的 4 项 Known Issues（**勿重开**）

> 2026-09-15 逐条核对：本节原本是原作者留下的 4 项调查中问题（英文原文，描述的是**当时的实现**）。
> 比对当前代码与历史记录后确认**四项均已解决或已不再适用** —— 与现状不符的描述已删除，
> 只保留「怎么处理的 + 现在去哪看」，避免后续误判成"还没修"。
>
> | # | 原问题 | 现状 | 现在去哪看 |
> |---|---|---|---|
> | 1 | 战后卡牌奖励的「额外卡组」不显示 | **已实现**（开关 `extraCrossCharacterCardReward`，**默认关**）。旧写法 `combatRoom.AddExtraReward(otherPlayer, …)` 已删除 —— 它按玩家键取额外奖励，必然取不到挂到别人名下的那一组；现改为**以接收者身份**创建奖励组 | `Scripts/Patch/CombatRoomOfferRoomEndRewardsPatch.cs` 的 `AddExtraCrossCharacterCardRewards`（注释里明确记了旧写法的坑）；配置字段 `WakuuConfigData.extraCrossCharacterCardReward` |
> | 2 | 宝箱房 `Attempted to pick relic while relic picking is not active!` 卡死 | **已修**（流程重写）：`OnPicked` 的拦截**只在 5 人以上**（`OverflowCopyPlan`）生效，一次结算完直接结束房间；4 人及以下**完全不干预**原版投票流程。旧的「自动补齐宝箱投票 + 立即 `AwardRelics`/`EndRelicVoting`」那条日志字符串在代码里已不存在 | `Scripts/Patch/TreasureRoomRelicSynchronizerPatch.cs`（`Prefix` / `TryGetOverflowPlan`）；历史见 `docs/archive/player-update-history.zh.md`「宝箱遗物复制去重」「4遗物上限防卡死」「5人以上宝箱快速结算」 |
> | 3 | 药水栏归属/显示错乱（原注：`NPotionContainerPatch` 被禁用） | **已修**：`NPotionContainerPatch` 已启用、已登记到 `Ui` 补丁域，把药水栏绑定到**当前受控玩家** | `Scripts/Patch/NPotionContainerPatch.cs`；历史见「药水栏改为按角色独立」（v1.05） |
> | 4 | 战斗内用药不能选目标 | **已修**：`PotionManualUseTargetPatch` 前缀改写 `PotionModel.EnqueueManualUse` 的 `target`（仅 `TargetType.Self` 且目标为自己时生效，落到当前受控角色） | `Scripts/Patch/PlayerPotionMirrorPatch.cs` |
>
> ⚠ 若日后这些功能又出问题，**当新 bug 查（附日志）**，不要照原作者的旧描述定位 ——
> 那些描述引用的代码路径（`AddExtraReward` 按玩家键取奖励 / 立即 `AwardRelics` / `NPotionContainerPatch` 禁用）
> **在当前代码里都已经不存在**。游戏版本：v0.111.0。

---

## 决策记录跟踪表（提案生命周期 · 唯一事实源）

> 2026-09-25 新增。此前 `maintenance-docs/decision-records/` 的提案**没有单一状态事实源** ——
> 状态散落在各提案正文里，`维护现状分析.md §4.6` 的清单又会随快照过期，两边都可能与实际脱节；
> 结果是「某提案到底拍板了没、做到哪」只能靠翻正文猜。
>
> **口径**：
> - **本表 = 工作项状态**（谁做、做到哪、下一步是什么）；提案的**清单与定位**在
>   `maintenance-docs/decision-records/README.md`（冲突时以那份索引为准）。
> - 提案正文里旧的「待拍板 / 未动工」字样是**历史记录，不再作为判据**，也不回填改写。
> - **登记 ≠ 排期、≠ 承诺动工**；下面的提案都不占发布排期。
>
> **登记纪律**（写进 `AGENTS.md` §8）：新 proposal 落进 `decision-records/` **当天** →
> ① 在 `decision-records/README.md` 加一行 → ② 在这张表挂条目（或明确标「仅调研，不排期」）。
> 状态变化当天改这一行，不再往提案正文里追加状态。

| 提案 | 现状（2026-09-25） | 下一步 | 落在哪 |
|---|---|---|---|
| `瓦库托管最终阶段-实施草案.md`（M1~M4） | **动工中（M1 已落地）**：Q1~Q5 已拍板（新开关默认关 / Skip 跟随 `deckAwareDraft` / 静态效果表 ≤50 条 / 日志 `[瓦库评价]` / `AnyAlly` 自用优先）；**M1 知识层已于 2026-09-26 实现（r153）** —— 纯逻辑知识层（效果事实 / 静态语义表 31 条 / 端口与基线 / 置信度 / 牌组评估 / 机会成本 / 协同估计）+ 只读抽取器 `WakuuEffectExtractor` + `[瓦库评价]` 只读抽样日志；**不做决策接入、默认（启发式）档零变化**，+43 单测（653 全绿）；分支早已合回 master，随 **v1.43.0** 发布（2026-09-27） | 上一局实机顺带看 `[瓦库评价]` 锚点（读不懂的卡按 id 去重上报）→ 再开 **M2 智能抓牌**（`deckAwareDraft` 默认关：抓牌评分 + Skip，复用本层 `WakuuDeckAssessment`/`WakuuOpportunityCost`） | 本表 + `STS2…AI总规范_Final_v2.md`（总约束） |
| `STS2…瓦库托管最终阶段_AI总规范_Final_v2.md` | **规范稿**（无落地） | 作为 M1~M4 的总设计约束，不单独动工 | 本表 |
| `瓦库托管基础收口草案-最终阶段前置G0.md` | **部分落地**：**G0.1**（B1 买遗物 / B2 买药水 / B3 删牌服务）**已被 §改进-5 吃掉**（r137/r139，已实机确认）；**G0.2**（B4 恋降级逃生门 / B5 回归清单固化）**未做** | 只做 G0.2 两个子项（范围小，差两项） | **§改进-5** |
| `多瓦库并行托管可行性与方案.md` | **部分落地**：Phase 0（r107~r110）/ Phase 1（r113）/ 方案 D（r121~r134）均已实机确认；三层档位：队列 / 并发**已于 2026-09-26 转正为默认开**，加速档默认关（用户自选） | **转正判定已完成（2026-09-26，方案 §12.15，14 会话含 1 真人+3 瓦库对照局）**：① **并发档收益已证实** —— 同回合三瓦库 `delayMs` 增量中位 **14ms**（inline 基线是秒级 494→6297→9724），第 3 位瓦库启动 **9.7s → 95ms**；② 出牌总时延 —— 队列单张中位 1143ms（2 席）/ 1674ms（3 瓦库，排队所致），但 pass 相当 ⇒ **不提速也不劣化**；③ 回归 —— `收回滞留节点` / `幽灵弹层` / `add_child failed` **14 会话全 0**。⇒ ✅ **已于 2026-09-26 转正落地（r152）**：两档默认值改 `true`，设置页/玩家指南/单测/CHANGELOG 同步；`fastVakuuPlay` 仍为独立项（用户当前关着） | **§改进-2** |
| 改进-2 出牌加速二期（`CardModel.OnPlayWrapper` 前缀省两段固定等待） | **待拍板**：r128 已给估值（~0.35~0.5s/张），用户未拍 | 拍板后作为独立的出牌路径优化做 | **§改进-2 ⑱** |
| `runtime架构分层重构评估.md`（#18） | **动工中：R0 / R1 / R2 第一批已落地（2026-09-27 ~ 09-28）** —— R0（ADR + `AGENTS.md §1` 四条架构边界硬规则）`0a740c0`；R1a 布局数学（`LobbyCardLayoutPolicy`，marker r164，实机确认选人界面一致）`72dcaf1` / R1b 奖励判定（`CombatRewardMergePolicy`，marker r165）`7f85d09`；**R2 第一批（2026-09-28）** = ① `EnumerateDescendants` 四份私有副本 → `LocalNodeTree` 单点化 ② 三份人数面板（标准/每日/自定义）→ `LocalPlayerCountPanel` 共用组件。**⚠ 实机回归与收口（r169 → r171）**：一度抽过的通用子树扫描缓存（TTL 500ms）被实机证明**不能**用于写后读（席位卡每帧重复补建 ⇒「无限玩家」）、每帧列布局与标签订位（漂移）⇒ **全部回退为实时遍历**，本批只剩「单点化 + 组件化」收益（详见 `references/local-multicontrol-pitfalls.md` 坑 J）。**R2 第二批（2026-09-28）** = ③ 镜像五件套（药水 / 卡牌 / 金币得失 / 遗物 Obtain+Remove）与藏宝图的**门控 + 上下文判据 + 目标枚举 + 镜像作用域**收进 `Scripts/Runtime/LocalRewardMirror.cs`（行为零变化；S7 因"-2 反射目标"预期刷新基线）；marker **`2026-09-28-r172`**（**待实机一局**）。**R2 第四批（2026-09-28，本轮）= ④ Daily/Custom 同构席位对齐与角色指派入口**：新增 `Scripts/Runtime/PureLogic/LocalLobbySeatPolicy.cs`（席位计划 / reconcile 判定 / 指纹，由 `DailyLobbyPolicy` 通用化而来 + 新增 `ResolveTargetSeats` / `OrderedLocalSeats`）与 `Scripts/Runtime/LocalLobbySeatReconciler.cs`（「加席位 / 删多余本地席位 / 标 ready」三步编排），两个页面补丁各减约 60 行（净 **-294 行**），Custom 内联的 reconcile 判定改为调用同一纯函数；**角色来源不合并**（Daily = 日期种子逐席位驱动 `SetupLobbyParams`、Custom = 真人点选后同步 UI，属语义差异）；日志文案逐字保持；单测 **721 → 726**；marker **`2026-09-28-r176`**（**待实机**）。分支：R0/R1/R2 第一二批**均已合回 `master`（`08c9234` / `fc542bd`）**；本轮在 **`refactor/r2-daily-custom-seat-assign`**（未合回、未推送）；同日修复分支 `fix/wakuu-turnstart-copy-pick`（r173~r175 复制牌作答）已 `--ff-only` 合回 `master`（`3ce43dc`）。门禁：单测 **726 全绿**、静态层 **10 PASS**、`preflight -Deploy` 4 PASS / 3 SKIP | **R2 四项全部收口**；**R3 已动工**（2026-09-28 第一轮 = 席位身份唯一取数入口 `SeatRegistry` / `SeatIdentity` 纯逻辑 + 靶区清单，**一个调用点都没改**；单测 **744**、marker `2026-09-28-r177`；靶区与分批见 ADR §八 —— 实测裸身份比较 ≈48 处 / 28 文件、`CurrentControlledPlayerId` 读取点已从 13 涨到 **32**；**第二轮 B1 已落地** = 新增薄适配 `Scripts/Runtime/LocalSeatSource.cs`，**24 处**动作 / 前台读点改走它（写入点未动；动作/前台侧两路口径**各只剩 1 处**，都在奖励归属 ⇒ B2），marker `2026-09-28-r178`、单测 744、**待实机**，下一批 = **B1b 本地席位判定统一** + **B2 奖励归属**，契约见本文件 §R3）→ 之后 R4（拆 God class） → R5（生命周期契约）；**R3/R4 期间冻结功能改动**（M2 智能抓牌须排在 R3/R4 之后）；每步一实机、行为零变化。⚠ 已知未收的两处同形代码（**非** R2 清单项，待拍板）：`LocalSelfCoopContext.ReconcileStartRunLobbyPlayerCount`（标准角色选择页）的加/删/ready 三段与本次编排同形但有三处差异（多余席位过滤掉主席位 / ready 只遍历目标席位 / 日志字段不同），未纳入本轮 | 本表 + `runtime架构分层重构评估.md` |
| `长期方向L1-L3规划.md` | **部分落地**：L1 接口层已落地（任务 2.2，r31）；L3 离线静态层 CI 已落地（2026-09-16）；**L2 已扩为 `runtime架构分层重构评估.md`（#18，R0 动工中）** | L2 走该提案的 R1~R4；L1 剩"反射面收敛" | 本表 |
| `键盘手柄双输入本地双控可行性分析.md`（L1 档） | **待拍板**：结论已出（L1 轮流操作可行 / L2·L3 真正同时不可行）；**未实现** | 备选线索：`LocalDeviceSplitRouter` + patch `NControllerManager` 模式抢占 + 秒切防抖（仅认确认性输入），默认关 | 本表（备选） |
| `本地LLM辅助开发可行性分析.md` | **部分落地**：P0 冒烟 / P1 索引 / P2 日志分诊 / P3 双语 + diff 预审 / T2 NUnit 草稿**均已完成**；**P4 经实测改换做法**（明细当优先级清单 + 自写抽取器）；`functiongemma` 工具路由未动 | P4 若要继续：先写自己的 `sts2src` 逐卡效果抽取器 → 人审 → 固化 `PureLogic` 表，**默认关**且须过 §21.4.2 回归清单 | 本表 |
| `开发环境迁移Linux可行性分析.md` | **未动工**（纯调研，未动 U 盘/分区） | 推荐路线：先 Phase 0、再 Phase 1（两步没跑完前不要动 U 盘与分区） | 本表（待拍板） |
| `本地多角色扩展到Daily模式可行性分析.md` | **✅ 已实机通过（联网 + 断网各一局，2026-09-27，marker r161）；已随 v1.43.0 发布**：按提案 §五/§六 落地 ① 独立卡片入口（**不劫持官方 Daily**）② 异步大厅 reconcile（clamp 4）③ 按日期种子分配角色 ④ `DAILY_SCORE_SKIP` 禁止上传 ⑤ 每日页人数面板 ⑥ 每日页瓦库勾选/切换编辑席位；r156（POC）→ r157（入口改独立卡 + 每日页白名单）→ r158（**零劫持**：三个官方入口全部还给原版）→ r159（lobby 判空修每帧 NRE + 面板加严）；代码已提交并合回 master（`a2dc84b`，r160 经验固化 `c3db47c`，r161 `98b3f46`/`b0943de`）。**联网局（marker r161，4 席出征）与断网局（marker r161，2 席）各一局均通过**，详见 `§Daily 本地多控` | ① 断网局 ✅（2026-09-27，r161 通过）；② **BUG-21** ✅ 关单（r161）；③ 仍可择日用官方单机每日对照 3 条 `NDailyRunLeaderboard` 的 `ObjectDisposedException`；增强项（只让 primary 上传 1p 榜 / 超过 4 席 / 每日页 UI 精修 / `NDailyRunLoadScreen` 适配）留待后续 | 本表 + 仓库 `TODO.md §Daily 本地多控` / `§BUG-21` |
| `瓦库四功能-可行性核验报告.md` | **待拍板**（结论已出）：四功能均无架构级死路；功能一难度下调为「中」 | 按建议顺序（净化 → 我们联合 → 炼化 → 地狱战神）逐级动工，动工前需用户拍板 | 本表 + 提案 §7 |
| `瓦库炼化净化联合地狱战神-功能提案与可行性分析.md` | **未动工**（仅提案；已被 09-22 核验报告逐条核验） | 同上（以核验报告的顺序与难度为准） | 本表 |
| `Co-op_Bots联机队友兼容可行性分析.md` | **部分落地 + 实机进行中（r144 POC → r145 回合死锁 → r146 席位隔离 → r147「幽灵弹层 + 镜像来源席位」→ r148 商店 R1 通过）**：r144~r146 已提交并 `--ff-only` 合回 master（`50bff2b` 代码 / `25077d1` 文档）；r147+r148 已提交为 `1eefef7`，且已随 v1.43.0 合回 master（分支 `fix/r147-phantom-overlay-mirror` 已删除）。落地 = ① `CoopBotsAdapter` 反射适配器（探测 / `AutoPilot.Set` 逐席接管与释放 / 未装即优雅降级）② 席位驱动**三态互斥**（`CoopBotsSeatPlan` + `LocalSelfCoopContext`，POC 配置键 `coopBotsSeats`）③ **R1 商店 ACK 旁路补丁** `CoopBotsShopAckPatch`（**装了 CB 就要挂**，与其 Bot 是否被我们接管无关）④ **r145**：3 席「回合结束不了」（就绪补齐改成「只补本地席位 + 走真实方法调用」）⑤ **r146**：第三方席位隔离（`LocalSelfCoopContext.IsLocalSessionSeat`）⑥ **r147**：**幽灵弹层**根治 + 自愈，以及 `MirrorSeatPolicy` 五条镜像链的来源席位过滤。**实机已验**：r147 全部改动通过（0 条 add_child failed / 0 条幽灵弹层自愈 / 0 条 bot 奖励镜像）；**R1 于 r148 通过**（Bot 删牌服务→药水，两次旁路均 `count=1,seats=[]`，最终 `done=1`，正常离店并进入下一战斗）。**未做**：12 席三态 UI / 持久化、Phase 3 补丁面回归、**被接管的本地席位**的奖励归驱动方分流、**BUG-17** 根因。**2026-09-26 逐条实测（marker r148，第一幕整局；配置 `coopBotsSeats` 为空 ⇒ 本局 Bot 是 CB **自己的合成 Bot**、非我方 POC 接管）**：§R2 **11 点全部通过** —— #1 `地图自动跟投 … filled=3/3` **54 次 / 0 失败**（含 Bot 全票、随后均触发推进）；#2 `本地多控自动补齐敌方回合就绪 … mirrored=` **从不含 Bot id**（41 次）；#5 `RequestEnqueue 空引用已拦截` **0**；#7 `事件/流程金币已同步到其余角色` **0**；#8 `角色独立奖励已生成(Offer): player=<Bot>` **0** + `第三方席位卡牌奖励交回原版远端作答` 24；#9 `打开奖励界面: player=<瓦库>` **0**；#11 `瓦库火堆已自动选择 … success=True` 6 / `扫描瓦库休息区失败` 0；#6 `瓦库火堆队友选择已自动指定` 4；#3/#4 选人屏 **弱覆盖**（`角色选择页已创建本地人数 +/- 实体按钮` 4 次，无错位/残留）。同局 `add_child() failed` / `幽灵弹层已自愈` / `弹层阻挡自动流程` **全 0**（r147 稳定）。**仍待验**：**我方 POC 接管席位**路径下的 R2 回归（本局未接管）、BUG-17 诊断锚点 | ✅ ① R2 逐条实测**已完成（2026-09-26）** —— **清单已固化**：`Scripts/Tools/thirdparty_patch_overlap.py`（口径与命令见 `Co-op_Bots联机队友兼容可行性分析.md` §R2；逐条判据表同节，产物 `decision-records/第三方补丁目标交叉分析-CoopBots.md`）；② 事件末页选牌复现后按 BUG-17 的三条锚点分流；③ 之后再谈 Phase 1 的选人屏三态钮与 save tag v4 | 本表 + §BUG-17 |
| `局内加人与战斗临时玩家-可行性分析.md` | **未动工**（调研结论稿）：需求 A 局内加正式玩家 = 有条件可行但风险高；需求 B 战斗中临时玩家（召唤型）= 可行且有现成抓手 | 2026-09-25 **已归位**到 `maintenance-docs/decision-records/`（原先错放在 `maintenance-docs/` 根）；是否排期待拍板 | 本表（待拍板） |
| `原版药水一览表.md` / `原版附魔一览表.md` | **参考数据表**（非提案）；药水表已随 r136 校正"当前 mod 行为"列 | 改规则表时同步这两张表；附魔侧"优先选哪类牌"待用户填期望 | **§改进-4** |

> 与其它清单的关系：`维护现状分析.md §4.6` 是**快照**（保留定位描述，状态可能滞后）；
> 本表 + `decision-records/README.md` 是**单一口径**。发现两者不一致，先信本表，再顺手订正快照。
>
> ⚠ **2026-09-26 POC 实机结论（Co-op Bots 接管路径）**：接管链本身工作正常（配置 → 冲突收敛 → 接管 → 驱动分配 → 释放 全通过），
> 但接管席会掉进「谁都不管」的空档（奖励既不自动领、也不交回 CB；休息区气泡/视角/事件亦未按三态分流）——
> 根因 = 我们所有服务都按**瓦库名单**驱动。**结论：不做**（现状 CB 自己加合成 Bot 已能「真人+瓦库+CB」），
> POC **冻结为能力保留**、`coopBotsSeats` 保持默认空；四条现象清单（含证据锚点）见提案 §七「若重启 POC 的必办清单」。

---

## 实机反馈待办（2026-09-08，用户拍板：先记录、暂不修）

> 都是「能用但别扭」的**多人规模化**体验问题，数据层未见错误；先留档排期，本轮不动代码。
> 同类既有备案：`maintenance-docs/decision-records/瓦库托管优化可行性分析.md` §16.2。

### BUG-1 战斗第一回合能量不同步（新增，2026-09-08）

- **现象**：进战斗前选中的是**瓦库托管角色**时，进入战斗后第一回合——手牌等内容显示的是真人玩家
  （同时也是战斗开始时的默认第一个玩家），**能量条却是瓦库的**；手动切换一下角色即恢复同步。
- **初判**：战斗开始时「前台 / 控制上下文」与「能量 UI 归属」不是同一份状态源——手牌按战斗开始的
  默认玩家渲染，能量按当前托管上下文渲染；切角色会重刷两者所以自愈。与 §16.2 同源（前台上下文切换层）。
- **排查入口**：`CombatManager.SetupPlayerTurn`、本 mod 的 `CombatManagerTurnHookForegroundPatch`
  （回合开始 hook 前切前台）、能量 UI 的归属刷新（战斗 UI 能量条 / `PlayerCombatState` 能量同步）。
- 待确认：能量**数值**本身是否也错（数据层），还是仅 UI 串了（表现层）。
- ✅ **2026-09-10 r96 已修并部署（r96 实机未通过 → r97 再修后闭环，见下）**：根因 = 能量球与手牌分属两个玩家；
  按不变量「能量球必须与当前展示的手牌同属一个玩家」收口——新纯函数
  `CombatEnergyOwnership.TryResolveMismatch` + 手牌归属追踪 `_lastCombatUiPlayerId`
  （入战先记原版 `NCombatUi.Activate` 那一版）+ 入战延迟刷新改按手牌归属 +
  `SetupPlayerTurn` 前缀校正两次（切前台前后各一次）。校验日志：
  `战斗能量归属不一致已校正: 能量=…, 手牌=…, 受控=… → 重建为 …`、
  基准日志 `入战战斗UI归属已记录` / `战斗UI已刷新到当前角色`。marker **r96**，344 单测全绿。
- ⚠ **r96 实测未修复 → r97 再修（2026-09-10）**：r96 日志实证两处错误——
  ① 入战刷新的 `CombatManager.IsInProgress` 门禁在 `OnCombatSetUp` 时还是 false（战斗真正开始在其之后），
  三次调用全部空转，能量球从未重建（放宽为 `ActiveCombat` 亦可刷新）；
  ② 归属基准取了入战瞬间的 `LocalContext`（=瓦库），而真实手牌是开战后按**当前受控玩家**抽出来的，
  导致"基准=瓦库、能量=瓦库"被判成一致、一次校正都没触发 →
  改为**直接读手牌区实际卡牌的持有者**（`NCardHolder.CardNode.Model.Owner`），空手牌才退化到受控玩家，
  并删掉不可靠的 `_lastCombatUiPlayerId` 追踪。另加帧末延迟补校 + 每场一条
  `战斗能量归属核对: 能量=…, 手牌=…(cardOwner/emptyHand), 受控=…`。marker **r97**。
- ✅ **BUG-1 已闭环（2026-09-10，用户实机确认）**：r97 能量同步 + r99 辉星（储君第二资源）正常显示。
- ✅ **r97 能量已实机确认同步（2026-09-10）**；**辉星（储君/Regent 第二资源 Stars）不同步 → r98 已修（待复测）**：
  ① 入战切人提前到 `NCombatRoom._Ready` **前缀**（新 `NCombatRoomReadyForegroundPatch`，Combat 域已登记）——
  原版 `NCombatUi.Activate` 与第三方按 LocalContext 绑定本地玩家的 mod（本机装有
  RegentFX「万象辉星」，dll 内可见 `NCombatRoomReadyPatch`/`SetupStarRingForLocalPlayer`）
  都在旧时机之前就绑完了；前缀一定早于任何 mod 的后缀。只处理 `ActiveCombat`。
  ② 辉星纳入同一条不变量：`EnsureCombatEnergyMatchesHand` 同时核对辉星，能量一致时也会单独校正
  （新日志 `辉星归属不一致已校正`，核对行加 `辉星=` 字段）。
  ③ 修原版 `NStarCounter` 订阅死角：`Initialize` 只有 `!_isListeningToCombatState` 才订阅 `StarsChanged`，
  该标志无人复位 → 重绑改为「先退订旧玩家 + 复位标志」。marker **r98**。
- 🔧 **r98 实机证明归属本来就对 → 真根因是生命周期，r99 已修（待复测）**：r98 日志
  `战斗能量归属核对: 能量=326, 辉星=326, 手牌=326(cardOwner)` 全对、无校正 WARN；
  用户复测「**单人储君正常，本地多控下无论入战时是不是瓦库都不显示辉星**」。
  根因：原版把辉星计数器 `Reparent` 进能量球，而本 mod 每次切角色都**重建能量球并 QueueFree 旧的**
  → 辉星作为旧球子节点被一并带走（单人走原版路径，所以正常）。
  修法 `EnsureStarCounterDisplay`：辉星与能量球**解耦**——直接从 `star_counter.tscn` 实例化、
  挂在**战斗UI**下（场景原始父节点/设计位置），每次切人重绑 + 显式显隐 + 一条
  `辉星计数器就绪: player=…, visible=…, parent=…, pos=…` 诊断。marker **r99**。

### BUG-2 真人先结束回合后，切到瓦库点结束回合无效（2026-09-08 备案；2026-09-10 r104 已修，✅ 已实机确认）

- 已记录于 `瓦库托管优化可行性分析.md` §16.2 第 1 条（结束按钮状态机绑定前台）；用户 2026-09-08 再确认。
- 现状绕法：切回自己 → 再切到瓦库 → 点结束回合才生效。
- 🔧 **r104 修法（2026-09-10）**：根因 = **按钮归属取错来源**。原版 `NEndTurnButton.CallReleaseLogic`
  用 `LocalContext.GetMe(...)` 判定「结束谁的回合」，而本 mod 的 `LocalContext` 会为**瓦库后台出牌的动作归属**
  临时漂移；漂移到「已结束回合的角色」上时，点击被当成「撤销结束回合」（本 mod 补丁还会直接拦掉）→ 点了没反应。
  三条一起收口：
  ① **点击目标改取前台玩家**：新增 `LocalMultiControlRuntime.AlignLocalContextToForegroundForEndTurn()`，
  在点击瞬间把上下文校正到前台（`Session.CurrentControlledPlayerId`），后续原版逻辑即结算玩家正在看的角色；
  ② **按钮自愈**：新增 `ReconcileEndTurnButtonForForeground()`（战斗逐帧调用、内部 500ms 节流，判定用纯函数
  `EndTurnButtonReconcilePolicy.ShouldReconcile`）——前台角色可操作却按钮处于禁用/隐藏时重评一次
  （原版按钮只在 `TurnStarted`/`PlayerEndedTurn` 切状态，自动切人/瓦库自动结束回合会绕开它们）；
  ③ **文字与目标玩家对齐**：切到瓦库后不再残留「撤销结束回合」文案。
  新增诊断 `结束回合点击门禁: target=…, foreground=…, context=…, state=…, inputEnabled=…, focused=…, handMode=…, inPickFlow=…`。
- **验证步骤**：`marker=2026-09-10-r104` + `INIT_OK`；真人先结束回合 → 自动/手动切到瓦库（瓦库还有牌可出）→
  **第一次**点结束回合即生效；日志应有 `结束回合点击门禁` 或 `结束回合按钮自愈`（按钮曾被留在禁用态时）。
  回归：瓦库回合正常自动结束、真人回合结束按钮文字正确（未结束时 END TURN、结束后 UNDO）。
- ✅ **已闭环（2026-09-10，用户实机确认「测试过了确实没问题」）**。

### BUG-3 事件选项角标把「投票角色头像」挤到很右边（新增，2026-09-10，不影响游玩，先记录不修）

- **现象**：多人（本地多控）事件里选了某个选项后，该选项按钮上会出现**投票玩家的角色头像**
  （原版 `NEventOptionButton.PlayerVoteContainer` / `NMultiplayerVoteContainer`，头像= `player.Character.IconTexture`）；
  开着统计角标（`statBadge`）时，这些头像被「顶」到选项按钮很靠右的位置。
- **已核对的底层事实**（sts2src）：
  - 按钮场景 `scenes/events/event_option_button.tscn`：根 Control `custom_minimum_size = (800,100)`，
    `PlayerVoteContainer` 是**普通子节点**（`layout_mode = 0`，offset `532,75 → 781,105`，宽 249 高 30，`alignment = 2`=END 右对齐）。
  - `NMultiplayerVoteContainer.RefreshPlayerVotes` 给每个投票玩家 `AddChildSafely` 一个
    `ui/multiplayer_vote_icon`（`TextureRect`），由父容器排布；`alignment=END` → 图标从右往左排。
  - 我们的角标（`LocalStatBadgeUi.StatBadgeOverlay`）是**树根顶层 overlay，不是按钮子节点**，
    理论上不该参与按钮内部排布 —— 所以「顶开头像」的机理还不清楚，待复现确认。
- **待复现时收集**：① 角标位置档位 `statBadgeCorner`（默认左下；若当时在**右下**，角标矩形
  x≈728~792 / y≈75~95 与投票容器 x 532~781 / y 75~105 **正好重叠**，最可能就是这个）；
  ② 头像数量（玩家数越多、END 对齐越容易整体偏右溢出）；
  ③ 关掉 `statBadge` 后头像位置是否回正（用于确认因果，而不是「多人本来就这样」）。
- **修法备选**：① 角标档位落在右下时，对事件按钮目标做「避让投票容器」的横向偏移（纯函数
  `WakuuStatBadgeLayout.Resolve` 增参数，可单测）；② 事件角标改用按钮矩形**左侧**固定偏移，
  彻底避开右下投票区；③ 若确认是 overlay 每帧写 `Size/Position` 触发了按钮重排，则改为
  只画不写（CanvasItem `_Draw` 自绘）。

### BUG-4 第三方「次级资源」战斗UI切角色后仍显示（蕾克拉，2026-09-10 已修 r100，待复测）

- **现象**：LexNinja2 的**蕾克拉**在蕾忍角色上正常显示在能量旁边，但**切到其它角色后依旧显示**。
- **定性**：蕾克拉走 **RitsuLib 次级资源框架**（`[NodeAttachment] LEX_NINJA2_NODEATTACHMENT_LEX_KELA_COMBAT_COUNTER:
  NCombatUi -> NSecondaryResourceCounter`）。反编译 `SecondaryResourceCombatUiStateTracker` 实证：
  它**只在 `CombatStateChanged`** 时 `UpdateCombatUi(parent, LocalContext.GetMe(state))`——
  「本地玩家」在原版联机语义下是固定的；本 mod 切前台不触发该事件 → 计数器停在旧角色
  （`Refresh` 里的 `_hasBeenMaterial` 粘滞标记又让它不会自己消失；公共 `Bind(player)` 在玩家变化时会复位它）。
- **修法（r100）**：`Scripts/Runtime/LocalThirdPartySecondaryResourceBridge.cs`（**全反射 + 缓存 + try/catch**），
  每次重建战斗UI（切角色/入战）后调用 RitsuLib 公共入口
  `SecondaryResourceUiRuntime.UpdateCombatUi(NCombatUi, Player)`；未装 RitsuLib / 失败只记一次日志并跳过。
  成功日志 `第三方次级资源战斗UI已同步到当前玩家: player=…`。
- **验证**：切到非蕾忍角色 → 蕾克拉消失；切回 → 恢复。
- ⚠ **r100 残留场景 → r101 补强 → r102 改对根因（2026-09-10，待复测）**：蕾忍是瓦库 + 非 1 号位 +
  入战前停在瓦库视角时，进战斗后蕾克拉仍在，直到瓦库打完牌才自己消失。
  **r101 实机日志推翻粘滞假设**：入战时计数器本来就绑在 1 号位且已隐藏
  （`counters=1, bound=326, material=False, visible=False`）→ 是**之后**被改回瓦库的。
  真根因：RitsuLib 把「本地玩家」等同于 `LocalContext.GetMe`，而本 mod 的 `LocalContext.NetId`
  会为**瓦库后台出牌的动作归属**漂移 → 瓦库出牌期间 RitsuLib 自带刷新把计数器重绑到瓦库（显示），
  出牌结束上下文回真人 → 下次状态变化又隐藏。
  **r102**：新增 `SecondaryResourceCombatUiOwnerPatch`（ThirdParty 域，反射字符串目标
  `...SecondaryResourceUiRuntime:UpdateCombatUi`，前缀把归属强制为**前台玩家**；
  新增 `LocalMultiControlRuntime.TryGetForegroundPlayer()`，只认 `Session.CurrentControlledPlayerId`），
  r101 的桥也统一改用同一前台来源。marker **r102**。
- ✅ **BUG-4 已闭环（2026-09-10，用户实机确认）**：r102 起蕾克拉随前台正确显示/隐藏。

### BUG-5 进战斗后第一次切角色被弹回自己（2026-09-10 已修 r103，✅ 已确认）

- **现象**：战斗开始前停在瓦库视角 → 进战斗后**第一次**切角色只看到一次刷新动画、人还在自己身上，
  第二次才切到瓦库。
- **根因（r102 日志实证）**：不是"切换顺序差一位"，第一次热键确实切到了瓦库
  （`切换操控角色(指定): 326 -> 327` + `战斗UI已刷新到当前角色 327`），
  紧接着被 `source=wakuu-no-playable-cards-tick` 的自动化**弹回**（`TryAutoSwitchFromWakuuWhenAllWakuuNoPlayableCards`）。
  该自动化**每回合一次**（弹回后登记 `_wakuuToNonWakuuSwitchedRounds`），所以第一次弹回把名额用掉、第二次才停住。
- **修法（r103）**：`NoteManualSwitchToWakuu(source)` 挂在三个切人入口之后——手动来源
  （`hotkey*` / `player-state*`）切到瓦库、且该角色当前**确实没有可出的牌**时，本回合直接登记为已处理；
  有牌可出时不登记（不白占名额，免得削弱「瓦库打完把视图交回真人」的自动化）。
- **验证**：第一次切角色即停在瓦库；日志 `手动切到瓦库角色，本轮不再因「无牌可出」自动切走`。
- ✅ **已闭环（2026-09-10，用户实机确认）**。

### BUG-6 两个角色都带【工具箱】时，非前台那位被静默自动选卡（2026-09-10 r106 已修，✅ 已实机确认）

- **现象（2026-09-10 用户实机反馈）**：控制台给两个角色各塞了一堆「战斗开始/回合结束时选择卡牌」的遗物后，
  【工具箱】只有**前台那位**被问到、可以选；**另一位没被问，却照样自动拿到了工具箱该给的无色牌**。
- **日志实证（marker r105）**：
  `工具箱自动接管已命中: player=…327, reason=background-player` →
  `工具箱已自动选择首张卡: player=…327, card=RALLY`。
- **根因**：`Scripts/Patch/ToolboxPatch.cs` 的 `ShouldAutoPickFirstCard` 里有一条
  `isBackgroundPlayer = !LocalContext.IsMe(player)` → 非前台的玩家一律"自动选首张"。
  这是 **d2c2c31（2026-03-21「升级工具箱接管逻辑并绕过后台三选一阻塞」）** 加的临时手段；
  2026-08 之后后台选牌链路已经成熟（`CardSelectForegroundSwitchPatch` 切前台交真人 +
  `ShouldSelectLocalCard` 强制本地手选；b949dfa 实证「作用域外选牌必须切前台交真人，
  自动作答会导致进战斗黑屏」），这条自动选卡就变成**把真人的选择静默吃掉**了。
- **修法（r106）**：`ShouldAutoPickFirstCard` 只对**瓦库托管角色**接管（`IsWakuuEnabled`）；
  真人（包括此刻不在前台的另一位）走原版 `FromChooseACardScreen` → 自动切前台 → 真人自己选。
- **验证**：`marker=2026-09-10-r106`；两个角色都带工具箱进战斗 →
  两位都各自弹三选一（后台那位会先自动切前台，日志 `... source=combat-choice-FromChooseACardScreen`）；
  **不应**再出现 `工具箱自动接管已命中: reason=background-player`；瓦库角色的工具箱仍自动选首张
  （日志 `reason=wakuu-player`）。
- ✅ **已闭环（2026-09-10 实机日志）**：`FromChooseACardScreen` 对后台角色正常切前台（×2）；
  `工具箱自动接管已命中` / `工具箱已自动选择首张卡` 均为 **0 条**。
  用户拍板：**真人一律弹界面自己选，暂不做「非前台真人自动选」开关**。

### BUG-7 手牌「排序/动画」与数据不同步（2026-09-10 用户实测发现；**2026-09-11 r112 ✅ 已实机确认，关单**）

- **现象**：牌面内容都对，但**手牌顺序**与数据不一致（「数据链」只把**相邻**的牌变成数据链，
  所以错位非常明显）；**切换一下角色**（重建手牌 UI）就同步回来。
- **暴露时机**：r109 修好「瓦库打手牌变换牌会中断」之后才显形 —— 之前那一步直接抛异常中断整个出牌，
  现在数据层正常生效、只是视觉没跟上（r59/r109 都**故意**让后台角色的手牌变换跳过视觉：
  `IsMine=false`，因为前台手牌区根本没有它的牌）。
- **日志证据（marker r109，本局 3 次）**：
  `[手牌同步修复] 后台角色手牌变换：临时让开 NetId 以跳过前台动画查找: owner=…327, controlled=…326, netId=…327 -> …326`
- **机理（r111 已按源码核对修正）**：原假设「数据层把替换牌加到**手牌堆尾部**、顺序真的会变」**不成立** ——
  `sts2src/src/Core/Commands/CardCmd.cs:438-448` 对非 Deck 牌堆是
  `pile2.AddInternal(replacement2, item4)`，`item4` = 原牌索引；`CardPile.cs:83-97` 在 `index >= 0` 时
  `_cards.Insert(index, card)` → **数据层保序**。真正会脱节的是「屏幕上显示的手牌属于**非前台**角色」的窗口
  （切人后 `ScheduleDeferredCombatUiRefresh` 的延后重建窗口）：此时后台角色变换跳过视觉（r109
  `ShiftAwayFromOwner`）→ 这份手牌 UI 的顺序/内容不跟随数据，切一次角色（整表重建）就恢复。
- **修法（r111 → r112 收敛，按不变量收口）**：*显示的手牌 UI 顺序必须等于「前台角色手牌堆」的顺序*。
  ① 纯函数 `HandUiOrderPolicy.Decide(pileOrder, uiOrder)`（`Scripts/Runtime/PureLogic/HandUiOrderPolicy.cs`，
  多重集比较、同名重复牌按次数算）→ **只剩 `None` / `Reorder`**：只有「同一批牌、仅顺序不同」才重排，
  任何「多余 / 缺失」一律 `None`；② `LocalMultiControlRuntime.ReconcileDisplayedHandOrder(source)`：
  守卫「选牌/拖牌/出牌/有牌等待打出」一律不管；顺序不同 → `MoveChildSafely` 按数据重排 +
  `ForceRefreshCardIndices()`（**只动次序，不建节点、不删节点、不触发重建**）；陈旧显示/多余/缺失 → 纯诊断，
  同一差异持续 ≥1.5s 才记一条 WARN；③ 调用点 = `CardTransformNetIdPinPatch` 变换异步收尾（下一帧）+
  `LocalCombatSwitchTracker._Process`（战斗逐帧，250ms 节流）。+11 单测 → **399 全绿**，marker r112。
- ⚠ **r111 的教训（已修，务必别回退）**：r111 曾把「UI 多出陈旧节点 / 两边都缺」升级为「清多余节点 /
  整表重建」，实机立刻回归 —— **原版手牌变换的视觉更新故意延迟约 0.9s**
  （`NCardTransformShineVfx.PlayUntilCardUpdate` 先等 `0.75+0.125` 秒才 `UpdateCard`），
  这段窗口里「数据已是新牌、UI 还是旧牌」是**正常动画态**，据此整表重建会在出牌结算途中
  `hand.CancelAllCardPlay()` + 全量重建 → **打出的数据链停在屏幕中间不消耗**。
  所以本收口**只允许重排**这一种动作。
- **验证（请实机复测）**：`marker=2026-09-11-r112` + `INIT_OK`；
  **回归复测**：反复打「数据链」/「不等价交换」→ 牌正常结算消耗，**不再停在屏幕中间**，
  且**不应**再出现 `手牌UI与数据整体不一致，触发整表重建` / `战斗UI刷新顺延到选牌/出牌流程结束后`；
  **BUG-7 本体**：手牌顺序与数据一致，真出现顺序错位时应能看到
  `手牌UI顺序已按数据自愈: player=…, 重排=N, 前=[…], 后=[…]`。
  诊断（正常局几乎没有）：`手牌UI与数据存在差异但未处理（仅记录）: …多余=[…], 缺失=[…]` /
  `手牌显示归属与前台不一致（仅记录，重建由切人链路负责）` —— 有就把日志给我，它们指向真正该修的地方。
- ✅ **已闭环（2026-09-11，marker r112 实机确认）**：`手牌UI顺序已按数据自愈` 命中 **3 次**（均 `重排=1`），
  前后对比实证「数据把新牌插在原位、UI 把它排在末尾」——例如
  `前=[…,DEFEND_SILENT,DEFEND_SILENT,DATA_LINK] → 后=[…,DEFEND_SILENT,DATA_LINK,DEFEND_SILENT]`；
  同时 `整表重建` / `战斗UI刷新顺延` 均 **0 条**（r111 那局是 5+5，14 次数据链不再卡牌），
  诊断项 `存在差异但未处理` / `显示归属与前台不一致` 均 0 条（无误报）。
  用户结论：「我的视角和瓦库视角都没有什么问题」。**BUG-7 关单**。
- **优先级**：不影响游玩（不阻塞、数据正确）。

### BUG-8 瓦库自动出牌作用域被游戏侧 `OrbQueue is full` 打断（2026-09-11 实机日志发现；**暂不修，已记录**）

- **日志（marker r110，L9954 / L9957）**：
  ```
  [WARN] 瓦库选择器作用域异常退出: player=…327, round=2, error=OrbQueue is full
  [WARN] 瓦库看门狗重启失败:        player=…327, source=combat-watchdog, error=OrbQueue is full
  ```
- **根因：不是我们抛的，是游戏原生异常**。`sts2src/src/Core/Entities/Orbs/OrbQueue.cs:57-59`
  → `if (Orbs.Count >= Capacity) throw new InvalidOperationException("OrbQueue is full");`
  而 `OrbCmd.Channel`（`sts2src/src/Core/Commands/OrbCmd.cs:80-84`）的流程是「队列满了先 `await EvokeNext()`
  挤出一个腾位置，再 `TryEnqueue`」——**第二步仍会抛**，这就是那个窗口。
- **归因：由真人自己触发**。异常前约 20 行实锤：
  `9886 Enqueueing …CARD.IGNITION (38153845) … from owner …326`（**真人**在打一张充能 orb 的牌）、
  `9937 Asset not cached: res://scenes/orbs/orb_visuals/plasma_orb.tscn`
  → 异常冒泡到瓦库的自动出牌作用域，被我们的 catch 记下。
- **影响**：本次自动出牌 pass 中断；但 L9962 起看门狗已 `重新进入全量出牌模式 round=2` ——
  **自愈、未软锁**，整局 3 个回合仅 1 次。
- **决策（2026-09-11 用户拍板「先不管它」）**：**暂不改代码、也不降级日志**，避免掩盖真实的中场中断。
- **将来若要处理，可选路线**：① 归类为「必定自愈的可恢复游戏侧异常」→ 降到 INFO + 干净重启，
  但**保留计数**不丢信息；② 深挖上下文漂移：同局另有 **3 次**
  `检测到手动出牌上下文漂移，已强制校正: 327 -> 326, source=card-enqueue-manual-play`，
  怀疑 orb 归属可能受「上下文短暂钉在瓦库身上」影响 → 顺 `OrbCmd.Channel` 的 `player` 参数来源追。
- **优先级**：低（偶发、自愈、不影响数据）。

### 已知项（游戏侧告警）：被取消的动作仍跑完 → `ActionExecutor` 报 `was in state Canceled`（r151 实机发现，**暂不修**）

- **现象（marker r151，2026-09-26，归档 `logs-archive/godot__20260926-184411__r151.log` L11738）**：
  ```
  [ERROR] GameAction PlayCardAction card: CARD.YUI_CARD_EXPANSION_CARD_WEAKENING_STRIKE (…) index: 49
          targetid: 3 finished execution, but was in state Canceled! The task probably kept executing
          in a paused state without properly resuming.
  ```
  整局 **1 条**（该局 5 条 `[ERROR]` = 2 条第三方 mod 分支不兼容 + 2 条游戏侧 VFX 空引用 + 本条）。
- **归因：游戏侧，不是我们的**。栈是 `GameAction.Execute()` → `ActionExecutor.ExecuteActions()`；
  报错那张牌 `owner=…326`（**真人**），而本局「让真人插队」撤掉的是**瓦库**的两张动作
  （L16173 `C_HH_UNITED_WE_STAND`、L17588 `DEFEND_IRONCLAD`，均 `source=card-enqueue-manual-play`）
  ⇒ 撤掉的不是这一张。上游是游戏自身：`Combat state becomes EndTurnPhaseOne (from PlayPhase).
  Starting to cancel all player-driven actions` + `Cancelling non-executing actions of type
  PlayCardAction owned by …326`（本局 6 次）。
- **影响：不阻塞**。紧随其后 L11747 `Completed execution of action … attempting to find new action`、
  L11750 `Action … becomes new ready action` ⇒ 队列正常推进，未卡、未软锁。
- **决策（沿用 BUG-8 口径）**：**暂不改代码、不降级日志**。

### BUG-9 SL（读档）后个人记录的抉择未回滚 → 选择率被污染（2026-09-11 用户反馈；**r118/r119 定位 → r120 改为"写时幂等" → ✅ 2026-09-12 实机确认，关单**）

- **现象（用户 2026-09-11）**：「本地事件选项选择率记录好像有点问题？比如说我 **SL** 了它还是会记录我
  **SL 前的选项**」。
- **根因（代码级，成立）**：记录**不随游戏存档回滚** —— 真人一点事件选项就立即
  `_store.eventChoices.Add(...)` + `Save()` **落盘**（`LocalPersonalRecorder.RecordHumanEventPage`），
  而 `runKey = 种子 + 玩家数` 是**跨存档稳定**的（注释里就写着"跨存档稳定"）。
  只有 `RecordRunEnded(isAbandoned: true)` 才按 runKey 删数据，**SL 不触发**。于是：
  ① 同一事件页被**重复记多行** → `Offered`/`Chosen` 被放大（`CountEventOptionSlice` 是逐行累加）；
  ② SL 前点过、SL 后**改选**的选项仍残留 `chosen=true` → 继续参与选择率统计（用户看到的就是这条）。
  （注：`CountEventWinSlice` 按 runKey 去重，所以**胜率**没被放大，只有**选择率**被污染。）
- **修法（r118）**：**按存档点回滚**。
  1. 纯逻辑 `Scripts/Runtime/PureLogic/WakuuPersonalRollback.cs`：`RollbackAfter(store, runKey, cutoffUnixMs)`
     —— 丢弃该 runKey 中 `ts >` 存档点的行（四张表同一口径；边界取严格大于 = 存档当刻的视为已固化），
     返回各表丢弃数（+6 单测 `WakuuPersonalRollbackTests`）。
  2. 运行层 `LocalPersonalRecorder.RollbackToLastSavePoint(source)`：从
     `current_run_mp.save` 的**最后修改时间**取"上一次存档点"（`GodotFileIo.GetLastModifiedTime`，
     游戏自己也是这么读存档时间的），调上面那个纯函数；**取不到存档点就保守不删**（只 WARN）。
  3. 时机：`LocalMultiControlRuntime.OnRunLaunched`（进局/读档的公共入口）→ 日志
     `个人记录-读档回滚: source=run-launched, run=…, 存档点=…, 丢弃 events=N, …`（只在真丢弃时打）。
  4. 语义：**晚于存档点 = 会被重玩的那一段 → 丢弃；早于等于存档点 = 已随存档固化 → 保留**，
     所以「隔天继续游戏」不会误删真实记录；新开局时 runKey 不同（或存档已被清）→ 天然删 0 条。
- **已知边界**：若 SL 是**手工复制/覆盖存档文件**（而不是游戏内"退到主菜单→继续游戏"），
  存档文件 mtime 会变成复制时刻 → 我们的判据会偏晚、可能删不到东西。这种情况需要另加机制，
  **待用户确认 SL 方式**（见验证步骤第 3 条）。
- **历史污染**：已经写进 `personal_stats.json` 的重复行**无法自动区分**（与合法重复不可辨），
  如需干净重来可备份后删除该文件。
- **门禁**：0 警告 0 错误、444 单测全绿（+6）、`clr_compat_check` PASS、marker **r118** 已部署且 `dll_check` 字节一致。
- ⚠ **r118 实机发现回滚根本没执行（r119 修）**：r118 日志里 `source=run-launched` 出现过 2 次
  （第 2 次就是 SL 读档），但 `个人记录-读档回滚` **一条都没有** —— 回滚在 `runKey == null` 处**静默返回**了：
  原实现用 `RunManager.Instance.DebugOnlyGetState()` 取 runKey，而 `OnRunLaunched` 那一刻全局 state **还没就绪**。
  **r119 修法**：改为直接用调用方传入的 `RunState` 构造 runKey（新增 `BuildRunKey(RunState)` 供复用，
  `CurrentRunKeyOrNull` 一并改走它），并且**无论是否丢弃都打一条**
  `个人记录-读档回滚检查: source=…, run=…, 存档点=…, 丢弃 events=N, cards=N, shop=N, removals=N, total=N`
  （避免"没生效"与"没东西可删"再次无法区分）。marker **r119** 已部署（sha256 `c5aa4b27...`）。
- **抓牌（卡牌 offer）同样被 SL 污染，且已被同一机制覆盖**：`cardOffers` 与 `eventChoices` 一样是
  真人点选时**立即落盘**、`runKey` 跨存档稳定 → 抓取率 `PickRate = Picked / Offered` 会被放大。
  r118 的回滚本就是**四张表同一口径**（`eventChoices`/`cardOffers`/`shopPurchases`/`cardRemovals`），
  所以 r119 修好取 runKey 之后**事件与抓牌一起生效**，无需另做。
- ✅ **r120：思路修正（用户判断正确）—— 放弃"按存档点回滚"，改为"写时幂等"**。
  r119 实机日志显示回滚**跑起来了但 `total=0`**（一次都没删）：两次进局的存档点只差 13 秒，
  且恰等于**读档那一刻** → **读档/进局动作本身会重写存档文件**，存档文件 mtime 永远 ≥
  最近一次抉择的时间，`ts > mtime` 恒为假。**根因是"从外部猜出游戏回滚到了哪个状态"这件事本身不可靠。**
  **新思路（r120 已实现）**：不猜、改成**幂等** —— 每次写入前先删掉"同一抉择标识"的旧行，只保留最后一次。
  标识：事件页 `(runKey, eventId)`；卡牌批次 `(runKey, batchKey)`（batchKey = 该批卡 id 去重排序拼接，
  新增 `PersonalCardOfferRecord.batch` 字段；重 roll 出不同牌 = 新批次，不误合并）；
  商店 `(runKey, act, kind, item)`；删牌 `(runKey, card)`。
  纯逻辑 `WakuuPersonalDedupe`（+10 单测，含"SL 前选 A、SL 后改选 B → 旧页整页替换、A 不再残留 chosen"）。
  r118/r119 的 `WakuuPersonalRollback` + 回滚调用**已整体删除**（基于错误前提，`dll_check` 已确认产物里没有）。
  日志可验证：`个人记录-事件选择: …, 覆盖旧页=N` / `个人记录-卡牌奖励批次: …, 覆盖旧批次=N`。
  代价（刻意接受）：同一局内**合法地**重复遇到同一事件会被合并为一次（事件一局基本不重复；卡牌按卡集合区分，
  误合并概率很低，且"最终选了啥"比"重复计数"更符合直觉）。
  门禁：0 警告 0 错误、**448 单测全绿**、`clr_compat_check` PASS、marker **r120**（sha256 `9a4a1baf...`）。
- ⚠ **r120 残留不一致（已记录，本轮未改）**：四张表的去重键粒度分别是 —— 事件页 `(runKey,eventId)`、
  卡牌批次 `(runKey,batchKey)`、商店 `(runKey,act,kind,item)`、**删牌 `(runKey,card)`（唯一没带 `act` 的）**。
  而 `PersonalCardRemovalRecord` 本身有 `act` 字段、商店那版也带了 `act`，所以
  「同一局在不同幕（或同一幕）**合法地**删两张同名牌」会被合并成一行 → 删牌偏好被少算。
  加上 `act` 既修得掉它，又**不影响 SL 覆盖语义**（SL 不会跨幕）。属低风险小修，**另开一轮做**。
- **r120 验证方法**：进事件 → 点一个选项 → 退到主菜单 → 继续游戏 → 重新点（可改选）→
  期望 `个人记录-事件选择: … 覆盖旧页=1`（**改选也应为 1**，且 SL 前那一项的 `chosen` 行不再残留）；
  卡牌奖励同理看 `个人记录-卡牌奖励批次: … 覆盖旧批次=1`。
- ✅ **2026-09-12 实机确认通过（marker r120）**：用户按上述路径复测，**日志 + 落盘数据双向验证** ——
  - 该 `(runKey, eventId)` 在此之前已被历次 SL 累积成 **8 行**，本次抉择落盘时 `覆盖旧页=8`
    → 一步收敛为 1 页（本页 2 个选项 = 2 行）；**这 8 行就是用户所报 bug 的实物证据**。
  - SL 读档后**改选**（REST → TAKE_SCULPTURE）：`覆盖旧页=2` → 旧页整体替换。
    `personal_stats.json` 里该 `(runKey, eventId)` **恰好 2 行**：
    `TAKE_SCULPTURE chosen=true` / `REST chosen=false`
    ⇒ 用户原报的「SL 前点过的选项残留 `chosen=true`」**已彻底消失**（这是外部看不出、只能查盘的一条）。
  - 前后两次 `瓦库事件按个人统计选取` 的 `offered=3, withData=2` **完全一致**
    ⇒ 统计口径跨 SL **稳定、不再被放大**。
  - ✅ **同口径的卡牌批次去重（`覆盖旧批次=`）已于 2026-09-13（marker r128）复现并通过**：
    一局内 3 条 `个人记录-卡牌奖励批次` 的 `覆盖旧批次` = 0 / 0 / **3**；落盘 `personal_stats.json`
    交叉核对：本局 6 个不同批次各 3 行、互不重复，最后一条把 SL 前那 3 行**整体替换**
    ⇒ 选牌一次 SL 只留最后一页，抓取率不被放大。**本节可关单**。
  - 本轮复测会话**无战斗**，故 r116/r117 的战斗路径未参与（r117 早前已单独实机确认）。

### BUG-10 瓦库在「回合被强行结束」后仍继续出牌（虚空形态，2026-09-13 用户实机发现；**r122 初修 → r123 收敛为"按结束来源归因" → ✅ 2026-09-13 实机确认，关单**）

- **现象（用户原话）**：瓦库打出**虚空形态**后，"本来虚空形态打出后强行结束回合无法出牌，
  但是瓦库结束回合了也能出牌"。**与两个新开关（出牌队列实验档 / 出牌加速）无关** —— 四种组合都能复现。
- **根因（代码级）**：`VoidForm.OnPlay` → `PlayerCmd.EndTurn(owner, canBackOut: false)`
  → `CombatManager.SetReadyToEndTurn` → 该玩家进入 `PlayersReadyToEndTurn`。
  原版拦人的手段是 **UI 层**：`PlayerCmd.EndTurn` 里 `if (LocalContext.IsMe(player)) OnEndedTurnLocally()`
  → `CombatManager.PlayerActionsDisabled = true` → `NPlayerHand.AreCardActionsAllowed()` 返回 false。
  而**我们的自动出牌链路完全绕开 UI**：`TryGetAutoplayUnsafeReason` 只看全局战斗状态
  （PlayPhase / `CurrentSide` / 弹层 / 拖牌 / 瞄准），**从未检查"这一位自己的回合是否已结束"**；
  `TryScheduleWatchdog` 也只按"手上还有可出牌"就调度 → 瓦库继续出牌。
  （同一根因也覆盖更隐蔽的一条：瓦库自动结束回合后，本回合内又因别人的效果拿到可出牌 → 继续打。）
- **为什么不能用 `PlayerActionsDisabled` 当判据**：它是**全局单值**，而且 mod 自己在
  `LocalMultiControlRuntime.ReevaluateEndTurnButtonState`（L2457）里按**前台玩家**反复重算并写回它
  （r104 的按钮自愈）—— 它只代表"当前前台那位"，代表不了别的瓦库。
- **修法（r122 初版 → r123 收敛）**：拦截位置不变（出牌循环 + 看门狗调度两处），
  但判据从"只要 ready 就停手"改为**按结束来源归因**（r122 那一刀切宽了，把用户想保留的行为一起否掉了）：
  ① **位置**：出牌循环 `TryGetAutoplayUnsafeReason` 熔断收手（**真正的拦截点**，覆盖"循环中途打出虚空形态、
     回合当场结束"的同帧情况）；`TryScheduleWatchdog` 同一条 → 干脆不调度，省掉每 300ms 空转与重复 WARN。
  ② **判据**（纯函数 `WakuuTurnEndOrigin.ShouldStopAutoplay`，+9 单测）：
     - 被**卡牌效果/原版强行结束**（`VoidForm.OnPlay` → `PlayerCmd.EndTurn`）**或来源不明** → **停手**；
     - 被**模组自己收口**（`TryEndAllPlayersWhenNoCards`）且 `!AllPlayersReadyToEndTurn()` → **放行**
       （即用户要求保留的"模组收口后，本回合内又因别人的效果拿到可出牌 → 继续打"）；
     - 一旦 `AllPlayersReadyToEndTurn()`（回合马上推进）→ 一律停手，不许打进攻城窗口。
  ③ **归因来源**：`CombatManager.SetReadyToEndTurn` 的前缀补丁（`CombatManagerPatch`，本就在）登记
     "这一位的 ready 是谁造成的"；模组自己发起时用 `WakuuTurnEndOrigin.BeginModIssuedEnd()` 包裹那一次
     `PlayerCmd.EndTurn`。**外部结束优先于模组收口**（同一回合先被模组收口、后又被打出虚空形态 → 仍停手）。
- **验证方法**：① 瓦库打出虚空形态 → 期望 `… reason=player-ready-to-end-turn`，之后该瓦库不再出牌；
  ② **模组收口后拿到新牌**：瓦库无牌 → 自动收口 →（别人给牌/给能量后）→ 期望它**继续出牌**
  （日志里**没有** `player-ready-to-end-turn` 熔断行）。
- **关于"加个瓦库不自动结束回合 / 等真人结束再结束"的开关（用户 2026-09-13 提议，本轮未做）**：
  排查后的结论是**当前架构下"瓦库不自动结束"会挂死** —— 推进到敌方回合要求**所有玩家都 ready**，
  而 mod 关闭瓦库的唯一时机就是 `TryEndAllPlayersWhenNoCards`（逐帧 tick 只切视角、**不**结束回合），
  且触发源只有"真人点结束回合"与"真人结束后的兜底"两条。瓦库自己不结束 → 没人再触发 → 敌方回合永不开始。
  "等真人全部结束再收口"同理需要**新增逐帧收口**（否则最后一个真人结束那一刻若瓦库还有牌就会漏收口）。
  鉴于 r123 已用归因把用户要的行为保住，**先不加开关**；要做的话应连带补"逐帧收口"，等用户拍板。
- 门禁：0 警告 0 错误、**467 单测全绿**（+9）、`clr_compat_check` PASS、marker **2026-09-13-r123**、
  `dll_check` 字节一致（`WakuuTurnEndOrigin` 在）。**r122 已由用户实机确认虚空形态已修**。
- ✅ **2026-09-13 实机确认（r123，日志实证）**：瓦库 `…329` 在 round 1 出 3 张牌
  （`STRIKE_REGENT` / `FALLING_STAR` / **`VOID_FORM`（actionId=15，栈里有 `VoidForm+<OnPlay>`）**）后
  立刻 `瓦库自动出牌已熔断跳过本次执行: player=…329, round=1, reason=player-ready-to-end-turn, played=3`，
  该回合再无出牌 ⇒ **虚空形态这条完全符合预期**；同局 43 张牌全走动作队列（实验档）且全部完成。
- ✅ **2026-09-13 用户在 r124 上做了"对照实验"（两条都通过）**：分别给**被自动结束回合**的瓦库和
  **被虚空形态结束回合**的瓦库塞牌 —— **前者依旧能打牌**、**后者不打牌**。
  与设计完全一致：r123 的归因判据（模组收口 → 放行 / 外部强行结束 → 停手）行为正确。
  同局 `动作队列UI入队触发空引用` **0 条** ⇒ BUG-11（r124）修复也实测生效。
- ⚠ **r124 首版的观察日志判据写错了（r125 修正）**：那条 `瓦库在模组收口后继续出牌（…）` 原先只看
  `IsPlayerReadyToEndTurn`，既不校验归因、又放在闸门**之前** ⇒ 实机日志出现误报：
  `8925 走动作队列完成 … VOID_FORM` → `8926 瓦库在模组收口后继续出牌`（**误报**，其实是虚空形态造成的结束）
  → `8927 熔断跳过 … reason=player-ready-to-end-turn`。
  **r125** 把它改为：判据用新增纯函数 `WakuuTurnEndOrigin.IsModIssuedExemption`（= 闸门放行的同一条件），
  并且挪到**闸门之后**调用 —— 即"闸门已放行、下一次迭代真的要去打牌"才记录。
  **行为零变化（只动日志）**，+2 单测（豁免状态真值表）。

### BUG-11 `NCardPlayQueue.OnActionEnqueued` 对「本地非前台玩家出牌」必然空引用（2026-09-13 实机日志发现；**r124 已修 → ✅ 2026-09-13 实机确认，关单**）

- **现象（日志）**：`动作队列UI入队触发空引用，已拦截避免阻塞: action=PlayCardAction card: CARD.DEFEND_IRONCLAD index: 25 …,
  context=76561198422527326` ×6（既有 fail-safe 的 WARN，600ms 限流）。
  **出牌本身不受影响**（同局 43 张瓦库出牌全部完成），用户观感也正常。
- **根因（代码级，是我们自己的软肋被放大）**：原版 `NCardPlayQueue.OnActionEnqueued` 要把"别人打出的牌"
  画进出牌队列；非本地分支取 `creatureNode.PlayerIntentHandler.CardIntent` 当飞牌起点 —— 而
  **本 mod 主动关掉了远端意图 UI**（`NMultiplayerPlayerIntentHandlerPatch` 让
  `NMultiplayerPlayerIntentHandler.Create` 返回 null）⇒ `PlayerIntentHandler` 为 null ⇒ **必然空引用**。
- **为什么现在才明显**：以前只有"本地非前台玩家手动出牌"这种偶发情况会撞；方案 D 实验档（`wakuuPlayQueue`）
  让**瓦库的出牌也走 `PlayCardAction` 入队** ⇒ 变成"每次瓦库出牌都可能撞"。
- **修法（r124）**：新增前缀守卫 `NCardPlayQueueActionEnqueuedGuardPatch` —— **先判后跳**：
  本地分支要求 `NPlayerHand.Instance != null`、远端分支要求 `creatureNode.PlayerIntentHandler != null`，
  否则 `return false`（等价于"这张牌不进 UI 出牌队列"，与原本被 Finalizer 吞掉后的可见结果一致，
  但**没有异常、没有 WARN、也没有 `AlignContextForActionOwner` 那次无谓的上下文校正**）。
  原 Finalizer 保留作最后一道网。已在 `PatchDomainMap` 登记（否则 `PatchDomainMapTests` 直接红）。
- 门禁：0 警告 0 错误、467 单测全绿（含补丁域登记哨兵）、`clr_compat_check` PASS、marker **2026-09-13-r124**、`dll_check` 字节一致。
- **验证方法**：开实验档打一局战斗 → 日志里**不应再出现** `动作队列UI入队触发空引用`。

### BUG-12 真人删牌统计从未记录（2026-09-13 实机发现；**r129 定位 → r130 修正 → ✅ r130 实机确认，关单**）

- **现象**：`个人记录-删牌` 一条都没有；落盘 `personal_stats.json` 的 `cardRemovals` **全历史 0 行**
  （425 KB / 8 局的累计数据），而同局 `cardOffers` / `eventChoices` 记录正常。
- **实机证据（marker r128）**：商店删牌走了 3 次（`商店-删牌归属玩家: context=…326` ×3），
  其中一次选牌链路完整结束 —— `NDeckCardSelectScreen` → `Player …326 chose cards [WATCHER-DEFEND_WATCHER]`，
  金币与删牌都生效，但**没有**任何删牌记录。
- **真正根因（r130，代码级）**：记录守卫拿 **AsyncLocal 字段当值**比较。原守卫是
  `if (IsEnabled && !TestMode.IsOn && !InEventAutoChoiceScope.Value
  && AutoClaimCardOwnerId == null && LocalWakuuMerchantAuto.PurchaseOwnerId == null) { 记录 }`，
  而 `PurchaseOwnerId` 当时是 **`internal static readonly AsyncLocal<ulong?>` 字段本身** —— 恒非 null
  ⇒ `== null` 恒 false ⇒ **整块守卫恒不成立、永远不记录**（`AutoClaimCardOwnerId` 是属性，
  所以只有这一条错，也只需要这一条错就能让记录全丢）。
  ⚠ r129 我先怀疑的是「锚点挂在 4 行包装方法 `FromDeckForRemoval` 上、被 JIT 内联」，并把守卫从
  `== null` 误翻成 `!= null` —— 同样是字段/值混淆、方向相反（恒真 ⇒ 每次必跳过），
  于是 r129 实机日志里出现两条 `跳过: 瓦库商店自动采购作用域内`。**"补丁挂上了"从不等于"补丁会跑"，
  但这次真正的原因是判据写错**，教训一并记下。
- **修法（r129 + r130）**：
  ① `LocalWakuuMerchantAuto` 的 AsyncLocal 收私有，改经**值属性** `PurchaseOwnerId` 暴露
     （与 `AutoClaimCardOwnerId` 同套写法）⇒ 调用方不可能再把字段当值比；
  ② 新增纯逻辑 `WakuuRecordScopePolicy`：
     - `IsDeckRemovalPrompt`：只认 prefs 提示键 `TO_REMOVE`（`FromDeckGeneric` 还被
       `DollysMirror` 复制 / `WoodCarvings` 变化复用，必须过滤；`TO_EXHAUST` / `TO_DISCARD`
       是**手牌**语义，同样判否）；
     - `IsAutoScopeOwnedBy`：自动化作用域**按归属者比较**（AsyncLocal 会沿异步链残留，
       只看"非空"会把真人自己的操作一起吞掉 —— r129 实机就是这样连丢 2 张删牌的）；
  ③ 锚点从 `CardSelectCmd.FromDeckForRemoval`（4 行包装方法，有被内联的风险）移到
     `CardSelectCmd.FromDeckGeneric`（async、函数体大，r129 实机确认会触发）；同一类判据一并收紧：
     卡牌奖励快照 / 事件网格入卡组 / 商店购买；
  ④ **不再静默**：原实现任一条件不满足就无声返回（r118 老毛病）。改成先算 `RemovalRecordBlockReason`，
     每次命中都打一条
     `个人记录-删牌钩子命中: owner=…, prompt=…, min/max=…, 选中=N, 瓦库形态=…, 记录｜跳过: <原因>`；
  ⑤ `+8 单测`（`WakuuRecordScopePolicyTests`），含「别人的自动化作用域不算本人」回归用例与
     「锚点不许挪回 `FromDeckForRemoval`」的反射哨兵。
- 门禁：构建 0 警告 0 错误、**487 单测全绿**、`clr_compat_check` PASS、marker **2026-09-13-r130**、
  `dll_check` 字节一致（`WakuuRecordScopePolicy`/`IsAutoScopeOwnedBy`/`FromDeckGeneric` 在、
  `__runOriginal` 不在）。
- **验证方法（请实机；r129 那次的结果已用作定位，本次是修正版）**：
  ① 商店删牌服务删 2 张**同名**牌 → 期望两条 `个人记录-删牌钩子命中: … prompt=TO_REMOVE, 选中=1,
     瓦库形态=False, 记录`，紧跟着两条 `个人记录-删牌: … card=<牌名>, 覆盖旧行=…`
     （同一幕第 2 张同名应 `覆盖旧行=1`）；
  ② 查盘：`personal_stats.json` 的 `cardRemovals` 应新增行，且同一 `(runKey, act, card)` 只留最后一行；
  ③ 反向核对**不该记**的：变化 / 复制类选牌（`prompt=TO_TRANSFORM` 等）**不应**出现"钩子命中"行；
     瓦库自己的事件删牌应出现 `跳过: 瓦库事件自动选择作用域内（归属者=本人）`；
  ④ 顺带看：商店**买**东西（卡/遗物/药水）应出现 `个人记录-商店购买: … 覆盖旧行=…`（同一修法）。
- ✅ **2026-09-13 实机确认（marker r130，日志 + 落盘双向验证）**：用户连删 **2 张同名防御**（`WATCHER-DEFEND_WATCHER`）、
  商店买 2 张卡 ——
  - 两次删牌都是 `个人记录-删牌钩子命中: owner=…326, prompt=TO_REMOVE, min=1, max=1, 选中=1,
    瓦库形态=False, **记录**`（不再是被 `跳过:` 吞掉），紧跟着
    `个人记录-删牌: … card=WATCHER-DEFEND_WATCHER, 覆盖旧行=0` 与 **`覆盖旧行=1`**
    ⇒ 同一幕删两张同名牌，落盘按 `(runKey, act, card)` 只留最后一行（r120 写时幂等的预期行为）；
  - 商店购买首次正常入账：`个人记录-商店购买: … kind=card, item=WATCHER-RUSHDOWN, gold=77,
    覆盖旧行=0` 与 `item=WATCHER-SHARED_WISDOM, gold=157, 覆盖旧行=0`；
  - 查盘：`cardRemovals` 由 **0 → 1 行**、`shopPurchases` 由 **2 → 4 行**（本局新增 2 行）；
  - 本会话我们的 WARN 只有既有启动项（本我牌守卫/解放补挂、第三方补丁 owner 审计），
    5 条 `[ERROR]` 全部是第三方/游戏侧（mod 分支 min/max、BetterModMenu 超时、本地化格式串）。
  - 本会话无战斗（只进商店），战斗路径回归与上下文漂移降噪已在 r129 会话确认
    （4 条 INFO / 0 条 WARN）。**BUG-12 关单。**

### BUG-13 战后卡牌奖励不被瓦库自动领取（2026-09-14 实机发现；**r134 已修 → ✅ 2026-09-14 实机确认，关单**）

- **现象（用户）**：「战斗结束后瓦库不会自动领取**卡牌**奖励（其它奖励依旧领取）；我退出重进后瓦库就正常自动领取了。」
- **实机证据（marker r133，`godot.log` 行 13128~13189）**：同一场战斗的 5 个瓦库，金币/药水全部领取成功，
  卡牌奖励**全部失败**，两条日志成对出现：
  - `检测到真人选牌请求，本次跳过瓦库选择器改走正常UI: chooser=76561198422527326`（chooser = 真人）
  - `瓦库奖励自动领取失败，保留为人工领取: reward=CardReward, error=Card selector unset during test!`
  - 紧邻的探针 `瓦库选择器栈探针: source=apply-control-before-merged-rewards-offer-room-end, selectorStackCount=0`
    说明那一刻**全局选择器栈是空的**；本局奖励走的是 `读档重放路径检测到战后奖励，转为后台弹出`（`isPreFinished=True`，
    fire-and-forget）。
- **根因（代码层）**：`CardReward.OnSelect` 读的是 `CardSelectCmd.Selector`
  （`sts2src/src/Core/Rewards/CardReward.cs:207`，读不到就抛 `Card selector unset during test!`）；
  而 r113 的 `CardSelectCmdSelectorGuardPatch` 会按 AsyncLocal
  `CardSelectForegroundSwitchPatch.CurrentChoicePlayerId` 决定处置 —— **归属者是真人时把 `Selector` 改成 null**
  （这是"瓦库出牌循环进行中，真人同时打出需要选牌的卡"的正常设计）。
  问题在于：`CurrentChoicePlayerId` 是**沿异步链残留**的 AsyncLocal（守卫自己的注释就写了），
  而 `CardReward.OnSelect` **不经过任何 `CardSelectCmd.From*` 入口** ——
  只有那些入口才会由 `CardSelectForegroundSwitchPatch` 写入该值。
  于是没人写 → 保留旧值（本局是真人 …326）→ 守卫判成"真人选牌" → 抛异常 → 自动领取失败。
  金币/药水奖励**不读 `Selector`**，所以照常领到 —— 完全对应"只有卡牌奖励漏领"。
- **为什么"退出重进就好了"**：新进程/新局重建了 AsyncLocal 上下文，残留值消失（实机同文件里后一场奖励 5/5 全成功）。
- **修法（r134）**：`LocalWakuuRewardAutoClaim.TrySettleAsync` 的 `CardReward` 分支在
  `reward.SelectUnsynchronized()` 之前**写入选牌归属者**（保存/恢复旧值），
  与 `LocalWakuuEventAutoChoice`（`CurrentChoicePlayerId` + `AutoChoiceOwnerId`）和
  `LocalWakuuRelicEffectAutoChoice.Enter` 同一套既有做法（r94 就是为同一个坑加的）。
  另加一条**只在命中该坑时**才打的 INFO：
  `卡牌奖励自动领取：选牌归属者在异步链上残留为其他角色，已改写为奖励归属者: stale=…, owner=…`。
- **不是 r132/r133 引入的**：跨会话比对（本机 5 份日志）—— 09-13 的两份日志各有 10 / 14 条
  `检测到真人选牌请求` 且 `读档重放路径` 也出现过（4 / 2 次），但**从没触发过奖励领取失败**
  （`自动领取失败` 计数 0）；本次是**同一条残留 AsyncLocal 撞上"该链没有任何入口写它"**才暴露。
  r132/r133 改的是出牌演出，与该 AsyncLocal 无关。
- **门禁**：0 警告 0 错误、**499 单测全绿**（无新增：修复是 AsyncLocal 作用域写入，靠实机回归）、
  `clr_compat_check` PASS、marker **2026-09-14-r134**、`dll_check --deployed` 全绿
  （`--u16 选牌归属者在异步链上残留为其他角色` 在、`__runOriginal` 不在），
  部署位与仓库根字节一致（sha256 `398168504912...`）。
- **验证方法**：两个实验档开关随意（本 bug 与之无关），打一场多瓦库战斗/读档重放进入战后奖励 →
  ① 期望 `瓦库卡牌奖励已自动领取` 条数 = 瓦库数量（不再有 `保留为人工领取: reward=CardReward`）；
  ② 若命中过那个坑，会顺带出现 `…已改写为奖励归属者: stale=…, owner=…`（正是本次修复生效的判据）；
  ③ 真人自己的卡牌奖励**不受影响**（仍然弹屏给真人点）。
- **同类风险点（已记录，本轮未改）**：同样"压了托管选择器但没写 `CurrentChoicePlayerId`"的还有
  `LocalWakuuRestAutoChoice.PushSelectorFor`（火堆）与 `LocalWakuuPotionAutoUse.PushSelectorFor`（战斗内药水）。
  两者的选牌入口分别是 `FromDeckFor*` / `FromCombatPile`：前者**不在** `CardSelectForegroundSwitchPatch`
  的 6 个 From* 前缀清单里（所以理论上同样会吃到残留值），后者在。
  ⚠ 火堆那条还牵涉既有「火堆卡退」未定位问题，**等实机证据再动**，不要顺手改。
- ✅ **2026-09-14 r134 实机确认通过（战斗奖励专项）**：marker `2026-09-14-r134`、`INIT_OK`、
  本会话 2 场战斗 × 5 瓦库 ⇒ `瓦库卡牌奖励已自动领取` **10 条**、
  `保留为人工领取` **0 条**（也就是 `reward=CardReward` 的失败一条都没有）；
  `检测到真人选牌请求` **0 条**；`选牌归属者在异步链上残留为其他角色` **0 条**
  （说明本局没再出现残留值，修复属防御性生效）。
  同样确认无回归：`收回滞留在出牌区的卡牌节点` 39 条（dest 全为 `Discard`，画面正常）、
  `瓦库选择器作用域异常退出` / `瓦库看门狗重启失败` / `Couldn't get hand node` /
  `动作队列UI入队触发空引用` 全 **0**。**BUG-13 关单。**
- ✅ **火堆同类风险实测无问题（2026-09-14，marker r134）**：5 个瓦库
  `瓦库火堆已自动选择 … success=True` 全部成功（MEND/HEAL/SMITH 都覆盖到），
  说明 `LocalWakuuRestAutoChoice` 不写 `CurrentChoicePlayerId` 在当前时序下没有踩到残留值。
  **维持不改**（等真出现"火堆选项选错/不选"的证据再说）。

### BUG-14 火堆最多只显示 4 个玩家的角色气泡（5 号及以后不显示）—— 已知、**按用户意愿先不修**

- **现象（用户 2026-09-14）**：「老 bug：火堆最多显示 4 个玩家，多的不显示（除了不知道 5~12 号瓦库
  玩家选什么选项以外无影响，可以先不修）」。
- **根因（代码层，已核对反编译源码）**：原版休息区场景**硬编码了 4 个角色容器** ——
  `NRestSiteRoom._Ready`（`sts2src/src/Core/Nodes/Rooms/NRestSiteRoom.cs:124-151`）只
  `GetNode("BgContainer/Character_1..4")` 四次，然后对**全部玩家**循环
  `_characterContainers[i].AddChildSafely(...)` ⇒ 第 5 个玩家起**必然 `ArgumentOutOfRangeException`**。
  模组的 `NRestSiteRoomReadyGuardPatch`（`Scripts/Patch/RoomFocusGuardPatch.cs`，Finalizer）把它接住
  （日志 `休息区初始化出现越界，已拦截并继续流程: …`）+ 延迟恢复（`rest-site-finalizer-recover-N`，
  日志 `休息区选项可见性已恢复: … options=4`），所以**流程不受影响**；
  但 5 号及以后的玩家**根本没有气泡节点** ⇒ 看不到他们选的选项，也看不到他们的角色立绘。
- **数据层无影响（实机核对）**：5 个瓦库的选项**都真的生效**（`瓦库火堆已自动选择: player=…, option=…,
  success=True`，见 2026-09-14 r134 会话）；没显示的只是"谁选了什么"这一层表现。
- **附带一条 WARN**：`驱动瓦库火堆气泡失败: option=MEND, error=Object reference not set to an instance of an object.`
  （`LocalWakuuRestAutoChoice.ShowCharacterBubble` 里驱动 MEND 的确认图标时空引用）——同样只是表现层，
  已被自身 try-catch 兜住，选择本身 `success=True`。
- **修法备选（未做，留档）**：① 运行时给休息区**补建** `Character_5..N` 容器并加进
  `_characterContainers`（要克隆场景节点/摆位，风险在布局）；② 只补"选项气泡"层（不做立绘），
  把 5+ 玩家的选择渲染到固定位置的一排文字/图标上；③ 干脆接受（当前选择）。用户拍板：**先不修**。
- **历史**：该问题自 v1.05 前就存在（`docs/archive/player-update-history.zh.md` 里
  「休息区5+越界改为Finalizer恢复链路」「[待修复] 休息区5人以上首帧可能不显示选项」等条目），
  属于**游戏侧 4 人上限**与模组"本地 12 人"叠加的固有限制。

### 改进-1 每回合开始必须逐个看完所有真人玩家的抽牌演出（2026-09-10 r105 已实现，✅ 已实机确认）

> 🔧 **r105 实现**：设置页「其 它 设 置」新增开关 **「跳过他人回合开始抽牌演出」**（配置键
> `skipTurnStartDrawAnim`，**默认关**）。开启后回合开始只保留「当前正在看的那位」的抽牌演出，
> 其他人不切前台 → 其抽牌瞬时生效（依据原版 `CardPileCmd.GetTweenForCardsChangingPiles`：
> 非本地玩家的 Draw→Hand 本就不建节点、不做补间），数据照常，切过去即见完整手牌。
> 判定抽为纯函数 `TurnStartDrawAnimPolicy.ShouldSkipSwitch`（+5 单测）；日志
> `已跳过回合开始抽牌演出（非前台玩家，改进-1）: player=…, foreground=…, round=…`。
> 只在回合开始路径生效（回合结束/弃牌不受影响）。门禁：0 警告 0 错误、357 单测全绿、marker r105。
> **验证**：开开关进战斗 → 回合开始只应看到一位的抽牌动画，其余人的抽牌不播（日志有跳过行）；
> 切到其他角色时手牌应完整；关掉开关回到原观感。

- **现象**：回合开始会依次把前台切到每个真人玩家、播完其自动抽牌动画再切下一个；2 人还好，
  **满员 12 人时要等很久**。
- **方向（未做）**：提供「跳过 / 加速他人回合开始抽牌演出」开关——后台玩家不切前台、不建抽牌节点，
  或统一缩短动画时长。注意别误伤数据层（演出与数据分离，见 §11.4 的 `CardPileCmd.Add` 相关补丁）。

### 改进-2 多瓦库串行打牌 + 视角跟着切（新增）

> ⚠ **配置键拼写（2026-09-18 r140 起）**：本节各轮记录里出现的 `wakuuPlayQueue` / `wakuuPlayOverlap` /
> `wakuuViewMode` / `wakuuBrain` / `fastWakuuPlay` / `keepWakuuFormRelic` 是**当时的旧键名**；
> 现键名已统一为 `vakuu*`（旧键仍兼容、加载时自动迁移，见顶部指针与 § 改进-5 的 r140 条）。
> **C# 属性/类名**（`WakuuPlayQueue` / `LocalWakuuAutopilotConfig` 等）**刻意不改**（纯机械重构零收益）。

> 📄 **2026-09-10 方案已产出（用户拍板：只做调研+方案，未动代码）**：
> `maintenance-docs/decision-records/多瓦库并行托管可行性与方案.md`（无 git，不进 github）。
> 结论要点：① 「后台托管免切前台」已落地（本节「现象」描述部分过时，`backgroundMode` 默认开、5 处切前台点有守卫）；
> ② 真正的串行源 = 全局 `SelectorScopeGate` + autoplay 被游戏回合循环 `await`；
> ③ `CardCmd.AutoPlay` **就地执行、不走全局动作泵**，故并行瓶颈是全局静态上下文而非动作队列；
> ④ 分期建议：Phase 0 视角策略（默认**不跟随**，低风险）→ Phase 1 选择器按归属分发 → Phase 2 autoplay 与回合循环解耦（准并行）。
> ⑤ **方案 §十一（用户追问后追加）**：原版 `ActionQueueSet` 设计注释即「某玩家等自己选择时，其他玩家可自由出牌」——
> 「多人不互相卡」由「每人一条队列 + 全局 action ID 排序 + 等选择的队列被跳过」实现，**单进程内即成立**；
> 瓦库当前串行是自研 `SelectorScopeGate` + inline `AutoPlay` 的产物。推荐 **Phase 2'（方案 D）**：
> 瓦库出牌改走 `PlayCardAction` 入自有队列，顺带**天然消除全局上下文并发风险**（全局单泵串行 ⇒ 无需 AsyncLocal 化）。
> ⑥ 用户拍板：`never` 档**保留兜底切换**（防软锁）、接受 Phase 0 独立交付、**慢慢来不着急**。
> ⑦ **Phase 0 已实现（r107，2026-09-10，已部署 ✅ 2026-09-11 实机确认）**：新增「瓦库托管视角」三档
> （`wakuuViewMode`，设置页「瓦库托管」区循环按钮，**默认不跟随**）+ 纯函数 `WakuuViewPolicy.ShouldSuppressSwitch`；
> 5 处切前台点（回合开始/结束、Hook 入队、出牌前、选牌兜底）改走它；**两处防软锁兜底不受档位影响**
> （作用域外真人交互选牌恒切、安全网救援恒切）。安全网候选甄别改用与档位无关的 `IsBackgroundHostedWakuu`。
> +12 单测 → **369 全绿**，构建 0 警告 0 错误、`clr_compat_check` PASS、marker r107 已部署且 `dll_check` 字节一致。
> ⑧ **r108（实机反馈修复，2026-09-10，已部署 ✅ 2026-09-11 实机确认）**：① 用户实测「不跟随」下**仍要真人替瓦库选**
> 战斗开始遗物给的无色牌三选一 —— 根因 `CardSelectWakuuTurnStartAutoAnswerPatch` 只覆盖
> `FromChooseACardScreen`，漏了 `FromSimpleGrid`；已补同款前缀（顺带用同一判据跳过无意义切前台）。
> ② 「仅关键节点」档位**实际无效**（被同一钩子里的「改进-1」开关再拦一次）→ 改为 **peek**
> （回合开始跳过去看一眼、约 1.2s 后自动切回真人），并让改进-1 不再管辖瓦库形态角色。
> ⑬ **Phase 1 已实现（r113，2026-09-11，已部署；✅ 2026-09-11 实机核对通过）**：**选择器按归属者分发**（方案 §4）。
> 新增纯逻辑 `WakuuOwnerSelectorMap<TSelector>`（归属者→选择器登记表，支持同归属者嵌套、乱序释放、
> 幂等释放）+ `WakuuSelectorDispatch.Decide(hasChooser, registryHit, chooserIsWakuu)`（路由真值表）；
> 运行层 `WakuuSelectorRegistry.Open(ownerId, selector)` = `CardSelectCmd.PushSelector` + 登记，
> **6 处选择器压栈点全部迁移**（战斗出牌 / 遗物效果 / 事件选项 / 火堆 / 药水 / 卡牌奖励）；
> `CardSelectCmdSelectorGuardPatch` 改走路由（瓦库命中→用它自己的选择器；真人→摘掉走 UI；未知→保持栈顶）；
> 新增 `CardSelectCmdResetRegistryPatch`（run cleanup 同步清表）；新增启动自检 `SELECTOR_ROUTE`
> （反射枚举 `CardSelectCmd.From*` 全部入口并分类，出现未分类新入口即 WARN，便于游戏更新适配）。
> **行为零变化**：三条老路（真人摘掉 / 瓦库放过 / 保持栈顶）逐条保留，只有「瓦库 + 登记表命中」这一种
> 新组合走精确分发。+21 单测 → **420 全绿**（含「入口枚举_无未分类的新入口」这条游戏更新哨兵），
> 构建 0 警告 0 错误、`clr_compat_check` PASS、marker r113 已部署且 `dll_check` 字节一致。
> ⏭ Phase 1 的收益在于**铺好路由底座**（Phase 2' 准并行后两个瓦库作用域才能各归各），本轮单独交付无可见变化。
> ⑨ **r109（新 bug 修复，2026-09-10，已部署 ✅ 2026-09-11 实机确认）**：瓦库打「手牌变换」类牌（YUI「数据链」、
> 酒狐「不等价交换」）**中断**——`Couldn't get hand node for original card CARD.INJURY`，
> 牌停在屏幕中间、效果没跑完、没消耗（本局 18 次）。根因：原版 `CardCmd.Transform` 视觉分支按
> `LocalContext.IsMine` 决定是否到**前台手牌**找原卡节点，而瓦库自动出牌期间看门狗**已把 NetId 钉在瓦库身上**
> → 判成"我的牌" → 找不到 → 抛穿异步链；r59 的守卫只覆盖「要不要钉」，漏了「NetId 已经是牌主人」。
> 修法：纯函数 `CardTransformNetIdPolicy` + 新增 `ShiftAwayFromOwner`（后台变换时把 NetId 让到当前前台）。
> +6 单测 → **378 全绿**，marker r109。
> ⑩ **r110（历史小尾巴清理，2026-09-10，已部署 ✅ 2026-09-11 实机确认）**：① **清理期异常降级** —— 退出这一局时
> 自动出牌作用域仍在飞而抛出的 `Nullable object must have a value.` 不再打 WARN、不再上抛
> （纯函数 `WakuuTeardownPolicy.ShouldTreatAsExpectedAbort`，两条日志降级为 INFO）；
> ② **磁盘历史脏值自愈** —— 加载配置时把非法的字符串策略字段（已见 `wakuuBrain=bottomRight`）
> 归一后写回盘（`LocalWakuuAutopilotConfig.TryRepairHistoricalValues`），只改一次。
> +10 单测 → **388 全绿**，marker r110。
> ⑪ **2026-09-11 实机复核（marker r110 日志实证，✅ 全部闭环）**：
> ① r110 脏值自愈 WARN **仅 1 次**，随后两个配置快照均为 `wakuuBrain=heuristic`（原 `bottomRight` 已消失），
> 第二次加载不再告警 → 「只写一次」成立；② `Nullable` 全文仅剩 **2 处且均为 BaseLib 第三方**
> （`SavedProperty…System.Nullable\`1[System.Int32]`），我们那条清理期异常**已彻底消失**；
> ③ r108 作用域外自动作答命中 **3 次**（`FromChooseACardScreen`×1 + **`FromSimpleGrid`×2**）
> → 「不再要真人替瓦库选」成立；④ r109 手牌变换 **3 次** `[手牌同步修复] … NetId …327 -> …326`，
> 且**无** `Couldn't get hand node`；⑤ 视角档 `wakuuViewMode=never` 生效，多处
> `瓦库形态后台模式，跳过自动切换视角`；⑥ `BUILD_IDENTITY commit=5c295c3 state=clean`
> → 反证部署位二进制就是那份干净提交。
> ⑫ **2026-09-11 追加：「仅关键节点 peek」✅ 也已实机确认**（本轮最后一个未验证项出清）：
> 用户切到该档后，round 1 / round 2 各出现一次完整闭环 ——
> `检测到后台角色触发战斗效果/选牌，自动切换前台: 326 -> 327, source=turn-start-setup` →
> `控制上下文已更新: 326 -> 327, source=auto-foreground-turn-start-setup` → 约 1.2 秒后
> `仅关键节点：瓦库回合开始已看过，自动切回原视角: 327 -> 326, source=turn-start-setup` →
> `控制上下文已更新: 327 -> 326, source=wakuu-peek-return-turn-start-setup`。
> 即「跳过去看一眼再自动切回」与观察一致，符合设计预期（用户原话：跳过去看一眼然后自动切回）。
> **附带确认**：该会话**没有**再次出现脏值自愈 WARN（`wakuuBrain=heuristic` 保持）→
> 证明 r110 的「每刀只改一次」在**跨会话**同样成立。
> ⑭ **方案 D 可开关实验档已落地（r121，2026-09-12，已部署；✅ 后续 ⑮~⑱ 实机确认）**：新增开关
> **「【实验】瓦库出牌走动作队列」（配置键 `wakuuPlayQueue`，默认关）** —— 开启后瓦库出牌不再用
> inline 的 `CardCmd.AutoPlay`，而是 `new PlayCardAction(card, target)` 经
> `ActionQueueSynchronizer.RequestEnqueue` 入**该瓦库自己的**动作队列（原版 `CardModel.EnqueueManualPlay`
> 就是这一行），并**逐张 `await action.CompletionTask`**（`GameAction` 公开的"等这个动作彻底跑完"）——
> 每张牌执行完（含扣费）才做下一次决策，能量读数准确，也不会堆出一串注定被 Cancel 的动作。
> 三条语义迁移（方案 §12.2）逐条处理：
> ① `isAutoPlay` true→false —— 实验档要观察的核心差异（`VoidFormPower` / `PaelsEye` / `UnceasingTop` 等）；
> ② 目标按**原版真人出牌口径**归一：**只有 `AnyEnemy` / `AnyAlly` 传大脑解析出的目标，其余一律 null**
>   —— `PlayCardAction` 用 `CardModel.IsValidTarget` 校验，而它对"非 Any 的牌 + 非空目标"返回 false ⇒
>   沿用 AutoPlay 口径（`AnyPlayer` 会被解析成自己）会让动作被 `Cancel()` 打不出牌；
>   目标解析不到时**不构造动作**，牌留在手牌；
> ③ **删掉外层 `await SpendResources()`**（动作自己会扣，否则双重扣能量）。
> 判定收敛为纯函数 `WakuuPlayQueuePolicy`（`DecidePath` / `NeedsExternalSpendResources` /
> `ShouldUseResolvedTarget`，+9 单测）。设置页「瓦库托管」区新增该项，文案写明三条语义变化。
> **阶梯式落地**：本步只换"出牌路径"，`SelectorScopeGate` 与选择器作用域**原样保留**（回滚面最小）；
> "去掉全局闸门、让多瓦库真正重叠"留作下一步，等本步实机数据（方案 §12.7「尚未做」）。
> 门禁：0 警告 0 错误、**458 单测全绿**（+10）、`clr_compat_check` PASS、marker **2026-09-12-r121**、
> `dll_check` 字节一致（`WakuuPlayQueuePolicy` 在、`__runOriginal`/`WakuuPersonalRollback` 不在）。
> **验证方法**：开开关进战斗 → 应出现 `瓦库出牌走动作队列完成（方案 D 实验档）: …, actionId=…, ms=…`
> 与游戏自带的 `Player <netId> playing card <卡>`；关掉开关应回到既有路径（**不再出现** `走动作队列` 行）。
> **观察点**：① 虚无形态 / 佩尔之眼 / 不歇之巅等牌对瓦库出牌的统计与触发是否变化；
> ② `AnyEnemy`/`AnyAlly` 牌是否照常打得出、解析不到时是否留在手牌；③ 能量有没有被扣两次（不应）；
> ④ `AnyPlayer` 类牌（按原版口径传 null）表现是否与旧路径一致；⑤ 与「瓦库出牌加速」叠加时的观感。
> **同一轮的顺手小修**：`WakuuPersonalDedupe.RemoveCardRemoval` 的去重键补上 `act`
> （原先四张表里唯一少一层 `act` 的，会把跨幕的两次合法同名删牌合并成一行；+1 单测「跨幕同名删牌不合并」）。
> ⑮ **r122（2026-09-13，用户实机反馈后收口）**：
> ① **修 BUG-10**（虚空形态强行结束回合后瓦库仍继续出牌，见该条目）—— 判据改用**逐玩家**的
> `IsPlayerReadyToEndTurn`，加在**出牌循环**与**看门狗调度**两处；
> ② **用户实测（r121）通过项**：**开实验档、不开加速**＝正常（**不会**错误触发佩尔之眼、
> **不会**无视限制打出华丽收场 —— 说明队列路径的 `CanPlay`/目标校验是有效的）；**都不开**＝正常；
> ③ **两个都开会"加速失效" —— 属设计使然**：队列路径走 `PlayCardAction`（`isAutoPlay: false`），
> 它没有 `skipCardPileVisuals` 参数，补间与两段固定等待都走「真人出牌」分支，因此**会盖过**
> 「瓦库出牌加速」，单张回到约 1 秒。设置页文案原先误写成"与加速同向"，r122 已改正为
> 「本项会盖过加速、二者取一」，并写明想两者兼得需再给 `CardModel.OnPlayWrapper` 打前缀补丁
> （且**只能**省两段固定等待，"牌从手牌飞出"仍走 `AddDuringManualCardPlay` 真人分支，收益有限）。
> 门禁：0 警告 0 错误、**458 单测全绿**、`clr_compat_check` PASS、marker **2026-09-13-r122**、
> `dll_check` 字节一致。
> ⑯ **r126（2026-09-13，方案 D 第二步：队列路径下去掉全局闸门）**：新增第二层实验开关
> **「【实验】瓦库并发出牌（不互相等）」（配置键 `wakuuPlayOverlap`，默认关，仅 `wakuuPlayQueue` 开时生效）**。
> 三个调用点共用同一真值 `WakuuPlayQueuePolicy.IsOverlappingQueuePlay(path, overlap)`：
> ① 出牌循环不再 `await SelectorScopeGate.WaitAsync()`（**这才是"一个打完才轮到下一个"的来源**）；
> ② `TryScheduleWatchdog` 不再因 `_selectorScopeInFlight > 0` 返回 `selector-scope-busy`；
> ③ `RunWatchdogAsync` 不再把 `LocalContext.NetId`/sender 钉在瓦库身上（出牌已交给游戏全局单泵
> `ActionExecutor.ExecuteActions`，归属走 r113 的 `WakuuSelectorRegistry` 按归属者分发；全局单值被两个
> 看门狗互相覆盖无意义，且会让原版把别人的出牌当"我的牌"走前台视觉 —— r109 那类异常的来源）。
> **顺带堵掉一个真坑**：原版 `StackedSelectorScope.Dispose` 只在"自己仍是栈顶"时弹栈 ⇒ 并发档下
> 先压入先释放的那个选择器会**永久残留在全局栈**（"栈上无选择器"的判定全部失效）。
> 新增纯逻辑 `WakuuSelectorStackSurgery.RemoveByReference`（按引用、只摘一份、未命中原样返回，+7 单测）
> + 运行层 `RemoveSelectorFromStackIfPresent`（反射读 `_selectorStack`，命中则 Clear + 逆序 Push 复原），
> 无条件挂在 `WakuuSelectorRegistry.OpenScope.Dispose`（正常路径未命中 = 空操作）。
> **明确不做**：`Selector` getter 守卫与登记表不动（Phase 1 底座已够用）；`NPlayerHandSelectCardsSerializationPatch`
> 保留（§12.3 判定不能退休）；inline 路径**永不**并发（就地执行会同步触发选牌链，没闸门保护会真抢答）。
> **代价（已写进设置页文案）**：瓦库出牌的前台视觉演出更少（更接近后台托管）；视角档位「全程跟随」下
> 多瓦库会来回抢视角（建议配默认「不跟随」）。
> 门禁：0 警告 0 错误、**478 单测全绿**（+11）、`clr_compat_check` PASS、marker **2026-09-13-r126**、
> `dll_check` 字节一致（`WakuuSelectorStackSurgery`/`IsOverlappingQueuePlay`/`wakuuPlayOverlap`/
> `RemoveSelectorFromStackIfPresent` 在，`__runOriginal` 不在）。详见方案 **§12.11**。
> **验证方法**：两个以上瓦库打一回合 → 开局应出现 `wakuuPlayOverlap=True` 与
> `瓦库并发出牌：跳过全局选择器闸门（方案 D 第二步）`/`本次不钉全局上下文`；各瓦库的
> `瓦库出牌耗时` 时间窗应**互相重叠**（不再是一个的 `delayMs` ≈ 前面所有瓦库耗时之和）；
> 某个瓦库等选牌时其他瓦库/真人应能继续出牌；**不应**出现
> `并发出牌作用域释放时选择器仍在全局栈中`（出现 = 安全网在救场，请发日志）、
> `Couldn't get hand node`、`瓦库选择器作用域异常退出`、选牌被抢答。
> 关掉本项 ⇒ 与 r121~r125 完全一致（`瓦库选择器闸门等待/已进入/已释放` 照旧）。
> ⑰ **r127（2026-09-13，r126 实机反馈的收口：「真人被瓦库插队」）**：用户实测 r126 后反馈
> 「瓦库之间不再必须打完一整轮才轮到下一个（✅ 并发出牌生效），但**我打了牌要等前面出牌的瓦库的牌
> 全部生效完**才轮到我的牌」。
> **日志定性（5 瓦库局，marker r126）**：并发出牌**完全生效** —— round 1 五个瓦库的
> `瓦库回合开始→出牌启动延迟` = **32/79/81/83/83 ms**（r116 串行时代 494/6297/9724）；
> `瓦库选择器闸门` **0 条**、`选牌选择器按归属分发` 2 条、`检测到真人选牌请求` 0 条、
> `Couldn't get hand node` / `瓦库选择器作用域异常退出` / `瓦库看门狗重启失败` 全 **0 条**。
> **真问题**：`GetReadyAction` 按**全局递增 action ID** 取下一个动作（多人模式原生语义），而并发档下
> 每个瓦库都用"逐张 `await`"占着一个**已入队、在排队**的动作 ⇒ 5 个瓦库 = 队列里常驻 5 张瓦库牌，
> 真人点出的牌必然排在它们全部之后（实测：行 8784 真人入队 → **行 9019** 才生效，其间 2 张瓦库牌；
> 另一张等了 3 张）。每张瓦库牌管线耗时 ~3.2~4.0s（5 瓦库平分单泵）。
> **修法（真人插队）**：`CardModel.EnqueueManualPlay` 前缀（真人**按下**出牌那一刻）调
> `LocalWakuuRelicRuntime.YieldPendingQueuePlaysToHuman()` → 把瓦库**还在排队、尚未开始执行**
> （`GameActionState.WaitingForExecution`）的入队动作 `GameAction.Cancel()` 撤掉，真人的牌随即成为
> 队列里 ID 最小的那个。判定抽为纯函数 `WakuuPlayQueuePolicy.ShouldCancelPendingPlayForHumanPlay`
> （+1 单测）；登记表 `_pendingQueuePlays` 在动作的 `finally` 里自我摘除，`ResetTakeoverFallbackState` 整体清空。
> **安全性**：被撤的动作**从未执行过** ⇒ 不扣能量、不结算、牌仍在手牌，只是回看门狗下一轮重新决策
> （`瓦库出牌入队后被取消（牌留在手牌，交看门狗下一轮）` 会变多，属预期）；`Cancel()` 走原版自己的取消通道，
> 并发档 `LocalContext.IsMe=false` ⇒ **不会动真人的手牌**。正在执行/正在等选择的动作一律不撤。
> **顺带修正一条判据**：`并发出牌作用域释放时选择器仍在全局栈中`（r126 一局 21 条）复核确认是**预期路径**
> （两个作用域交错、先释放的已不在栈顶），**r127 已从 WARN 降级为 INFO** —— 不是异常，不必再报。
> 门禁：0 警告 0 错误、**479 单测全绿**（+1）、`clr_compat_check` PASS、marker **2026-09-13-r127**、
> `dll_check` 字节一致（`ShouldCancelPendingPlayForHumanPlay`/`YieldPendingQueuePlaysToHuman`/
> `_pendingQueuePlays` 在、`__runOriginal` 不在）。详见方案 **§12.12**。
> **验证方法**：并发档下打一场 2+ 瓦库的战斗，真人点牌 → 期望日志
> `瓦库并发出牌：真人操作优先，撤掉尚未执行的瓦库入队动作让真人插队: actor=…, count=N, 详情=[…]`，
> 且**紧接着一两张之内**就出现 `Player …326 playing card <真人那张牌>`；瓦库的牌不应丢失。
> ⑱ **r128（2026-09-13，r127 实机反馈的收口：结束回合也得插队 + "能不能一次跑多个动作"的澄清）**：
> 用户实测 r127 后反馈「**这个大概没什么问题了**」（出牌插队 ✅），但
> 「**点结束回合按钮也必须等瓦库结束才看起来生效**」。
> **日志实证（同一局）**：行 8184 真人入队 `EndPlayerTurnAction`（id 24）→ 行 8197~8406 一直是 ready action
> 但被 id 更小的瓦库动作挡着（行 8409~8411 三张 `PlayCardAction` id 25/26/27 排在后面）→
> **行 8413 才 `Executing action`**。与出牌**完全同源**：`NEndTurnButton.CallReleaseLogic`
> （`sts2src/src/Core/Nodes/Combat/NEndTurnButton.cs:329-349`）同样是 `RequestEnqueue(new EndPlayerTurnAction(...))`。
> **修法**：`NEndTurnButtonPatch.Prefix`（r104 前台校正之后）调同一条
> `LocalWakuuRelicRuntime.YieldPendingQueuePlaysToHuman(me.NetId, "end-turn-button")`；
> 同时把 r127 里那条「瓦库形态直接跳过」的防御**移到出牌调用点**（瓦库走 `RequestEnqueue`，不经
> `EnqueueManualPlay`）—— 结束回合这条**必须**对瓦库形态也生效（真人把视角停在瓦库上点结束，
> 点击目标就是那个瓦库，正是要让它立刻停下）。日志统一为
> `瓦库并发出牌：真人操作优先，撤掉尚未执行的瓦库入队动作让真人插队: actor=…, count=N, 详情=[…], source=…`。
> **答用户问「不能做成原版多人游戏那样一次跑多个动作吗？」—— 不能，原版本身也不是**：
> `ActionExecutor.ExecuteActions()`（`ActionExecutor.cs:123-197`）是**全局单泵**（`await readyAction.Execute()`
> 等它彻底跑完再取下一个）；`ActionQueueSet.GetReadyAction()`（`:172-251`）跨**所有玩家**取 action ID 最小的那个，
> 仅对"正在等玩家选择"的队列**跳过**；设计注释（`:13-19`）说的就是「某人等自己选择时**其他人照样出牌**」，
> 而非"多个动作同时执行"。⇒ 原版多人**执行仍是一次一个**，"不互相卡"只体现在等选择时队列被跳过。
> 真并行要同时踩 `LocalContext`/`CurrentlyRunningAction`/`CheckWinCondition` 时机/动画节点/确定性校验和，
> 等于重写动作执行器，**不做**。现实里能做的是 ① 真人永远不排在瓦库后面（r127+r128 ✅）
> ② 缩短单个动作耗时（队列路径每张约 1s；`CardModel.OnPlayWrapper` 前缀强制 `skipCardPileVisuals`
> 可省 0.4~0.65s/张 ⇒ 预计 ~0.35~0.5s/张，**待用户拍板**）。
> 门禁：0 警告 0 错误、**479 单测全绿**、`clr_compat_check` PASS、marker **2026-09-13-r128**、
> `dll_check` 字节一致。详见方案 **§12.13**。
> **验证方法**：并发档下点结束回合 → 期望日志里同一次点击出现
> `…让真人插队: actor=…, source=end-turn-button`，且**紧接着**就 `Executing action: EndPlayerTurnAction`；
> 结束回合/撤销、瓦库照常自动收口都要正常。
> ⑲ **r132（2026-09-14，方案 D 第三步：队列路径出牌加速）**：用户拍板做「缩短单个动作耗时」——
> **r122 ③ 那条"两个开关都开会加速失效"的取舍已被本步推翻**。
> **根因（代码层）**：r117 的加速靠给 `CardCmd.AutoPlay` 传 `skipCardPileVisuals: true`，
> **只有 inline 路径有那个形参**；队列路径由 `PlayCardAction.ExecuteAction`
> （`sts2src/…/GameActions/PlayCardAction.cs:103`）自己以 `isAutoPlay: false` 调
> `CardModel.OnPlayWrapper`，**没有**这个参数可传 ⇒ 队列出牌必然走完整演出。
> **修法**：新增补丁 **`CardPlayVisualsSkipPatch`**（前缀挂在 `CardModel.OnPlayWrapper`，
> 把形参 `skipCardPileVisuals` 改成 `true`），判定走新纯函数
> `WakuuPlaySpeedPolicy.ShouldSkipCardPileVisualsForQueuedPlay(开关, 本地多控, 瓦库形态, 是否我们入队的牌)`
> —— 第 4 条来自 `LocalWakuuRelicRuntime.HasPendingQueuePlay(owner.NetId)`
> （登记表 `_pendingQueuePlays` 在 `TryPlayCardViaActionQueueAsync` 里"入队前写、动作跑完摘"，
> 而 `OnPlayWrapper` 恰好在这个窗口内被调用）⇒ **真人手动替瓦库出的牌不加速**，观感不倒退。
> 该形参只影响演出、不参与任何数据语义（游戏官方给自动出牌场景留的开关）。
> **实际收益比 r117 小（已按源码核实修正原估）**：`isAutoPlay: false` 分支本来就不走
> `CustomScaledWait(0.25f, 0.35f)` 与前段牌堆补间，能跳过的只有**收尾固定等待**
> `CustomScaledWait(0.15f - num, 0.3f - num)` 与**结算堆**（弃牌堆 / 消耗 / 移出战斗）的补间
> ⇒ 约 **0.15~0.3s/张**（原估 0.4~0.65s/张；"牌从手牌飞出"的 `AddDuringManualCardPlay` 那段省不掉）。
> **安全**：前缀整体包 try-catch（`Owner` getter 会走 `AbstractModel.AssertMutable()`，
> 补丁**绝不允许**自己抛异常打断出牌）；返回 void、**不用** `__runOriginal`（本项目坑 2）。
> 门禁：0 警告 0 错误（仅 NU1900 离线还原告警）、**499 单测全绿**（487 → +12：
> `WakuuPlaySpeedPolicyTests` 队列口径 +5、新 `CardPlayVisualsSkipPatchTests` 锚点哨兵 +7）、
> `clr_compat_check` PASS、marker **2026-09-14-r132**、`dll_check --deployed` 全绿
> （`CardPlayVisualsSkipPatch` / `ShouldSkipCardPileVisualsForQueuedPlay` / `HasPendingQueuePlay` /
> `--u16 瓦库出牌加速（队列路径）：强制跳过卡牌堆演出` 在、`__runOriginal` 不在），
> 部署位与仓库根字节一致（sha256 `93ba60c8...`）。
> 设置页文案同步改正：r122 那句「本项会盖过加速、二者取一」已改写，
> `fastWakuuPlay` 文案补一句「两个实验档的出牌同样吃本项」。
> **验证方法**：两个实验档都开（`wakuuPlayQueue` + `wakuuPlayOverlap`）且 `fastWakuuPlay` 开 →
> ① 期望每张队列出牌出现 `瓦库出牌加速（队列路径）：强制跳过卡牌堆演出: owner=…, card=…`；
> ② 与之配对的 `瓦库出牌走动作队列完成 … ms=` 应比 r126/r128 那局（单张执行约 0.7~1.0s）明显下降；
> ③ 关掉 `fastWakuuPlay` ⇒ 该行消失、演出恢复完整；关掉队列档 ⇒ 与 r131 完全一致；
> ④ 回归：瓦库牌不丢不白打、真人出牌/结束回合照旧插队。
> ⑳ **r133（2026-09-14，r132 实机反馈修复：卡面滞留在出牌区）**：用户实测 r132「瓦库出牌确实快了很多，
> 但**有些牌会停在出牌区不消失**」。
> **根因（代码层）**：`skipCardPileVisuals` 在 `CardModel.OnPlayWrapper` 结尾是**一票两用** ——
> 既跳过收尾 `CustomScaledWait`，也跳过**"把卡牌节点从出牌区收走"的换堆补间**
> （`CardPileCmd.Add/Exhaust/RemoveFromCombat(..., skipVisuals: true)` ⇒
> `CardPileCmd.GetTweenForCardsChangingPiles` 整段不跑）。而**正是那条补间**在结束时
> `MoveCardNodeToNewPileBeforeTween` + `QueueFreeSafely`（消耗牌则播 `NCardExhaustVfx`）。
> inline 路径（r117）不会出这个问题，是因为它的节点**从没建过**（`isAutoPlay: true` 分支用
> `CardPileCmd.Add(..., skip)`，全程不建节点）；队列路径走 `isAutoPlay: false` 分支，
> 节点由 `CardPileCmd.AddDuringManualCardPlay` 建（**它没有跳过形参**）⇒ 建了却没人收。
> **修法**：`TryPlayCardViaActionQueueAsync` 在 `await action.CompletionTask`（动作真正跑完）之后
> 调新方法 `LocalWakuuRelicRuntime.ReleaseLeftoverPlayedCardNode(card)`，把滞留在出牌区的节点按
> 「那条被跳过的补间的最终效果」收掉：`Power` 不动（原版自己也排除，走 `PlayPowerCardFlyVfx`）、
> 仍在 `Play`/`Draw` 堆不动、去 `Hand` 则交还手牌容器、其余（`Discard`/`Exhaust`/`Deck`/已移出战斗）
> `QueueFreeSafely`；找不到节点即空操作，所以无条件调用安全。**只在 `fastWakuuPlay` 开时调用。**
> ⚠ 用 Harmony **后缀**做这件事不可行：`OnPlayWrapper` 是 async，后缀在**首个 await 之前**就跑完了
> （那时牌还没进结算堆），所以必须挂在"动作真的跑完"的那个 await 之后。
> 门禁：0 警告 0 错误、**499 单测全绿**（无新增：补偿是纯 UI 路径，无法单测，靠实机回归）、
> `clr_compat_check` PASS、marker **2026-09-14-r133**、
> `dll_check --deployed` 全绿（`ReleaseLeftoverPlayedCardNode` / `--u16 收回滞留在出牌区的卡牌节点` 在、
> `__runOriginal` 不在），部署位与仓库根字节一致（sha256 `fba50973...`）。
> **验证方法**：两个实验档都开 + `fastWakuuPlay` 开 → ① 出牌区**不再滞留**卡面；
> ② 每次收回都会打一条 `瓦库出牌加速（队列路径）：收回滞留在出牌区的卡牌节点: card=…, dest=…`；
> ③ 速度应与 r132 持平（补偿只做一次 `QueueFree`，不含等待）；④ 关掉 `fastWakuuPlay` ⇒ 该行消失。
> **⚠ 附：⑲ 里"实际收益比 r117 小、约 0.15~0.3s/张"的估算偏低**。实机体感"快了很多"说明大头不是
> 收尾 `CustomScaledWait`（它本来就常接近 0），而是被一并跳过的**换堆补间**
> （`CardPileCmd.Add(..., skipVisuals: false)` 结尾 `await tween.AwaitFinished(...)` = "卡牌飞向弃牌堆"那段）。
> 也就是说"加速"与"节点滞留"是**同一段补间**的两面 —— 这正是 r133 必须做补偿的原因。详见方案 §12.14.1。

- **现象**：多个瓦库时只能「一个瓦库打完 → 切到下一个瓦库」串行进行，且真人视角会跟着切到
  瓦库正在操作的角色；瓦库多时同样很慢。
- **方向（未做）**：① 后台托管免切前台（类似单人双角色的后台模式）→ 多瓦库可并行 / 准并行推进；
  ② 视角策略可配置（不跟随 / 仅关键节点跟随）。
- **风险**：与选牌串行化、前台绑定类 UI（结束回合按钮，见 BUG-2）强耦合，
  需先解决「前台归属」的单一事实来源，否则会把 BUG-2 放大。

### 改进-3 瓦库事件选项：接上「我们自己」的选择率 + 胜率统计（2026-09-11 用户反馈；**r114 / r115 ✅ 2026-09-11 实机核对通过**）

> 🔧 **r114 实现（2026-09-11）**：
> ① `WakuuEventSignal` 扩展出 **`ChosenRate`（选择率）** 与 `WinRateSkipped`（没选它的局胜率）、
> 派生 `WinRateGain` / `HasChosenRate`（社区链无选择率 → 保持 `null`，**不用 0 冒充**）；
> ② `WakuuPersonalQuery.TryGetEventDecisionSignal` **回填选择率**（原先整条丢弃），
> 且 `SkippedRuns == 0` 时**不回填** skipped 胜率（避免用 0% 当假基准把选项算成强正增益）；
> ③ 新增纯函数 `WakuuSignalPicking.PickBestEventOptionIndex`（**口径与卡牌 `PickBestCardIndex` 对齐**：
> 主信号 = 选择率，无选择率时退化为胜率；叠加因果增益加权；**负面信号直接出局**；同分保留更靠前）；
> ④ `LocalWakuuEventAutoChoice.SelectByPersonalStats` 改走该纯函数，日志补
> `chosenRate / winRate / gain / offered / tier`；⑤ 设置页「个人统计决策辅助」文案写明
> 「事件选项按你的选择率 + 胜率选，用稳定 loc key 查表、不受界面语言影响」。
> 门禁：0 警告 0 错误、**432 单测全绿**（+12）、`clr_compat_check` PASS、marker **r114** 已部署且 `dll_check` 字节一致。
>
> **前置条件（重要）**：这条链受开关 **「个人统计决策辅助」（`personalAssist`）** 管辖，**默认关** ——
> 想让它生效必须先在设置页打开；且样本门槛是 **≥3**（`WakuuPersonalQuery.DefaultMinPersonalCount`）、
> **只统计已打完（胜/负）的局**，所以早期样本少时会回退到社区统计 → first/last/random。
> **是否把 `personalAssist` 改成默认开，留给用户拍板**（会同时影响卡牌奖励的个人统计链）。
>
> **验证要点**：开开关后进事件 → 日志 `瓦库事件按个人统计选取: event=…, option=…, chosenRate=…, winRate=…, gain=…`；
> 样本不足/全为负面时是 `瓦库事件个人统计未采用，回退社区/原策略: …`（属正常回退，不是 bug）。
>
> 🔧 **r115 诊断补强（2026-09-11，r114 实机日志暴露的盲点）**：r114 第一条实机日志里
> `personalAssist=True`、`eventChoiceMode=random`、事件自动选择 2 次，但**两条统计链一行日志都没有** ——
> 因为两条链在「本页只有 0/1 个可选项」「记录器还没有任何事件样本」「社区适配器未就绪」这些早退路径上
> **全是静默 return**，导致"没生效"与"没数据"在日志里无法区分。r115 把这些路径全部留痕：
> `瓦库事件{个人统计|社区统计}跳过（本页仅 N 个可选，无选择余地）` /
> `瓦库事件个人统计无记录（记录器还没采到任何事件选项）` /
> `瓦库事件个人统计无可用样本…各选项展示次数=[KEY:n, …]`（可直接看出"该事件从未被记录"还是"样本不够"）/
> `瓦库事件社区统计无数据…, skadaReady=…`。marker **r115** 已部署。
> （事件选项选择率/胜率逻辑本身**没有改动**，只补日志。）
>
> ✅ **2026-09-11 实机核对通过（marker r119 会话日志）**：该局「个人统计决策辅助」为**开**，
> 两位瓦库各自动选一次（两局共 6 次）**全部命中个人统计链**，且数值合规：
> `瓦库事件按个人统计选取: event=…INTEGRATED_STRATEGY_EVENTS_EVENT_SECRET_ROOM_EVENT, char=IRONCLAD,
> index=0/2, option=…TAKE_SCULPTURE, chosenRate=1.000, winRate=0.000, gain=无, offered=3, withData=2,
> tier=characterFirst`
> —— ① `chosenRate` 有值 ⇒ `TryGetEventDecisionSignal` 的**选择率回填生效**（原先整条丢弃）；
> ② `offered=3, withData=2` 与 `tier=characterFirst` 正是 r115 补的回退诊断字段 ⇒ **诊断链同样生效**；
> ③ 结论：r114（选择率参与决策）+ r115（"没生效"与"没数据"可区分）**均已在实机跑通**，
> 只剩"玩法观感是否满意"这一主观项（用户未提异议）。
> 注：`winRate=0.000` 是真实切片结果（点过它的局都输了），**不是**占位 0 ——
> 「`SkippedRuns == 0` 不回填假基准」的设计未受影响。

- **现象（用户 2026-09-11）**：瓦库的事件选项现在体感「只能随机 / 按 first-last 乱选」，
  用户问「什么时候能按选择率或胜率选」，并指出**我们自己已经在 mod 里统计了事件选项的选择率与胜率**。
- **现状核查（结论：数据我们早就采到了，是决策链漏用了选择率）**：
  - **我们已经有的数据**：个人记录器 `personal_stats.json` → `eventChoices`（事件每页每选项一行，
    带 `chosen` 标记），足以算出 —— ① **选择率** `PersonalEventSlice.ChosenRate = Chosen / Offered`
    （`Scripts/Runtime/PureLogic/WakuuPersonalData.cs:179-192`）；② **胜率** `PersonalWinSlice.WinRateHeld`
    （同文件 `CountEventWinSlice`）。查询入口 `WakuuPersonalQuery.CountEventOptionSlice`（L508-554）与
    `TryGetEventDecisionSignal`（L471-505）。
  - **但决策链只用了胜率**：`TryGetEventDecisionSignal`（L486-502）算出了 `PersonalEventSlice slice`
    却**只拿 `slice.Offered` 当门槛**，返回的 `WakuuEventSignal(optionKey, win.WinRateHeld, slice.Offered)`
    把 `Chosen` / `ChosenRate` **整条丢掉**；上层 `LocalWakuuEventAutoChoice.SelectByPersonalStats`
    （L292-364）也只比较 `signal.Value.WinRate`。→ **选择率从未参与决策**。
    与卡牌侧明显不对称：卡牌的 `WakuuCardSignal.PickRate` 在 `WakuuSignalPicking.PickBestCardIndex`
    里是**主信号**（选择率 + 因果增益），事件侧没有对应物。
  - **且事件统计这条链默认是关的**：`personalAssist`（设置页「个人统计决策辅助」）**默认关**、
    `skadaAssist`（社区统计）也**默认关** → 默认落到 `SelectByStrategy` 的 first/last/random
    （`WakuuConfigData.eventChoiceMode` 默认 `First`）。这才是「只能乱选」的**直接原因**。
  - **个人数据链的优势（比社区链可靠）**：用**稳定 loc key**（`EventOption.TextKey`）查表，
    **不受界面语言影响**、也不依赖第三方 mod；而社区链是 SkadaHelper **文本模糊匹配**，
    中文界面下大概率整体 miss（r48 已记录、日志锚点 `瓦库事件社区统计匹配: … 命中=0`）。

- **修法（小改动，纯逻辑为主，建议作为 r114 候选）**：
  1. `WakuuEventSignal` 增加 `Chosen` / `ChosenRate`；**社区侧无此字段 → 置"无选择率"**
     （不要用 0 冒充，否则会污染口径）；
  2. `WakuuPersonalQuery.TryGetEventDecisionSignal` 把 `slice.Chosen` 一并回填（不再丢弃）；
  3. 新增纯函数 `WakuuSignalPicking.PickBestEventOptionIndex`（口径与卡牌一致）：
     有选择率时按「选择率优先 + 胜率不差」综合；无选择率时退回胜率；
     胜率明显低于"未点该选项"的局（负面信号）直接出局；同值保留更靠前的选项（与"最上"兜底同向）；
  4. `LocalWakuuEventAutoChoice.SelectByPersonalStats` 改走该纯函数，日志补
     `chosenRate` / `offered` / `winRate` 便于核对；
  5. 单测：`WakuuSignalPicking` 事件口径真值表 + `WakuuPersonalData` 回填断言。
- **做的时候一并定的开放问题**：
  - `personalAssist` 默认关是否合适？至少设置页文案要写明「事件选项想智能选必须打开这一项」；
  - 事件侧的样本量门槛（现 `DefaultMinPersonalCount = 3`）在"同一事件不常重复遇到"的现实下偏严，
    可能需要按事件统计覆盖率后再调整；
  - 社区侧若 SkadaHelper 的事件条目其实带选择率字段（当前只读了 `Text/WinRate/Count`），
    可一并接入 —— **待确认字段名，别猜**。

### 改进-5 商店自动化增量：自动买遗物 / 买药水 / 删牌服务（**r137 / r139 已实现，✅ 2026-09-18 实机确认**）

- **背景**：`shopAssist`（Phase 4，默认关）此前只买卡（r64/r65/r66/r67）。可行性分析 §9.3 里的
  「遗物 / 药水 / 删牌服务」三项一直挂着未做。
- **本轮做了两项（各自独立开关，均默认关、均需总开关）**：
  - **`shopAssistBuyRelics`（商店自动买遗物）**：遗物**没有社区评级**（SkadaHelper 只有卡牌统计，
    `maintenance-docs/game-entities.md` 只是实体清单），所以只按「买得起 + 付完仍保留 ≥ 50 金币」；
    不做稀有度加权（没有可信数据源，凭空加权就是拍脑袋）。参考价：`RelicModel.MerchantCost` =
    Common 175 / Uncommon 225 / Rare 275 / Shop 200（商店价再乘 0.85~1.15 随机）。
    生效条件 = 本地多角色模式启用（`LocalSelfCoopContext.IsEnabled`，外层已判）+ 瓦库形态 + 在商店房 + 本开关。
    ⚠ **r138 订正**：初版这里写过一道"仅在单机冒险模式生效"的门禁
    （`LocalSelfCoopContext.UseSingleAdventureMode`），但该属性是 **`=> true` 的常量** ⇒ 门禁恒不成立、
    纯死代码，还让设置页文案 / CHANGELOG / 日志都描述了一个不存在的条件（实机证据：`遗物跳过_非单机` 恒 0）。
    **已删除该门禁与相关文案**；遗物获得触发的选牌由 `LocalWakuuRelicEffectAutoChoice`（r94，非战斗期）
    照常自动作答，不需要额外门禁。
  - **`shopAssistBuyPotions`（商店自动买药水）**：同口径（价格 + 金币保底），额外要求
    `Player.HasOpenPotionSlots`；每买一瓶后再判一次，栏满即停手并在日志里说明。
  - 决策纯函数 `WakuuMerchantPicking.SelectPricedBuys`（+5 单测 → **507 全绿**）；
    判定写成「价格 > 余额 - 保底」而不是「余额 - 价格 < 保底」，避免 `SafeCost` 兜底的
    `int.MaxValue` 做减法溢出。
- **顺手修的一处既有 bug（买卡下标错位）**：`TryAutoBuyCardsAsync` 原先在跳过 Null 占位卡 /
  未上架条目时只 `continue`、**不往候选列表补位**，于是 `candidates` 的下标与 `entries` 错开 ——
  店里一旦出现 Null 占位卡（2026-09-07 实测出现过），后续 `picks` 里的下标就会买到"错位的那张"、
  读到的价格也是别人的。现改为候选与条目**成对**收集（`plan`），下标恒等；
  新增的遗物 / 药水路径从一开始就用成对收集。
- **删牌服务 + 个人统计：2026-09-18（r139）已做**（用户指示「遗物/药水决策要用统计」「删牌也做掉」）：
  - **新增开关 `shopAssistBuyRemoval`（商店自动删牌，默认关）**：金币保底允许时买一次删牌服务，
    并用 `WakuuPickScenario.Remove` 的选牌优先级挑一张删掉。
    ⚠ **刻意没走原版入口** —— 它有两个多控致命伤：
    ① `OneOffSynchronizer.DoLocalMerchantCardRemoval` 用的是**同步器自己的** `_localPlayerId`
       （不是 `LocalContext`；`AlignContext` 只对齐了 Rewards/Reward 两个同步器）
       ⇒ 会**删真人的牌、扣瓦库的钱**；
    ② 它会 `_gameService.SendMessage(MerchantCardRemovalMessage)` 广播，接收端
       `HandleMerchantCardRemoval` 对「sender == LocalPlayer」直接抛 `InvalidOperationException`，
       而本地多控下"其他玩家"全在同一进程 ⇒ 要么重复执行、要么刷错误日志。
    ⇒ 改为**自实现它的后半段**：`CardSelectCmd.FromDeckForRemoval`（压选择器 + 写
    `CurrentChoicePlayerId`，与 r134 卡牌奖励同一套；`FromDeckGeneric` 的 `Selector != null` 分支
    优先于 `RequireManualConfirmation`，压栈后不会弹屏）→ `PlayerCmd.LoseGold` →
    `CardPileCmd.RemoveFromDeck` → `CardShopRemovalsUsed++` →
    `NRun.Instance.MerchantRoom.Inventory.OnCardRemovalUsed()` → `Hook.AfterItemPurchased` →
    `entry.InvokePurchaseCompleted`。**不广播、只认传入的 player。**
  - **遗物 / 药水决策接入个人统计（否决式）**：新增「商店购买 → 局胜负」切片
    `WakuuPersonalQuery.CountShopWinSlice` / `TryGetShopDecisionSignal`（+2 单测）、
    信号结构 `WakuuShopSignal`（held = 买了该商品的局；基准 = 同切片内**没买它**的已结束局；
    abandon 不进分母）；`personalAssist` 开且买过 ≥ 3 局时**增益为负 → 不买**
    （`WakuuMerchantPicking.IsPersonalStatsVeto`，+3 单测）。
    **只做否决、不做主动挑选** —— 遗物/药水没有选择率（`shopPurchases` 不记"摆出过什么"）、
    样本远少于卡牌，"用统计决定该买什么"是过度解读；无数据 / 开关关 → 回退纯价格规则（行为不变）。
    日志新增 `个人统计样本=N/M, 否决=K, 门槛=3局`，把"没开统计 / 没数据 / 有数据但没否决"区分开。
- **门禁（r139 最终态）**：构建 0 警告 0 错误、**512 单测全绿**（507 → +5：`WakuuMerchantPickingTests`
  +3 统计用例、`WakuuPersonalDataTests` +2 商店切片用例；`WakuuConfigJsonTests` 断言同步）、
  `clr_compat_check` PASS、`preflight.ps1 -Deploy` **4 PASS / 3 SKIP**、部署位 marker
  **`2026-09-18-r139`**、`dll_check --deployed` 全绿（`IsPersonalStatsVeto` / `CountShopWinSlice` /
  `TryGetShopDecisionSignal` / `WakuuShopSignal` / `shopAssistBuyRemoval` / `TryAutoBuyRemovalAsync` 在、
  `__runOriginal` 不在）、部署位与仓库根 **字节一致**（sha256 `4b6454d5e301…`）。后已提交 **`b737374`**（r137+r138 为 `7a7eb5e`）。
- **实机验证方法（请复测，r139）**：设置页「瓦库托管」区把 **「商店自动买卡」+「商店自动买遗物」+
  「商店自动买药水」+「商店自动删牌」** 四个开关都打开 → 进商店切到瓦库视图，期望日志：
  - `瓦库商店自动买遗物成功: player=…, relic=…, rarity=…, gold=…`（不买时是
    `瓦库商店自动买遗物: 无符合条件候选，不买 … 最便宜=…`，并带 `个人统计样本=N/M, 否决=K, 门槛=3局`）；
  - `瓦库商店自动买药水成功: …` / `瓦库商店自动买药水跳过：药水栏已满`；
  - **`瓦库商店自动删牌成功: player=…, card=…, gold=…`**（每次进店最多 1 次；
    选牌前应有 `瓦库自动选牌作答: source=商店删牌服务, scenario=Remove, …`；
    不删时是 `瓦库商店自动删牌: 金币不足或价格异常，不删 …` 或
    `瓦库商店自动删牌跳过：本店删牌服务不可用 …`）；
  - 钱不够时不买（日志里写 `保留≥50金`）；买卡的行照旧（回归）。
  - **个人统计否决**（需开「个人统计决策辅助」且某商品买过 ≥ 3 局）：期望出现
    `个人统计样本=…, 否决=1 …` 且该商品**没有**出现在成功日志里；
    样本不足时 `否决=0` 属正常（个人样本本来就少），不是 bug。
- ✅ **2026-09-18 实机确认（r137，用户「有金币可以正常购买遗物、药水」+ 日志核对）**：
  日志 `logs-archive/godot__20260918-222513__r137.log`（1.0 MB，终态 OK）——
  - `瓦库商店自动采购启动` **2** 次（两家商店）；
  - **`瓦库商店自动买遗物成功` 7 次**（Common 172/170/175、Uncommon 230、Shop 210 …；
    两次分别「2 个 / 402 金」与「3 个 / 555 金」）；
  - **`瓦库商店自动买药水成功` 3 次**（Rare 99 / Common 48 / Uncommon 77）；另一次是
    `瓦库商店自动买药水: 无符合条件候选，不买。… 金币=54, 最便宜=50, 保留≥50金` ⇒ **金币保底正确**；
  - 买卡回归正常（7 张/543 金 + 7 张/529 金）；六个回归项（选择器作用域 / 看门狗 / 手牌节点 /
    队列空引用 / 领取失败 / 保留人工领取）**全 0**；
  - **`个人记录-商店购买` 0** ⇒ 瓦库自动购买**没有被误记成真人决策**（`PurchaseOwnerId` 排除生效）；
  - **`商店-删牌归属玩家` 0** ⇒ 删牌服务确实没被自动点（**r137 当时未做**；r139 已做并实机确认，见下条）。
- ✅ **2026-09-18 实机确认（r139，含自动删牌）**：日志 `logs-archive/godot__20260918-225158__r139.log`
  （1.07 MB，终态 OK）—— 两家商店共 `瓦库商店自动采购启动` **2** 次：
  - 买卡 7+7 张（527 / 516 金）；
  - **`瓦库商店自动买遗物成功` 6 次**（VAJRA/JUZU_BRACELET/CHEMICAL_X + MINIATURE_CANNON/GIANT_TURTLE_SHELL/DINGY_RUG，
    Common/Uncommon/Shop 都有；两次「买了=3，花=542 / 579 金」）；
  - **`瓦库商店自动买药水成功` 3 次**（CURE_ALL/POWER_POTION/ATTACK_POTION，花 178 金）；
    另一店 `瓦库商店自动买药水跳过：药水栏已满（上限=3）` ⇒ **栏满即停正确**；
  - **`瓦库商店自动删牌成功: player=…327, card=STRIKE_IRONCLAD, gold=75` 1 次**；
    删牌前有 `瓦库自动选牌作答: source=商店删牌服务, scenario=Remove`（选牌优先级生效）；
    另一店 `瓦库商店自动删牌: 金币不足或价格异常，不删 … 价格=75, 金币=52, 保留≥50金` ⇒ **金币保底正确**；
  - 记录归属：`个人记录-删牌钩子命中 … 跳过: 瓦库商店自动采购作用域内（归属者=本人）`
    ⇒ 自动删牌**没有被误记成真人决策**；`个人记录-商店购买` **0**、`商店-删牌归属玩家` **0**；
  - 六个回归项（选择器作用域异常/看门狗/手牌节点/队列空引用/领取失败/保留人工领取）**全 0**。
  - ⚠ **未覆盖项**：**个人统计否决路径**本局没被走到（无 `个人统计样本=…` 行 —— 该行只在
    "无符合条件候选，不买"分支打印，本局两家店都买到了东西）⇒ 属**未覆盖**而非失败，下次顺带观察。
- ✅ **2026-09-18 实机确认（r140，配置键 `wakuu*` → `vakuu*` 迁移）**：日志
  `logs-archive/godot__20260918-230800__r140.log` —— `瓦库托管生效配置` 已是**新键**且取值与升级前逐项一致；
  **决定性证据 = `vakuuPlayQueue=True` / `vakuuPlayOverlap=True`**（这两个开关在 2026-09-26 转正前默认是"关"）
  ⇒ 确认是**旧键迁移过来的**，而不是被打回默认值；`INIT_STATUS=OK`、`INIT_FAILED=0`、`FATAL=0`、
  我们的 `[ERROR]` **0**、我们的 `[WARN]` **19 条全为启动期第三方 owner / 框架提示**（无选择器残留、无 BUG-15 命中）。
  - 📌 **一处订正（原验证步骤写得太乐观）**：磁盘上的 `vakuu_autopilot.json` 当时**仍是旧键**
    （该文件 mtime 停在 2026-09-18 22:46，早于 r140 那次启动）—— 迁移逻辑**只在"保存配置"时**才把新键写盘，
    那局没动过任何设置 ⇒ 文件未被重写。**不影响读取**（每次加载都会迁移）。这是预期行为，不是 bug。
  - ✅ **2026-09-19 10:53 补验（用户随便改了一个设置）**：磁盘文件 mtime → **09-19 10:53:48**、
    **内容只剩新键**（`wakuu*` 全部消失）、`keepVakuuFormRelic: false`；同会话 `瓦库托管生效配置`
    由启动时的 `keepVakuuFormRelic=True`（旧键迁移值）变为改设置后的 `=False`（L7506）
    ⇒ **读 → 迁移 → 改 → 写新键**整条链闭环。该会话门禁：`marker=2026-09-18-r140`、`INIT_STATUS=OK`、
    `PATCH_RESULT critical=25/25 optional=11/11`、`COMPAT_RESULT PASS`、我们的 `[ERROR]` **0**、
    `[WARN]` 19 条全为启动期第三方 owner（全局 4 条 `[ERROR]` 均第三方/游戏侧）。
- ✅ **配置键清理复查（2026-09-19，静态核对，无代码改动）**：写盘路径**全部**走 `WakuuConfigData`
  的属性名（`TrySetAndSave` / `TrySetAndSaveString` 都是 `nameof(WakuuConfigData.*)`，`Serialize` 直接序列化该类）
  ⇒ 保存时**只会写新键**；旧键仅存在于 `MigrateLegacyKeys`（**读侧**兼容）与迁移测试样本里；
  全仓 `*.json` 搜不到任何旧键样例；`README(.zh-CN).md` 的命名说明是**有意引用旧键**；
  `CHANGELOG.md` 里 r140 之前的条目属**历史记录、不改写**；`TODO.md` § 改进-2 的旧记录同理 ——
  已在该节顶部加一行「键名已统一为 `vakuu*`」的提示（只提示、不改历史）。
- 📌 **已知行为（用户 2026-09-18 拍板：不算 bug、不用修，仅记录）**：**同一家商店只采购一次**
  （`_handled` 以 `(room, player)` 去重，见 `LocalWakuuMerchantAuto.OnMerchantInventoryShown`）——
  所以「买完后再用控制台给瓦库加钱」不会触发第二轮采购，本来买不起的也不会补买。
  实际游戏里金币只会在商店界面**之外**变化（战斗 / 事件 / 遗物），不存在"同店加钱"的场景，
  **不影响游戏体验**。若将来真要支持，需把去重键从"每店一次"改成"金币变化后重评"，
  并处理 `OnMerchantInventoryShown` 反复触发时的节流。
- 📌 **`UseSingleAdventureMode` 是常量 `true`（2026-09-18 发现）**：`LocalSelfCoopContext` 里
  `public static bool UseSingleAdventureMode => true;` ⇒ 全 mod **60+ 处**拿它当门禁的地方**都是 no-op**
  （既有老代码同样如此，非本轮引入）。本轮已删掉自己新加的那一处并订正文案；
  **其余存量不动**（清理属纯卫生工作、零行为收益、改动面大）。日后写新门禁请用 `IsEnabled`
  （真正会变）或别的实变量，别再拿这个常量当条件。
- 📌 **商店统计现状（回答"遗物 / 药水 / 删牌有没有做统计"）**：**都做了，只是分两张表** ——
  - 卡 / 遗物 / 药水 → `shopPurchases`（`kind=card|relic|potion`，字段 `item` + `goldSpent` + `act` + `isMulti`），
    入口 `MerchantEntry.OnTryPurchaseWrapper`（`PersonalShopPurchasePatch`）；
  - 删牌（**含商店删牌服务** / 事件删牌 / 营地删牌 / 删牌遗物）→ `cardRemovals`，
    入口 `CardSelectCmd.FromDeckGeneric`（r64 / r130）；
  - **刻意不重叠**：`MerchantCardRemovalEntry` 用三参重载自己走另一条链，
    `PersonalShopPurchasePatch` 的类型 switch 里明确写了 `// 删卡服务等不记（花钱删牌不是"买了什么"）`。
  **本轮遗物 / 药水的决策并未使用这些统计**（只按价格 + 金币保底）：`shopPurchases` 只回答
  "买过什么、花了多少钱"，**没有**"买了它之后胜率如何"的切片（卡牌有 `PersonalWinSlice`、
  事件有 `CountEventWinSlice`，商店侧没有对应物）。要用统计驱动商店决策，得先补一个
  「商店购买 → 局胜负」的查询（纯函数 + 单测）—— **待用户拍板**。

### 改进-7（= BUG-16）遗物效果自建的奖励集不自动领取 → 瓦库奖励弹屏等真人点（**r143，2026-09-20 已部署，待实机**）

- **现象（用户 2026-09-20 实机反馈）**：「瓦库拾取 YUI extra mod 的**赐福**（一种遗物）时不会自动领取
  赐福自动弹出的、**只有一张牌**的战斗奖励；其它『拾取遗物时获得卡牌奖励』的情况（如**星系仪**）似乎同样如此。」
- **实机证据（`godot.log` 2026-09-20 会话，marker r142，已归档）**：本局 **3 次**
  `打开奖励界面: player=…327, count=1|2`（**327 = 瓦库**）——L48127 / L52195 / L194910，
  紧跟 `RewardsSetSynchronizer Beginning rewards set Id: 0|1|2 Owner: …327 Rewards: CardReward`，
  之后是**真人手动点击**的痕迹（`Card selected: Rampage` / `Card selected: IronWave`）
  与 `个人记录-卡牌奖励批次: … picked=1`（**瓦库的奖励被当成真人决策记进了个人统计**）。
  三处上下文都能看到 `Player …327 obtained RELIC.YUI_SPIRE_EXPANSION_RELIC_BLESSED_* from relic reward`
  —— 「赐福」类遗物的拾取效果就是给一张卡的卡牌奖励。
- **根因（两层，都已修）**：
  1. **直接原因 = `IsMe` 误判（`RewardsCmdOfferCustomPatch` 的静默门禁）**：
     栈痕迹实证 YUI 赐福遗物走的是
     `YuiExtra.Relics.CardBlessingRelic.OfferBlessingReward()` → `MegaCrit.Sts2.Core.Commands.RewardsCmd.OfferCustom`，
     而我们的 `RewardsCmdOfferCustomPatch` 里有一条「**前台正是瓦库（`LocalContext.IsMe(player)`）就不干预，
     交真人点**」（r54 写的）。遗物是在**瓦库的战后奖励结算作用域内**被获得的，该作用域会把 `LocalContext`
     **对齐到瓦库**（`LocalWakuuRewardAutoClaim.AlignLocalContext`）⇒ 作用域内 `IsMe(瓦库)` 恒真
     ⇒ 判据把「我们的自动化造成的 IsMe」误读成「真人正看着瓦库」⇒ **静默 `return true`**（连日志都没有）
     ⇒ 原版弹屏等人点。**与 r54 给"事件自动选择作用域"补 `IsAutoChoosingFor` 是同一个坑，只是换了个作用域。**
  2. **兜底缺口 = 非战斗奖励集没有自动领取链**：即便绕开第 1 条，遗物/第三方自建的 `RewardsSet`
     Room 不是 `CombatRoom`（战后合并链不管）、也不走 `OfferCustom`（第 1 条那条链不管），
     落到 `RewardsSetPatch.OfferLocalSelfCoop` 时原实现一律「切控制到归属者 + 弹原生界面」。
- **修法（r143，两层都补）**：
  1. **`LocalWakuuRewardAutoClaim` 新增 `IsAutoClaimingFor(player)`**（AsyncLocal 作用域标记，与
     `AutoClaimCardOwnerId` 同套写法：`TrySettleAsync` 里置位、`finally` 还原）；
     `RewardsCmdOfferCustomPatch` 在该作用域内**照常自动结算**，并打一条可区分的日志
     `瓦库自定义奖励改由自动结算（奖励自动领取作用域内，上下文被对齐到瓦库，并非真人前台）`。
     ⚠ 判定顺序：`eventAutoScope` → `autoClaimScope` → `LocalContext.IsMe(player)` 交真人，
     保持 r54「整批里有不可自动领取的项就交真人、绝不静默跳过」的语义不变。
  2. **`RewardsSetPatch.OfferLocalSelfCoop` 兜底**：弹屏前对**瓦库形态**归属者调用同款
     `LocalWakuuRewardAutoClaim.SettleAsync`（复用既有开关与规则：卡牌最左 / 金币 / 遗物 / 药水换栏）：
     - 领掉的从 `rewardsSet.Rewards` 移除（该列表是 public 的 `List<Reward>`）；
     - **全部领完 → 直接返回，不弹屏、也不抢视角**，并用
       `CombatRewardMergeContext.BeginDisplaySet/CompleteDisplaySet` 把该奖励集在同步器里登记后立即
       标记完成（与「真人领完最后一张」同一条收口语义），避免留下永不完成的奖励集；
     - 仍有剩余（开关关着 / 删牌类奖励 / 药水换栏判定不值得领）→ 照旧弹屏，**行为与旧版逐字一致**；
     - **刻意不进 `CombatRewardMergeContext`**：本路径没有「每个角色已独立生成奖励」的前提，
       与 `RewardsCmdOfferCustomPatch` 同一套写法（那条链同样直接调 Settle 入口）。
- **顺带修一处真隐患**：`LocalWakuuRewardAutoClaim._suppressCardRewardScreen`（布尔）→
  **深度计数 `_suppressCardRewardDepth`**。r143 起结算会**嵌套**（遗物效果自建的奖励集会在战后奖励结算的
  await 链里再进一次 `TrySettleAsync`），布尔会被内层的 `finally` 提前清成 `false` ⇒ 外层那次卡牌奖励
  漏抑制、又弹出选牌界面。
- **门禁（r143）**：构建 **0 警告 0 错误**（195 个 .cs）、**545 单测全绿**（无新增：修的是运行时链路，靠实机回归）、
  `static_checks` **7 PASS / 0 FAIL**、`clr_compat_check` **PASS**、`preflight.ps1 -Deploy` **4 PASS / 3 SKIP**、
  marker **`2026-09-20-r143`**、`dll_check --deployed` 全绿
  （`IsAutoClaimingFor` / `瓦库自定义奖励改由自动结算` / `瓦库非战斗奖励已自动结算` /
  `瓦库奖励已全部自动领取` 在、`__runOriginal` 不在、部署位与仓库根字节一致 sha256 `e19a44ee0499…`）。
  分支 `feat/wakuu-scored-brain`，**未 commit / 未 push**。
- **验证方法（请实机）**：再拾取一次「赐福」类遗物（或任意"拾取时给卡牌奖励"的遗物 / 星系仪）——
  - ✅ 期望：**不再弹奖励界面**，日志出现
    `瓦库自定义奖励改由自动结算（奖励自动领取作用域内…）: player=…327, rewards=1`
    （若走的是兜底那条链则是 `瓦库非战斗奖励已自动结算: … 自动领取=1, 剩余=0` +
    `瓦库奖励已全部自动领取，不再弹奖励界面: player=…327, 原奖励数=1`），卡片直接进牌组；
  - ✅ 期望：控制视角**不会**被切到瓦库（旧版会切过去再让你点）；
  - ✅ 期望：**没有** `个人记录-卡牌奖励批次` 里那一笔（那是真人点出来的）；
  - ⚠ 反例（属正常）：若你把「卡牌奖励自动领取」开关关掉，则照旧弹屏 —— 那时日志里
    **不应**出现上面那些行，行为与旧版一致。
- **同类风险点（本轮已一并覆盖，无需另做）**：任何第三方 mod / 遗物 / 事件用
  `new RewardsSet(...).WithCustomRewards(...).Offer()` 直接开奖励的写法，都会走同一条 `RewardsSet.Offer`，
  因此本次改动**一次性覆盖**了这一整类入口。
- **仍未覆盖（刻意，按开关语义）**：`CardRemovalReward`（删牌奖励）等 `ShouldAutoClaim` 里 `default: return false`
  的类型仍交真人（与战后奖励链一致）。
- ✅ **2026-09-20 18:35 实机复测通过（marker r143，已归档 `logs-archive/godot__20260920-183506__r143.log`）**：
  用户连给瓦库塞了一批「拾取时给卡牌奖励」的遗物 —— 本局 **5 批自定义奖励共 10 张卡全部自动领取**
  （`瓦库事件/遗物自定义奖励结算完成: player=…327, 领取=1/1/1/5/2, 跳过=0`，每条都有配对的
  `瓦库卡牌奖励已自动领取: … reward=CardReward`）；**`打开奖励界面` 0 次**（用户报的"弹屏等人点"消失）、
  `个人记录-卡牌奖励批次` **0 条**；回归项（选择器作用域异常 / hand node / 看门狗重启 / 领取失败 /
  保留为人工领取 / 检测到真人选牌请求）**全 0**，我们的 `[ERROR]` **0**、`[WARN]` 19 条全是启动期第三方 owner 审计。
  - ⚠ **如实两条**：① 本次 5 批走的是**既有的 `!IsMe` 静默路径**（没出现本轮新增的两条日志锚点）
    ⇒ **没有复现原始场景**（原件是"遗物来自战后遗物奖励、控制上下文被对齐到瓦库"）；
    ② 该局**无战斗**（`瓦库大脑就绪` / `瓦库选择器作用域进入` 均为 0）⇒ 评分档这次没再走一遍。
    ⇒ 本项按「**用户报的现象消失 + 无回归**」收口；IsMe 那条修复的**直接复现留待下次自然遇到**
    （两层修复都已部署，且 `RewardsSetPatch` 兜底覆盖整类入口）。
  - **提交**：`2e96fd1`（r142 评分档）+ `563c8bd`（r143 修复 + 文档收口），分支 `feat/wakuu-scored-brain`。

### 改进-6 Phase 5「局内打牌评分」落地：新增 `vakuuBrain=scored` 档（**r142，2026-09-20 已部署，待实机**）

> 方向来源：`maintenance-docs/decision-records/瓦库托管优化可行性分析.md` §18.2（阶段 B 卡牌评分 /
> §18.2.6 目标选择升级）与 §21.6（**Phase 5 范围收缩为「接口 + 轻量启发式评分 + 目标选择升级」**）。
> 本轮做的是收缩后的那一版：**不引入任何外部求解器、不复制参考实现代码**（§21.6 门槛 4）。

- **做了什么**：
  1. 新增纯逻辑评分/排序 `Scripts/Runtime/PureLogic/WakuuCardScoring.cs`
     （类型基础分 + 费用 + 关键词 + 场景修正；**致死线硬门槛** / **X 费收尾** / **同分最左** / 分数下限 0）；
  2. 新增纯逻辑目标选择 `Scripts/Runtime/PureLogic/WakuuTargetPicking.cs`
     （**可击杀优先 → 否则有效血量最低（集火）→ 并列取最左**）；
  3. 新增大脑实现 `Scripts/Runtime/WakuuBrain/ScoredWakuuBrain.cs`（只做"游戏模型 → 纯逻辑标量"映射，
     含伤害/格挡粗估、`AnyAlly` 优先真人、异常降级为最左可打牌）；
  4. 新增第三档 `vakuuBrain=scored`（`WakuuBrainModes.Scored` + `NormalizeBrainMode` + 工厂分支），
     设置页「瓦库托管」区新增**「战斗决策大脑」**循环行（启发式 → 评分 → 自动探测）；
  5. 意图伤害求和抽成 `Scripts/Runtime/LocalWakuuThreatEstimate.cs`
     （原内联在 `LocalWakuuPotionAutoUse.BuildContext`，药水侧改为调用它，**逻辑逐字搬移、行为零变化**），
     供药水规则表与评分大脑的致死线判定共用同一口径；
  6. 出牌循环加**评分档专属日志锚点** `瓦库评分出牌: player=…, round=…, card=…, target=…, scored:score=N:…`
     （**只在 `vakuuBrain=scored` 时打印**，默认档日志基线不变；记在**执行处**而非大脑里 ——
     `IWakuuCombatBrain` 约定"快路径必须无副作用"）；
  7. 单测 **+31 → 545 全绿**（`WakuuCardScoringTests` 19 / `WakuuTargetPickingTests` 8 / 归一化 4）。
- **默认档不变**：`vakuuBrain` 默认仍是 `heuristic`（最左可打牌），**默认档行为零变化**；
  评分档只在显式切换后生效。
- **已知取舍**（写进代码注释与 CHANGELOG，别再当 bug 报）：
  ① 伤害/格挡是「卡面基础值（含附魔）+ 力量/敏捷」的**粗估**，不含易伤/虚弱等 Hook 修正；
  ② 本期**没有跨回合计划**（逐张重新评分）—— §21.4.2 #7 指出的"线性总分"缺陷靠**致死线硬门槛**兜住最要紧的部分；
  ③ 阵容中途变化（§21.4.2 #2）与局内生成卡的入场联动（#3）不在本期范围（目标选择每轮现读
     `HittableEnemies`，能跟上敌人增减，但不为新增单位重算计划）。
- **实机回归清单（请务必按 §21.4.2 的 #1/#2/#3 各走一遍）**：
  1. **自动出牌/自动从抽牌堆出牌的嵌套选择**（#1）：瓦库打出 横祸 / 破灭 / 骚动 / 蒸馏混沌 等
     "自动出牌"卡 → 观察是否卡住或选牌错位（我们靠全局 selector 兜住，评分档不应改变这一点）；
  2. **阵容中途变化**（#2）：召唤物出现 / 敌人逃跑 / 复活后，瓦库的目标选择与"敌人已全部死亡"判定是否正常；
  3. **局内生成卡的入场联动**（#3）：幻影之刃 / 鬼种等"入战斗时触发"的生成牌是否照常结算；
  4. **基础回归**：`瓦库出牌走动作队列` / `瓦库并发出牌` / `瓦库出牌加速` 三个实验档与评分档的四种组合
     任选一两个组合打一场，确认无 `瓦库选择器作用域异常退出` / `Couldn't get hand node` /
     `瓦库看门狗重启失败`；`瓦库评分大脑异常，本次降级为最左可打牌` 若出现请把日志发回来（那是降级信号）。
  5. **观测锚点**：评分档每打一张牌会有一条
     `瓦库评分出牌: player=…, round=…, card=<卡 id>, target=…, scored:score=<分数>:<卡 id>`；
     切回启发式应**一条都没有**（反过来也说明档位真的切过去了）。
- **门禁（r142）**：构建 **0 警告 0 错误**（195 个 .cs）、`dotnet test` **545 全绿**、
  `static_checks` **7 PASS / 0 FAIL**（S6 marker = `2026-09-20-r142`、S7 目标基线一致）、
  `clr_compat_check` **PASS**、`preflight.ps1 -Deploy` **4 PASS / 3 SKIP**、
  `dll_check --deployed` 全绿（`ScoredWakuuBrain` / `WakuuCardScoring` / `WakuuTargetPicking` /
  `LocalWakuuThreatEstimate` / `EstimateIncomingThreat` / `战斗决策大脑` / `瓦库评分出牌` 在、
  `__runOriginal` 不在、部署位与仓库根字节一致 sha256 `9bf8f9f3b711…`）。
  分支：**`feat/wakuu-scored-brain`**（用户要求"先建分支再干活"；`master` 停在 `6e6dc41`）。
- ✅ **2026-09-20 实机通过（评分档首局，marker r142）**：用户把「战斗决策大脑」切到**评分**打了一整局 ——
  `瓦库大脑就绪: mode=scored, id=scored` ×4、**`瓦库评分出牌` 501 条**、
  **`瓦库评分大脑异常，本次降级为最左可打牌` 0 条**；回归项 `瓦库选择器作用域异常退出` /
  `Couldn't get hand node` / `瓦库看门狗重启失败` / `瓦库奖励自动领取失败` / `保留为人工领取` 全 **0**，
  我们的 `[ERROR]` **0**（全局 22 条全为第三方/游戏）。`手牌UI与数据存在差异` 2 条属 r112 的
  **只诊断不处理**锚点（设计如此）；`检测到真人选牌请求` 31 条上下文都是**真人自己打需要选牌的牌**
  （Private Square / Knife Throw），是既有设计的正确路径。
  ⇒ §21.4.2 的 #1/#2/#3 三条回归**未单独取证**（本局没有走到可判定的场景），
  但"评分档本身不引入异常/降级"已有 501 次决策的实测支撑。**玩法观感仍以用户主观为准。**
- **仍未做**：`vakuuBrain=auto` 的求解器适配器（§21.3.4，等真有可用求解器再说）；
  逐卡评级/效果表（P4）仍卡在"要自己写抽取器"这条路上（见 § 决策表缺口）。

---

## 维护性改进 backlog（门禁体系 2026-09-08 之后的下一批）

已落地（见 `AGENTS.md` §9 门禁表 + `Scripts/Tools/clr_compat_check.py`）：
源码隔离 Guard、单元测试门禁、CLR/PE/Assembly 三层兼容检查、Critical/Optional 补丁分级、
`INIT_OK`/`INIT_FAILED` 终态收口 + 统一错误码、`dll_check` false-green 修复、marker 缺失 FAIL、
`log_parser --init-status`。

下一轮候选（按性价比排序，2026-09-08 更新）：

1. ~~**Harmony owner / 第三方 Patch 冲突检查**~~ ✅ 已做（Entry.cs `LogKeyPatchOwners`，r88）：
   `关键目标 NPlayerHand.SelectCards — sts2.dualroleadventure [P2Po0...]`，
   第三方 owner 单独 WARN。
2. ~~**BuildIdentity 增强**~~ ✅ 已做（csproj 注入 GitCommit/GitDirty/BuildTimeUtc，r89）：
   `BUILD_IDENTITY commit=<hash> state=clean|dirty built=<UTC>`。
3. ~~**部署槽唯一性检查**~~ ✅ 已做（r93）：跨槽位 json id 重复检测（WARN，既有）+ 新增
   `Test-SlotIdentity`（本仓库 mod 的「json id ≠ dll 主文件名 / id 对应 dll 缺失」= **FAIL**，
   `-List`/部署/`-CheckOnly` 三种模式都跑）与全槽位 WARN 扫描 `Find-SlotIdDllMismatch`。
   命名规则已按各槽实证确认：**目录名可与 id 不同，但 dll 必须与 id 同名**
   （`DualRoleAdventure` 槽 = `DualRoleAdventurefixed.dll` + id `DualRoleAdventurefixed`）。
4. ~~**标准化回归验证契约**~~ ✅ 已做（AGENTS.md §10）：
   `BUG / EXPECTED / SETUP / ACTION / OBSERVE / PASS CONDITION / FAIL CONDITION / LOG ANCHORS`，
   配合固定 token（`INIT_OK` / `PATCH_RESULT` 等）可机器校验；每轮交付随改动写明。
5. ~~**ExpectedPatchTargets 升级到完整签名**~~ ✅ 已做（r92）：Critical/Optional 清单全部换成
   FullName，重载目标钉死参数个数（`FromCombatPile/4`+`/5`、`TryToProcure/3`、`FromDeckForEnchantment/4`）；
   新增 `ExpectedPatchTargetsTests` 用 sts2.dll 元数据逐条核对（格式 + 可解析 + 参数个数 + 无重复），
   清单写错在游戏更新/手误时**单测先红**，不再等到实机误报 Critical 缺失。
6. **marker 解析的对齐坑**：`deploy_dll.ps1` 的 UTF-16 解码已修（r92，两种对齐都扫）。
   同类隐患：`dll_check.py` 早就是双对齐，其它自研脚本若从 dll 里抠字符串需同样处理。
7. ~~**发布包构建元数据（源码 commit + 依赖锁定）**~~ ✅ 已做（2026-09-19，用户拍板）：
   `release_build.ps1` 在 build 后生成 `build-info.json`（打进 zip + `release\` 留同名副本），
   记录 git commit / 分支 / dirty（含**改动文件路径清单**，并单独给出
   `dirtyFilesExcludingVersionJsons` 以区分"发布流程自身改的 3 处版本 json"）与依赖锁定
   （toolchain：dotnet SDK / Godot SDK / TargetFramework；**gameAssemblies**：`sts2.dll` /
   `0Harmony.dll` / `Steamworks.NET.dll` / `GodotSharp.dll` 的 fileVersion · productVersion ·
   size · SHA256）。做法参照 CouchCoop 的 `build-info.txt`。
   ⚠ **实现踩坑（已修）**：**不要用 `git status --porcelain` 的字符串截位取路径** —— porcelain
   首行 `" M path"` 的**前导空格是有意义的**，被 `Out-String | .Trim()` 吃掉后首行路径会少一个字符
   （实测产出过 `ualRoleAdventure.json`，进而把版本 json 误判成"非版本号改动"）。
   现改用两条输出干净的命令：`git diff --name-only HEAD` + `git ls-files --others --exclude-standard`。
8. **版本 lane + 根 loader（**待正式版更新再做**，用户 2026-09-19 拍板）**：
   现状 = 只吃当前游戏版本（public-beta `0.111.0`），而且**原作者的 mod 依旧适配现在的正式版**
   ⇒ 现在做没有任何收益，**先不做**。等**正式版更新**（我们和原作者都不得不跟版本）时再动手。
   做法参照社区两个工坊作品（`3799476240` CouchCoop 的 `lanes/0.107.1` + `lanes/0.111.0`；
   `3802686135` LocalCoopClone 的 `lib/<ver>/`）：根目录放一个极薄的 **loader dll**
   （读 `release_info.json` 判定宿主游戏版本 → 用自定义 `AssemblyLoadContext` 从
   `lanes\<gameVersion>\` 加载对应实现），一份工坊作品同时覆盖正式版与 public-beta。
   我们侧的额外成本：每个 lane 需要一份**独立的引用路径配置** + 各自重跑 `regenerate_src.ps1`
   生成对应 `sts2src`，S7 目标基线也要按 lane 拆（`targets.baseline.txt`）。
   ⚠ 前置判断：**只有真的需要同时支持两个游戏版本时才做**，单版本下 lane 纯属增加复杂度。
9. **存量日志/格式卫生（2026-09-25 r144 顺手发现；编码项已于 r151 完成）**：
   - ✅ **已修（r151，2026-09-26）**：本文件前一条描述的乱码串实测**只集中在 `LocalSelfCoopContext.cs`**
     （**10 条**日志串，UTF-8 字节被当 GBK 解码），已全部还原为正确中文，**零行为改动**（只改字符串字面量）。
     扫描口径：`.cs` / `.ps1` / `.py` 共 **257 个文件**；判据 = 片段 `GBK→UTF-8` **严格往返**可还原 +
     还原结果字符白名单 ⇒ 对正常中文零误报（旧版 10 行全命中、修复后 0 命中）。
   - **已固化为门禁**：`static_checks.py` 新增 **S8 源码编码卫生**（离线静态层自此 **8 项**）。
     理由：这类乱码**不报错、不影响功能**，但实机日志里就是乱码 ⇒ **日志锚点无法 grep**，
     而我们的排查（`log_scan.py` 计数、跨会话对比）全靠锚点。日志字符串本身属排查资产，必须门禁化。
   - **`dotnet format --verify-no-changes` 本来就是红的**：报 `CardTransformNetIdPinPatch.cs` /
     `LocalWakuuMerchantAuto.cs` / `WakuuStatBadgeTests.cs` 的**存量** WHITESPACE 问题。
     它不在 §9 强制门禁链（`preflight` 的 G3 是 diff 预审，`-Lint` 才跑 lint）。
     要么一次性 `dotnet format` 收敛并加进 G3，要么明确"不做格式门禁"。

---

## 决策表缺口（`coverage_digest.py` 对账发现，2026-09-16，**待用户拍板**）

> 来源：`tools/coverage_digest.py`（外部 CombatSolver 的 `COMBAT_HOOK_COVERAGE.md` 汇总 × 仓库决策表对账），
> 报告落 `maintenance-docs/combat-hook-coverage.md`，哨兵结果 **PASS 11 / WARN 1**。

### 改进-4 三种原版药水「一览表写了使用时机、规则表与代码里都没有」（**2026-09-17 r136 已补实现**）

| 药水（类型名） | `原版药水一览表.md` 的「使用时机」 | 该表「当前mod行为」列 | 代码实况 |
|---|---|---|---|
| `FlexPotion`（肌肉药水） | 精英/Boss 有攻击牌 | 精英/Boss 首回合 | `Scripts/` 下 **0 处**出现 |
| `PotionOfBinding` | 精英/Boss 首回合对敌 | 精英/Boss 首回合对敌 | **0 处** |
| `OrobicAcid` | 精英/Boss 首回合 | 保守不自动用 | **0 处** |

- **机制**（已核实代码）：`LocalWakuuPotionAutoUse.cs:419` 对**未收录的原版药水**是
  `continue; // 未收录原版药水保守跳过` ⇒ 这三瓶目前**永远不会被瓦库自动使用**，
  真人手动用不受影响（也不影响任何数据层正确性）。
- **为什么算「疑似缺口」而不是「预期」**：一览表的「使用时机」列是**期望**，写了具体时机；
  另外两根对照项已排除：`FairyInABottle` 那行写的是「不用（游戏会自动使用）」、
  `FoulPotion` 走 `FoulPotionPatch` 专用机制（`Scripts/` 下 15 处出现）。所以只剩这 3 瓶说不清。
- **两种可能，需用户判定**：① **漏实现** —— 用户当初写了期望但没落地（那就补规则表条目）；
  ② **表填过头** —— 「当前mod行为」列把期望写成了现状（那就改表 + 明确不自动）。
  ⚠ 注意 `FlexPotion` / `PotionOfBinding` 两行的「当前mod行为」列写的就是「精英/Boss …」，
  与实际不符 —— 无论走哪条路，**这一列都该顺手校正**。
- ✅ **2026-09-17 用户拍板「补实现」→ r136 已落地**：
  - `LocalWakuuPotionAutoUse` 规则表 **+3 条**（都照同类药水的既有形状写，并加注释注明来源）：
    | 规则名 | 药水 | 形状 |
    |---|---|---|
    | `肌肉药水有攻击牌` | `FlexPotion`（肌肉药水） | `HardFight` + `Condition = 手牌有攻击牌`（与「速度药水有技能牌」同型，不限定首回合） |
    | `缚魂药水首回合对敌` | `PotionOfBinding`（缚魂药水） | `HardFight` + `FirstRoundOnly`（`AllEnemies`，与易伤/虚弱同批） |
    | `欧洛巴斯之酸首回合` | `OrobicAcid`（欧洛巴斯之酸） | `HardFight` + `FirstRoundOnly`（`AnyPlayer`，与攻击/技能/能力药水同批） |
  - 单测 **+3**（`PotionRuleTableTests`：逐条断言 `MatchedPotionTypeName` / `Scope` / `Phases` / `FirstRoundOnly` / `Target`），
    数量断言 `InRange(55, 65)` → `InRange(55, 70)`（当前 **63** 条；上线时才发现默认 `Phases` 是 `Both`，
    断言按实际语义写并注明理由）。**502 单测全绿**（499 → +3）。
  - 一览表的「当前mod行为」列顺手校正：`FlexPotion` 「精英/Boss 首回合」→「精英/Boss 有攻击牌」、
    `OrobicAcid` 「保守不自动用」→「精英/Boss 首回合」（`PotionOfBinding` 本来就对）。
  - 门禁：构建 0 警告 0 错误、`preflight.ps1 -Deploy` **4 PASS / 3 SKIP**、`dll_check --deployed`
    全绿（marker **`2026-09-17-r136`**、部署位与仓库根字节一致 sha256 `8e299f88…`、无 `__runOriginal`）。
  - **对账已清零**：`python tools\coverage_digest.py --strict` → **PASS 16 / WARN 0**
    （药水规则表覆盖 60 → **63**，缺口规则消失；退出码 0）。
- ✅ **2026-09-17 实机确认（用户「测试无异常」）** —— 日志：`godot.log`（11116 行，已归档
  `logs-archive/godot__20260917-221609__r136.log`）：
  - `BUILD_ID … marker=2026-09-17-r136`、`INIT_OK 1` / `INIT_FAILED 0`、`COMPAT_RESULT` / `PATCH_RESULT` 各 1 条；
  - **三条新规则各命中 1 次**（均 `round=1, phase=StartOfTurn`，`target` 与语义一致）：

    | 行号 | 日志（节选） |
    |---|---|
    | L9734 | `瓦库自动用药: player=…327, round=1, phase=StartOfTurn, potion=FLEX_POTION, reason=肌肉药水有攻击牌, target=PlayerId …327` |
    | L9779 | `… potion=POTION_OF_BINDING, reason=缚魂药水首回合对敌, target=无`（`AllEnemies` ⇒ 目标解析为"无" ✓） |
    | L9825 | `… potion=OROBIC_ACID, reason=欧洛巴斯之酸首回合, target=PlayerId …327`（`AnyPlayer` ⇒ 自用 ✓） |

  - 用药链路异常 0：`瓦库自动用药跳过（目标非法）` **0**、`瓦库用药条件判定异常` **0**；
  - 回归项（`log_scan --preset health` 的期望 0 命中项）**全 0**：选择器作用域异常退出 / 看门狗重启失败 /
    `Couldn't get hand node` / 动作队列UI入队触发空引用 / 保留为人工领取 / 检测到真人选牌请求 / 手牌UI与数据存在差异；
  - **我们的 `[ERROR]` 行 0**（全局 5 条 `[ERROR]` 全为第三方/环境：Manosaba·ddu 分支不支持、
    BetterModMenu 抓 Workshop tags 超时、游戏自身删旧存档失败 ×2）；
    我们的 `[WARN]` 行 19 → 20（+1，唯一一条是 § BUG-15 的自恢复命中，与本轮改动无关）。
  ⇒ **本项关单**。

### 另记：逐卡评级草表 —— **明细已到手，但这条路仍然不成立（理由换了）**（2026-09-19 订正）

- **09-16 的问题**：外部文件只有 **Hook 目录汇总**（分类级计数），没有逐项明细；文档里
  「3035 项」是 `2302（Exact）+ 733（OutOfScope）` 的 Hook 总数，没有可枚举的条目清单。
- **09-19 已解**：用户拉下 CombatSolver 仓库 ⇒ `CombatSolver-main\coverage\combat-hooks.json`
  **就是那份逐项明细**（**3035 行**，正好等于汇总结论那句「3035 项」），已由
  `tools/coverage_digest.py --detail-out` 吃干 → **`maintenance-docs/combat-hook-detail.md`**
  （逐实体档位 A 引擎精确 / B 推断 / C 求解器补偿 / D 不支持 / E 非战斗、与 `game-entities.md`
  逐类目对账、样板外卡 51 张）。
- ⚠ **但「LLM 从明细产出逐卡评级草表」这条依然不成立**：明细是 **hook 级**的，而卡牌层
  **529/580** 张卡只有 `OnPlay` + `OnUpgrade` 两条**样板 hook** ⇒ **hook 名不携带逐卡效果信息**，
  让模型据此写评级仍是幻觉。逐卡有信息量的只剩两列：
  `engineDispatch` 三档（Card 分类：精确 218 / 推断 150 / 不支持 225 行，**确定性**）与
  `notes`（Card 分类 475 条，是 CombatSolver 作者的措辞 —— **许可红线：只读思路，不得复制**）。
- ▶ **若要继续做（待拍板）**：把 `combat-hook-detail.md` 当**优先级清单**（三档 + 51 张样板外卡），
  由**我们自己的抽取器**从 `sts2src` 产逐卡效果表 → 人审 → 固化 `WakuuBrain` 用的 `PureLogic` 表；
  **默认关**，须过 §21.4.2 回归清单。详见 `maintenance-docs/combat-hook-detail.md` 与
  `本地LLM辅助开发可行性分析.md` §4.6 / §7-P4。

---

### BUG-15 事件页自动切换后残留 1 个策略选择器（自恢复已清理，**无功能影响，待观察**）

- **现象（2026-09-17 r136 局首次出现在日志里）**：
  ```
  L8437 [INFO] 瓦库选择器栈探针: source=apply-control-before-rewards-offer, selectorStackCount=1,
        selectorStackTop=LocalWakuuStrategySelector, allVakuu=True, inFlight=0
  L8438 [WARN] 检测到瓦库选择器栈残留，已执行自恢复清理: source=apply-control-before-rewards-offer,
        clearedCount=1, selectorStackTop=LocalWakuuStrategySelector
  ```
  清理后 L8441/L8442 探针 `selectorStackCount=0`，后续流程正常。
- **上下文（同一局 L8415~L8438）**：`apply-control-after-event-finished-next-player`（栈 0）→
  `个人记录-事件选择 event=NEOW page=2 chosen=YUWANCARD-SEVEN_CURSES_SKIP` →
  `记录事件自动切换请求` → `event=NEOW page=3 chosen=LOST_COFFER` →
  发奖励前的自检点发现栈里有 1 个 `LocalWakuuStrategySelector` ⇒ 判定为**事件页自动切换路径上压了选择器
  而该步没有走到摘除**（`allVakuu=True` 说明压入时上下文还钉在瓦库身上）。
- **归因（为什么说与本轮 r136 无关）**：
  ① r136 只加了 3 条**药水规则表条目**，未触碰选择器栈的压/摘代码；且本局的 3 次自动用药发生在
  **L9734 / L9779 / L9825**（战斗 round=1），**晚于**这条残留（L8437），时间上不可能由它引起；
  ② 这条路径（多页事件自动切换 + 宇万卡扩展事件）在**此前 8 份归档日志里从未被走过**
  （`记录事件自动切换请求` / `YUWANCARD-SEVEN_CURSES` / `非共享事件房间…重建` 全为 0 命中，
  连 `瓦库自动用药` 也是 0）⇒ 旧日志**不是有效的对照基线**，"首次出现"更可能是**首次覆盖到这条路径**；
  ③ 守卫按设计自愈：清理后栈归零、无异常计数、无软锁。
- **影响**：无（自恢复）。**决策：先不动**，等下次复现（或再遇到多页事件）时看是否稳定复现，
  复现再顺 `WakuuSelectorRegistry.Open` 在"事件页自动切换"这条链上的配对 `Pop`。
  ⚠ 若日后要查：`log_scan.py --rules <含"检测到瓦库选择器栈残留"的规则文件> --file godot.log --show`
  （中文关键字**不能走命令行**，必须走规则文件）。

---

### BUG-17 事件末页效果是「牌组选牌」时，非共享事件房间推不动（2026-09-25 第一幕实机发现，**r147 只加了诊断，未定死根因**）

- **现象（用户原话）**：「事件获得卡牌奖励 …… 导致屏幕变暗且无法继续（可退出重进恢复）」。
  日志里两种形态都能看到：
  - 卡死形态：事件末页选完后界面停在 `optionCount=1`，点角色按钮反复重建
    （`非共享事件房间已按当前角色重建` ↔ `optionCount=1`）也不动，用户按 ESC 退出重进；
  - 对比能过的那次：同一事件（`INTEGRATED_STRATEGY_EVENTS_EVENT_DESPERATE_CHOICE_EVENT`）
    多出一行 `打开奖励界面: player=…326, count=1`，领完即正常推进。
- **日志证据（marker r146 那局，已归档 `logs-archive/godot__20260925-…`）**：
  - 卡死的两次（L15535 起 / L15955 起）末页都选了 `BANSHEE.options.HOME_GIFT`（＝牌组变形选牌），
    只有 `本地多控下强制牌组选牌弹出背包: source=FromDeckForTransformation` /
    `NDeckCardSelectScreen`，**没有** `打开奖励界面`；
  - 能过的那次（L16423 起）末页选 `CYCLOPS.options.ALLIES_GIFT`（＝卡牌奖励），有 `打开奖励界面`。
  - ⚠ 其中一次（L15619）所在的那局内**没有任何** `Parent node is busy … add_child() failed`，
    所以**不能**全归给 r147 已修的幽灵弹层（那是另一条，见 CHANGELOG r147）。
- **假设（待下一局日志判定）**：非共享事件的末页效果走 `NDeckCardSelectScreen` 牌组选牌时，
  `EventRoom` 的"完成 → 推进"没被触发（或我们的"按角色重建"把某个已完成的视图重建成了半死状态）。
- **r147 已加的诊断（下一局按这三条锚点定性）**：
  1. `事件流程已完成，等待奖励/选择弹窗关闭后自动切换角色。 overlay=screenCount=N, top=Type[inTree=…]`
  2. `事件已完成，等待弹窗关闭后自动切换到下一位: … overlay=…`
  3. `非共享事件房间已按当前角色重建: player=…, event=…, isFinished=…, overlay=…`
     —— `inTree=False` ⇒ 仍是幽灵弹层（回到 r147 那条）；`screenCount=0` ⇒ 事件状态机自身问题。
- **下一步**：复现一局 → 用上面三条锚点分流 → 再决定是补 `NEventRoom` 的推进兜底，还是回到
  `EventRoom` + `NDeckCardSelectScreen` 的完成链。
- 📌 **2026-09-25 归档日志复盘（不需要实机，纯读 `logs-archive/godot__20260925-151258__r146.log`）**
  —— 三条**订正**（原先的描述有三处不准确，按证据改口径）：
  1. **末页效果不是「牌组变形选牌」**：两个卡死窗口里，窗口 3 的末页效果实证是**从牌组移除一张牌**
     （`L16064 Player …326 chose cards [WATCHER-DEFEND_WATCHER]` +
     `L16065 个人记录-删牌钩子命中: prompt=TO_REMOVE, 选中=1, 记录`）；窗口 2 则**没有任何**真人选牌日志
     （无 `chose cards`）。全文 3 次 `本地多控下强制牌组选牌弹出背包`（L15576 / L15978 / L16464）
     **全部是 CB Bot 自己的 deck edit**（紧跟 `CoopBots build: edit purpose=FromDeckForTransformation bot=…`），
     与真人末页无关 ⇒ 「末页是牌组变形」这个定性作废。
  2. **`optionCount=1` 不能当「已 SetEventFinished」的证据**：那是第三方事件（IntegratedStrategyEvents）
     自己布局里的选项计数，可能只是"下一页只剩一个继续选项"；同理
     `记录事件自动切换请求` 只证明"本地玩家刚选了一个选项"（`ChooseLocalOption` 后缀触发），
     不证明事件已完成。**这两条都不能再用来判断 `IsFinished`。**
  3. **幽灵弹层与本次卡死无关**（原先怀疑的那条链不成立）：L14946 那条 `add_child() failed` 属于**上一个房间**的
     卡牌奖励（紧接着 `Player 127167579810… selected card reward`，事件房 L15057 才开始），
     两个卡死窗口所在的房间访问内**一条 `add_child() failed` 都没有**。
- ✅ **本轮新增的两条硬事实（这才是下次复现时的抓手）**：
  - **卡死在"事件房 → 地图"这一段，不是事件内部**：两次卡死访问里
    `地图自动跟投` / `MoveToMapCoordAction` **0 条**（整局只在**第 4 次**访问成功时出现
    `L16573 地图自动跟投: vote=MapVote (gen: 1 coord: (6, 5)), filled=3/3` → L16579 触发推进 → L16644 换节点）
    ⇒ 用户当时**根本没走到地图**（Proceed 没被点成 / 没出现），最后靠 ESC「重启房间」重开（11 次）。
  - **不是我方的 Proceed 守卫**：全文 `检测到另一名角色尚未完成事件，拦截 Proceed` **0 条**；
    同窗口那 4 次来回切人日志写的是 `source=hotkey:Tab/]/R` = **用户自己按热键自救**，不是我方自动乒乓。
- **结论（2026-09-25）**：r147 起 3 份日志（41 MB r147 + 2 份 r148）**零复现**，
  且 `非共享事件房间已按当前角色重建` 的 `isFinished=True, overlay=screenCount=0` 全部干净
  ⇒ **本轮不为它写修复代码**。若再次出现，按下面顺序取证（比原来的三条锚点更准）：
  ① 先看这一段的 `弹层栈快照`（`overlay=screenCount=N, top=Type[inTree=…]`）——
  若 `top=NDeckCardSelectScreen` / `NCardRewardSelectionScreen` 仍在栈里 ⇒ 是选牌屏没关；
  ② 再看有没有 `地图自动跟投` —— 0 条说明卡在"点 Proceed 之前"；
  ③ 若前两条都干净 ⇒ 才需要补「事件选项处理任务结束后采样 `IsFinished`」的新诊断
  （挂 `EventSynchronizer` 的 `_pendingOptionTasks`，注意必须在 Godot 主线程上采样）。
- **2026-09-26 第三份负样本（marker r148，第一幕整局）**：r148 埋点**首次命中 2 条**，
  但 reason 都是良性的 `combat-not-in-progress`（`PRECISE_CUT` L37800 / `DEADLY_POISON` L43663，
  两张都在**战斗刚结束那一刻**被点，原版本来就不会打出）；`手牌点击已受理但出不了牌` 6 条
  也全部正常（5 条 `GUILTY` 诅咒牌 `HasUnplayableKeyword` + 1 条 `EnergyCostTooHigh`）。
  ⇒ 「瓦库出牌期间真人点不动」**连续三局未复现**；埋点零副作用、继续保留。
  相关观察（既有设计，不是新问题）：`瓦库自动出牌已熔断跳过 … reason=hand-in-card-play` **33 次**、
  `hand-in-card-selection` 11 次、`combat-not-in-progress` 4 次 —— 瓦库自动出牌会在"手牌正在出牌/正在选牌"时
  主动收手，也说明 InCardPlay 这个状态在局内出现得很频繁（若将来 BUG-18 复现，这是第一个要看的方向）。

---

### r149（2026-09-26）：休息区里第三方席位（Co-op Bots）的选择现在会显示出来 —— 原「待拍板」项已实现

- **用户原话**：「bot 似乎在休息处不会行动」，补充「bot 每次残血了都不回血，合理怀疑选项没生效，或者每次都选锻造」。
- **实测结论：它是行动的，只是完全看不见**。marker r148 第一幕整局的 **6 个休息区**（L27368 / 28302 / 33900 / 38098 / 43845 / 48672）都有
  `[RestSiteSynchronizer] Rest site option index N chosen for player 12716757972810793218 with success True. Option: MEND|HEAL|SMITH`
  \+ `Clearing all remaining rest site options` + `Completing rest site`（选择各不相同、全部生效）。
- **为什么看不见**：CB 在 `RestSiteSynchronizer.BeginRestSite` 那一刻就把该席位的 `PlayerChoice` 答了
  （`Reserved choice id 4` → 选完 → 房间节点**随后**才加载：`Preloading 'RestSite Room'` 在选完之后）
  ⇒ 没有选中动画、没有角色气泡；而我方气泡驱动只覆盖**瓦库席位**
  （实证：`[气泡诊断] SetSelecting` 的 owner 只有真人 46 次 + 瓦库 18 次，**Bot 0 次**）。
- **「回血」这条也查实了（用户补充的怀疑）**：`MEND` 不是"自己回血"，它是**指定一名玩家**回
  30% 最大生命（`MendRestSiteOption.OnSelect` → 要么开目标选择 UI，要么 `WaitForRemoteChoice` 拿目标；
  **拿不到目标就 `return false`、一点血都不回**）；`HEAL` 才是自己回 30%。该局 Bot 在 6 个休息区里
  **3 次选回血**（MEND / HEAL / HEAL）、**3 次选锻造**（SMITH）⇒ 「残血不回血」的直接原因是
  **它一半时间在锻造**，加上它的选择没有任何可视化（既看不到选了什么，也看不到血是它加的）。
  选什么是 CB 自己的策略（它自己的日志里 `idle: kernel=Fallback` + 215 条 `tournament: falling back… CanonicalModelException`），
  我们**不干预**；我们只负责把它选了什么显示出来。
- ✅ **已实现（r149，2026-09-26）**：新增 `LocalRestSiteSeatBubble`（纯表现）+ 纯判据 `RestSeatBubblePolicy`（+5 单测）：
  订阅游戏公开事件 `RestSiteSynchronizer.AfterPlayerOptionChosen`（**不新增 Harmony 目标**）→ 记下第三方席位的已选项 →
  房间就绪后补画「已选」气泡；`success == false` 只记 WARN、**不画"已选"**（避免把没生效的显示成生效）。
  接入点两处：`RestSiteSynchronizerBeginRestSitePatch`（订阅 + 清旧记录）、
  `NRestSiteRoomReadyPatch.EnsurePrimaryPlayerOptionsVisible`（房间就绪后补画）；退局在 `RunCleanup` 里 `Reset`。
  门禁：构建 **0 警告 0 错误**（205 .cs）、**602 单测全绿**、`static_checks` 7 PASS、`clr_compat_check` PASS、
  `preflight.ps1 -Deploy` **4 PASS / 3 SKIP**、marker **`2026-09-26-r149`**、`dll_check --deployed` 全绿
  （`LocalRestSiteSeatBubble` / `RestSeatBubblePolicy` / 三条新锚点在、`__runOriginal` 不在、部署位与仓库根字节一致）。
- **验证方法（下次实机顺带看）**：任意休息区应出现
  `第三方席位休息区选择已记录（将补画气泡）: player=<Bot>, option=…, success=True`
  → 紧跟 `第三方席位休息区气泡已补画: source=rest-site-ready-0, player=<Bot>, option=…`，
  并且**画面上该席位的角色头顶出现它选的选项图标**（这是本轮唯一要肉眼确认的东西）。
  若出现 `第三方席位休息区选项执行失败（游戏返回 false，本次未生效）` ⇒ 说明那次选择真的没生效（那就是另一个问题了，把日志发我）。
- **不做的部分**：让 Bot "像人一样慢慢思考再选" 属于 CB 自己的行为，我们不改（也不该改）。
- ✅ **2026-09-26 实机确认（marker r149，第二幕整局）**：4 个休息区**全部**成对出现
  `第三方席位休息区选择已记录（将补画气泡）: player=<Bot>, option=…, success=True` →
  `第三方席位休息区气泡已补画: source=rest-site-ready-0, player=<Bot>, option=…`；
  `第三方席位休息区选项执行失败` **0 条** ⇒ 记录与补画链路都按设计工作。
  ✅ **画面气泡已由用户肉眼确认可见（2026-09-26，用户反馈「bot 休息区气泡显示已经有了，我之前有看到」）** ——
  本项**关单**（唯一剩余待确认项清零）。注：r151 那局（2 席、`coopBotsSeats` 空）Bot 未在休息区作答，
  日志里只有 `第三方席位休息区气泡已复位: source=run-cleanup`（退局清理），属正常。

---

### BUG-20 瓦库不自动领取「特殊卡牌奖励」= 取回被跳虫偷走的牌（2026-09-26 用户报，**r150 已修并部署**）

- **用户原话**：「瓦库似乎不会取回自己被偷走的牌」。
- **机制查证（st2src 源码）**：原版**唯一**会偷牌的怪是 `ThievingHopper`（跳虫：`ThieveryMove` 按稀有度优先级
  从 Draw/Discard 抽一张 `CardPileCmd.RemoveFromCombat` + `SwipePower.Steal`）；打死它时
  `SwipePower.BeforeDeath` 用 `new SpecialCardReward(StolenCard.DeckVersion, 失主)` + `AddExtraReward`
  把牌**作为该玩家的额外战后奖励**还回来（`MarkLootReturned`）。`SpecialCardReward` 的类注释自己就写着
  "like `ThievingHopper` giving you your stolen card back as a reward"。
- **为什么瓦库拿不回来**：`SpecialCardReward.OnSelect` 才 `CardPileCmd.Add(card, PileType.Deck)`
  （**领取之前这张卡不在牌组里**），`OnSkipped` 只记 `wasPicked:false` ⇒ **不领 = 牌就没了**；
  而我们的 `LocalWakuuRewardAutoClaim.ShouldAutoClaim` 把它落在 `default: return false`
  （注释"删牌/特殊奖励等保持人工"）⇒ 瓦库那条只能等真人在合并奖励屏上手动点，容易漏。
- **实机旁证（marker r149 第二幕那局）**：该局 3 条 `SpecialCardReward`（合成 Bot 白噪声 / 真人精准切割 /
  瓦库战斗恍惚），瓦库那条确实落在了真人的展示集里被手点掉；另外**最近 31 份归档日志里没有任何
  `ThievingHopper` 出没**（`Thieving` / `跳虫` / `HOPPER` 全 0）⇒ 用户看到的"被偷走的牌"更可能是
  特殊精英/事件发的同类奖励 —— 但**两条来源共用同一个奖励类型**，修一处即覆盖。
- **r150 修法（已部署）**：新增纯判据 `WakuuRewardClaimPolicy`（+8 单测）把「特定卡牌」归到与卡牌奖励
  **同一个开关 `autoClaimCards`**；`TrySettleAsync` 增 `case SpecialCardReward`（它不读 `CardSelectCmd.Selector`、
  不弹选牌屏 ⇒ **不需要**压选择器作用域）；新日志
  `瓦库特殊卡牌奖励已自动领取（取回被偷走的牌/指定卡牌）: player=…, reward=SpecialCardReward`。
  `CardRemovalReward`（删牌奖励）**仍保持人工**（要走瓦库的 Remove 选牌规则，属另一条链）。
  门禁：构建 **0 警告 0 错误**（206 .cs）、**610 单测全绿**（602 → +8）、`static_checks` 7 PASS（S7 目标集合未变）、
  `clr_compat_check` PASS、`preflight.ps1 -Deploy` **4 PASS / 3 SKIP**、marker **`2026-09-26-r150`**、
  `dll_check --deployed` 全绿（`WakuuRewardClaimPolicy` / `WakuuRewardKind` / 新锚点在、`__runOriginal` 不在、字节一致）。
- **验证方法（下次遇到"给特定卡牌"的奖励时顺带看）**：期望
  `Player <瓦库id> obtained CARD.X from special card reward` **紧跟着**
  `瓦库特殊卡牌奖励已自动领取（取回被偷走的牌/指定卡牌）: player=<瓦库id>, reward=SpecialCardReward`，
  并且真人奖励屏上**不再**出现瓦库那一条；反例（正常）：关掉「卡牌奖励自动领取」后照旧要真人点。

---

### BUG-18 瓦库出牌期间真人「点了牌没反应 / 无法插队」（2026-09-25 r147 实机反馈，**r148 已加诊断，根因未定**）

- **现象（用户原话）**：「战斗中 bot 死后瓦库出牌我无法插队，只能等瓦库打完才能打」。
- **实机量化（marker r147 那局，43 MB / 305133 行；`EnqueueManualPlay` ↔ `playing card` 对齐统计）**：
  - 机器人死亡（L262323）**之前**：真人 338 次点牌里有 **40 次**要排队等别的牌先结算
    （等待 1~15 张，最长一次 `WWWWWBBBBBBBBBW` = 15 张），其余 298 次立即结算；
    `让真人插队`（撤掉瓦库尚未执行的入队动作）命中 **89 次**，**全部在死亡之前**；
  - 死亡**之后**：41 次点牌**全部立即结算**（0 张插队），`让真人插队` **0 次**；
  - 但死亡后有一段 **12 秒**（L263362 真人出完 ERUPTION → L265792 才出下张）期间，
    瓦库连出 4 张（THRASH / POMMEL_STRIKE / INFERNO / ECLIPSE），而**真人一次 `EnqueueManualPlay` 都没有**
    —— 也就是说这段时间里真人的点击**被静默吃掉了**，日志里没有任何痕迹（既无我方日志，也无游戏提示）。
- **已排除**：幽灵弹层（本局 `幽灵弹层已自愈` = 0、`Parent node is busy` = 0）、
  弹层遮挡（同窗口看门狗拒绝原因是 `watchdog-in-flight` 而非 `overlay-open`）、
  玩家动作被禁用（`actionsDisabled=True` 的 37 次都不在该窗口）、额外回合（只在 L255348 及之前出现过，且都是真人自己）。
- **r148 已加诊断（`NPlayerHandClickDiagnosticPatch`，只记录不干预）**，下一局按这两个锚点分流：
  - `手牌点击被忽略: reason=…` —— `hand-in-card-play` / `overlay(screenCount=…, top=…)` /
    `hand-mode-none` / `player-actions-disabled` / `extra-turn-other` / `no-card-node` …
    （`hand-in-card-play` 说明手牌节点卡在"正在出牌"状态 → 方向是给 `RefreshCombatUiForControlledPlayer`
    补一条"长时间 InCardPlay 强制收口"）
  - `手牌点击已受理但出不了牌: card=…, reason=<UnplayableReason>, preventer=…` —— 说明点中了但
    `CanPlay` 不通过（能量不足 / 无合法目标等），原版只会把牌拖回去，属"预期行为"，不算 bug。
- **r148 实机首测（2026-09-25 17:30，marker r148，已归档 `logs-archive/godot__20260925-173038__r148.log`）：未复现**。
  - 新一局（floor 0，3 席）：真人 **点牌 9 次 / 开打 9 次（1:1，全成功）**，`让真人插队` 0 次（本次不需要）；
    时间线正常：瓦库在飞的那张结算完（L9724 `瓦库出牌走动作队列完成 … BASH`）→ 真人 L9740 立刻开打。
  - 埋点自证有效：`手牌点击被忽略` **0 条**（= 没有任何一次点击被闸门吃掉）；
    唯一命中 `手牌点击已受理但出不了牌: card=SURVIVOR, reason=EnergyCostTooHigh` ⇒ **正常行为**
    （费用不够，原版只把牌拖回手牌）—— 说明这个埋点能把"真·被吞"和"本来就出不了"分开。
  - ⚠ 测试强度：本次只打了 **1 场战斗（3 个回合）** 就退出；r147 那次现象出现在"打到中段、机器人被打死之后"
    ⇒ "未复现"是**弱信号**。埋点零副作用（只在真被忽略/真出不了时才打，10 秒节流），建议**保留**观察。
- **r148 强化复测（2026-09-25 22:33，marker r148，已归档 `logs-archive/godot__20260925-223324__r148.log`）：仍未复现**。
  - Bot 本局触发死亡处理 **3 次**（L9081 / L12643 / L15771），覆盖了「Bot 死亡后继续战斗」场景；
    `让真人插队` **5 次**（L9303 / L14201 / L14442 / L14520 / L15548），其中 **4 次在第二次死亡处理之后**。
  - `手牌点击被忽略` **0 条**；仅 2 条 `手牌点击已受理但出不了牌`，均为正常的 `EnergyCostTooHigh`
    （L10415 `DEFEND_SILENT` / L15021 `STRIKE_SILENT`）。本局 `Parent node is busy` / `幽灵弹层已自愈` 也均为 0。
  - 同局商店 R1 顺利通过（Bot 完成删牌服务与药水购买、ACK 旁路两次命中、最终 `done=1` 并正常离店）。
- **结论 / 下一步**：两次测试（首测 + 本次强化复测）都未复现，且本次插队链有实际成功证据；作为**更强负样本**
  记录，但不据此宣称根因已消失。保留 r148 零副作用诊断，等自然复现时读取 `手牌点击被忽略: reason=…`；
  若长期 0 命中，再评估降级为 `-` 或设置页开关。

### BUG-19 瓦库出牌期间，真人打「奖励式三选一」类卡牌被瓦库替答（2026-09-26 用户反馈；**r154 已修 → ✅ 2026-09-26 实机确认，关单**）

- **现象（用户原话）**：「瓦库打牌时我打出自己的**类猪体**（猪猪 mod / YuWanCard 的牌），瓦库会替我选牌；
  瓦库打完了我再打就不会出现这个 bug」。
- **定性（先量后猜 + 反编译对照，证据链完整）**：
  1. 归档 `logs-archive/godot__20260926-203939__r153.log`：真人 `Player …326 playing card YUWANCARD-LEI_ZHU_TI`（L12584）
     → 12 行后 `Player …326 chose cards [YUWANCARD-PIG_STAY_UP_LATE]`（L12596），
     **中间没有任何选牌日志**（全文 `检测到真人选牌请求` = **0**、`选牌选择器按归属分发` = **0**）；
  2. 同一时刻的看门狗统计（L12595）写着
     `selectorStackCount=1, selectorStackTop=LocalWakuuStrategySelector` ⇒ **瓦库的托管选择器正压在全局栈上**；
  3. 启动自检（L1056）`SELECTOR_ROUTE from=13 … legacyFallback=[…,FromSimpleGridForRewards] …` ⇒
     该入口**没有**归属者前缀；
  4. 反编译 `YuWanCard.Content.dll` 的 `YuWanCard.Cards.Event.LeiZhuTi.OnPlay` ⇒ 选牌走
     **`CardSelectCmd.FromSimpleGridForRewards`**（3 张候选挑 1 张，再给一张免费复制）。
- **根因**：`CardSelectForegroundSwitchPatch` 的"写选牌归属者"前缀**只补了 `FromSimpleGrid`**，
  漏了同族的 `FromSimpleGridForRewards`（**两个不同方法**）⇒ 该入口选牌时 `CurrentChoicePlayerId` 为空
  ⇒ 选择器守卫落回 `WakuuSelectorDispatch` 的 **`KeepTop`（"信息不足一律不动"）**
  ⇒ **栈顶的瓦库选择器替真人作答**（且这条分支一条日志都不打）。
  瓦库打完 ⇒ 选择器栈已被清空 ⇒ 守卫 `top == null` 直接返回 ⇒ 真人看到正常选牌界面
  ⇒ 正好解释用户观察到的"只在瓦库打牌时出现"。
- **修法（r154）**：补 `[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGridForRewards))]`
  前缀写归属者（与其余 6 个入口同款）；`WakuuSelectorRouteAudit` 分类同步（ownerAware 6 → 7）；
  **新增单测哨兵**「归属者清单必须与补丁前缀一一对应」（反射核对，防"清单说已接入、实际漏补丁"再发生）；
  `WakuuSelectorDispatch` 类注释补上这条软肋的警示。
- **同类残留（本轮**未动**，留档）**：`FromDeckForUpgrade` / `FromDeckForTransformation` /
  `FromDeckForEnchantment` / `FromDeckForRemoval` / `FromDeckGeneric` 仍是 `legacyFallback`
  ⇒ 理论上同样会被栈上的瓦库选择器抢答。未动原因：它们有人工兜底链（`本地多控下强制牌组选牌弹出背包`），
  且牵涉火堆/商店/Co-op Bots 的既有链路（CB 的 deck edit 就走 `FromDeckForTransformation`），
  需要单独取证 + 回归。**复现任一"真人选牌被替答"再按本条修法接入。**
- **验证契约（请实机复测）**：
  ```
  改动:        选牌归属者漏补 FromSimpleGridForRewards（r154 / BUG-19）
  EXPECTED:    瓦库出牌期间真人打「类猪体」等三选一卡 → 真人自己选；日志出现"检测到真人选牌请求"
  SETUP:       本地多控（瓦库在场上）+ 装 YuWanCard；**建议把「瓦库并发出牌」保持默认开**（复现前提）
  ACTION:      1. 让瓦库开始连续出牌 2. 瓦库还没打完时，真人打出「类猪体」
  OBSERVE:     是否弹出三选一界面由真人点；日志 [LocalMultiControl] 行
  PASS:        真人能自己选；日志有 `检测到真人选牌请求，本次跳过瓦库选择器改走正常UI: chooser=<真人id>`
  FAIL:        仍被自动选掉（没有任何选牌界面）；或该行 chooser 是瓦库 id
  LOG ANCHORS: INIT_OK / SELECTOR_ROUTE / 检测到真人选牌请求 / chose cards
  ```
- **回归要求**：瓦库自己打同类卡（三选一）仍应**自动作答**（它的选择器照常生效）；
  真人普通选牌（手牌/弃牌/战斗堆）行为不变；`瓦库选择器作用域异常退出` 仍为 0。
- ✅ **2026-09-26 实机确认通过（marker r154，归档 `logs-archive/godot__20260926-211038__r154.log`）**：
  - 修复点实证：真人打「类猪体」时出现 `自动切前台延后（…）: source=combat-choice-FromSimpleGridForRewards`（新补的前缀生效）
    → 游戏自己 `Pausing action … for player choice`→`paused execution` ⇒ **走"等人选"的 UI 路径**；
    全文 **9 条** `检测到真人选牌请求…: chooser=…326`（**修前恒 0**，chooser 全为真人）；
    另有 `弹层阻挡自动流程: top=NSimpleCardSelectScreen[inTree=True]` = 真人的选牌界面确实在栈上、瓦库自动出牌被正确挡住。
  - 回归：9 条里**没有一条** chooser 是瓦库 ⇒ 瓦库自己的同类选牌仍自动作答；
    `选择器作用域异常退出`/看门狗重启失败/`Couldn't get hand node`/队列空引用/`保留为人工领取` **全 0**。
  - ⚠ **口径订正**：`检测到真人选牌请求` 自此**不再是"期望 0"**（它是修好后的正常锚点）；
    `tools/log_scan.py` 的 health 预设已同步，并把"chooser 不是真人 ⇒ 归属者写错"写成判据。**本节关单。**

### BUG-20 游戏结束后结算页没有结束按钮（2026-09-27 用户反馈；**r155 已修 → 待实机确认**）

- **现象（用户原话）**：「游戏结束后没有结束按钮」—— 结算第一页（战绩页）的「继续」按钮还在，
  点进第二页（战绩明细 / 徽章页）后**没有「返回主菜单」按钮，卡死出不去**。
- **定性（先量后猜 + 反编译 + 存档三方对照，证据链完整；归档 `logs-archive/godot__20260927-113416__r154.log`）**：
  1. `[WARN] Local player with net id 1 not found in run! Progress will not be updated`（L73782）——
     紧接着 `Saved run history`（L73791），随后就是本局的结算动画；
  2. `[ERROR] KeyNotFoundException: The given key 'CHARACTER.WTW_CHARACTER_GOJO_SATORU' was not present
     in the dictionary`（L73852），栈顶 `NGameOverScreen.SaveBadgesToProgress ← AnimateBadges ← AnimateRunSummary`；
     主玩家 326 的角色就是 wtw 五条悟（L24065 / L73301 `character=WTW_CHARACTER_GOJO_SATORU`）；
  3. 存档核对 `modded\profile2\saves\progress.save`：`character_stats` **22 条里有 LEX_NINJA2 / WINE_FOX /
     KOISHI / PIG / SLUGCAT / SAKUYA 等一堆 mod 角色，唯独没有 `CHARACTER.WTW_CHARACTER_GOJO_SATORU`**。
- **根因（两层，第一层是我们的）**：
  1. **进度整局不写入（我们的锅）**：本 mod 的回环 host 服务
     `LocalLoopbackHostGameService.Platform => PlatformType.None`，而 run 存档的 `platform_type` 取自
     `RunManager.ToSave()` 的 `NetService.Platform` ⇒ 也是 `None`；`None` 平台的本地玩家 id 是占位值
     **1**（游戏 `NullPlatformUtilStrategy.LocalPlayerId = 1`），而 run 里的玩家 NetId 是本机 Steam ID
     ⇒ `ProgressSaveManager.UpdateWithRunData`（`ProgressSaveManager.cs:204-210`）里
     `FirstOrDefault(p => p.NetId == 1)` 落空 ⇒ **直接 return，本局胜场/败场/时长/卡牌与遗物统计/
     epoch 解锁一条都不写**（用户本局是 Act4 通关也一样没记）；
  2. **结算页崩在缺角色统计上（游戏对 mod 角色不健壮 + 第一层的连带）**：
     `NGameOverScreen.SaveBadgesToProgress` 用**索引器**取 `Progress.CharacterStats[_localPlayer.Character.Id]`
     （`NGameOverScreen.cs:407`，游戏在同文件里明明有 `GetOrCreateCharacterStats` 可用却没用）⇒
     新角色的条目从没被创建（因为第一层）⇒ 抛 `KeyNotFoundException` ⇒ `AnimateRunSummary` 在
     `_mainMenuButton.Visible/Enable`（`NGameOverScreen.cs:350-351`）**之前**中断 ⇒ 第二页没有按钮。
- **修法（r155，两层）**：
  1. `ProgressSaveManagerUpdateWithRunDataPatch`（Core 域，新文件 `Scripts/Patch/ProgressSaveManagerPatch.cs`）：
     进 `UpdateWithRunData` 前判定「按 run 记的平台认不到本地玩家、按 `PlatformUtil.PrimaryPlatform` 能认到」
     ⇒ 临时把 `serializableRun.PlatformType` 换成主平台，finalizer 立刻还原（不影响 RunHistory / 每日榜 /
     读档对同一 run 对象的读取）；判据抽成纯函数 `RunProgressLocalPlayerPolicy.Decide`（+6 条单测），
     单人局与正常平台局一律不动。**顺带修好"整局进度不写入"这个更大的隐性损坏。**
  2. `NGameOverScreenSaveBadgesToProgressPatch`（Ui 域，新文件 `Scripts/Patch/NGameOverScreenPatch.cs`）：
     原方法前置兜底 —— 进度里缺该角色统计条目时用游戏自己的 `GetOrCreateCharacterStats` 补建，
     保证索引必中（第三方 mod 角色 / 任何遗漏路径都不会再锁死结算页）。
  3. 两个补丁都登记进 `PatchDomainMap` 与启动自检 `OptionalPatchTargets`，S7 目标基线已刷新
     （179 补丁类 / 203 目标行 / 语义标识 346）。
- **验证契约（请实机复测）**：
  ```
  改动:        整局进度写入按真实平台认本地玩家 + 结算页徽章保存兜底（r155 / BUG-20）
  EXPECTED:    用第三方角色（如 wtw 五条悟）打完一局 → 结算第二页出现「返回主菜单」按钮，能正常退出
  SETUP:       本地多控 2~3 席；主玩家角色 = 任意"进度里还没有 CharacterStats 条目"的角色（新 mod 角色最容易命中）
  ACTION:      1. 打完整局（通关或死亡）2. 进结算第一页，点「继续」看第二页（战绩/徽章）
  OBSERVE:     第二页是否有「返回主菜单」按钮并可点；`progress.save` 的角色统计是否新增该角色
  PASS:        按钮在、能回主菜单；日志有 `整局进度写入已校正本地玩家识别: platform=None -> Steam, netId=76561…`
              且该局不再出现 `Local player with net id 1 not found in run!`
  FAIL:        仍无按钮；或仍出现 `not found in run` / `KeyNotFoundException`
  LOG ANCHORS: INIT_OK / BUILD_ID(marker=2026-09-27-r155) / 整局进度写入已校正本地玩家识别 /
               结算页徽章保存兜底 / Local player with net id 1 not found in run（期望 0）
  ```
- **回归要求**：单人对局（`Players.Count == 1`）与非回环平台对局行为**逐字不变**（补丁 Keep 分支直接放过）；
  结算第一页/`ViewRun`/`ReturnToMainMenu` 按钮行为不变；
  存档里的 `platform_type` 字段**不被改动**（只在 `UpdateWithRunData` 调用期间临时替换并还原）。

### Daily 本地多控（r156 起，r157 修正，r158 零劫持 / r159 收尾 / **r161 断网可玩**；**联网局、断网局均已通过（2026-09-27）**）

提案：`maintenance-docs/decision-records/本地多角色扩展到Daily模式可行性分析.md`（§五 实施建议、§六 结论口径）。

**2026-09-28 r176：席位对齐 / 角色分配入口改为共用实现（R2 收口，行为零变化）**——
只把每日页与自定义页**逐字相同的**「加缺失席位 / 删多余本地席位 / 标 ready」与判定收进
`Scripts/Runtime/PureLogic/LocalLobbySeatPolicy.cs` + `Scripts/Runtime/LocalLobbySeatReconciler.cs`；
**日志文案与 sender 上下文来源串逐字不变**；**角色来源未动**（每日 = 日期种子逐席位驱动 `SetupLobbyParams`；
自定义 = 真人点选后同步 UI）。详见 `§决策记录跟踪表` 的 `runtime架构分层重构评估.md` 行。

**r176 实机验证契约（进每日 / 自定义各一次厅，任一出征一次即可）**：
- 照旧出现：`每日挑战大厅本地人数已同步: target=…, actual=…, added=…, removed=…, readyChanged=…`、
  `自定义模式大厅本地人数已同步: …`、`每日挑战角色已按日期种子分配: seats=…, signature=…`、
  `每日挑战席位卡已补建: added=…`、`通过每日挑战实体按钮调整本地人数成功` / `通过自定义模式实体按钮调整本地人数成功`、`通往…`级别的出征链；
- 席位卡、瓦库勾选框、「切人 / 变瓦库」按钮、人数面板行为与 r175 一致；
- **期望 0**：`### Exception ###`、`add_child() failed`、`NullReferenceException`、以及「席位卡已补建」在人数不变时反复刷；
- 一条命令核对：`python D:\Download\pain\tools\log_scan.py --preset daily <日志>`（健康度另看 `--preset health`）。

**⚠ 已知噪音（别当回归查）**：进每日 / 自定义页时日志里的
`[ERROR] Error deleting path modded/profile2/saves/current_run_mp.save: Failed`（连带 `.backup`，每次进页 2 条，
栈顶 `GodotFileIo.DeleteFile` ← `RunSaveManager.DeleteCurrentMultiplayerRun` ← 我们的入口 `Enter`）
是**游戏侧存档管理器在文件本就不存在时**打的日志，**自 r156 起一直如此**（r172 6 条 / r173 2 条 / r175 0 条，
r175 为 0 只因那局没进厅）；不是我们的异常，本轮也未改动该调用。

**2026-09-27 首轮实机（r156，联网一局）用户反馈与修正（r157）**：
1. ❌ **「不能玩原版多人联机每日游戏」** —— r156 把入口做成了**劫持** `StartHost(GameMode.Daily)`，
   把官方「每日挑战」按钮整个占掉了。**修正：还给玩家** —— 官方 Daily 按钮完全走原版流程，
   本地多角色改走联机菜单新增的第 5 张卡「单人每日挑战」（`LocalDailySelfCoopEntry.Enter`）。
   *教训：Custom 可以劫持（它原本没有正当的官方多人用法），Daily 有（最多 4 人 + 按人数分榜），不能一刀切。*
2. ❌ **「不能选瓦库托管」** —— `LocalRemoteLobbyPlayerSwitchUi.TryGetLobbyScreen` 的页面白名单只有
   `NCharacterSelectScreen` / `NCustomRunScreen`，**每日页不在其中** ⇒ 席位卡上的瓦库勾选框、
   切换席位按钮、左上角「全瓦库」总开关都不显示。**修正：把 `NDailyRunScreen` 纳入白名单。**

**本轮做了**：独立卡片入口（不劫持官方）/ 异步大厅 reconcile（clamp 4）/ 按日期种子分配角色 /
每日榜分数禁止上传 / 每日页人数面板 / 每日页瓦库勾选与切换席位。

**⚠ 2026-09-27 第二轮实机（r157）用户反馈与 r158「零劫持」改造**（用户拍板方案 A：mod 与官方联机并存）：
用户报 4 条：① 每日页**仍没有瓦库勾选框**；② 先点我们的卡再点官方卡，官方页也冒出我们的席位/切人按钮；
③ 单人每日页**切席位角色卡不跟随**（切谁都显示同一角色，进游戏后正常）；
④ 连**原版联机自定义**也玩不了（Custom 劫持自 2026-03-25 起）。
（另有"官方多人每日不联机进不去"—— 用户已澄清是没好友联机的正常现象，**不修**。）
- **根因 ②/④**：mod 仍在**接管**官方入口（Custom 劫持 + r156 的 Daily 劫持是同一类错误），
  且大厅页退回主菜单时会话状态**残留**（`Disable` 只在进局结束 / ESC 重启房间时调）。
- **根因 ①**：每日页 `displayLocalPlayer: false`，而加伪席位时我们把回环 sender 切到该席位 ⇒
  游戏 `NRemoteLobbyPlayerContainer.OnPlayerConnected` 的过滤 `player.id != LocalPlayer.id || _displayLocalPlayer`
  把伪席位当成"本地玩家自己"⇒ **压根不建卡片**（所以 r157 加白名单没用，勾选框的宿主都不存在）。
- **根因 ③**：角色卡由游戏 `InitializeDisplay()` 渲染 `_lobby.LocalPlayer.character`，我们切 sender 后没人触发重绘。
- **r158 修法（零劫持）**：
  1. 官方标准/每日/自定义三张卡**全部还给原版**；本机多角色只走「单人多角色」卡片 + 其下方两个小按钮
     （`本地·自定义模式` / `本地·每日挑战`），入口改为 `LocalCustomSelfCoopEntry` / `LocalDailySelfCoopEntry`（非补丁）。
  2. 新增 `NMultiplayerHostSubmenuOfficialEntryGuardPatch`（点官方入口前清残留会话）
     + `LocalSelfCoopSessionGuard`（未进局且无回环大厅页约 1 秒 ⇒ 自动清理，兜底根治残留）。
  3. 每日页席位稳定后补调 `screen.PlayerConnected(player)` 补建席位卡（瓦库勾选框宿主）。
  4. 每日页 `_Process` 检测 sender 变化后重调 `InitializeDisplay()`（角色卡跟随当前席位）。
- **r158 门禁**：构建 0 警告 0 错误（224 .cs）；**671 单测全绿**；`static_checks` 8 PASS
  （S7：187 补丁类 / 211 目标行 / 198 字符串目标 / 语义标识 359）；`preflight -Deploy` 4 PASS / 3 SKIP；
  `dll_check --deployed --marker 2026-09-27-r158` 全绿（`LocalCustomSelfCoopEntry` / `LocalDailySelfCoopEntry` /
  `NMultiplayerHostSubmenuOfficialEntryGuardPatch` / `LocalSelfCoopSessionGuard` 在；
  `NMultiplayerHostSubmenuCustomRunPatch` 与 `NMultiplayerHostSubmenuDailyRunPatch` **均已消失**）。
- **r159（2026-09-27，r158 日志复查后的收尾修复）**：日志 `godot__20260927-151337__r158.log` 里发现
  `NCustomRunScreen._Process` **每帧抛 `NullReferenceException`**（栈顶 `NCustomRunScreenLocalPlayersPatch.TryReconcileLocalPlayers`）
  —— `screen.Lobby`（`_lobby`）在「未建厅 / 已被清理」时为 null，而代码直接访问 `lobby.NetService`。
  修法：Custom reconcile 与 Custom 出征守卫全部改成 `lobby?.NetService is ...` 判空；
  并给**三个大厅页的人数面板加严**（`本地多控开启 && 本页大厅就是我们的回环服务` 才挂），
  官方联机（自定义/每日/标准）页面不再出现我们的席位/切人按钮。
  r159 门禁：构建 0 警告 0 错误；**671 单测全绿**；`static_checks` 8 PASS（S7：187 补丁类 / 211 目标行 /
  199 字符串目标 / 语义标识 359）；`preflight -Deploy` 4 PASS / 3 SKIP；
  `dll_check --deployed --marker 2026-09-27-r159` 字节一致（sha256 `67af4000b36e…`）。
- **r158 实机结论（第三轮，用户「没什么问题」）**：日志 `godot__20260927-151337__r158.log`（44229 行，INIT_OK）验证契约全过 ——
  `联机菜单卡片已重排: count=4, cardWidth=330, gap=26, startX=261`；
  三个入口（单人多角色 / 本地·自定义模式 / 本地·每日挑战）都走通；
  `进入官方联机入口前已清理残留的本地多控会话: gameMode=Daily`（清理生效）；
  `每日挑战席位卡已补建: added=3`（瓦库勾选框宿主已生成）；
  `每日挑战角色已按日期种子分配: seats=326:WINE_FOX, 327:WATCHER, 328:PIG, 329:REGENT`（各席位角色不同）；
  `本地多控会话已自动结束：当前没有本地多角色大厅页面，且未进局`（守卫多次生效 ⇒ 残留根治）。
- **r159 联网一局实机通过（2026-09-27，marker r159）**：留档日志 `logs-archive/godot__20260927-154423__r159.log`
  （3.84 MB / 33291 行，`INIT_OK`、`PATCH_RESULT critical=25/25 optional=15/15`、`COMPAT_RESULT PASS`）。
  一次启动里进厅 7 次（每日 ×4 / 自定义 ×3，前几次"进厅即退"均留下会话自动结束），**出征 2 局**：
  局 1 = 用户所指"联网一局"（2 席 WineFox + Watcher、`Seed: 27_09_2026_2P`、**第一幕阵亡 `result=loss`**）；
  局 2 = 出征后主动弃局（`run was abandoned`）。契约核对结果：
  ②`联机菜单卡片已重排: count=4`+小按钮注入 ✅ ③大厅人数 `target=actual=2/3`（10 条）✅ ④席位卡 `added=1`（7 条）✅
  ⑤角色按日期种子分配（11 条），signature 同日同人数可复现（2 席 `1404169136`、3 席 `1026103217`）✅
  ⑥出征正常开局、`Random character is not currently allowed in daily` 0 ✅ ⑦`DAILY_SCORE_SKIP` ×2 且无真实上传 ✅
  ⑧会话自动结束 ×5 + `RunManager.CleanUp` 清理 ✅；①官方入口本份日志无守卫行（该行**条件打印**，不作为判据）。
  顺带：`整局进度写入已校正本地玩家识别` ×2（r155 在 Daily 路径同样生效）、`Local player with net id 1 not found` 0 条。
  ⚠ **新记录的设计事实**：大厅种子 `27_09_2026_**1P**`、出征种子 `27_09_2026_**2P**`
  ⇒ 每日种子带人数后缀，本机 2 人多控**不是**官方 1P 每日那张图。
  ⚠ **新发现缺陷：BUG-21**（`LocalCustomRunSelectionSync.TrySync` 漏判空 ⇒ 本局 981 次 NRE，详见 §BUG-21）。
  ⚠ **新观察（游戏侧）**：3 条 `NDailyRunLeaderboard` 的 `ObjectDisposedException`（本份首现），
  待用官方单机每日对照定性（见 §BUG-21 末尾）。
- **r159 断网一局已试（2026-09-27，marker r159）**：留档 `logs-archive/godot__20260927-160114__r159.log`
  （1.47 MB / 13460 行，`INIT_OK`）。用户三条现象 → 逐条定性：
  ①「官方多人每日点不进去」= **正常**（离线无 Steam 联机会话）。
  ②「本地多人每日能进页，但只有单人、没有加人按钮」= **我方链路离线完全没跑起来**（可修，本轮不修）：
  `每日挑战大厅本地人数已同步` / `席位卡已补建` / `人数面板已注入` / `角色已按日期种子分配` **全 0**
  （对照联网局 10 / 7 / 3 / 11），每次都是 `席位上限 4 → 模式已启用 → 本地多控会话已自动结束 → 模式已关闭: no-local-lobby-screen`。
  根因链：断网 ⇒ `NDailyRunScreen.GetTimeServerTime()` 打 `time.megacrit.com:443` DNS 失败并重试
  （`Gave up trying to retrieve server time` ×5 / `Couldn't retrieve time from time server, using local time` ×5）
  ⇒ 每日大厅迟迟不就绪 ⇒ 我们「反射 `_lobby`、**就绪才动手**」的 reconcile 永不触发
  ⇒ 且 `LocalSelfCoopSessionGuard` 约 1 秒即判「无回环大厅页 + 未进局」清会话。
  **可选修法**：①guard 把「当前停在每日页」视为在流程中、不按 1 秒清理；②或入口不等时间服务器也能建厅。
  **用户判定「断网玩不了每日属正常现象」⇒ 当时记为已知限制**，随后用户 2026-09-27 拍板做掉它
  ⇒ **r161 已按方案 ① 修**：`LocalSelfCoopContext.ActiveSelfCoopLobbyScreen`（入口 push 页面时记录）
  + 会话守卫新增「页面开着（`IsInsideTree() && Visible`）即算在流程中」判据
  （纯逻辑 `LocalSelfCoopLobbyScreenPolicy` + 4 单测；`Visible` 判据不可省，否则退回 r158 的会话残留问题）。
  ✅ **2026-09-27 断网局实机通过**（`logs-archive/godot__20260927-161701__r161.log`）：
  时间服务器照旧失败（游戏回落本地时间）的前提下，`每日挑战大厅本地人数已同步: target=2, actual=2, added=1`
  → `每日挑战席位卡已补建: added=1` → `每日挑战人数面板已注入` → 角色按日期种子分配（signature `1404169136`，
  与联网局同值）→ `Embarking on a DAILY multiplayer run … Seed: 27_09_2026_2P` + `本地回环 Lobby 开局流程完成，玩家数=2`
  ⇒ **能出征、能打完、能结算**（`DAILY_SCORE_SKIP`）；且 `本地多控会话已自动结束` **0 条**（修前 4 条）。
  **用户口述确认**：「断网能进本地多人每日」。
- **r161 联网复测通过（2026-09-27）**：留档 `logs-archive/godot__20260927-163842__r161.log`
  （4.40 MB / 32675 行，`INIT_OK`、`COMPAT_RESULT PASS`）。本次用户**直接开 4 席**：
  `每日挑战大厅本地人数已同步: target=2 → 3 → 4`、`席位卡已补建: added=1` ×3、`人数面板已注入`、
  `角色已按日期种子分配` ×4（各席位角色互不相同）→ `Embarking on a DAILY multiplayer run`（4 人，`Seed: 27_09_2026_4P`）
  + `本地回环 Lobby 开局流程完成，玩家数=4` → 打完一局（`result=loss`）→ `DAILY_SCORE_SKIP`（4 players）
  ⇒ **4 席路径首次实测全通**（此前联网局都只开 2 席）。联网侧 `LocalCustomRunSelectionSync` / `### Exception ###` **均 0 条**
  ⇒ **BUG-21 两侧都归零**。回归面 `收回滞留节点` / `幽灵弹层已自愈` / `add_child() failed` 全 0。
- ✅ **r161 反面判据也已实机验证（2026-09-27，进每日大厅后不进游戏直接返回）**：
  留档 `logs-archive/godot__20260927-164433__r161.log`（8005 行，`INIT_OK`）。两次进「本地·每日挑战」都先正常建厅
  （`Successfully queried time server` + 席位卡/人数面板/角色分配齐全），随后**直接返回主菜单**时：
  `本地回环网络断开: reason=Quit` → **`本地多控会话已自动结束：当前没有本地多角色大厅页面，且未进局`** →
  `本地多控模式已关闭，原因: no-local-lobby-screen`（×2 各一组）⇒ **页面被 `Pop`（`Visible=false`）后照旧清理，
  没有退回 r158 的会话残留**；`LocalCustomRunSelectionSync` / `### Exception ###` / `ObjectDisposedException` 均 0 条，
  `[ERROR]` 7 条全是既有项（Manosaba/ddu ×2、BetterModMenu ×1、游戏侧删 `current_run_mp.save(.backup)` ×4）⇒ **我方 0 条**。
  ③「官方单机每日能开始但黑屏」= **不是断网正常现象，是第三方 YuWanCard 在开局链抛异常**：
  `Embarking on a DAILY … 1 players` 之后紧跟
  `[ERROR] Exception starting daily singleplayer run : System.InvalidOperationException: Local player not found in player collection.`
  栈 = `LocalContext.GetMe ← YuWanCard.Utils.CloudAnalyticsService.TryRegisterRunStart ← OnRunStarted ← RunState.CreateForNewRun_Patch6`
  ⇒ 局建不出来 ⇒ 黑屏；**栈里我方帧 0 条**。
  ✅ **已定性为偶发（2026-09-27 完成）**：后续 **3 次**官方单机每日（2 次离线 + 1 次联网）**全部正常开局**，
  `Local player not found in player collection` 均 0 条 —— 其中离线那次跑的还是 **r159 旧 dll**
  （`godot2026-09-27T16.12.57__…__r159.log`），联网那次见 `godot__20260927-163842__r161.log`
  （`Successfully queried time server` + `Embarking on a DAILY … 1 players`，无异常）
  ⇒ **4 次里只第一次黑屏 ⇒ 偶发，既非"断网必现"、也非 r161 修好的**。
  若再现：按 `maintenance-docs/references/thirdparty-mod-conflicts.md` 定性后**另开独立补丁 mod**（主仓库不动）。
  另保留推测：该异常可能与 r155 同源（`PlatformType.None` 下本地玩家 id 是占位 `1`，日志 `[StartRunLobby (1)]`），待更多样本区分。
  回归面：BUG-21 本局复现 **304** 次（联网局 981；**r161 已修**，断网复测 0 条）。
  ⏳ **仍待**：①现象 ③ 的在线对照与更多样本（离线本地每日 guard 放宽**已由 r161 落地并实机通过**）。
**本轮没做**：① 超过 4 席（游戏 `StartRunLobby._maxPlayers` 是 readonly，只能 ≤4）；
② 「只让 primary 上传 1p 榜」增强（提案备选方案 2，仍搁置）；③ 每日页 UI 精修（面板位置先放左侧中部，实机若遮挡再挪）；
④ 每日读档重连页（`NDailyRunLoadScreen`）未单独适配。

实现（3 个新文件 + 4 处扩展）：
- `Scripts/Patch/NDailyRunLocalSelfCoopPatch.cs`：`LocalDailySelfCoopEntry`（**独立入口**，由联机菜单第 5 张卡调用，
  不是补丁类）、`NDailyRunScreenLocalPlayers{Open,Process,Close}Patch`（席位 reconcile + 角色分配，Lobby 域）、
  `NDailyRunEmbarkGuardPatch`（出征前强制校正席位/角色/sender，Lobby 域）、
  `NDailyRunLocalCountButtons{Open,Process,Close}Patch` + `LocalDailyRunCountButtons`（Ui 域）。
- `Scripts/Patch/NMultiplayerHostSubmenuPatch.cs`（扩展）：注入第 5 张卡「单人每日挑战」；排列改为
  5 张一行自适应间距（6~26px）+ 重排日志；**官方 Daily 按钮不再被劫持**。
- `Scripts/Patch/NRemoteLobbyPlayerSwitchPatch.cs`（扩展）：页面白名单加 `NDailyRunScreen` ⇒
  每日页恢复「瓦库托管」勾选框 / 切换编辑席位按钮 / 左上角「全瓦库」总开关。
- `Scripts/Patch/DailyRunUtilityPatch.cs`：`DailyRunUtilityUploadScorePatch`（Core 域）。
- `Scripts/Runtime/PureLogic/DailyLobbyPolicy.cs`：判定纯函数（要不要 reconcile / 席位指纹 / clamp 4）。
- `Scripts/Runtime/LocalSelfCoopContext.cs`：新增 `LobbyLocalPlayerLimit`（页面级席位上限）+
  `Set/ResetLobbyLocalPlayerLimit`；`AdjustDesiredLocalPlayerCount` 的上限改取「全局 12」与「页面上限」的较小值
  （默认行为不变，只有进 Daily 页才被收到 4）。

日志锚点（实机核对用）：`单人多角色每日挑战入口：改走本地回环开局` /
`联机菜单卡片已重排: count=5, cardWidth=…, gap=…, startX=…, viewport=…` /
`每日挑战大厅本地人数已同步: target=…, actual=…, added=…, removed=…, readyChanged=…` /
`每日挑战角色已按日期种子分配: seats=<id:角色>…, signature=…` / `每日挑战人数面板已注入: viewport=…, position=…` /
`DAILY_SCORE_SKIP 本地多控每日局跳过排行榜分数上传: …`。

已知风险（实机重点看）：
1. **时间服务器**：每日大厅要 await 时间服务器；断网时回落本地时间（流程可用，但当日关卡可能与官方不一致）。
2. **角色分配依赖大厅顺序**：游戏按 `lobby.Players` 顺序 roll 角色；若某席位 sender 切换失败或不在大厅，
   分配会整体跳过并打 WARN（保持游戏默认角色，不崩）—— 实机若看到 WARN 请回报。
3. **reconcile 幂等性**：`_Process` 每帧调用，判定为「无变化即返回」；若看到同步日志每帧刷屏 = 判定失效，需回报。
4. **出征就绪**：非主席位的 ready 由我们补齐；若出征点不动，先看是否卡在 `IsAboutToBeginGame()` 之前。

验证契约（请实机两局：联网 / 断网各一）：
```
改动:        Daily 本地多控 + 零劫持改造（r158，《本地多角色扩展到Daily模式可行性分析》§五）
EXPECTED:    ① 官方「标准 / 每日 / 自定义」三张卡**全部是原版行为**（联机每日仍要真好友才能开始，属正常）；
             ② 官方卡那一行最左是我们的「单人多角色」卡；该行下方居中有一行小按钮
                （`本地·自定义模式` / `本地·每日挑战`），点它们才进本机多角色；
             ③ 每日大厅里**能看到席位卡并勾「瓦库托管」**（左上角另有「全瓦库」总开关）；
             ④ 用 ◀/▶ 切席位时，中央角色卡**跟着变**；2~4 个席位角色各不同、能直接出征；
             ⑤ 打完一局正常结算且不上传每日榜
SETUP:       本地多控；首局联网（拿官方每日关卡），第二局可断网复测回落路径
ACTION:      1. 主菜单「联机」→ 确认 4 张大卡 + 下方 2 个小按钮（不重叠）
             2. 点官方「每日挑战」→ 进原版多人联机每日 → 返回（验证 ①）
             3. 点官方「自定义」→ 进原版自定义多人 → 返回（验证 ①）
             4. 点「本地·每日挑战」→ 面板调到 3~4 人 → 看席位卡 + 勾「瓦库托管」（或「全瓦库」）
             5. 用 ◀/▶ 切席位，观察中央角色卡是否跟随；出征 → 打完一局 → 回主菜单
             6. 回主菜单后（**不要**点任何官方入口）等 2 秒，日志应出现会话自动结束
OBSERVE:     卡片/按钮布局；大厅日志的席位数 / 角色 / 瓦库勾选 / 会话清理 / 分数锚点
PASS:        ① 官方「每日挑战」「自定义」都进原版联机界面（日志**没有**我们的入口行）
             ② 点「本地·每日挑战」后日志有「单人多角色每日挑战入口：改走本地回环开局」+「联机菜单卡片已重排: count=4」
             ③ 「每日挑战大厅本地人数已同步: target=N, actual=N」且 N == 面板人数
             ④ 有「每日挑战席位卡已补建: added=…」（= 瓦库勾选框的宿主存在了）
             ⑤ 「每日挑战角色已按日期种子分配: seats=…」每席位角色非空；切席位时角色卡跟随变化
             ⑥ 出征后能正常开局，无 "Random character is not currently allowed in daily!"
             ⑦ 结束局有 `DAILY_SCORE_SKIP`，且没有真实上传
             ⑧ 从我们的大厅页退回主菜单后，日志出现「本地多控会话已自动结束：当前没有本地多角色大厅页面」（守卫生效）
FAIL:        官方入口仍进本地多角色（劫持没还回去）／小按钮缺失或与卡片重叠／
             席位卡不出现（瓦库勾不上）／切席位角色卡不变／席位不足或角色为空／
             出征卡在大厅／出现随机角色异常／出现真实上传／回主菜单后日志无会话清理（残留）
LOG ANCHORS: INIT_OK / BUILD_ID(marker=2026-09-27-r158) / 联机菜单卡片已重排 / 单人多角色每日挑战入口 /
             每日挑战大厅本地人数已同步 / 每日挑战席位卡已补建 / 每日挑战角色已按日期种子分配 /
             本地多控会话已自动结束 / 进入官方联机入口前已清理残留的本地多控会话 / DAILY_SCORE_SKIP
```
回归要求：Standard / Custom 两档入口与人数上限行为**逐字不变**（页面上限默认 12，只有进 Daily 才收 4）；
`LobbyLocalPlayerLimit` 在离开每日页时恢复；非回环 NetService 一律放行分数上传。

### BUG-21 Custom 页 `_Process` 每帧 NRE（`LocalCustomRunSelectionSync.TrySync` 漏判空）（2026-09-27 r159 实机日志发现；**r161 已修 → ✅ 2026-09-27 断网局实机确认，关单**）

问题：r159 联网一局日志 `logs-archive/godot__20260927-154423__r159.log` 里，我方代码抛
**981 条 `System.NullReferenceException`**（`### Exception ###` 915 条），栈顶恒为
`LocalMultiControl.Scripts.Patch.LocalCustomRunSelectionSync.TrySync(NCustomRunScreen screen)`，
调用链是 `NCustomRunScreen.InvokeGodotClassMethod`（`_Process` / `OnSubmenuOpened` / `PlayerChanged` 三个 postfix 之一）。
**无功能阻塞**（用户体感"没什么问题"），但它在日志里淹掉真异常，属每帧级噪音。

根因（已定位到行）：`Scripts/Patch/NCustomRunLocalSelfCoopPatch.cs:411-412`

```
StartRunLobby lobby = screen.Lobby;
if (lobby.NetService is not LocalLoopbackHostGameService)
```

只判了 `NetService`，**没判 `screen.Lobby` 本身为 null**。主菜单里 `NCustomRunScreen` 是常驻子屏，
「未建厅 / 大厅已被清理」时 `_lobby` 为 null ⇒ `_Process` 每帧 NRE。

与 r159 的关系：r159 修的正是**同一类**问题（`screen.Lobby` 未判空），4 个访问点补了 3 个
（`NCustomRunScreenLocalPlayersPatch.TryReconcileLocalPlayers` L89-90 / `NCustomRunEmbarkGuardPatch` L200 /
`LocalCustomRunCountButtons.Sync` L258），**漏掉第 4 处**。

跨会话对照（说明为什么之前没看见）：

| 日志 | marker | `TryReconcileLocalPlayers` 命中 | `LocalCustomRunSelectionSync` 命中 |
|---|---|---|---|
| `godot__20260927-143525__r157.log` | r157 | 3803 | 0 |
| `godot__20260927-151337__r158.log` | r158 | 1917 | 0 |
| `godot2026-09-27T15.37.04__20260927-153704__r159.log` | r159 | 0 | 57 |
| `godot__20260927-154423__r159.log` | r159 | 0 | 981 |

⇒ 与「同一 `_Process` 上更早注册的 postfix 抛异常会中断后面的 postfix」一致（r159 判空后 `TrySync` 才第一次真正跑起来）。
**该机制是推断，本轮未单独做实验验证。**

修法（**r161 已落地**，1 行判空）：

```
StartRunLobby? lobby = screen.Lobby;
if (lobby?.NetService is not LocalLoopbackHostGameService)
```

**全仓库同类扫描结果（r161 一并做完，防止第 5 处漏网）**：`grep Lobby\.NetService` 共 11 处 ——
`LocalCustomRunSelectionSync.TrySync`（**本次修**）；
`NDailyRunLocalSelfCoopPatch` L137 / L234 / L495、`LocalSelfCoopSessionGuard` L129 均为
`is not StartRunLobby lobby` / `?.` 已过滤 null ✅；`NMultiplayerLoadGameScreenPatch` L21 用 `runLobby?.NetService` ✅；
其余 `.NetService` 命中都是 `__instance` / `RunManager.Instance` / `LocalSelfCoopContext` 这类非空实例属性，不涉及。
⇒ 现在**没有**"`.Lobby` 之后直接点 `.NetService`"的裸访问点了。

**r161 门禁（全实跑）**：构建 0 警告 0 错误（**225 .cs**）、**675 单测全绿**（671 → +4，`LocalSelfCoopLobbyScreenPolicyTests`）、
`static_checks` **9 PASS / 0 WARN / 0 FAIL**（S7 基线不变 = 187 补丁类 / 211 目标行 / 199 字符串目标 / 359 语义标识）、
`clr_compat` PASS、`preflight -Deploy` **4 PASS / 3 SKIP**、
`dll_check --deployed --marker 2026-09-27-r161` 字节一致（sha256 `63aab3b00bb7…`，含新标识
`LocalSelfCoopLobbyScreenPolicy` / `ActiveSelfCoopLobbyScreen`、无 `__runOriginal`）。

**✅ 实机确认（2026-09-27 断网局，marker r161）**：`logs-archive/godot__20260927-161701__r161.log` 里
`LocalCustomRunSelectionSync` 与 `### Exception ###` **均 0 条**（修前联网局 981 / 断网局 304）⇒ **关单**。

验证契约：

```
改动:        BUG-21 —— LocalCustomRunSelectionSync.TrySync 补 lobby 判空
EXPECTED:    `LocalCustomRunSelectionSync.TrySync` 在 godot.log 里 0 命中（含 Custom / Daily 两条流程）
SETUP:       本地多控；① 进「本地·自定义模式」大厅 → 退出到主菜单 ② 再进「本地·每日挑战」出征一局
ACTION:      按 SETUP 走完，回主菜单停 3 秒后关游戏
OBSERVE:     log_scan.py --kw LocalCustomRunSelectionSync --kw "### Exception ###" --file <godot.log>
PASS:        两条计数均为 0；INIT_OK；无新增 [ERROR]
FAIL:        仍有 NRE（判空位置不对，或还有第 5 处访问点）
LOG ANCHORS: (期望 0) LocalCustomRunSelectionSync / ### Exception ###；INIT_OK
```

**同日志的新观察（游戏侧，待定性）**：3 条 `ObjectDisposedException`
（`NDailyRunLeaderboard.QueryFriendScores ← LoadLeaderboard ← TaskHelper.LogTaskExceptions`，
被释放对象 `MegaText.MegaLabel` / `NDailyRunScoreWarning`）。跨会话核对 r156 / r157 / r158 / r159 短会话均 **0** 条，
本份**首现**。判据：用**官方单机每日**打一局对照 —— 同样出现 ⇒ 游戏既有问题（不修）；
只在本地多控下出现 ⇒ 再查是否与 `DAILY_SCORE_SKIP` 跳过上传导致排行榜查询链空转有关。

### 维护：经验固化 + 防回归门禁（r160，2026-09-27）

问题：「零劫持」是一条**跨会话必须遵守**的约定，但此前只写在 agent 记忆（`.codebuddy/.../memory`，换 harness 读不到）。
用户要求：**写进 references（仓库侧，任何 harness 都能读）**，并给这类约定配上门禁与小工具。

- **新增参考文档** `maintenance-docs/references/official-entry-coexistence.md`（权威源）：
  零劫持规则、为什么（Custom 2026-03-25 / Daily r156 两次踩坑）、正确做法模板（自注入入口 + 会话生命周期三条铁律 +
  UI 注入加严判据）、症状→病因→修法表（席位卡缺失 / 每帧 NRE / 角色卡不跟随 …）、验证锚点、配套文件表。
  已同步 skill 侧副本（`references/` 两侧 10 份逐字节一致）。
- **AGENTS.md §1 加硬约束**：官方联机入口不得劫持；离开大厅页必须清理会话；指向上述参考文档与 S9 门禁。
- **门禁 S9（离线、防回归）**：`Scripts/Tools/static_checks.py` 新增「官方入口劫持检查」——
  扫 `Scripts/**/*.cs`，只要 `NMultiplayerHostSubmenu.StartHost` / `OnStandardPressed` / `OnDailyPressed` /
  `OnCustomPressed` 上出现「前缀 `return false`」即 FAIL；放行式补丁（如官方入口会话清理守卫）允许并列入备注。
  可选扩展清单 `Scripts/Tools/official_entries.txt`。离线静态层自此 **9 项（S1~S9）**。
  自测：临时造一个劫持式假补丁 ⇒ S9 `FAIL` ✓；真实仓库 ⇒ `PASS`（并列出"官方入口补丁均为放行式"）✓。
- **小工具** `D:\Download\pain\tools\sync_references.py`：references 两侧
  （仓库 `maintenance-docs/references` ↔ skill 侧副本）比对与同步；`--check` 不一致退出码 1（可进门禁）、
  默认「仓库 → skill」、`--reverse` 反向。本轮用它发现并修掉一处历史漂移
  （`local-multicontrol-pitfalls.md` 仓库侧比 skill 侧新，已同步）。

### 维护：防回归门禁 S10 + 两件日志小工具（r162，2026-09-27）

用户口径：「该沉淀的沉淀、该搓小工具的小工具」。本轮 **零 mod 代码改动**（marker 仍 `2026-09-27-r161`；
与 r160 同例：门禁 / 工具 / 文档不进 dll 身份，所以不升 marker、不需要重新部署）。

- **门禁 S10（离线、防回归）**：`Scripts/Tools/static_checks.py` 新增「大厅访问点判空检查」——
  扫 `Scripts/**/*.cs`，命中下面任一条即 FAIL，并列出 `文件:行` 与修法：
  ① 非空条件访问 `.Lobby.NetService`（缺 `?`）；② 非空声明 `StartRunLobby x = …` 之后的裸访问 `x.NetService`。
  这正是 **BUG-21 的写法**（也是 r157/r158 → r159 只修 3/4 → r161 漏网 这四次同族坑的第 4 次）。
  离线静态层自此 **10 项（S1~S10）**。
  **自测**：临时造 `Scripts/Patch/S10SelfTestFake.cs`（两条违规各一处）⇒ S10 `FAIL`、退出码 1，
  正确报出 `S10SelfTestFake.cs:8` / `:9` ✓；删掉假样本 ⇒ 真实仓库 `PASS`（224 文件）、退出码 0 ✓。
- **`tools/log_archive.py`**：判重跳过原因从「已归档过（sha256 相同）」改为
  「已归档过：与 `<归档名>` 内容完全相同」—— 2026-09-27 分析 r161 时一度误以为"丢掉一份会话"，
  实际是 Godot 轮转副本（`godot<轮转时刻>.log` 的内容 == 它**之前**那个会话）；语义已写进脚本 docstring。
- **`tools/log_scan.py`**：新增 **`--preset daily`**（每日挑战本地多控契约：8 条锚点 + 2 条期望 0 哨兵）。
  自测：对 r161 断网 / 联网两份日志各跑一次 —— 锚点齐全、哨兵均 0 ✓。
- **沉淀**：`AGENTS.md §1` 新增「大厅访问点必须判空」硬约束（指向 S10 + 坑 I）；
  `AGENTS.md §2` 静态层计数订正 **8 → 10 项**（此前漏更两次）；
  `references/local-multicontrol-pitfalls.md` 坑 I 补「已固化为 S10」；
  `references/logging-and-marker.md` 新增「归档副本的两个坑」（`logs-archive/` 被 `.gitignore` ⇒
  `rg`/`search_content` 恒 0 命中，别据此判"证据丢了"；轮转副本 == 上一会话）；
  `references/tools.md` 同步 S10 / `--preset daily` / 归档跳过语义；
  skill 侧 `SKILL.md` 硬规矩 **三条 → 四条**（新增第 4 条判空规矩）。references 两侧 10 份逐字节一致。

### BUG-22 读档后瓦库整局失效（不出牌 / 不自动选事件 / 不自动领奖）—— **r166 已修，待实机**

- **现象（用户 2026-09-27 报）**：「打一半瓦库不会自己选事件选项了」。日志 `logs-archive\godot__20260927-185422__r165.log` 实证：
  全 4 个事件房（`Beginning event`）里**只有读档前的那一个**被自动选（L18778），读档后两次同类事件（L19193 / L19634）**一条自动选择日志都没有**；
  更硬的是**读档后的出牌作用域全为 0**（`瓦库选择器作用域进入` 最后一条在 L18463，两次读档在 L18905 / L19346）⇒ 瓦库不是"不选事件"，而是**整局停摆**。
- **根因（读档路径，与 r156~r165 的功能改动无关）**：
  1. **读档只恢复玩家 ID、从不恢复瓦库席位**：`NMultiplayerSubmenuPatch`（继续游戏）与 `LocalQuickRestartLoader`（ESC 快速重启）
     都用 `TryReadCurrentProfile(out playerIds)`（**丢弃 `wakuu=` 段**），而 `LocalSelfCoopSaveTag` 其实写了 `v3:players=…;wakuu=…`（L7914 实证），
     带 `out wakuuPlayerIds` 的重载**没有任何调用点**（死代码）；
  2. 瓦库席位又会在**进我们自己的大厅入口时被显式清空**（`NMultiplayerHostSubmenuPatch:283` / `NDailyRunLocalSelfCoopPatch:72` /
     `NCustomRunLocalSelfCoopPatch:49` 一律传 `Array.Empty<ulong>()`）⇒ 读档后 `IsWakuuEnabled(瓦库id)=false`；
  3. 由此 `IsVakuuFormMode(...)=false`（缺【瓦库形态】遗物，且 `IsTakeoverPlayerFallback` 同样要求 `IsWakuuEnabled`）
     ⇒ 托管遗物补发循环遍历空集合，出牌 / 事件 / 奖励三条链路**全部静默停摆**。
- **为什么以前没暴露**：有读档的 r151/r154 会话都在**会话守卫（r158）之前**，席位是内存静态字段、没被清 ⇒ 照样工作；
  r161 两次实机（联网 4 席 / 断网 2 席）**读档数 = 0**，这条路径根本没走到。
- **修法（r166）**：
  1. 两条读档路径改用 `TryReadCurrentProfile(out playerIds, out wakuuPlayerIds)`，在 `UseSavedPlayerIds` **之后**调
     `UseSavedWakuuPlayerIds(wakuuPlayerIds)`（顺序有要求：恢复时按本地席位表过滤）；新增锚点 `读档已恢复瓦库席位` / `快速重启已恢复瓦库席位`；
  2. 过滤规则抽成纯函数 `WakuuSeatRestorePolicy.FilterToLocalSeats`（只认本地席位、丢占位 0、去重、保持顺序）+ 7 条单测；
  3. `TrimWakuuPlayerIdsToConfiguredPlayers` 真剔除席位时补 WARN（此前**完全静默**，是这类问题难定位的直接原因）。
- **验证契约（请实机）**：
  ```
  改动:        读档（继续游戏 / ESC 快速重启）后恢复瓦库席位（r166 / BUG-22）
  EXPECTED:    读档后瓦库照常自动出牌、自动选事件、自动领奖
  SETUP:       本地多控 2 席，其中 1 席勾选瓦库托管；先玩一段（至少过一个事件房）再存档退出
  ACTION:      主菜单 → 多人游戏 → 载入（继续游戏）→ 进事件房看瓦库是否自动选；再打一场战斗看是否自动出牌
  PASS:        日志出现 `读档已恢复瓦库席位: <瓦库id>`；随后每个事件房都有 `瓦库事件自动选择完成`；战斗有 `瓦库选择器作用域进入`
  FAIL:        `读档已恢复瓦库席位:` 为空 / 之后仍无自动选择与出牌作用域 / 仍有奖励归属报错
  LOG ANCHORS: marker=2026-09-27-r168 / 读档窗口已开启 / 读档窗口已关闭: source=run-in-progress /
               读档已恢复瓦库席位 / 瓦库事件自动选择完成 / 瓦库选择器作用域进入
  期望 0：     读档窗口内的 `本地多控模式已关闭，原因: no-local-lobby-screen`；`SelectLocalReward called for reward`；
               `瓦库席位被收敛剔除`；`瓦库事件自动选择异常`
  ⚠ 判据订正:  **不要**把 `已为瓦库角色自动发放托管遗物` 当通过条件 —— 遗物通常本来就在存档里
               （`GrantWakuuRelicsAsync` 已有则跳过），r167 实测该计数为 0 而瓦库工作正常；
               "瓦库活过来了"的真判据是 `瓦库事件自动选择完成` 与 `瓦库选择器作用域进入` 的条数。
               一条命令判读：`python D:\Download\pain\tools\log_scan.py --preset load --file <日志>`（r167 新增；
               2026-09-28 起该哨兵已在工具侧**按"读档窗口"限定计数** —— 本契约写的"读档窗口内的"从此由工具自动执行，
               窗口外命中照打「窗口外另有 N 条已排除」、老版本日志退回全量计数并打提示）
               ```
- **同窗口的第二颗雷 —— r167 已修（本轮真凶）**：会话守卫（r158/r161）在读档窗口会误判「没有大厅页 + 未进局」并在约 1 秒后
  `Disable("no-local-lobby-screen")`。r166 日志实证（**5 次读档 5 次复现**）：
  ```
  8077 本地多控模式已启用 → 8110 会话已自动结束 → 8111 已关闭(no-local-lobby-screen) → 8179 会话已初始化（IsEnabled=false）
  ```
  而 `LocalMultiControlRuntime.GrantWakuuRelicsAsync` 首行就是 `if (!IsEnabled) return;`（`:527`）
  ⇒ **托管遗物不发**（`已为瓦库角色自动发放托管遗物` = 0）⇒ 瓦库整局不出牌 / 不自动选事件；
  同时所有门控在 `IsEnabled` 上的归属守卫一起失效 ⇒ 事件卡牌奖励归属断档（r166 出现 3 条游戏侧
  `InvalidOperationException: SelectLocalReward called for reward CardReward with non-local…`）。
  **修法**：新增「读档窗口」（`LoadReplayWindowPolicy` 纯函数 + 180 秒超时安全阀）：
  读档入口（继续游戏 / ESC 快速重启）开窗，守卫在窗口内不下手，进局（`IsInProgress`）/ `RunManager.CleanUp` / `Disable` 自动关窗。
  +7 条单测。**注意**：`_wakuuPlayerIds` 的恢复（r166）是必要条件但**不充分** —— 会话被关掉时名额恢复也没用。

### Co-op Bots 在本地多控大厅「人数到 4 后加不了 Bot」（2026-09-28 用户实机反馈；**✅ 已定性：非我方 —— 原版多人大厅同样受 4 人限制**）

- **现象（用户原话）**：本地多控大厅把玩家人数调到 4 ⇒ Co-op Bots 的「添加 Bot」不再可用；
  **再把人数降到 4 以下也仍然不可用**；退回上一个界面再进来（页面重建）就恢复正常。
- **已知**：本局日志（`logs-archive/godot__20260928-131003__r169.log`）里 CoopBots **0.39.1 加载正常**、
  适配器就绪、`coopBotsSeats` 为空（本局不接管任何席位）；**我方异常 0 条**（`### Exception ###` 0 / `add_child() failed` 0）。
- **待办（先判是不是我们的锅，流程见 `references/thirdparty-mod-conflicts.md`）**：
  ① 官方联机大厅里把人数调到 4 再加 Bot —— 若同样复现 ⇒ CB 自身行为，与我们无关；
  ② 纯自定义（不启用本地多控多席）下同样操作；
  ③ 若只在我们的回环大厅复现，再查两件事：CB 的按钮状态是否只在
  `PlayerConnected/Disconnected` 上刷新，而我们的 `AddLocalHostPlayerInternal` / 席位移除路径**是否广播**了该事件。
- ⚠ **注意**：同一轮反馈里的「选人界面无限玩家」与「按钮位置漂移」**已由 r170 修掉**（references 坑 J）。
- ✅ **定性完成（2026-09-28 用户实机对照）**：**原版多人大厅**里人数到 4 后同样加不了 Bot ⇒ 这是
  Co-op Bots / 游戏侧的 4 人上限行为，**与我们无关**（退回上一界面再进来能刷新也只是 CB 侧的 UI 刷新时机）。
  **不修、不另开补丁 mod**：复现路径照旧可用「退到上一个界面再进」绕过。本条**关单归档**。

### ✅ 已定性：大厅席位卡上「切人 / 变瓦库」按钮位置偏左 —— **原版实现即如此，不是 bug**（2026-09-28）

- **现象**：本地多控大厅（每日 / 自定义）里，席位卡上的「切人 / 变瓦库」按钮比"对齐该席位卡 id 标签"的位置**偏左一点**。
- **定性（2026-09-28 用户对照原作者 mod）**：**玩原作者的本地多角色 mod，那个按钮本来就是偏左的**
  ⇒ 属于**原版实现就有的行为**，不是我们引入的，也不是 R2 期间的缓存问题。
- **排查轨迹（供参考，别重复走）**：r170 移除"写后读 / 每帧列布局"缓存（修掉「无限玩家」「漂移」）、
  r171 把标签订位也回退成逐帧实时 —— **位置始终没变**；现在原作者版本对照彻底确认非回归。
- **处置**：**不是 bug ⇒ 不修、不排期、不再查**；本条目只作为"别再当新 bug 查"的档案。

### 瓦库不自动选「从牌组选一张牌复制」类效果（猪猪 mod【重瞳】等遗物）（2026-09-28 用户报；**r175 已修 → ✅ 2026-09-28 实机确认，关单**）

- **现象**：瓦库托管席位带着【重瞳】（`YUWANCARD-REINCARNATED_EYE`）进战斗，战斗开始时的「选择一张牌复制」
  界面没人自动点，得真人手选。
- **根因（日志 + 第三方 dll 的 IL 引用分析）**：我们的「作用域外选牌自动作答」
  （`CardSelectWakuuTurnStartAutoAnswerPatch`，r107 为"回合开始类遗物"加的口子）**只挂了
  `FromChooseACardScreen` 与 `FromSimpleGrid` 两个入口**（本局日志：自动作答 6 次**全部**是 `FromChooseACardScreen`）；
  而 `YuWanCard.Content.dll` 还引用了 `CardSelectCmd.FromHand` / `FromHandForDiscard` / `FromSimpleGridForRewards` /
  `FromDeckGeneric` / `FromDeckForUpgrade` / `FromDeckForRemoval` / `FromDeckForEnchantment`。重瞳走的是未覆盖入口
  （最可能 `FromHand` —— 从 r6 起我们就规定「作用域外 `FromHand` 一律不代答」，防进战斗黑屏）。
- **修法（两步：r173 → r174）**：
  1. **r173**：作用域外自动作答扩展到 `FromHand`（`Priority.Low`，作用域内仍由 `CardSelectHandScenarioPatch`
     按场景优先级作答）；+ 未适配入口探针（`FromCombatPile` / `FromHandForUpgrade`）。
     **实机未通过**：用户复测仍然要真人手点，而且日志里**连探针都不打**。
  2. **r174（真因所在）**：第三方 `YuWanCard.Content.dll` 的 IL 引用里**有 `CardSelectCmd.PushSelector`**
     ⇒ 它在调 `From*` **之前自己压了选择器**，而 `ShouldAutoAnswer` 当时要求「栈必须为空」⇒ 永远接管不到
     （连探针都被这条判据挡住）。修法 = 判据改为「**栈顶不是我们自己的选择器**」
     （`CardSelectCmd.Selector is not LocalWakuuStrategySelector`；我们的前缀执行早于游戏方法体内的
     `Selector != null` 分支，因此能抢在它前面 `return false` + 给 `__result`）；
     + 新增 `FromSimpleGridForRewards` 适配（遗物描述是"从牌组随机展示 N 张，选一张获得它的原始版本复制"，
     最可能走这个入口）；+ 探针铺满（`FromHandForDiscard` / `FromDeckGeneric` / `FromChooseABundleScreen`），
     并把 `selectorStackTop` 打进日志（一眼看出被谁挡住）。
  - 门控仍为：本地多控 + 单人冒险 + 本地回环 + 本地席位 + **后台托管** + 瓦库形态；选牌用场景表
    （Copy / Remove / Transform，未知场景按 `cardPickMode`）。
  3. **r175（定案）**：r174 挂的探针**一次就命中** —— 日志给出
     `瓦库作用域外选牌入口未适配: entry=FromDeckGeneric, player=…327, selectorStackTop=none`
     ⇒ 该效果走的是 **`FromDeckGeneric`**（牌组选牌，正对应"从牌组随机展示 N 张"），且**栈是空的**
     （不是被第三方 selector 挡住）。修法 = 把 `FromDeckGeneric` 从探针**升级为自动作答**
     （候选与游戏原方法同口径：`PileType.Deck` + `filter` + `sortingOrder`，再用策略选择器按 min/max 作答），
     并顺带适配 `FromDeckForUpgrade`（火堆 / 事件升级类）；`FromDeckForTransformation` 仍只探针。
     marker `2026-09-28-r175`。
     **✅ 2026-09-28 实机确认（关单）**（日志 `logs-archive/godot__20260928-185607__r175.log`）：
     `瓦库作用域外牌组选牌自动作答: player=…327, options=10, select=1~1, mode=last, source=FromDeckGeneric`
     命中（该效果唯一一次触发即被自动作答）；`瓦库作用域外选牌入口未适配` **0**、
     `### Exception ###` / `add_child() failed` / `ObjectDisposedException` **全 0**。
- **验证契约（请实机）**：
  ```
  改动:    作用域外选牌自动作答：判据放宽（不再要求栈空）+ 补 FromSimpleGridForRewards（r174，marker 2026-09-28-r174）
  SETUP:   本地多控 2 席，瓦库托管那一席带【重瞳】等"拾起/战斗开始时选一张牌复制"类遗物
  ACTION:  触发该遗物（拾起或进战斗），看选牌界面是否被瓦库自动作答
  PASS:    `瓦库作用域外选牌自动作答: … source=FromSimpleGridForRewards`（或 FromHand / FromSimpleGrid 等）
           且界面不再停下来等真人点
  FAIL:    界面仍停在原地；或出现 `瓦库作用域外选牌入口未适配: entry=…, selectorStackTop=…`
           ⇒ 把整行发我（selectorStackTop 会说明被谁挡住），下一批按它扩展适配
  期望 0： ### Exception ### / 进战斗黑屏 / add_child() failed
  ```
- **经验**：见 `references/local-multicontrol-pitfalls.md` **坑 K**（作用域外作答的入口覆盖面 + 两条定位手段）。

---

### BUG-23 瓦库打出「第三方自绘选牌」类卡牌后卡死（2026-09-28 实机，沙耶 mod 色素细胞；**已定性，待拍板修法**）

**现象**（用户 2026-09-28，marker `r180`，日志 `logs-archive/godot__20260928-202151__r180.log`）：
瓦库打出沙耶 mod 的【色素细胞】`FIGURE_SAYA-PIGMENT_CELL_CARD`（千变万化词条，打出后从几张牌里选一张加入手牌）
后**卡牌停屏、界面不弹、也没有交给真人**，整局卡死；行动队列一直停在 "waiting for player choice"。

**日志实证（一条链定死根因）**：
```
[VERYDEBUG] [PlayerChoiceSynchronizer] Reserved choice id 6 for player 76561198422527327, next is 7
[DEBUG] [ActionQueueSet] Pausing action PlayCardAction card: CARD.FIGURE_SAYA-PIGMENT_CELL_CARD … for player choice
[DEBUG] [PlayerChoiceSynchronizer] Awaiting remote choice 6 for player 76561198422527327      ← 走了"远端等待"
[VERYDEBUG] [ActionQueueSet] … at front of player queue … is waiting for player choice        ← 每秒刷，永不完成
```
我方看门狗同时报 `rejected=watchdog-in-flight:120+`、`selectorStackTop=LocalWakuuStrategySelector`
⇒ 出牌作用域一直不退出（选牌链挂着），而我方选择器其实**已经压在栈上**。

**根因（第三方代码，只读反编译证据）**：沙耶 mod 自己实现了选牌助手
`figure_Saya…CommonActions.SelectCenteredBranchCards`，**既不读 `CardSelectCmd.Selector`、也不走 `CardSelectCmd.From*`**：
```csharp
uint choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(player);
if (LocalContext.IsMe(player) && NetService.Type != Replay) {
    NPlayerHand.Instance?.CancelAllCardPlay();
    var screen = NChooseACardSelectionScreen.ShowScreen(choices, false);   // 它自己的 UI
    result = (await screen.CardsSelected()).ToList();
    RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(player, choiceId, …);
} else {
    result = …(await …WaitForRemoteChoice(player, choiceId)).AsIndexes()…;  // ★ 我们的回环里永远没人回答
}
```
而**并发出牌档（方案 D）刻意不钉全局上下文**（`LocalWakuuRelicRuntime`：`LocalContext.NetId = player.NetId`
那一支只在非并发档执行）⇒ `LocalContext.IsMe(327)` 为 false ⇒ 第三方走 else 远端分支 ⇒ 死等。

**为什么现有三件套都够不着**：① `CardSelectCmd.From*` 的 `Selector != null` 短路 —— 第三方根本不经过；
② `CardSelectWakuuTurnStartAutoAnswerPatch`（作用域外作答）拦的是游戏入口，不是第三方方法体；
③ `WakuuSelectorRegistry` + getter 守卫只管 `CardSelectCmd.Selector` 的读取者。

⚠ **不能简单"空结果放行"**：`PlayerChoiceResult` 是**类型化**的（`AsIndex()` / `AsIndexes()` /
`AsCombatCards()` / `AsDeckCards()` / `AsPlayerId()` 各自对错类型抛 `InvalidOperationException`），
而 `WaitForRemoteChoice` 这一层**看不到调用方期望哪种类型** ⇒ 盲回一种类型会把"卡死"换成"异常"。
（沙耶这条链要 index 类型；但 `FromCombatPile` 那条要 combat cards。）

**修法决策（2026-09-28 用户拍板）**：**先做 A（解软锁），B 随后单独一轮**。

**✅ A 已实现并部署（marker `2026-09-28-r181`，待实机）**：
- 新增补丁 `Scripts/Patch/PlayerChoiceSynchronizerRemoteChoiceFallbackPatch.cs`
  （`PlayerChoiceSynchronizer.WaitForRemoteChoice` 前缀）：**我们本地席位 + 后台托管瓦库**等待远端选择时，
  直接以"空结果"放行并在日志里点名——
  `瓦库远端选择无人作答，已按空结果放行（防软锁兜底，本次选择被跳过）: player=…, choiceId=…, 结果类型=…, 调用方=…`。
  门控刻意收窄：本地多控 + 单人冒险 + 本地回环 + 我们的本地席位 + 瓦库形态；火堆"选一个队友"让路给既有补丁。
- ⚠ **按调用方给对应类型的空结果**（`PlayerChoiceResult` 类型化，猜错 = 抛 `InvalidOperationException`）：
  分类与映射表抽成纯逻辑 `Scripts/Runtime/PureLogic/PlayerChoiceCallerClassifier.cs`
  （状态机帧名还原 + `CardSelectCmd.FromHand*/FromCombatPile*` → 战斗卡、`FromDeck*` → 牌组卡、
  `FromSimpleGrid*`/`FromChooseACardScreen`/`RelicSelectCmd`/`CardReward` → 索引、`MendRestSiteOption` → 玩家、
  **未知（含第三方自绘）默认索引**），单测 **744 → 755**（`PlayerChoiceCallerClassifierTests` 11 例：
  映射表逐项 + 状态机帧还原 + 内部帧跳过 + 第三方默认）。
- 门禁：构建 0 警告 0 错误（237 .cs / 306 源码）→ **755 单测全绿** → 静态层 **10 PASS**
  （S7 基线刷新为 **188 补丁类 / 221 目标行 / 195 字符串 / 367 语义标识**；新补丁已登记 `PatchDomainMap`）
  → `clr_compat` PASS → `preflight -Deploy` **4 PASS / 3 SKIP** → `preflight -Lint` **3 PASS / 3 SKIP**
  → `dll_check --deployed` 字节一致（`87fde3fb205d…`）。**未提交**。
- **验证契约（请实机，marker `2026-09-28-r181`）**：
  ```
  SETUP:   本地多控 2~4 席（瓦库开启【并发出牌】档，复现原路径）；让瓦库打出沙耶 mod【色素细胞】
  ACTION:  等它打出后观察：卡牌是否还会停屏
  PASS:    `瓦库远端选择无人作答，已按空结果放行（防软锁兜底，本次选择被跳过）: player=…, choiceId=…,
           结果类型=index, 调用方=<第三方类型>.<方法>` 出现 1 条；卡牌**正常结算**（本次不拿牌）、
           行动队列继续往下走、`waiting for player choice` 不再刷
  FAIL:    仍卡在 "waiting for player choice"；或出现 `InvalidOperationException`（⇒ 说明调用方需要别的
           结果类型，把日志那行的 `调用方=…` 发我，补 `PlayerChoiceCallerClassifier` 的映射表）
  期望 0： ### Exception ### / 幽灵弹层 / 我方 NullReferenceException
  ```

**✅ 实机结论（2026-09-28，marker `2026-09-28-r181`，用户「现在确实没卡住」；
日志 `logs-archive/godot__20260928-204536__r181.log`）**：
- 兜底**精准命中一次**（L9117）：
  `瓦库远端选择无人作答，已按空结果放行…: player=…327, choiceId=0, 结果类型=index, 调用方=CommonActions.SelectCenteredBranchCards`
  ⇒ **调用方正是反编译定位到的第三方助手**（状态机帧名还原成功），`结果类型=index` 与它后面的 `AsIndexes()` 一致，
  故**没有** `InvalidOperationException`（日志里该异常 0 条）⇒ "按类型给空结果"这条设计成立。
- 放行后流程正常继续（游戏侧逐帧证据）：`Sending message to clients to resume action id 11` →
  `Resuming action … PIGMENT_CELL_CARD` → `finished gathering player choice, and is assigned new id 12` →
  `resumed execution` → 卡牌正常结算。
- **原卡死锚点归零**：`waiting for player choice` **0**（r180 是每秒刷）、`Awaiting remote choice` **0**；
  出牌作用域 **进 6 / 退 6 平衡**（r180 是 4/3 + 看门狗 `watchdog-in-flight` 涨到 120+，本局仅 8）。
- 健康度：`INIT_STATUS=OK` / `FATAL=0`；期望 0 项**全 0**（选择器作用域异常退出 / 看门狗重启失败 /
  `Couldn't get hand node` / 动作队列空引用 / 手牌差异 / 保留为人工领取 / 归属者残留）；我方 `[ERROR]` **0**；
  全局 3 条 ERROR 仍是老噪音（Manosaba / ddu 分支、BetterModMenu 超时）；12 条 NRE 全是第三方 `STS2RitsuLib`（同基线）。
- **WARN 模板对比**（新增的 `log_scan.py --warn-diff`，对比 r180）：本局独有 4 个 = 本轮那条兜底 WARN +
  `流程阻塞看门狗统计`（既有族：r178 2 / r179 7）+ `本我牌守卫`/`本我解放修复`（第三方 Koishi 补丁**加载顺序**族，
  历史 60+ 份日志都有）；参考局独有的 9 个全是 `关键目标 … 存在第三方补丁 owner`（同一件事的另一面）
  ⇒ **A 没有引入任何新告警来源**。**BUG-23 方案 A 关单**。

**✅ B 已实现并部署（marker `2026-09-28-r182`，待实机）——「功能完整」版：第三方自绘选牌由瓦库自动作答**

用户 2026-09-28 拍板「A 先做、B 随后单独一轮」，故 B 单列本轮。**两半缺一不可**：

1. **让第三方走它自己的本地分支**（新补丁 `Scripts/Patch/LocalContextThirdPartyIsMePatch.cs`，域 `ThirdParty`）：
   `LocalContext.IsMe(Player)` 后缀 —— 只在①**调用方是第三方**（调用栈上 `LocalContext` 外第一个真实帧属于第三方程序集；
   跳过 `LocalContext` 自身以覆盖 `IsMe(Creature)` / `IsMine(card)` / `ContainsMe(...)` 包装）②该席位在我们的
   **自动化窗口**内（后台托管瓦库 + **此刻登记着托管选择器**，即正处一次自动出牌/遗物效果/事件作答作用域）
   ③原本判 false —— 时改口为 true。⇒ 第三方弹它自己的界面，而不是走无人作答的远端等待。
   ⚠ 这条刻意不动**原版**调用方语义 ⇒ 把 r109 那类"把别人的牌当本地牌做前台视觉"的风险面限制在第三方代码里；
   真人亲自操作该席位时（无托管选择器）窗口不成立，**绝不**替真人做决定。改口会打一条去重 INFO 点名调用方。
2. **我们驱动它的界面**（新补丁 `Scripts/Patch/NChooseACardSelectionScreenAutoAnswerPatch.cs`，域 `Wakuu`）：
   `NChooseACardSelectionScreen.ShowScreen` 后缀 → 延迟 0.6s（避开屏幕自带的 **350ms 点击保护窗**、等 holder 建好）
   → 按 `LocalWakuuStrategySelector.Shared` 在候选里选一张 → 对选中 holder 发 `NCardHolder.SignalName.Pressed`
   （**走游戏自己的点击路径**，不碰私有方法）⇒ `await screen.CardsSelected()` 拿到牌、效果正常结算。
   作答后 +1.2s 核对界面是否关闭；没关 ⇒ WARN"可能是多选/被第三方改写，交回真人"（下一轮适配的实证锚点）。
   日志：`瓦库自绘选牌自动作答: chooser=…, options=…, picked=…, screen=NChooseACardSelectionScreen`。
- 判据收敛在纯逻辑 `Scripts/Runtime/PureLogic/WakuuSelfDrawnChoicePolicy.cs`（`IsManagedWakuuSeat` /
  `IsAutomatedSeatInPlay` / `ShouldWidenIsMe` / `ShouldAutoAnswerScreen`），单测 **755 → 759**
  （`WakuuSelfDrawnChoicePolicyTests`）：既有「作用域外自动作答」的六项口径也改走同一个原语，避免口径漂移。
- 门禁：构建 0 警告 0 错误（241 .cs / 310 源码）→ **759 单测全绿** → 静态层 **10 PASS**
  （S7 基线刷新为 **190 补丁类 / 223 目标行 / 196 字符串 / 370 语义标识**）→ `clr_compat` PASS →
  `preflight -Deploy` **4 PASS / 3 SKIP** → `preflight -Lint` **3 PASS / 3 SKIP** → `dll_check --deployed`
  字节一致（`8bfc18f66efb…`）。**未提交**。
- **验证契约（请实机，marker `2026-09-28-r182`）**：
  ```
  SETUP:   本地多控 2~4 席、瓦库开【后台托管】+【并发出牌】档（原来卡死的那套配置不用改）
  ACTION:  让瓦库打出沙耶 mod【色素细胞】（或任一"打出后从几张牌选一张"的第三方卡）
  PASS:    `瓦库自绘选牌自动作答: chooser=…, options=N, picked=…, screen=NChooseACardSelectionScreen` 1 条；
           界面**一闪而过**、瓦库**拿到那张牌**（不再停屏、也不再出现"本次选择被跳过"那条 A 兜底 WARN）
  FAIL:    仍停屏（⇒ 看有没有 `瓦库自绘选牌界面出现但本次不代答` / `单击后界面仍未关闭` /
           `自动化作用域已结束` 三行之一，连同上下文发我）；或出现 `InvalidOperationException`
  期望 0： ### Exception ### / 幽灵弹层 / 我方 NullReferenceException / `add_child() failed`
  ```
- ⚠ 遗留（不阻塞，已记录）：① 我们驱动不了的**其它界面类**（如遗物三选一 `NChooseARelicSelection`）在瓦库席位上
  仍会停在屏幕上等真人点（比"无界面死等"好，但没有自动作答）；② 多选/被第三方改写过的自绘界面只作一答并 WARN；
  ③ 「真人席位」在并发出牌期间撞上第三方自绘选牌仍可能卡（同源问题，遇到再按本节模板处理）。

**🔍 r182 实机：B1 生效、B2 漏答（已由 r183 修正）** —— 日志 `logs-archive/godot__20260928-212922__r182.log`
（用户「现在是弹给我选了」= 界面弹出来但没人自动选，真人只好自己点）：
- ✅ **B1 生效实证**：`第三方询问本地玩家身份，已按「同机席位」放行: caller=figure_Saya.ModSupport.Utils.CommonActions+<SelectCenteredBranchCards>d__16, player=…327`
  —— 正是那条第三方自绘选牌链（同局共 15 条，来自 6 个不同第三方 mod；原版调用方一条都没被放行）；`瓦库远端选择无人作答` **0**（没再走远端死等）。
- ❌ **B2 漏答**：`[WARN] 瓦库自绘选牌界面无候选节点，本次不代答: chooser=…327` ⇒ 界面留给真人点。
  **根因 = 我用 `Node.FindChildren("*", nameof(NGridCardHolder), …)` 找候选 holder** —— Godot 的 `type` 过滤器按
  **原生 ClassDB 类名**匹配，`NGridCardHolder` 是 C# 脚本类（原生类是 `Control`）⇒ **恒返回空**。
- 其余健康：`### Exception ###` / `add_child() failed` / 我方 `[ERROR]` **全 0**；12 条 NRE 仍是第三方 `RitsuLib`；
  `弹层阻挡自动流程 … top=NChooseACardSelectionScreen[inTree=True]`（既有兜底，识别到界面挡着看门狗，正常）。

**✅ r183 修正（marker `2026-09-28-r183`，待实机）**：
- 候选改走我们自己的 `LocalNodeTree.EnumerateDescendants(screen).OfType<NGridCardHolder>()`（**C# 类型**遍历，
  R2 的单点化设施正是为这类场景收的），不再用 `FindChildren` 的 type 过滤器；
- 候选（holder）是屏幕在 `_Ready` 里建的，偶有晚半拍 ⇒ 加 **0.35s × 最多 3 次**重试后才判"交回真人"，
  WARN 里带上 `attempt=`；
- 门禁：构建 0 警告 0 错误 → **759 单测全绿** → 静态层 10 PASS（S7 基线不变）→ `clr_compat` PASS →
  `preflight -Deploy` 4 PASS / 3 SKIP → `dll_check --deployed` 字节一致（`b3948defe2de…`）。**未提交**。
- **验证契约（请实机，marker `2026-09-28-r183`）**：同 r182 的配置与动作（瓦库打【色素细胞】）：
  PASS = `瓦库自绘选牌自动作答: chooser=…, options=N, picked=…` 1 条、界面**一闪而过**、瓦库**拿到那张牌**；
  FAIL = 仍由真人点 ⇒ 看 WARN 里的 `attempt=`（=3 说明候选始终没建出来，需换更晚的时机/别的容器名）与
  `无候选节点` 那行；期望 0 同 r182。

**✅✅ r183 实机通过（2026-09-28 关单，用户「现在是弹出来一下就没了」= 界面被打完就关，
日志 `logs-archive/godot__20260928-213917__r183.log`）** —— 全链闭环，四步实证：
1. `L19992 瓦库自绘选牌自动作答: chooser=…327, options=3, picked=FIGURE_SAYA-ENZYME_CARD, mode=last, screen=NChooseACardSelectionScreen`
   （前一行是 `第三方询问本地玩家身份… caller=…CommonActions+<SelectCenteredBranchCards>d__16` ⇒ 界面是 B1 打开的、由 B2 作答的）；
2. `PlayerChoiceSynchronizer: Sending player choice id 1 for player …327, result indexes 2` →
   `PlayerChoice sender/context 已恢复` → `ResumeActionAfterPlayerChoiceMessage` → 动作 `resumed`（归属正确）；
3. **选中的牌真的进了瓦库手牌**：`已跳过非前台角色的进手牌视觉节点（防串手牌显示）: card=FIGURE_SAYA-ENZYME_CARD, owner=…327, foreground=…326`；
4. **下一回合瓦库把它打了出来**（最强证据）：`瓦库评分出牌: player=…327, round=2, card=CARD.FIGURE_SAYA-ENZYME_CARD` →
   `Player …327 playing card FIGURE_SAYA-ENZYME_CARD` 正常结算。
- 健康：`### Exception ###` / `add_child() failed` / 我方 `[ERROR]` / `无候选节点` / `单击后界面仍未关闭` **全 0**；
  出牌作用域 **进 34 / 退 34 平衡**；`弹层阻挡` 仅 1 条（本次作答期间，属预期）。
- 两条**非我方噪音**（记录，不定性/不追）：① `InvalidOperationException: The type is not supported for conversion
  to/from Variant: 'System.Threading.Tasks.Task'` **×2** —— 出现在控制台 loadout 指令
  （`ConsoleCmdGameAction … __loadout_add_cards_v2`，真人发起）之后，且**我们仓库里没有任何 Func 式
  `Callable.From<…,…>`**（grep 0 条）⇒ loadout/控制台那条第三方线；r179/r181/r182 该族均为 0，只在用了 loadout 的这局出现。
  ② 13 条 NRE = `RitsuLib` 12（基线）+ **1 条游戏侧 `NCombatCardPile.OnRelease()`**（真人点战斗牌堆触发，
  紧跟 `控制上下文已更新: …326 → …327, source=player-state-button`）⇒ 见下方候选 BUG-24。

**候选 BUG-24（观察项，2026-09-28，仅见 1 次）**：手动切到瓦库角色后点战斗牌堆（抽/弃牌堆）⇒
游戏侧 `NCombatCardPile.OnRelease()` 抛 NRE（栈内无我方/第三方帧，`NClickableControl.HandleMouseRelease` 触发）。
我们只在该类的 `Initialize` 上挂了前缀（退订旧 `CardPile` 的增删监听），**不写 `_pile`**，理论上留不下空引用；
历史 8 份日志该族为 0（本局是第一次点牌堆）。**待自然复现再定性**（若复现：记「切到瓦库后多久点的」「点的是抽牌堆还是弃牌堆」）。

**临时绕过（A 时代留下，B 已实机通过，一般不再需要）**：把「瓦库并发出牌」档关掉 ⇒ 出牌走 inline 档、上下文被钉住
⇒ 第三方走**本地分支**、界面弹出来真人可以直接点。

**排查方法沉淀**：见 `references/local-multicontrol-pitfalls.md` **坑 M**（第三方自绘选牌家族 + 定位手段：
先 `thirdparty_extract_embedded.ps1` 导出壳里的内嵌实现，再 `thirdparty_api_refs.ps1` / `decompile_mod.ps1`）。

---

## R3 身份收编（席位身份唯一取数入口 `SeatRegistry` / `LocalSeatSource`）

> 提案：`maintenance-docs/decision-records/runtime架构分层重构评估.md` §四 R3 + **§八 靶区清单**（该文件在仓库外维护）。
> 目标：把"这个 id 是谁"从四路各自猜（`LocalContext.NetId` / `Session.CurrentControlledPlayerId` /
> 反射 `_localPlayerId` / 裸比 `player.NetId`）收成"问 `SeatRegistry`"。**每批行为零变化 + 一局实机。**

**已落地**
- **第一轮（r177）**：纯逻辑 `Scripts/Runtime/PureLogic/SeatIdentity.cs` + `SeatRegistry.cs`
  （唯一取数入口 + 席位表自检），单测 **726 → 744**；**一个调用点都没改**（无需实机）。
- **第二轮 B1（r178，本轮）**：薄适配 `Scripts/Runtime/LocalSeatSource.cs`（读席位表 / 受控位 / 回环上下文
  → 不可变快照；**命中校验是权威内容比对**，所以"写后读"安全、无需手工失效），并把**动作 / 前台归属**的
  读点全部改走它 —— `LocalMultiControlRuntime`（11 处，含 `TryGetForegroundPlayer` / 结束回合按钮自愈 /
  回合开始抽牌判定 / peek 回切 / 自动切前台及其写后校验）、`CardTransformNetIdPinPatch`（3 处）、
  `CardPileAddForegroundContextPinPatch`、`CardPileHandVisualOwnerGuardPatch`、`MapSelectionSynchronizerPatch`、
  `HookPlayerChoiceContextLocalPatch`、`EventSynchronizerPatch`（2 处）、`CombatManagerTurnHookForegroundPatch`、
  `ThievingHopperPatch`、`LocalWakuuRelicRuntime`、`LocalWakuuSafetyNet`；**写入点一个没动**
  （`ApplyControlContext` / `AlignContextForActionOwner` / `AlignLocalContextToForegroundForEndTurn` 照旧）。
  效果：动作 / 前台侧的 `LocalContext.NetId ==` 比较与 `SessionState.CurrentControlledPlayerId` 读取
  **各只剩 1 处**（都在奖励归属 ⇒ 属下一批 B2）。
- 进局新增锚点：`会话席位自检通过: seats=…, primary=…`（有问题则 `会话席位自检发现问题: …`）。
- **第三轮 B1b（r179，本轮）：席位归属判定统一**——把 16 个文件 **28 处**裸写法
  （`LocalSelfCoopContext.LocalPlayerIds.Contains(...)` 18 处 + `LocalSelfCoopContext.IsLocalSessionSeat(...)` 10 处）
  全部改走 `LocalSeatSource.IsLocalSeat(...)`：`RewardsSetSynchronizerSelectLocalRewardPatch` /
  `RewardsSetPatch` / `RewardsCmdPatch` / `CardRewardPatch` / `CardSelectCmdPatch`（2 处）/
  `CardSelectWakuuTurnStartAutoAnswerPatch` / `CombatManagerReadyEnemyTurnPatch`（2 处）/
  `CombatRoomOfferRoomEndRewardsPatch`（3 处）/ `OneOffSynchronizerSpoilsMapPatch` / `HookEnqueueForegroundPatch` /
  `LoadRunLobbyPatch` / `NPlayerHandSelectCardsSerializationPatch`（2 处）/ `NRemoteLobbyPlayerSwitchPatch`（2 处）/
  `LocalMultiControlRuntime`（6 处）/ `LocalMultiSessionState` / `LocalRestSiteSeatBubble`。
  **判据逐字等价**（`IsLocalSeat` = `id != 0 && 席位表包含`，与 `IsLocalSessionSeat` 同义）。
  `LocalSelfCoopContext.IsLocalSessionSeat` 保留为**席位表源级原语**并加注记（消费方一律走唯一入口），
  B1b 后**外部调用点 = 0**；`LocalSelfCoopContext.LocalPlayerIds` 仍供大厅侧"取席位表"用（不是判定）。
  复核：`grep 'LocalSelfCoopContext\.(LocalPlayerIds\.Contains|IsLocalSessionSeat)\('` 在 `Scripts/` **= 0**；
  `LocalSeatSource` 调用点 24 → **53**；写入点仍 22 处未动。
- **第四轮 B2（r180，本轮）：奖励 / 掉落 / 药水 / 商店归属的「当前归属者」读取收编**——
  `LocalSeatSource` 新增 `ContextSeatId()`（= 旧 `LocalContext.NetId`，**可空**）与 `IsContextSeat(id)`；
  **8 处**改走唯一入口（判据逐字等价）：`CrystalSpherePatch`（`CurrentControlledPlayerId ?? LocalContext.NetId ?? 0`
  → `ForegroundSeatId()`，它本来就是"受控位优先"口径）、`LocalWakuuRewardAutoClaim`（作用域保存的上下文原值 +
  `AlignLocalContext` 写前判定）、`CardRewardPatch`（钉扎前保存原值）、`PlayerPotionMirrorPatch`（药水默认目标）、
  `NPotionContainerPatch`（药水栏绑人）、`NMerchantInventoryPatch`（商店库存绑人）、`NHandImageCollectionPatch`
  （"本机当前屏幕属于谁"）。**写入点一个没动**；顺手删 3 个因此不再需要的 `using`。
  ⚠ **口径坑**：route ①（`LocalContext.NetId`）的读取**不能**一律换成 `ForegroundSeatId()` —— 前台口径是
  「受控位优先」，而自动化作用域（奖励自动领取 / 商店自动采购 / 瓦库出牌看门狗）恰好**把上下文对齐到归属者、
  受控位仍停在真人**，两种口径在那时会给出不同的 id；只有原式本身就是"受控位优先"的那处才能换。
  另把 8 处**不是**身份判定的点（作用域归属比较 ×3 / 两两比较 ×4 / 反射读取 ×1 → B3）就地加注释定性。
  单测仍 **744**（改动全在 Godot 依赖层，靠实机 + grep 复核）。

**验证契约（请实机，marker `2026-09-28-r180`）**
```
改动:    R3 B2 —— 奖励/掉落/药水/商店归属的「当前归属者」读取收编到 LocalSeatSource（8 处，行为零变化）
SETUP:   本地多控 2~4 席（真人 + 至少一个瓦库托管席位）；一局里走到战斗奖励 + 事件 + 商店（+ 宝箱/水晶球更佳）
ACTION:  正常玩：领战后奖励、瓦库自动领取卡牌奖励、进商店看库存/买卡、拿一瓶药水、必要时切人
PASS:    行为与 r179 完全一致：`瓦库商店自动买…成功` / `卡牌奖励已自动领取` / 药水栏显示正确角色的药水 /
         `瓦库奖励自动领取` / 商店库存绑定日志 `商店库存绑定到当前角色: player=…` 归属正确；
         `会话席位自检通过: seats=…` 照旧
FAIL:    药水栏/商店库存/药水默认目标跟随到错误角色；奖励归属错人或弹层错乱；切人后药水栏不跟随
期望 0： ### Exception ### / add_child() failed / 我方 NullReferenceException / 幽灵弹层
```

**验证契约（请实机，marker `2026-09-28-r178`）**
```
改动:    R3 B1 —— 动作/前台归属的身份读取改走 LocalSeatSource（行为零变化，marker 2026-09-28-r178）
SETUP:   本地多控 2~4 席（真人 + 至少一个瓦库托管席位），打一场战斗 + 走一个事件/地图
ACTION:  ① 战斗中切人、出牌、结束回合；② 让瓦库后台出牌（数据链 / 不等价交换这类手牌变换效果更好）；
         ③ 地图选点投票；④ 事件里投票
PASS:    `会话席位自检通过: seats=…`（进局 1 条）
         战斗/前台锚点照旧出现：`瓦库自动操作前切换视角` / `检测到后台角色触发战斗效果/选牌，自动切换前台` /
         `仅关键节点：瓦库回合开始已看过，自动切回原视角` / `已跳过非前台角色的进手牌视觉节点` /
         `[手牌同步修复]` / `结束回合点击：上下文已校正到前台玩家` / `跳过结束回合后自动切人`
         观感与 r176 一致：切谁就显示谁的手牌、结束回合点了就有反应、后台瓦库的牌不出现在前台手牌区
FAIL:    `自动切前台失败，已回滚会话控制索引` / `检测到无效战斗角色ID` / `会话席位自检发现问题` 出现；
         或切人后手牌不跟随、结束回合点了没反应
期望 0： ### Exception ### / add_child() failed / 我方 NullReferenceException / 手牌串角色
```

**✅ 实机结论（2026-09-28，marker `2026-09-28-r178`，日志 `logs-archive/godot__20260928-193549__r178.log`）**
一局覆盖「出征（3 席）→ 战斗切人 → 瓦库自动出牌 → 事件自动选择 → **读档（继续游戏）** → 读档后再战」，
契约全过：
- 新锚点 `会话席位自检通过: seats=…, primary=…` **2 条**（出征 + 读档各一次，3 席齐全）、
  `会话席位自检发现问题` **0**；
- 失败哨兵全 0：`自动切前台失败` / `控制上下文切换回滚` / `检测到无效战斗角色ID` / `检测到手动出牌上下文漂移`；
- 正常路径照旧：`检测到后台角色触发战斗效果/选牌，自动切换前台` **2**、`切换操控角色` **9**、
  `瓦库形态后台模式，跳过自动切换视角` **56**（本局瓦库走后台档 ⇒ 不切视角属预期）、
  卡牌奖励自动领取 **4**、瓦库事件自动选择完成 **6**、`让真人插队` **3**、`熔断跳过` **6**（历史局 4~72，同量级）；
- **读档后瓦库照常干活**（L13040 重新初始化 → L13192/L13523 事件自动选择完成、L14129/L15381 出牌统计）⇒
  B1 的席位快照在"读档"这条历史盲区上同样正确重建；
- 我方 `[ERROR]` / `[ERROR] [LocalMultiControl]` **0**、我方 NRE **0**、`add_child() failed` / 幽灵弹层 /
  `ObjectDisposedException` / `裸异常块` / `Couldn't get hand node` / 看门狗重启失败 **全 0**；
  `PATCH_RESULT critical=25/25 optional=15/15 total_patched=186` 与 r175/r176 **逐字相同**；
- 我方 WARN 模板与 r176 对比：r178 多出的 6 个模板**全是既有的熔断 / 看门狗 / 药水动画族**
  （r176 是只进厅的短局，本来打不到），**没有一条来自 B1 新代码**；`only in r176` = **0**（老告警一条没消失）。
- 噪音（非我方，均为已知）：12 条 NRE 全是第三方 `RitsuLib` 反射注册（与 r173/r175/r176 同基线 12 条）；
  5 条 `[ERROR]` = Manosaba/ddu 分支 2 + BetterModMenu 超时 1 + 游戏侧存档删除 2（前文已定性的噪音）。
- 未覆盖（本局没走到，不算失败）：`仅关键节点` peek 回切、`已跳过回合开始抽牌演出`、`[手牌同步修复]`、
  蝗虫偷牌收敛、次级资源归属校正。

**验证契约（请实机，marker `2026-09-28-r179`）**
```
改动:    R3 B1b —— 席位归属判定统一到 LocalSeatSource.IsLocalSeat（16 文件 28 处，行为零变化）
SETUP:   本地多控 2~4 席（真人 + 至少一个瓦库托管席位）；一局里尽量走到战斗 + 事件 + 休息区（+ 商店/宝箱）
ACTION:  正常玩：战斗结束领奖励、瓦库自动出牌/自动选事件、休息区选择、必要时读档一次
PASS:    `会话席位自检通过: seats=…` 照旧；奖励/事件/休息区行为与 r178 完全一致；
         席位相关锚点照旧（`卡牌奖励已自动领取` / `瓦库事件自动选择完成` / `瓦库休息区…` / `共享遗物同步` 等）
FAIL:    奖励发错人 / 瓦库该动的席位不动、不该动的动了；第三方席位（CB 合成 Bot）被我们代管
期望 0： ### Exception ### / add_child() failed / 我方 NullReferenceException / 归属者残留
```

**✅ 实机结论（2026-09-28，marker `2026-09-28-r179`，日志 `logs-archive/godot__20260928-200133__r179.log`）**
本局是**读档续玩**（`本地多控读档自动就绪` ×1，故无 `Embarking` 行），长局：席位自检通过 ×1（3 席齐全）/
冲突 0、卡牌奖励自动领取 **20**、瓦库自动出牌 **35**、休息区 **33**、火堆自动指定 **4**、商店自动买药水 **1**、
就绪补齐 **39**、`汇总奖励流程中跳过遗物镜像` **3** ⇒ 席位判定的主要消费面都走到了。
- 失败判据全 0：`归属者残留已改写` / `保留为人工领取` / 第三方席位被代管的迹象 / 奖励发错人；
- 我方 `[ERROR]` **0**、我方 NRE **0**、`add_child() failed` / 幽灵弹层 / `ObjectDisposedException` /
  `InvalidOperationException` / 选择器作用域异常退出 / 看门狗重启失败 / `Couldn't get hand node` **全 0**；
  `PATCH_RESULT` 与 r175/r176/r178 逐字相同；12 条 NRE 全是第三方 `RitsuLib`（同基线）；
- **两条"哨兵命中"已定性为非异常（口径订正）**：
  ① `检测到真人选牌请求` **16** —— 文案是「本次跳过瓦库选择器改走正常UI: chooser=…326」，
  即模组检测到**真人**该选牌就让位 ⇒ 是**正向信号**；跨会话 r172 **28** / r167 **6** / r161 **12**（r178 为 0
  只因那局没有真人选牌请求）。**今后不要再把它当"期望 0"**。
  ② `手牌UI与数据存在差异但未处理（仅记录）` **6** —— 文案自带"仅记录"，是既有诊断；跨会话 r172 **23** /
  r169 **1** / r161 **4**。
- 我方 WARN 模板对比 r178：r179 多出的 20 个模板**全是既有族**（熔断 `overlay-open` / 看门狗 /
  药水与遗物动画跳过 / 安全网事件滞留超时 / 藏宝图 quest 丢失兜底 / 弹层阻挡 / 手牌差异 / 手牌点击能量不足），
  并逐族跨会话核对过（藏宝图 4 vs r172 9；安全网 2 vs 4；弹层 4 vs r172 3 / r161 8；看门狗 7 vs 12 / 5；
  出不了牌 2 vs 5 / 11）⇒ **B1b 没有引入任何新告警来源**；`only in r178` 仅 1 条（同族的另一瓶药水 id）。

**下一批（待拍板）**
- **B1c（可选）**：驱动三态判定统一 —— `LocalSelfCoopContext.IsWakuuEnabled` / `IsCoopBotsDriven` 的调用点
  改问 `SeatRegistry.DriverOf`。⚠ 需要先拍一个语义问题：两处命中时 `IsWakuuEnabled` 返回 true，
  而 `SeatRegistry.IsWakuuDriven` 按三态互斥**以联机机器人为准**（返回 false）—— 现状靠写入侧保证互斥，
  所以要在"逐字等价"（包一层集合判定）与"顺带把互斥判定拉齐"之间选一个。
- **是否给"席位判定唯一入口"加棘轮**（防回头路）：ADR §五 曾定「不新增 S 项承担架构职责」，
  所以本轮**没有**加门禁；若要，可加一条只查 diff/新增代码的棘轮（把裸 `LocalPlayerIds.Contains` 判 FAIL）。
- **B2 ✅ 已落地并实机通过（r180；2026-09-28 第十四段按日志关单）**：奖励 / 掉落 / 药水 / 商店归属的
  「当前归属者」读取收编（8 处，行为零变化）—— 见上方「第四轮 B2」与验证契约；
  剩余 route ① 读取清单与"哪些点不是身份判定"已写进 ADR §八。
- **B2b ✅ 已落地（r184，待实机；行为零变化，同批还含已实机关单的 BUG-23 A/B 与工具层哨兵口径修正）**：
  剩余 route ① 读取收编 **10 处** —— 动作队列兜底 `ActionQueueFailSafePatch:94/131/156`
  （`NetId ?? PrimaryPlayerId` → 新访问器 `LocalSeatSource.ContextOrPrimarySeatId()`，⚠ 刻意**不是**
  `ForegroundOrPrimarySeatId`，旧口径没有受控位层）+ `ActChangeSynchronizerPatch:35` /
  `LocalGhostHandsRuntime:351` / `CardTransformNetIdPinPatch:78/100` / `CardPileAddForegroundContextPinPatch:55` /
  `EventSynchronizerPatch:52` / `PlayerChoiceContextPatch:37`（钉扎前保存原值的读侧；写侧与还原照旧）。
  留原地：`SynchronizationOwnershipLogPatch:47` / `RestSitePatch:62` / `RewardsSetSynchronizerSelectLocalRewardPatch:87`
  → **B3**；`NPlayerHandSelectCardsSerializationPatch:111` → **B4**；`NCustomRunLocalSelfCoopPatch:285` → 大厅线。
- **B3 ✅ 已落地并实机通过（r188，2026-09-30，行为零变化）**：第三方同步器私有 `_localPlayerId` 的反射读写收编到唯一入口
  `Scripts/Runtime/SynchronizerLocalPlayerId.cs`（按类型缓存 `FieldInfo`；`TryRead` / `ReadOrZero` / `TryWrite`；
  **不吞异常、不打日志** ⇒ 各调用方原有的 try/catch 与 WARN 文案逐字不变）。收编 **12 处 / 9 文件**：
  读 —— `RewardsSetSynchronizerSelectLocalRewardPatch:71`（字段缺失即 early return）、`EventSynchronizerPatch:92`
  （`TryRead ?? 受控位 ?? 0`）、`RestSitePatch:159`、`HookPlayerChoiceContextLocalPatch:36`、
  `LocalRewardMirror:69`（`ReadOrZero`）、`CombatRewardMergeContext:213`、
  `SynchronizationOwnershipLogPatch:48`（按运行期类型读，等价旧 `target.GetType()`）；
  写 —— `RewardsSetSynchronizerSelectLocalRewardPatch:130`（还原写）、`HookPlayerChoiceContextLocalPatch:48`、
  `LocalMultiControlRuntime.TrySetLocalPlayerId`、`LocalWakuuMerchantAuto.TrySetLocalPlayerId`、
  `CombatRewardMergeContext.SetLocalPlayerId`。复核：仓库内 `AccessTools.Field(…, "_localPlayerId")` **= 0**；
  S7 语义标识 370 → **366**（消失的 4 条正是原来"各自反射"的目标，已人审后刷新基线）；单测 **762 → 769**；
  marker **`2026-09-30-r188`**、`dll_check --deployed` 字节一致（sha256 `d40ca7607170…`）。
- **B4 + B5 ✅ 已落地并实机通过（r189，2026-09-30，行为零变化）= route ① 收尾**：
  **6 处读点改走 `LocalSeatSource.ContextSeatId()`**（全是"钉扎前保存原值 / 上下文兜底读"，写侧与还原照旧）——
  `NPlayerHandSelectCardsSerializationPatch:111`（选牌主人域）、`RewardsSetSynchronizerSelectLocalRewardPatch:85`、
  `LocalWakuuMerchantAuto:111`、`LocalWakuuRelicRuntime:942`（看门狗）、`RestSitePatch:62`（取不到再回退同步器私有字段）、
  `SynchronizationOwnershipLogPatch:48`（诊断读；与上下文同源同值）；
  另把 **8 处"不是身份判定"的点就地定性注释**（免得下轮再全仓盘一遍）：`NPlayerHandAddOwnerGuardPatch:53`（卡片主人 ↔ 选牌主人）、
  `LocalGhostHandsRuntime:360`（排除自己）、`LocalLoopbackHostGameService:239`（枚举非主席位玩家）与 `:260`（回环服务自身即上下文载体）、
  `LocalSelfCoopContext:310`（席位表**源级**谓词）与 `:560`（意图缓存比较）、`LocalQuickRestartLoader:63`（按主席位 id 查存档玩家 —— lookup，非判定）、
  `ProgressSaveManagerPatch:57`（**平台身份**，口径已由纯逻辑 `RunProgressLocalPlayerPolicy` 统一，r155）。
  复核（本机工具 `tools/identity_read_audit.py`）：**route ② 在 `LocalMultiControlRuntime` 之外 = 0 处**；
  route ① 剩余"真读取"只剩 `LocalLoopbackHostGameService`（已注释的来源自身）与大厅线的 `NCustomRunLocalSelfCoopPatch:285`。
  单测仍 **769**（本批无新纯逻辑）；S7 基线不变（366）；marker **`2026-09-30-r189`**、
  `dll_check --deployed` 字节一致（sha256 `320cd65bbedc…`）。
- **未归批（已定性）**：`NGameOverScreen._localPlayer` —— 是**结算页节点自己的"当前展示玩家"引用**（反射只为读它的
  `Character.Id`），不是身份来源 ⇒ 不接 `LocalSeatSource`；R4 拆类时随 UI 层再看。
- **B1c（驱动三态）✅ 已评估并关闭（2026-09-30，不做代码替换）**：驱动事实源本来就只有一处
  （`LocalSelfCoopContext._wakuuPlayerIds` / `_coopBotsPlayerIds`，写入侧 `SetWakuuEnabled` / `SetCoopBotsDriven`
  已强制互斥），消费点全是调 `IsWakuuEnabled` 这**一个**方法 ⇒ 换成 `SeatRegistry.DriverOf` 只多一层间接；
  而"三态互斥"口径在"两处同时命中"（当前不可达）时会改变结果，不属"行为零变化"。
  **结论**：`IsWakuuEnabled` / `IsCoopBotsDriven` 保留为**驱动事实源的源级原语**（已加注记，指明 B1c 为何没做），
  **新代码**要"互斥的唯一答案"就显式问 `LocalSeatSource.CurrentSeats().IsWakuuDriven(id)`。
  ⇒ **R3（身份收编）到此收口**（R0/R1/R2/R3 全部落地，下一步进 R4）。

**验证契约（请实机，marker `2026-09-30-r188`）**
```
改动:    R3 B3 —— 第三方同步器私有 _localPlayerId 的反射读写收编到唯一入口（12 处 / 9 文件，行为零变化）
SETUP:   本地多控 2~4 席（真人 + 至少一个瓦库托管席位）；一局里尽量走到 战斗 + 事件 + 休息区 + 商店
         （能读档一次更好 —— 读档后瓦库席位/上下文重建是这条链路的盲区）
ACTION:  正常玩：战斗出一张 Owner 不是前台的牌（如 hook 类效果牌）、事件里让瓦库自动选、
         休息区选择、商店自动买/手动删牌、领战后卡牌奖励（必要时先切人再点领取）
PASS:    行为与 r185 完全一致：
         `奖励领取按归属角色绑定: owner=…, syncLocal=…` 与紧跟的 `奖励领取归属已恢复: source=…` 成对出现；
         `瓦库事件自动选择完成` / `瓦库休息区…` / `瓦库商店自动买…成功` / `奖励-拿牌归属玩家: context=…, syncLocal=…` 照旧；
         `本地多控：hook 选择上下文 _localPlayerId 已强制归属到所选角色 …` 只在真的归属不一致时出现；
         `会话席位自检通过: seats=…` 照旧
FAIL:    奖励领不了 / 领到错误角色；瓦库不出牌或事件不自动选；商店删牌删错人；
         `同步 … 的 _localPlayerId 失败`（新入口找不到字段=反射目标变了）
期望 0： ### Exception ### / add_child() failed / 我方 NullReferenceException / 幽灵弹层 /
         `SelectLocalReward` 相关游戏侧报错
```

**✅ 实机结论（2026-09-30，marker `2026-09-30-r188`，日志 `logs-archive/godot__20260930-134143__r188.log`）⇒ B3 关单**
2 席（真人 326 + 瓦库席位 327）、**读档续玩 ×3** 的长局（`瓦库出牌` 352 / `选择器作用域进入` 67 /
`自动领取` 27 / `瓦库商店自动买` 8 / `瓦库火堆` 5 / `瓦库事件自动选择完成` 6 / `让真人插队` 11）。
- **唯一入口的四条真实链路都被走到**：① `RewardsSetSynchronizer` 读 + 还原写 ——
  `奖励领取按归属角色绑定: owner=…326, syncLocal=…327 -> …326` 与 `奖励领取归属已恢复: source=postfix, syncLocal=…327`
  **成对 1 次**（正是"领取时同步器归属指向另一个角色"的错位场景）；② `HookPlayerChoiceContext` 读 + 写 **13 次**（326↔327 来回）；
  ③ 按运行期类型读（诊断补丁）—— `奖励-拿牌归属玩家: context=…, syncLocal=…` / `商店-删牌归属玩家: …` 两侧数值一致；
  ④ `CombatRewardMergeContext` 读（`合并奖励展示集` 7）+ `LocalWakuuMerchantAuto` 写（随 8 次商店自动采购）。
- **失败哨兵 0**：`同步 … 的 _localPlayerId 失败` 0（找不到字段 / 写失败都会打这条）、
  `合并奖励展示集…不一致` 0、`休息区升级切换失败` 0。
- 期望 0 全 0（`### Exception ###` / `add_child() failed` / 我方 NRE / `Couldn't get hand node` / 幽灵弹层 /
  `保留为人工领取` / `归属者残留已改写` / `手动出牌上下文漂移`）；`PATCH_RESULT critical=25/25 optional=15/15
  total_patched=186` 与 r184/r185 **逐字相同**；`BUILD_IDENTITY commit=5c6a44b state=dirty`（= master HEAD + 本批改动）。
- 读档契约顺带覆盖（BUG-22 无回归）：窗口 开/关 各 3、恢复玩家 3、恢复瓦库席位 3、`读档后自动选事件` 6、`读档后瓦库出牌` 67，5 条哨兵全 0。
- 噪声（均非我方）：`[ERROR]` 4 = Manosaba/ddu 分支不匹配 2 + BetterModMenu 超时 1 +
  `Tried to add hand for player … twice!` 1（**既有偶发**：r169 1 / r172 3 / 其余 0）；NRE 12 全为第三方 `RitsuLib`（同基线）。
- 我方 WARN 模板对比 r186 / r184：本局独有的 13 个模板**全是既有族**（看门狗 / 熔断 / 药水动画 / 藏宝图 quest 兜底 /
  手牌点击出不了牌）⇒ **未引入新告警来源**。
- 未直接命中（不影响判定）：`RestSitePatch` 反射读（仅上下文为空时兜底）、`LocalRewardMirror` 反射读（本局 0 次镜像）、
  `CombatRewardMergeContext` 写（仅归属不一致时才写）—— 三处与已覆盖链路共用同一入口。

**验证契约（请实机，marker `2026-09-30-r189`）**
```
改动:    R3 B4+B5 —— route ① 剩余读点收编（6 处：钉扎前保存原值 / 上下文兜底读）
         + 选牌/存档域的"非身份判定"点就地定性注释（行为零变化）
SETUP:   本地多控 2~4 席（真人 + 至少一个瓦库托管席位）；一局里走到 战斗 + 事件 + 休息区 + 商店；
         最好用一张"战斗内手牌选牌"类卡（如 炉心融解 / MiniHakkero / 复制牌）触发选牌串行化路径
ACTION:  正常玩：战斗内触发手牌选牌（让后台角色触发 hook 类效果牌）、让瓦库后台出牌、商店自动采购、
         休息区选择、领战后奖励；能读档一次更好
PASS:    行为与 r188 完全一致：
         `战斗内手牌选牌串行化: 已进入选牌 …` / `选牌展示前已切换前台到所属角色` /
         `[选牌诊断] NPlayerHand UI holder: owner=…, LocalContext.NetId=…`（选牌期间两值应一致 —— 上下文被钉到选牌人）；
         `奖励领取按归属角色绑定/已恢复` / `奖励-拿牌归属玩家: context=…, syncLocal=…` /
         `瓦库商店自动买…成功` / `瓦库火堆…` / `卡牌奖励已自动领取` 照旧；`会话席位自检通过: seats=…` 照旧
FAIL:    选牌界面串到对家手牌 / 选牌卡住不出结果；奖励领不了或领错人；`休息区升级切换失败`
期望 0： ### Exception ### / add_child() failed / 我方 NullReferenceException / 幽灵弹层 /
         `同步 … 的 _localPlayerId 失败`
```

**✅ 实机结论（2026-09-30，marker `2026-09-30-r189`，日志 `logs-archive/godot__20260930-140636__r189.log`）⇒ B4+B5 关单**
2 席（真人 `…326` + 瓦库席位 `…327`）、读档 1 次、中局长度的第二幕（`瓦库出牌` 63 / `选择器作用域进入` 11 /
`自动领取` 7 / `瓦库商店自动买` 10 / `瓦库火堆` 3 / `卡牌奖励已自动领取` 2）。
- **本批改动的站点被真实走到**：① **战斗内手牌选牌串行化 ×2**（`mode=SimpleSelect`）——
  `选牌展示前已切换前台到所属角色: player=…326`，且 `[选牌诊断] NPlayerHand UI holder: owner=…326,
  LocalContext.NetId=…326` **两值一致**（选牌期间上下文被钉到选牌人）；`选牌守卫` 0（没有跨手牌串台）；
  ② `奖励-拿牌归属玩家: context=…, syncLocal=…` **7 条**、两值一致（诊断读改走入口后仍与同步器归属同值）；
  ③ 商店自动采购 3 张卡 + 遗物/药水决策（`LocalWakuuMerchantAuto` 的钉扎读）；④ `瓦库火堆` 3（休息区路径）。
- **失败哨兵 0**：`同步 … 的 _localPlayerId 失败` 0、`休息区升级切换失败` 0、`奖励归属错位` 0；
  期望 0 全 0（`### Exception ###` / `add_child() failed` / 我方 NRE / `Couldn't get hand node` / 幽灵弹层 /
  `保留为人工领取` / `归属者残留已改写` / `手动出牌上下文漂移`）；`PATCH_RESULT 25/25·15/15·186` 与 r188 逐字相同；
  `BUILD_IDENTITY commit=eda0348 state=dirty`。
- 读档契约（1 次）：窗口开/关 1、恢复玩家 1、恢复瓦库席位 1、`读档后自动选事件` 1、`读档后瓦库出牌` 11，5 条哨兵全 0。
- 噪声（均非我方、均既有）：`[ERROR]` 6 = Manosaba/ddu 分支 2 + BetterModMenu 超时 1 +
  **游戏侧 `NHeavyBluntVfx.PlaySequence()` NRE 1**（栈里我方 0 帧，与历史 `NStarryImpactVfx`/`NHeavyBluntVfx` 同族）+
  游戏侧存档删除失败 2；NRE 13 = 12 条第三方 `RitsuLib` + 上述 1 条游戏侧。
- 我方 WARN 模板对比 r188：本局独有 5 个模板全是既有族（手牌点击出不了牌 / 手牌点击被忽略 / 跳过药水动画）⇒ **未引入新告警来源**。

---

## R4 拆 God class（`LocalMultiControlRuntime.cs`，2026-09-30 起）

> 提案：`runtime架构分层重构评估.md` §四 R4 + §八（该文件在仓库外维护）。
> 目标：**按职责把 2800+ 行的 Runtime 拆成独立单元**；每刀行为零变化 + 一局实机；
> 拆出去的单元优先选"职责单一、能独立读懂（能单测更好）"的，Runtime 侧只保留编排与来源自身。

**已落地**
- **第一刀（r190，✅ 2026-09-30 实机通过）= Run 级同步器的「本地玩家」对齐**：新增 `Scripts/Runtime/RunSynchronizerSeatSync.cs`
  —— `Apply(playerId)` 把 7 个 Run 级同步器（`Event` / `RewardsSet` / `Reward` / `RestSite` / `OneOff` /
  `TreasureRoomRelic` / `Flavor`）的私有 `_localPlayerId` 对齐到当前归属角色；
  **`EventSynchronizer` 走"事件流所属者"这条差异原样保留**（`UseSingleEventFlow` 时钉主席位，`FoulPotionPatch` 依赖它）；
  写入失败按 `组件:类型` 去重只记一条 WARN（键格式与文案逐字不变）。
  从 Runtime 搬走 `SyncRunSynchronizerLocalPlayerId` + `TrySetLocalPlayerId` + `_fieldSyncFailures`
  （5 个调用点 → `RunSynchronizerSeatSync.Apply(...)`）；新单元**不再自带反射**，一律走 B3 的唯一入口
  `SynchronizerLocalPlayerId` ⇒ Runtime 里最后一处"自己写 `AccessTools.Field(..., "_localPlayerId")`"随之消失。
  Runtime 净减 ~35 行；构建 0 警告 0 错误（**243 .cs**）、单测仍 **769**、S7 基线不变；
  marker **`2026-09-30-r190`**、`dll_check --deployed` 字节一致（sha256 `8abeffc05dcb…`）。

**下一刀候选**（按内聚度 / 风险排序；每刀单独一局实机）
1. **切换目标选择**（`TrySwitchCombatPlayer` / `TrySwitchToNext*` / `TryAutoSwitchToNonWakuuOncePerRound` /
   `BuildWakuuSwitchRoundKey` …）—— 内聚度高，且其中"该切谁"能提成纯逻辑进 `PureLogic` 并补单测；
2. **弹层 / 转场诊断与兜底**（`DumpControlVisibilityChain` / `DumpTransitionOverlayState` / `CollectTransitionNodes` /
   `EnsureOverlayNotCoveredForRewards` / `IsLoadReplayTransitionCovering`）—— 自包含、低风险；
3. **前台 / 上下文对齐**（`ApplyControlContext` / `AlignContextForActionOwner` /
   `AlignLocalContextToForegroundForEndTurn` / `TryEnsureForegroundForPlayer`）—— 热路径，风险最高，放最后。

**验证契约（请实机，marker `2026-09-30-r190`）**
```
改动:    R4 第一刀 —— Run 级同步器「本地玩家」对齐抽成 RunSynchronizerSeatSync（行为零变化）
SETUP:   本地多控 2~4 席（真人 + 至少一个瓦库托管席位）；一局里尽量走到 战斗 + 事件 + 商店 + 休息区 + 宝箱
ACTION:  正常玩：中途切人后让瓦库领奖励 / 事件里换人再选 / 进商店（自动买 + 手动删牌）/ 休息区选择 / 开宝箱遗物投票
PASS:    与 r189 完全一致：切人后奖励归属正确、`奖励-拿牌归属玩家: context=…, syncLocal=…` 两侧一致、
         `瓦库商店自动买…成功`、`瓦库事件自动选择完成`、`瓦库火堆…`、宝箱投票正常推进；`会话席位自检通过: seats=…` 照旧
FAIL:    切人后奖励 / 事件 / 商店删牌认错人（这正是本单元要防的症状）；`同步 … 的 _localPlayerId 失败`
期望 0： ### Exception ### / add_child() failed / 我方 NullReferenceException / 幽灵弹层
```

**✅ 实机结论（2026-09-30，marker `2026-09-30-r190`，日志 `logs-archive/godot__20260930-204056__r190.log`）⇒ R4 第一刀关单**
本机当晚跑了 3 次 r190 会话（20:28 / 20:33 / 20:40），**三次全部 `INIT_OK`、`INIT_FAILED` 0**；
主局（20:40，1.7MB）2 席（真人 `…326` + 瓦库席位 `…327`）：
- **本单元要防的症状被正面覆盖**：① `奖励领取按归属角色绑定: owner=…326, syncLocal=…327 -> …326`
  与 `奖励领取归属已恢复: source=postfix, syncLocal=…327` **成对 1 次** —— 正是"切人之后同步器归属还是上一个人"
  的场景，抽取后的 `Apply()` 按原口径改绑并还原；② `商店-删牌归属玩家: context=…326, syncLocal=…326`
  （`OneOffSynchronizer` 也是这 7 个之一）⇒ 删牌认人正确；③ `瓦库事件自动选择完成` **5**（`EventSynchronizer`
  走"事件流所属者"这条特例照旧）；④ 宝箱 `宝箱已禁用自动代投，改为逐角色手动选择` +
  `宝箱选择完成后自动切换到下一位未选角色`（逐角色投票正常推进）；⑤ `瓦库火堆` 3、`瓦库商店自动买` 3（本局金币 19，
  三条都是"不买"的正当决策）。
- **失败哨兵 0**：`同步 … 的 _localPlayerId 失败` **0**（去重 WARN 的键与文案未变）、`休息区升级切换失败` 0；
  期望 0 全 0（`### Exception ###` / `add_child() failed` / 我方 NRE / `Couldn't get hand node` / 幽灵弹层 /
  `保留为人工领取` / `归属者残留已改写` / `手动出牌上下文漂移`）；`PATCH_RESULT` 与 r189 逐字相同。
- 噪声（三次会话合计，均非我方且均既有）：Manosaba/ddu 分支不匹配 ×2、BetterModMenu 超时 ×1、
  游戏侧存档删除失败 ×2（仅主局）、`[SteamHost] Error creating steam lobby! k_EResultNoConnection` ×1（20:28 局，Steam 侧）。
- 我方 WARN 模板对比 r189：本局独有 4 个模板**全是既有族**（藏宝图 quest 兜底 / 托管遗物缺失当场补发 /
  选择器栈自恢复 / 跳过药水动画）⇒ **未引入新告警来源**。
- 未直接命中（不影响判定）：`奖励领取按归属角色绑定`（"领取时同步器归属错位"是数据相关场景，本局没出现）、
  `RestSitePatch:62` 的上下文兜底（只在上下文为空时才回退同步器私有字段）—— 两处与已覆盖链路共用同一入口。
