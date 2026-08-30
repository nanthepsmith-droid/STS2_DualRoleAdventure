# Mod 系统维护性改进实施方案

> 日期：2026-08-29
> 对应分析：《mod系统复杂度与长期维护性分析报告.md》第 6 节建议（S1–S5 / M1–M4 / L1–L3）
> 性质：**可执行方案**。按任务分步落地，每步独立分支、独立验收、可回滚。
> 合规基线：中文注释/日志/提交；构建 0 警告 0 错误；部署用仓库根 DLL + `tools/deploy_dll.ps1`；
> 每轮 marker 升位 + 用户实机回归。**所有任务默认不改变现有运行时行为。**

---

## 0. 总体思路

```
目标：把维护从"逐点考古"变成"按清单核对"。

Phase 1 止血（1–2 周）   → 覆盖清单 + 自检 + 适配脚本化 + 文档 + 仓库统一
Phase 2 结构（1 个月）   → 单测 + 决策接口分层 + 补丁隔离 + 发布自动化
Phase 3 长期（不定）     → 反射面收敛 + 领域逻辑外置 + CI（见 §7，不排期）
```

**优先级判断**：三件事立刻有价值——
1. 补丁覆盖清单（工具化）：**首次运行就能暴露存量失效补丁目标**；
2. 启动自检升级：代码量小，直接终结"静默跳过"；
3. 适配脚本化：下次游戏更新直接受益。

建议执行顺序 = 任务 1.1 → 1.2 → 1.3 → 1.4 → 1.5 → Phase 2。

---

## 1. Phase 1 止血

### 任务 1.1 补丁目标覆盖清单（工具 + 文档）★ 最先做

**目标**：让"哪些补丁打在哪、是否已验证"成为可查询、可再生的清单。

**产出物 A：`tools/patch_coverage.py`**
- 扫描 `Scripts/Patch/*.cs`，用正则解析三类声明：
  - 类级 `[HarmonyPatch(typeof(X), "methodName")]` / `[HarmonyPatch(typeof(X), nameof(X.M))]`
  - 类级裸 `[HarmonyPatch]`（多目标容器）+ 方法级 `[HarmonyPatch(...)]`
  - 方法级-only 补丁（**列出警告**，此类会被 PatchAll 静默跳过）
- 输出 `docs/patch-coverage.md`，每行含：`补丁类 | 目标类型 | 目标方法 | 是否字符串目标 | 反编译锚点(可空) | 状态`
- 状态字段来源：若 `sts2src` 存在，自动 grep 目标类型/方法 → `verified`（找到）或 `STALE`（找不到，疑似失效）；`sts2src` 缺失时标 `unverified`。

**产出物 B：`docs/patch-coverage.md`**（首次生成即入库）
- 96 个补丁文件全覆盖；含"方法级-only 补丁警告段"（对照已知的 `CardSelectManualConfirmationPatch` 一类）。

**涉及文件**：新增 `tools/patch_coverage.py`、`docs/patch-coverage.md`；不动 `Scripts/`。

**验收标准**：
1. 一分钟内重新生成清单，结果与入库版 diff 干净（除预期变更）；
2. 清单能列出当前所有字符串反射目标（供任务 1.3 复用）；
3. 首次运行能指出 ≥1 个存量可疑目标（如方法级-only 补丁）。

**工作量**：0.5–1 天。**风险**：低（纯只读工具 + 文档）。

**后续自动化衔接**：此清单可作为任务 1.2 的期望目标表来源、任务 1.3 的核对输入、游戏更新后的 diff 基线。

---

### 任务 1.2 启动自检升级（代码，小改动）

**目标**：补丁被静默跳过时，启动即 ERROR，不再靠人肉发现。

**方案**：
1. `Entry.cs` 增加一个**期望补丁清单常量表**（首批覆盖最关键目标，约 15–20 个）：
   ```csharp
   private static readonly string[] ExpectedPatchTargets =
   {
       "MegaCrit.Sts2.Core.XXX.NPlayerHand.SelectCards",
       "MegaCrit.Sts2.Core.XXX.CardSelectCmd.FromHand",
       // ... 从任务 1.1 清单摘取关键项
   };
   ```
2. `Init()` 末尾：`GetPatchedMethods()` 结果与期望表比对，缺失即 `Log.Error("[LocalMultiControl] 期望补丁缺失(可能被静默跳过): {target}")`；
3. 不自动 fail（避免整个 mod 拒绝加载），但缺失时在日志顶部醒目报错 + 启动后首个回合计数。

**注意**：期望表是**手工维护的关键项子集**，不是全部 140+ 目标（避免清单漂移负担）；全量比对交给任务 1.1 工具按需运行。

**涉及文件**：`Scripts/Entry.cs`（约 +40 行）。

**验收标准**：故意把某补丁的类级标记注释掉 → 启动日志出现 `期望补丁缺失` 的 ERROR，恢复后消失。

**工作量**：半天。**风险**：低。**注意**：此改动会产生新 marker，需一轮构建部署回归。

---

### 任务 1.3 游戏更新适配 playbook 脚本化

**目标**：把 AGENTS.md §5 的人工步骤 2/4（重新反编译、核对字符串目标）变成命令。

**产出物 A：`tools/regenerate_src.ps1`**
- 读 `<game>\release_info.json` 打印版本/commit；
- 检测 `ilspycmd`（缺则提示 `dotnet tool install -g ilspycmd --version 9.1.0.7988`）；
- 执行反编译 → 输出到临时目录 → 覆盖拷贝到 `sts2src`；
- 打印新 `src/` 文件数与 diff 统计（与旧树对比变化文件数）。

**产出物 B：`tools/check_string_targets.py`**
- 扫描 `Scripts/Patch/*.cs` 中**字符串式** Harmony 目标（`[HarmonyPatch(typeof(X), "字符串")]`、`AccessTools.*("...")`、反射 `GetMethod("...")`）；
- 用任务 1.1 的机制在 `sts2src` 中查找目标类型/成员是否存在；
- 输出：`全部目标 | 已验证 | 失效(STALE) | 未知`，失效项给行号；
- 退出码 0/1（1 = 有失效），可进 CI。

**产出物 C**：更新 `AGENTS.md` §5，把步骤 2/4 改为"运行上述脚本"。

**涉及文件**：新增 2 个工具 + `AGENTS.md`；不动 `Scripts/`。

**验收标准**：在现有 `sts2src` 上运行 `check_string_targets.py` 产出全量核对表；`regenerate_src.ps1` 语法检查通过（真实重生成留到游戏更新时验证，避免重复耗时的全量反编译）。

**工作量**：1 天。**风险**：低。

---

### 任务 1.4 文档整理与归档

**目标**：消除"知识只存在于 AI skill / 根目录散乱文档"的单点。

**动作**：
1. 刷新 `docs/维护现状分析.md`（98 文件 → 129 文件、v1.32 → v1.38 数据）；
2. 根目录 8 份可行性/方案分析文档移到 `docs/decision-records/`（**保留原文，不删**），根目录只留《待修复Bug清单》等活文档；
3. skill 的 7 份 references 复制一份进 `docs/references/`（加一行"与 skills 同步维护"说明），让纯人工维护者可读；
4. `CHANGELOG.md` 新增 `[Unreleased]` 记本次维护性改动。

**验收标准**：不看 skill 也能查到全部已踩坑；根目录整洁；无内容丢失。

**工作量**：0.5–1 天。**风险**：低（纯文档）。

---

### 任务 1.5 修复仓库统一

**目标**：消除 4 个独立 fix 仓库的重复脚手架与分散部署。

**方案（保守优先）**：
- 把 4 个 fix 小仓库的源码并入主仓库 `Scripts/Fixes/<ModName>/`，由主仓库统一构建出 4 个 DLL？—— **否决**（改动大、跨项目构建链路复杂、失败风险高，本期不做）。
- **本期只做**：统一**构建/部署脚本**——新增 `tools/build_all_mods.ps1`：
  1. 按目录列表逐个 `dotnet build -c Release`；
  2. 逐个拷贝 DLL 到约定槽位（沿用各仓库既有 json 槽位，不覆盖 json）；
  3. 逐个 SHA256 校验 + 打印 marker；
- 仓库内 `README` 各加一行"由 build_all_mods.ps1 统一构建部署"说明。

**涉及文件**：新增 `tools/build_all_mods.ps1`；各 fix 仓库只加 README 说明。

**验收标准**：一条命令构建+部署全部 5 个 dll（主 + 4 fix），字节校验通过。

**工作量**：0.5 天。**风险**：低。

---

## 2. Phase 2 结构（建议 Phase 1 全部验收后再启动）

### 任务 2.1 纯逻辑单元测试

> **状态：✅ 已完成（2026-08-30，commit 33c0930）**——详见 `pain/维护性改进进度记录.md` §1.6。

**目标**：给最易回归的领域逻辑加保护网。

**可行面（不依赖 Godot 运行时）**：
- `LocalWakuuPotionAutoUse` 的规则匹配/决策（纯数据+枚举判断，抽成静态函数）；
- `LocalWakuuStrategySelector` 的 first/last/random 与排序策略；
- 配置读写（`LocalWakuuAutopilotConfig` 的 JSON 解析/默认值/边界）；
- 药水分类表（65 条规则的类型匹配、条件判定）。

**做法**：
1. 新建 `tests/LocalMultiControl.Tests/`（nunit + Microsoft.NET.Test.Sdk），`<Reference>` 引 `sts2.dll` 与主程序集；
2. **先抽纯函数再测**：把上述逻辑从依赖单例/游戏上下文的调用点抽出为 `internal static` 纯函数（**行为零变化**，纯搬移）；
3. 首批覆盖上述 4 类，预计 30–60 个用例；
4. 构建脚本加入测试步骤：`dotnet test` 0 失败才允许部署。

**涉及文件**：新增测试项目 + 主仓库若干文件抽纯函数（搬移不改逻辑）。

**验收标准**：`dotnet test` 全绿；改乱一条药水规则能被测试抓住。

**工作量**：2–3 天。**风险**：中（需要小心"抽函数不改行为"）。

---

### 任务 2.2 决策接口分层（IWakuuCombatBrain 落地）

**依据**：`瓦库托管优化可行性分析.md` 21.3 的设计稿（接口 + 上下文 + 动作 + `HeuristicWakuuBrain` + `WakuuBrainFactory`）。

**做法**：
1. 新增 `Scripts/Runtime/WakuuBrain/`：`IWakuuCombatBrain` / `WakuuDecisionContext` / `WakuuPlannedAction` / `HeuristicWakuuBrain` / `WakuuBrainFactory`；
2. **`HeuristicWakuuBrain` 把现有出牌循环逻辑原样搬入**（取第一张可打牌 + ResolveTarget + 选择器 + 药水规则调用），行为零变化；
3. `LocalWakuuRelicRuntime` 主循环改为问大脑要"下一步"；
4. 新增开关 `wakuuBrain`（heuristic/auto，默认 heuristic）；
5. 独立分支、独立 marker，先纯接口+默认实现一轮，再切循环一轮。

**涉及文件**：新增 5 文件 + 改 2 文件。

**验收标准**：默认开关下实机行为与 v1.38 无差异（用户对照回归清单确认）；开关可切换。

**工作量**：2 天。**风险**：中（瓦库主循环是核心路径，需要一轮专门回归）。

---

### 任务 2.3 补丁隔离（PatchAll 分组）

**目标**：单个补丁崩溃不再拖垮整个 mod 初始化。

**做法**：
1. 先把补丁按域分组（core / lobby / combat / rewards / wakuu / ui / 第三方适配）；
2. `Entry.cs` 改为分组 `ApplyPatches(group)`：每组合并 try-catch，失败组打 `Log.Error` 并跳过，其余组继续；
3. 注意：**先做影响分析**——确认 wakuu 组依赖 core 组的哪些补丁、失败后运行态是否可降级；对"失败即危险"的核心补丁（如回环网络、同步器）不降级（仍然失败即停）。

**涉及文件**：`Entry.cs` + 各补丁类加组标识（可约定 namespace 后缀或分组常量数组）。

**验收标准**：模拟某 wakuu 补丁抛异常 → mod 仍能进游戏、core 功能可用、日志明确报哪组失败。

**工作量**：1–1.5 天。**风险**：中高（动的是初始化路径），建议排在 2.2 之后。

---

### 任务 2.4 发布自动化

**目标**：消除三处版本号同步/手工打包。

**做法**：新增 `tools/release_build.ps1`：
1. 入参版本号，三处同步（根 `DualRoleAdventure.json` / `workshop/content/DualRoleAdventure.json` / `mod_manifest.json`）；
2. 校验 semver 格式；生成 marker 建议串（`Revival vX.Y.Z (game v0.111.0, marker=YYYY-MM-DD-rN)`）；
3. `dotnet build -c Release`（0 警告 0 错误门禁）；
4. 拷贝 dll+json 进 `workshop/content/`；打 zip 到 `release/`；
5. 打印 SHA256（dll / zip 内 dll），供发布时核对。

**验收标准**：一条命令产出发布包，三处版本一致，SHA256 可核验。

**工作量**：0.5–1 天。**风险**：低。

---

## 3. 每轮执行闭环（所有任务通用）

```
新建分支(如 chore/patch-coverage) → 实现 → dotnet build -c Release (0警告0错误)
→ 部署(deploy_dll.ps1) → marker 校验(dll_check.py --deployed --marker ...)
→ 用户实机回归(给聚焦步骤) → 通过后询问合并 master → 每逻辑变更一个 commit(中文)
```

---

## 4. 依赖关系与排期建议

```
1.1 补丁覆盖清单 ──────────┐
                           ├→ 1.3 适配脚本化（复用清单解析）
1.2 启动自检 ──────────────┘
1.4 文档整理（可与 1.1 并行，低依赖）
1.5 仓库统一（独立，随时可做）

Phase 1 全部验收 → 2.1 单测 → 2.2 决策接口 → 2.3 补丁隔离 → 2.4 发布自动化
```

| 任务 | 预估 | 风险 | 建议顺序 |
|---|---|---|---|
| 1.1 覆盖清单 | 0.5–1 天 | 低 | **1（最先）** |
| 1.2 启动自检 | 半天 | 低 | 2 |
| 1.3 适配脚本化 | 1 天 | 低 | 3 |
| 1.4 文档整理 | 0.5–1 天 | 低 | 4（可并行） |
| 1.5 仓库统一 | 0.5 天 | 低 | 5 |
| 2.1 单元测试 | 2–3 天 | 中 | 6 |
| 2.2 决策接口 | 2 天 | 中 | 7 |
| 2.3 补丁隔离 | 1–1.5 天 | 中高 | 8 |
| 2.4 发布自动化 | 0.5–1 天 | 低 | 9 |

---

## 5. 每项任务的回滚预案

- 1.1 / 1.3 / 1.5：纯新增工具 + 文档，不改变运行时，**无需回滚**（不想要删文件即可）；
- 1.2：只加日志/自检，不改变逻辑；若误报烦人，删期望表项即可；
- 2.1：抽纯函数是搬移；如回归，`git revert` 该 commit；
- 2.2：开关默认 heuristic 即旧行为；关掉 `wakuuBrain` 即回退；
- 2.3：保留"失败即停"开关，可整体关闭分组容错回到 PatchAll 直跑。

---

## 6. 与既有工作流的衔接

- 本方案任务之间互相独立、顺序可调；已排期的功能开发（Phase 3/5 聪明决策等）不受影响，做完 2.2 反而给 Phase 5 铺好了接口；
- 所有任务都不改变默认运行时行为（1.2/2.2/2.3 的默认路径与现状一致）；
- 每轮 marker 升位独立，可与功能开发并行（不同分支）。

---

## 7. 长期方向（L1–L3，本期不排期，仅记录）

| 方向 | 说明 | 前置 |
|---|---|---|
| L1 反射面收敛 | 游戏更新时把能具名化的目标替换掉反射 | 每次适配顺带做 |
| L2 领域逻辑外置 | 瓦库"大脑"做成独立模块/mod | 2.2 接口就绪后 |
| L3 CI | GitHub Actions：构建 + 单测 + 补丁目标 diff | 2.1 / 1.1 就绪后 |

---

## 8. 立即行动建议

**从任务 1.1 开工**（纯只读工具 + 文档，零运行时风险，且首次运行就能暴露存量问题）。若你确认，我即：
1. 新建分支 `chore/patch-coverage`；
2. 实现 `tools/patch_coverage.py` + 生成 `docs/patch-coverage.md`；
3. 给你清单结果（含方法级-only 警告段与 STALE 目标），再决定后续顺序。
