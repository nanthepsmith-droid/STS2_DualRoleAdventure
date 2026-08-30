# 编写"多第四幕 Mod 协调器"可行性分析（v3）

> 目标：让 blacksouls2、明日方舟集成战略(ISE)、act4heart、heart of spire、act4最后攀登(a4f) 在同时加载时不冲突。
> 分析依据：游戏源码反编译 `D:\Download\sts2beta111` + 各 mod DLL 反编译（ilspycmd 9.1.0）。
> v2：并入对全部 6 个 workshop DLL（含 loader 变体）与游戏捆绑 0Harmony.dll 的重新反编译核查；吸收外部评审意见（目标分层 / 状态机 / 三不变量）。改动清单见附B。
> v3：新增对 Act Like It 2（workshop 3788402582，下称 ALI2）的反编译评估——外部生态出现"统一第四幕协议"的首个落地实现，V2 路线改为适配器模式复用之，V1 骨架不变。见附C。

## 0. 结论

**可以做，但只能是"路由器 / 仲裁者"，而不是把它们融合成一个系统。**

本质模型：**多个独立状态机 → 共享同一个"最后一幕后去哪"的控制权 → 没有任何协议 → 五个人同时坐驾驶座，每个人都认为自己是导航。**

五个 mod 全部闭源，补丁均落在同一份共享 Harmony 实例上（含 act4heart——其 Dolso `[Hook]` 底层就是 `new Harmony(...)` 的 PatchProcessor 封装，见 §1.0），大量使用反射/Traverse 直接改游戏内部状态。唯一例外是"彼此完全不知道对方存在"这句 v1 表述需弱化：ISE 0.110.1 已内置对 a4f 建筑师事件的**单向兼容垫片**（见 §4），协调器设计要顺着这些垫片走而不是对抗。

### 0.1 目标分层（重要）

**兼容加载 ≠ 多第四幕串联**：

- **G1 兼容加载**：五个 mod 同时装着不互殴，本局选定一个第四幕来源接管。
- **G2 单局串联**：心脏 → 建筑师 → 无终安息 → 冬之钟……一条龙。

G2 不是"协调器"，而是给五个没有互操作设计的 mod **重新定义统一的第四幕协议**，复杂度爆炸且极脆弱。本 mod 明确分期：

| 阶段 | 内容 | 定位 |
|---|---|---|
| V1 | C 骨架（逐 mod 中和守卫）+ EnterNextAct 结算仲裁点 + 第四幕状态机 | 兼容加载，可交付 |
| V2 | 叠加 A 高优路由增强（单局串联实验性） | 可选 |
| V3 | 完整 G2 一条龙 | 不承诺 |

---

## 1. 关键机制（游戏本体）

### 1.0 Harmony 版本与前缀语义：已定案（IL 级验证）

- 游戏自带 `0Harmony.dll`，程序集版本 **2.4.2.0**（原版 Harmony）。无 `HarmonyLib.Public.*`
  （HarmonyX 标志性 API），类型结构为原版 `MethodCreator/Emitter/AddPrefixes` 形态。
  注意：**MonoMod.Core/Utils 类型以 ILMerge 内嵌在该 DLL 内部**（原版 Harmony 2.3+ 的正常形态，
  这也是文件体积 2.3MB 的原因）——"目录里有没有 MonoMod*.dll"不能作为原版/HarmonyX 判别依据。
- **前缀 skip 语义已在 IL 层面定案**（反编译 `AddPrefixes`）：每个返回 bool 的前缀调用之前都会生成
  `ldloc runOriginal; brfalse skip_this_prefix` 守卫；前缀 bool 返回值直接 `stloc runOriginal`。
  因此 **return false 跳过原方法 + 全部后续前缀** 在本游戏实装上成立，方案 A 的核心假设从
  "文档推断"升级为"实测定案"。v1 §1.0 的存疑关闭。
- 但注意两点边界：
  1. **postfix / finalizer 无条件执行**，任何前缀都拦不住——hs 的 `get_IsVictoryRoom` 全局 postfix、
     a4f 的 `GenerateRooms` postfix 都属于这一类；
  2. **无法从高优前缀"选择性屏蔽某一个低优前缀"**——skip 是一刀切。要单独中和某个 mod，
     只能对它自己的方法打守卫补丁（见 §5.3）。
- 游戏 `ModManager.cs:1024` 的 `AssemblyResolve` 兜底把任何 `0Harmony,` 解析失败强制重定向到游戏
  自带程序集，全游戏只有一份 Harmony 实例。
- 可选保险：协调器启动时打一个自检补丁（记录 EnterNextAct 上实际生效的前缀顺序并写日志），
  运行时断言语义符合预期。成本极低，防未来游戏换 Harmony 实现。

### 1.1 ModelDb.Acts / ActsByIndex 与双懒缓存【v2 新增】

- `ModelDb.Acts`：硬编码静态列表，只有 Overgrowth(0)/Underdocks(0)/Hive(1)/Glory(2)，
  源码 `src/Core/Models/ModelDb.cs:299`。mod 加的 ActModel 不会自动纳入。
- **`_acts` 是静态懒缓存字段**：首次访问 `Acts` 后列表固化在字段里。
- `ModelDb.ActsByIndex`（`ModelDb.cs:323`）派生自 `Acts` 属性，但 **`_actsByIndex` 同样是懒缓存**
  ——一旦生成，后续不再读 `Acts` 属性。
- act4heart 的 transpiler 匹配 `stsfld ModelDb._acts` 后 emit 追加委托，且安装时若 `_acts` 已构建
  会**立即修改列表本体**（不是只改 getter 行为）。
- **推论：任何"过滤 get_Acts 结果 / 重排 acts"的协调手段，必须同时失效这两个静态字段**
  （反射置 null 即可触发重建），否则首次访问之后改动不生效。方案 B 与 act4heart 中和均受此约束。

---

## 2. 各 mod 的"第四幕"机制

### 2.0 loader / variant 结构说明【v2 新增】

**ISE 与 a4f 都是 loader 型 mod**：workshop 顶层 DLL 只是引导器（20KB 左右），真正实现按游戏版本
从 `lib/0.107.1/` 或 `lib/0.110.1/` 动态加载。后果：
- 版本漂移风险 ×2（§7）；反射目标必须按当前加载的变体解析；
- 反编译结论必须注明出自哪个变体（本文以 0.110.1 为准，行号在不同变体间会漂移，故尽量引方法名）。

### 2.1 对比表（v2 修订）

| mod | ID / Harmony ID | 加内容方式 | 关键触发判定 | 与"最后一幕"的关系 |
|---|---|---|---|---|
| act4heart | `Act4Heart.dll`；Dolso [Hook] 底层=共享 Harmony 实例的 transpiler/postfix | transpile `ModelDb.get_Acts`（匹配 stsfld `_acts`）追加 `TheEnding`(Index=3, IsDefault=true)，并在安装时立即改 `_acts` 缓存本体 | 无条件；另 `EnsureAct4_After_FromSerializable` 读档时只要 `Acts.Count<4 或 Acts[3]≠TheEnding` 就追加（index 3 被占则追加到尾部） | 永久占用 index 3 |
| act4最后攀登(a4f) | `Act4FinalAscent.dll`（loader）/ id=`act4placeholder.mod` | 运行时 `AppendAct4Placeholder` 把 `Act4PlaceholderMapTemplate` 追加进 `runState.Acts`（条件 `Acts.Count<=3`，反射直写 backing field） | 第三幕 Architect 事件注入选项；EnterNextAct 前缀（默认优先级400）+ `Act4TransitionPending` 安全网；`ShouldOverrideFinalActWin`=IsAct4Placeholder && boss房(RoomType==3) → `FinishRunAfterAct4BossAsync` 强结算；`EnsureAct4ArchitectBossConfigured(默认index=3)` 强改 boss | 占用 index 3，并抢跑结算 |
| heart of spire(hs) | `SpireHeart.dll` / `boninall.spireheart` | `DescendAsync` 把 `CorruptHeartAct` 追加进 `runState.Acts`（**无 Count 保护**，但有自重复保护：Acts 已含 CorruptHeartAct 时转 WinRun） | 最后一幕胜利房 + 三钥匙，拦 EnterNextAct | 追加为新最后一幕 |
| blacksouls2(bs2) | `St2bs.dll` | `GetDefaultListPatch` 是 **postfix**，开局就把冬之钟加进默认 act 列表；EnterNextAct 上有**两个**前缀：`DarkStageTransitionPatch` 在 WINTER_BELL 后插 DARK_STAGE 并**截断其后全部 act**；`EnterNextActPatch` 冬之钟只插一次（WINTER_BELL-in-Acts 守卫）、PSYCHIATRIC_WARD→GLORY 跳转、休息点(RoomType==7)**全局阻断**、TrueEndingTriggered→真实层 | CRIMEA_CARETAKER_CEMETERY 或 CurrentActIndex==Acts.Count-1 触发 | 动态制造新最后一幕 |
| ISE 无终安息 | `IntegratedStrategyEvents.dll`（loader）/ Harmony（依赖 `STS2-RitsuLib`） | **不**加 act，临时替换地图（树洞系统），打完恢复原地图再继续 | Priority 800 前缀：`!TreeHoleSessionManager.IsActive && CurrentActIndex>=Acts.Count-1 && boss房(RoomType3非胜利房)` 且遗物链命中（碎人偶/安萨沙业果/时光与光/次元液/主教研究/深蓝记忆 → 无尽之钥 EndlessFinale）；**无"本局已完成 finale"持久标记** | 只认"最后一幕 boss"；0.110.1 已内置对 a4f 的单向垫片（§4） |

其它 ISE finale（永恒之尘/辉光天顶/逍遥兰若/幽海丛林/诡谲断章/渴欲大厅）由对应遗物触发，机制相同，
入口统一在 `SpecialFinaleCoordinator.GetSpecialFinaleEntryKind`。

### 2.2 EnterNextAct 之外的旁路 hook 清单【v2 新增】

单一 `EnterNextAct` 路由器管不到以下补丁点，**任何方案都必须另行处理**：

- **hs**：`ArchitectWinRedirectPatch` 挂在 **`RunManager.WinRun`** 上——持三钥匙时把胜利转成
  `ActChangeSynchronizer.SetLocalPlayerReady()` 投票；`HeartVictoryRoomPatch` 全局 postfix 改写
  `AbstractRoom.get_IsVictoryRoom`；`WinHeartRunAsync` 直接调 `rm.OnEnded(true)` 结算。
- **ISE**：`ActChangeSynchronizer.OnPlayerReady` 前缀（Priority 800，重置过渡记忆允许重复过幕）；
  `LoadIntoLatestMapCoord` 前缀+postfix（读档恢复时改写房间，含建筑师接力持久化）；
  `EventModel.SetEventState`（建筑师选项抑制/显示/点击三连补丁）；`CreateRoom` 与 boss 节点渲染 swap。
- **bs2**：休息点阻断与 GetDefaultList 注入都是全局行为变化（见上表），不属于"第四幕入口"但会被
  中和操作误伤，守卫时必须保留或按局恢复。
- **a4f**：`RunManager.GenerateRooms` postfix（`RepairDoubleBossTargetAct`）；`OnEnded` 前缀
  （`MarkAct4BossVictory` 强制胜利判定）；建筑师事件选项；多人 JoinSync 序列化补丁。
- **act4heart**：`get_Acts` transpiler + `RunState.FromSerializable` HookAfter（读档即补挂 act4）。

---

## 3. 用户报告的四个冲突场景根因

### 场景 1：bs2 + ISE → 无终安息⇄冬之钟⇄舞台装置 无限循环
- ISE 前缀（Priority 800）判定仅为"最后一幕 boss + 遗物"，**没有任何"已进过无终安息层"的持久标记**；
  只有 `TreeHoleSessionManager.IsActive` 这种"会话进行中"守卫，恢复地图后即失效。
- bs2 冬之钟只插一次（有守卫），循环的另一翼是：`DarkStageTransitionPatch` 截断其后 act 后，
  `CurrentActIndex == Acts.Count - 1` 再次满足 → 舞台装置链继续；每打完一个新"最后一幕"boss 房，
  ISE 条件又重新满足 → 再拽进树洞。
- 本质：双方都没有共同认知"**这个 run 已经处理过 finale 了**"。修复必须由协调器持有**持久化的
  resolution 状态**（§6 不变量3），且该状态要走 run 存档路径，否则读档后复发。

### 场景 2：act4heart + heart of spire + a4f → 心脏地图叠建筑师 boss，打完直接结算
- act4heart 让 index 3 永远有 `TheEnding`（心脏视觉，`TheEndingMap`）。
- a4f `EnsureAct4ArchitectBossConfigured(runState, 3)` 把 index 3 的 boss 强改成建筑师。
- a4f `ShouldOverrideFinalActWin`（IsAct4Placeholder && RoomType==3）→ `FinishRunAfterAct4BossAsync`
  **直接结束 run**；即使绕过它，hs 的下潜也依赖 `WinRun` 拦截路径，同样被吞。

### 场景 3：heart of spire + a4f → 第四层只有建筑师，打完直接结束
- a4f 的 EnterNextAct 前缀抢先 return false，其后所有前缀（含 hs `DescendToHeartPatch`）被跳过
  （§1.0 已定案的 Harmony 语义）。
- 打完建筑师即结算，hs 无触发机会。

### 场景 4：ISE + heart of spire + a4f + 无终安息 → 无终安息→错乱地图(心脏节点/建筑师)→无终安息
- 三者同时改写"最后一幕后的下一个目标"：ISE 临时地图、a4f 追加建筑师 act + boss 覆盖、
  hs 视觉/下潜补丁互相叠。
- 打完无终安息恢复地图后，a4f 介入产生"心脏节点但进建筑师"的错乱地图；
  且 ISE 在 a4f/hs 追加新最后一幕后会再次拦截，形成"无终安息→错乱→无终安息"。

---

## 4. 发现的 mod 自身问题（协调器需规避/利用，v2 修订）

- **ISE `HarmonyBefore(["Act4Placeholder"])` 失效**（已复核）：指向 a4f 的命名空间，而 a4f 实际
  Harmony ID 是 `"act4placeholder.mod"`，排序声明不生效。
- **ISE 无终安息缺"已完成"持久标记**（场景 1 根因之一）。
- **ISE 0.110.1 已内置对 a4f 的单向兼容垫片**【v2 新增】：`ShouldCompleteArchitectAfterEndlessFinale` /
  `PersistArchitectHandoffAfterEnterNextAct` / `SuppressArchitectActChangeOptions`
  （日志可见 "Suppressed N non-vanilla Architect option(s)"），且建筑师选项三补丁带
  `HarmonyAfter(["Act4Placeholder"])`。说明"完全互不知晓"不成立；协调器应复用/让路于这些垫片，
  而不是再叠一层对抗逻辑。
- **hs `DescendAsync` 无 Count 保护但有自重复保护**【表述修正】：Acts 已含 CorruptHeartAct 时转
  WinRun，因此 hs 自身不会重复叠 act；风险在于它会接在**任何** mod 制造的"最后一幕胜利房"后面。
- **a4f `FinishRunAfterAct4BossAsync` 全局结算强切** + `OnEnded` 前缀强制胜利判定（场景 2/3 根因）。
- **bs2 两处全局行为变化**【v2 新增】：休息点阻断（RoomType==7 return false）与 GetDefaultList
  开局注 act——中和 bs2 第四幕时必须精确到方法级别，避免误杀这些无关行为。
- **act4heart 注入永久性**（不按局判断）：transpiler + 安装时直改 `_acts` 缓存本体 +
  读档 HookAfter 三处都要覆盖。

---

## 5. 协调方案与推荐路线（v2 修订）

### 5.1 三种可行手段回顾

- **手段 A：高优先级 Harmony 仲裁前缀**（`RunManager.EnterNextAct` 上 `HarmonyPriority(int.MaxValue)`）。
  语义已 IL 级定案（§1.0）。但 v1"单一入口"的定位**不成立**：§2.2 的旁路 hook 大量存在于
  EnterNextAct 之外（hs 的 WinRun、ISE 的 OnPlayerReady/SetEventState、bs2 的 GetDefaultList、
  a4f 的 GenerateRooms/OnEnded……），路由器拦不住它们。A 只适合做"结算仲裁点"和 V2 的串联增强。
- **手段 B：ModelDb.Acts / ActsByIndex 归一化**。除 v1 列举的缺点外，新增两条硬伤：
  双懒缓存失效问题（§1.1）；以及最危险的特性——**可能 90% 时间表现正常，然后在某个深路径把存档
  状态变成不可逆错误**。维持不推荐；若坚持做，必须先建存档回滚测试。
- **手段 C：运行前仲裁（一局一主）**。哲学干净：本局第四幕=X，其余四家第四幕入口全部禁用。
  最稳健，天然消灭场景 1 循环。

### 5.2 推荐路线图

**V1：C 为骨架，A 只保留一个"结算仲裁点"。**

C 式中和不是"兜底开关"，而是任何方案的必要底座——因为旁路补丁必须逐一处理。V1 结构：

```
第四幕状态机（§6）＝策略层
        │
        ├── 咽喉守卫前缀×N（中和非活跃 mod，见 §5.3）
        ├── EnterNextAct 高优仲裁（只裁决"结算 vs 继续"，防 a4f 抢跑式强切）
        └── 持久化 resolution / entry token（走 run 存档路径）
```

**V2：叠加 A 的完整路由**（单局串联实验性，依赖各 mod 内部接管点反射调用）。
**V3：G2 一条龙，不承诺**（需要给每个 mod 补"从上一家手里接力"的钩子，工程量远大于仲裁）。

### 5.3 中和的两种技术手段

无法选择性屏蔽别人的前缀（§1.0 边界2），所以只有两条路：

1. **咽喉方法守卫前缀（推荐）**：对各 mod 的内部咽喉函数打协调器自己的前缀，按局策略 return false：
   - a4f：`AppendAct4Placeholder` / `FinishRunAfterAct4BossAsync` / 建筑师选项注入
   - hs：`DescendAsync` / `WinHeartRunAsync` / `ArchitectWinRedirectPatch.Prefix`
   - bs2：`EnterWinterBellChoice` / `DarkStageTransitionPatch.BeforeEnterNextAct`
   - ISE：`HandleEnterNextAct`（或 `GetSpecialFinaleEntryKind` 恒返 null 的等价守卫）
   - act4heart：get_Acts postfix 过滤 TheEnding（**必须同时失效 `_acts`/`_actsByIndex` 缓存**）
     + `EnsureAct4_After_FromSerializable` 守卫
   注意这些方法恰好就是 V2 接力所需的反射调用点——**守卫与接力一补两用**，工程量低于 v1 估计。
2. **动态 Unpatch/Repatch**：进局时按策略 Unpatch 对应 owner，出局恢复。彻底但要处理线程安全与
   多次进出局的幂等。

---

## 6. 第四幕协调状态机【v2 新增】

把"谁抢第四幕"形式化为协调器自己维护的状态机，每个 mod 不再"随时想进就进"：

```
NORMAL_ACT3 ──(来源选定/事件选择)──▶ ACT4_PENDING(source=X)
      ▲                                    │ CanEnter(X)==true && PerformEnter(X)
      │                                    ▼
      │                             ACT4_ACTIVE(source=X)
      │                                    │ boss defeated
      │                                    ▼
      └──(resolution=RETURN_MAP)    ACT4_RESOLUTION(resolved=Y)  ← 必须持久化
                                           │
                        FINISH_RUN ────────┼──────── DESCEND_NEXT / HANDOFF(next)
```

per-mod 统一接口：`CanEnter(source, state) / PerformEnter(source) / OnCompleted(source)`。

### 三条硬性不变量

1. **一次 Run 中，一个来源最多获得一次进入权限**（entry token，随 run 持久化）。
2. **ACT4_ACTIVE 期间，任何其它 mod 不得偷换当前 act / boss / 地图**（对应场景 2 的 boss 强改）。
3. **一次第四幕完成后，必须产生明确且持久化的 resolution**（FINISH_RUN / RETURN_MAP /
   DESCEND_NEXT / HANDOFF(next source)）。场景 1 的根因就是双方都缺这个共同认知。

### 联机策略

V1 **明确声明不支持联机**：检测到多人局时禁用全部第四幕来源（仅放行原版流程）。
理由：单机是"协调器决定→本地执行"，联机变成投票/RPC/ready/rollback 多方语义
（ISE 有专门的 `FinaleActChangeGuardPatch`，a4f 有 JoinSync 序列化），为兼容五个 mod 最后会死在
`ActChangeSynchronizer` 上。深挖推迟到 V2 之后。

---

## 7. 主要风险（v2 重排）

1. **存档兼容【一级阻断，从"主要风险"升级】**：entry token / resolution 必须走与各 mod 相同的
   `ToSave`/`FromSerializable` 持久化路径；ISE 自己就有临时地图存档交互（`LoadIntoLatestMapCoord`
   恢复逻辑），协调器改 act 列表若不走同一路径，轻则读档复发，重则炸档不可逆。
2. **版本漂移 ×2**：ISE/a4f 是 loader+variant 结构，`lib/0.107.1` 与 `lib/0.110.1` 的反射目标
   可能不同；游戏本体更新还会同时切换 variant 选择。仲裁/守卫表需按变体维护。
3. **联机同步**：V1 以"多人局全禁用"规避；后续版本必须兼容 `ActChangeSynchronizer` 投票语义
   （多方 ready/回滚），否则卡死或不同步。
4. **共享字段的并发写入**：各 mod 用 Traverse/反射写 `Acts` backing field（bs2、a4f、hs），
   协调器的过滤/失效操作必须在同一底层字段上进行并考虑时序（尤其 `_actsByIndex` 缓存）。
5. ~~Dolso 与 Harmony 混用~~ 【v2 撤销】：act4heart 就在同一 Harmony 链内（§1.0），可用标准
   Harmony 手段（postfix 过滤 / 按 owner Unpatch）控制，无需特殊通道。

---

## 8. 下一步

- [x] Harmony 前缀语义静态验证（IL 定案，§1.0）。
- [ ] 状态机骨架实现（§6：token/resolution 持久化优先）。
- [ ] 两变体 diff：`lib/0.107.1` vs `lib/0.110.1` 的反射目标差异表（ISE/a4f）。
- [ ] get_Acts postfix + `_acts`/`_actsByIndex` 缓存失效原型（act4heart 中和）。
- [ ] 咽喉守卫点落地清单（§5.3 所列方法逐一验证签名与可打性）。
- [ ] 存档回归测试：进第四幕→存→读→再进/再完成，验证 token/resolution 不复发。
- [ ] ALI2 共存矩阵实测：五 mod × {有, 无} ALI2 全组合回归 §3 场景（重点验证 EnterNextAct 上 ISE(800)/a4f(400)/ALI2(默认400) 的实际生效顺序）。
- [ ] 适配器原型：反射调 `ActRegistry.Register` 将单个 mod 第四幕（建议 hs，行为最线性）挂入 ALI2 岔路，验证 IsAvailable 回调时机与存档往返。
- [ ] （推迟）联机 ActChangeSynchronizer 兼容性设计。

---

## 附A：关键反编译文件索引（v2 更新）

- 游戏：`D:\Download\sts2beta111\src\Core\Models\ModelDb.cs`、`ActModel.cs`、
  `src\Core\Runs\RunManager.cs`、`src\Core\Modding\ModManager.cs`(1024 行 AssemblyResolve)
- 游戏捆绑 Harmony：`D:\Download\sts2beta111\_mono_referenced_assemblies\0Harmony.dll`（2.4.2，
  AddPrefixes 语义验证对象）
- workshop 目录映射（`D:\SteamLibrary\steamapps\workshop\content\2868840\`）：
  - `3747637213` ISE（顶层=loader，实现 `lib/{0.107.1,0.110.1}/IntegratedStrategyEvents.dll`）
  - `3756024281` heart of spire（`SpireHeart.dll`，单层）
  - `3748937859` a4f（顶层=loader，实现 `lib/{0.107.1,0.110.1}/Act4FinalAscent.dll`）
  - `3749061519` blacksouls2（`St2bs.dll`，单层）
  - `3747537811` act4heart（`Act4Heart.dll`，单层；HookManager 内部 new Harmony）
  - `3747602295` STS2-RitsuLib（ISE 依赖库）
  - `3737335127` BaseLib（`BaseLib\BaseLib.dll`，v3.4.5；CustomActModel 注册协议提供方，见附C）
  - `3788402582` Act Like It 2（`ActLikeIt2.dll`，单层 v0.1.4，min 0.111.0，HarmonyId=`ActLikeIt2`，见附C）

## 附B：v2 修订记录

1. 【定性修正】act4heart 并非"Dolso 非 Harmony"：`HookManager` 静态构造即 `new Harmony(...)`，
   其 get_Acts 注入是共享实例上的 transpiler → §1.0/§2.1/§7.5 同步改写。
2. 【事实补充】Harmony 前缀 skip 语义由文档推断升级为 IL 级定案；补充"postfix 无条件执行"与
   "不能选择性跳过单个低优前缀"两条边界。
3. 【事实补充】`_acts`/`_actsByIndex` 双懒缓存及其对一切 get_Acts 过滤方案的约束（§1.1）。
4. 【事实补充】ISE/a4f 的 loader+variant 结构与 workshop id 映射（§2.0/附A）；版本漂移升格 ×2。
5. 【事实补充】EnterNextAct 之外旁路 hook 清单（§2.2）：hs-WinRun/IsVictoryRoom、
   ISE-OnPlayerReady/读档恢复、bs2-休息点阻断/开局注 act、a4f-GenerateRooms/OnEnded。
6. 【弱化】"五个 mod 彼此完全不知道对方存在" → ISE 0.110.1 已有对 a4f 单向垫片（§4），协调器顺势而为。
7. 【修正】bs2 冬之钟只插一次；场景 1 循环的另一翼是 DARK_STAGE 截断后"最后一幕"条件重新满足（§3）。
8. 【架构修订】弃用"A主体+C兜底"提法，改为：目标分层 G1/G2 + 路线图 V1(C骨架+结算仲裁)/V2(+A路由)/
   V3(串联不承诺)；中和守卫与 V2 接力点一补两用（§5）。
9. 【新增】第四幕协调状态机与三条硬性不变量（§6）；V1 明确不支持联机。
10. 【风险重排】存档兼容升为一级阻断风险；Dolso 风险撤销（§7）。

---

## 附C：Act Like It 2（ALI2）评估【v3 新增】

> 触发事件：workshop 出现 `Act Like It 2`（id 3788402582，v0.1.4，作者 ShuiMuNianHua，
> min_game_version 0.111.0，HarmonyId=`ActLikeIt2`，单层 44KB DLL）。自述为 STS1 Act Like It! 复刻：
> 自动扫描经 BaseLib `CustomActModel` 实现自定义章节的模组并提供统一选幕界面。
> 以下全部结论基于反编译定案（ilspycmd），关键类：`ActRegistry` / `ActRegistration` / `BaseLibCompat` /
> `ActTogglerCompat` / `ForkInTheRoadEvent` / `EnterActForkPatch` / `EnterNextRegisteredActPatch` /
> `VanillaActRollPatch` / `RunStateActUtil`。

### C.0 结论

**它是"统一第四幕协议"的首个第三方落地实现，恰好补上本方案 V2 缺失的执行层；定位应是复用/适配，而不是对抗或重造。**

它没有触碰本方案的三个核心难题（五家抢跑中和、结算强切防御、resolution 持久化），所以 V1 骨架一点不少；
但它把 V2 最重的三块工程（选幕 UI、act 替换、注册协议）变成了现成品。

### C.1 补丁点全表（反编译核实）

| 补丁点 | 优先级 | 行为 | 与本文档的关联 |
|---|---|---|---|
| `ActModel.GetRandomList` 前缀 | **800**，return false | 自建纯原版列表（仅 Overgrowth/Underdocks/Hive/Glory），快照存入 `__state` | 开局 act 隔离 |
| `ActModel.GetRandomList` finalizer | **0**（最后执行） | 用 `__state` 快照覆盖 `__result` | **反杀一切 postfix 追加**——绕过 §1.0 边界2 的技术示范 |
| `RunManager.EnterAct` 前缀 | 默认(400) | 候选 = 当前槽位已定 act 的 CanonicalInstance ∪ 注册表该槽位可用项（按 IdEntry 去重、IsAvailable/IsUnlocked 过滤）；候选>1 或越界追加时拦截 → 进入"岔路"事件房等待玩家选择 → 选定后放行原版 EnterAct | 进幕路由 + 选择 UI |
| `RunManager.EnterNextAct` 前缀 | **未声明（默认400，与 a4f 同级！）** | `CurrentActIndex+1 >= Acts.Count` 时延长 run 到下一槽位再 EnterAct（触发岔路） | 幕尾扩展入口 |
| `ActModel.CreateMap` 前缀 | 默认 | 注册项带 `CustomCreateMap` 委托时替换地图生成 | 注册扩展点 |
| `ActModel.GenerateRooms` 后缀 | 默认 | 应用注册项 `ForcedBossOrder`（`SetBossEncounter` 强设 boss，跳过 run 内已遭遇 boss） | 注册扩展点 |

岔路流程细节（`EnterActForkPatch.RunForkThenEnterAct` + `ForkInTheRoadEvent.ApplyClaimedAct`）：

- 淡出 → 清 OverlayStack / CapstoneContainer / MapScreen → `EnterRoom(new EventRoom(ForkInTheRoadEvent))`；
- 事件 `IsShared=true`（联机共享）；`TryClaimChoice` 保证只被认领一次，其余玩家走重复回调直接结束；
- 选定后：`canonicalAct.ToMutable()` → `ReplaceActAt`（**走 `RunState.Acts` 属性 setter**，迁移 `_sharedAncientSubset`）→ `RegenerateActRooms` → `SkipNextFork=true` → 原版 EnterAct 正常执行。

注册 API 全公开：`ActRegistry.Register(new ActRegistration{ CanonicalAct, ActNumber, IsAvailable(runState), OptionDescription, SelectionGroupId/Title/Description, CustomCreateMap, CustomMapPointTypes, ForcedBossOrder })`，按 IdEntry 去重幂等。

### C.2 四件可直接复用的资产

1. **岔路选择 UI + 进幕路由**＝§6 状态机中"ACT4_PENDING 选定来源 + PerformEnter"的 UI 半边与执行半边。V2 的串联入口不用自己写界面。
2. **干净的 act 替换路径**＝手段 B 被 §1.1 双懒缓存卡死的正确解法示范：完全不碰 `ModelDb._acts/_actsByIndex`，只改 run 实例的 Acts 列表；每幕独立走 `SerializableActModel.ToSave()` 序列化路径，§7 风险1（存档兼容）大幅下降。
3. **prefix(800)+finalizer(0) 夹心术**：突破 §1.0 边界2"postfix 无条件执行、拦不住"的限制——finalizer 最后恢复快照即可吞掉所有 postfix 的修改。可直接搬到 `ActModel.GetDefaultList`（`src/Core/Models/ActModel.cs:574`）上反杀 bs2 开局注冬之钟（bs2 的 `GetDefaultListPatch` postfix 打的正是此方法，单机路径 `RunState.CreateShared` 调用）。注意 ALI2 自己的隔离**管不到 bs2**：`GetRandomList` 只有多人开局路径调用（`StartRunLobby.cs:472`），单机走 GetDefaultList——两套补丁互不覆盖。
4. **生态协议入口**：BaseLib（workshop 3737335127，v3.4.5，作者 Alchyr）本体同样 transpile `get_Acts`（匹配 stsfld `_acts`，与 act4heart 同款手法）把 `CustomContentDictionary.CustomActs` 并入 `ModelDb.Acts`。新 mod 按 BaseLib 写第四幕即自动进岔路、彼此天然兼容——G2 设想的"统一第四幕协议"已有第三方在建。ALI2 懒发现后把这些 acts 导入自己的注册表（槽位 = `BaseLib.Extensions.ActModelExtensions.ActNumber` = Index+1）。

### C.3 它不解决的（V1 工作量不变）

- 五个闭源 mod 不向任何注册表登记、照旧抢跑 → §5.3 咽喉守卫仍是底座；
- 只裁决"下一幕是谁"，不碰结算强切 → a4f `FinishRunAfterAct4BossAsync`、hs `WinRun`/`ArchitectWinRedirectPatch`、ISE 树洞拦截全部无防御，EnterNextAct 高优结算仲裁点仍需自建；
- 没有 entry token / resolution 持久标记 → 场景 1 式循环在它之上理论上仍会复发（ISE 在岔路选定后照样拦最后一幕 boss）；
- 多人只是"有处理"（共享事件 + 单次认领）而非"禁用"，作者自述未充分测试 → §6 联机策略（多人全禁）仍由协调器把守；
- `GetSelectableActs` 会把 `runState.Acts` 里已有的 act 也列为候选——bs2 注入的冬之钟会以"可选项"出现在岔路里（行为变化是机会也是干扰源，bs2 自己的 EnterNextAct 前缀仍在场）。

### C.4 新增风险

1. **第六个驾驶座**：ALI2 在 `EnterNextAct`/`EnterAct` 上新增前缀，且 `EnterNextRegisteredActPatch` 未声明优先级（默认400，与 a4f 同优先级，实际顺序取决于补丁安装时序）。§3 冲突矩阵必须增加"+ALI2"列重测。
2. **成熟度**：v0.1.4 早期版本；绑定 0.111.0（游戏更新即可能失效）；作者自述多人未经充分测试。
3. **协议分叉期**：BaseLib 注册制与五家闭源抢跑制将长期并存，同一局可能同时存在两类 act 来源，协调器需要同时讲两套语言（这正是适配器模式的由来）。

### C.5 路线图修订（覆盖 §5.2 的 V2 描述）

- **V1 不变**（C 骨架 + 结算仲裁点），新增一条：检测到 ALI2 在场时，进幕路由让路给其岔路 UI，避免双重拦截。
- **V2 改为适配器模式**：

```
协调器 = 守卫层   （§5.3 不变：咽喉前缀压制五家自主行为）
       + 适配器层 （反射调 ActRegistry.Register 把各家第四幕挂入注册表：
                   IsAvailable = 协调器策略函数，SelectionGroup 分组呈现）
       + 结算仲裁点（§5.2 不变：高优裁决"结算 vs 继续"，ALI2 不管结算）
```

  自研 fork UI / act 替换 / BaseLib 导入代码全部砍掉。
- **发布形态可选**：作为 ALI2 的伴生 mod（守卫+适配器）发布，声明依赖其存在；或保留独立实现、仅在检测到 ALI2 时启用适配路径。
