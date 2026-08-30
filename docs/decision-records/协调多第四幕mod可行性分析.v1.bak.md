# 编写"多第四幕 Mod 协调器"可行性分析

> 目标：让 blacksouls2、明日方舟集成战略(ISE)、act4heart、heart of spire、act4最后攀登 在同时加载时不冲突。
> 分析依据：游戏源码反编译 `D:\Download\sts2beta111` + 各 mod DLL 反编译（ilspycmd 9.1.0）。

## 0. 结论

**可以做，但只能是"路由器 / 仲裁者"，而不是把它们融合成一个系统。**

五个 mod 全部闭源，使用 Harmony（a4f、ISE、bs2、heart of spire）或 Dolso IL Hook（act4heart），
大量使用反射/Traverse 直接改游戏内部状态，彼此完全不知道对方存在，也没有任何协调 API。

因此协调 mod 的可行角色有且只有三种：
1. **高优先级 Harmony 仲裁前缀**（对 `RunManager.EnterNextAct`）：接管时 return false 阻断其它所有 mod 的前缀；
2. **ModelDb.Acts / ActsByIndex 归一化**：统一管理"哪个 act 占 index 3"，并同步补丁各 mod 硬编码的 `== 3` 判定；
3. **"一局一主"运行前仲裁**：本局只启用一个第四幕 mod，协调者去中和其余 mod 的触发。

三者可实现性见第 5 节。若想真正把多个 mod"串起来"（如 心脏→建筑师→真实层 一条龙），
需要对每个 mod 补"从上一家手里接力"的钩子，工程量远大于仲裁，且极脆弱，不建议。

---

## 1. 关键机制（游戏本体）

### 1.0 Harmony 版本确认：原版 Harmony，非 HarmonyX

- 游戏自带 `0Harmony.dll`，程序集版本 **2.4.2.0**（原版 Harmony 2.4.2）。
- 该程序集**没有** `HarmonyLib.Public.*`（`ManualPatch`/`PatchManager` 是 HarmonyX 的标志性 API），
  也没有 `MonoMod*.dll`（HarmonyX 的运行时依赖），类型列表全部为原版 `HarmonyLib.*`。
- 六个 mod 的 workshop 目录均**不携带**自己的 `0Harmony.dll`/`HarmonyX*.dll`/`MonoMod*.dll`，
  全部解析到游戏捆绑的同一份原版 Harmony。
- 游戏 `ModManager.cs:1024` 的 `AssemblyResolve` 兜底会把任何 `0Harmony,` 解析失败强制重定向到游戏自带程序集，
  保证全游戏只有一个 Harmony 实例。
- 对协调器的影响：所有 Harmony mod 共享同一个 Harmony，优先级别语义（`[HarmonyPriority]` /
  前缀 return false 阻断后续前缀）全局一致，方案 A 的仲裁路由可以可靠生效；
  也无需考虑 HarmonyX 特有 API（如 `Public.Patching`），也不能依赖它们。

- `ModelDb.Acts`：**硬编码静态列表**，只有 Overgrowth(0)/Underdocks(0)/Hive(1)/Glory(2)，
  源码 `D:\Download\sts2beta111\src\Core\Models\ModelDb.cs:299`。
  任何 mod 加的 ActModel 不会被自动纳入（与 Cards/Relics 等按 `AllAbstractModelSubtypes` 自动扫描不同）。
- `ModelDb.ActsByIndex`：按 `Act.Index` 分组的派生列表（`ModelDb.cs:323`），
  是 `ActModel.GetRandomList / GetDefaultList`（`ActModel.cs:538 / 574`）生成一局 act 列表的唯一依据。
- `RunManager.EnterNextAct`：所有 mod 争夺的"最后一幕后去向"的总入口。
- 原版没有 act index 3，谁先占据 index 3、或谁在运行时追加新 act，谁就决定"第四幕"内容。

---

## 2. 各 mod 的"第四幕"机制

| mod | ID / Harmony ID | 加内容方式 | 关键触发判定 | 与"最后一幕"的关系 |
|---|---|---|---|---|
| act4heart | `Act4Heart.dll` / Dolso Hook（非 Harmony） | IL 注入 `ModelDb.get_Acts`，把 `TheEnding`(Index=3, IsDefault=true) 追加进**全局 act 列表** | 无需特殊条件；另外 `EnsureAct4_After_FromSerializable` 对旧存档补 act | 永久占用 index 3 |
| act4最后攀登(a4f) | `IntegratedStrategyEvents` 无关；`Act4Placeholder.dll` / Harmony id=`act4placeholder.mod` | 运行时 `AppendAct4Placeholder` 把 `Act4PlaceholderMapTemplate` 追加进 `runState.Acts`（条件 `Acts.Count<=3`） | 第三幕 Architect 事件注入"普通/苦难"两选项（`EventModelSetEventStatePatch`，条件 CurrentActIndex==2 且 Acts.Count<=3）；`EnsureAct4ArchitectBossConfigured` 强制 index 3 boss=`ACT4_ARCHITECT_BOSS_ENCOUNTER`；`ShouldOverrideFinalActWin` 在 index3 boss 房直接结束 run | 占用 index 3，并抢跑结算 |
| heart of spire | `SpireHeart.dll` / `boninall.spireheart` | `DescendAsync` 把 `CorruptHeartAct` 追加进 `runState.Acts`（**无 Count 保护**） | 胜利房 + 三钥匙 + 拦截 EnterNextAct | 追加为新最后一幕 |
| blacksouls2(bs2) | `St2bs.dll` | `GetDefaultListPatch` 往默认 act 列表加冬之钟；`EnterNextActPatch` 运行时插入冬之钟/舞台装置/真实层 | 到达最后一幕 boss 后插入；`DarkStageTransitionPatch` 在冬之钟后插舞台装置并截断其后 act | 追加为新最后一幕 |
| ISE 无终安息 | `IntegratedStrategyEvents` / Harmony（依赖 `STS2-RitsuLib`） | **不**加 act，临时替换地图（树洞系统），打完 finale boss 恢复原地图再继续 | `CurrentActIndex >= Acts.Count-1` 且 boss 房(RoomType 3, 非胜利房) 且持无尽之钥 → `EndlessFinale`（`SpecialFinaleCoordinator.cs:233`）；Priority 800 | 只认"最后一幕 boss"，无防重复标记 |

其它 ISE finale（永恒之尘/辉光天顶/逍遥兰若/幽海丛林/诡谲断章/渴欲大厅）由对应遗物触发，机制相同。

---

## 3. 用户报告的四个冲突场景根因

### 场景 1：bs2 + ISE → 无终安息⇄冬之钟⇄舞台装置 无限循环
- ISE 前缀（Priority 800）判定仅为"最后一幕 boss + 无尽之钥"，**没有任何"已进过无终安息层"的持久标记**。
- bs2 前缀（默认 Priority 400，晚于 ISE 执行）每打完一个"最后一幕"就追加一个新最后一幕（冬之钟 → 舞台装置）。
- 于是每次 ISE 把玩家拽进无终安息 → 打完恢复 → bs2 又造出一个新的最后一幕 → ISE 再次触发 → 循环。

### 场景 2：act4heart + heart of spire + a4f → 心脏地图叠建筑师 boss，打完直接结算
- act4heart 让 index 3 永远有 `TheEnding`（心脏视觉，`TheEndingMap`）。
- a4f `EnsureAct4ArchitectBossConfigured(runState, 3)` 把 index 3 的 boss 强改成建筑师 → 心脏地图 + 建筑师战斗。
- a4f `ShouldOverrideFinalActWin`（CurrentActIndex==3 且 Acts.Count>3 且当前为 boss 房）→ `FinishRunAfterAct4BossAsync` **直接结束 run**，heart of spire 的 `DescendToHeartPatch / WinHeartRun` 永远到不了。

### 场景 3：heart of spire + a4f → 第四层只有建筑师，打完直接结束
- a4f 的 EnterNextAct 前缀 `ShouldOverrideFinalActWin` 抢先 return false，
  Harmony 语义下其后所有前缀（含 heart of spire 的 `DescendToHeartPatch`）被跳过。
- 打完建筑师即结算，heart of spire 无触发机会。

### 场景 4：ISE + heart of spire + a4f + 无终安息 → 无终安息→错乱地图(心脏节点/建筑师)→无终安息
- 三者同时改写"最后一幕后的下一个目标"：ISE 临时地图、a4f 追加建筑师 act + boss 覆盖、hs 视觉/下潜补丁互相叠。
- 打完无终安息恢复地图后，a4f 介入产生"心脏节点但进建筑师"的错乱地图；
- 且 ISE 在 a4f/hs 追加新最后一幕后会再次拦截，形成"无终安息→错乱→无终安息"。

---

## 4. 发现的 mod 自身问题（协调器需规避/修复）

- **ISE `HarmonyBefore(["Act4Placeholder"])` 失效**：
  指向的是 a4f 的命名空间，而 a4f 实际 Harmony ID 是 `act4placeholder.mod`（`ModEntry.cs:416`），大小写与内容都不匹配，排序声明实际不生效。
- **ISE 无终安息缺防重复标记**（场景 1 根因）。
- **heart of spire `DescendAsync` 追加 act 无 Count 保护**，会与任何"第四幕 act"重复叠加。
- **a4f 的 `FinishRunAfterAct4BossAsync` 是全局结算强切**，会吞掉其它 mod 的结算逻辑（场景 2/3 根因）。
- **act4heart 的 `get_Acts` IL 注入是永久性**的（不按局判断），只要加载就占 index 3。

---

## 5. 协调 mod 的三种可行方案

### 方案 A：高优先级 Harmony 仲裁前缀（推荐）
- 给 `RunManager.EnterNextAct` 打 `[HarmonyPriority(int.MaxValue)]` 前缀作为"路由器"。
- Harmony 语义：前缀 return false 会跳过原方法**以及所有更低优先级的前缀**。
- 协调器根据"当前 run 状态 + 遗物 + 各 mod 内部标记 + 配置"决定本层由谁接管：
  - 接管 → 调用对应 mod 的接管逻辑（反射调其 private 方法或自实现），return false；
  - 不接管 → return true 放行。
- 需按 mod 版本精确复刻触发条件表（本分析已反编译出全部条件）。
- 优点：单一入口、逻辑集中、可精确消除场景 1 的循环（加防重复标记）与场景 2/3 的结算抢占。
- 缺点：反射调用依赖 mod 内部字段/方法名，mod 更新即需维护；act4heart 走 Dolso，不在 Harmony 链内，需单独处理其 `get_Acts` 副作用。

### 方案 B：ModelDb.Acts / ActsByIndex 归一化
- 协调器自己的 `get_Acts` IL 补丁（排在 act4heart 之后执行）做去重/重排，把各 mod 的 act 分配到不同 index
  （例如 TheEnding→3、Act4Placeholder→4、CorruptHeartAct→5、WinterBell→6 …）。
- 并同步补丁各 mod 硬编码的 `== 3` 判定：`TheEnding.Index=>3`、`CorruptHeartAct.Index=>3`、
  a4f 的 `EnsureAct4ArchitectBossConfigured` 默认 actIndex=3、`IsAct4Placeholder` 检查 CurrentActIndex==3 等。
- 优点：能从根上解决"index 3 被多 mod 抢"。
- 缺点：改动面最大、IL 补丁顺序敏感、最容易因游戏更新崩坏。**不推荐作为首选。**

### 方案 C：运行前仲裁（一局一主）——最稳健
- 进局前（或开局时）选定本局唯一的第四幕来源（可配置/可在 Architect 事件选择）。
- 协调器**中和**其余 mod 的触发：
  - patch 掉 bs2 `GetDefaultListPatch` 的追加结果；
  - 屏蔽 ISE `EndlessFinale` 的触发（或清除无尽之钥判定）；
  - 屏蔽 a4f 的选项注入与 `ShouldOverrideFinalActWin`；
  - 屏蔽 heart of spire 的 `DescendAsync`。
- 优点：不依赖精确复刻内部逻辑，只是"让不用的那个失效"，最不容易被 mod 更新打爆；
  且天然解决场景 1 的循环。
- 缺点：同一局无法同时体验多个第四幕（只能在多局间切换）。

> 混合建议：以 **方案 A 为主体**（统一路由 + 防循环 + 防结算抢占），辅以 **方案 C 的开关**
> 作为兜底（某个 mod 触发异常时直接整局禁用该 mod 的第四幕机制）。

---

## 6. 主要风险

1. **版本漂移**：反射字段/方法名、IL 特征随 mod 更新变动，仲裁表需持续维护。
2. **Dolso 与 Harmony 混用**：act4heart 不在 Harmony 补丁链内，只能通过"去覆盖其结果"间接控制。
3. **联机同步**：`ActChangeSynchronizer` 的投票/就绪流程被 ISE（`FinaleActChangeGuardPatch`）与 a4f 都干预，协调器必须兼容多方投票语义，否则多人局会卡死或不同步。
4. **存档兼容**：runState.Acts 的序列化（`FromSerializable` / `ToSave`）与 a4f 的 Act3 快照、ISE 的临时地图存档交互，协调器改 act 列表必须走与各 mod 相同的持久化路径，否则读档崩溃。
5. **难调试**：各 mod 用 Traverse/反射写内部字段（如 bs2 写 `Acts` 的 backing field），协调器需要在同一底层字段上操作。

---

## 7. 下一步

- [ ] 确定方案取向（A 主体 + C 兜底，或纯 C）。
- [ ] 编写 `EnterNextAct` 仲裁前缀骨架（状态机 + 触发条件表）。
- [ ] 验证各 mod 反射可调用点（bs2 的 `EnterWinterBellChoice`、a4f 的 `ProceedToAct4Async`、ISE 的 finale 动作队列、hs 的 `DescendAsync`）。
- [ ] 处理 act4heart 的 `get_Acts` 副作用与存档路径。
- [ ] 联机 ActChangeSynchronizer 兼容性测试。

---

## 附：关键反编译文件索引

- 游戏：`D:\Download\sts2beta111\src\Core\Models\ModelDb.cs`、`ActModel.cs`、`Runs\RunManager.cs`
- mod 原目录：`D:\SteamLibrary\steamapps\workshop\content\2868840\{3747637213,3756024281,3748937859,3749061519,3747537811,3747602295}`
