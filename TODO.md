# TODO（下一步做什么）

> **口径**（`AGENTS.md` §8.1）：本文件**只写「接下来做什么」**（待办 / 待拍板 / 未定根因）。
> 已关单的项**不留正文**：玩法可见的变化看 `CHANGELOG.md`；做法与教训进 skill 侧 `references/`；
> 逐轮验收流水进 `D:\Download\pain\开发进度记录.md`；
> **2026-09-08 ~ 2026-10-06 的已关单 bug / 功能流水**（含根因、复测契约、实机结论）已切到
> `maintenance-docs\decision-records\archive\TODO-归档（已完成 bug 与功能流水·r96~r226）.md`（**仓库外、不进 GitHub**）。
> **现状**（分支 / marker / 未推送提交数 / 版本 / 部署位）只看
> `D:\Download\pain\开发进度记录.md` 顶部最新「🔚 本轮收工」块，本文件不复制。
> 更新：**2026-10-07**（**3152 → 339 行**：切出已关单流水 + 跟踪表压缩为一行一案）；
> **2026-11-04**：新增 §玩家上报待修（BUG-34~41 + QOL-1~3）；同日实机复测后 **34/36/37/38 + QOL-1/2
> 判为未复现或不做**（已压成一行），**35 用日志定根因**（血条按钮绕过占卜锁）、**40 用反编译定根因**
> （`HoldYourTurn` 只绑第一个本地席）、**41 = 沙坑计数显示错人（`NPowerContainer` 只判一次可见性）**，
> 新登记 **39（回合切换视角漂移）** 与 **QOL-3（污染/沙坑徽标 + 开关）**。
> **同日 r334 全部落地**（35/39/41 + QOL-3 进主 mod、40 = 新 compat 补丁 `HoldYourTurnLocalCoopFix`），
> 已构建 + 单测 1416 全绿 + 部署。**实机判读（`godot.log` 19:19，用户「完全没问题」）**：
> 35/41/QOL-3 ✅，39 的**回合结束侧** ✅（`source=turn-end-*` = 0）、回合开始侧还原分支未触发
> （`skipTurnStartDrawAnim` 开着）；**40 未验**（游戏内 HoldYourTurn 被禁用 ⇒ 补丁按设计等待）。
> 全程 `turn loop died` / `Trying to sync crystal sphere rewards` / 我方新失败锚点 **= 0**；
> 5 条 ERROR 全第三方（Oddmelt 脚本注册 / 两个 Steam 分支限制）。
> **第二轮补测（19:27 日志）**：40/39 两条锚点齐（`HoldYourTurnLocalCoopFix INIT_OK` + `非绑定席位放行结束回合`；
> `回合开始批量切前台已结束，还原到原视角`）；同局暴露 **BUG-42 立绘左右对调** ⇒ 按用户拍板的 A 方案落地
> （`NCombatRoomPositionSeatPinPatch`，只钉「谁是 me」不移动节点）。**r335 首版没修好**（捕获时机用了
> 战斗态 ⇒ 把漂移后的 POV 当开局席位），**r336 改用房间身份后实机通过**（20:32 日志）。
> **2026-11-10（r337~r342，瓦库托管增强）**：药水腾栏 / 无实体与空场出牌 / 商店顺序 + 预留删牌金 /
> 设置页文案去 markdown / 药水动画日志合并 —— **五条已全部实机验证**（详见 `开发进度记录.md` 第一百六十四段）；
> 只剩下面 §瓦库托管增强 里列的"未覆盖场景"待自然遇到。
> **2026-11-10（r343~r345，诅咒/状态牌处置）**：三档（保命档立即打 / 兜底档延后 / 普通牌照旧）+
> 狂乱逃离按沙坑倒计时（≤3 保命、>3 兜底）**已全部实机验证**（详见 `开发进度记录.md` 第一百六十五段）；
> 剩余只登记下面 §诅咒/状态牌处置 的 3 条未覆盖场景。

---

## ⏰ 更新日（**2026-11-13 游戏本体更新，含新模式：单人 + 多人**）—— 准备已做完，等当天实机

> 口径（2026-11-10 拍板）：**更新前不开新功能线**，只做零风险保险 + 演练，把功能留给更新之后。
> 顺序与判据见 `AGENTS.md` §5；工具 `Scripts/Tools/game_update_prep.ps1`（`-Snapshot` / `-Verify`）
> + `regenerate_src.ps1`。基线已就绪：`Scripts/Tools/baselines/game-version-baseline.json`
> （v0.111.0 / `marker r345` / `head 9324d0c`）；旧 dll 与存档归档在 `D:\Download\pain\game-archives\v0.111.0\`
> （含 `DualRoleAdventure-2026-11-10-r345.dll`，适配期出问题可拿它做 A/B）。

**更新前 3 件准备 —— ✅ 2026-11-10 全部做完**（详见 `开发进度记录.md` 第一百六十六段）：

1. ~~push master~~ ⇒ 远端已同步（`8e6c5e6` + 其后的 docs 提交），有完整回滚点；
2. ~~刷新基线 repo 侧字段~~ ⇒ `-Snapshot -NoArchive` 重跑：`marker r336 → r345`、`head 39818ea → 9324d0c`；
3. ~~演练更新日脚本链~~ ⇒ `-Verify` / `regenerate_src -CheckOnly` / `preflight`（离线）/ `preflight -Build -Deploy -Lint`
   全部按预期跑通（6 PASS / 2 SKIP，单测 1457 全过）。

**更新日当天，实机只判这 4 项**（顺序别乱）：

1. `INIT_OK` + `PATCH_RESULT critical=25/25 optional=20/20`（不齐 → 先 `LMC_INIT_STRICT=0` 保命，再逐个补丁修）；
2. **我们的入口**能进：锚点 `单人多角色…入口：改走本地回环开局` / `每日挑战大厅本地人数已同步`；
3. **官方 标准 / 每日 / 自定义 三入口保持原版**（S9 只是静态门禁，必须手点一次）；
4. **新模式两个入口**各点一次：看 `联机菜单卡片已重排: count=?`（**4 → 5 就说明官方加了卡**，确认我们那张卡没跑飞、
   小按钮没重叠）+ 期望"栈顶为本项目的 `NullReferenceException` = 0"（S10 那族：`lobby?.X`，`_Process` 里每帧跑）。

⚠ **可能被动摇的地基**（更新后先读 release notes 再下结论，别提前猜）：
`StartRunLobby` / 回环 `NetService` / 伪席位（我们整套本地多控的前提）、大厅页类型白名单
（`TryGetLobbyScreen` 里写死 `NCharacterSelectScreen / NCustomRunScreen / NDailyRunScreen`）、
存档 `current_run_mp.save`（读档自动化那条线）。若官方「单人模式」能吃 AI 队友 / 多角色，
"单人多角色"的定位需要重新评估（**待拍板**）。

---

## 瓦库托管：诅咒/状态牌处置（2026-11-10 r343~r345）—— ✅ 已关单，未覆盖场景待观察

> 实现与实机判读见 `开发进度记录.md` 第一百六十五段；玩法可见的变化见 `CHANGELOG.md` `[Unreleased]`。
> 判据只认游戏机制级信号 `CardModel.HasTurnEndInHandEffect`（走 `OnTurnEndInHand` 的卡必然带它，
> 因为 `CombatManager` 只在它为真时才调 `OnTurnEndInHandWrapper`），不硬编码第三方卡名。

1. **用自建 Power / Hook 实现"不打出就去世"的 mod 诅咒**识别不到（不走 `OnTurnEndInHand`）。
   遇到漏网的把卡名/出处给出来，再补名单或另找判据。
2. **兜底档目前等于"有剩余能量才打"**：实测这类牌多是"打不出"（如【我不能走啊！】99 费）或
   "打了没收益"（【污秽】）；若日后希望"沙坑计数够多时一张都不打"，再给狂乱逃离的兜底档加硬条件。
3. **保命档打不动时（能量不足）没有补救**：要不要"先打能加能量的牌"——待拍板。

---

## 瓦库托管增强（2026-11-10 r337~r342）—— ✅ 已关单，仅剩未覆盖场景待观察

> 实现与实机判读见 `开发进度记录.md` 第一百六十四段；行为说明见 `CHANGELOG.md` `[Unreleased]`。
> 剩余**只登记不排期**（都是"要真玩到才验得到"的分支）：

1. **无实体**在**默认（heuristic）档**的表现（已验的是 scored 档：锚点 + `:intangible` + 实际没打攻击牌）；
2. **`AllyCharacter` 类药水**的腾栏目标（扩容 / 黑暗精华 / 星星 / 王之勇气 / 骨头酿 / 尸鬼瓮 / 士兵炖汤）——
   已验的是 `HumanFirst`（复制药水，r342 `target=…326` 真人）；
3. **`卡组≥40 + 有诅咒`** 两个商店规则叠加时的取值（当前按"诅咒优先"取，`Removal` 排第一）；
4. **`NMerchantInventory.GetClosestStockedSlot` NRE**：买东西后原版导航偶发 NRE（纯日志、购买成功），
   12 席/连续自动购买时更容易撞上；要不要加 compat 级兜底**待拍板**。

---

## 性能线：待拍板的两条（2026-10-31 收工后登记）

> ⛔ **2026-10-31 用户拍板：性能线暂停（先不搞）**。决定性证据（`godot__20261031-183349__r332.log`）：
> `ThirdPartyHotPathFix` v1.3.0 接管生效（三家整场战斗跳过 612,486 次、零异常、零玩法变化），
> **但同一时间窗 fps 仍 4~8** ⇒ 三家 Filter 链（约占主线程 20~25%）**不是体感主因**，摘掉无感。
> 下面三条挂起；**再开的前提 = 先采一份 `-Allocation`**（fps 4 窗口分配 160~270MB/s、GC 占 8~16%，
> 大概率主因在分配洪水/引擎侧，而不是更多 hook 手术）。
> 现状（三条病已摘、剩下的账）看 `D:\Download\pain\开发进度记录.md` 顶部第一百五十九段；
> 这两条不建提案文档，就挂在这里等用户拍板。

- **YUI `Wrap` 按持有者细化**：现在只要 12 席里有 1 件实现 YUI 接口的遗物，整局所有席位的**所有**遗物
  每次分发都要写两次 `AsyncLocal`（实测主线程 10.7%）。细化后只给真正需要的遗物建 scope，预计再省 8~10%。
  ⚠ 代价：必须**重写 `Wrap`**，而重写依赖它内部的小方法 `MarkFlashed`（会被 tiered JIT 内联绕过 ⇒
  绕过时 YUI 遗物的"触发复读/通知"会**静默失效**）。要做就带 `HOT_PATH_FIX` 开关 + 开机自检 + 实机验收复读。
- **listener 列表缓存**：`IterateHookListeners` 每次分发重建 600~1200 项列表（主线程 12%），且存在**重入分发**
  （handler 里再问关键词/能量 ⇒ 又走一遍全场）。缓存预计 -10~15%，但需要**精确失效键**（能力版本 / 卡堆 / 遗物 /
  药水 / 球 / 回合 / 玩家活跃位），**漏一个失效点 = 静默丢 hook**（玩法 bug 而非性能问题）。
- **旁证**：同一局把席位从 12 降到 8 打一场，可以量化"每减一席省多少 ms"——还没做过，是决定"要不要冒险缓存"的最便宜依据。

---

## 决策记录跟踪表（提案生命周期 · 唯一事实源）

> 提案的**清单与定位**以 `maintenance-docs\decision-records\README.md` 为准（冲突时以那份索引为准）；
> 本表只记**工作项状态**（谁做、做到哪、下一步）。登记纪律见 `AGENTS.md` §8.1：
> 新 proposal 落地当天 → 索引加一行 + 本表挂一行（或明写「仅调研，不排期」）；**登记 ≠ 排期、≠ 承诺动工**。
> 「索引 #N」= `decision-records\README.md` §一 的编号（`maintenance-docs\` 无 git，不进 GitHub）。
> 📦 **2026-10-19**：`#5` / `#9` / `#10` / `#11` / `#18` / `#20` 六份已收口提案已归档到 `decision-records\archive\`；
> §一 **编号不重排**（保留空号），下表里的「索引 #N」照旧可读，归档条目见索引 §二「2026-10-19 归档」子表。
> 🔒 **非主 mod 的事项已拆出**（2026-10-24）：第四幕协调器（索引 #22）/ `PrimeVakuu` 桥接（#19）/ 开发环境与工具链（#12 / #8）——
> 见仓库外 `D:\Download\pain\TODO-项目.md`（本文件随仓库公开，只放主 mod 自身的事）。

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
| `本地多角色扩展到Daily模式可行性分析.md` | ✅ 已落地 + 联网/断网各一局实机通过（r161），随 v1.43.0 发布 | 可选增强（>4 席 / 1p 榜上传 / 每日页 UI 精修 / `NDailyRunLoadScreen` 适配）—— 用户「先不做」 | 索引 #9 + 归档（Daily 记录） |
| `瓦库四功能-可行性核验报告.md`（#10）+ `瓦库炼化净化联合地狱战神…提案`（#11） | **①②③④ 全部 ✅ 关单**（净化 r212 / 我们联合 r214 / 炼化 r223 / **④「瓦库的爹」= 遗物 + 三张牌 r227~r231**；均已合回 master、随 **v1.45.0** 发版） | 无（④ 效果线已收口：我挡 ✓ / 你攻 ✓ / 合体 ✓ + 退局复位 ✓，r231 三局复测全绿）；可选只剩**数值平衡调整** —— 见下方 §④ | 索引 #10/#11 §7 拍板附录 + 归档（①②③ 记录） |
| `Co-op_Bots联机队友兼容可行性分析.md` | 部分落地：r144~r148 兼容层已落地并实机通过；**POC 接管路径 2026-09-26 实机后决定不做**（冻结为能力保留，`coopBotsSeats` 默认空） | 未做（非排期）：12 席三态 UI/持久化、Phase 3 补丁面回归、被接管本地席位的奖励分流 | 索引 #13 + 下方 §BUG-17 |
| `局内加人与战斗临时玩家-可行性分析.md` | 未动工（调研结论稿：A 局内加正式玩家风险高 / B 战斗临时玩家可行且有抓手） | 是否排期待拍板（与 ④ 战灵召唤需求同源） | 索引 #16 |
| `原版药水一览表.md` / `原版附魔一览表.md` | 参考数据表（非提案）；药水表 r136 已校正 | 改规则表时同步；附魔侧「优先选哪类牌」待用户填期望 | 索引 #14 / #15 |
| `第三方补丁目标交叉分析-CoopBots.md`（#17） | 参考表（脚本产物，只读勿手改） | 无 | 索引 #17 |
| `补丁mod收进主仓库compat-mods.md`（#20） | ✅ 已落地（2026-10-05 迁 1 个 + 2026-10-07 再迁 2 个 ⇒ `compat-mods/` 现有 3 个；范围仍为「主 mod ↔ 第三方交互」补丁） | 无（push 已完成；**分发口径 = 主 Release 的独立附件 `compat-mods-v<major>.<minor>.zip`**，已挂 v1.45、`release_build.ps1` 支持自动打与 `-CompatOnly`） | 索引 #20 |
| **战灵召唤（原 ④「地狱战神」）**（#21） | **D1~D13 已拍板**；**Phase 0 POC ✅ 关单（r239）**；**Phase 1 + r241 四项追加 ✅ 实机关单（r240~r243）**；**Phase 2 召唤闭环 ✅ 实机关单（r244 落地 → r245「不能结束回合」/ r246「第二个战神不会被打」两轮修复 → 2026-10-09 r246 判读全绿，部署位 `2026-10-09-r246`）**：回合开始召唤选择 + 消耗计数 + 生成完整战神（D7「普通档」）+ 战后补满清理；**🆕 两个"大招"召唤 R16【无尽战神】/ R17【终极九连环】（D14~D19 已拍板：整包价 无尽 8 / 九连环 12 + 部分成功；2026-10-09 二轮修订 = 【九连环】改扣"计数上限"本身（永久消耗、不补满），实际一轮只能开一次）**；**Phase 2b（D7 预设档 + 两个大招）✅ 实机关单（r247 落地「一切都好正常」→ r248 修订【九连环】门槛 12/固定 9 个/按实际数量计费 → 2026-10-10 判读全绿：无尽 10/10 补满 12 席·随机各档 / 九连环 9 个先古·永久扣上限 12→0），部署位 `2026-10-09-r248`** | **Phase 3（战神托管出牌 + 台词「唔」）✅ 实机关单 = r253**（r249 落地 → r250 `GetMe` → r251 队列档 → r252 补登记 → **r253 定稿：回归 inline + 不钉上下文**；同轮追加【无尽战神】先古开关 + 召唤保命【无实体】；用户「一切都变得好正常」，异常族全 0）；**2026-10-10 用户拍板后续口径 = 预设卡组走「手工 id 列表 + 开放到配置文件」/ 预设遗物走「A 方案：只挑不依赖开战时机的遗物」/ 顺序 = 卡组 → 遗物 → Phase 4/5** | **① 手工预设卡组 ✅ 实机关单（r254：内置默认改 `ModelDb.Card<T>()` 类型引用 + 四档配置键 `hellwarDeck*` 可自定义；判读全绿，`logs-archive/godot__20261010-145141__r254.log`）；② 预设遗物（A 方案）✅ 实机关单（r255：罕见 1 / 稀有 2 / 先古 3 件，全为"常驻钩子"遗物；判读全绿，`logs-archive/godot__20261010-182625__r255.log`；⚠ 稀有/先古档本场未召，效果体感待后续）；③ Phase 4 生存规则：**R8 绑主死亡联动 + R9 死后复活 ✅ 实机关单（r256，判读全绿：复活后 round 4/5 继续出牌、绑主的战神当场收掉且不影响别人）**；**R10 挡伤 1/2（D4 = 玩家零伤害 + 战神吃一半）✅ 实机关单（r257：绑主受击 0 次 / 无守卫的对照组 19 次 / 战神吸收 53 次）** → **Phase 4 全线关单**；**④ Phase 5 收尾（R12 战神无战斗奖励 + R13 我挡/你攻覆盖战神）✅ 实机关单（r258：契约四条全中 —— 奖励只发真人 / 无[未知角色] / `我挡：4096(战神)` ×2 / `你攻生效: player=4096(战神)`）** ⇒ **战灵召唤 R1~R13 全部实机关单，功能线关单**；**🆕 r259（用户三条口径）**：① R10 修订 = 肉盾资格改**下一回合**起生效 ✅ **用户实机确认**；② **R13b = 【合体】覆盖战神** ✅ **实机确认**（`合体：… ← 瓦库 4096/4097(战神)`，手牌 5 + 2 张被收编；`我挡：4096(战神)`、`你攻生效: player=4096(战神)` 同场复现）；③ 「战神当回合就能打牌」= **不做**（用户不强求）；R10 平衡（战神是否太脆）= 用户先**完整测几局**再议；**🆕 r260：战后清理逐项容错（第三方 NinjaSlayer 布局补丁抛异常曾中断清理 ⇒ 漏摘战神；同场 **R12 排除闸门立功**）+ 合体「空气牌」**自检探针** + 合体日志补混抽可用性**；**🆕 r261：① 托管**不再打"需要目标却没目标"的牌**（根因 = 猪猪 mod「抱猪」用**第三方自定义 `TargetType`**；同一根因导致"合体空气牌"）② 战后摘战神改**「数据先摘、视觉节点后收」****；**🆕 r262（2026-10-11，承接实机反馈，⭐ 待实机）：① **【无尽战神】整包价 8 → 12**（用户拍板：它一次补满 12 席 ≈ 10 个战神，太强）② **托管出牌抛异常的牌记入「本局别让托管打」名单**（沙耶 mod「色素细胞」抛 `Index was out of range` ⇒ 卡面卡屏幕中间 + 后续空气牌；不再重试、真人仍可自己打）③ 战神视觉节点**「先藏再摘」**（第三方在 `RemoveCreatureNode` 链上抛 ⇒ 原来隐藏那步永远执行不到）**；`§待查-6` 已定性（跟原版 UPPERCUT 命中特效绑定，与 R10/战灵召唤无关）**；另：D12③「1 席本地会话」入口；**改动已本地提交 9 笔、领先 origin 14 笔、未 push、未合回 master（待用户拍板）**；备选：用户提的"战神当回合就能打牌"（本轮选了无实体保命方案） | 索引 #21 + 下方 §战灵召唤 + 设计文档 §8（含 r249~r253 落地记录与后续两阶段口径） |


---

## 玩家上报待修（2026-11-04）—— ✅ 全部收口（r334~r336，实机通过 / 未复现）

> 本节收工时已切到 `maintenance-docs\decision-records\archive\TODO-归档（玩家上报 BUG-34~42 · r334~r336）.md`
> （**仓库外、不进 GitHub**；含每条的现象 / 日志证据 / 根因 / 修法 / 验收锚点）。
> 结论：**35 占卜切人软锁 / 39 回合视角漂移 / 41 沙坑计数错人 / 42 立绘左右对调 + QOL-3 污染·沙坑徽标
> = 已修并实机通过；40 = 第三方 `HoldYourTurn` 冲突，新 compat 补丁修好并实机通过；
> 34 / 36 / 37 / 38 = 实机未复现；QOL-1 / QOL-2 = 用户确认不做。**
> 现状（分支 / marker / 部署位）看 `D:\Download\pain\开发进度记录.md` 顶部收工块。

---

## 待办（未定根因 / 待观察 / 暂不修）

> 下列都是「能用但别扭」或「偶发自愈」的问题，**用户已拍板先记录不修** —— 复现 / 想修时按各条自己的
> 判据与备选路线走（逐条已写好现象、日志锚点、根因分析与修法备选）。
> ⚠ 原「BUG-14 已排期」的例外已随 §BUG-14 关单切到归档（2026-10-21，见文末索引）。
> 同类既有备案：`maintenance-docs\decision-records\瓦库托管优化可行性分析.md` §16.2。

### 待查-1 宝藏房共享选遗物界面手牌图像重复添加（`add hand … twice!`）—— ✅ **2026-10-24 已修（r318）**

- **根因**：`NHandImageCollection.Initialize` 已给每个席位 `AddHand`，而我们切前台改 `LocalContext.NetId` 会让
  `PeerInputSynchronizer.GetOrCreateStateForPlayer` 新建同一席位的输入态 ⇒ `StateAdded` ⇒ 再 `AddHand` 一次。
- **修法**：前缀 `NHandImageCollection.AddHand`，`GetHand(playerId) != null` 时跳过（仅本地多控会话内）；
  原版"已存在"分支只有一句 `Log.Error`、**什么都不做**（幂等）⇒ 行为不变，只关掉日志噪声
  （首次命中打 `共享手牌图像已存在，跳过重复添加`）。
- **复验锚点**：宝藏房共享选遗物界面进出 + 切角色若干次 ⇒ `Tried to add hand for player … twice!` 应为 **0 条**
  （r246/r260/r288/r295/r298 单局曾 1~3 条）。

### 待查-3 战神被打**没有音效**（用户 2026-10-10 口径；**先跑探针定性，别直接改**）

- **现象**：召唤出来的战神被敌人打时**听不到受击音效**（真人被打时有）。
- **已做的侦查（源码逐处核对，结论：原版玩家侧可能本来就没有"被打"音效）**：
  - `CreatureCmd.Damage`（`sts2src/src/Core/Commands/CreatureCmd.cs:342-390`）：受击只做
    `NHitSparkVfx`（**纯粒子，无音频**）+ `TriggerAnim("Hit")` + 屏幕抖动；
    **受击音效只给怪物**（`:348-351` `receiver.IsMonster && receiver.Monster.HasHurtSfx → SfxCmd.Play(...)`），
    **伤害音效只给敌方**（`:372-375` `originalTarget.Side == CombatSide.Enemy → SfxCmd.PlayDamage(...)`）。
  - `CharacterModel` 只有 `AttackSfx / CastSfx / DeathSfx / CharacterSelectSfx`，**没有 `HurtSfx`**（`:209-219`）。
  - `Nodes/Combat/NCreatureVisuals.cs` 全文件**无 `Sfx`/`Audio`**；`NHitSparkVfx` 只有 `GpuParticles2D`。
  - `NCombatRoom.CreateAllyNodes()` = `foreach { AddCreature(...) }`（`:277-285`）⇒ 与我们中途
    `NCombatRoom.AddCreature` **同一条路** ⇒ **不是"我们少配了节点/少了某步"**（与坑 AC/AD/AE 那类"漏抄步骤"不同）。
  - 原版确实存在：格挡 `event:/sfx/block_hit`、破防 `event:/sfx/block_break`、攻击方的攻击音效。
- **定性探针（一次实机定死）**：临时给 `NAudioManager.PlayOneShot(...)`（或 `SfxCmd.Play`）挂一个
  **只打日志的前缀补丁**（`[地狱战神探针] 音频: key=…, 调用栈=…`），在**同一场战斗**里分别让
  「真人被打一次」与「战神被打一次」，对比音频调用：
  - 真人那次**也没有额外调用** ⇒ 确认原版没有玩家受击音效 ⇒ **关掉本项**（不是缺陷）；
  - 真人那次**有我们没有的调用** ⇒ 定位调用点并同款补上（大概率是"只给本地/前台席位"的 UI 反馈路径，
    ⚠ 与 `LocalContext.IsMe` 同源 —— 而我们**故意**让战神 `IsMe=False`，所以这类反馈需要**单独补**）。
- **优先级**：低（纯表现，不影响玩法/结算）。

### 待查-6 新出现的「表现层」ERROR（VFX `PlaySequence` 空引用 + 节点池 `back to pool`）—— ✅ **已定性：跟原版牌 `UPPERCUT` 的命中特效绑定，与 R10/战灵召唤无关**（r258 判读，剩一条"是否与托管出牌链有关"的小尾巴）

- **现象**（marker r257 首现 3 条 / r258 复现 2 条；历史 12 份归档里这两类各 0 次）：
  - `System.NullReferenceException`，栈顶 = **原版特效类**
    `MegaCrit.Sts2.Core.Nodes.Vfx.NHeavyBluntVfx.PlaySequence()`（= 原版"重击"命中特效场景
    `vfx/vfx_heavy_blunt`）/ `NLargeMagicMissileVfx.PlaySequence()`（r257 那次）。
    读反编译源码：`PlaySequence()` 直接 `for (i…) _anticipationParticles[i].Restart(); … _impactParticles[j].Restart();`
    ⇒ **NRE = 场景里那些导出粒子槽是 null**（资源/场景层问题，不是游戏逻辑）。纯表现。
  - `Tried to free object <Control#…> (NCard) back to pool`（Godot 节点池，纯表现；r257 那次 1 条）。
- ✅ **定性证据（跨会话相关性，零实机成本）** —— 用归档日志做"**卡名 ⇔ 异常**"的对照：
  `NHeavyBluntVfx` 的 NRE **只出现在 `CARD.UPPERCUT` 被打出的那两场**（r257：UPPERCUT 3 条 / NRE 3 条；
  r258：UPPERCUT 在场 / NRE 1 条），而**其余 12 场归档 UPPERCUT 出场 0 次、NRE 也是 0 次**。
  且两场里紧邻 NRE 的伤害行都是 `cardSource=CARD.UPPERCUT`（r257 = 战神 4096 打出、r258 = **真人 327** 打出）
  ⇒ **不是"战神当施法者"、也不是 R10 的重定向落点**（r258 那次根本没走重定向）。
  反编译侧对照：`vfx_heavy_blunt` 被 `Uppercut / Bludgeon / Stomp / Mangle / Break / AllForOne / CrushUnder` 等
  **原版牌**引用；**本仓库对这整套 VFX 零引用**（搜过了）⇒ 不是我们的功能代码。
  两次 NRE 的紧邻上方都是第三方 `[figure_Saya] [SayaApiBridge][ResolveCard]` 卡面解析 +
  `[PermanentEvolution] refresh-enter; … path=<outside-tree>` ⇒ 落在**第三方美术/特效层**。
- ⚠ **剩一条没排除**：两次 UPPERCUT 都是**经我们的托管出牌链**打出的（r257 战神 inline / r258 真人席位走队列档，
  且都伴随我们的 `已跳过非前台角色的进手牌视觉节点（防串手牌显示）`）⇒ 不能排除"**托管/非前台出牌**"
  是触发条件的一环（第三方模块看到 `path=<outside-tree>` 的卡节点后去取特效资源）。
- **最便宜的定性法（下轮）**：让**真人自己手打一张 `UPPERCUT`**（不走托管）⇒
  NRE 还在 = 纯第三方资源层（**本条直接关单**）；不在 = 与"托管/非前台出牌"相关（再查我们那条视觉跳过）。
- **影响**：**纯表现**（战斗/结算/存档正常，异常族全 0）；只把"`[ERROR]` 期望条数"这条卫生线搞脏。
- **优先级**：低（已定性到"不是我们的功能逻辑"，只剩上面那条小尾巴）。
- **登记**：2026-10-10（第八十六段收工块 ③ 首现 ⇒ 第八十八段收工块 ③ 定性）。
- **同族新成员（2026-10-21，r294 局 3 条）**：`NStarryImpactVfx.PlaySequence()` 的 NRE —— 同一个模式
  （原版/第三方特效场景的导出粒子槽为 null ⇒ `Restart()` NRE），落在异步 VFX 链上，**纯表现、不影响战斗**
  （该局 `turn loop died` 0、`托管出牌抛异常` 0）。⇒ 这条按同族**一并归为"非我方 + 低优先"**，不单独修。

### 待查-7 退出到主菜单时 `PlayCardAction … Nullable object must have a value`（**已定性 = r299 同族，不改代码**）

- **现象**（2026-10-26 r321 局 / 2026-10-27 r322 局各 1 次，日志里出现 2~3 行）：
  `GameAction PlayCardAction card: CARD.FEEL_NO_PAIN … completed with exception: AggregateException
  (InvalidOperationException: Nullable object must have a value.)`，栈顶是第三方一串 `After(card, Task)` 包装
  + 原版 `CardModel+<OnPlayWrapper>d__339.MoveNext_Patch3` ← `PlayPowerCardFlyVfx`。
- **根因**（反编译源码定位）：`OnPlayWrapper` 里的
  `new BranchingPlayerChoiceContext(this, LocalContext.NetId.Value, …)` —— 本局**正在退出到主菜单**时
  `RunManager.CleanUp` 已把 `LocalContext.NetId` 置空，而这张能力牌的 `PlayPowerCardFlyVfx` 的 tween
  await 还没回来 ⇒ 回来时 `.Value` 抛。**与 r299 定过因的"退出期自动用药"是同一族**。
- **为什么不修**：退出瞬间的收尾，玩家已经不在战斗里；实测**无卡死**（`turn loop died` 0、
  `战斗胜利结算被异常打断` 0、后续回主菜单/存档写盘正常）。在"出牌链"上再加一道 teardown 闸门
  只能省掉这条日志，代价是给第三方托管出牌加一处新的时序分支 —— 收益不成比例。
- **判读口径**：看到它先确认三件事 —— ① 是否发生在**回主菜单/退局前后**；② `turn loop died` 是否为 0；
  ③ 上方是否有 `Quit` / `Time to main menu`。三条都符合 = 本条（良性），不用再查。
- **登记**：2026-10-27（第一百五十五段收工块 ④）。

### 待查-5 休息区「悬停索引越界」WARN 是误报（`index=-1` = 没有悬停）—— ✅ **2026-10-24 已修（r318）**

- **根因**：`RestSiteSynchronizer.GetHoveredOptionIndex` 在"没有悬停任何选项"时返回 **`-1`**（`null` 才是"没记录"），
  而 `RestSitePatch` 的判据把负数也当越界 ⇒ 每次鼠标移开刷一条 WARN（纯日志，行为本来就对）。
- **修法**：负数视为"无悬停"**静默忽略**（`option` 保持 `null` = 清掉上一次气泡），只在 `>= options.Count` 时才 WARN。
- **复验锚点**：火堆里反复悬停/移开选项 ⇒ `休息区悬停索引越界` 应为 **0 条**。

### 待查-4 「同回合多个战神」时后面的那个可能整场不出牌（inline 路径抢全局闸门 ⇒ 串行；r254 判读发现）—— ✅ **已关单（2026-10-10，r255 判读给出反证）**

> **✅ 结论（2026-10-10，`logs-archive/godot__20261010-182625__r255.log`）**：同一场景（一场战斗内 2 个战神：
> `seat=4096` 罕见档 / `seat=4097` 普通档）下 **两个都正常出牌、且都打出了 `战神已自动结束回合`**
> （4096 打 rounds 2/3/4、4097 打 round 3；战斗持续到第 4 回合）⇒ r254 那次确实是**"战斗提前结束、没轮到它"**，
> 不是"第二个战神永远拿不到闸门"。**结论：良性，不需要修**（inline 抢全局闸门的串行化是 r253 定稿的已知代价，见坑 AE-8/AE-10）。
> 下面保留当时的现象与时间线备查。

- **现象（marker r254，`logs-archive/godot__20261010-145141__r254.log`）**：同一场战斗里召唤了 2 个战神
  （Common=4096 / Rare=4097），`①a 预设卡组已替换` 与 `②b 抽牌堆=14 张` 都对，
  但**4097 整场 0 出牌**（`瓦库评分出牌: player=4097` 0 条），而 4096 正常出了 3 张。
- **时间线（判读结论：很可能是"没轮到"）**：4097 在第 3 回合才首次轮到
  （`L11603 战神回合开始：交由瓦库托管链出牌（Phase 3）: seat=4097`），
  而 4096 在第 3 回合打完后**战斗立刻胜利结算**（`L11996 击杀动作链已走完（当前无动作在执行），触发战斗胜利结算。`）
  ⇒ 战斗结束早于 4097 拿到闸门。期间 `瓦库看门狗调度统计` 一直是 `rejected=selector-scope-busy:176,watchdog-in-flight:47, lastPlayer=4096`。
- **为什么是"已知代价"而非 bug**：r253 定稿 = **战神走 inline 出牌路径**（队列/并发档对"不进 `runState.Players` 的实体"
  结构性不可行，见 references 坑 **AE-9 / AE-10**），而 inline 要抢**全局 1 槽选择器闸门** ⇒
  **同回合多个战神天然串行**，前一个没打完，后一个进不来（见坑 AE-8）。
- **怎么定性（下次）**：让**两个战神在同一场持久战**（血量厚、至少打 3~4 回合）里各走一轮，
  看第二个战神（同档 / 不同档都试）是否会出牌：
  - 会 ⇒ 本次只是"战斗提前结束"，**关单**；
  - 不会 ⇒ 再查"第二个临时席位的调度为什么一直被 `selector-scope-busy` 拒绝"（届时别急着改路径 —— 路径口径见 AE-10）。
- **优先级**：中低（只在"同场多战神 + 战斗很快结束"时出现；单战神场景无影响）。
- **登记**：2026-10-10（第八十段收工块 ②）。

### 待查-2 进入本地多控流程时删旧多人 run 存档报 `Error deleting path` —— ✅ **2026-10-24 已修（r318）**

- **根因**：三个入口（单人多角色 / 每日 / 自定义）都调游戏 `SaveManager.DeleteCurrentMultiplayerRun()`，
  它**无条件删两个文件**（`current_run_mp.save` + `.backup`），而 `GodotFileIo.DeleteFile` 对不存在的文件
  会打 `[ERROR] Error deleting path …: Failed` ⇒ 没旧存档时每次进入刷 2~4 条（r239/r240/r247 都见过）。
- **修法**：新增 `MultiplayerRunSaveCleaner.DeleteIfPresent(reason)`：先看 `SaveManager.HasMultiplayerRunSave`，
  再反射取 `RunSaveManager.CurrentMultiplayerRunSavePath` + `_saveStore` **逐个文件判存在再删**；
  反射面任何一步拿不到就**退回游戏原 API**（宁可多一条日志，不漏清存档）。三处调用点已替换。
- **复验锚点**：从主菜单进本地多控（三个入口各一次）⇒ 应见
  `历史多人存档本来就不存在，跳过清理（…）` 或 `已清理（…）: 删除 N 个、跳过不存在 M 个`，
  且 `Error deleting path modded/profile*/saves/current_run_mp.save` 应为 **0 条**。

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

### 待查-8 第三方「圣主历险记（ShengZhuSts2Mod 1.0.13）」**每次结束回合必抛 NRE ⇒ `turn loop died`**（2026-10-16 判读；**不是我方**，⭐ 待用户复测确认）

- **现象（用户原话）**：「点击结束回合后不会进入敌方回合」（游戏里能看到内置预设是另一回事，见 §r279）。
- **日志**（`logs-archive/godot__20261016-090151__r279.log`）：`Combat #1 / #2 / #4 turn loop died while its combat is in progress … System.NullReferenceException`，
  栈顶固定是 `ShengZhuSts2Mod.…Patches.RabbitTurnEndHookPatch.RestorePlayerHooksAfter(Task, RabbitHookSuppressionState, string)`
  → `RestorePlayerHooksAndClearContextAfter` → `STS2RitsuLib.Utils.HarmonyIl.HarmonyAsyncTaskBridge.After` → `CombatManager.EndPlayerTurnPhaseTwoInternal`。
  **我方帧一个都没有**。
- **排除我们的三条依据**：① 同机 08:55 那份日志（marker r278、59 个 mod）`turn loop died` = **0**；
  09:01 这份多了 3 个 mod —— **圣主历险记 / Ascendants / SilentFX**，`turn loop died` = **3**（每次结束回合一条）；
  ② 第一次死亡在 **Combat #1**，而本局第一次召唤战神在行 15686 之后 ⇒ **没有战神时照样死**；
  ③ 我们仓里**没有**任何补丁挂在全局 `Hook.AfterSideTurnEnd` / `BeforeSideTurnEnd` 上（只有 LexNinja2 那个按它自己模型方法的守卫）。
- **反编译定位**（`ilspycmd` 该 mod 的 dll）：`RestorePlayerHooksAfter` 第一句就是 `state.DeactivatedPlayers.Count`
  ⇒ **`state == null` 时必 NRE**；而整局**一条【卯兔】日志都没有**（它的前缀里会打「临时停用…」）
  ⇒ 即 **它的 Prefix 没跑、只有 Postfix 跑了**（Harmony 经典成因 = 更早注册的某个 prefix 返回 `false` 跳过原方法；
  该修复版 README 自己写着"尚未逐项验证…其他 mod 组合与多人联机场景"）。
- **为什么不进 `compat-mods/`**：这是**它自己**的健壮性缺口（`state` 可能为 null 没防），不是「主 mod ↔ 它」的交互；
  且我们**没法**从外部给它补 `__state`（Harmony 的 `ref` 参数要求类型精确匹配，而它是个私有嵌套类型）。
- **下一步（用户 1 分钟可验）**：游戏内**关掉「圣主历险记」**再打一场 ⇒ 若不再 `turn loop died` = 确认是它（要留这个 mod 就带栈反馈给作者）；
  若关掉后仍复现 ⇒ 回来找我们，按判读流程重跑（先看"我方锚点在不在" + 跨会话对比）。

### 待查-9 局内**经常没有背景音乐** —— ✅ **已定性并关单（2026-10-21）：第三方幕 `BgMusicOptions` 为空；按口径不修、不做兼容补丁**

> **✅ 判读结果**（`logs-archive/godot__20261021-132230__r292.log`）：`UpdateMusic: act=index0, 候选=0, 选中=none`
> 而 `Audio.PlayMusic: event=event:/music/menu_update, audioNode=ok` 正常 ⇒ 本局跑的是 **ArknightsBoss 的幕**
> （`Preloading 'Act=ARKNIGHTS_BOSS_ACT_GARDEN_OF_GROTESQUERIES_ACT1'`），它的 `BgMusicOptions` 是**空数组**
> ⇒ 原版 `ResolveMusic` 返回 null ⇒ 整幕不放音乐；主菜单走另一条入口所以有声音。
> **拍板（用户 2026-10-21）**：**纯第三方缺内容 ⇒ 主 mod 不修、也不做独立兼容补丁**（暂时没这个需求）。
> r293 曾加过的"原版第一幕兜底"已按此在 **r294 移除**；只读探针 `[BGM探针]` 保留（下次遇到别的幕能一眼看出
> 是不是同一个原因；`LMC_BGM_PROBE=0` 可关）。
> **若将来再提**：先看探针 `候选=` 是否为 0 —— 是 ⇒ 同一个第三方原因（去催那个 mod 补音乐）；
> 不是 0 却仍没声音 ⇒ 看 `失败=<track>`（bank 加载失败）或 `StopMusic` 的调用方。

- **现象（用户原话）**：「最近玩感觉好像经常没有背景音乐」—— 不崩、不是音量设置（音效仍在）。
- **本次零成本排除**（`logs-archive/godot__20261021-124341__r290.log`）：
  - 原版**唯一会动总音量**的 `NMuteInBackgroundHandler` 全程正常：4 次焦点切换全部 `FocusOut → FocusIn →
    Unmuted, restored volume to 1`；只有**退出游戏时**一条 `NullReferenceException @ _Notification`
    （`SaveManager.Instance` 已销毁），与本现象无关。
  - 我方仓里**没有任何 BGM 代码**（全仓搜 `Music|BGM` 只命中 `LocalQuickRestartLoader` 的
    `SfxCmd.Play(CharacterTransitionSfx)`）⇒ 现有日志里**根本没有音乐锚点**，先别猜。
- **下轮判据（按顺序，别跳）**：
  1. **先问用户三件事**：① 整局一直没有 / 还是某次切场景后不再回来？② 与 alt-tab、过场、切角色有关吗？
     ③ 主菜单有音乐、进局内没有？—— 三种答案指向三条完全不同的链路。
  2. **加只读探针**（先让它可观测）：在音乐入口（反编译找 `AudioManager` / `MusicCmd` / 进局的音乐播放点）
     挂**只打日志的前缀**（`[BGM探针] play=…, key=…, source=…`）+ 换场景/开战/战斗结束时打一次"当前曲目"。
     **不打补丁干预**，先要一行能数的事实。
  3. **跨会话 A/B**：同一台机**关掉本 mod** 打一局对照；再区分"第三方 / 原版 / 我们"（
     `references/diagnostics-and-triage.md` §一 三分法）。
- **当前怀疑面（未验证，不是结论）**：本机至少三处会动音乐 —— `IntegratedStrategyEvents`
  （`IntegratedStrategyMusicReturnToMainMenuPatch` / `...AfterRunPatch`，自带战斗音乐）、`Oddmelt`（预载自己的 BGM）、
  `NinjaSlayer` + `RitsuLib`（FMOD bank / `EventMusicGUIDs`）；任一处"切场景后不恢复"都长得像"没 BGM"。
- **优先级**：中（纯体验）；**登记即可，未排期**。

### 待查-10 第三方「悬停 tooltip」与「血条节点」两族 ERROR（2026-10-21 判读；**均为第三方自身缺陷、只登记不动手**）

- **`System.ArgumentOutOfRangeException` @ `BaseLib.Patches.UI.ModSourceTooltip.Fold`**（r298 长局 39 次）：
  栈固定 `ModSourceTooltip.Fold → List.get_Item`，宿主 `NCreature.OnFocus()`（30 次 = 鼠标悬停生物）
  与 `NCreatureStateDisplay.OnHovered()`（9 次 = 悬停状态图标）⇒ BaseLib 折叠 tooltip 时对空/单项列表取下标越界。
  **栈里没有任何我方帧**；影响面 = 悬停提示可能不显示，战斗流程正常（`turn loop died` 0）。
  后续局（20:22）同族还多出 12 条 **`ERROR: Exception thrown while trying to log another exception...`** ——
  看 `### Exception ###` / `### Logger exception ###` 两段可知**同一根因**：Godot 记这条越界时自己又 NRE
  （`ExceptionUtils.LogException`）⇒ 以后见到这一对，不用另开条目。
- **`ERROR: Node not found: "MintyIncomingDamageText"`**（同局 9 次）：MintySpire2 的
  `SummedIncomingDamageRender.RepositionLabel` ← RitsuLib 血条 graft `NHealthBarGraftCompat.TryResizeHpBarContainer`，
  节点路径在 `AllyContainer`（**托管席位血条**）。紧跟在我们「第三方询问本地玩家身份，已按同机席位放行」之后，
  但栈里**只有第三方帧**；与 ⑱「战神血条挪到本体玩家旁边」是同一条血条链。
- **动作**：两条都**不做兜底**（按 compat-mods 口径：第三方自身缺陷 ⇒ 登记 + 探针就行）；若将来真的影响可玩
  （tooltip 卡死 / 血条消失）再谈。

### 待查-11 瓦库自动用药抛异常后把药水留在 `PlayerChoiceContext` 栈顶 ⇒ 原版 pop 遗物报 ERROR（**✅ 已定因并修复（r299）；实机两局无回归，竞态窗口本身靠运气**）

**日志**（`logs-archive/godot2026-10-21T20.52.32__20261021-205232__r298.log`）：

```
[WARN]  [LocalMultiControl] 瓦库自动用药异常（跳过该瓶）: potion=FIRE_POTION, error=Nullable object must have a value.
[ERROR] Tried to pop model RELIC.LOCAL_WAKUU_FORM_RELIC (65152824) from stack of player choice context
        HookPlayerChoiceContext but POTION.FIRE_POTION (26879195) was on the top of the stack instead! (Stack size: 2)
        at PlayerChoiceContext.PopModel ← Hook.AfterAutoPrePlayPhaseEntered
```

**✅ 真因（2026-10-21 r299 定案）：不是「目标为空」，是这一按撞上了「退出这一局」**。
同一段日志的时序（按行号）：

| 行 | 事件 |
|---|---|
| 63118 | `[BGM探针] StopMusic … 调用方=<CloseToMenu>` ⇒ **玩家此刻已经在退出到主菜单** |
| 63159 | 我们的 `瓦库自动用药: potion=FIRE_POTION, target=灵魂枢纽`（目标合法） |
| 63185 | `检测到 RunManager.CleanUp(graceful=True)` |
| 63332 | `瓦库自动用药异常 … Nullable object must have a value`（异步 catch 直到主菜单加载完才回来） |
| 63333 | 原版 `PopModel` 错位 ERROR |

反编译核对（`src/Core/Models/PotionModel.cs:297-354`）：`OnUseWrapper` 在 `PushModel(this)` 之后、
走到 `new BranchingPlayerChoiceContext(this, LocalContext.NetId.Value, …)` 之前还有 **~0.5s 的投掷动画
`await Cmd.Wait(0.5f)`**；而 `RunManager.CleanUp` 会把 `LocalContext.NetId` 置 **null**
（`src/Core/Runs/RunManager.cs:1612`）⇒ **`.Value` 抛 `InvalidOperationException: Nullable object must have a value`**。
这是药水使用链上唯一没有 `HasValue` 保护的那处（`CardModel.cs:1931` 同款，`CombatManager` 各 hook 派发点都做了保护）。
药水自己的 `PopModel` 在异常路径上永远不执行 ⇒ 模型留在栈顶 ⇒ 同 hook 里原版遗物模型 pop 错位。
**影响**：那一瓶没喝成（跳过）、一条原版 ERROR；不影响出牌/回合（该局 `turn loop died` 0）。属**时序竞态**，
不是每局必现。

**r299 修法（三层，均无新反射面）**：
1. **进入清理期就不再开新的用药**：循环内每瓶前 `LocalWakuuRelicRuntime.IsRunTeardown()`（既有口径 =
   `!RunManager.IsInProgress || RunState==null`）⇒ 打 `瓦库自动用药中止（运行已进入清理期）` 并整段退出；
2. **catch 里摘残留**：只在「栈顶**就是这瓶药水的同一个实例**」时调原版 `PopModel`（`RepairChoiceContextStack`
   + 纯逻辑 `PotionUseAbortPolicy.ShouldPopResidualModel`，上限 4 层防死循环），摘到即停、打
   `瓦库自动用药后已摘除残留的药水模型（防后续 PopModel 错位）: … 层数=N`；
   **栈顶是别人一律不动**（原版 `PopModel` 不匹配时只打 ERROR 且不 pop，不能拿它当清栈工具）；
3. **异常日志改口径**：`PotionUseAbortPolicy.DescribeException` = `类型名: 消息 | 栈帧压成一行`（截断 900 字），
   并且清理期的异常降级为 INFO `瓦库自动用药在运行清理期中止（预期内）`（与 r110 的选择器清理期口径一致）。

**复测契约（r299）**：正常打完一局（中途**从战斗里按 ESC 退回主菜单**最理想）⇒
① `Tried to pop model … POTION.… was on the top of the stack` **应为 0**；
② 若又撞上清理期，应看到 `瓦库自动用药中止（运行已进入清理期）` 或
`瓦库自动用药在运行清理期中止（预期内，跳过该瓶）`，且异常详情里带**类型名 + 栈帧**；
③ 正常用药路径的锚点 `瓦库自动用药: …` 与之前完全一致（本修不改行为）。

**✅ 实机两局（2026-10-21 / 10-22：第一局无用药；第二局 3 瓶药 + 立刻退出）**：
两局都 `Tried to pop model` **0**、`turn loop died` 0、我方 `[ERROR]` 0；第二局顺带确认
`瓦库自动用药: …` 三条正常、`退局复位自检: 无残留`。
⚠ **竞态窗口本身没被打中**（第二局：`POTION_SHAPED_ROCK` 的伤害 `L12447` → `CloseToMenu` `L12448` →
`RunManager.CleanUp` `L12520`，药水已经收尾完了）⇒ r299 的两条新锚点（清理期闸门 / 摘残留）
**都未触发**，属**防御性修复**：逻辑按"栈顶即自己才弹"设计、正常路径零行为变化，能跑就一直留着。

### 待查-12 12 席规模的「卡」—— ✅ **2026-10-31 关单（r324~r332）**：我方全部修完（采样已归零），剩下的全是第三方

> **结论（一句话）**：`12 席 / 11 瓦库` 本身能跑 **50~60 fps**；「越打越卡」= 第三方 mod 在**每次 hook**
> 里做全栈回溯之外的重活（`YUICore.YuiRelicTriggerReplay` 每次 hook 写 AsyncLocal + `figure_Saya` /
> `Koishi` / `newsanguo` 三个 Filter），12 席把这套按"牌数 × 人数"放大 10 倍以上。
>
> **我方已修（三层 + r332 三处，全部实机验证）**
> - **r329** `LocalContextThirdPartyIsMePatch`：第三方每问一次 `IsMe` 都做**全栈回溯**（采样显示主线程 63.9%
>   在 `StackTrace.CaptureStackTrace`）⇒ 改**每帧 2ms 预算** + 调用点方法句柄记忆化 + 探针新槽位。
> - **r330** `CombatStateListCachePatch`：原版 `Creatures/PlayerCreatures/Players` **每次访问都
>   Concat+ToList**，而 hook 分发对每个订阅者都取一次（采样 58.6% 在 `List.CopyTo`）⇒ 一帧一缓存 +
>   `CreaturesChanged` 事件失效。
> - **r331** 同上补丁：缓存自身在热路径里调了 Godot icall（`Engine.GetProcessFrames`）⇒ 占主线程 **35.7%**，
>   改 `Environment.TickCount64` 分桶。
> - **r332** 同一条链最后三处：① `ModelDb.GetId<T>()` 每次都跑 `Slugify → Regex.Replace`
>   （采样主线程 **30.2%**）⇒ 瓦库遗物 id 改静态缓存；② `WakuuOwnerSelectorMap.TryGet` 每次 `lock`
>   （**5.6%**）⇒ 写时复制快照、读零锁零分配；③ 栈回溯提到席位资格判定之前 + 预算窗口换托管时钟。
>   复采：主线程 `DualRoleAdventurefixed!` **0 条**，用户「纵享丝滑了」。
>
> **量出来的事实（判读口径见 skill `references/diagnostics-and-triage.md`）**
> - 关掉绝大多数 mod：12 席 floor 2→18、13 分钟，fps 中位 **50~60**、帧时 17~21ms、长帧 0、GC 1~4%。
> - 只加回 `YUICore,figure_Saya,Koishi,newsanguo,YUISpireExpansion,GensokyoSpire,MokouMod,Watcher,
>   Oddmelt,FlawTheFunction,STS2_Things`：fps 28→10（随楼层继续掉），GC 25~39%，分配 300MB/s。
> - 再关掉 `YUICore,figure_Saya,Koishi,newsanguo`（其余 11 个留着）：**floor 12~13 仍有 59/40/47 fps**。
> - 归因链：`Hook.ModifyKeywordsInCombat / AttackIntent.GetTexture → YUICore.YuiRelicTriggerReplay.Wrap
>   → Saya.GeneralizedResistanceSuppressionPatch.Filter → Koishi.SealAllRelicPatch.Filter
>   → newsanguo.AbilitySuppressionPatch.Filter`。
>
> **⭐ 下一轮任务（用户 2026-10-31 点名，非我方 bug）**：
> **`古明地恋 Koishi` / `沙耶之歌 figure_Saya` / `新三国 newsanguo` / `YUI 系列` 四家的兼容优化**。
> 起步口径（按 r332 采样）：
> 1. 三家各挂一个 `Hook.ModifyKeywordsInCombat` 的 `Filter`（`Koishi.SealAllRelicPatch` /
>    `figure_Saya.GeneralizedResistanceSuppression` / `newsanguo.AbilitySuppression`），**叠加时每问一次
>    关键词就把三份全量扫描跑一遍**（`IsInstanceOfClass` 16.3%、三家 Filter 合计 ≈5%）。
> 2. `YUICore.YuiRelicTriggerReplay.Wrap` 每条 hook 写一次 AsyncLocal（上一段实测主线程 10.7%）。
> 3. 做法：先反编译四家的 Filter / Replay 入口，判断能否 compat 成「**结果按帧或按版本号缓存**」/
>    「**只在关键词/遗物集合变化时重算**」；落地按 `compat-mods/` 规矩（独立 dll、**默认关**、只在本地多控会话启用、
>    随主 Release 出 `compat-mods-v<major>.<minor>.zip`）。
> 4. 注意：这三家**不是**「第三方自身缺陷」⇒ 属于主 mod ↔ 第三方交互，符合 compat 收录范围；
>    但**不要**给它们加"缺内容兜底"（2026-10-21 口径）。
> 5. ⚠ 关 mod 前先确认当前存档没有引用它（否则原版读档校验失败会把存档改名成 `*.VAL.corrupt`，
>    `tools\restore_corrupt_save.ps1` 可还原）。
>
> **遗留未定性（登记，不阻塞）**：`手牌UI与数据存在差异` 1→17、`Couldn't get hand node` 1 条、
> `finished execution, but was in state Canceled` 17 条（疑为"真人插队撤动作"副作用，r127 口径内但变多了）。
>
> **同类噪声（纯第三方，只登记不修）**：`MintySpire2` 的 `MintyIncomingDamageText` 节点找不到
> （`SummedIncomingDamageRender.RepositionLabel ← CatchBarResize ← RitsuLib 血条 graft`），每条 ERROR 带 ~20 行栈。
>
> 📦 旧的三次结论（r324~r326 的探针读数、"牌慢 vs 帧慢"、solo 对照前的推断）已切到
> `maintenance-docs\decision-records\archive\TODO-归档（已完成 bug 与功能流水·r96~r226）.md`。

### 待查-13 「怪打死了不结束战斗」= 第三方胜利 hook 抛异常冒穿结算链 —— ✅ **软锁已解决（2026-10-24，保命补丁 v1.0.8；附魔「重新选」过滤待下一局验证挂载）**

- **现象**：12 席 + 11 瓦库局，敌全灭后战斗迟迟不结束（用户等了几十秒）。
- **日志铁证**（`logs/godot.log`，marker r320，L14706→L14763→L15171）：
  `检测到敌方已全部死亡，登记延迟胜利结算观察器` → `触发战斗胜利结算` →
  `[ERROR] InvalidOperationException: Cannot enchant CARD.DEFEND_IRONCLAD with ENCHANTMENT.THINGS_DISPERSE
  because it already has enchantment ENCHANTMENT.NIMBLE` at
  `HextechRunesSponsorPack.EnchantmentMasterRune.AfterCombatVictory` ← `Hook.AfterCombatVictory` ←
  `CombatManager.EndCombatInternal` ← `CheckWinCondition` → **约 40 秒后**才 `Combat state becomes NotInCombat`。
- **根因**：① 第三方【附魔大师】没判"这张牌已有附魔"；② 原版 `Hook.AfterCombatVictory` 是**裸 foreach**，
  没有逐 listener 容错 ⇒ 一个第三方 hook 抛异常就冒穿整条结算链（不切房间、不发奖励）。
  中间的 40 秒 = 11 个瓦库还在动作队列里逐个排队（每个 3~5s）。
- **主 mod 侧（r321，只做诊断）**：`CreatureCmdKillWinCheckPatch.TryFinishCombatAsync` 里 catch，
  打一条点名 ERROR（异常类型 + 消息 + 前四帧栈），**不重试**（那张牌状态没变，重试必然再抛）。
- **根治（仓库外保命补丁，已部署）**：`D:\Download\pain\CombatVictoryHookGuard\`（不进 git、不随 Release 分发）
  —— 前缀重写 `Hook.AfterCombatVictory`，保持原版两轮遍历与调用顺序，只给每个 listener 包 try/catch：
  谁抛就跳过谁、记一条点名 WARN（同 listener/hook/异常类型只记一次），结算继续。没人抛时行为与原来一致。
  代价：那个 listener 这次的效果丢掉（本例 = 那张牌这次不附魔）。
- **复验锚点**：敌全灭后应见
  `[CombatVictoryHookGuard] INIT_OK: 已接管 Hook.AfterCombatVictory`（启动时一次）与
  `[WARN] [CombatVictoryHookGuard] 第三方 hook 抛异常，已跳过并继续战斗结算: model=…EnchantmentMasterRune…`，
  **紧接着**就是奖励界面 / `Combat state becomes NotInCombat`，不再等几十秒。
- **第二轮实机（2026-10-24，marker r321）：v1.0.0 没修好** —— 它吞掉了第一个异常（日志有那条 WARN），
  但**自己随后抛了 NRE**：`NullReferenceException | at Hook.AfterCombatVictory_Patch4(IRunState, ICombatState, CombatRoom)`
  ← `EndCombatInternal` ← `CheckWinCondition` ← `SawatariWinCheckPatch.Postfix`（NinjaSlayer）。
  栈顶是**同步帧** ⇒ 出在 `RunTolerantAsync` 的同步段（不在被 try 包住的 listener 调用里）。
  同时暴露第二个会抛的第三方：`HextechRunes.HextechMayhemModifier.AfterCombatVictory` →
  `HextechMaxHpScaling.ReapplyScale` → `CreatureCmd.SetMaxHp` → `NHealthBar.RefreshValues` →
  `MintySpire2.RepositionLabel`（`MintyIncomingDamageText` 节点不存在 ⇒ NRE，本局 19 处栈内提及）。
  ⇒ **v1.0.1 两条加固**：① **整体 try/catch**（整个方法体任何异常都吞 ⇒ 返回的 Task 永远成功）；
  ② **先物化 listener 列表**（惰性 `IterateHookListeners` 一次取完并包 try/catch，避免上位 listener
  把状态搞半损后迭代器自己炸）。**铁律：兜底方法自身绝不允许抛。**
- **第三~五轮实机（v1.0.2 → v1.0.5）：每吞一层就冒下一层** ——
  v1.0.2 加 `Hook.AfterCombatVictory` 的 **finalizer**（实机证明我们 prefix 返回 false 后，
  **同方法上别人家的 postfix 仍会抛**，finalizer 是唯一盖住整条 wrapper 链的位置）；
  v1.0.3 想兜 YUI 的 `Cancel` NRE 但**类型没挂上**（`YuiExtra` 程序集在 Init 后才加载，v1.0.4 改成
  AssemblyLoad 重试 + 候选类型自检）；v1.0.5 换打法——**已知必抛的第三方进结算前整家跳过**
  （黑名单 `HextechRunes*`）⇒ 软锁解决（一整局 15 场全部正常结算），但代价是海克斯战后符文全废（36 个 listener）。
- **v1.0.6 精准修复（用户拍板方向）**：反编译确认【附魔大师】选牌逻辑本身已会"全不可附魔就跳过、选完复核"，
  唯一漏判 = **没排除"牌上已有别的附魔"**（`EnchantmentModel.CanEnchant` 只看类型/关键字；
  `CardCmd.Enchant` 才判已有附魔，同类型=叠加合法、不同类型=抛）⇒ postfix
  `RandomEnchantmentPool.GetLegalEnchantments(CardModel, pool)`：已有附魔的牌只保留**同类型**候选，
  没有就上层自动重选/跳过 ⇒ **黑名单收窄**到只剩 `HextechMayhemModifier`（Minty 缺陷独立存在），海克斯符文恢复。
- **v1.0.7/v1.0.8**：v1.0.6 的过滤没挂上 —— SponsorPack 的**变体 dll**（`lib/0.111.0/`）不走
  `AssemblyLoad` 可见路径（v1.0.7 加了候选自检 dump 也没触发）⇒ v1.0.8 改**惰性挂载**：
  我们的 `Hook.AfterCombatVictory` prefix 在每次结算时执行，那时 SponsorPack 必然已加载，
  顺手把过滤挂上（对本场后续调用立即生效）。
- **复验锚点（下一局）**：启动见 `INIT_OK v1.0.8` + `附魔大师「重新选」修法已挂上`；
  战后若它又抽到已附魔牌 ⇒ `附魔大师候选过滤: CARD.X 已有附魔 … 候选 N → 只留同类型 M 个`，
  且 `已跳过…` 只剩 `HextechMayhemModifier`；`战斗胜利结算被异常打断` 保持 **0**。
- **归属**：第三方自身缺陷（单人装同一个 mod 也会触发）⇒ 按口径不进 `compat-mods/`、不随 Release 分发；
  已登记 `mod_registry.json`（带 ⚠ 说明）。上游修好后本补丁可静默保留（没人抛时无副作用）。
- **优先级**：中（软锁级，但只要持有该遗物 + 有已附魔牌就必现）。

## 维护性改进 backlog（门禁体系 2026-09-08 之后的那批）

已落地（明细见 `AGENTS.md` §9 门禁表 + `Scripts/Tools/`）：源码隔离 Guard、单元测试门禁、
CLR/PE/Assembly 三层兼容检查、Critical/Optional 补丁分级、`INIT_OK`/`INIT_FAILED` 终态收口 + 统一错误码、
`dll_check` false-green 修复、marker 缺失 FAIL、`log_parser --init-status`、Harmony owner 冲突检查（r88）、
BuildIdentity 增强（r89）、部署槽唯一性检查（r93）、标准化回归验证契约（AGENTS §10）、
`ExpectedPatchTargets` 完整签名（r92）、发布包构建元数据（2026-09-19）、S8 源码编码卫生、
格式存量收口（2026-10-05，口径 = 不把 `dotnet format --verify-no-changes` 加进强制门禁链）。

条目状态：第 3 项 **2026-10-23 已关单**；第 4 项 **2026-10-23 新登记（用户点名「下个对话干它」）**。

1. **版本 lane + 根 loader（**待正式版更新再做**，用户 2026-09-19 拍板）**：
   现状 = 只吃当前游戏版本（public-beta `0.111.0`），而原作者的 mod 依旧适配现在的正式版
   ⇒ 现在做没有任何收益，**先不做**。等**正式版更新**（我们和原作者都不得不跟版本）时再动手：
   根目录放一个极薄的 **loader dll**（读 `release_info.json` 判定宿主游戏版本 → 用自定义
   `AssemblyLoadContext` 从 `lanes\<gameVersion>\` 加载对应实现），参照社区两个工坊作品
   （`3799476240` CouchCoop 的 `lanes/0.107.1` + `lanes/0.111.0`；`3802686135` LocalCoopClone 的 `lib/<ver>/`）。
   我们侧额外成本：每个 lane 需要一份**独立的引用路径配置** + 各自重跑 `regenerate_src.ps1`
   + S7 目标基线按 lane 拆（`targets.baseline.txt`）。
   ⚠ 前置判断：**只有真的需要同时支持两个游戏版本时才做**，单版本下 lane 纯属增加复杂度。

2. **源码换行符（EOL）卫生检查（2026-10-11 登记）**：`.gitattributes` 声明 `*.cs … working-tree-encoding=UTF-8 eol=lf`，
   且 index 里存的确实是 LF，但**写文件工具默认产 CRLF** ⇒ 工作区跨轮累积（2026-10-11 一次清掉 19 个 `.cs`，
   连同 `tests` / `compat-mods` / `release` 共 67 个文本文件规范化成 LF）。
   根治 = 给 `Scripts/Tools/static_checks.py`（S8 源码编码卫生）加一条"`Scripts/**/*.cs` 等不得含 CRLF"的检查，
   或者**每轮收工顺手规范化一次**。⚠ 两个坑：① 规范化对**提交内容零影响**（`git hash-object --path=<f>` == `HEAD:<f>`），
   但 `git status` 会多列一批 ` M`（stat 缓存噪声，**以 `git diff --name-only` 为准**）；
   ② 规范化会改变 **dll 字节**（本仓库构建不逐字节可复现）⇒ **必须排在最后一次构建之前**，否则要重跑 build + deploy 并订正 sha。

3. **补丁 mod 的「本体已修 ⇒ 退场自检」+ 加载项精简（✅ **关单 2026-10-23**：r308 核查（含 `Act4FinalAscentFixes` 第二轮反编译订正）→ r309 退场 2 个槽位 → r310 两条 P1 收尾。结论 = `maintenance-docs\补丁mod退场核查.md`；⚠ 只剩「registry 摘掉两条 obsolete 登记」这件 cosmetic 和「真玩 Act4 时把 loc 补丁改成注入别名键」（P3，触发条件见下））**：
   用户原话：「我们有若干补丁 mod，但是不知道这些 mod 能不能检测本体 mod 有没有修，如果本体 mod 修了补丁报错还行，
   不报错就静默运行卡我游戏加载时间了」。
   **现状盘点**（2026-10-21）：仓库外 11 个补丁/辅助 mod（`Act4FinalAscentFixes` / `EnchantTargetRandomFix` /
   `HextechBurnVisualFix` / `HistoryFilter` / `HistoryLoadoutExport` / `LexNinja2LaserFix` / `OddmeltGaugeCardRenderFix` /
   `TouhouAncientsAncientFixes`（obsolete）/ `TouhouAncientsMiniHakkeroFixed`（obsolete）/ `YuiVfxSceneFix` /
   `YuWanCardWhiteScarfFix`）+ 仓库内 `compat-mods/` 3 个（`HextechRunesLocalCoopFix` / `NinjaSlayerBossGreetingFix` /
   `PreloadStallGuard`）。现在只有"**目标成员缺失 ⇒ 不挂补丁**"这一种防御（多数 mod 有），
   **没有"目标还在、但上游已经自己修好"的判定**，也没有"这个 mod 现在还需不需要"的统一结论 ⇒ 会静静加载、静静挂补丁。
   ⚠ **先认清成本结构**（否则方案会做成无效功）：**补丁不挂 ≠ 省加载时间** —— 游戏仍要扫槽位、加载 dll、跑
   `ModInitializer`。真正省时间只有三条：① 提示用户**禁用/删除**槽位；② **合并**多个小补丁成一个 dll（减少槽位/程序集）；
   ③ 让 `ModInitializer` 尽早 return（省 Harmony 挂载开销，省不了 dll 加载）。
   **拟做（待拍板先做哪条）**：
   - **A. 统一退场自检**（每个补丁 Entry，三层判据）：① 目标类型/方法**不存在**（改名或已删）；
     ② 目标存在但**形状变了**（已有 `参数个数已变` 这类 WARN ⇒ 可升级为"疑似已修"）；
     ③ **适用上游版本基线**（照 `compat-target.txt` 的做法：当初修的是哪个上游版本；上游版本 > 基线 ⇒ 提示"可能已修"）。
     输出统一一行：`INIT_SKIPPED: 目标 mod 已更新到 <ver>，本补丁可能不再需要（依据=…），建议禁用/删除本 mod`，**且不挂任何补丁**。
   - **B. 巡检工具** `tools/scan_compat_need.py`（本机层，不动游戏）：扫 mods 槽位 + 各补丁的"适用上游版本"表
     ⇒ 输出"建议保留 / 建议删除"清单（附证据：dll 版本、目标是否存在、上游 manifest 版本）。这条**最直接省加载时间**。
   - **C. 合并**（可选，先不动）：把 `compat-mods/` 3 个 + 若干仓库外补丁合成一个 `LmcCompatSuite` dll；
     代价 = 单点失败连累整包 ⇒ 必须"每组独立 try/catch + 各自 INIT 行"。
   ⚠ **判据坑**：`目标消失` 既可能是"上游已修"、也可能是"上游改名"⇒ 不要自动当成已修就静默退场，
   必须打醒目日志 + 建议人工确认（尤其仓库外私有补丁 `HextechBurnVisualFix`：用户明确要求上游自修后靠 `HBV_DISABLE=1` 退场）。
   ⚠ 已知特例：`HextechRunesLocalCoopFix` 已出现 `CreateRuneSelectionScreenAsync 期望 10，实际 11`（上游加参数）——
   这正是"形状变了"的活样本，可作为 A-② 的首个用例与回归锚点。

   **✅ 已核查（2026-10-23 r308）—— 结论表 + 逐条证据 + 复跑工具：`maintenance-docs\补丁mod退场核查.md`**：

   - **可退场 2 个（✅ 2026-10-23 r309 已退场：槽位已删 + registry `enabled=false`）**：
     `YuWanCardWhiteScarfFix`（上游 0.5.12 已自修，反编译 `WithCardPools(ColorlessCardPool)+flags16`
     与补丁重建等价）/ `OddmeltGaugeCardRenderFix`（主 mod 已内置**逐字相同**的
     `Scripts/Patch/NCardCreateHiddenCardGuardPatch.cs`，且在 `baselines/targets.baseline.txt` 里）。
   - **仍需要 8 个**：`Act4FinalAscentFixes`（⚠ **第二轮反编译订正**：0.2.2 只补了 Powers 的 57 条复合 Entry
     本地化别名键，事件/遭遇/幕的 loc 键仍无前缀、Boss 地图图标仍无扩展名、Power 图标仍只有 `architect_*.tres`
     ⇒ **4 条补丁全都要留**；运行时 Entry 是 `ACT4_FINAL_ASCENT_<CATEGORY>_<TYPE>`，证据 = 存档里的
     `EVENT.ACT4_FINAL_ASCENT_EVENT_...` + RitsuLib `ModelDbModdedEntryPatch` 强制复合命名；详见核查文档 §3.1）/
     `EnchantTargetRandomFix`（本体 `Inky.OnPlay` 无判空；2026-09-19 r140 实机仍命中兜底 ×2）/
     `HextechBurnVisualFix`（上游 0.9.8 仍逐骨 `Call("get_world_x")`、无有效性校验）/
     `LexNinja2LaserFix`（本体 `vfx_hyperbeam.tscn` 未修 + `ShowLaser` 裸解引用 + LexNinja2 3.0.10 仍调 `NHyperbeamVfx`）/
     `YuiVfxSceneFix`（`.tscn.remap` 覆盖仍在，2026-10-11 r262 仍触发重建）/
     `HextechRunesLocalCoopFix`（`INIT_OK patched=9`，上游无本地多控支持）/
     `NinjaSlayerBossGreetingFix`（1.1.1 lane dll 仍 `return val.NetHost.ConnectedPeerIds…` 无判空）/
     `PreloadStallGuard`（兜底；历史 3 次强制放行，2026-10-03/04/08，全为 `MomoVfx`）。
   - **不属退场范围 2 个**（辅助功能，`INIT_OK`）：`HistoryFilter` / `HistoryLoadoutExport`。
   - **obsolete 残留 2 条**：`TouhouAncientsAncientFixes` / `TouhouAncientsMiniHakkeroFixed`（`enabled=false`、无槽位）。
   - **顺带发现（均已处理或已记账）**：工坊两个 `Oddmelt` 同 id ⇒ **✅ 用户已退订旧件**；
     `CreateRuneSelectionScreenAsync 期望 10，实际 11` 的 WARN ⇒ **✅ r310 改成按参数名验注入名**（v1.7.1）；
     `Act4FinalAscentFixes` 的激光那条与 `LexNinja2LaserFix` 同代码（可留一份）；
     `Act4FinalAscent` 自身两条：① 5 个 power 图标上游就没给（`Act4ArchitectRevivalPower` + 4 个书类），
     ② 它自己的 `ImageHelperGetRoomIconPathPatch` 用**无前缀** Entry 比对 ⇒ 永远不命中（上游 bug）；
     ③ **我们的覆盖漏洞**：`EventModel.GameInfoOptions` 走 `LocTable.Keys` 直接枚举、不经 patch 的 4 个入口
     ⇒ 复合 Entry 下会抛 `LocException("Event Loc … does not conform")`（P3 一起修）。

   **✅ 执行结果（r309 / r310）**：
   1. 两个可退场项 → **r309**：槽位目录已删（`mods\YuWanCardWhiteScarfFix\`、`mods\OddmeltGaugeCardRenderFix\`；
      源码在 `pain\` 下，`-Only <Name>` 可重建），registry 两条改 `enabled=false` + OBSOLETE；游戏内 `is_enabled`
      本来就是 false ⇒ **未动 `settings.save`**；
   2. `HextechRunesLocalCoopFix` **v1.7.1**：基线 10 → 11 + `FindUnboundPatchParameters`（真按参数名验绑定）；
   3. `LexNinja2LaserFix` **v1.0.1**：`PatchClassProcessor` 逐个类挂 + 启动打 `PATCH_VERIFY`（含 build tag）；
      两个 dll 均已构建 + 字节校验部署；门禁 `preflight -Lint` = 4 PASS / 0 WARN、`check_string_targets` STALE 0。
   4. ⏳ 剩下两件（都不阻塞）：registry 摘两条 obsolete 登记（cosmetic）；真玩 Act4 时再做 P3。
   **巡检复跑**：`python D:\Download\pain\tools\scan_compat_need.py`（本机层；只输出机械事实 + 复核线索，
   **不判断上游是否真修好** —— 有「上游晚 5 天但没修」与「上游晚 17 天真修了」两个反例）。
   ⚠ 判据红线（不变）：**目标消失 ≠ 已修**；`HextechBurnVisualFix` 走 `HBV_DISABLE=1` 退场；
   任何"可退场"都先给用户确认，不擅自删槽位；**补丁不挂 ≠ 省加载**（真正省时间只有「禁用+删槽位」与「合并 dll」两件事）。

4. **启动耗时分段定位（`✅ 定位完成（r312 打点 / r313 心跳）`：2026-10-23 判读 —— **大头全在第三方与引擎，我们只占 0.8%**；候选优化已记账，**不动代码**）**：
   **要回答的问题** = 「进主菜单那 **120 秒**里，时间到底花在哪？哪些是我们能省的？」
   **已做（r312，2026-10-23）**：
   - 主 mod `Entry.Init()` 分段打点 —— `[StartupProf] mod_init_begin`（`since_process_start_ms`）/
     每阶段 `stage=<名> ms=` / 汇总 `mod_init_total ... stages=A:B|C:D`；
   - `StartupProfilerPatch` 手工挂 `ModManager.CallModInitializer`（**无类级 `[HarmonyPatch]`**、失败只 WARN）
     ⇒ `[StartupProf] other_mod_init index=… ms=… since_process_start_ms=… mod=<程序集>/<类型>`；
     只覆盖**排在主 mod 之后**的 mod（前面的已经跑完，由 `mod_init_begin` 的位置整体兜住）；
   - 三个 `compat-mods` 各打一行 `[StartupProf] mod_init_total`；
   - 台账 `D:\Download\pain\tools\startup_profile.py`：四段拆分（init 前 / 我们 / 后续 mod / 余下）、
     `--pair A B`（指标差值 + 启用 mod 集差异 = 这轮禁用了谁）、`--stages`（阶段明细 + 逐 mod 排名）、`--json`。
   - **r313 追加**：5 秒一拍的**启动心跳**（`[StartupProf] heartbeat n=… since_process_start_ms=…`，
     最多 36 拍）—— 日志没有行时间戳，靠它把"某段日志"与"花了多少秒"对齐；
     台账 `--stages` 同时解析游戏自报的预加载会话（`Preloading '…' Complete: time_elapsed=`）。
   **首轮判读（r312，`logs-archive/godot__20261023-110127__r312.log`，到主菜单 112.5 s / 62 个 mod）**：
   - **init 前 21.4 s（19%）** / **我们 773 ms（0.7%）** / **后续 mod 12.2 s（11%）** / **余下 78.1 s（69%）**；
   - 后续 mod 单只最大 = `RegentFX` **5.40 s**（其次 HextechRunes 1.70 / SponsorPack 1.31 / Koishi 1.15 / SkadaHelper 0.76）；
   - **预加载只有 7.5 s**（Common 5.19 s + Loadout 选择屏预热 2.0 s）⇒ 「余下」78 s 里 **~70 s 不是预加载**，
     也不是我们的初始化 ⇒ **必须用心跳把 mod 注册段 / 主菜单构建段切开**（否则"预加载很重"这个先验是错的）。
   **r313 时间轴判读（`logs-archive/godot__20261023-110843__r313.log`，107.7 s / 62 mod，19 拍心跳）**：
   | 段 | 秒 | 内容 |
   |---|---|---|
   | 0~26 | 25.6 | 引擎启动 + 前 37 个 mod 初始化（含 RitsuLib 自报 3.2 s） |
   | 26~31 | 5 | `RegentFX` 加载 pck + 同步预加载 32 资产（自报 4.6 s） |
   | 31~36 | 5 | 内容注册 2768 条（`[Content] Registered …`） |
   | 36~41 | 5 | loc 表合并 474 条 |
   | 41~46 | 5 | 图集 / ModelDb 初始化 |
   | 46~71 | 25 | 资源预加载会话 + `Loadout` 12 个选择屏预热（刷 225 条带堆栈的 WARN） |
   | **71~96** | **25** | **只有 `Sts2TouhouSakuya` 的 `Could not find power image path` 在说话（每段仅 7~37 行）** |
   | 96~101 | 5 | `Loadout` 预热收尾（`Registered 12 and prewarmed 12 …`） |
   | 101~106 | 5 | 只有 1 行日志 ⇒ 主菜单构建 / 渲染初始化 |
   | 106~107.7 | 2 | 设置按钮注入 / BGM / 进主菜单 |
   **结论**：① 我们 **832 ms（0.8%）**，没有优化空间；② 最大单块 = `Sts2TouhouSakuya` 的 power 图标缺失探测
   **25 s**；③ 次之 = 引擎 + 前 37 个 mod 25.6 s、`Loadout` 预热 ~20 s、`RegentFX` 4.6 s、内容注册/loc ~10 s；
   ④ **预加载自报合计仅 7.7 s** —— "VRAM 6.7 G ⇒ 预加载是大头"的先验被数据否掉。
   **可选尾巴（不阻塞，想省时间才做）**：a) 游戏内禁用 `Sts2TouhouSakuya` 跑 3 次，验证能否省 ~25 s
   （**只能游戏内禁用**，挪目录不算）；b) `Loadout` 若有"少预热选择屏"的配置可关；c) 内容 mod 退订是唯一能减注册段的手段。
   **已交付**：四段表 + 逐 mod 耗时 + 心跳时间轴 + 预加载会话统计，全部可用
   `python D:\Download\pain\tools\startup_profile.py --stages` 复查。
   **历史数据**（同一台机、同一套 63 个已启用 mod，两轮实测）：
   - `[Startup] Time to main menu` = r307 **117.9 s** / r311 **120.2 s**；
     `Resource stats (main menu loaded (complete))`: `VRAM=6.6GB, Objects≈59.5k, Resources≈5.7k, Nodes≈20k, CachedAssets=80`；
     日志 = `logs-archive/godot__20261022-231503__r307.log` 与 `logs-archive/godot__20261023-102723__r307.log`。
   - **同轮对照**：删掉两个"游戏内已禁用"的补丁槽位（r309）后启动时间**无可测差异** ⇒ 大头不在"被禁用的补丁静默加载"，
     也不是"少扫几个 manifest"能解决的。
   - **现有可用的时间信号只有 4 行**（`Time to main menu` ×2 + `Resource stats` ×2）⇒ **不足以分段**，必须自己打点或做 A/B。
   **做法（先量后猜，按这个顺序做，别跳步）**：
   a. **先 A/B 定位大头（最省力）**：每轮只在游戏内**禁用 1 个** mod（不必删槽位），连跑 **3 次取中位**，
      ⚠ **不能用"把 mod 目录挪走"当禁用**（用户 2026-10-23 当面提醒）：启用开关在 `settings.save` 的
      `mod_list`（id+source 双键），目录不在只是"扫不到"，与游戏内禁用不是一回事，A/B 结论会失真；
      与基线比 `Time to main menu`；优先嫌疑 = 重 pck/图集 mod（`CachedAssets=1649`、`VRAM` 6.5G 说明预加载很重）、
      `RitsuLib`（日志自称挂 432+120+53+31+3+22 个补丁）、`BaseLib`/`MomoLib` 这类框架、以及装了很多内容的角色 mod。
      **判据**：单次差异 **> 5 s** 才算数；单次波动不算（这台机 MV 波动大，必须中位）。
   b. **再给启动分段打点（只打我们能控的）**：主 mod `Entry.Init()` 首尾、各 `compat-mods/*` 的 `Install()` 首尾
      ⇒ 先证明"我们的初始化不是大头"（预期 < 1 s），再谈要不要优化；第三方只能用"少了它快多少"间接量。
   c. **产出台账工具** `tools/startup_profile.py`（本机层，只读归档日志）：解析每轮 `Time to main menu` +
      `Resource stats` + 该轮启用的 mod 集（日志里 `Finished mod initialization for …` 那段就是清单），
      输出「本轮 vs 上轮差值 / 本轮禁用了谁」的表 —— 让 A/B 可跨会话复查，不用靠记性。
   d. **只优化我们能控的地方**：查主 mod / compat 补丁初始化里的可省项（重复 `AppDomain.GetAssemblies()` 全表
      `GetType` 扫、同步 IO、重复反射解析）；第三方慢**只记账 + 给建议**（退订 / 换轻量配置），不改人家代码。
   **红线**：① 不许为快砍功能或砍护栏（`PreloadStallGuard` 这类兜底不算"开销"）；② 第三方自身慢不擅自修
   （`compat-mods` 只收主↔第三方**交互**补丁，见既有口径）；③ 任何"优化"必须有 A/B 数据（≥3 次中位）；
   ④ 结论口径 = 「这台机 / 这 63 个 mod / 这一版游戏 v0.111.0」，**不许外推**成"我们的 mod 很快"。
   **交付判据**：能一句话答出「120 秒里 X% 在资源预加载 / Y% 在 mod 初始化 / Z% 在别处」+ 有可复查台账；
   若结论是"大头是第三方预加载"，**到此为止**（记账 + 给退订建议），不动代码。

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

## 战灵召唤（原 ④「地狱战神」）—— ✅ D1~D13 已拍板（2026-10-08），Phase 0 POC 开工中

> 命名口径（2026-10-06 用户拍板）：**「地狱战神」= 本节（战灵召唤）**；
> **「瓦库的爹」= 上一节那条**（遗物 + 三张占位牌，r227 已实现）。

**新版设计**：遗物【地狱耳环】+ 休息处【分裂精神】+ 战斗内**每回合消耗计数召唤临时玩家「战神」**
（预设角色/血量/遗物/卡组、挡伤 1/2、绑主死亡联动、消耗计数复活、智能瓦库 + 台词「唔」、无自己的战斗奖励、【我挡】/【你攻】同样适用）。

- ✅ **D1~D13 已全部拍板（2026-10-08）** ⇒ 结论逐条落档在
  `maintenance-docs/decision-records/地狱战神-战灵召唤设计方案与可行性分析.md`（#21）**§7.1 拍板附录**
  （含 **D5 = 做成可设置档位**、**D7 = 分档做、首档「普通」= 运行期读角色初始遗物+初始卡组 ⇒ 自动兼容 mod 角色**、
  D13 默认档 10%/**15%**/20%）。⇒ 已切 `feat/hellwar-god-poc` **开工 Phase 0 POC**
  （临时席位名单 + `CreatureCmd.Add` 塞一个哑战神 + 立绘/回合推进/战后清理三件验证）。
- **Phase 0 POC 多轮实测（2026-10-08）**：症状从"五条一起出满"逐轮收敛到"只剩结束回合"，
  三个根因**都是"漏抄游戏建正式玩家的步骤 / 漏认临时席位"**（不是战神自己不可用，而是**公共路径先炸**）：
  ① r236 = 漏**战斗态**（`ResetCombatState` + `PopulateCombatState`）⇒ 真人一抽牌就 `turn loop died`；
  ② r237 = 漏**卡组牌 `Owner`**（只有 `RunState.AddPlayerDebug` 会赋值）⇒ 初始洗牌 `CombatState.Contains` NRE；
  ③ r238 实测「能抽牌/出牌/切人、瓦库也打牌，**就是结束不了回合**」= 漏**敌方回合就绪补齐**
  （`SetReadyToBeginEnemyTurn` 的完成判据含它，而回合循环只替 `LocalContext.GetMe` 那一席报就绪）⇒ r239 把临时席位加进补齐集合。
  ⇒ 修法总纲 = 照抄 `RunState.AddPlayerDebug`（**只剔除"进 `runState.Players`"这一条**）+ 开战两件事 + 清理镜像 + **所有"按 `Players.Count` 算齐"的集合都要显式认临时席位**；
  另补：视觉节点/`StateTracker` 自己收、生成异常时回滚半成品战神。
  详根因与栈 → `references/local-multicontrol-pitfalls.md` **坑 AC-5 / AC-6 / AC-7 / AC-8**；design doc §7.1 两轮「POC 实测修正」。
- **✅ Phase 0 POC 实机关单（2026-10-08，marker `2026-10-08-r239`，`logs-archive/godot__20261008-220309__r239.log`）**：
  用户「**一切都好正常**」；日志锚点齐 `①①b②②b③④` + **战神回合自动收口 ×2**（`战神已自动结束回合（POC 不出牌）`）
  + 战斗正常推进到结束 + 战后清理干净（`已从战斗移除战神: 剩余 Players=2` → `临时席位已注销: reason=combat-cleanup`）
  + **奖励只发给两位真人**（`rewardCount=2 / 5`，没有战神那份）+ 异常族全 0（`turn loop died` / `### Exception ###` / NRE / LocException / 生成异常 / 回滚 均 0）。
  顺带确认：既有 `[LexKela守卫]` 兜底**自动覆盖了临时席位**（`已为未登记玩家补键…涉及席位=[4096, 4096]`）。
  ⇒ 三条验证（立绘/回合推进/战后清理）全部达成，**下一步进 Phase 1**。
- **✅ Phase 1 实机关单（2026-10-09）** —— 只做"计数"这条线，战斗内召唤留 Phase 2：
  - **判读（r240 代码 = 部署位 `2026-10-09-r240`，日志 `logs-archive/godot__20261009-183538__r240.log`）**：
    用户「大概没什么问题」+ 锚点全绿：`遗物【地狱耳环】已就绪: 计数显示=True` → `已为真人角色发放` →
    `休息区已注入【分裂精神】选项: 计数=0, 扣血档位=10%, 免费额外选项=True` →
    `【分裂精神】已完成: 扣血=7（70→63/70, 档位=10%）, 计数=0→1` → `免费额外选项生效：…保留其余休息处选项`；
    异常族全 0（3 条 ERROR = Steam 分支告警 / 引擎退出泄漏 / 第三方 Minty 节点，非我方）。
  - **同一份日志还抓到下一个问题的证据**：`休息区进入后选项检查: options=7` ⇒ 选项溢出屏幕 ⇒ 下面的 r241 追加项。
  - **配置三项**（设置页三行，都默认保守）：`hellwarGod`（总开关，默认关）、`hellwarSplitHpRatio`（扣血档位 10 / **15（默认）** / 20）、
    `hellwarSplitFree`（默认开 = 用完【分裂精神】还能继续休息 / 锻造）。
  - **遗物【地狱耳环】**（`Models/Relics/LocalWakuuHellwarEarring.cs`）：计数走原版原生三件套
    `ShowCounter` + `DisplayAmount` + `[SavedProperty] int SpiritCount`（**存档往返与遗物格子上的数字都白得**）；
    开局按 `HellwarSummonPolicy.ShouldGrantEarring`（开关 && 真人席位 && 非瓦库）发放，发放点 `GrantHellwarEarringAsync`。
  - **休息处【分裂精神】**（`HellwarSplitSpiritOption` + `HellwarSummonRuntime`，注入点 `RestSitePatch`）：
    只给真人席位、图标提前注册（复用 `LocalRestSiteOptionIcon`）；**先扣当前血量的档位百分比（向下取整、clamp ≥1 不扣死）
    → 再给耳环 +1 计数**；扣血走 `CreatureCmd.SetCurrentHp`（非战斗安全，避开 `SetMaxHp(0)` 老坑）。
  - **D5 档位**落在遗物上：覆写 `ShouldDisableRemainingRestSiteOptions`（原版【迷你帐篷】同款钩子）+ **一次性标记**
    （否则免费动作会泄漏给本次休息的其他选项）—— 见 references 坑 **AD-1**。
  - **纯逻辑 + 单测**：`PureLogic/HellwarSummonPolicy.cs`（强度→成本 D7 / 可召唤判据 R6 / 扣血档位 D13 / 发放判据）
    + `tests/.../HellwarSummonPolicyTests.cs`（档位、取整、clamp、成本、可召唤）；`WakuuConfigJsonTests` 补默认值与序列化键断言。
  - **内容挂点** `Patch/HellwarContentPatch.cs`（`LocManager.Initialize` 后注入遗物 / 休息处选项本地化，
    `ModelDb.Preload` 后打 `[地狱战神] 遗物【地狱耳环】已就绪` 锚点）+ `LocalWakuuHellwarLocalization`。
  - **门禁**：构建 0 警告 0 错误（294 .cs）→ 单测 **966 全绿** → `preflight -Deploy` = **G1 10 PASS / 0 WARN**、
    **G2 `STALE=0`**、**G5 PASS**；部署位 marker **`2026-10-09-r240`**（部署位与仓库根字节一致，sha256 `89a7ee89b7bb…`）；
    targets 基线已刷新（194 类 / 230 目标行）。
- **🆕 r241 追加四项（2026-10-09 第二轮，实机复测后的用户追加；marker `2026-10-09-r241`，⭐ 待实机）**：
  1. 🔴 **休息处选项横向滚动条**（用户：「选项太多了，导致有时候根本放不下」）＝ 配置 `restSiteOptionScroll`（**默认开**）。
     原版 `%ChoicesContainer` 是写死 **799px** 的居中 HBox、按钮 247px + 间隔 100 ⇒ 内容宽 = n×247+(n−1)×100
     （原版 3 个 = 941、5 个 = 1635 摆得下；**6 个 = 1982 就开始超出 1920 屏幕**，7 个 = 2328 两端点不到）。
     做法：**内容超出屏幕可用宽时**才包进横向 `ScrollContainer`（`ScrollMode.Auto` + `FollowFocus`，
     窗口宽 `min(内容宽+12, 屏幕可用)`）。见 references 坑 **AD-5**（两条硬要求：HBox 必须设 `CustomMinimumSize.X`、
     包装层要向下加高给滚动条留一行；以及"别用延迟布局的 `Size`/`GetCombinedMinimumSize` 做判定"）。
     - ⚠ **r241 首测翻车（三条症状同一个根因）**：日志 `需要宽=7183, buttons=21` —— 游戏换选项时 `QueueFree` 的旧按钮
       **当帧还在子节点列表里**，按 `GetChildCount()` 量宽度把三代叠加 ⇒ HBox 撑到 7183 + `alignment=Center`
       ⇒ 左侧多出 ~2427px 空白（"滚到最左什么都没有、1/3 处才见到第一个选项"）、可见内容被 `clip_contents`
       夹在边缘 ⇒ 文字看不全、点不到。⇒ **r242 已修**（只数活着的 `NRestSiteButton` + 挂
       `ChildEnteredTree`/`ChildExitingTree` 自愈 + 判据改按屏幕宽）；算术收进纯逻辑
       `RestSiteOptionScrollPolicy` + 单测。见坑 **AD-5b**。
     - ⚠ **r242 复测后剩下的两条（同一批原版写法造成）⇒ r243 已修**：
       ① **点不了** = `Reparent` 触发 exit/enter tree ⇒ 按钮 `_ExitTree()` 取消了 `AnimateIn` 等待的 CTS
       ⇒ `mouse_filter` 永远停在场景里的 `Ignore` ⇒ 收不到 `MouseEntered` ⇒ `NClickableControl` 的按下/松开
       （要求 `IsFocused`）全废 ⇒ 自己补 `MouseFilter = Stop`（`RepairButtonClickability`，日志 `可点性已修=n`）；
       ② **文字不完整** = 选项名字标签**挂在按钮下方**（`offset_top = offset_bottom = 165`，原版靠不裁剪才看得见）
       ⇒ 包装层 `clip_contents` 切掉下半截、滚动条还压在字上 ⇒ 包装层向下**先留名字带再留滚动条**。
       见坑 **AD-5c / AD-5d**。
  2. **每进入新的一幕自动获得计数**（用户担心攒不出大招的量）＝ 开关 `hellwarActGain`（**默认关**）
     + 档位 `hellwarActGainAmount`（1 / **2（默认）** / 3）。落点 = **遗物的原版钩子 `AfterActEntered`**（零新增 Harmony 目标）
     + 发放点补一次；**幂等靠存档字段 `LastRewardedActIndex`**（进局与进第 1 幕会两次经过同一条路、读档还会重进当前幕）。
  3. 【分裂精神】扣血档位**加「不扣血（0%）」** ⇒ 四档循环 0% → 10% → **15%（默认）** → 20%（D13 口径同步更新）。
  4. 【地狱耳环】**风味文本**换成用户给的候选之一（「达摩经记载，当战纹现于强者面上…可能这就是地狱开始的时候了。」）。
  - 落档：design doc **§2.3（R18~R21）+ §8 Phase 1 关单记录**；门禁：构建 0 警告 0 错误（295 .cs）、单测 **1004 全绿**、
    `preflight -Deploy` **G1 10 PASS / G2 STALE=0 / G5 PASS**，部署位 marker **`2026-10-09-r241`**（字节一致，sha256 `d00c0b17fc3a…`）。
  - **r241 实机判读（2026-10-09）**：用户「**每幕自动获得计数没问题**」；其余三项未报问题；
    **滚动条翻车**（"最左什么都没有、1/3 处才见第一个选项、文字看不全、点不到"）⇒ 见上面第 1 条的 r242 修复。
  - **r242（滚动条修复，marker `2026-10-09-r242`）**：构建 0 警告 0 错误（296 .cs）、单测 **1016 全绿**、
    `preflight -Deploy` **G1 10 PASS / G2 STALE=0 / G5 PASS**（字节一致，sha256 `a1bc424d3031…`）。
    复测抓手：`[休息区滚动] …需要宽=2328, 窗口宽=1872, 屏幕可用=1872, buttons=7, nodes=7, separator=100`
    （**必须是 2328/7，而不是 r241 的 7183/21**）；**滚到最左就是第一个选项**、文字完整、点得到；
    选完选项后剩 6 个应打 `滚动内容宽已更新: 2328 → 2082`；**选项 ≤5 个的房间完全不动原版**。
  - **✅ r243 实机关单（2026-10-09，marker `2026-10-09-r243`）**：用户「**现在没问题了，一切都变得好正常**」+
    补测「**火堆只有 5 个选项时不出滚动条，符合设计**」。日志 `logs-archive/godot__20261009-195254__r243.log` 逐条对上：
    `[休息区滚动] … 需要宽=2328, 窗口宽=1872, buttons=7, **名字带=48**, **可点性已修=21**, separator=100` +
    `滚动内容宽已更新: 2328 → 1981, buttons=6, nodes=6`（自愈生效，算式精确）+ `【分裂精神】不扣血（档位=不扣血(0%)）`
    → `已完成: 计数=5→6` + `免费额外选项生效`；`[ERROR]` 仅 1 条（第三方）、异常族全 0。
    ⇒ **Phase 1 + r241 四项追加全部关单**；已知小瑕疵：`可点性已修` 的计数含"待销毁的旧按钮"（21 = 7×3），纯日志美观问题。
- **✅ Phase 2 召唤闭环 —— 实机关单（2026-10-09，r246；分支 `feat/hellwar-god-phase2`）**：
  - **判读（`logs-archive/godot__20261009-204838__r246.log`，用户「测了一下，应该没问题」）**：
    一场战斗三个战神 = `seat=4096/4097/4098`（**同号已修**）；`③b 战神已补登「本回合已结束回合」` ×3；
    `①b … 地图点统计=True` ×4 / `已从 run 卡牌表除名: … 地图点统计摘除=1` ×4；
    **`Player with ID … not found in player stats` 0 条 + `怪物招式 … 执行失败（已兜住）` 0 条**（"不会被打"已修），
    敌人照常行动 4 次；`已从战斗移除战神` + `临时席位已注销: 移除=3` + `计数已自动补满: 补回=3`；
    异常族全 0（`[ERROR]` 仅 2 条：第三方 Manosaba 分支告警 + 见 §待查-1 的宝藏房手牌图像重复添加，均非本功能）。
  以下为实现记录（r244 落地 + r245/r246 两轮实机修复）：
  - **① 低层链抽成工厂** `HellwarWarGodFactory`（POC 与正式召唤共用：生成 / 接入 run / 战斗态 / 立绘 / 清理）；
    席位号从 `0x1000` 起取**第一个空闲**（Phase 2b 一次召唤多个会有空洞）。
  - **② `HellwarSummonCombatRuntime`**：真人席位**回合开始**弹召唤选择（复用 `LocalWakuuChoiceOverlay` +
    压制瓦库自动作答 + 钉住归属者两道）→ **消耗计数** → 生成完整战神（D7「普通档」= 召唤者角色自身的初始遗物 + 初始卡组）；
    挂点仍是既有补丁 ⇒ **不新增 Harmony 目标**。
  - **③ R3 口径 A 落地**：`SpiritCount` = 累计**上限**（存档、只增），新增**不存档**的 `CombatSpentSpirit`（本场已花）；
    显示/判据一律用 `AvailableSpirit`；**先扣后生成、生成失败退回**；战后（发奖之前）摘战神 + 已花清零 = **自动补满**。
  - **④ 门禁**：构建 0 警告 0 错误（298 .cs）、单测 **1047 全绿**、`preflight -Deploy` **G1 10 PASS / G2 STALE=0 / G5 PASS**，
    部署位 marker **`2026-10-09-r246`**（字节一致）。
  - **两轮实机修复**：r244 首测 = 「召唤战神后**不能结束回合**」⇒ r245 补**第二道就绪门**
    （`SetReadyToEndTurn`，见 references 坑 **AE-3**）；r245 复测 = 「**第二个战神不会被打**」⇒ r246 修两处
    （① 空闲号分配拿范围谓词当占用判断 ⇒ 两个战神同 NetId；② 受击结算查 `MapPointHistoryEntry.PlayerStats`
    查不到就抛 ⇒ 那次攻击整体失败 ⇒ 战神看起来免伤；见坑 **AE-4a / AE-4b**）。
  - **复测抓手**：`本回合不弹召唤选择: … reason=…` / `回合开始可召唤，弹出选择: …` / `已召唤战神: … 花费=…` /
    `已消耗计数召唤…` / `③b 战神已补登「本回合已结束回合」` / `①b … 地图点统计=True` /
    `战神已自动结束回合` / `已从战斗移除战神` / `已从 run 卡牌表除名: … 地图点统计摘除=1` / `计数已自动补满`；
    **两次召唤必须是两个号**（`seat=4096` 然后 `seat=4097`）。
  - ⚠ 本阶段战神仍**不出牌**（托管出牌是 Phase 3），它的回合由 `TryRequestWarGodTurnEnd` 自动收口（同 POC）。
  - 通用教训 → `references/local-multicontrol-pitfalls.md` 坑 **AE-1 / AE-2 / AE-3 / AE-4**。
- **✅ Phase 2b（D7 预设档 + 两个大招）实机关单（2026-10-10，r247 落地 → r248 修订 → r248 判读全绿）**：
  **R16【无尽战神】** = 消耗大量计数、**随机召唤战神直到玩家栏位填满**；**R17【终极九连环】** = 消耗真的很多计数、**召唤九个先古强度的战神**。
  - **✅ r248 实机判读（2026-10-10，`logs-archive/godot__20261010-131130__r248.log`，用户「都测过了，没问题」）**：
    `已开【无尽战神】: 花费=8（本场预算）, 预计=10 实际=10 个, 档位=[Rare,Rare,Epic,Common,Epic,Rare,Epic,Epic,Common,Rare], 战斗内玩家=12/12`
    ⇒ **补满 12 席 + 每补位各掷一次随机取档（先古没进池）**；10 个战神 seat `4096…4105` 无重复；
    `已开【终极九连环】: 永久花费=12, 上限=12→0, 预计=9 实际=9 个先古` ⇒ **门槛 12 / 固定 9 个 / 满编扣 12**；
    战后 `移除=10` + `计数已自动补满: 补回=8, 可用=12`；战神回合自动收口 38 次；**异常族全 0**
    （`[ERROR]` 5 条全是既有非本功能：第三方 Manosaba + `§待查-2` 的删存档失败）。
    ⇒ **Phase 2b 关单**；另记 `§待查-3`（战神被打无音效，先跑探针定性）。
  - **r247 落地内容**：**D7 预设档补齐**（`PureLogic/HellwarWarGodPresets`：罕见=静默猎手 / 稀有=铁甲战士 /
    先古=摄政王，三档**卡组全升级**、先古 **+25 血** ⇒ 四档都能单点召唤 1/2/3/4）；
    **【无尽战神】** 整包 8（本场预算）· 每个补位各掷一次随机取 普通/罕见/稀有 · 补满 12 席 · 每场 1 次；
    **【终极九连环】** 门槛 **12（整包价）**· **固定召唤 9 个**（只有栏位不足才截断）· **消耗按实际召唤数折算 `round(12×实际/9)`**（2026-10-09 **三轮修订 r248**，取消原"部分成功"）· 每场 1 次；
    两个大招都进同一个召唤选择 UI（文案带"实际召唤几个"与"永久消耗"）；扣费"先扣后生成、全失败全额退回"（少生按实际数量退差价）。
    门禁：构建 0 警告 0 错误（300 .cs）、单测 **1092 全绿**、部署位 marker **`2026-10-09-r248`**。
    ⚠ 战神**仍不出牌**（Phase 3 才托管）⇒ 档位强度体感要等 Phase 3；现在先古档已生效的部分 = 更肉 + 更占敌人攻击次数。
  - **口径**：design doc **§2.2**；**成本依据 = "计数曲线"**（§7.2 上方块：每幕自动 +2（档位 1~3）+ **火堆【分裂精神】每幕 1~3 个（典型 2~3）**
    ⇒ **整轮 9~15、典型 12~15**；火堆数由用户口径给出，源码侧面 = 每幕地图生成 6~7 个休息处节点、只走路径上的 1~3 个）；
    **拍板结论 = §7.3 + §7.3.1（2026-10-09 二轮修订）**：
    **整包价【无尽战神】8**（从**每场预算**扣、战后补满）/ **【终极九连环】12 但扣"计数上限"本身（永久、不补满）+ 部分成功**
    （每 3 点永久消耗 1 个先古、至少 1 个）—— 用户口径「九连环明显太强、比无尽战神远强，估计开出来就赢
    ⇒ 改成永久消耗计数（上限），这样玩家只会在最后一场战斗开这个」；
    触发入口 = 召唤选择 UI 的两个额外选项；**每场各限 1 次**；无尽随机取 普通/罕见/稀有（先古不进随机池）；
    超 12 席**截断到剩余栏位**；【九连环】**依赖 D7「先古」档（必须先做）**。
  - 两者都超出 D6「每回合最多 1 个」的节奏 ⇒ 分期排在 **Phase 2 召唤闭环之后（Phase 2b）**。
  - 预判代价：ID 段要 ≥12 位、"已满 12"判定要含战神、12 席同回合出牌的性能与数值平衡压力集中在此。
- **① 手工预设卡组 —— ✅ 实机关单（r254，2026-10-10；判读全绿，`logs-archive/godot__20261010-145141__r254.log`）**：战神卡组不再"原版角色初始卡组 + 全升级"，
  改成**手工挑的列表**（用户 2026-10-10 拍板："手工 id 列表 + 开放到配置文件"，自己"想不出好卡组"）：
  - **内置默认**（`Scripts/Runtime/HellwarWarGodDecks.cs`）：三个高档各 14 张，用 `ModelDb.Card<T>()`
    **类型引用**写死（编译期可校验，不会像字符串 id 那样拼错静默失效）；挑卡口径 = **只挑托管 AI 好驾驭的线性牌**
    （罕见=毒+格挡+敏捷 / 稀有=易伤+力量+大格挡 / 先古=攒星+花星）；**不高档不替换**（普通档仍用召唤者自身角色初始卡组
    ⇒ 保住"自动兼容 mod 角色"）。
  - **配置覆盖**（`hellwarDeckCommon/Rare/Epic/Ancient`，默认空 = 用内置默认）：逗号/分号/空白分隔的
    `ModelId.Entry`（`CARD.` 前缀可省、大小写不敏感），末尾 `+` = 这一张单独升级，写几遍就是几张；
    认不出的 id **跳过并打 WARN**，一条都没认出来回落角色初始卡组。解析层 = 纯逻辑
    `PureLogic/HellwarWarGodDeckPlan`（+14 条单测）。
  - **挂点**：`HellwarWarGodFactory.ApplyPresetDeck`（在 `AttachToRun` / `PopulateCombatState` **之前**替换卡组 ——
    那两步会分别补 `Owner`/登记 run 卡牌表、把卡组克隆进抽牌堆）；启动自检锚点
    `[地狱战神] 预设卡组已就绪: 档位=…, 内容=…`（**同时是给玩家的配置模板**）。
  - **复测契约**：召唤战神 ⇒ 日志 `①a 预设卡组已替换: … 来源=内置默认/配置文件, 张数=14`；
    手牌里应是**手工那 14 张**（不再有原版基础 Strike/Defend 堆）；改一个 `hellwarDeck*` 键 ⇒
    下局召唤应打 `来源=配置文件` 且内容跟着变；普通档不写配置时应打"不替换"（日志无 `①a`）。
  - **✅ 实机判读（2026-10-10，r254 全绿）**：三档自检 `预设卡组已就绪` ×3 内容逐字一致；
    `①a 预设卡组已替换` 只在 Rare/Epic 出现（**普通档确实不替换**：`LEX_NINJA2…卡组=10 张 / 升级 0 张`），
    Rare=`SILENT` 14 张全升级、Epic=`IRONCLAD` 14 张全升级，`②b 抽牌堆=10/14/14 张`；
    Epic 战神**真在用新牌出牌**（`POMMEL_STRIKE` / `TWIN_STRIKE` / `SHRUG_IT_OFF`）；
    异常族全 0、`[ERROR]` 仅 1 条（第三方分支告警）、无「认不出卡牌」WARN。
    顺带发现并登记 `§待查-4`（同场多战神时后面那个可能因 inline 抢闸门串行而整场没出牌 ⇒ 待定性）。
- **② 预设遗物（A 方案）—— ✅ 实机关单（r255，2026-10-10；判读全绿，`logs-archive/godot__20261010-182625__r255.log`）**：给战神补"**不依赖开战时机**"的遗物
  （战神是战斗中途中加进来的，原版 `BeforeCombatStart` / `AfterRoomEntered(CombatRoom)` / `TurnNumber<=1`
  那批钩子**早跑完了** ⇒ 补挂只显示不动，坑 **AE-5b**）：
  - **内容表** `Scripts/Runtime/HellwarWarGodRelics.cs`（typed `ModelDb.Relic<T>()`，编译期可校验）：
    罕见 **1 件**（`Orichalcum` 回合结束无格挡 ⇒ 6 格挡）/ 稀有 **2 件**（`MiniatureCannon` **已升级攻击牌 +3 伤害**
    + `SelfFormingClay` 受未格挡伤害 ⇒ 下回合 +3 格挡）/ 先古 **3 件**（`PenNib` 每 10 张攻击下一击翻倍 +
    `Shuriken` 每回合 3 张攻击 +1 力量 + `TungstenRod` 每次生命损失 −1）；**普通档 0 件**（只有它自己角色的初始遗物）。
    ⚠ `MiniatureCannon` 与"预设卡组**全升级**"是**强协同**；刻意排除有副作用的（如 `Brimstone` 每回合 +2 力量
    **同时给敌人 +1 力量**）。
  - **件数阶梯 = 成本 − 1**：`HellwarSummonPolicy.ResolvePresetRelicCount`（纯逻辑、有单测）是单一事实源，
    启动自检拿内容表实际件数与之**对账**（不符打 WARN）。
  - **发放口径**：**必须 `player.AddRelicInternal(...)`，绝不用 `RelicCmd.Obtain`**（后者会写
    `runState.CurrentMapPointHistoryEntry.GetEntry(NetId)` ⇒ 不进 `runState.Players` 的战神**直接抛**，同族坑 AE-4b）；
    时机 = **`AttachToRun` 之后**（`RunState.TotalFloor` 才可读）；"获得时"钩子 `AfterObtained()`
    由生成收尾阶段补调（`AddRelicInternal` **不**替我们调）。
  - **预期现象**：**战神的遗物不显示在界面上**（遗物槽只画 `LocalContext.IsMe` 那一席）—— 效果不看 UI，属正常。
  - **锚点**：启动自检 `[地狱战神] 预设遗物已就绪: 档位=…, N 件: …`；召唤时
    `①c 预设遗物已发放: seat=…, 档位=…, 本次=N 件[…]`。
  - **复测契约**：召唤罕见档 ⇒ `①c … 本次=1 件[ORICHALCUM]`，该档战神的遗物总数 = 1（角色初始）+ 1 = 2；
    稀有档 `本次=2 件[…]`、先古档 `本次=3 件[…]`、普通档**不出现** `①c`；
    打一场能让先古战神攻击≥3 次的战斗 ⇒ 它身上应出现【力量】层数（`Shuriken` 生效的间接证据）。
  - **✅ 实机判读（2026-10-10，r255 全绿）**：启动三行自检逐条对上（`Rare 1 件: ORICHALCUM` /
    `Epic 2 件: MINIATURE_CANNON, SELF_FORMING_CLAY` / `Ancient 3 件: PEN_NIB, SHURIKEN, TUNGSTEN_ROD`，与阶梯对账无 WARN）；
    罕见档 `①c 预设遗物已发放: seat=4096, 本次=1 件[ORICHALCUM], 该席位遗物总数=2`（账对得上）；
    **普通档不出现 `①c`**（且不换卡组：`卡组=10 张, 升级了=0 张`）；罕见档战神真在用预设卡组出牌
    （`NOXIOUS_FUMES / AFTERIMAGE / DEADLY_POISON / NEUTRALIZE / POISONED_STAB / PREDATOR`）；
    预设遗物相关的"发放失败 / 跳过 / 异常 / 件数与阶梯不符"**全 0**、异常族全 0、`[ERROR]` 仅 1 条（第三方分支告警）。
    ⚠ **本场只召了罕见 + 普通档** ⇒ 稀有 / 先古档的**效果体感**仍待后续实机（内容表解析已由自检覆盖）。
- **⏳【待排期·用户 2026-10-11 点名要做】预设战神 v2：同档位多套「角色预设」+ 召唤时选档再选角色**：
  - **需求原话**：「后面记得要做预设战神，以及同一个档位要有不同的预设战神；其中**普通档要有所有角色**
    （初始卡组 + 初始遗物），并且**玩家选择哪个档可以选自己想要的角色预设**」。
  - **与现状的差距**：现状（r254 ① 手工预设卡组 / r255 ② 预设遗物）= **四档各一套固定内容**
    （普通档 = 召唤者的角色皮 + 强度档 10 张；罕见/稀有/先古 = 预设表里的原版角色皮 + 阶梯遗物 1/2/3 件）。
    目标是把它变成**「档位 × 角色预设」二维**：每个档位下**多套**可选角色预设，召唤时两步选择。
  - **✅ 5 条已拍板（用户 2026-10-11）**：① 罕见/稀有/先古每档各要哪几套预设 = **等用户手搓给表**（本项**等输入**）；
    ② **可** —— 普通档 = 读 `CharacterModel` 的初始牌组 + 初始遗物（不再用"召唤者角色皮 + 手工 10 张"）；
    ③ **可** —— 保留一个「自动 / 随机」档；④ **UI 由我定**（选：在现有 `LocalWakuuChoiceOverlay` 选档弹层之后
    追加第二层"选角色预设"，不新造）;⑤ **可** —— 配置键从 `hellwarDeck*`（四档）扩成"档位.角色"多维键。
  - **要点**：预设内容表继续走「内置默认用 `ModelDb` 类型引用 + 字符串 id 只留给配置文件」（AE-11 口径），
    并保留启动自检（件数与阶梯对账）。
  - **✅ Phase A（r269）已落地**：`hellwar_presets.json` 档位 × 角色预设二维的解析 / 归一 / 继承映射 + 单点召唤用默认预设。
  - **✅ Phase B-1 / B-2（r273 落地 → r274 修 3 处 → 2026-10-11 三局实机关单）= 「选档 → 选预设」两层 UI + 随机池接文件池**：
    - **第二层弹层**（单点召唤选定档位后）：行 = `[自动]` →（具体候选 > 1 时）`[随机]` → 各具体预设；
      **具体候选 ≤ 1 条不弹**（直接用它）；在第二层取消 ⇒ **不扣计数、整次召唤不执行**。
    - **候选来源**（`HellwarPresetCatalog`）：普通档 = **所有原版角色**（皮肤 = 该角色、卡组 = 不替换 ⇒ 该角色初始卡组、
      不额外加遗物 ⇒ 该角色初始遗物）；罕见/稀有/先古 = 该档**内置默认** + 文件里挂该档的预设。
    - **随机池**：【无尽战神】= 普通/罕见/稀有（可选含先古）各档的「内置默认 ∪ 文件预设」；【九连环】= 先古档的同款并集。
      **没有文件预设时与旧行为逐条一致**（零回归）。
    - **语义订正**：预设 `deck` **显式空串 = 不替换**（原先会一路落到"该档内置卡组"，对角色预设是错的）。
    - **启动自检**：`[地狱战神] 预设候选已就绪: 档位=…, N 套[…]（召唤时弹第二层…）`
      （⚠ r273 实机踩到：它原先挂在 ③ 预设表自检的提前 `return` 下游 ⇒ **没写文件时永远打不出来**，r274 已修）。
    - **✅ r273 实机判读（用户「测了一下，应该没问题」）**：**B-1 契约全中** —— 普通档两次弹第二层（`候选=23 套`）、
      一次**取消**（不扣计数）、一次选 **mod 角色古明地恋**（`character=CHARACTER.KOISHI-KOISHI`，
      **初始遗物 1 件 + 初始卡组 10 张**都正确带上、立绘节点建成）；罕见/稀有/先古 4 次召唤**都没弹第二层**（零回归）；
      异常族全 0。⇒ **r269 起那个"mod 角色待摸注册机制"的未知项已事实上打通**
      （`ModelDb.AllCharacters` 实机含 mod 角色，共 23 个）⚠ 但可能混着 `IsPlayable=false` 的占位角色
      ⇒ **r274 起按 `IsPlayable` 过滤**（r274 实机自检 23 → 23 ⇒ 表里没有占位角色，这一层是空转的安全网）。
    - **🆕 r274 实机判读（用户「无尽战神和终极九连环都测了」）**：**两个大招的池口径全中** ——
      `【无尽战神】随机池=不含先古(3 套)`（补满 12 席、每补位各掷一次）、`【九连环】池=1 套`（固定 9 个先古、永久扣上限 12）；
      正对"**没写预设文件时与内置档位表逐条一致**"的零回归口径。
      ✅ **"内置 ∪ 文件预设"并集已实测通过**（2026-10-11：写入 demo `hellwar_presets.json`（4 套）后重测 ⇒
      `随机池=6 套` / `九连环池=2 套`、**四档都弹第二层**、`①a … 来源=预设(罕见·毒刺/先古·影)` ×6
      ⇒ **文件预设真的被抽进了两个大招的随机池**；`认不出`=0 / 异常族全 0）。
      ✅ **最后一条也验完（session C）**：单点【稀有】档选 `epic-custom` ⇒ `①a 预设卡组不替换` + `hp=90/90` +
      `character=CHARACTER.DEFECT, 卡组=10 张, 升级了=10 张` + **无 `①c`** ⇒ `deck:""`（不替换）/ `relics:[]`（不加）/
      `upgradeDeck` / `bonusHp` 四个字段语义全对 ⇒ **部署位 r274 上的 Phase B（B-1 + B-2）全线关单**。
      另：三局都以"退出 run"结束 ⇒ **发奖前的战后清理**这条路径仍未走到（不是缺陷，退局复位按 r231 口径兜住）；
      session B `战神已自动结束回合` 9 次 ⇒ 托管出牌正常。
      观察：session A 10 掷没出 Epic（≈1.7% 小概率；session B 分布正常 4C/2R/4E；`Rng.NextInt(maxExclusive)` 已确认是 `[0,n)` 均匀）⇒ 判为运气。
    - ✅ 拍板① 要的表**已到货并落地（r279，2026-10-15）**：用户用「战神构筑器」搓的 **18 套**
      （罕见 5 / 稀有 5 / 先古 8）已作为**内置预设**随 mod 分发（`Scripts/Data/hellwar_presets.json`，见本文件末尾 §r279）
      —— **不用再改代码**；想让某套当"自动默认"，在编辑器里勾「默认」再覆盖那份 json 即可。
  - **✅ Phase B-3（r275 落地 → 2026-10-12 实机导出成功 → r276 修 3 处口径 → 用户再导一次复测全中 ⇒ **关单**）
    = 游戏内导出 `content_catalog.json`**（编辑器的数据源）：设置页（战灵召唤段）新增一行「导出内容目录（给预设构筑器用）」
    = 本页唯一**一次性动作按钮**（点一下导出，不持久化状态），产物 `%APPDATA%\SlayTheSpire2\content_catalog.json`
    （与 `hellwar_presets.json` 同目录，`.tmp` + Move 原子写）。
    - **首份实机基线（本机 26 个来源 / v0.111.0）**：`卡=2964(其中 mod 2368) / 遗物=1555(其中 mod 1256) /
      角色=23(其中 mod 18)`、`缺名或描述=0` ⇒ 枚举层与文本层全对；角色 23 个**全 `isPlayable`**、
      初始卡组 8~12 张 + 初始遗物 1~4 件（**含 mod 角色**）。
    - **结构**：`cards` / `relics` / `characters` 三段，每条都带 **`Source`**（提供它的程序集简单名；
      `base` = 游戏本体）⇒ 第三方 mod 内容天然在内且能分组。
      卡 = id / entry / 名称 / 描述（`GetDescriptionForPile` 展开动态变量与关键词）/ 稀有度 / 类型 /
      **费用（含 X 费与星费）** / `Pool`（`Id.Entry`，如 `SILENT_CARD_POOL`）+ `PoolTitle`（显示名）/
      `Portrait` + `PortraitPng` + `HasPortrait`；
      遗物 = id / entry / 名称 / 描述 / 稀有度 / 遗物池 / `Icon` + `BigIcon` + `HasIcon` / `HasBigIcon`；
      角色 = id / entry / 名称 / `IsPlayable` / **初始卡组 entry 列表（保留重复）** / 初始遗物 entry 列表。
    - ⚠ **图片字段的准确口径（r276 修正后）**：`Portrait` = 游戏自己给的路径（**权威**，三种形态：`.tres`
      图集条目 / `.png` / `.jpg`，反斜杠已规范成 `/`）；`PortraitPng` = **只在能确定时给**（本身是图 ⇒ 原样；
      是图集 `.tres` ⇒ 从该路径换算成 `packed/card_portraits/…png` 并复核存在性；否则留空）；
      **`HasPortrait` 只对没覆写路径的卡准确**（实机 322 张 mod 卡有图却为 `false`）⇒ **编辑器判"有没有图"以
      `Portrait` 为准**。遗物 `BigIcon` 同理（只在文件存在时给；实机 1555 件仅 306 命中）。
      `GameVersion` 取游戏自己的 `ReleaseInfoManager`（实机 `v0.111.0`）。
    - **口径**：枚举 `ModelDb.AllCards` / `AllRelics` / `AllCharacters` 并**显式并上 `Event*Pool`**（防漏收
      mod 内容）+ 按 `ModelId` 去重 + 按 id 排序（便于 diff）；**`ModelDb.Preload` 之前点按钮 ⇒ 只排队**，
      就绪那一刻自动补做（时序坑见 skill `references/local-multicontrol-pitfalls.md` 坑 AA 续-2）。
    - **记账**：日志锚点 `[内容目录]` 打条数（含"其中 mod"）与"缺名或描述"条数（第三方卡缺本地化键时点名）。
    - **⚠ 图片来源的实际形态（2026-10-12 查证，纠正原计划）**：玩家侧 **pck 里没有散装图片** ——
      只有 `<图源>.png.import`（~200 B 侧车，内含 `[remap] path="res://.godot/imported/<名>-<md5>.ctex"`）
      与 `.ctex`（主 pck 3555 个：**2311 个 WebP 容器 / 704 个 `.bptc`(BC7) / 540 个 `.s3tc`**）。
      **单卡整图 `packed/card_portraits/…` 是 WebP（好解）**，而 **卡图集 `card_atlas_0/1.png` 与
      `images/relics/*.png` 是 BC7**。mod 资源在**各自工坊 pck**（`steamapps\workshop\content\2868840\<itemid>\<Name>.pck`，
      挂载为 `res://<Name>/…`；本地 mod 在 `%APPDATA%\SlayTheSpire2\mods\…`），形态相同。
      ⇒ 编辑器提图：**优先 `PortraitPng`（WebP）**，需要图集裁剪时再加 BC7 解码器。
    - **下一步 = 编辑器本体**（独立 WPF / .NET 8，复用 `D:\Download\pain\Sts2ModManager` 骨架：
      读本文件 + 首次运行指向游戏目录、按需提取图片缓存到 `%APPDATA%`）。
    - **✅ Phase C-1~C-3 编辑器本体首版已落地（2026-10-12，新目录 `D:\Download\pain\WarGodForge\`）**：
      独立解决方案（`WarGodForge.sln`：Core 纯逻辑 + WPF App + xUnit 测试 + `Scripts\build.ps1` 门禁，
      骨架照抄 ModKit；**零第三方依赖**）。落地内容 —— ① **Core**：`content_catalog.json` 读取 + 索引（按 entry 查卡 /
      遗物 / 角色 / 附魔，来源 / 稀有度 / 类型筛选）、卡组文本解析与格式化（与 `HellwarWarGodDeckPlan` 逐条对齐）、
      `hellwar_presets.json` 读写（camelCase / 枚举写成字符串 / null 省略 = 继承）、**预设校验**
      （引用了目录里不存在的卡 / 遗物 / 附魔 / 角色、附魔与卡类型不匹配、同档多默认、id 重复 ⇒ 逐条点名）；
      ② **App**：三栏界面（预设列表 + 校验 / 四页签目录浏览 / 当前预设编辑器），卡组逐行可勾升级、挂附魔（下拉 + 数量）、
      上下移、删除，也可**粘贴卡组文本**追加；一键「为所有角色生成普通档」（用目录里各角色的初始卡组 + 初始遗物）；
      ③ **导入导出** + 隐藏只读自检 `--selftest`（打印并落盘 `%APPDATA%\WarGodForge\selftest-report.txt`）。
      **本机实机自检全绿**：目录 2964 卡 / 1555 遗物 / 23 角色 / 87 附魔（28 个来源）、现有 4 套预设逐字段解析正确、
      校验正确点出「SHARP 不能给 Skill 类技能牌 DEADLY_POISON」、序列化往返一致。
    - **⛔ Phase C-4（编辑器图片）= 不做（2026-10-12 用户拍板：「图片不做了没必要」）**：
      目录里其实已带图路径（卡 `Portrait` / `PortraitPng`、遗物 `Icon` / `BigIcon`），本来只差"按路径取像素" ——
      要给编辑器加**只读 pck 提取器**（`.png.import` 侧车 → `.ctex`；单卡整图 WebP 可直读，卡图集 / 遗物图 BC7 需解码）
      再缓存到 `%APPDATA%`，成本与收益不成正比 ⇒ **关单**。文本信息（名称 / 描述 / 费用 / 类型 / 稀有度 / entry / 来源）
      已足够挑卡。小尾巴（费用曲线统计）一并**不做**。
  - **✅ 预设支持「附魔」（r277 落地 → 2026-10-12 实机关单）**（让预设里的某张牌自带某个附魔）：
    - **三条口径（用户 2026-10-12 说「随便你」⇒ 我们拍的）**：① **语法** = 条目末尾 `[SHARP]` / `[SHARP:3]`
      （不写数量 = **1**；`ENCHANTMENT.` / `ENCHANT.` 前缀可有可无；`+` 与 `[附魔]` **顺序随便**）；
      ② **范围** = 战神预设 **和** `hellwarDeck*` 四档配置键**一起**（同一个解析器，等于免费）；
      ③ **内容目录同轮加第四段 `Enchantments`**。
    - **落地**：`HellwarWarGodDeckPlan`（词法，`Format` 同步输出 ⇒ 日志模板可复制）+
      `HellwarWarGodDecks.LookupEnchantmentByEntry`（查表）+ `HellwarWarGodFactory.ApplyPresetEnchantment`
      （施加点 = **`Deck.AddInternal` 之前**，`Pile == null` ⇒ 绕开"按 Owner 记账"那段，战神此刻 Owner 还是 null）；
      校验走游戏自己的 `CanEnchant(card)`，认不出 / 不能带 / 抛异常 ⇒ **只跳过那个附魔、卡照给**。
    - **锚点**：启动 `附魔可选项: N 个[...]`；召唤 `①d 预设附魔: 施加=N 跳过=M 明细=[…]`；预设表那行加 `带附魔=N 张`。
    - 📌 **编辑器口径**（2026-10-12 实机导出发现）：附魔 `description` 里的**数量占位显示 `0`**
      （如 `锋利` = "伤害值增加[blue]0[/blue]点"）—— canonical 附魔的 `Amount` 本来就是 0（游戏靠调用方传数量）
      ⇒ 编辑器要用 `showAmount` + 自己的数量输入替换那个 0，**别当真值显示**。
    - **⏳ 仍未观察的一条（2026-10-12 实机里那张 `DASH[TEZCATARAS_EMBER]` 没进手牌 / 没被打出来）**：
      改费用 / 改值的附魔（`TEZCATARAS_EMBER` / `SHARP`）会不会让**托管大脑**判断分裂
      （大脑读 `EnergyCost.Canonical` / `DynamicVars.BaseValue`，而附魔后可见值是 `EnchantedValue`）——
      Sharp 那次证明"附魔不影响托管出牌链"，**改费用那条还等一次自然遇到**。
  - **📌 附魔支持的**可行性依据（2026-10-12 侦察原稿，保留备查）**：
    - **可行性 = 高**（2026-10-12 code-explorer 侦察）：游戏有公开入口
      `CardCmd.Enchant(EnchantmentModel enchantment, CardModel card, decimal amount)`（`CardCmd.cs:534`，
      内部 = `card.EnchantInternal(...)` + `enchantment.ModifyCard()` + `FinalizeUpgradeInternal()`），
      附魔**随卡存档**（`SerializableCard.Enchantment`），`UpgradeInternal` 会回调 `Enchantment?.ModifyCard()`；
      可施加性有现成判据 `EnchantmentModel.CanEnchant(card)`（默认排除 状态/诅咒/任务牌，个别附魔更严如 Sharp 只给攻击牌）。
      全量枚举**只有** `ModelDb.DebugEnchantments`（含 mock ⇒ 按 `IsMock` 过滤；**也含 mod 附魔**）。
      mod 侧现状：**只有"选哪张牌附魔"的决策**（`LocalWakuuEnchantPicker` / `WakuuEnchantRules`，
      按**类型名字符串**查规则），**没有**"id → `EnchantmentModel`"查表、也没有施加动作 ⇒ 这两块要新写。
    - **（r277 已按上面那三条口径实装**：词法扩展 + 施加点放在 `Deck.AddInternal` 之前 + 目录第四段。）
- **✅ r278（2026-10-12 落地 → 2026-10-13 实机自检通过）—— ① 预设可随 mod 分发（= 嵌入资源）② 引用了本机没有内容的预设整条跳过**：
  - **起因（用户原话）**：「我写了预设后作为 mod 自带预设，如果里面是 mod 角色 / mod 卡牌，可以直接跳过这个预设加载其它战神吗」
    ⇒ 拍板"随便你" ⇒ 两条都做。
  - **① mod 自带预设** = `Scripts/Data/hellwar_presets.json`（**编译进 dll 的嵌入资源**，csproj `EmbeddedResource` +
    固定 `LogicalName`；**不选"文件放 dll 旁边"** —— 部署与工坊上传脚本只拷 dll，外部文件会静默漏掉）。
    加载 = **玩家文件 ∪ 内置**：同 `id` **玩家胜出（内置那条整条丢弃，不改名）**、**玩家在前**（⇒ 同档多个 `default`
    时玩家的先生效）；哪一份坏就只用另一份。纯逻辑 `PureLogic/HellwarPresetMerge`（6 条单测）。
    改它要**重新构建**才生效。（**r279 起这份不再是空模板** —— 已填 18 套定稿，见本文件末尾 §r279。）
  - **② 可用性跳过** = 角色皮 / 卡组**每一张** / 遗物**每一件**，任一在本机没有 ⇒ **该预设整条剔除**
    （不进「选角色预设」候选、【无尽战神】/【九连环】随机池，也不当"自动默认"），其余预设照常；
    打一条 `预设已跳过（引用了本机没有的内容，其余预设照常）…`（**同条只报一次**）。
    **附魔缺失不算**（只跳那个附魔，卡照给）；**退化文本（全是分隔符）不算**（归归一的 WARN 管）。
    纯逻辑 `PureLogic/HellwarPresetAvailability`（10 条单测）+ 运行时 `HellwarPresetCompat`
    （⚠ **探测器失败按"全部可用"处理** —— 绝不能因为探测坏了把玩家预设全隐藏）。
    启动自检那行改口径：**打"实际会用的那套"**（`实际使用="…"(id=…)`，与召唤同一个入口 `ResolveDefault`），
    被跳过的再附一句 ⚠（原来打的是"配置里写的默认"，会出现"日志说 A、实际用 B"的判读陷阱）。
  - **③ 顺带**：`ResolveSkin` 那句"mod 角色暂未接入"的注释已过时（r275 导出的目录里 18 个 mod 角色都来自
    `ModelDb.AllCharacters`，与查表同一条 API）⇒ **按 entry 匹配 mod 角色大概率已可用**，待实机确认。
  - **复测契约（r278）**：① 不填内置 json ⇒ 启动锚点与 r277 逐字一致（零回归）；
    ② 想验跳过：往 `Scripts/Data/hellwar_presets.json` 写一条**故意引用不存在内容**的预设（如 `skin=NO_SUCH_MOD_CHAR`
    或卡组写 `NO_SUCH_CARD`）⇒ 重新构建部署 ⇒ 启动应出现 `预设已跳过（引用了本机没有的内容…）` +
    该档 `实际使用=…` 附 `⚠ 配置的默认预设(… )已跳过`，**召唤照常**（走别的预设 / 内置默认）；
    ③ 想验内置通道：往那份 json 写一条**真**预设 ⇒ 重新构建 ⇒ 玩家文件里没有它时也能在候选里看到。
    ④ 期望 0：我方 `turn loop died` / 异常 / 索引越界。
  - **坑已沉淀**：skill `references/local-multicontrol-pitfalls.md` **AE-31**（嵌入资源分发 + 两条静默失败要单测钉住）
    / **AE-32**（跨机器预设的跳过口径 + 探测器失败开放 + 日志打实际生效 + PureLogic 命名空间不统一）。
  - **✅ 实机判读（2026-10-13 15:04 局，`logs-archive/godot__20261013-1504__r278-selftest.log`；做法 = 往内置 json 塞 3 条
    "故意引用不存在内容"的假预设，名字都带「自检·」）**：
    `内置预设已读取: 条数=3` ✓、`预设表已加载: 套数=7（玩家 4 条 / 内置 3 条）` ✓、
    **三条 `预设已跳过` 各只出现一次**且原因分别点到 `NO_SUCH_MOD_CARD` / `NO_SUCH_MOD_CHARACTER` / `NO_SUCH_MOD_RELIC` ✓、
    `档位=Common 实际使用="（内置默认）"(id=内置…)，⚠ 配置的默认预设(zz-selftest-bad-card-common)已跳过` ✓；
    **反向检查**：四档候选与 r277 完全一致（普通 23 套全部可玩角色，**无任何「自检·…」**；罕见 3 / 稀有 2 / 先古 2）✓；
    `预设可用性探测失败` / `turn loop died` / `### Exception ###` / `预设表问题` **全 0**。⇒ **关单**。
    判读后已**清空内置 json 并重新部署**（正式版 sha256 `3b5C1E88…`，dll 里已无 `zz-selftest-*`）。
  - **实机顺带实证**：普通档候选列出 **23 个角色**（含 蕾忍 / 忍者杀手 / 观者 / 猪 / 沙耶 / 古明地恋 等 **mod 角色**）
    ⇒ **`ModelDb.AllCharacters` 确实含 mod 角色**，`ResolveSkin` 里"mod 角色暂未接入"的注释确已过时（上面 ③ 的判断成立）。
- **✅ r279（2026-10-15 落地 → ⭐ 待实机）—— 内置预设从"空模板"变成 18 套定稿（行为代码零改动）**：
  - **一句话**：用户「预设我搓好了」+ 拍板"设为内置随 mod 分发" ⇒ 把编辑器产出的 `hellwar_presets.json`
    **原文照搬**进 `Scripts/Data/hellwar_presets.json`（11895 B / 无 BOM / 逐字节一致），marker 升 `2026-10-15-r279`。
  - **数据**：**18 套**（罕见 5 / 稀有 5 / 先古 8；普通档**不挂文件预设** —— 那档候选由"全部可玩角色"自动组成）。
    `tiers` 全是 `Rare|Epic|Ancient`、**无一套写 `default`** ⇒ 各档「自动」= **该档第一条文件预设**
    （罕见 `ironclad1` / 稀有 `ironclad2` / 先古 `watchera15`）。**2 套用 `WATCHER-*`**（Watcher mod）
    ⇒ 没装那个 mod 的玩家被 r278 判据**整条跳过**（r278 通道第一次真实使用）。
  - **入库前的校验**：`WarGodForge.exe --selftest` ⇒ 18 套全解析 / **校验无问题** / 序列化往返逐字段一致。
  - **新增单测** `内置预设数据归一后没有问题`（读嵌入资源 → 归一 → 断言无 `Problems`；**不钉条数**，退回空模板也合法）。
  - **⚠ 本机验不了内置通道**（玩家那份与内置**同 id** ⇒ 玩家胜出，合并后仍是 18 套）：要验得把
    `%APPDATA%\SlayTheSpire2\hellwar_presets.json` **临时改名**再启动（看 `玩家 0 条 / 内置 18 条`）—— 坑见 skill **AE-33**。
  - **复测契约**：启动 `内置预设已读取: 条数=18` + `预设表已加载: 套数=18（玩家 18 条 / 内置 18 条）`；
    罕见/稀有/先古各召一次 ⇒ `①a/①c/①d` 三条锚点 + 第二层弹层能看到这些名字；
    期望 0 = `预设已跳过`（本机不该有）/ `预设表问题` / `turn loop died`。
  - 口径写进 `Scripts/Data/README.md`（含"没有 default"与"改完要重新构建"）。
- **✅【已交付·用户 2026-10-12 关单】「预设构筑器」（= 战神构筑器 `D:\Download\pain\WarGodForge\`）**：可视化造战神的程序（选卡/遗物/角色/血量/档位 → 导出给 mod）——mod 侧「预设可配置化」地基（Phase A/B）与编辑器本体（Phase C-1~C-3）均已落地并实机自检全绿；**图片（Phase C-4）用户拍板不做**：
  - **需求原话**：「可能以后需要写一个程序，自动读取杀戮尖塔2反编译的代码或我们自己把所有原版卡牌和遗物记录进去，
    我在这个程序（最好能直接显示卡牌、遗物的图片）里面自己构筑卡牌、遗物、角色、血量、战神档位，然后导入到 mod 里面。
    发布的时候需要有一些预设的战神，并且全靠我念给你你再搓有点慢，写好程序也方便玩家自己搓战神（如果可以，再兼容 mod）。
    当然也可以不是独立程序而是集成到 mod 里面」。
  - **✅ 可行性侦察（2026-10-11，code-explorer 两路）—— 结论：数据/图片/本地化本机已齐，不用手工抄表**：
    - **反编译源码** `d:\Download\pain\sts2src\src\Core\Models\`：卡牌 `Cards\` 约 598 个 `*.cs`（一卡一文件，
      `base(费用, CardType, CardRarity, TargetType)`，id 由类名派生）、遗物 `Relics\` 约 300 个、角色 `Characters\` 8 个；
      离线全量枚举入口 `ModelDb.AllCards`/`AllRelics`（`ModelDb.cs` L114/L261）+ 436KB 的 `AbstractModelSubtypes.cs` 全类型清单。
    - **图片已解包**（不是 pck）：`sts2src\images\`。卡图 = 图集两段式
      （`atlases/card_atlas.sprites/<pool>/<id>.tres` 给 `region`，像素在 `card_atlas_0/1.png`，需按 Rect 裁剪；
      另有 `packed/card_portraits/<pool>/<id>.png` 整图，**覆盖率待量**）；遗物图 = `images\relics\<name>.png` 整图（直接显示）；
      角色图标 `ui/top_panel/character_icon_<id>.png`。判"有没有立绘"的现成判据 = `CardModel.HasPortrait` / `Portrait != null`。
    - **本地化** `sts2src\localization\<lang>\*.json`（16 语言；`zhs/cards.json` 99.6KB、`relics.json`…），
      键 = `<类名大写>.title/.description` ⇒ 中文卡名/描述直接读表，不用我们抄。
    - **pck 备用工具**：`d:\Download\pain\tools\pck_tool.py`（info/list/extract，V1~V3）；实体基线
      `maintenance-docs\game-entities.md`（卡 596 / 遗物 299 / 药水 65…）。
    - **更省事的路（推荐为主）**：mod 里已有现成的运行期枚举先例 `Scripts\Runtime\WakuuDaddyContentProbe.cs`
      （`ModelDb.CardPool<EventCardPool>().AllCardIds` / `RelicPool.AllRelicIds` / `GetId<T>()` / `LocString.Exists` /
      `Portrait != null`，挂在 `ModelDb.Preload` 后）⇒ 照它写一个**「导出内容目录」**的开关，跑一次 dump
      `content_catalog.json`（id / 名称 / 描述 / 稀有度 / 类型 / 费用 / 图片路径 / 是否有立绘 / 来源 mod）——
      **天然含第三方 mod 卡**，正好满足"再兼容 mod"；`sts2src` 离线解析留作兜底（拿不到 mod 卡、解析 C# 也脆）。
    - **编辑器形态候选**：① 独立程序（可复用 `d:\Download\pain\Sts2ModManager` 的 WPF/.NET 8 工程骨架 —— 它现在
      **只**管 mod 清单/开关，没有任何卡牌/图片代码可抄，但工程约定是现成的）；② 集成进 mod 的 Godot UI（改配置要重载、
      复杂 UI 成本高）；③ 本地 HTML/JSON 版（零安装、显示图片最容易、我改起来最快，但"写文件"受浏览器限制）。
    - **⚠ 依赖顺序（关键）**：编辑器能输出什么，取决于 mod **能读什么** —— 现状**只有卡组可配置**
      （`hellwarDeckCommon/Rare/Epic/Ancient`，`WakuuConfigData.cs` L165-174，落盘 `%APPDATA%\SlayTheSpire2\vakuu_autopilot.json`）；
      **遗物 / 皮肤 / 血量 / 升级全是硬编码**（`HellwarWarGodPresets.cs` 的 `record HellwarWarGodPreset(Tier, SkinKey, UpgradeDeck, BonusHp)`
      + `HellwarWarGodRelics.cs` / `HellwarWarGodDecks.cs`；件数阶梯 `HellwarSummonPolicy.ResolvePresetRelicCount = 成本 − 1`），
      且**没有"档位 × 角色预设"的二维表达能力** ⇒ **必须先做"预设可配置化"（= 预设战神 v2 的地基），再造编辑器**，否则输出格式返工。
    - **外部文件读写先例**（编辑器产物如何被 mod 吃）：`personal_stats.json`（`LocalPersonalRecorder.cs`：
      读 `Reload` L46 / 原子写 L445-456，纯逻辑解析在 `WakuuPersonalData.cs`）与 `vakuu_autopilot.json`（`LocalWakuuAutopilotConfig`）
      —— 两套范式都是「纯逻辑 `*Data/*Json` + 运行时 `Local*Config` + `*Policy` 校验」。
  - **✅ 已拍板（2026-10-11 用户三连选）**：① 形态 = **独立程序**（复用 `d:\Download\pain\Sts2ModManager`（ModKit）的
    WPF/.NET 8 工程骨架新建工具）；② 数据源 = **游戏内导出为准**（mod 加"导出内容目录"开关 dump `content_catalog.json`，
    **含第三方 mod 卡**；`sts2src` 离线解析只做兜底/校验）；③ 顺序 = **先做 mod 侧地基**（预设从硬编码改成配置可读、
    档位 × 角色预设二维），再造编辑器 —— 编辑器输出格式一次到位，不返工。
  - **✅ 玩家侧文件口径（用户问"玩家是否要额外下载"，2026-10-11 定）**：
    - **只玩预设战神的玩家 = 零额外下载**（官方预设内置在主 mod：内置默认表编译进 dll + 预设包随 mod 走工坊）；
    - **自搓的玩家 = 只下编辑器本体**（GitHub Release 一个 zip，几 MB）。**目录与图片都不随编辑器分发**：
      ⚠ 已查证（2026-10-11）玩家侧游戏目录**没有散装资源**，全部在 `SlayTheSpire2.pck`（1.99GB）里
      ⇒ 编辑器内置一个**只读 pck 提取器**（格式知识已有：`tools/pck_tool.py`，V1~V3），首次运行指向游戏目录、
      按需提取图片缓存到 `%APPDATA%`。**⚠ 2026-10-12 订正"图怎么取"**（实机查了主 pck 与两个 mod pck）：
      pck 里**没有散装图片** —— 只有 `<图源>.png.import`（~200 B，内含
      `[remap] path="res://.godot/imported/<名>-<md5>.ctex"`）与 `.ctex`（主 pck 3555 个：**2311 WebP / 704 BC7 / 540 S3TC**）。
      **单卡整图 `packed/card_portraits/…` 是 WebP**（编辑器优先用它、直读即可），
      **卡图集 `card_atlas_0/1.png` 与 `images/relics/*.png` 是 BC7**（要图集裁剪时才需要 BC7 解码器）。
      mod 资源在**各自工坊 pck**（`steamapps\workshop\content\2868840\<itemid>\<Name>.pck`，挂载为 `res://<Name>/…`；
      本地 mod 在 `%APPDATA%\SlayTheSpire2\mods\…`），形态一模一样 ⇒ 提取要**叠加所有 pck**。
      目录侧 = 玩家先在 mod 设置页点一次"导出内容目录"生成他自己机器的 `content_catalog.json`（含他装的 mod 卡），
      编辑器读它；没导出则提示先导出（不显示列表）。**（✅ 该按钮已于 r275 实装、r276 修图路径，见 §预设战神 v2 的 Phase B-3。）**
    - **用别人分享的预设 = 零**（预设 = 小 json，编辑器支持导入/导出）。
  - **✅ 剩余两项已拍板（2026-10-11 用户）**：④ 首批可编辑维度 = **一步到位全要**（卡组/遗物/角色皮/血量/升级，
    用户原话「大不了我慢慢测」）；⑤ 件数阶梯 = **允许破坏平衡**（用户原话「玩家想怎么样就怎么样我们无权干涉」
    ⇒ 编辑器不锁件数；mod 侧对超出阶梯的预设只打一条提示日志、**不阻断**）；产物 = **独立 `hellwar_presets.json`**（默许）。
  - **❓→✅ 「mod 角色的战神」可行性（用户 2026-10-11 问，答 = **高**，且有 3 个实机验证点）**：
    - **依据**：① 皮肤解析 `HellwarSummonCombatRuntime.ResolveSkin` 的普通档已经在用 `owner.Character`
      （= 传 `CharacterModel` 实例这条路**已通**，自动兼容 mod 角色）；② 卡组/遗物的解析链基于 `ModelDb.AllCards`
      的**字符串 id**（`HellwarWarGodDecks.LookupByEntry`），而编辑器产物恰恰是字符串 id ⇒ **mod 卡/遗物天然可用**
      （内置默认表因编译期 `ModelDb.Card<T>()` 类型引用而永远只能原版 —— 这正是 AE-11 口径的分工，不算缺口）；
      ③ 实机先例：瓦库商店已在买/打第三方卡（`YUWANCARD-…` / `YUI_…`），按 id 发遗物（`AddRelicInternal`）同链路。
    - **待实机验证 3 点**：① mod 角色 id 能否从 `ModelDb` 枚举到、并按 id 取实例（导出 + `SkinKey` 改字符串 id 后）；
      ② mod 角色的初始卡组/遗物在导出时机是否已构建完成；③ mod 资源图在 **mod 自己的 pck** 里（如 `HextechRunes.pck`）
      ⇒ 编辑器的资源提取要**叠加 `mods\*\*.pck`**（不止主 pck）。
    - **设计预留**：`SkinKey` 从 4 值枚举改**字符串 id**（可指向任意 `CharacterModel`，含 mod 角色）；
      缺失降级沿用既有口径（`预设卡组配置里有认不出的卡牌（已跳过…）` + 回落召唤者角色）⇒ 没装那个 mod 的玩家
      用到含 mod 内容的预设**不崩**，只跳过缺失项并 WARN。
  - **🆕 Phase A 已落地（2026-10-11，r269，⭐ 待实机）= mod 侧地基**：`hellwar_presets.json`（档位 × 角色预设二维）全链路：
    - **格式**：每条 = `id / name / tiers(一套可复用多档) / skin / deck(与 hellwarDeck* 同文本格式) / relics(id 列表) /
      upgradeDeck / bonusHp / default`；**字段缺省(null) = 继承该档内置默认**；deck 显式空串 = 不替换；relics 空列表 = 一件不加；
      **件数阶梯只 INFO 不阻断**（拍板⑤）；档位名收中文别名（⚠「罕见」=Rare、「稀有」=Epic，测试钉死）。
      **`bonusHp` 正数加、负数减**（r289，2026-10-20 用户拍板）：负数 = 减少血上限、**最少留 1 点**
      （纯逻辑 `HellwarPresetHpPolicy`，召唤锚点 `①e 预设额外血`）；归一阶段**不改值、不记问题**
      —— 下限要等拿到角色基础血量才算得出（普通档可能是 mod 角色的皮）。
    - **代码**：纯逻辑 `PureLogic/HellwarPresetData.cs`（DTO + `HellwarPresetPolicy` 归一 + 继承映射，**15 条新单测**，全绿 1172）+
      运行时 `LocalHellwarPresetStore`（entry-init / run-launched / ModelDb.Preload 后三处幂等重读）；
      record `HellwarWarGodPreset` 加 4 个可选参数（内置四档零改动）。
    - **消费点**：单点召唤 = 该档**默认预设**（`LocalHellwarPresetStore.ResolveDefault`）；`ResolveSkin` 支持任意角色 id
      （`ModelDb.AllCharacters` 按 Entry 匹配，查不到回落召唤者 + WARN；**mod 角色待摸注册机制**）；
      卡组预设文本优先于 `hellwarDeck*`；遗物按 id 查（`AllRelics`+`EventRelicPool`）。
    - **Phase A 边界（刻意不做）**：【无尽战神】/【九连环】的**随机池仍走内置档位表**（接文件池与「召唤时选预设」UI 一起 = Phase B）；
      **游戏内导出 `content_catalog.json`** = Phase B（跟编辑器一起）—— **✅ 已落地（r275，见 §预设战神 v2 的 Phase B-3）**。
    - **复测契约（r269）**：① 不写文件 ⇒ 启动 `[地狱战神] 预设表: 未配置…`，召唤行为与 r268 逐字一致（回归）；
      ② 手写一份（Rare 一套 skin=IRONCLAD + deck + relics=[MINIATURE_CANNON] + default）⇒ 启动 `预设表已加载: 套数=1` +
      `档位=Rare 默认预设="…"(…)`，召唤罕见档 `①a 预设卡组已替换 … 来源=预设(…)` + `①c 预设遗物已发放 … MINIATURE_CANNON`；
      ③ 故意写不存在的卡/遗物 id ⇒ 启动 WARN 点名、召唤跳过不崩；④ relics 写 5 件 ⇒ `超出阶梯（允许，玩家自由）` INFO。
- **⏳【用户 2026-10-11 点名】海克斯符文兼容补丁（`compat-mods\HextechRunesLocalCoopFix`）：抓率统计 + 按抓率选符文 + 选项很烂时自主 roll**：
  - **✅ 2026-11-03 已落地（v1.8.0 选择 + v1.8.1 判读锚点，⭐ 待实机命中升档）**：
    用户 2026-10-31 订正「每次 3 个选项稀有度一致」这个旧观察（实测 **roll 之后可能出稀有度更高的符文**）后，
    选择顺序改为 **稀有度更高者优先（棱彩 > 金 > 银）→ 同档按抓率 → 再并列取最左**；
    稀有度读不到 / 全未知 ⇒ 退化成纯抓率（行为与 v1.7.x 逐下标一致）。
    自主 roll 的判据补上「升档机会」：某槽 reroll 后可达档位高于本屏最高档就 roll 它（优先于旧的「三张全烂」）。
    - 纯逻辑 `Scripts/RuneRarityPolicy.cs`（新，链接进单测工程，14 条）；
      反射 `Scripts/RuneRarityReader.cs`（新）：`HextechCatalog.TryGetPlayerRuneRarity`（兜底 `PresenterOptionRarity`）；
      升档可达档 = 金色 reroll 的 `UpgradedRarity` / 保底首抽槽用 reroll 闭包 `stageRarity`（= 本幕档位）。
    - **反编译事实（别再猜）**：`HextechRunePoolBuilder.GetRerollRarity` = 该槽自身档位
      （只有 `IsGuaranteedFirstPickRune` 槽改用本幕档）⇒ **普通 reroll 是同档换牌，不升档**；
      只有金色 reroll（`GetGoldenRerollOverride`）与保底首抽槽会升档。
    - **实机两轮（2026-11-03）**：稀有度反射四项全可读；第 2 轮真实触发 **3 次「全烂」reroll**（逐槽各一次），
      **每次 reroll 前后档位都是全金**（印证上面那条事实）；最后一张无数据（未知 0.5 > 0.40）⇒ 停止 roll 并选中它。
      ⇒ **仍未命中**：混合档位下的稀有度优先、金色 reroll / 保底首抽槽的升档 roll。
    - **下一步（不加强制调试开关，用户口径「等后面玩的时候测到」）**：自然遇到金色 reroll 时看
      `瓦库符文选项有升档机会，自主 reroll: … 当前档=…, 可达档=…` + 随后作答行的 `档位=[…]`；
      若一直不出现，先看启动的 `符文稀有度反射面` 里「金色reroll=可读」。
    - 新增判读锚点：`符文稀有度反射面: 档位=…, 金色reroll=…, 保底首抽=…, 本幕档位=…`（启动）、
      作答行 `档位=[…] / reroll=未触发（无升档机会：…；抓率未全烂）`、候选日志（**含真人席位**）`档位=[…]`。
  - **需求原话**：「为海克斯符文兼容补丁 mod 加上我们的本体 mod 相似的**抓率统计**，瓦库根据抓率选择，
    并且在**选项很烂的情况下自主 roll 符文**」。
  - **✅ 前两件已落地（v1.3.0 / r299，⭐ 待实机）**：
    - 纯逻辑 `Scripts/RunePickRatePolicy.cs`（抓率 = `picked/offered`，Laplace 平滑 ⇒ 没数据 = 0.5；
      并列取最左 ⇒ **没有统计时行为与旧版"取最左"逐下标一致**；坏 JSON 按空账本）；
    - 运行时 `Scripts/RunePickStats.cs`：落盘 `%APPDATA%\SlayTheSpire2\hextech_rune_stats.json`
      （写 `.tmp` 再原子替换）；**真人与瓦库的每一屏都记**（`RelicsSelected` 之前记 offered +
      给候选按钮挂"按下即记账"的 picked，一屏只记一次）；
    - 瓦库作答改成按抓率挑（`WakuuRuneAutoAnswer`）；**自选池屏（锻造选择）不参与统计**（会冲爆 offered）、仍取最左；
    - 开关 `HRLF_RUNE_STATS=0`；新锚点 `符文抓率记账（候选/选中）`、`rule=按抓率, 抓率=[…]`；
    - 单测 15 条（`tests/LocalMultiControl.Tests/RunePickRatePolicyTests.cs`，**链接源码**进测试工程）。
  - **✅ v1.4.0（2026-10-21 实机判读后补的两件，r299b）**：
    ① **与 Co-op Bots 并存**：它的**合成 Bot 席位**（NetId 巨大，实测 `12716757972810793218`）
    既不是主 mod 的本地席位、也不是真远端 ⇒ 海克斯判成远端玩家 ⇒ `WaitForRemoteHextechChoice` 死等
    （实测 11 条 WARN、符文批次一直挂着 = 用户报的「Co-op Bots 导致海克斯符文无法正常使用」）。
    修法 = ① 的放行判据扩成 `IsLocalSeat || CoopBotsAdapter.Drives(seat)`（后者反射自主 mod），
    ⑤ 的代答判据同一口径（日志里叫 `Co-op Bots` 席位）。
    ② **「打到一半突然 1fps」**（同一份日志）：`HextechBurnVisual` 在生物的 Spine 被摘/换掉后仍每帧读
    已释放骨骼 ⇒ `ERROR: Native Spine object not set` **2944 次**（Godot 的 ERROR 是同步 I/O）⇒
    `fps均=1.0`、`引擎 process≈1500ms/帧`（之前 53.8 / 49.7ms）。修法 = 两层反射守卫
    （`Tick` 前缀查 `_spineSpriteNode`，`UpdateBoneEmissionPoints` 前缀再逐个查 `_emissionBones`）
    ⇒ 失效就调它自带的 `DisableBoneEmission()`（火焰改走矩形发射，照常显示）。
    新锚点：`海克斯燃烧视觉的 Spine 骨骼发射点已停用（源=Tick/UpdateBoneEmissionPoints，第 N 次）`。
  - **✅ v1.4.1「点名」探针**（用户 2026-10-21「好吧加一个」）：停用那一刻打出**挂在谁身上** ——
    `对象=怪物=<ID>` / `对象=席位=<角色N>(本地席位|Co-op Bots 合成席位|**不在 run 玩家表=临时席位/战神**)`。
    取法 = 反射基类 `HextechCreatureAttachedVisual.Creature`（`protected NCreature`）→ `Creature.Entity`
    → `IsMonster/Monster` 或 `Player.NetId`；席位描述走 `MainModBridge.DescribeSeat`
    （**判据顺序：Bot → 本地 → 正式 → 临时**，因为合成 Bot 席位**也在** `runState.Players` 里）。
    目的 = 海克斯自己不打挂载日志（`[Burn]` 全 0 条），下次复发就能一句话定死是不是我们的战神。
  - **✅ v1.4.2（2026-10-21 第二次实机"还是好卡"后改口径）**：
    `godot__20261021-185710__r298.log` 里同一条栈又刷 **1472 次**、`fps均=1.0`/`process≈1209ms`，
    而**我们的守卫日志 0 条** ⇒ 证明"检测失效再降级"抓不住：骨骼的**原生对象被就地释放**
    （SpineSprite 节点同 id、包装对象 `IsInstanceValid` 为真，连海克斯自己的 InstanceId 判据也过）
    ⇒ 托管侧判不出来（唯一能判的方式就是调用它 = 刷错误）。
    **改为默认「强制矩形发射」**：`Tick` 前缀见骨骼缓存就 `DisableBoneEmission()`（每视觉一条点名日志）+
    `TryInitBoneEmission` 前缀挡掉重抓 + `UpdateBoneEmissionPoints` 前缀兜底；
    想恢复原样设 `HRLX_BURN_BONE_EMISSION=1`（退回检测档）。代价 = 火焰从"贴骨骼"变"矩形区域发射"（纯观感）。
    通用原则（已沉淀 references §十一 *补记*）：**修第三方性能事故，结构性绕开 &gt; 精确检测**。
  - **✅ 掉帧关单（2026-10-21 第三次实机，用户「现在不卡了」）**：
    `logs-archive/godot__20261021-190924__r298.log`（1.5MB）—— `Native Spine object not set` **0 条**；
    性能窗口 24 个：`fps均=53.1`（最低 31.2）、`引擎 process` 均 66.0ms / 最大 258.6ms（单次场景切换尖峰）；
    同一个触发点（L11758 `Missing sprite 'hextech_burn_power'` + `missing_power.png`）**这次不再有后续报错**；
    `[ERROR]` 只剩 5 条既存第三方（Oddmelt 注册/挂载、Manosaba·MEP 分支）。
  - **✅ v1.4.3 两处收尾**：① 点名日志一直没打出来 —— `HextechBurnVisual` **不是 Godot 节点**，
    拿不到 `GetInstanceId()` ⇒ 去重键改用**引用**（`ConditionalWeakTable`），下次着火就会打出 `对象=…`；
    ② ⑧b（`CreateForgeSelectionScreenAsync`）在海克斯 0.111 **已被重构掉**（锻造屏改走符文协调器的
    `CreateRuneSelectionScreenAsync`，③b 已覆盖）⇒ 标为**可选目标**，不再把 `INIT` 打成 `DEGRADED`。
  - **✅ v1.5.0 拆分（2026-10-21 用户拍板「完全和我们无关 ⇒ 拆成独立补丁并且不上传 github」）**：
    燃烧视觉守卫已从本补丁**移出**（点名确认触发者是**怪物**、且在第三方 Loadout 图鉴预热里 ⇒ 纯海克斯自身问题），
    改放到仓库外 `D:\Download\pain\HextechBurnVisualFix\`（不进 git、不随 Release/compat 附件分发；
    `HBV_BONE_EMISSION=1` 检测档、`HBV_DISABLE=1` 整套不挂）。本补丁从 v1.5.0 起只管符文/锻造选择与归属。
  - **✅ v1.6.0 已实现：选项很烂时自主 roll**（2026-10-21，**⭐ 待实机**）：
    反编译 `lib\0.111.0\HextechRunes.dll` 确认 —— `HextechRuneSelectionScreen._rerollButtons`
    （节点 **`RerollButton_{slotIndex}`**，与 `_holders`/`_relics` 同序同下标；`Pressed` → `OnRerollPressed(slot)`），
    **"剩余次数"不用另读字段**：该槽按钮 `Disabled` = 已用完（上限 `_playerRuneRerollLimit`，默认 1）；
    reroll 后它自己 `RebuildCards()` + `RestartSelectionConfirmGuard()`（1s 点击保护窗重置）。
    判据（纯逻辑 `RuneRerollPolicy.cs` + 10 条单测）：**三张全烂才 roll**（每张都要有数据且抓率 ≤ 0.40；
    没数据 0.5 = 未知不算烂）⇒ 按最烂那槽；每屏每槽只试一次（**不能只看 Disabled**：新候选与旧相同时它原地
    return、按钮不变灰 ⇒ 会无限重试）；reroll 后 1.2s 重入作答；自选池屏不 roll；`HRLF_RUNE_REROLL=0` 关、
    `HRLF_RUNE_REROLL_THRESHOLD` 调阈值。
    **✅ 已实机生效（2026-10-21，`logs-archive/godot__20261021-202243__r298.log`）**：
    `L34276 自主 reroll: slot=0, 旧=STRENGTH_TO_DEXTERITY_RUNE, 抓率=[0.33(0/1) ×3], 阈值=0.40` →
    `L34278 自动作答: picked=ATTACK_DEFENSE_UNITY_RUNE（新符文无数据）, rule=按抓率` → `L34286 界面已关闭`。
    闭环正确：全烂 → 换最烂那槽 → 重读候选 → 选到新符文。⚠ 若一次没触发，先看抓率是否真有 `offered>0` 数据。
    - **✅ v1.7.0：符文候选卡**选取率角标**（用户 2026-10-21 当面新需求「我自己看不到选取率，请像本体 mod 一样做个显示」；⭐ 待实机）**：
    `RunePickRateBadge` 在候选按钮（`_holders[i]`）**左上角**加子 `Label`：`33% (1/3)`（**原始比例**，
    不是决策用的 Laplace 分）/ 无记录 `—` / 30%·50% 分档 红·黄·绿；`MouseFilter=Ignore` 不挡点击。
    刷新 = `RelicsSelected` 前缀 + `RebuildCards()` postfix（reroll 后立即补）+ 0.5s 轮询兜底（屏关自动停）；
    自选池屏不画；`HRLF_RUNE_BADGE=0` 关；纯逻辑 `RuneBadgePolicy.cs` + 4 条单测。
    **实机验证契约**：进符文屏应看到 `符文选取率角标已挂到候选卡（左上角…），候选=3`，
    三张卡左上角各有一行百分比（没统计过的显示灰色 `—`）；reroll 后角标应跟着换内容/立刻出现。
  - **✅ 抓率统计首日实测**（`logs-archive/godot__20261021-184214__r298.log`）：`符文抓率记账（候选）` 10 条 /
    `（选中）` 9 条 / `rule=按抓率` 5 条；落盘 `%APPDATA%\SlayTheSpire2\hextech_rune_stats.json` 已积累
    （如 `STARLIGHT_SPARKLE_RUNE` 3/3、`MORE_THE_MERRIER_RUNE` 0/5）。
- **🕐【退场动作·上游自修后】海克斯若自己在 `HextechBurnVisual` 修好骨骼校验**：本地独立补丁
  `D:\Download\pain\HextechBurnVisualFix\` 设 `HBV_DISABLE=1`（或删 `mods\HextechBurnVisualFix\`）即可，
  **不必追版本**；该补丁代码在仓库外，无需改本仓库。
- **✅【2026-10-24 关单】猪猪 mod（`YuWanCard`）本地多控兼容补丁**（②【队友帮付】实机通过；①【我全都要】静态判定安全、限张数待拍板）：
  - **需求原话**：「排期以后做一个猪猪 mod（YuWanCard mod）兼容补丁。因为猪猪 mod 有一些功能还不兼容，
    包括遗物【我全都要】可以在卡牌奖励中选择多张牌，虽然现在还没有引发问题但是**瓦库自动抓牌可能会有问题**
    （**其它 mod 也有可能有类似的功能可能有影响**）；【队友帮付】遗物可以在商店让其它玩家付钱，
    但是**我实际测试是不行的**」。
  - **① 遗物【我全都要】（卡牌奖励多选）**：症状 = 我们假设"选一张 → 关屏 → 进入下一张奖励"，
    多选遗物会让一屏持续存在 / 一次取多张 ⇒ 瓦库自动抓牌可能重复取、卡在等屏、或把别人的卡算到自己头上。
    抓手 = `Scripts/Runtime/LocalWakuuRewardAutoClaim.cs` + `Scripts/Patch/CardRewardPatch.cs`（自动抓牌入口）
    ⇒ 判据要改成"看这一屏**还能不能继续取**"，而不是"取完一张就收口"；**同类第三方功能一律按这个口径兜**
    （用户明确点名"其它 mod 也可能有类似功能"）。
    **静态结论（2026-10-24，暂不改代码）**：遗物只 override 原版钩子 `ShouldAllowSelectingMoreCardRewards`；
    原版 `CardReward.OnSelect` 本身是 `while(!endSelection)` 循环，我们的自动领取在**同一次** `SelectUnsynchronized()`
    里由选择器逐轮作答，`_cards` 取空后 `GetSelectedCardReward` 返回 `card=null` ⇒ `num=null` ⇒ 循环正常收口
    ⇒ **不会卡在等屏**。唯一的行为差异是"瓦库会把这一屏抓到空"（多选语义本身就允许），是否需要限制张数待用户拍板。
  - **② 遗物【队友帮付】（商店让其它玩家付钱）：✅ 已落地 v1.0.0 并**实机关单**（2026-10-24，r317 局）**。
    **根因（反编译 + 回环服务对照钉死）**：整条链都走**定向**网络消息
    （`QueryAllTeammatesGold` → `SendRequest` → `SendResponse`），而主 mod 的
    `LocalLoopbackHostGameService.SendMessage<T>(T, ulong)` 只打日志、**不派发** ⇒ 付款者收不到请求、
    不弹确认、`ProcessPurchase` 永不执行 ⇒ "点了没反应"。另外 `OnPayRequestReceived` / `DeductGoldLocally`
    都用 `LocalContext.GetMe()`（本地多控下只返回前台那一位）⇒ 消息通了也会判失败/扣错人。
    **修法**：`compat-mods/YuWanCardLocalCoopFix` 仅在回环会话里前缀接管
    `TeammatePayShopPatch.OnTeammatePayButtonPressed` —— 复用猪猪自己的「选择队友」弹层（喂本机真实金币）
    ⇒ 本机 `PlayerCmd.LoseGold` 扣那位席位 ⇒ `entry.OnTryPurchaseWrapper(inventory, ignoreCost:true)` 结算
    ⇒ 失败退回。全程不发网络消息；真实联机一律交回原版。**不引用第三方 dll**（全反射，改名/缺席只降级）。
  - **复验结果（2026-10-24，`logs-archive/godot__20261024-140659__r317.log`，用户「应该没问题」）**：
    `INIT_OK: patched=[TeammatePayShopPatch.OnTeammatePayButtonPressed], missing=[], source=assembly-load:YuWanCard.Content`
    （补丁在主 mod 之后、猪猪 Content.dll 加载时才挂上，符合"壳 dll + 变体 dll"的预期）；
    L12002 第一次点（控制角色1、付款席位仅 99G < 188）⇒ 弹层按钮灰 ⇒ 用户取消 ⇒ `玩家取消/超时，未结算`（**未扣钱** ✅）；
    L12083~12089 第二次（450G）⇒ `requester=…326, cost=188` → `已扣款: payer=…327, 剩余=262G`（450−188=262 ✅）
    → `奖励-拿遗物归属玩家: context=…326` + `个人记录-商店购买: item=MINIATURE_TENT` → `购买完成 purchased=True` ✅。
    同局 `[ERROR]` 8 条全为既有第三方/游戏侧（Steam 分支不支持 2 个 mod / Oddmelt 注册 / 存档删除失败 / 某遗物本地化格式串），
    `turn loop died` 0、`已有其它弹层` 0、`看门狗重启失败` 0。
  - **第二轮复验（2026-10-24 `logs-archive/godot__20261024-141552__r317.log`，用户「2 发起 1 付钱测了，
    假商人也测了，一切正常」）**：① **反向成立** —— L11167 `requester=…327, cost=226, 候选=[…326:450G]`
    ⇒ 扣 …326 → 224G ⇒ 买 `ORRERY` 归 …327；② **假商人（`FAKE_MERCHANT` 事件）双向都过** ——
    L11276（角色1 付 49G 买 `FAKE_LEES_WAFFLE`，付款席位 99→50）与 L11304（角色2 付 51G 买 `FAKE_MINIATURE_TENT`，
    224→173），两次都 `purchased=True`、遗物归发起者；③ NetId 已是完整值（截断修复已生效）。
    同局健康度：`INIT_OK` / `turn loop died` 0 / `[ERROR]` 6 条全为既有第三方侧（Steam 分支不支持 2 个 mod、
    Oddmelt 脚本注册、某遗物本地化格式串）。
  - **唯一遗留（不是 bug）**：购买走 `ignoreCost:true` ⇒ `个人记录-商店购买` 的 `gold=0`（钱在付款者那边扣），
    **猪猪原实现也是这样**；另日志候选 NetId 曾用 `(int)` 截断成 `462261599`（= ...327 的低 32 位）易误判，
    已改成完整 NetId 并重新部署。
- **⑱ 战神的血条并进「本体玩家旁边的召唤物血条」面板（A 档；✅ 已落地 r300，⭐ 待实机）**：
  - **需求原话**：「记下来以后把战神的血条放到本体玩家的旁边（用我们之前做的召唤物血条）」（2026-10-21 点名）。
  - **既有载体现状**（`Patch/NMultiplayerPlayerStatePetHpPatch.cs`，r86）：在**多人顶部玩家状态条**
    `NMultiplayerPlayerState` 的 HP 条右缘挂一块面板，逐行画该玩家 `PlayerCombatState.Pets` 的迷你血条
    （名字 + 当前/上限；死亡 `0-X` 置灰）；每帧从数据重算、`MouseFilter=Ignore` 不挡点击、
    开关 `PetHpBadge`、仅"本地双控 + run 进行中"显示 ⇒ **中途召唤/死亡/复活/战斗结束清空天然正确**。
  - **差距**：战神**不是 pet**（不在 `Pets` 里），它是 `CombatState` 里的**临时 Player**（有自己的立绘 +
    立绘上方血条）⇒ badge 原来完全看不到它。
  - **r300 实装（用户 2026-10-22「先做战神血条吧」⇒ 取 A 档：只加行、原位血条不动）**：
    - 数据源 = `Pets ∪ 绑主=本人的在册战神`：新增
      `HellwarWarGodFactory.CollectWarGodsOfSummoner(state, summonerNetId) → (Player, Tier)[]`
      （按登记顺序、**含已死等复活**、`state.Players` 里查不到的不返回 ⇒ 战斗结束/清理天然消失）；
    - 行规划抽成纯逻辑 `WarGodHpBadgePolicy`：召唤物在前（文案 `名字 当前/上限` **逐字不变**）、
      战神在后（`战神·档位 当前/上限`，档位中文口径同预设表：普通/罕见/稀有/先古）、
      **超 8 行压成「还有 N 个（已折叠）」**（12 席全画会把面板拉出屏幕）、死亡一律 0 条 + 置灰；
    - 首帧锚点改成 `召唤物/战神血条：player=…, pets=N, 战神=M, 行数=…`（原来只打 pets）。
    - +11 单测（`WarGodHpBadgePolicyTests`）：文案/顺序/折叠/边界上限/死亡/血量兜底。
  - **复测契约**：战斗中召唤战神 ⇒ 该绑主的顶部状态条右侧多一行 `战神·<档位> 75/75`；
    打掉它半血 ⇒ 那行条宽跟着变；打死（尸体留场）⇒ `战神·… 0-75` 置灰；复活 ⇒ 恢复；
    战斗结束 / 退出这一局 ⇒ 那行消失（不需要额外清理）。
  - **B 档（把战神立绘上方的原血条隐藏）= 用户 2026-10-22 拍板「不需要」** ⇒ 原血条永久保留，不再做隐藏。
  - **✅ r300 实机通过（2026-10-22 用户「战神和奥斯提的血条都正常」）**：2 个玩家各召 2 个战神，
    两边面板各自 2 行、奥斯提行也在；`INIT_OK` 1 / `turn loop died` 0 / 我方 `[ERROR]` 0。
  - **🆕 r301 两条（同轮实机反馈，⭐ 待实机）**：
    - **「招了 4 个只看到 3 个」= 原版站位算法只支持 ≤4 人**：`NCombatRoom.PositionPlayersAndPets`
      列数 `ceil(sqrt(玩家数))`、列宽只按**前几列**算（源码注释自己写着 "Technically we only support four players"）
      ⇒ 2 真人 + 4 战神时实测两个只隔 **36px**（几乎完全重叠）、一个被推到 **x≈-150（屏幕外）**。
      修法 = **按绑主自己归位**（`WarGodFormationPolicy`：站在绑主立绘上方、同一绑主的战神排扇形、
      每行 4 个、逐行往上、屏幕边界夹取），召唤收尾 / 复活各重排一次；顺带把原版对"第二排"的**置灰**恢复原色。
      锚点：`战神站位已按绑主归位: 绑主=…, 数量=…, 锚点=…, 位置=[…]`。
    - **多玩家同时召唤时看不出弹层是谁的** ⇒ 两层选择弹层（召唤档位 / 角色预设）标题加 `—— 角色N`
      （`LocalModText.ForRole` + `LocalSelfCoopContext.GetSlotLabel`）。
  - **r301 复测契约**：两个玩家各召 2 个战神 ⇒ 4 个立绘都看得见、各自排在**自己绑主上方**（不再重叠、不再出屏）；
    弹层标题显示 `召唤战神（消耗计数） —— 角色2` / `选择战神角色预设（第 2 步） —— 角色2`。
  - **🔴 r301 实机半成 ⇒ r302 修（2026-10-22）**：用户「玩家1的2个正常，玩家2只看到1个，另一个在屏幕边缘」。
    探针证明**落点全对**（4096→649.5 目标 650 / 4098→741.5 目标 742 / 4097→421 目标 421），
    只有**最新召唤的那只**（4099）停在原版给的 x≈-53~-66，且它那次召唤的站位日志**一条没打**。
    根因 = `_warGods.Add(...)` 写在 `RunSafely(FinishSpawnAsync(...))` **之后**，而收尾里的
    `AfterCreatureAdded` / `Hook.AfterCreatureAddedToCombat` **可能同步完成** ⇒ 重排读到的是
    **还没登记**的旧表 ⇒ 最新那只自己不在表里。修法 = **登记提前**到 `RunSafely` 之前
    （`RollbackSpawn` 本来就 `RemoveRegistration`，回滚语义不变）。
    **r302 复测契约**：每召唤一只都应看到**数量递增**的 `战神站位已按绑主归位`（1 → 2 各绑主各一条），
    4 个战神立绘都在各自绑主上方、不重叠、不出屏。
  - **✅ r302 实机（2026-10-22）：站位递增日志全在**（`数量=1→2→3`，含最新那只）⇒ 位置问题关单。
  - **🆕 r303 三条（同轮用户反馈，⭐ 待实机）**：
    - **用了【无尽战神】后两个真人突然贴在一起** ⇒ `WarGodFormationPolicy.SpreadEvenly`：
      **只在间距 < 232px 时**以中点拉开（够宽就一个都不动），锚点 `真人站位已拉开（原版人多时会挤到一起）`；
    - **计数用光仍弹【终极九连环】** ⇒ `CanOpenNineChain` 判据补上**本场可用 ≥ 本次花费**
      （原来只看上限 12），`TrySpendPermanentSpirit` 同步要求可用够（否则会把"本场已花"顶成负数）；
    - **战神有时退回原版多人排布** ⇒ 新增 `SettleFormation`（真人拉开 + 战神归位）：
      **一批召唤结束后**统一收尾 + **真人回合开始**幂等兜底。
  - **✅ r303 实机（2026-10-22 用户「其它大概都没问题」）**：九连环判据与"下回合纠回原位"都验过；
    真人位置还会变但**不再贴贴** ⇒ 用户拍板**暂时不管**。
  - **🆕 r304（判读 r303 日志）**：
    - **「被重排」的触发者 = 友方单位被加进战斗**（用户猜的奥斯提正是之一）：`osty.tscn` 一加载，
      紧接全队立绘大跳变（Δ 最大 -446）。原版 `PositionPlayersAndPets` **只在开战建节点时调一次**
      ⇒ 中途重排来自**第三方布局补丁**（NinjaSlayer 的 YamotoKoki reflow），非我方；
    - 我们自己的"真人来回动"= r303 把含真人拉开的 `Settle` 也放进了**逐只**收尾（每加一只挪 ±35px，
      刷 15 次）⇒ r304 改为**只在批次结束后 + 真人回合开始**拉开，逐只收尾只摆战神。
  - **🆕 r305（r304 实机「感觉好像什么都没有修」）**：r304 生效（`真人站位已拉开` 15 → 1），
    但**被第三方重排之后没人纠**（实机：批次结束摆好 622/714/806/898，十秒后重排散成 203/280/356…，
    真人间距只剩 80px，直到战斗结束都没恢复）⇒ 当时加了 400ms 的 `GuardFormationTick` 偏差轮询。
  - **🆕 r306 = 用户否掉轮询，改成"接管原版重排入口"**（原话：「每 400ms 跑一次这种东西太多会影响游戏，
    为了修小的视觉 bug 加这个不太好；无尽战神打到后面本来就已经有点卡」）：
    `NCombatRoom.PositionPlayersAndPets` 是 **`public static`** ⇒ 挂 **Postfix** 覆盖重排结果；
    **r305 的轮询/偏差检测整套删除**。锚点 `站位重排已接管（…）: 战神=N 个`。
  - **🆕 r307 = 用户又提更根本的顾虑 ⇒ 撤回接管**（原话：「原版做重排肯定有它的理由…万一有其它 mod 需要重排呢？
    远的不说，这个会影响奥斯提之类的宠物吧。果然还是回滚比较好」）：
    ① 原版布局**把宠物摆在主人脚边**，我们覆盖 ⇒ 主人/战神挪走而**宠物留在原地（脱钩）**；
    ② 覆盖别人的布局意图会让第三方逻辑失效。⇒ **删掉 Postfix**（基线回到 205/245），
    只保留"自己的事件上自愈"（批次结束 + 真人回合开始），并新增 `MoveWithPets`：
    挪立绘时**连带平移它名下的宠物**（`PlayerCombatState.Pets`）。
- **⑰ r290（2026-10-20 判读 + 修复，✅ **实机关单**）—— 「故障机器人被召唤时不会显示充能球」= 视觉球槽只在**开战那一帧**建，战神是中途席位**：
  - **根因**（`NOrbManager.cs:88-94`）：球槽**视觉节点**只在 `CombatManager.CombatSetUp` 时按当时的
    `PlayerCombatState.OrbQueue.Capacity` 建（`OnCombatSetup → AddSlotAnim(Capacity)`）。战神是**战斗中途中**
    加入的席位 ⇒ `NOrbManager._orbs` 永远 **0 个节点**；而**数据层有槽**（`PlayerCombatState` 构造里
    `OrbQueue.AddCapacity(player.BaseOrbSlotCount)`，`Defect.BaseOrbSlotCount = 3`）⇒ 充能/激发**数值全对**、
    只是画面上一个球都看不到。r283 的 `球槽视觉兜底` 只把动画兜掉了（不再打死 turn loop），这层一直没补。
    （旁证：r288 局同一次打 `TEMPEST` 时日志只有 `球槽视觉兜底 … method=AddOrbAnim`，没有任何球节点。）
  - **修法**：`Runtime/WarGodOrbSlotBridge.SyncVisualSlots` —— 取 `NCreature.OrbManager`，把
    `Capacity − 视觉槽数` 的差额补成空槽（等价于原版开战那一刻做的事）；**幂等**、纯表现、失败只 WARN。
    调用三处：③ 建完节点立刻一次 / `FinishSpawnAsync` 收尾一次（覆盖 deferred 建节点）/
    `ReviveWarGodAsync` 一次（死亡时 `NCreature.AnimDie` 会 `OrbManager.ClearOrbs()`，复活后同样缺槽）。
    顺带把 `_orbs` 的反射读收进 `OrbSlotVisualProbe`（r283 兜底补丁与本桥共用一份）。
  - **复测契约（r290）**：召唤**故障机器人皮**的战神 ⇒ 立绘旁出现 3 个空球槽 + 日志
    `[地狱战神] ③c 已补球槽视觉节点: seat=…, 数据层槽=3, 视觉 0 → 3`；它充能后球**看得见**，
    且**不再**出现 `球槽视觉兜底 … method=AddOrbAnim`；复活后（死一次再复活）同样有槽。
  - **✅ 实机判读（`logs-archive/godot__20261020-224247__r290.log`）**：两个故障机器人战神
    （4096 先古 `defect3` / 4097 普通 `char:DEFECT`）各一条 `③c 已补球槽视觉节点: 数据层槽=3, 视觉 0 → 3`；
    全场 `球槽视觉兜底` **0 条**；4096 实打 `ZAP`/`VOLTAIC`/`TURBO`/`ECHO_FORM`/`BIASED_COGNITION`
    （球的进出动画正常走）；`turn loop died` 0、`托管出牌抛异常` 0、退局复位 `无残留`。
    4097 在 round 3 才首次轮到、战斗随即结束 ⇒ 0 出牌（= §待查-4 "没轮到" 同口径，良性）。
  - ✅ **复活路径也验了（2026-10-21，`logs-archive/godot__20261021-124341__r290.log`）**：
    `③c 已补球槽视觉节点: seat=4096 … 视觉 0 → 3` 紧跟着 `已复活战神（R9）: 血量=75/75` ⇒ 死亡清槽后复活能补回来。
  - **仍未覆盖**：`RemoveSlotAnim`（`BULK_UP` 类"减球槽"）仍留着 r288 的兜底 —— 有槽后本应正常执行，待踩到再验。
- **⑮ r271（2026-10-11 判读 + 修复，⭐ 待实机）—— 「进战斗没怪 / 不抽牌 / 不能换人 / 不能结束回合」= 第三方坏怪 + 第三方开战特效把 turn loop 打死**：
  - **判读**（`logs-archive/godot__20261011-191308__r270.log`，1.3MB 小局，用户进战斗就卡）：
    `turn loop died: NullReferenceException at VfxCmd.PlayOnCreatureCenter_Patch2 ← CreatureCmd.GainBlock ←
    HextechRunes.BrutalForceEnemyHex.ApplyCombatStartToEnemy ← HextechEnemyHexDispatcher.ForEachActive ←
    HextechMayhemModifier.ApplyMonsterCombatStartHexesToEnemy`，且同局
    `[ERROR] Failed to load resource: arknights_boss_monster_eblana.tscn / …_dublinn_flamechaser_soldier.tscn`
    —— **这局的怪是 ArknightsBoss mod 的怪、它的场景 .tscn 加载失败** ⇒ 敌列表留下 null 条目；
    海克斯【残暴】敌 hex 开战给怪上格挡 → 播特效 `PlayOnCreatureCenter(null…)` ⇒
    `target.IsDead` 对 null 抛 NRE ⇒ 冒穿 `Hook.BeforeCombatStart` ⇒ 回合循环被原版打死 ⇒ 卡 `NotPlayPhase`
    （没怪/不抽牌/不能换人/不能结束回合全吻合；`忽略切人请求：战斗同步阶段=NotPlayPhase`）。
    原版实现里 `GetCreatureNode()==null` 本有判空，**只漏了 `target==null`**（游戏健壮性缺口）。
  - **修法 = 主 mod 通用护栏**（新 `Scripts/Patch/VfxCmdNullTargetGuardPatch.cs`，**Combat 域**，targets 基线已更新）：
    给 `VfxCmd` 四个特效播放入口（PlayOnCreatureCenter / PlayOnCreature / 两个复数版）挂 **finalizer，只吞 NRE** ——
    特效是纯表现层（吞掉只损失那一下特效，`GainBlock` 的数值逻辑照常），其它异常照抛；命中去重 WARN
    （`[特效护栏] … 仅损失该次特效，战斗流程照常`）。**不修 ArknightsBoss 资源缺失本身**（第三方自身问题，
    用户的 ArknightsBoss 可能需要更新/重装），只保证"它坏时不拖死整场战斗"。取向同 AE-24 的表现层护栏。
  - **复测契约（r271）**：同一张图再进战斗 ⇒ ① 不再出现 `turn loop died`、怪与回合正常（若 ArknightsBoss 场景
    仍加载失败，那条 ERROR 还会在、怪可能显示异常，但**战斗能打**）；② 若再命中护栏 ⇒ 一条
    `[特效护栏] 对坏目标播特效的 NRE 已拦截…`（同族只一条）；③ 顺带回归：r270 的满员复活、r269 的预设表锚点。
    marker = `2026-10-11-r271`。
  - **⭐ r272 局（`godot__20261011-200415__r272.log`）复测情况**：本局**没踩到坏怪**（`Failed to load resource` 0、
    护栏 0 命中）⇒ ⑮/⑯ 的护栏仍待实机复测（r271 局护栏命中 1 次算部分验证；r272 的 `NCreature` getter 护栏待踩到再验）。
    ⑭ 满员复活与 Phase A 预设表锚点已在 r272 局验证 ✓（见 ⑭ 判读）。
- **⑯ r272（2026-10-11 判读 + 修复，⭐ 待实机）—— r271 护栏命中后，同一只坏怪在「伤害数字」上又把 turn loop 打死**：
  - **实机**（`logs-archive/godot__20261011-193429__r271.log`）：r271 护栏**命中 1 次**（`[特效护栏] … name=深池逐火战士`
    ⇒ 开战链活了、`StartTurn` 进入、还弹了召唤选择）⇒ 但回合 1 里猪猪 mod 遗物「猪冲前进」对这只怪扣上限
    → `CreatureCmd.Damage → NDamageNumVfx.Create → NCreature.get_VfxSpawnPosition()` ⇒
    `Visuals.VfxSpawnPosition.GlobalPosition` 对 null 链抛 NRE ⇒ turn loop died（症状 = 能进战斗但不能出牌/结束回合/切人）。
  - **根因不变**：ArknightsBoss 场景加载失败 ⇒ 怪**数据层在、`NCreature.Visuals` 为 null**（隐形怪）——
    对它的**任何视觉访问**都会炸；r271 只兜了特效入口，这次从伤害数字炸。
  - **修法（刻意收窄）**：新 `NCreatureVisualsGuardPatch`（Combat 域）给 `NCreature` 的两个 **Vector2 getter**
    （`VfxSpawnPosition` / `PowerAppliedVfxSpawnPosition`）挂 finalizer 吞 NRE、落 `(0,0)`（伤害数字画在原点，无害）。
    **刻意不兜引用类型返回**（`Body` = `Visuals.GetCurrentBody()` 等）——吞掉后调用方拿 null 再 `.PlayAnim()`
    只会把炸点转移到下游。**打地鼠策略**：后续冒出新炸点（死亡动画/意图/血条）按同模式补。
  - **真正解法在用户侧**：更新/重装 ArknightsBoss mod（它的场景资源与游戏 0.111 不匹配）。
  - **复测契约（r272）**：同一张图 ⇒ ① 不再 `turn loop died`、能正常出牌/结束回合/切人；② 命中护栏 ⇒ 一条
    `[特效护栏] 无视觉怪…兜底为原点…`（伤害数字可能画在屏幕 (0,0)，无害）；③ 若**死亡动画**再炸出新的
    turn loop died ⇒ 把新栈发我，按同模式补（或干脆先禁用 ArknightsBoss 测一局对照）。
    marker = `2026-10-11-r272`。
- **⑭ r270（2026-10-11 判读 + 修复，✅ **实机关单**）—— 战神死一堆后「有计数但复活选项不弹」= 满员挡板把弹层整层挡死**：
  - **✅ 实机判读（2026-10-11，`logs-archive/godot__20261011-200415__r272.log`，47881 行，用户「测了一下，应该没问题」）⇒ 契约全中，关单**：
    无尽战神召满 12 → 战神死亡 ⇒ **满员弹层出现 4 次**（L27217/32466/38824/42346：
    `回合开始可召唤，弹出选择: 选项=4→3→2→1 个 …（满员：只提供复活选项）`——选项数随尸体被逐个复活递减）；
    **`已复活战神` ×8**（复活计费/退回链路照常）；满员且无尸体时 `party-full(12>=12, 无可复活尸体)` 不弹（新细分 reason 生效）；
    **`turn loop died` 0 / `忽略切人` 0**，5 场战斗全程正常；预设表 Phase A 回归锚点正常（`预设表: 未配置…`）；
    `[ERROR]` 仅 3 条 NRE 全第三方（GensokyoSpire 血条 ×2、§待查-6 已知）+ 2 条 mod 分支告警 ⇒ **无我方新增**。
  - **症状**（用户原话）：「打到后面战神死了一堆，我还有计数但是没有弹出复活战神的选项」。
  - **判读**（`logs-archive/godot__20261011-164350__r269.log`）：无尽战神花 12 召 10 个（L10158，计数 20→8）⇒
    战斗内玩家 2→**12（满员）**；之后 9 条 `战神已死亡（**尸体留场**，绑主可在自己回合开始复活它）`（R9 数据层全对，
    `已复活过=0`）；但每回合 `本回合不弹召唤选择: reason=offer, 战斗内玩家=12` ⇒ 弹层被挡。
  - **真因**：R9 设计 = **尸体留场**（仍在 `state.Players` 占名额），而弹层判据
    `combatPlayers >= MaxLocalPlayerCount`（12≥12）**一票否决整个弹层** —— 复活选项明明已组进 options
    （可用 8 ≥ 复活成本 1/2），被满员挡板殃及。⇒ 复活是把尸体**回填**、**不新增席位**，不该受满员限制。
  - **修法**：满员时把弹层**过滤成只剩复活项**（`FilterOfferable`，两处同口径：回合开始判据 + 延迟一帧弹层前
    重算满员再过滤）；四档/无尽/九连环会新增席位 ⇒ 满员时照旧挡（D18 补满口径不变）；顺带把 `skipReason` 细分
    （`party-full(…)` / `party-full-but-revive` / `already-offered-this-turn`）—— 原 `reason=offer` 是兜底文案，
    **掩盖了真实原因是满员**（诊断坑）。
  - **复测契约**：再现场景（无尽召满 12 → 让战神死几个）⇒ 回合开始应弹
    `回合开始可召唤，弹出选择 …（满员：只提供复活选项）`，选复活 ⇒ `已复活战神: …`；满员但无可复活尸体（或买不起）⇒
    `party-full(…, 无可复活尸体)` 不弹；不满员时一切照旧。marker = `2026-10-11-r270`。
- **⑬ r268（2026-10-11 落地，✅ **实机关单**）—— 用户复测「锻造器还是不会自动选」⇒ 真因 = 锻造屏走的是**另一个协调器**：
  补丁 mod `HextechRunesLocalCoopFix` **v1.2.3**（⑧a/⑧b），主 mod 本轮**只升 marker（r268）不改行为代码**：
  - **症状/判读**：用户「商店瓦库买海克斯符文属性锻造器还是不会自动选」。
    日志 `logs-archive/godot__20261011-145339__r267.log`：确实买到（L10475 `relic=RANDOM_FORGE_SHOP_RELIC, gold=250`），
    但**我方 ③/⑤ 锚点一条都没有**（`将为该席位创建符文界面` / `符文界面已标注归属` / `符文界面选完即关闭` 全缺；
    对比 `godot__20261011-132615__r265.log` 三条齐全）⇒ **那屏根本没走我们挂的那条链**。
  - **真因（反编译实现变体 `lib/0.111.0/HextechRunes.dll`；根目录的 `HextechRunes.dll` 只是加载器）**：
    屏幕类只有一个（`HextechRuneSelectionScreen`），但**两个协调器各建各的屏** ——
    `HextechRuneSelectionCoordinator`（③a/③b 挂的就是它，逐席位符文选择）与
    **`HextechForgeSelectionCoordinator.CreateForgeSelectionScreenAsync`**（自带标题 `HEXTECH_FORGE_SELECTION_TITLE`，
    商店【随机属性锻造器】/ 锻造奖励 / 随机锻造发放）。锻造链 = `SelectForge → SelectLocalForge → CreateForgeSelectionScreenAsync`，
    **绕过 ③a/③b** ⇒ `_seatForNextScreen` 永远是 0 ⇒ ⑤ 不代答。⇒ ⑫ ① 的根因是**猜**的（已在其下加订正注）。
  - **修法（⑧a/⑧b）**：⑧a `SelectLocalForge` 前缀记席位；⑧b `CreateForgeSelectionScreenAsync` 前缀登记
    `_seatForNextScreen`（自带标题 ⇒ 只登记、不改标题）。协调器缺席只让 ⑧ 退化（`Missing` + WARN）。
    覆盖面顺带含 `obtain-random-forges` / `reward` 调用点。
  - **复测契约**：marker `2026-10-11-r268` + `INIT_OK: patched=[…9 项…]`；买锻造器 ⇒
    `将为该席位创建锻造选择界面…` → `符文界面已登记归属（锻造选择屏：自带标题…）` → `已登记自动作答` →
    `自动作答: … picked=…` → `自动作答完成（界面已关闭）`（界面一闪而过、不用手点）；真人买同款照旧自己点。
  - **已知未覆盖**：`HextechRelicOptionSelectionCoordinator.SelectRelicOption` 用**原版** `NChooseARelicSelection`
    （不是海克斯自己的屏）⇒ 不适用 ⑤；海克斯内部目前没有调用它（只有一行对外包装）。
  - **运维备注**：`build_all_mods.ps1` 部署补丁 mod **只拷 dll、不拷槽位 json** ⇒ 槽位清单里的 version 会一直
    停在装它时的旧值（本次 1.2.1 → 已手工同步成 1.2.3）。以后改补丁 mod 的 json 记得一起手工同步。
  - **✅ 实机判读（2026-10-11，`logs-archive/godot__20261011-152946__r268.log`，76945 行；用户原话「海克斯符文商店瓦库关单，亲测没问题」）⇒ 契约四条全中，关单**：
    `BUILD_ID … marker=2026-10-11-r268`、mod 列表 `(1.2.3)`（槽位 json 同步生效）、
    `INIT_OK: patched=[…9 项…], missing=[]`（⑧a/⑧b 在列）；锻造链 L14673 `将为该席位创建锻造选择界面…` →
    L14674 `符文界面已登记归属（锻造选择屏：自带标题、不改标题，但要能自动作答）` → L14676 `已登记自动作答` →
    L14677 `自动作答: options=[POCKET_FORGE, SCISSORS_FORGE, SILVER_PROTECTION_FORGE], picked=POCKET_FORGE, rule=取最左` →
    L14697 `自动作答完成（界面已关闭）`；L14678-14681 `GoldLost`+`RewardObtained`（归属=瓦库）→
    `瓦库商店自动买遗物成功: relic=RANDOM_FORGE_SHOP_RELIC, gold=250`（选择回流 `PurchaseRandomForge` 并 `ObtainSelectedForge` 成功）。
    我方异常族全 0（`turn loop died` / `托管出牌抛异常` / `本局不再让托管打` / `WaitForRemoteHextechChoice` / `交回真人` / `单击后仍未关闭`）；
    顺带回归：20 条 `临时席位（战神）已补一条本地动作队列`（10 席 × 2 场，r266 正常；那句
    `Tried to get local action queue` 只出现在**我们自己的 INFO 文案**里，不是异常）、火堆 `option=SMITH`（BUG-27 正常）、
    `[ERROR]` 128 条 = 第三方 newsanguo 资源缺失 127 + Manosaba 分支告警 1、4 条 NRE = `§待查-6` 已知 ⇒ **无我方新增**。
    ⚠ 这一局没出现普通符文屏（③/⑤ 每幕符文链本轮未被复测，r265 已验过）；**上一轮 ④ 附魔两条规则仍未实机验**。
- **⑫ r267（2026-10-11 落地，⭐ 待实机）—— 用户实机报的一批三件**：
  - ⚠ **订正（r268）**：下面 ① 的根因**是猜的、且修不到这个 bug**（用户复测"还是不会自动选"）——
    真因见上一段 ⑬（那屏走的是**另一个协调器**，压根不经 ③b）。③b 的解耦本身保留。
  - **① 海克斯符文兼容补丁 v1.2.2：修「瓦库买了**随机属性锻造器**后不自动选符文」**：
    根因 = ③b 里"登记席位归属"与"改标题"**耦合** —— 守卫 `if (owner == null || titleOverride != null || !IsLocalCoopRun()) return;`
    让**自带标题**的屏（锻造选择 / 敌人 hex 预览）被提前 return ⇒ 连 `_seatForNextScreen` 都没登记 ⇒ ⑤ 不代答。
    修法 = 解耦（归属一律登记；只在 `titleOverride == null` 时才改标题），并加一条
    `符文界面已登记归属（自带标题的屏…）` 日志便于实机确认。
  - **② 删「按稀有度挑符文」**（用户实测：海克斯每次 3 个选项**稀有度一致** ⇒ 按稀有度挑没有区分度）：
    `WakuuRuneAutoAnswer` 的 `PickIndex`/`RarityRank`/`RarityName`/`EnsureRarityResolver` 整节删除，
    改为**取最左**（日志 `rule=取最左（3 个选项稀有度一致）`）。
  - **③ 附魔选牌规则表两条改「考虑 mod 卡池」**：涡旋 Spiral = **费用最高且 ≥2 > 能力牌 > 打击/防御**
    （其中**打击与防御优先级相当 ⇒ 同档随机**；用户 2026-10-11 当场澄清"后两者"指打击与防御）；
    特兹卡塔拉的余烬 TezcatarasEmber = **费用最高的牌 > 打击**。
    引擎侧扩了：`WakuuEnchantPredicate.Keyword`（id 含 STRIKE/DEFEND，复用 `WakuuCardId`）、
    `WakuuEnchantRuleEntry.RankGroup`（同档分组，默认 -1 = 独占一档 ⇒ 旧行为不变）、
    `WakuuEnchantSort.Random`（纯逻辑确定性打乱 + 运行时 `Random.Shared.Next()` ⇒ 可单测的真随机）。
  - **复测契约**：① 瓦库在商店买锻造器 ⇒ 界面弹出后**自动选**（日志见
    `符文界面已登记归属（自带标题的屏…）` + `瓦库符文界面自动作答: … rule=取最左…`）
    —— ⚠ **① 这条已被 r268 ⑬ 取代**（真正的锚点是 ⑬ 那组；③b 的这条锚点在锻造链上根本不会出现）；
    ③ 打一场带这两张附魔的牌 ⇒ 涡旋选**费用最高且 ≥2** 的牌（没有则从"能力牌/打击防御"里随机）；
    余烬选**费用最高**的牌。启动 marker = `2026-10-11-r267`（已被 **r268** 取代，见 ⑬）。
  - **⏳ ③ 附魔两条规则的验证口径（用户 2026-10-11 拍板：改**被动验证**）**：这两条附魔在局里**很难主动凑到**，
    用户玩到了再把日志发来判读（选牌时的日志锚点在 `LocalWakuuEnchantPicker`，实机命中会有选牌记录）——
    不再列主动测试项；后续每轮收工时保留一条"附魔两规则仍待被动验证"的尾巴。
- **⑪ r266（2026-10-11 落地，✅ 实机关单）—— 露出下一层：给临时席位补一条「本地动作队列」（§待查-7 的修法）**：
  - **症状（r265 局日志 L16701）**：`托管出牌抛异常 … card=CARD.MOKOUMOD-IMMORTAL_SMOKE,
    error=Tried to get local action queue for nonexistent player with ID 4098!`
    —— 原版要为**战神**入队 `GenericHookGameAction`（`owner 4098`），而 `ActionQueueSet` 的队列是
    **开局**按 `runState.Players` 建的、之后按 `ownerId` 查（`GetQueue`）⇒ 战神不在那张表里 ⇒ 抛。
  - **修法（改道 > 跳过 > 不管，AE-24 口径）**：新 `LocalActionQueueSetBridge` 用反射在 `_actionQueues`
    里给它补一条队列（`ActionQueue` 是 private 嵌套类）；⚠ **必须照抄现存队列的状态位** ——
    `isCancellingCombatActions` 的字段初值是 `true`，直接 new 进去会让刚入队的 hook 被**静默取消**。
    摘战神时 best-effort `CancelNonExecutingActionsForPlayer`，队列本体交给原版 `ActionQueueSet.Reset()`。
    启动自检 `动作队列反射面已就绪…`（挂 `RuntimeCompatibilityCheck`，反射目标改名后能立刻看见）。
  - **复测契约**：召唤战神时应见 `临时席位（战神）已补一条本地动作队列…`（每席位一次）；
    再遇到"会入队 hook 的牌"（如不死之烟）**不再**出现 `Tried to get local action queue for nonexistent
    player`、**不再**进 `本局不再让托管打这张牌` 黑名单。启动日志应见 `动作队列反射面已就绪（…）`。
- **⑩ r264 + r265（2026-10-11 落地，✅ **实机关单**）—— 修「战神把 turn loop 打死」：临时席位 choiceId 记账改道 + 远端选择兜底并入临时席位 + 配套扫描工具 + 同族护栏（HoveredModelTracker）**
  - **症状**（用户原话）：「打一半不能出牌不能切人不能结束回合；瓦库和战神也不出牌」。
  - **真凶（我们自己的老坑换入口，不是第三方）**：原版 `PlayerChoiceSynchronizer.ReserveChoiceId` 按
    **玩家槽位索引**记账（`_choiceIds[runState.GetPlayerSlotIndex(player)]`），而战神**刻意不进**
    `runState.Players`（AE-1/AE-3）⇒ `GetPlayerSlotIndex` 返回 **-1**（接口注释原文 "or -1 if the player
    is not in Players"）⇒ `_choiceIds[-1]` 抛 `ArgumentOutOfRangeException`。实机爆点：第三方 GensokyoSpire
    的抽牌前钩子（Watcher「先见之明」→ BaseLib `ScryCmd` → `CardSelectCmd.FromSimpleGrid`）在**战神回合**
    调它 ⇒ 异常冒穿 `CombatManager.StartTurn` ⇒ 原版打 `turn loop died … the combat is stuck until the
    room is restarted` ⇒ **整场战斗冻结**（日志 `logs-archive/godot__20261011-121808__r263.log` L18279）。
  - **修法（两层，缺一不可）**：① 新补丁 `PlayerChoiceSynchronizerTemporarySeatChoiceIdPatch` ——
    `ReserveChoiceId` **与** `GetChoiceId` 两处同进同退，临时席位改走 `TemporarySeatChoiceIds` 独立序列；
    ② `PlayerChoiceSynchronizerRemoteChoiceFallbackPatch` 闸门并入临时席位（否则修完 -1 只是把"抛"换成
    `WaitForRemoteChoice` **死等**：战神不是 `IsMe` ⇒ `ShouldSelectLocalCard` 为 false）。
  - **配套交付**：`Scripts/Tools/scan_slot_index_consumers.py`（+ 基线 `Scripts/Tools/baselines/…`）扫
    `sts2src` 全部 `GetPlayerSlotIndex` 消费点（分类 + 战斗内/外可达性 + 基线比对），已挂 `preflight` **G2b**；
    首跑：35 个消费点 / 「当索引用」27 个 / **战斗内只有 2 处**（`PlayerChoiceSynchronizer` 已补、
    `HoveredModelTracker` 实机未触发、**r265 已加护栏**），其余 23 处都在房间/地图/奖励流程（战神不在场）。
  - **r265（用户拍板"先加防护我再测"）**：`HoveredModelTrackerForeignSeatGuardPatch` —— 三个方法
    （`OnPlayerStateChanged` / `OnPlayerStateRemoved` / `GetHoveredModel`）对结构外成员直接跳过/返回 null
    （判据 `ForeignSeatGuardPolicy`，有单测；跳过安全：悬停是纯表现层，没有"计数必填项"语义）。
    ⚠ `EventCombatSynchronizer.ReadyToEnterCombat` **刻意不加**：它的 `_states[slot]` 后面有
    `_states.All(s => s != null)`（全员就位才进战斗），跳过会永远不满足 ⇒ **反而卡住事件**。
  - **复测契约**：战神回合遇到"占卜/选牌"类第三方效果 ⇒ 出现
    `临时席位（战神）的 choiceId 已改道到独立序列…`（每个席位一次）+ 可能一条
    `临时席位（战神）远端选择无人作答，已按空结果放行…`；**不再**出现 `turn loop died`、
    不再卡死、真人「结束回合」/切人正常。若那条 `远端选择…按空结果放行` 频繁出现说明第三方在战神回合
    频繁要选择（功能上被跳过，属已知限制，可后续按 §BUG-23 的"功能完整版"做）。
  - **✅ 实机判读（2026-10-11，r265 局，用户自测「应该没问题」；日志 `logs-archive/godot__20261011-132615__r265.log`）**：
    `turn loop died` **0 次**（上一局 1 次 ⇒ 整场废掉）；`choiceId 已改道到独立序列` **2 次**
    （seat=4098 / 4105，其中 4105 那次正对应上一局 L18278「战神回合开始 ⇒ turn loop died」的同一场景）
    ⇒ **修复直接命中**；`跳过悬停模型追踪` 0 次（与预判一致：战神不进 PeerInput 状态表）；
    `远端选择无人作答` 0 次（兜底那层本局没被用到，备用）；`被重复选中` 0；`瓦库放弃` 3 条（每席位/回合一条）；
    `回收滞留在出牌区`/`托管出牌抛异常` 各 1（r263 契约正常）；`[ERROR]` 仅 3 条（Manosaba 分支不兼容 + 2 条
    `NHeavyBluntVfx.PlaySequence` = §待查-6 已知）；回合号一路推进到 5、退局 `会话/退局复位自检: 无残留`。
  - **🔎 同轮新发现（登记 §待查-7，已由 r262 黑名单兜住、**不卡死**）**：L16701 出现
    `托管出牌抛异常 … card=CARD.MOKOUMOD-IMMORTAL_SMOKE, error=Tried to get local action queue for
    nonexistent player with ID 4098!` —— 原版 `ActionQueueSynchronizer` 要为**战神**入队一个
    `GenericHookGameAction`（`owner 4098`），而战神没有自己的**本地动作队列** ⇒ 抛。
    这是 r264 修掉 `ReserveChoiceId` 之后**露出来的下一层"结构外成员"缺口**（同一个族：
    原版按玩家取"每玩家一份"的东西）；当前表现 = **这张牌本局托管不再打（真人可自己打）+ 残影被回收**，
    不影响回合推进。修法待下一轮：要么给临时席位补一条本地动作队列，要么在 `ActionQueueSynchronizer`
    取队列失败处对结构外成员兜底（"跳过这条 hook"）—— 需先读清 `GetReadyAction`/队列账本再动。
  - **坑已沉淀**：references **AE-24**。
- **⑨ r263（2026-10-11 落地，⭐ 待实机）—— 修 r262 判读出的两件：① 回合死锁（放弃名单单槽键）+ ②「不死之烟」残影（黑名单生效但不收残骸）**：
  - **① 回合死锁 = r261「放弃名单」的设计缺陷（**我们自己的 bug**）**。r262 日志铁证（`logs-archive/godot__20261011-115325__r262.log`
    L70000~L80901）：4103 反复选 `CARD.LEX_NINJA2_CARD_ITALY_SILVER_SLASH`（**382 次**）、
    4104 反复选 `CARD.SLUGCAT_THE_SPIRE_II_CARD_RAINWORLD_RIVER_FISH`（**381 次**），每轮都打
    `瓦库放弃「需要目标但解析不到目标」的牌…`（全场 **765 条**）、`round` 永远停在 3、单轮 `ms=8`（紧循环）；
    同场 `玩家主动结束回合时检测到仍有可出牌角色，不触发全员结束` **16 次** ⇒ **真人点「结束回合」被挡**。
    **两个独立缺陷叠加**：(a) 名单是**单槽键** `(席位, round)` —— 两个战神交替进托管 ⇒ 互相**清空**对方的记录 ⇒ 无限重选；
    (b) "可出牌"有**两套口径** —— 喂给大脑的候选集已过滤，而看门狗调度 / 战神空闲收口 / 真人结束回合门
    用的是裸 `card.CanPlay()` ⇒ 永远认为"这一席还有牌可出" ⇒ 战神永不 ready。
    **修法（三件一起）**：① 名单**按席位分槽**（`Dictionary<ulong, MissingTargetSkipSlot{Round, Cards}>`），换 round 只重置**该席位**那一槽；
    ② 抽出**唯一的托管口径** `LocalWakuuRelicRuntime.HasHostedPlayableCards(player, round)`
    （= `CanPlay()` **且**不在"本局不让托管打"名单、**且**不在该席位本回合"不打"名单），
    **6 处消费点全部改用它**：回合开始 hook / 看门狗调度 / 看门狗进作用域 / 战神空闲收口
    （`TryEndIdleWarGodTurns`）/ 真人「结束回合」门 / 瓦库无牌可出时的自动切人；顺手把 WARN 去重（765 → 每席位/回合一条）；
    ③ **循环闸（双保险）**：同一席位/回合里**又被选中**那张已放弃的牌 = "从候选集剔除"没生效 ⇒ 直接收手
    （`WakuuPlayQueuePolicy.ShouldContinuePlayLoopAfterMissingTargetAbstain`，有单测），宁可少打一张也不卡死整回合。
  - **② 「不死之烟」残影**：r262 的异常黑名单**生效**（`托管出牌抛异常 ⇒ 本局不再让托管打这张牌: card=CARD.MOKOUMOD-IMMORTAL_SMOKE,
    error=Index was out of range`）但**首次尝试留下的卡面节点仍在出牌区**（黑名单只防重试、不收拾残骸）
    ⇒ 用户仍看到"停留屏幕中间"。**修法**：出牌异常后 `CleanupStuckPlayAreaNode` —— 从
    `NCombatUi.GetCardFromPlayContainer` / `PlayQueue.GetCardNode` 找残影 ⇒ 淡出回收（共用
    `CardNodeVisualCleanup.FadeOutAndFree`）+ 只在"那张牌属于**受控席位**"时延后一次手牌 UI 重建
    （复用 `LocalMultiControlRuntime.ScheduleDeferredCombatUiRefresh`，天然满足 r111"别在出牌途中整表重建"）。
  - **复测契约**：遇到这类牌 ⇒ `瓦库放弃…` **每个席位/回合各一条**（不再 765 条）、`round` 能正常推进、
    **不再**出现 `玩家主动结束回合时检测到仍有可出牌角色`、战神照常 `战神已自动结束回合`、真人「结束回合」随时可用；
    `托管出牌抛异常…` 之后**出牌区不再留那张卡面**、手牌里那张牌仍可**真人手动打出**、合体自检 `缺 holder=0 张`。
  - **坑已沉淀**：references **AE-23**（名单/熔断类状态必须按席位分槽 + "谁有牌可打"全链同一口径）。
  - r262 同轮已确认 OK：`战神视觉节点摘除失败（节点已隐藏…）`（r262 生效）/ `R12 战后奖励遇到临时席位` = 0 /
    `摘除战神失败` = 0 / **无尽战神 12 点已生效**（5 场各开一次 `花费=12`；⚠ 上限已到 12~14 ⇒ 每场都开得起，涨价是否够待下次体感）。
- **⑧ r262（2026-10-11，r261 实机判读 + 用户口径）—— ① 【无尽战神】整包价 8 → 12 ② 托管出牌抛异常的牌记入「本局别让托管打」名单 ③ 战神视觉节点「先藏再摘」**：
  - **① 【无尽战神】整包价 8 → 12**（用户拍板；原话「**无尽战神强完了，需要上调消耗的计数**」）：
    常量 = `HellwarSummonPolicy.EndlessWarGodBundleCost`（**改这一处即可**；开启判据 / 选项文案 / 打日志都读它）；
    依据 = 它一次把空位**补满到 12 席**（本局实测 `预计=10 实际=10 个, 花费=8`）而本场预算通常 7~11
    ⇒ 12 点 = "前期开不出、后期才开"，与【终极九连环】（12 点换 9 个先古）同价。
    ⚠ 口径差别仍在：九连环扣**计数上限本身**（永久、不补满），无尽只扣**本场预算**（每场都还有机会）。
    单测同步（`EndlessWarGodBundleCost == 12` + 开启判据的 11/8 两组反例 + 不可开原因文案）。
  - **② 托管出牌抛异常 ⇒ 本局别让托管打这张牌**（用户报「**依旧会出现空气牌** + 后台不知道谁打的**色素细胞**
    （沙耶 mod 的千变万化牌）会**卡在屏幕中间**」）：
    - **日志铁证**：`瓦库评分出牌: player=4100, round=2, card=CARD.FIGURE_SAYA-PIGMENT_CELL_CARD`
      紧接 `瓦库选择器作用域异常退出 … error=Index was out of range`（**第三方卡效果内部抛**）
      ⇒ 出牌链半途断掉 ⇒ **卡面残留在出牌区正中央**；牌数据还在手牌 ⇒ 之后统计/【合体】按数据搬走它时，
      手牌里就出现**没 holder 的"空气牌"**（本局合体自检实测 `手牌堆=10 / UI holder=9` 的那一张就是它）
      ⇒ **两个现象同一个根因**，也解释了"空气牌依旧出现"（r261 修的是"需要目标却没目标"那一类，
      这一类是**牌自己抛异常**，目标类型正常 ⇒ r261 的守卫一条都没触发）。
    - **修法**：出牌循环里记 `attemptedCard` + 外层 catch 里 `MarkCardUnplayableByHosted`
      （按 `card.Id` 记名单 + WARN，**本局只报一次**）⇒ `ResolveAutoplayHand` 把它从大脑候选集剔除
      ⇒ 不再重试、不再产生残留卡面；**真人不受影响**（牌留在手牌，可自己打）；名单按**局**清
      （`ResetTakeoverFallbackState`）。
    - **复测契约**：再遇到这类牌 ⇒ 出现 `托管出牌抛异常 ⇒ 本局不再让托管打这张牌（真人可自己打）: card=…`，
      且该牌**不再**卡在屏幕中间 / 后续合体自检 `缺 holder=0 张`；真人手打这张牌仍正常。
  - **③ 战神视觉节点「先藏再摘」**：`RemoveCreatureNode` 在第三方布局补丁那链上抛（本局 **19 次**）
    ⇒ 原来紧随其后的 `Visible=false` 永远执行不到 ⇒ 画面留一个"还站着的空位"。
    改成**先把节点设为不可交互 + 隐藏**、再调 `RemoveCreatureNode`（失败只 WARN；数据层早已摘干净）。
  - **门禁**：构建 0 警告 0 错误 → 单测 **1140 全绿** → `preflight -Deploy` 全 PASS（S7 目标基线不变）；
    部署位 marker **`2026-10-11-r262`**（sha256 `3599a55de23f…`）。
  - ⚠ **工作流提醒**：单测工程是**按路径引用主 mod 的 DLL**（`$(MainModDll)`）⇒ 改完 mod 必须
    **先 `dotnet build` 主工程、再 `dotnet test`**，否则测试跑的是旧 DLL（本轮踩过：先跑 test 看到"价格还是 8"）。
- **⑦ r261（2026-10-11，r260 实机判读 + 用户反馈）—— ① 托管不再打"需要目标却没目标"的牌（第三方自定义 TargetType 根因）② 战后摘战神改「数据先摘、视觉节点后收」**：
  - **① 🔴 用户报的"抱猪卡在屏幕中间" 与 "合体空气牌" 是同一个根因，已定位并修**：
    - **现象**：用户「一个玩家打出了猪猪 mod 的抱猪，但是这张牌卡在屏幕中间；这张牌需要选择一个目标盟友」。
    - **根因（日志铁证）**：`YUWANCARD-PIG_HUG`「抱猪」用的是**第三方自定义 `TargetType`**
      （日志里 BaseLib 给该枚举生成 KeyGenerator：`starting value 10`），我们大脑的目标解析
      `ResolveTarget` 的 `_ => null` 覆盖不到它 ⇒ `瓦库评分出牌: player=4096, card=…PIG_HUG, target=无`
      ⇒ 原实现把 `target=null` 直接交给 `CardCmd.AutoPlay`（战神走 inline 路径）
      ⇒ **出牌流程卡住、卡面停在屏幕中间**（且**没有**任何 `PlayCardAction` 记录 ⇒ 确认没走完）。
    - **同一根因的第二症状**：随后【合体】把这牌的数据搬进真人手牌时，它的节点还留在 Play 区
      ⇒ 手牌里**没 holder** ⇒ **合体自检实测**：`手牌堆=10/UI=10, 本次搬运=5（缺 holder=1）:
      AFTERIMAGE[有] ADRENALINE[有] FOOTWORK[有] NOXIOUS_FUMES[有] **PIG_HUG[holder=无, 在容器=否, 节点在树=否]**`
      ⇒ 就是用户说的"没样子但占位、切人才显示"的空气牌 ⇒ **探针一次就分清了成因（是 (a) 缺节点，不是 (b) 没渲染）**。
    - **修法**：纯函数 `WakuuPlayQueuePolicy.ShouldAbstainForMissingTarget(目标为空, 目标类型需要目标)`
      + 运行时白名单 `RequiresTarget`（`Self/None/AllEnemies/AllAllies` 之外**一律**按"需要目标"处理 ——
      关键在"之外都算" ⇒ **第三方自定义 TargetType 也落进来**）：需要目标却没目标 ⇒
      **放弃这一张**（牌留在手牌、真人可自己打）+ 去重 WARN
      `瓦库放弃「需要目标但解析不到目标」的牌…`，并记入"本回合不再考虑"名单（否则大脑每轮又选中它、空转配额）。
    - **复测契约**：再遇到这类牌 ⇒ 日志出现上述 WARN（**不再**有卡面卡在屏幕中间）；牌仍在手牌且**能手动打出**；
      合体自检 `缺 holder=0 张`。
  - **② 战后摘战神：数据先摘、视觉节点后收**（承接 ⑥ 的第三方布局补丁问题 —— 那次的逐项容错只保证
    "不中断别的战神"，**这一个自己还是没摘干净**：r260 实机再次出现 `R12 战后奖励遇到临时席位… player=4097/4098`）：
    原来"先 `NCombatRoom.RemoveCreatureNode`（节点）→ 再摘数据"的写法，一旦第三方在节点链上抛，
    **数据摘除也一起没执行** ⇒ 战神留在 `CombatState.Players` 直到发奖。现在：
    ① 数据（必须成功、不包 try；**节点引用提前抓**）② 会话/登记/战斗态（各自 try）
    ③ 视觉节点（best-effort，失败只 WARN —— 数据已干净，最多画面残留）。
    **复测契约**：战后**不该**再出现 `R12 战后奖励遇到临时席位`；即便第三方仍抛，也只出现
    `战神视觉节点摘除失败（数据层已摘干净…）` 这一条 WARN。
  - **门禁**：构建 0 警告 0 错误 → 单测 **1139 全绿**（+4）→ `preflight -Deploy` 全 PASS（S7 目标基线不变）；
    部署位 marker **`2026-10-11-r261`**（sha256 `b545c9e2d53f…`）。
- **⑥ r260（2026-10-10，r259 实机判读 + 用户反馈，⭐ 待实机）—— 战后清理逐项容错 + 合体「空气牌」自检探针 + 合体日志补混抽可用性**：
  - **① 🔴 战后清理逐项容错（真因来自 r259 日志）**：`CleanupCombat` 原本用**一个 catch 包整圈**，
    而 `RemoveWarGodFromCombat` 链上会调 `NCombatRoom.RemoveCreatureNode` —— **第三方 NinjaSlayer 的
    `YamotoKokiAllyLayoutPatch`（`PositionPlayersAndPets` 后置）在那条链上抛
    `InvalidOperationException: Sequence contains no matching element`** ⇒ 循环**整体中断** ⇒
    只摘掉 4096、**4097 留在战斗里**（一路留到发奖时刻）。修法 = **逐项 try/catch**（谁炸谁记，继续摘剩下的）。
    ⭐ 同场 **R12 排除闸门立功**：`R12 战后奖励遇到临时席位并已排除（正常不该出现，请查上游清理）:
    入口=combat-still-has-temporary-seat, player=4097` ⇒ 残留战神**没拿到奖励、也没被交回原版**
    （r258 加那道闸门的价值兑现：上游一漏，闸门兜住）。
  - **② 🔍 合体「空气牌」自检探针**（用户反馈：「与战神合体有时候会出现空气牌，这张牌没有样子但确实占了位置，
    切人刷新才显示出这张牌的样子」）：合体搬完牌**下一帧**核对每张搬运牌的
    `holder / 在容器 / 节点在树 / 可见 / 缩放` + **手牌堆张数 vs 手牌 UI holder 张数**；
    缺 holder ⇒ WARN（`合体自检`），否则 INFO。
    为什么要"先探针"：现象对应**两种成因、修法相反** ——
    (a) 手牌 UI 少建了节点（要在合体链路里补建/补挂）vs (b) holder 与节点都在、只是**卡面没渲染**
    （补节点毫无作用，得走"重刷该节点视觉 / 第三方美术链"）。
    ⚠ **只观测、不自动修**：出牌途中整表重建会打断出牌链（r111 实测教训，见 `LocalMultiControlRuntime` 手牌对账注释）。
    已排除的两条现成嫌疑：我们的两条手牌节点守卫本场**一次都没拦**（`手牌视觉守卫` / `选牌守卫` = 0 条）、
    手牌 UI 与数据对账也**没报差异** ⇒ 偏"节点在、卡面没渲染"（本环境有第三方 `SayaArtApi/SayaApiBridge` 介入卡面解析）。
    **复测契约**：再合体打一次 ⇒ 看 `合体自检`：若 `缺 holder=N 张`（N>0）⇒ 走 (a) 修路；
    若 `节点齐全` 而画面上仍空白 ⇒ 走 (b)（第三方美术链 / 视觉重刷）。
  - **③ 合体日志补 `（混抽可用性：瓦库抽牌堆=N 张, 我方=M 张）`**：混抽/混弃的日志只在"真的搬了牌"时才打
    ⇒ 没这一行会把"瓦库抽牌堆本来就空"误读成"混抽没生效"（r259 那局 `合体混抽` / `合体混弃` 各 0 条正因此，**不是 bug**）。
  - **门禁**：构建 0 警告 0 错误 → 单测 **1135 全绿** → `preflight -Deploy` 全 PASS（S7 目标基线不变）；
    部署位 marker **`2026-10-10-r260`**（sha256 `60ec82b4e073…`）。
- **⑤ r259 用户三条口径（2026-10-10，代码落地，⭐ 待实机）—— R10 修订 + R13b【合体】覆盖战神 + 当回合打牌不做**：
  - **① R10 修订：肉盾资格改"下一回合"起生效**（用户原话：「战神刚出来有 1 回合无实体保护，同时可以替代
    本体玩家承受伤害，作为肉盾疑似有点太强了，考虑以后肉盾效果在战神出场的下一个回合再挂上去」）：
    判据 = 纯逻辑 `HellwarSummonPolicy.IsGuardEligible(spawnRound, currentRound)` = `currentRound > spawnRound`（+4 条 TestCase）。
    **为什么用 round 比而不是加状态机**：`CombatState.RoundNumber` 本来就是按**"玩家回合 + 敌方回合"一整个 round**
    递增的（敌方回合结束切回玩家侧时 +1，`CombatManager.cs:1878`）⇒ **天然等于"下一个玩家回合起"**，
    并且**每个战神各算各的**（死了再召一个新的，同样要等一回合，不需要额外复位）。
    登记项 `Registration` 新增 `SpawnRound`（**召唤 + 复活两条路径都刷新** —— 复活同样补【无实体】）；
    资格过滤落在 `FindGuardCreature(绑主, 当前 round)` ⇒ **Power 的两个钩子判据一行没改**
    （`hasAliveGuard` 自动升级成"有**已满一个回合**的活战神"）；Power 照旧在召唤时挂（幂等、每场一次）。
    ⚠ **"挂上 ≠ 在挡"** ⇒ 新增锚点 `战灵守卫已生效（自本次受击起战神替绑主承伤）`（真正开始挡的那一刻，每场一次）+
    挂载日志改成 `round=…, 在册杀手=seat…(可承伤 / 出场回合 N，下一回合起可承伤)`。
    **复测契约**：召唤战神当回合（含紧接的敌方回合）⇒ 绑主**照常掉血**、**无** `战灵守卫已生效`；
    到**下一个玩家回合**后一次受击 ⇒ 出现 `战灵守卫已生效`，此后绑主 0 伤害、战神吃一半（= 老 R10 表现）。
  - **② R13b：【合体】覆盖战神**（用户口径：「【合体】覆盖战神」，补上 R13 当时没含的那半）：
    ① `LocalWakuuDaddyMergeCard.OnPlay` 的席位判据取**并集**（`IsWakuuEnabled` ∪ **临时席位**）；
    ② `WakuuDaddyCombatState.TryGetMergedWakuu` 补**在册战神**兜底查询 —— 战神刻意不在 `runState.Players`
    ⇒ 老循环**永远查不到它**（"结构外成员"家族老坑，见 references AE-15/AE-16）⇒ 本回合的**混抽/混弃**对战神也成立；
    手牌 / 能量转移本来就按 `Player` 实例走，不需要改。
    **复测契约**：把【合体】点在**战神**上 ⇒ 日志 `合体：… ← 瓦库 409x(战神)`（不再走
    `合体目标不是瓦库托管席位，效果未发动`），战神手牌/能量归你，本回合抽/弃牌出现
    `合体混抽：本次从瓦库抽牌堆移入 N 张` / `合体混弃：N 张弃牌改归瓦库 409x 的弃牌堆`。
  - **③ 「战神当回合就能打牌」= 不做**（用户口径：「只能说不强求，难做就不做了」）⇒ 正式从备选里划掉。
  - **④ R10 平衡：口径反转（2026-10-31 用户实测**撤回"偏脆"**）= **战神强完了（太强）**⇒ 要议的是**削弱**方向；
    具体削法（扣血 / 扣计数 / 收窄守卫转移量）**待用户给口径** ⇒ 本轮不动任何阈值。
  -     **门禁**：构建 0 警告 0 错误 → 单测 **1135 全绿**（+4）→ `preflight -Deploy` 全 PASS（S7 目标基线不变）；
    部署位 marker **`2026-10-10-r259`**（sha256 **`a3f24c929680…`** —— ⚠ 订正：首次部署是 `e8d2c223c4e4…`，
    之后把两个工作区遗留 CRLF 的源文件规范成 LF 又重新构建部署过一次 ⇒ 以此值为准）。
- **④ Phase 5 收尾（R12 战神无战斗奖励 + R13 我挡/你攻覆盖战神）—— ✅ 实机关单（r258）**：
  - **R12 三入口显式闸门**：把"战神不参与战斗奖励"从"**靠它不在 `runState.Players` 里**"（= 上游
    `CleanupBeforeRewards` 摘人的**副作用**）改成**显式判据**：纯策略
    `CombatRewardMergePolicy.ShouldExcludeFromRewards(isTemporarySeat)`（+3 条单测），喂给
    ① `ShouldIncludeInMergedSet`（合并展示集）/ ② `ShouldOfferBackToVanilla`（交回原版那条分支）
    / ③ `LocalRewardMirror.IsMirrorableSource` + `SelectTargets`（奖励镜像）；
    另给两条**兜底路径**（`RewardsSetPatch` / `RewardsCmdPatch` 的 Prefix）加同款闸门：
    临时席位的**奖励集 / 战后奖励请求直接丢弃**（`Task.CompletedTask`）+ WARN。
    ⚠ **为什么必须显式**：`IsLocalSeat(4096) == false`（战神不在本地席位表）⇒ 它一旦进候选集就会被当成
    **第三方席位**走"交回原版流程"分支 ⇒ 真人奖励界面上出现「[未知角色]」的奖励（2026-09-25 那次实机的同款现象）。
    **异常锚点**（正常永远静默）：`[地狱战神] R12 战后奖励遇到临时席位并已排除（正常不该出现，请查上游清理）: 入口=…`。
  - **R13【你攻】—— 白得 + 补自证锚点**：战神在瓦库名单里（H2）⇒ 走**同一个大脑** ⇒ 集火天然覆盖它；
    本轮给 `WakuuDaddyCombatState.TryGetFocus` 加 `consumer` 参数（两个大脑都传自己），
    临时席位消费集火时打 **`[瓦库的爹] 你攻生效: player=409x(战神) 本回合按集火打 target=…`**
    （每回合每人一条，避免每张牌刷屏）—— 这就是 R13 的**可验证证据**。
  - **R13【我挡】—— 白得 + 日志说清**：`TargetType.AnyAlly` 的池子 = `CombatState.PlayerCreatures`
    （临时席位在里面）⇒ **战神天然可选、判定一行都不用改**；本轮把日志补成
    `[瓦库的爹] 我挡：409x(战神) 本回合受击伤害转由 … 承受（一半）` + 链式重定向边界说明
    （目标是本人的战神且主人挂 R10 守卫时，两条重定向在 `Hook.ModifyUnblockedDamageTarget` 里**链式**，
    最终落点按原版监听器顺序，不会死循环）。
  - ⚠ **边界登记（待你拍板）→ ✅ 已拍板并实装（r259，见下面 ⑤②）**：当时**【合体】不覆盖战神**
    （战神**不在** `IsWakuuEnabled` 名单里，它只是"托管席位"）⇒ 点在战神身上**效果不发动**（只抽 1 张）；
    用户 2026-10-10 口径 = **要覆盖** ⇒ r259 已把席位判据改成并集 + 补在册战神兜底查询。
  - **复测契约（R12/R13）**：① 打完一场有战神的战斗，日志里**每个角色一行** `角色独立奖励已生成(OfferRoomEnd): player=…`
    **只出现真人席位**（无 409x），且**没有** `R12 战后奖励遇到临时席位` 这条 WARN；
    ② **全程无**「[未知角色]」奖励（R12 的最终外观判据）；
    ③ 甩【你攻】点一个敌人 ⇒ 该敌方回合后瓦库出牌时出现 `你攻生效: player=409x(战神)`（证明战神也吃集火）；
    ④ 甩【我挡】点一个**战神** ⇒ 日志出现 `我挡：409x(战神)…`，此后该战神挨打时伤害转到你身上（一半）。
  - **✅ 实机关单（2026-10-10，r258，`logs-archive/godot__20261010-210702__r258.log`；分支已提交 `f569fed`）**：
    **四条契约全部命中** ——
    ① **R12**：3 场战斗共 6 行 `角色独立奖励已生成(OfferRoomEnd)`，**全是 326/327 两个真人**（**无 409x**）；
    `R12 战后奖励遇到临时席位` **0 条**（闸门静默 = 正常）；**`未知角色` 0 次**（外观判据达成）。
    ② **R13【我挡】**：`[瓦库的爹] 我挡：4096(战神) 本回合受击伤害转由 …326 承受（一半）` **×2**
    ⇒ 用户真的点了战神 ⇒ **我挡对战神生效 = 实机确认**。
    ③ **R13【你攻】**：`你攻：…集火目标已登记 target=CUBEX_CONSTRUCT` 后 6 行出现
    `你攻生效: player=4096(战神) 本回合按集火打 target=CUBEX_CONSTRUCT` **×1** ⇒ **你攻对战神生效 = 实机确认**。
    ④ 异常族全 0（`看门狗重启失败` / `nonexistent player` / `### Exception ###` / `LocException`）。
    同场顺带复核：**R10 继续生效**（`战灵守卫` 挂载 ×2；第三方 `RichtenDmg` 目标计数 = 绑主 326 **0 次** /
    战神 4096 **4 次** / 无守卫的真人 327 **4 次**）；3 次召唤（Rare×2 + Common×1）、战神出牌 10 次、收口 2 次、
    **0 死亡 0 复活**（短局，没走到 R9）。
    ⚠ 同场 NRE ×2：其中 1 条 = `§待查-6` 那条（**本轮已定性：跟原版牌 `UPPERCUT` 的命中特效绑定，与 R10/战灵召唤无关**），
    另 1 条 = Godot `NMuteInBackgroundHandler._Notification`（退场/切后台，非我们）。
- **③ Phase 4 生存规则 —— ✅ 全部落地（R8 + R9 = 实机关单 r256；R10 = 实机关单 r257）**：
  - **R10 挡伤 1/2（D4 = 玩家零伤害 + 战神吃一半，r257）—— ✅ 实机关单**：新 Power
    `Models/Powers/HellwarWarGodGuardPower`（挂在**绑主**身上，不可见 + 无 VFX）：
    `ModifyHpLostBeforeOsty` 返回 `amount / 2`（**绑主格挡结算之后**减半 ⇒ 绑主格挡照常生效）
    + `ModifyUnblockedDamageTarget` 返回**战神**（只有新目标掉血 ⇒ 绑主掉 0、战神掉一半；另一半消失 = 旧【我挡】语义）。
    ⚠ **刻意不做**"战神用自己的格挡"（§4 R10 原样口径；要加得再挂 `ModifyHpLostAfterOsty` + 一次性标记，抄【我挡】r229 写法）。
    **判据三条件**（纯逻辑 `HellwarSummonPolicy.ShouldGuardIncomingDamage` + 单测，**两条钩子共用**）：
    攻击伤害 + 打的是绑主本人 + **绑主名下还有活着的战神**（③ 是关键：没有承接者就**一点都不该改**，
    否则"减半了却没人承接"= 绑主白赚 50% 减伤）。挂载 = `EnsureGuardPowerAsync`（幂等；Power **不保存**是哪个战神，
    每个钩子当场 `FindGuardCreature(绑主 NetId)` 查 ⇒ 战神换人/死而复生/全死光都不用重挂；战斗结束自摘）。
    ⚠ **已知边界（别当 bug 修）**：`DieForYouPower`（Osty）是另一个重定向源，两源链式、先后看遍历顺序 ——
    若 Osty 先跑，本守卫看到 `target != Owner` 直接放行（退回原版 Osty 行为，无害）。
  - **同轮补的观测锚点**：`战神已死亡（尸体留场…）: seat=…`（每帧 tick；补上 r256 发现的"被敌人打死没有锚点"；
    复活成功复位标记 ⇒"死→复活→再死"各打一条）。
  - **✅ 实机判读（2026-10-10，r257 生效）**：守卫挂载 ×3（3 场战斗各一次，幂等正确）；
    **受击分布**（第三方 `[Oddmelt] [RichtenDmg] target=` 客观计数）：**绑主 326 = 0 次** /
    另一位真人 327（**无守卫**，对照组）= **19 次** / 三个战神合计 = **53 次** ⇒ **R10 真的生效**。
    战神托管链正常（出牌 62 次、收口 15 次），异常族全 0。新锚点 `战神已死亡` ×3（普通档 68 血，
    都死在最后一场、当时主人 `可用=0` ⇒ **复活选项买不起、按契约不出现**，不是 bug）。
    📌 **平衡点（2026-10-31 用户实测后口径反转 = 战神强完了/太强）**：早前"偏脆（接近一次性消耗品）"的
    说法**已撤回** —— 完整测过之后战神是**太强** ⇒ 要议的是**削弱**方向；可选调法留档：
    ① R10 转移量调小（1/3 或封顶）② 复活更贵/更难 ③ 计数消耗更狠 ④ 召唤整包价上调（先古档）。
    具体削法**待用户给口径** ⇒ 本轮不动阈值。
    ⚠ 另见 ⑤① 的 **R10 修订**（r259）：肉盾资格改**下一回合**起生效 —— 这条本身就是对"太强"的一次调整。
  - **复测契约（R10）**：召唤后日志 `已给绑主挂上【战灵守卫】（R10…）: 绑主=…, 当前守卫=seat409x`；
    敌人打绑主 ⇒ **绑主不掉血、战神掉一半**；绑主的**格挡照常先吃**（例：5 格挡 + 挨 20 ⇒ 战神掉 7.5）；
    **中毒等非攻击伤害不转**；**战神全死光**时绑主恢复原样挨打；异常族仍全 0。
- **（历史）Phase 4 首批 R8 + R9 —— ✅ 实机关单（r256，判读全绿，`logs-archive/godot__20261010-190410__r256.log`）**：
  - **R8 绑主死亡 ⇒ 战神立刻死**：做法 = **每帧 tick 兜底**（`HellwarWarGodFactory.EnforceSummonerBond`，
    挂在 `LocalMultiControlRuntime.TryAutoEndTurnForRelicControlledPlayer`，**刻意放在"当前玩家侧 / 队列出牌相位"
    两道门之前** ⇒ 绑主死在**敌方回合**也当场收）；`CreatureCmd.Kill(warGod, force: true)` + 登记项一次性标记防重入。
    ⚠ **订正设计文档 §4 R8 的原理由**：判负看 **`runState.Players`**（`CreatureCmd.cs:476`）而不是 `CombatState.Players`，
    战神刻意不进前者 ⇒ R8 **不是**"防挂机"的必需保障；它的价值是**一致性 + 反白嫖**（绑主死了，它的战神不该还给队友当肉盾）。
  - **R9 死后留场（D9）= 原生白送，一行都不用写**（Player creature 死亡不会被摘出战斗；`CombatManager.cs:804`
    会自动替死玩家 ready；敌人 AI 也排除死人）。**真正要写的只有复活**：
    - 入口 = **回合开始的召唤选择 UI 里多一项**「复活【X档】战神（消耗 N 计数）」（排在**最上面**）；
      成本 = **该战神原档位**的召唤成本（纯逻辑 `HellwarSummonPolicy.ResolveReviveCost` + 单测）；
      纪律 = 先扣后做、失败退回（同召唤）；**只有绑主能复活自己的战神**。
    - 动作 = `ReviveWarGodAsync`：`CreatureCmd.Heal(creature, MaxHp)`（**复活 = 治到 >0**；`HealInternal` 自动
      `ActivateHooks` + `Revived` + 复活动画）+ **必须自己重填战斗态**（`ResetCombatState`/`PopulateCombatState` ——
      绑主死亡时原版把该席 5 个牌堆的牌全移除过）+ `MarkReadyToEndTurnIfMidPlayerTurn`（否则「结束回合」分母多一个）
      + 再给一层【无实体】保命。
  - **门禁**：构建 0 警告 0 错误 → 单测 **1121 全绿** → `preflight -Deploy` 全 PASS；部署位 marker **`2026-10-10-r256`**。
  - **✅ 实机判读（2026-10-10，r256 全绿）**：**R9** 弹层里确实出现复活选项
    （`召唤选择已确定: … 种类=Revive, 档位=Rare, 成本=2`）→ `已复活战神（R9）: seat=4096, 血量=70/70, 第 1 次复活, 抽牌堆=14 张`
    → `已复活战神: … 花费=2, 可用=1/上限=5`，且**复活后在 round 4 / round 5 继续正常出牌并收口**
    （⇒ "重填战斗态 + 补登就绪门"都真的生效，真人回合未被卡）；**R8** `绑主已死 ⇒ 立刻击杀战神（R8）: seat=4097,
    绑主=…326, 档位=Epic` → `战神已随绑主死亡（尸体留场…）`，**另一位真人的战神完全不受影响**；
    无 `复活中止/失败/异常`、无 `绑主死亡联动检查失败`；异常族全 0（`[ERROR]` 3 条全为既有非我方：第三方 Manosaba + `§待查-2` 删存档）。
    ⚠ 顺带发现：**"战神被敌人打死"没有日志锚点**（只能从 `已复活战神（R9）` 反推）⇒ 下一轮顺手补 `战神已死亡`。
  - **复测契约**：打**死战神** ⇒ 尸体留场 ⇒ 下一回合开始的弹层里有 `复活【X档】战神（消耗 N 计数）`；
    选它 ⇒ `已复活战神（R9）: … 血量=满/满, 第 1 次复活` + `已复活战神: … 花费=…`，且真人仍能结束回合、下回合它能出牌；
    打**死绑主**（另一真人活着）⇒ `绑主已死 ⇒ 立刻击杀战神（R8）` + `战神已随绑主死亡`，另一个真人的战神不受影响。
  - **⏭ 未做**：**R10 挡伤 1/2（D4 = 玩家零伤害 + 战神吃一半）** —— 下一轮单独做（它是"第二个重定向源"，
    要实机验证那条 "Only DieForYouPower causes damage redirection" 的告警会不会被打破）。
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
  §BUG-20（**两处**：特殊卡牌奖励、结算页结束按钮）/ §BUG-14 / §BUG-21 / §BUG-22 / §BUG-23 / §BUG-27 / §BUG-29~§BUG-33
- **改进类**：§改进-1~§改进-7（§改进-2 含 ⑭~⑳ 逐轮；§改进-7 = BUG-16）/
  §维护性改进 backlog / §决策表缺口 / §另记：逐卡评级草表
- **功能 / 重构类**：§瓦库四功能（§① 净化 / §② 我们联合 / §③ 炼化）/ §立绘站位对调 / §R3 / §R4 / §R5 / §Daily 本地多控
- **其它已定性 / 已收口**：§原作者遗留 4 项 Known Issues（勿重开）/ §r149 /
  §维护：经验固化（r160）/ §维护：防回归门禁（r162）/ §Co-op Bots 4 人上限 / §大厅席位卡按钮 /
  §重瞳 / §就地复核结论 / §过期待办状态订正

配套去处：玩家可见的变化 → `CHANGELOG.md`；做法与教训 → skill 侧 `references/`；
逐轮验收流水 → `D:\Download\pain\开发进度记录.md`；R3~R5 逐轮 → `archive\TODO-归档（重构线 R3~R5 逐轮记录·r177~r205）.md`。
