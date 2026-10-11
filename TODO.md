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
| `补丁mod收进主仓库compat-mods.md`（#20） | ✅ 已落地（2026-10-05 迁 1 个 + 2026-10-07 再迁 2 个 ⇒ `compat-mods/` 现有 3 个；范围仍为「主 mod ↔ 第三方交互」补丁） | 无（push 已完成；**分发口径 = 主 Release 的独立附件 `compat-mods-v<major>.<minor>.zip`**，已挂 v1.45、`release_build.ps1` 支持自动打与 `-CompatOnly`） | 索引 #20 |
| **战灵召唤（原 ④「地狱战神」）**（#21） | **D1~D13 已拍板**；**Phase 0 POC ✅ 关单（r239）**；**Phase 1 + r241 四项追加 ✅ 实机关单（r240~r243）**；**Phase 2 召唤闭环 ✅ 实机关单（r244 落地 → r245「不能结束回合」/ r246「第二个战神不会被打」两轮修复 → 2026-10-09 r246 判读全绿，部署位 `2026-10-09-r246`）**：回合开始召唤选择 + 消耗计数 + 生成完整战神（D7「普通档」）+ 战后补满清理；**🆕 两个"大招"召唤 R16【无尽战神】/ R17【终极九连环】（D14~D19 已拍板：整包价 无尽 8 / 九连环 12 + 部分成功；2026-10-09 二轮修订 = 【九连环】改扣"计数上限"本身（永久消耗、不补满），实际一轮只能开一次）**；**Phase 2b（D7 预设档 + 两个大招）✅ 实机关单（r247 落地「一切都好正常」→ r248 修订【九连环】门槛 12/固定 9 个/按实际数量计费 → 2026-10-10 判读全绿：无尽 10/10 补满 12 席·随机各档 / 九连环 9 个先古·永久扣上限 12→0），部署位 `2026-10-09-r248`** | **Phase 3（战神托管出牌 + 台词「唔」）✅ 实机关单 = r253**（r249 落地 → r250 `GetMe` → r251 队列档 → r252 补登记 → **r253 定稿：回归 inline + 不钉上下文**；同轮追加【无尽战神】先古开关 + 召唤保命【无实体】；用户「一切都变得好正常」，异常族全 0）；**2026-10-10 用户拍板后续口径 = 预设卡组走「手工 id 列表 + 开放到配置文件」/ 预设遗物走「A 方案：只挑不依赖开战时机的遗物」/ 顺序 = 卡组 → 遗物 → Phase 4/5** | **① 手工预设卡组 ✅ 实机关单（r254：内置默认改 `ModelDb.Card<T>()` 类型引用 + 四档配置键 `hellwarDeck*` 可自定义；判读全绿，`logs-archive/godot__20261010-145141__r254.log`）；② 预设遗物（A 方案）✅ 实机关单（r255：罕见 1 / 稀有 2 / 先古 3 件，全为"常驻钩子"遗物；判读全绿，`logs-archive/godot__20261010-182625__r255.log`；⚠ 稀有/先古档本场未召，效果体感待后续）；③ Phase 4 生存规则：**R8 绑主死亡联动 + R9 死后复活 ✅ 实机关单（r256，判读全绿：复活后 round 4/5 继续出牌、绑主的战神当场收掉且不影响别人）**；**R10 挡伤 1/2（D4 = 玩家零伤害 + 战神吃一半）✅ 实机关单（r257：绑主受击 0 次 / 无守卫的对照组 19 次 / 战神吸收 53 次）** → **Phase 4 全线关单**；**④ Phase 5 收尾（R12 战神无战斗奖励 + R13 我挡/你攻覆盖战神）✅ 实机关单（r258：契约四条全中 —— 奖励只发真人 / 无[未知角色] / `我挡：4096(战神)` ×2 / `你攻生效: player=4096(战神)`）** ⇒ **战灵召唤 R1~R13 全部实机关单，功能线关单**；**🆕 r259（用户三条口径）**：① R10 修订 = 肉盾资格改**下一回合**起生效 ✅ **用户实机确认**；② **R13b = 【合体】覆盖战神** ✅ **实机确认**（`合体：… ← 瓦库 4096/4097(战神)`，手牌 5 + 2 张被收编；`我挡：4096(战神)`、`你攻生效: player=4096(战神)` 同场复现）；③ 「战神当回合就能打牌」= **不做**（用户不强求）；R10 平衡（战神是否太脆）= 用户先**完整测几局**再议；**🆕 r260：战后清理逐项容错（第三方 NinjaSlayer 布局补丁抛异常曾中断清理 ⇒ 漏摘战神；同场 **R12 排除闸门立功**）+ 合体「空气牌」**自检探针** + 合体日志补混抽可用性**；**🆕 r261：① 托管**不再打"需要目标却没目标"的牌**（根因 = 猪猪 mod「抱猪」用**第三方自定义 `TargetType`**；同一根因导致"合体空气牌"）② 战后摘战神改**「数据先摘、视觉节点后收」****；**🆕 r262（2026-10-11，承接实机反馈，⭐ 待实机）：① **【无尽战神】整包价 8 → 12**（用户拍板：它一次补满 12 席 ≈ 10 个战神，太强）② **托管出牌抛异常的牌记入「本局别让托管打」名单**（沙耶 mod「色素细胞」抛 `Index was out of range` ⇒ 卡面卡屏幕中间 + 后续空气牌；不再重试、真人仍可自己打）③ 战神视觉节点**「先藏再摘」**（第三方在 `RemoveCreatureNode` 链上抛 ⇒ 原来隐藏那步永远执行不到）**；`§待查-6` 已定性（跟原版 UPPERCUT 命中特效绑定，与 R10/战灵召唤无关）**；另：D12③「1 席本地会话」入口；**改动已本地提交 9 笔、领先 origin 14 笔、未 push、未合回 master（待用户拍板）**；备选：用户提的"战神当回合就能打牌"（本轮选了无实体保命方案） | 索引 #21 + 下方 §战灵召唤 + 设计文档 §8（含 r249~r253 落地记录与后续两阶段口径） |

> 注：`PrimeVakuu决策桥接-本地转接头可行性分析.md`（#19）是**本地私有**提案（对方未开源），
> **不进本表、不进任何公开载体**，只登记在 `decision-records\README.md`。

---

## 待办（未定根因 / 待观察 / 暂不修）

> 下列都是「能用但别扭」或「偶发自愈」的问题，**用户已拍板先记录不修** —— 复现 / 想修时按各条自己的
> 判据与备选路线走（逐条已写好现象、日志锚点、根因分析与修法备选）。
> ⚠ **例外**：**BUG-14 已于 2026-10-10 排期**（当前线做完后做），见该条末尾的排期段。
> 同类既有备案：`maintenance-docs\decision-records\瓦库托管优化可行性分析.md` §16.2。

### 待查-1 宝藏房共享选遗物界面：本地席位的手牌图像被**重复添加**（`add hand … twice!`，r246 判读时发现，**纯日志、无行为影响**）

- **现象（marker r246，`logs-archive/godot__20261009-204838__r246.log` L14868）**：
  ```
  [ERROR] Tried to add hand for player 76561198422527327 twice!
     at …NHandImageCollection.AddHand(UInt64 playerId)
     at …NHandImageCollection.OnInputStateAdded(UInt64 playerId)
     at …PeerInputSynchronizer.GetOrCreateStateForPlayer(UInt64 playerId)
     at …PeerInputSynchronizer.SyncLocalScreen(NetScreenType)
     at …ScreenStateTracker.SetIsInSharedRelicPickingScreen(Boolean)
     at …NTreasureRoomRelicCollection.AnimOut()
     at …NTreasureRoomRoom.OpenChest()
  ```
- **定性**：发生在**宝藏房**、对象是**真人席位**…327（不是战神；战神没有 peer 输入态）⇒ 与本轮战灵召唤无关。
  触发链 = 本 mod **切前台时更换本地 `NetContext.NetId`**；该 id 的 peer 输入态当前不存在 ⇒
  `GetOrCreateStateForPlayer` 新建 ⇒ 触发 `StateAdded` ⇒ `AddHand` 发现手牌图像已存在。
- **影响**：`AddHand` 的守卫**只 `Log.Error` 然后 `return`**（`NHandImageCollection.cs:83-96`），手牌图像本来就在 ⇒ **无行为影响**；
  唯一代价是**抬高全局 ERROR 计数**（判读回归时容易误判）。
- **候选修法**（想修时）：① 本 mod 在宝藏房期间不要切 `LocalContext.NetId`（或切前先 `SyncLocalScreen`）；
  ② 给 `AddHand` 做一条"同 id 且在树内就不重复加"的前缀补丁（新增 Harmony 目标，需权衡）。
- **优先级**：低（纯日志）。

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

### 待查-5 休息区"悬停索引越界"WARN 是**误报**（`index=-1` = 没有悬停，被当成越界；r256 判读首现，**纯日志**）

- **现象（marker r256，`logs-archive/godot__20261010-190410__r256.log`，共 7 条）**：
  `休息区悬停索引越界，已忽略: player=…, index=-1, options=4`（以及 `options=5`）。
- **根因**：`Scripts/Patch/RestSitePatch.cs:258` 的判据是 `hoveredOptionIndex.Value >= 0 && < options.Count`，
  而 `RunManager.Instance.RestSiteSynchronizer.GetHoveredOptionIndex(playerId)` 在**"没有悬停任何选项"**时返回
  **`-1`**（`null` 才是"没记录"）⇒ `-1` 落进 `else` 分支被打成 WARN。
- **影响**：**纯日志**（行为正确 —— `option` 保持 `null`，只是 `ShowHoveredRestSiteOption(null)`）。
  但会污染"异常族期望 0"的判读（本功能上一轮 r254/r255 恰好没进火堆，所以是首次出现）。
- **修法（一行）**：把 `-1` 显式当作"无悬停"**静默忽略**（只在 `>= options.Count` 时才打 WARN）。
- **⚠ 归属与纪律**：这是**休息区滚动条/悬停同步**那条线的历史遗留，与战灵召唤无关 ⇒ 按"**跨主题修复单独开分支**"
  的纪律，**不塞进 `feat/hellwar-god-phase4-survival`**，留待单独一轮清理。
- **优先级**：低（纯日志）。
- **登记**：2026-10-10（第八十四段收工块 ②）。

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

### 待查-2 「进入本地多控流程」时游戏删旧多人 run 存档报 `Error deleting path`（**既有行为、非我方引入**，r239/r240 就有）

- **现象（marker r247，`logs-archive/godot__20261009-224919__r247.log` L9395 起 4 条）**：
  ```
  [ERROR] Error deleting path modded/profile2/saves/current_run_mp.save: Failed
     at MegaCrit.Sts2.Core.Saves.GodotFileIo.DeleteFile(String path)
     at MegaCrit.Sts2.Core.Saves.CloudSaveStore.DeleteFile(String path)
     at MegaCrit.Sts2.Core.Saves.Managers.RunSaveManager.DeleteCurrentMultiplayerRun()
     at MegaCrit.Sts2.Core.Saves.SaveManager.DeleteCurrentMultiplayerRun_Patch1(SaveManager this)
     at LocalMultiControl.Scripts.Patch.NMultiplayerHostSubmenuPatch.OnLocalSelfCoopPressed(NMultiplayerHostSubmenu)
  ```
- **定性**：栈里的最后一行是**我们**的"进入本地多控流程"入口，它调游戏的
  `SaveManager.DeleteCurrentMultiplayerRun()` 做清理；`GodotFileIo.DeleteFile` 在**目标文件不存在**
  （或句柄被占）时只打一条 `Log.Error` 就返回 ⇒ **纯日志、无功能影响**。
- **跨会话证据**：r239 = 2 条、r240 = 2 条、**r243/r245/r246 = 0 条**、r247 = 4 条
  ⇒ 只在"存在待删的旧多人 run 存档"时出现，**不是本轮（战灵召唤）引入**。
- **候选修法**（想清日志时）：在 `OnLocalSelfCoopPressed` 里**先判存在再删**（`FileAccess.FileExists` /
  `File.Exists` 包一层），或把这条清理挪到"删完再打日志"的位置；⚠ 但它是游戏 API 的行为，**改之前先确认不破坏原版清理语义**。
- **优先级**：低（纯日志，会抬高全局 ERROR 计数）。

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

### BUG-14 火堆最多只显示 4 个玩家的角色气泡（5 号及以后不显示）—— 🕐 **已排期（2026-10-10 用户拍板）**：当前线（战灵召唤 Phase 4/5）做完后做

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
- **🕐 排期（2026-10-10 用户拍板）**：**当前线（战灵召唤 Phase 4/5）做完后做**。
  实现口径取上面**修法备选 ①**（运行时补建 `Character_5..N` 容器并加进 `_characterContainers`）+
  保留 `NRestSiteRoomReadyGuardPatch` Finalizer 当保底；落地前先做纯逻辑（补齐数量/占位/越界判据可单测），
  并复测「4 人及以下完全不动原版布局」与「5~12 人气泡/立绘/选项可见」两条契约。

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
- **⏳【待排期·用户 2026-10-11 提出】「预设构筑器」：可视化造战神的程序（选卡/遗物/角色/血量/档位 → 导出给 mod）**：
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
      按需提取图片缓存到 `%APPDATA%`（卡图还要按 `atlases/card_atlas.sprites/<pool>/<id>.tres` 的 `region` 从
      `card_atlas_0/1.png` 裁剪；遗物图 `images/relics/*.png` 整图直读）；不发布官方美术（版权 + 体积都干净）。
      目录侧 = 玩家先在 mod 设置页点一次"导出内容目录"生成他自己机器的 `content_catalog.json`（含他装的 mod 卡），
      编辑器读它；没导出则提示先导出（不显示列表）。
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
- **⏳【待排期·用户 2026-10-11 点名】海克斯符文兼容补丁（`compat-mods\HextechRunesLocalCoopFix`）：加**抓率统计** + 按抓率选符文 + 选项很烂时自主 roll**：
  - **需求原话**：「为海克斯符文兼容补丁 mod 加上我们的本体 mod 相似的**抓率统计**，瓦库根据抓率选择，
    并且在**选项很烂的情况下自主 roll 符文**」。
  - **载体建议**（`code-explorer` 已侦察）：该补丁 mod **当前没有任何统计/配置文件**，照抄主 mod 的
    `Scripts\Runtime\LocalPersonalRecorder.cs`（`%APPDATA%\SlayTheSpire2\personal_stats.json`，原子写；
    `Reload :46` / 入账 `:124/:181/:219/:260/:313` / 落盘 `:442`）与其开关
    `LocalWakuuAutopilotConfig.PersonalRecorder`（`vakuu_autopilot.json`）。
  - **入账挂点**：`WakuuRuneAutoAnswer.TryAnswer` 作答处（`WakuuRuneAutoAnswer.cs` 里
    `picked=` 日志那一段，约 :223-233）—— 落盘 `{席位, 幕, 候选 options, 选中项, 是否 roll}`。
  - **"自主 roll"前置**：必须先确认海克斯允许掷骰/重掷的入口（`HextechRuneSelectionScreen` 的
    reroll 按钮 / `EnemyHexAuthorityCompatPatch` 那套"只有可调席位能改"的归属判据是本轮可复用先例）；
    并且要定义"选项很烂"的判据（抓率全低？稀有度全银？ —— ⚠ 注意：**用户已实测"每次 3 个选项稀有度一致"**，
    所以稀有度不能当判据，得靠抓率或效果质量）。
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
  - **④ R10 平衡（战神是否太脆）= 挂起**（用户要**完整测几局**再看）⇒ 本轮**不动**任何阈值。
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
    📌 **平衡点（2026-10-10 用户处置 = 挂起）**：R10 让战神成了"伤害吸收器"（低档 68 血 + 计数全花在召唤上
    ⇒ 接近一次性消耗品）—— 用户口径「**放后面，我完整测几局再看看**」⇒ **本轮不动阈值**；
    可选调法留档：① R10 转移量调小（1/3 或封顶）② 复活更便宜 ③ 计数上限更高。
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
  §BUG-20（**两处**：特殊卡牌奖励、结算页结束按钮）/ §BUG-21 / §BUG-22 / §BUG-23 / §BUG-27 / §BUG-29~§BUG-32
- **改进类**：§改进-1~§改进-7（§改进-2 含 ⑭~⑳ 逐轮；§改进-7 = BUG-16）/
  §维护性改进 backlog / §决策表缺口 / §另记：逐卡评级草表
- **功能 / 重构类**：§瓦库四功能（§① 净化 / §② 我们联合 / §③ 炼化）/ §立绘站位对调 / §R3 / §R4 / §R5 / §Daily 本地多控
- **其它已定性 / 已收口**：§原作者遗留 4 项 Known Issues（勿重开）/ §r149 /
  §维护：经验固化（r160）/ §维护：防回归门禁（r162）/ §Co-op Bots 4 人上限 / §大厅席位卡按钮 /
  §重瞳 / §就地复核结论 / §过期待办状态订正

配套去处：玩家可见的变化 → `CHANGELOG.md`；做法与教训 → skill 侧 `references/`；
逐轮验收流水 → `D:\Download\pain\开发进度记录.md`；R3~R5 逐轮 → `archive\TODO-归档（重构线 R3~R5 逐轮记录·r177~r205）.md`。
