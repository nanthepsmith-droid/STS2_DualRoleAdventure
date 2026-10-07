# PreloadStallGuard

给**资源预加载会话**加「停滞检测 + 超时放行」兜底的独立补丁 mod（不属于 `DualRoleAdventure` 主 mod）。

## 它解决什么

原版预加载链路：

```
PreloadManager.LoadAssets(name, paths)
  → AssetCache.CreateSession(...)                      // 构造 AssetLoadingSession，打印 "Preloading 'X' assets..."
  → NAssetLoader.Instance.LoadInTheBackground(session) // 入队 + SetProcess(true)，返回 session.Task
  → NAssetLoader._Process()  每帧只处理 _currentSession // 完成后打印 "Preloading 'X' Complete: ..."
调用方 await session.Task                              // ← 一旦永不完成，调用方永久挂起
```

一旦某个会话**永不完成**，所有等它的代码就永久挂起。如果这发生在**战斗开始**阶段
（`CombatManager.StartCombatInternal` 里 `SetCombatState(NotPlayPhase)` 之后、`await StartTurn(...)` 之前，
中间只隔着 `Hook.BeforeCombatStart`），表现就是：

> 战斗开始后**不自动抽牌**、**不能换人**、**点结束回合没效果**（战斗停在 `NotPlayPhase`）。

2026-10-02 实机的触发源：第三方 **GensokyoSpire（依赖 MomoLib）** 在战斗开始时预加载 `'MomoVfx'`，
该会话从未 Complete（同一段里 `'Combat Room'` / `'Common'` 都正常 Complete）。

## 它怎么做

1. 后缀补丁 `NAssetLoader.LoadInTheBackground`：把刚入队的会话登记进台账（记录首次观察时间 + 队列计数签名）。
2. 自建常驻节点 `PreloadStallGuardWatchdog`（挂 `SceneTree.Root`，`ProcessMode = Always`）每 500ms 轮询：
   - 会话已完成 → 摘除；
   - 队列计数签名（`_toLoad/_loading/_finalizing/_vfxScenes` 四个计数）有变化 → 视为**仍在推进**，刷新时间；
   - **连续 5 秒既没完成、计数也一点没动** → 判停滞，打 `Log.Error` 并强制放行（`_completionSource.TrySetResult(true)`）。
     （阈值 = `PreloadStallPolicy.DefaultStallTimeoutMs` = 5000ms；判据要求「距首次观察」与「距上次计数变化」**都**超过它。）
3. 放行**不假装资源已加载**：后续真取用时走原版「未缓存则同步加载」路径兜底。

设计要点：轮询**不挂**在 `NAssetLoader._Process` 上——本次要救的故障恰恰是「它没跑」，
挂在同一条链路上等于和病人一起躺下。

## 开关

| 环境变量 | 作用 |
|---|---|
| （不设） | 默认 `force`：检测到停滞即强制放行 |
| `STS2_PRELOAD_STALL_GUARD=report` | 只打 ERROR 日志、不干预（用于确认「是不是它卡的」） |

## 日志锚点

```
[PreloadStallGuard] INIT_OK: target=found, probe=ok, timeoutMs=5000, mode=force
[PreloadStallGuard] 轮询节点已挂载: PreloadStallGuardWatchdog
[PreloadStallGuard] 资源预加载会话停滞 5000ms，已强制放行以免流程永久挂起（...）: name=MomoVfx, toLoad=1, loading=1, finalizing=0, vfx=0, released=True
```

排查时用：`python tools\log_scan.py --kw PreloadStallGuard --file <godot.log> --show`

## 实机验证

**✅ 首次实机触发（2026-10-04，`logs-archive/godot__20261004-140108__r205.log`）**：
开局链路里再次撞上同一处病灶（第三方 `MomoLib.Utils.MomoVfxAssets.PreloadInBackground` 预加载 `'MomoVfx'`
的会话不推进），兜底按设计放行：

```
[PreloadStallGuard] 资源预加载会话停滞 5360ms，已强制放行以免流程永久挂起（该批资源可能未预加载，后续按原版未缓存同步加载兜底）: name=MomoVfx, toLoad=3, loading=0, finalizing=0, vfx=0, driven=False, released=True, 累计停滞=1, 在跟踪=1
```

等待者栈（`创建者=…PreloadInBackground ← …MomoVfxPreloadFallbackPatch.Postfix ← NGame+<StartNewMultiplayerRun>…`）
与 2026-10-02 现场同源。用户侧**无感知**（流程未卡，只表现为一次加载等待）。
⚠ 这条是**设计内**日志且用 `Log.Error` ⇒ 会抬高全局 `[ERROR]` 计数（该局 3 → 7），读到别当回归。

## 构建 / 部署

由主仓库 `STS2_DualRoleAdventure-itriedtofix\Scripts\Tools\build_all_mods.ps1` 自动发现本目录
（pain 根下含 `*.csproj` 的目录 = 一个 mod）并统一构建部署：

```powershell
cd D:\Download\pain\STS2_DualRoleAdventure-itriedtofix
.\Scripts\Tools\build_all_mods.ps1            # 或 -List 先看发现结果
```

槽位：`<game>\mods\PreloadStallGuard\PreloadStallGuard.dll`（槽位 json `id` 必须与 dll 主文件名一致）。
