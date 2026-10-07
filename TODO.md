# TODO（下一步做什么）

> **口径**（`AGENTS.md` §8.1）：本文件**只写「接下来做什么」**（待办 / 待拍板 / 未定根因）。
> 已关单的项**不留正文**：玩法可见的变化看 `CHANGELOG.md`；做法与教训进 skill 侧 `references/`；
> 逐轮验收流水进 `D:\Download\pain\开发进度记录.md`；
> **2026-09-08 ~ 2026-10-06 的已关单 bug / 功能流水**（含根因、复测契约、实机结论）已切到
> `maintenance-docs\decision-records\archive\TODO-归档（已完成 bug 与功能流水·r96~r226）.md`（**仓库外、不进 GitHub**）。
> **现状**（分支 / marker / 未推送提交数 / 版本 / 部署位）只看
> `D:\Download\pain\开发进度记录.md` 顶部最新「🔚 本轮收工」块，本文件不复制。
> 更新：**2026-10-07**（**3152 → 339 行**：切出已关单流水 + 跟踪表压缩为一行一案）。

---

## 决策记录跟踪表（提案生命周期 · 唯一事实源）

> 提案的**清单与定位**以 `maintenance-docs\decision-records\README.md` 为准（冲突时以那份索引为准）；
> 本表只记**工作项状态**（谁做、做到哪、下一步）。登记纪律见 `AGENTS.md` §8.1：
> 新 proposal 落地当天 → 索引加一行 + 本表挂一行（或明写「仅调研，不排期」）；**登记 ≠ 排期、≠ 承诺动工**。
> 「索引 #N」= `decision-records\README.md` §一 的编号（`maintenance-docs\` 无 git，不进 GitHub）。

| 提案 / 工作项 | 现状 | 下一步 | 落在哪 |
|---|---|---|---|
| `瓦库托管优化可行性分析.md`（Phase 1~5 主线活文档） | 部分落地（r3~r139）；§18.2 评分大脑已随 r142 落地 | §21.3.4 求解器适配器 / 逐卡评级**搁置（2026-10-01）**，不排期 | 索引 #1（再评估条件见索引 §五） |
| `瓦库托管最终阶段-实施草案.md`（M1~M4） | **搁置（2026-10-01 拍板，不排期）**；M1 知识层已落地（r153）并随 v1.43.0 发布 | M2~M4 不再排期 | 索引 #3、§五 |
| `STS2…AI总规范_Final_v2.md` | 规范稿（无落地） | 只作 M1~M4 的设计约束，不单独动工 | 索引 #2 |
| `瓦库托管基础收口草案-最终阶段前置G0.md` | G0.1 已被商店自动化吃掉（r137/r139）；G0.2-B5（实机回归清单）✅ 2026-10-05 | 只剩 **B4（恋降级逃生门）** 待拍板（依赖第三方识别、场景仅古明地恋一例） | 索引 #4 + `references\实机回归清单.md` |
| `多瓦库并行托管可行性与方案.md` | 已实机确认（Phase 0 r107~r110 / Phase 1 r113 / 方案 D r121~r134）；**队列 + 并发两档已于 2026-09-26 转正为默认开（r152）** | 无（加速档 `fastVakuuPlay` 默认关，用户自选） | 索引 #5 + 该方案正文 |
| 改进-2 出牌加速二期（`CardModel.OnPlayWrapper` 前缀省固定等待） | ✅ **已落地为实验档（r132/r133）**：键 `fastVakuuPlay`，默认关；r133 修了「卡面滞留在出牌区」 | 无（用户想常年开时自己在设置页开） | 该方案 §12.14 + 归档（逐轮 ⑲⑳） |
| `runtime架构分层重构评估.md`（#18） | ✅ **R0~R5 全部收口**（2026-10-04 实机验收完） | 无 | 索引 #18 + 该提案 §十 |
| `长期方向L1-L3规划.md` | 部分落地：L1 接口层（r31）/ L3 离线静态层 CI（2026-09-16）；L2 已扩为 #18（已收口） | L1 剩「反射面收敛」—— 2026-10-05 侦察后判定**不值得单开一轮** | 索引 #6 + 该规划 §L1 |
| `键盘手柄双输入本地双控可行性分析.md` | **待拍板**（结论已出：L1 轮流操作可行 / L2·L3 真正同时不可行）；未实现 | 待拍板（备选线索：`LocalDeviceSplitRouter` + `NControllerManager` 模式抢占 + 秒切防抖） | 索引 #7 |
| `本地LLM辅助开发可行性分析.md` | 部分落地：P0~P3 + T1/T2 全完成；P4 经实测改换做法 | P4 若要继续：自写 `sts2src` 逐卡效果抽取器 → 人审 → 固化 `PureLogic` 表（默认关） | 索引 #8 |
| `开发环境迁移Linux可行性分析.md` | 未动工（纯调研，未动 U 盘/分区） | 待拍板：先跑 Phase 0 / Phase 1，跑完前不动 U 盘与分区 | 索引 #12 |
| `本地多角色扩展到Daily模式可行性分析.md` | ✅ 已落地 + 联网/断网各一局实机通过（r161），随 v1.43.0 发布 | 可选增强（>4 席 / 1p 榜上传 / 每日页 UI 精修 / `NDailyRunLoadScreen` 适配）—— 用户「先不做」 | 索引 #9 + 归档（Daily 记录） |
| `瓦库四功能-可行性核验报告.md`（#10）+ `瓦库炼化净化联合地狱战神…提案`（#11） | **①②③④ 全部 ✅ 关单**（净化 r212 / 我们联合 r214 / 炼化 r223 / **④「瓦库的爹」= 遗物 + 三张牌 r227~r231**；均已合回 master、随 **v1.45.0** 发版） | 无（④ 效果线已收口：我挡 ✓ / 你攻 ✓ / 合体 ✓ + 退局复位 ✓，r231 三局复测全绿）；可选只剩**数值平衡调整** —— 见下方 §④ | 索引 #10/#11 §7 拍板附录 + 归档（①②③ 记录） |
| `Co-op_Bots联机队友兼容可行性分析.md` | 部分落地：r144~r148 兼容层已落地并实机通过；**POC 接管路径 2026-09-26 实机后决定不做**（冻结为能力保留，`coopBotsSeats` 默认空） | 未做（非排期）：12 席三态 UI/持久化、Phase 3 补丁面回归、被接管本地席位的奖励分流 | 索引 #13 + 下方 §BUG-17 |
| `局内加人与战斗临时玩家-可行性分析.md` | 未动工（调研结论稿：A 局内加正式玩家风险高 / B 战斗临时玩家可行且有抓手） | 是否排期待拍板（与 ④ 战灵召唤需求同源） | 索引 #16 |
| `原版药水一览表.md` / `原版附魔一览表.md` | 参考数据表（非提案）；药水表 r136 已校正 | 改规则表时同步；附魔侧「优先选哪类牌」待用户填期望 | 索引 #14 / #15 |
| `第三方补丁目标交叉分析-CoopBots.md`（#17） | 参考表（脚本产物，只读勿手改） | 无 | 索引 #17 |
| `补丁mod收进主仓库compat-mods.md`（#20） | ✅ 已落地（2026-10-05）；范围已收窄为「主 mod ↔ 第三方交互」补丁 | 剩 **push origin**（需用户确认） | 索引 #20 |
| **战灵召唤（原 ④「地狱战神」）**（#21） | 未动工；前置功课「自定卡进卡池」✅ r224~r226（已随「瓦库的爹」转正） | 🅿️ **排期 = 队尾**：先拍板 doc §7 的 D1~D13，再做 Phase 0 POC | 索引 #21 + 下方 §战灵召唤 |

> 注：`PrimeVakuu决策桥接-本地转接头可行性分析.md`（#19）是**本地私有**提案（对方未开源），
> **不进本表、不进任何公开载体**，只登记在 `decision-records\README.md`。

---

## 待办（未定根因 / 待观察 / 暂不修）

> 下列都是「能用但别扭」或「偶发自愈」的问题，**用户已拍板先记录不修** —— 复现 / 想修时按各条自己的
> 判据与备选路线走（逐条已写好现象、日志锚点、根因分析与修法备选）。
> 同类既有备案：`maintenance-docs\decision-records\瓦库托管优化可行性分析.md` §16.2。

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

---

## 维护性改进 backlog（门禁体系 2026-09-08 之后的那批）

已落地（明细见 `AGENTS.md` §9 门禁表 + `Scripts/Tools/`）：源码隔离 Guard、单元测试门禁、
CLR/PE/Assembly 三层兼容检查、Critical/Optional 补丁分级、`INIT_OK`/`INIT_FAILED` 终态收口 + 统一错误码、
`dll_check` false-green 修复、marker 缺失 FAIL、`log_parser --init-status`、Harmony owner 冲突检查（r88）、
BuildIdentity 增强（r89）、部署槽唯一性检查（r93）、标准化回归验证契约（AGENTS §10）、
`ExpectedPatchTargets` 完整签名（r92）、发布包构建元数据（2026-09-19）、S8 源码编码卫生、
格式存量收口（2026-10-05，口径 = 不把 `dotnet format --verify-no-changes` 加进强制门禁链）。

仍挂着的只有一项：

1. **版本 lane + 根 loader（**待正式版更新再做**，用户 2026-09-19 拍板）**：
   现状 = 只吃当前游戏版本（public-beta `0.111.0`），而原作者的 mod 依旧适配现在的正式版
   ⇒ 现在做没有任何收益，**先不做**。等**正式版更新**（我们和原作者都不得不跟版本）时再动手：
   根目录放一个极薄的 **loader dll**（读 `release_info.json` 判定宿主游戏版本 → 用自定义
   `AssemblyLoadContext` 从 `lanes\<gameVersion>\` 加载对应实现），参照社区两个工坊作品
   （`3799476240` CouchCoop 的 `lanes/0.107.1` + `lanes/0.111.0`；`3802686135` LocalCoopClone 的 `lib/<ver>/`）。
   我们侧额外成本：每个 lane 需要一份**独立的引用路径配置** + 各自重跑 `regenerate_src.ps1`
   + S7 目标基线按 lane 拆（`targets.baseline.txt`）。
   ⚠ 前置判断：**只有真的需要同时支持两个游戏版本时才做**，单版本下 lane 纯属增加复杂度。

## ④「瓦库的爹」（遗物 + 三张牌）—— ✅ 全部实机关单（r227~r231，2026-10-07），已随 **v1.45.0** 发版

> 命名口径（2026-10-06 用户拍板）：**「地狱战神」= 战灵召唤**（临时玩家召唤，下一节，排期队尾）；
> **本节这条（遗物 + 我挡 / 你攻 / 合体 三张占位牌）=「瓦库的爹」**（占位名，正式名字用户另有安排）。

- **首版 = 遗物 + 卡框架**（提案 §5.1 的"效果①" + 核验报告 §7 拍板第 ④ 条）：
  遗物【瓦库的爹】进 `EventRelicPool`；三张 0 费技能占位牌（`EventCardPool` / `Rarity=Event`，**效果留空**，
  升级加「保留」）；**每场战斗开始时给持牌人这 3 张**；开局只发给**真人席位**（开关 `vakuuDaddy`，**默认关**）。
- **代码落点**：`Models/Relics/LocalWakuuDaddyRelic.cs` / `Models/Cards/LocalWakuuDaddy{Shield,Focus,Merge}Card.cs` /
  `Runtime/LocalWakuuDaddyLocalization.cs` / `Runtime/WakuuDaddyContentProbe.cs` + `Patch/WakuuDaddyContentPatch.cs`
  （早期挂点 + 自检）/ `PureLogic/WakuuDaddyPolicy.cs`（发放判据，有单测）/ 发放点
  `LocalMultiControlRuntime.GrantWakuuDaddyRelicAsync`。前置功课那张验证卡（`HellGod*` 命名 + `[地狱战神验证]` 锚点）
  **已"转正"并清理**（锚点现为 `[瓦库的爹验证]`）。
- **✅ 实机通过（2026-10-07，marker `2026-10-07-r227`；两局 = 新开局 + 读档续局，2 席 = 真人 + 瓦库）**：
  开机验证锚点四项全绿 ✓ / 只给真人席位发而瓦库席位不发 ✓ / 读档续局**不重复发放** ✓ /
  两局各在 `round=1` 给 3 张 ✓ / **升级带「保留」已确认** ✓ / 卡牌库「其它」与遗物库可见 ✓ /
  `LocException`·`turn loop died`·`### Exception ###` 全 0 ✓。逐条证据见 `D:\Download\pain\开发进度记录.md` 第五十七段。
- **三条硬约束（照旧，别再犯）**：① mod 自定卡一律进 `EventCardPool`，**绝不放无色池**（一般方式可得，见坑 AA）；
  ② 入池登记必须**早于游戏初始化**；③ 本地化/自检挂 `LocManager.Initialize` / `ModelDb.Preload` 的后置补丁
  （**别挂"每次进局"** —— 卡牌库/遗物库在主菜单就能打开）。
### 三张牌的**效果实装** —— ✅ 收口（r228 实现 → r229 / r230 修复 → r231 退局复位；**r231 三局复测全绿**，marker `2026-10-07-r231`）

- **【我挡】**：给队友挂 `LocalWakuuDaddyShieldPower`（`ModifyUnblockedDamageTarget` 转给施牌者 +
  `ModifyDamageMultiplicative` = 0.5）—— 照原版 `DieForYouPower`（唯一的伤害重定向）+ `GuardedPower`（减半）
  两条先例拼出来；**只对攻击伤害**生效，敌方回合结束自删（照 `CoveredPower`）。
  **r229 补丁**：重定向发生在被挡者格挡结算**之后** ⇒ 承伤者原来直接掉血、用不了自己的格挡；现在在
  `ModifyHpLostAfterOsty`（重定向之后那条钩子）里用原版同一个 `DamageBlockInternal` 补一次格挡。
  ⚠ 该状态**故意不可见**：mod 没有 PCK ⇒ 图标路径固定取 `power_atlas` 里不存在的资源，
  可见时 `NPower.Reload` 会直接 `ResourceLoader.Load` 它 ⇒ 每次施加留一条引擎 ERROR（理由写在类注释里）。
- **【你攻】**：`WakuuDaddyCombatState.SetFocus` 登记，两个瓦库大脑（heuristic / scored）解析 `AnyEnemy` 时优先取它。
  为什么不做成原版机制：r228 翻遍 `sts2src`，**"敌人/玩家选谁当目标"没有任何钩子**（`Taunt` 只是普通卡），
  而瓦库的目标选择本来就在我们手里 ⇒ 在决策侧优先即可（纯逻辑可单测）。**✅ r228 实机通过**。
- **【合体】**：手牌改归属进施牌者手牌 + 能量搬运（`PlayerCmd`）+ 本回合
  混抽（`WakuuDaddyMergePilePatch` 的 `CardPileCmd.Draw` 前缀：把随机来源的牌搬进自己抽牌堆**顶部**后放行原版流程）
  / 混弃（`CardCmd.DiscardAndDraw` **后置**：原版正常弃完，再把牌搬去掷硬币选中的那一方弃牌堆）。**抽牌随机 ✅ r228 实机通过**。
  🔴 **搬牌顺序铁律（r230 才踩明白，务必照抄）**：`CardModel.Pile` 是**按 Owner 反查**的
  （`Pile => _owner?.Piles.FirstOrDefault(p => p.Cards.Contains(this))`）⇒ **先改归属再 `CardPileCmd.Add`**
  会让 `Pile` 变 null、`Add` 以为"牌不在任何牌堆里"而**跳过摘除** ⇒ 同一张牌挂两个牌堆。
  正确 = **先 `RemoveFromCurrentPile`（Owner 还是原主、Pile 正确）→ 再 `GiveToAnotherPlayer` → 再 `Add`**。
  r229 就是为了修"牌不显示"把顺序写反（先改归属），炸出"瓦库把我收编的牌反复打 60 张/回合 + 泄漏 6.1 万节点 = 卡死"。
  兜底：瓦库自动出牌的手牌读数会**过滤掉不属于它的牌**（`ResolveAutoplayHand`，命中打限流 WARN）。
  生命周期（r231）：登记（集火 + 合体链接）除了"玩家侧回合结束 / 战斗结束"，还在**退局枢纽**
  （`LocalMultiControlRuntime.OnRunCleanup`）复位一次 —— 玩家"打到一半直接退出"时那两条正常路径**都不会触发**
  （r231 判读 r230 日志时发现：那局就是中途退出、全程没有 `本回合登记已清空`）⇒ 不补会在下一局同 NetId 的
  席位对上"复活"，表现成"新一局第一场战斗一开始就在混抽"。
  🔑 关键事实：战斗手牌是**卡组克隆**（`Player.PopulateCombatState` 的 `state.CloneCard`，带 `DeckVersion`）
  ⇒ 改归属只影响本场战斗那份，**不会真的偷走对方的卡组**；而 Harmony 前缀不能 await ⇒ 一律用同步内部方法搬运。
- 三张牌都**抽 1 张牌**；「本回合」登记（集火 + 合体链接）统一在**玩家侧回合结束**清空
  （挂在遗物 `AfterSideTurnEnd(side == Player)`，战斗结束再兜一次）—— 瓦库在同侧稍后行动，集火必须活到那一刻。
- **📋 实机三轮结果（2026-10-07 用户口径）**：
  - r228：【你攻】✓ 瓦库回合内确实都打那个怪；【我挡】✓ 掉血确实转移到你身上，但 ✗ 这笔伤害**不能格挡**；
    【合体】✓ 抽牌确实随机，但 ✗ 收编的牌**不显示在手里**、切一次人才出来。
  - r229：我挡 ✓ **已关单**（格挡生效）；【合体】✗ 瓦库**把我收编过去的牌又反复打出去**、且**严重卡顿**
    （日志实证：同一张牌实例连打 60 张/回合撞护栏上限，那局泄漏 6.1 万 CanvasItem）⇒ 根因 = r229 修显示时把
    搬牌顺序写反（见上面的顺序铁律）。
  - r230：按铁律修好搬牌顺序 + 混弃改后置 + 瓦库出牌侧加"只认自己的牌"兜底。
- **✅ r230 实测（2026-10-07 判读 `logs-archive/godot__20261007-192717__r230.log`）**：合体收编 7 张 + 5 能量 ✓、
  混抽 3 次 ✓、瓦库出牌 6 次全部正常完成（**无同一实例重打**）✓、`混进别人的牌` WARN 0 ✓、
  `turn loop died`/`LocException`/`### Exception ###` 全 0 ✓、节点泄漏 11970（与功能前 r227 的 13044 同量级）✓。
  唯一发现 = "退局复位没挂"⇒ r231 已补（纯补登 + 日志加 `round=`，未改三张牌逻辑）。
- **✅ r231 复测（3 局，marker `2026-10-07-r231`，`logs-archive/godot__20261007-194016__r231.log`）⇒ 关单**：
  回合登记清空 ×3 + `[瓦库的爹] 本回合登记已复位: run-cleanup` ×3（r231 的退局复位确实触发）、
  进局/退局/会话复位自检**全「无残留」**；三张牌 我挡 ×4 / 你攻 ×2 / 合体 ×2 全部正常；**无重打**
  （评分出牌 33 = 队列出牌完成 33）；异常族全 0；节点泄漏 6778（低于功能前的 r227）。三局全绿 ⇒ 已合回 master。

---

## 战灵召唤（原 ④「地狱战神」）—— 未动工（排期队尾）

> 命名口径（2026-10-06 用户拍板）：**「地狱战神」= 本节（战灵召唤）**；
> **「瓦库的爹」= 上一节那条**（遗物 + 三张占位牌，r227 已实现）。

**新版设计**：遗物【地狱耳环】+ 休息处【分裂精神】+ 战斗内**每回合消耗计数召唤临时玩家「战神」**
（预设角色/血量/遗物/卡组、挡伤 1/2、绑主死亡联动、消耗计数复活、智能瓦库 + 台词「唔」、无自己的战斗奖励、【我挡】/【你攻】同样适用）。

- 🅿️ **排期 = 队尾（暂缓，2026-10-06 用户口径「现在不做这个，等排在前面的做完了再考虑」）**：
  **启动条件 = 排在它前面的工作做完**。设计与可行性**已完整落档**在
  `maintenance-docs/decision-records/地狱战神-战灵召唤设计方案与可行性分析.md`（#21）⇒ **重启时直接从该文档 §7 的 D1~D13 拍板开始**（不必重新调研）。
  拍板完成后 = **Phase 0 POC**（临时席位名单 + `CreatureCmd.Add` 塞一个哑战神 + 立绘/回合推进/战后清理三件验证）。
- **已交付的前置功课**（不再重复）：mod 自定卡挂进卡池的链路（r224~r226，`EventCardPool`，口径见坑 AA）；
  r227 起这条链路已"转正"为「瓦库的爹」的实装（见上一节）⇒ `HellGod*` 命名、占位卡与 `[地狱战神验证]` 锚点**已一并清理**
  （现为 `Daddy*` / `[瓦库的爹验证]`）。
- **三条硬约束（开工前必读 doc §0）**：① 战神只能进 `CombatState`、不进 `runState.Players`；
  ② 必须引入"临时席位"名单，否则瓦库不托管、回合不推进（卡死）；③ 战斗 UI 不会"自动"生成（**走 `CreatureCmd.Add` 就有立绘/血条**；真正不建的是"每玩家手牌/能量 UI"，战神全程走 AI 故不需要）。
- **架构选型（D10）——✅ 2026-10-06 已拍板 = C（临时席位 + `CreatureCmd.Add`）**，详见 doc §11：
  C 同时满足"每回合召唤 + 战神出牌 + 战后消失"（立绘/血条有、不入存档；真正只改一处：让 `IsLocalSeat` 认"临时席位"，纯逻辑可单测）。
  被否方案：**A 召唤物**（`Creature.TakeTurn` 只允许**敌方**怪物 + 宠物无 `Player` ⇒ 战神**不可能出牌**，与 R11 冲突）；**B 正式玩家席位**（会把**事件/宝箱/金币/商店/地图**等战斗外流程全部拉进来，用户否掉的决定性理由）。
- **多人缩放影响面（doc §12）**：走 C 后**开战时的敌人数值与所有战斗外流程都按真人数量算**（敌人格挡走 `runState`、初始 HP/初始力量在开战前算完且中途不重算）；
  只有在**战斗中后来发生**的三件事会把战神算进去：① 战斗中新建敌人的 HP 缩放 ② 战斗中施加的敌人力量（Buffer/Plating/Artifact/Slippery 等 `PowerCmd.cs:132` 分支）③ 硬编码按当前人数的效果。
  ⇒ **D11 已拍板 = ① 接受 + 数值平衡**（用户：「数值平衡很有道理，不然强完了」），不去 patch 敌人缩放。
- **【分裂精神】要付出代价（R2b，2026-10-06 追加）**：每次使用**扣除若干百分比的血量（可设置）**（用户：「精神分裂对身体不好」；动机 = 单靠一次火堆换肉盾太便宜）。
  落点 = 自定义休息处选项 `OnSelect` 里"先扣血、再加计数"（失败/不合法则整次不消费），档位沿用 `RefineHpRatio` 那条可配流水线。
  ⚠ 休息处扣血是"有前车之鉴"的区域（**非战斗环境不要用会打 `Log.Error` 的 `CreatureCmd.SetMaxHp`**）；回血侧有官方安全先例 `CreatureCmd.Heal`（注释明写休息处该用它），扣血侧入口需 POC 定型。口径细项 → **D13**。
- **入口问题（D12）：「1 席本地会话」还是「纯单机也支持」**：
  ✅ 建议**统一实现** —— 把地狱战神子系统的门从 `LocalSelfCoopContext.IsEnabled` 改成「功能开关 &&（本地会话 ∨ 单机）」，两条入口共用代码；**先落「1 席本地会话」**（`MinLocalPlayerCount` 2→1 等零散改动 + **存档 tag 必须 1 席也能打** + `IsSaveOwnedByLocalSelfCoop` 改以 tag 为主判据**防误判普通单机存档**），单机作为第二阶段。
  ⚠ 1 席会话下 `CombatRoomOfferRoomEndRewardsPatch` 会早退 ⇒ 战神走**原版奖励路径**（遍历 `CombatState.Players`）⇒ "战神无奖励"必须做成**独立于合并路径**的排除。

---

## 已关单与归档索引

2026-09-08 ~ 2026-10-06 的全部**已关单段落**（原作者遗留 4 项 Known Issues、已关单的 BUG、
改进项、G0.2-B5、Daily、维护 r160/r162、R3~R5 重构线、瓦库四功能 ①②③、立绘站位对调、
就地复核与过期待办订正）已从本文件切出，统一放在（**仓库外、不进 GitHub**）：

`maintenance-docs\decision-records\archive\TODO-归档（已完成 bug 与功能流水·r96~r226）.md`
（含逐条现象 / 日志实证 / 根因 / 修法 / 门禁 / 验证契约 / 实机结论）

下面这张表**只为让别处的引用仍能按旧 §编号找到落点**（代码注释、`CHANGELOG.md`、
skill 侧 `references/`、各提案里都有 `TODO.md §xxx` 的写法；这些 § 在本文件已不存在）：

- **bug 类**：§BUG-1 / §BUG-2 / §BUG-4~§BUG-7 / §BUG-9~§BUG-13 / §BUG-19 /
  §BUG-20（**两处**：特殊卡牌奖励、结算页结束按钮）/ §BUG-21 / §BUG-22 / §BUG-23 / §BUG-27 / §BUG-29~§BUG-32
- **改进类**：§改进-1~§改进-7（§改进-2 含 ⑭~⑳ 逐轮；§改进-7 = BUG-16）/
  §维护性改进 backlog / §决策表缺口 / §另记：逐卡评级草表
- **功能 / 重构类**：§瓦库四功能（§① 净化 / §② 我们联合 / §③ 炼化）/ §立绘站位对调 / §R3 / §R4 / §R5 / §Daily 本地多控
- **其它已定性 / 已收口**：§原作者遗留 4 项 Known Issues（勿重开）/ §r149 /
  §维护：经验固化（r160）/ §维护：防回归门禁（r162）/ §Co-op Bots 4 人上限 / §大厅席位卡按钮 /
  §重瞳 / §就地复核结论 / §过期待办状态订正

配套去处：玩家可见的变化 → `CHANGELOG.md`；做法与教训 → skill 侧 `references/`；
逐轮验收流水 → `D:\Download\pain\开发进度记录.md`；R3~R5 逐轮 → `archive\TODO-归档（重构线 R3~R5 逐轮记录·r177~r205）.md`。
