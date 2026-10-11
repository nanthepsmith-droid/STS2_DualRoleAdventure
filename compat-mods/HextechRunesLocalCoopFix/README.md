# HextechRunesLocalCoopFix

让**海克斯大乱斗（HextechRunes）的「逐席位符文选择」在本地多控下可用**的独立补丁 mod
（不属于 `DualRoleAdventure` 主 mod，模板同 `PreloadStallGuard` / `YuWanCardWhiteScarfFix`）。

## 现象（用户原话）

> 玩海克斯符文 mod，切换玩家后**符文的界面不会跟着切**，第二个玩家的符文选不了 ⇒ **直接软锁**。

日志锚点（`logs-archive/godot__20261004-161256__r205.log`）：

```
[LocalMultiControl] 非共享事件房间已按当前角色重建: player=76561198422527327, event=TOUHOUANCIENTS-DOREMY_SWEET_ANCIENT,
                     isFinished=True, overlay=screenCount=1, top=HextechRuneSelectionScreen[inTree=True]   ← 界面一直在，但只有主席位的
[WARN] [HextechRunes][Mayhem] WaitForRemoteHextechChoice: still waiting context=rune-choice act=0 ordinal=0
       player=76561198422527327 choiceId=0                                                              ← 永远等不到
```

## 根因

海克斯的选择流程按**原版联机**写：判「这一席是不是本机玩家」用
`HextechRuneSelectionCoordinator.IsLocalPlayer(player)`，等价于

```csharp
LocalContext.IsMe(player) || player.NetId == runManager.NetService.NetId
```

一个客户端只有**一个**「本机玩家」（`LocalContext.NetId` / 回环 sender），所以：

| 席位 | 判定 | 走的路径 |
|---|---|---|
| 主席位（回环 sender） | local = true | 弹出**它自己**的符文界面，等人点 |
| 其余本地席位 | local = false | `WaitForRemoteHextechChoice`：等一个**远端玩家**的选择消息 |

单进程回环里**不存在**另一个客户端 ⇒ 那条远端等待永远没人回
（`... still waiting ... player=…327 choiceId=0` 就是这么来的）⇒ 批次 `Task.WhenAll` 永不完成
⇒ `SyncLocalChoice` 不会发生 ⇒ 行动/回合流程挂起。

同时因为只有主席位会弹界面，**切人不会切换符文界面**（那个界面属于主席位的事件/流程），
用户看到的就是「界面切不过去、第二个玩家的符文选不了」。

⚠ 我方既有的两条兜底都够不着：
① `PlayerChoiceSynchronizerRemoteChoiceFallbackPatch` 拦的是 `WaitForRemoteChoice`，
海克斯是**自己订阅 `PlayerChoiceSynchronizer.PlayerChoiceReceived`**（还带自己的 1800 帧轮询），根本不经过它；
② `LocalContextThirdPartyIsMePatch` 的放行口子只在「后台托管瓦库席位正在被我们自动化」时开，
这里两个席位都是**真人**。

## 做法（全反射，逐个挂、失败只 WARN）

只在「**主 mod 本地多控回环会话** + **该席位是本地席位**」成立时生效：

| # | 补丁点 | 作用 |
|---|---|---|
| ① | `HextechRuneSelectionCoordinator.IsLocalPlayer` 后缀 | 把本地席位也判成「本机玩家」⇒ 每个席位都弹自己的符文界面，不再有无人作答的远端等待 |
| ② | `HextechRuneSelectionScreen.RelicsSelected(bool)` 前缀 | 把 `removeOverlay=false` 改成 `true`。原版联机的「选完保持界面显示"等待其他玩家"」在同机多席位下有个副作用：两个席位的界面会**同时**入栈叠起来，只有栈顶可交互 ⇒ 选完不关就永远轮不到下面那个。改成选完即关，两个界面就能逐席位依次作答 |
| ③ | `CreateLocalRuneSelectionScreenAsync` 前缀 + `CreateRuneSelectionScreenAsync` 前缀 | 记下「这次是给哪一席建界面」，再给标题补「· 角色N」（符文逐席位，不标注就分不清在给谁选）。⚠ 标题刻意挂在**异步方法**上而不是小的同步工厂 `HextechRuneSelectionScreen.Create` 上：小方法会被 JIT 内联、内联副本不走 Harmony 入口（本仓库同类坑见 skill references「坑 S」第 5 条） |
| ④ | `HextechRuneSelectionScreen.DismissAfterSelectionComplete` 前缀 | ② 的收尾兜底：界面被提前关掉后，海克斯批次的 finally 仍会对**已释放**节点调收尾方法（它自己 catch 成 `Failed to dismiss blocking rune selection screen: ObjectDisposedException` 的 WARN，v1.0.0 实测 ×5）。功能无影响，但那是我们引入的噪声 ⇒ 节点不可用就跳过 |
| ⑤ | ③ 的归属绑定 + `RelicsSelected` 前缀里的自动作答（v1.1.0） | ③ 记下的「这一屏属于哪一席」在 `RelicsSelected` 被等待时绑到界面实例；只有该席位是**后台托管中的瓦库**时，才延迟 1.2s 走游戏自己的点击路径（候选按钮的 `Pressed` 信号）替它选一个符文。真人席位的符文界面**一律不碰** |
| ⑥ | `CreateEnemyHexAdjustmentOptionsForSelection` **后缀**（v1.2.0 / 判定 v1.2.1 修正） | 「敌人 hex 调整面板」的归属收口：这一屏的席位 ≠ **可调席位**时，把刚建好的那份 options 改成**只读**（`ControlsEnabled=false` + `RerollFunc`/`Changed`/`ScreenCreated` 清空）⇒ 面板照旧出现但没有 reroll/移除按钮，也不往同步里发任何东西。**「可调席位」= 权威席位若是真人就是它，否则给第一个真人本地席位**（v1.2.1：权威席位常常正是一个被 ⑤ 自动作答的托管瓦库席位 ⇒ 若把可调权给它，真人那屏就只能看不能动） |
| ⑦ | `CompleteLocalEnemyHexAdjustmentSync` **前缀**（v1.2.0） | 只读那一屏**不发 final**（判据 = 界面自己的私有字段 `_enemyHexControlsEnabled`，**实例准确**）——否则它会用创建时的初始值把可调席位刚调好的结果覆盖掉 |
| ⑧ | `HextechForgeSelectionCoordinator.SelectLocalForge` 前缀（⑧a）+ `CreateForgeSelectionScreenAsync` 前缀（⑧b）（**v1.2.3**） | **锻造器路径**（商店买的【随机属性锻造器】/ 锻造奖励 / 随机锻造发放）走的是**另一个协调器**，它自带标题、自己建屏，**不经 ③a/③b** ⇒ 原来归属永远是 0、⑤ 无从代答（瓦库那一屏只能真人手点）。⑧a 挂在**唯一建屏前的那一步**记下归属席位，⑧b 在建屏时登记 `_seatForNextScreen`（同 ③a/③b 的两步法；自带标题不动标题） |

- 不引用海克斯 dll（类型按全名反射），海克斯缺席/更新改名 ⇒ 本补丁整体退化为「不干预」；
- 不改游戏本体行为、不动海克斯的数值与发牌逻辑；
- 与主 mod 之间只通过「席位身份唯一入口」单向读取：`LocalSeatSource.IsLocalSeat`、`LocalSelfCoopContext.GetSlotLabel`、
  `LocalSelfCoopContext.IsEnabled` / `UseSingleAdventureMode`、`LocalWakuuAutopilotConfig.BackgroundMode`、
  `LocalWakuuRelicRuntime.IsVakuuFormModeById`（主 mod 缺席时退化为「本次 Run 的玩家都算本地」，并打一条 WARN）。

### ⑤ 瓦库席位的符文界面自动作答（v1.1.0）—— 细节

用户 2026-10-05 实测确认「瓦库席位会弹自己的符文界面，然后需要真人手点」，故补上这一环。
**只在「后台托管中的瓦库席位」生效**（与主 mod 纯逻辑 `WakuuSelfDrawnChoicePolicy.IsManagedWakuuSeat` 同口径：
本地多控启用 + 单人冒险档 + 本地回环 + 本地席位 + 后台托管档 + 瓦库形态）；真人席位一律不代点。

时序与姿势（每一步都有日志，便于实机核对）：

| 步 | 做什么 | 为什么 |
|---|---|---|
| 1 | 延迟 **1.2s** 再按 | 界面自带 **1000ms 点击保护窗**（`AfterOverlayOpened` 起算的 `IsSelectionConfirmGuardActive`），早按会被它吞掉 |
| 2 | 等这一屏 **露出来**（`IsVisibleInTree`）再按，每 0.6s 重试、最多约 24s | 同机多席位的符文界面会**同时入栈**，只有栈顶可交互 ⇒ 逐席位依次作答 |
| 3 | 按「候选按钮的 `Pressed` 信号」 | 走游戏自己的点击路径（海克斯把 `Pressed` 连到 `OnHolderSelected`），不碰任何私有方法 |
| 4 | 按完 0.5s 核对界面是否关闭 | 关了 = 完成；还开着按它自己的收尾按钮：**自选池**屏再按「确认」、海克斯设置里开了**「确认符文选择」**时再按符文确认；仍未关 ⇒ 重按一次，再不行 WARN 交回真人 |

**选哪一个符文**：**取最左**（v1.2.2 用户实测口径：海克斯每次给的 3 个选项**稀有度一致**
⇒ 原「稀有度优先（棱彩 > 金 > 银）+ 同档取最左」没有区分度，整节已删）。
每次作答仍把候选打进日志（`options=[…, …], picked=…`）⇒ 想换规则只要改
`WakuuRuneAutoAnswer.TryAnswer` 里那一处 `index`。

📌 后续排期（用户 2026-10-11 点名）：给本补丁加**抓率统计**（仿主 mod 的 `personal_stats.json`）+
按抓率选符文 + **选项很烂时自主 roll**（⚠ 判据不能用稀有度 —— 3 个选项稀有度一致）。见 `TODO.md`。

## 日志锚点

```
[HextechRunesLocalCoopFix] 检测到海克斯程序集加载: HextechRunes，尝试挂补丁。
[HextechRunesLocalCoopFix] INIT_OK: patched=[HextechRuneSelectionCoordinator.IsLocalPlayer,
    HextechRuneSelectionScreen.RelicsSelected, HextechRuneSelectionCoordinator.CreateLocalRuneSelectionScreenAsync,
    HextechRuneSelectionCoordinator.CreateRuneSelectionScreenAsync,
    HextechRuneSelectionScreen.DismissAfterSelectionComplete,
    HextechRuneSelectionCoordinator.CreateEnemyHexAdjustmentOptionsForSelection,
    HextechRuneSelectionCoordinator.CompleteLocalEnemyHexAdjustmentSync,
    HextechForgeSelectionCoordinator.SelectLocalForge,          ← v1.2.3 起：⑧a
    HextechForgeSelectionCoordinator.CreateForgeSelectionScreenAsync], ← v1.2.3 起：⑧b
    missing=[], source=init
[HextechRunesLocalCoopFix] 敌人 hex 调整面板：已按「只有权威席位可调」收口（非权威席位的面板变只读、不发 final；**不做**远端镜像）。syncContext 实参下标=7, selection 实参下标=8
[HextechRunesLocalCoopFix] 瓦库席位符文界面自动作答: 已就绪（后台托管瓦库席位由我们代选，真人席位照旧自己点）。
[HextechRunesLocalCoopFix] 本地席位已按「本机玩家」放行（…）: player=76561198422527327
[HextechRunesLocalCoopFix] 将为该席位创建符文界面（…）: player=76561198422527327
[HextechRunesLocalCoopFix] 符文界面已标注归属（…）: player=76561198422527327, title=<默认标题> · 角色2
[HextechRunesLocalCoopFix] 瓦库席位符文界面已登记自动作答（延迟 1.2s 避开界面自带的 1s 点击保护窗）: seat=角色2(…327), screen=HextechRuneSelectionScreen
[HextechRunesLocalCoopFix] 瓦库符文界面自动作答: seat=角色2(…327), mode=符文选择, options=[RUNE_A, RUNE_B, RUNE_C], picked=RUNE_A, rule=取最左（3 个选项稀有度一致）, screen=HextechRuneSelectionScreen
[HextechRunesLocalCoopFix] 瓦库符文界面自动作答完成（界面已关闭）: seat=角色2(…327), screen=HextechRuneSelectionScreen
[HextechRunesLocalCoopFix] 符文界面选完即关闭（…）: screen=HextechRuneSelectionScreen, 最近创建席位=76561198422527327
# ↓ 锻造器路径（v1.2.3，商店买【随机属性锻造器】/ 锻造奖励）走的是另一组锚点：
[HextechRunesLocalCoopFix] 将为该席位创建锻造选择界面（商店的【随机属性锻造器】/ 锻造奖励走的是另一个协调器）: player=76561198422527327
[HextechRunesLocalCoopFix] 符文界面已登记归属（锻造选择屏：自带标题、不改标题，但要能自动作答）: player=76561198422527327
（之后与上面同型：`已登记自动作答` → `自动作答: … picked=…` → `自动作答完成（界面已关闭）`）
```

| 观察 | 含义 |
|---|---|
| `INIT_OK` + `patched=[…9 项…]`, `missing=[]` | 补丁全挂上（v1.1.0 是 5 项；v1.2.0 起 +2 项 = 敌人 hex 面板归属；**v1.2.3 起再 +2 项 = 锻造器路径**） |
| `将为该席位创建锻造选择界面` → `符文界面已登记归属（锻造选择屏…）` | **v1.2.3 的关键锚点**：商店买的【随机属性锻造器】的屏已登记归属（旧版这两条不会出现 ⇒ 瓦库那屏只能真人手点） |
| `INIT_DEGRADED` / `missing=[…]` | 海克斯更新改名了，需要按新名字适配（日志点名是哪一项） |
| `瓦库席位符文界面自动作答: 已就绪` | ⑤ 的私有字段解析成功（`未就绪` = 字段改名，自动作答退化为「真人手点」） |
| 一个席位一条 `已按「本机玩家」放行` | 该席位不再走远端等待（复现修复的关键锚点） |
| `将为该席位创建符文界面` 后紧跟 `符文界面已标注归属` | 标题标注生效（只有前者没有后者 = 建界面那一环的补丁被内联跳过，贴日志报维护者） |
| 出现 `… · 角色2` 的标题 | 逐席位界面都弹出来了 |
| `符文界面归属席位不是「后台托管瓦库」，交真人作答` | ⑤ 把这一屏判给了真人（正常：真人席位本来就该自己选） |
| `瓦库符文界面自动作答: … picked=…` | 瓦库席位已被代选（关键锚点） |
| `瓦库符文界面没有可选项（例如「敌人 hex 预览」屏），交回真人` | 该屏没有符文候选（本轮未适配的屏，见「已知限制」） |
| `瓦库符文界面单击后仍未关闭…交回真人` | 界面被第三方改写过多选语义 ⇒ 交真人（把日志发我，按它适配） |
| 仍有 `WaitForRemoteHextechChoice: still waiting` 且没有本 mod 的锚点 | 补丁没生效（看 INIT 行），贴日志继续查 |
| `敌人 hex 面板归属补丁：目标方法没找齐 / 参数名对不上 / 界面字段没解析齐，跳过` | 海克斯更新改名 ⇒ ⑥⑦ **整体退化**为「不干预」（保留旧行为：两席都能调，后作答那屏的 reroll 不生效） |
| `后缀已生效` / `final 收口前缀已生效`（每次启动各一条） | ⑥⑦ 真的挂上了（**这两条缺失** = 目标方法被 JIT 内联跳过，把日志发维护者） |
| `敌人 hex 调整面板：非可调席位改为只读…: 席位=角色N, 可调席位=角色M, 原权威=角色K` | 该席位的面板已收成只读（它不该有 reroll 按钮）；`可调席位` 才是你可以去 reroll 的那一屏（v1.2.1 起：K 若是托管瓦库会与 M 不同） |
| `敌人 hex 调整面板：只读那一屏不发 final` | ⑦ 生效（该屏不抢 final） |

排查命令：`python tools\log_scan.py --kw HextechRunesLocalCoopFix --file <godot.log> --show`

## 实机验证

**✅ 2026-10-04 实机通过**（`logs-archive/godot__20261004-180316__r205.log`，marker r205 / `INIT_OK`；用户反馈「测过了感觉没问题」）：

| 观察 | 修前（`godot__20261004-161256__r205.log`） | 修后（本次） |
|---|---|---|
| `WaitForRemoteHextechChoice: still waiting` | 1（永久挂起） | **0** |
| `WaitForRemote*` 总数 | 1 | **0** |
| 逐席位符文界面 | 只有主席位 1 个、切人不换 | **两席各 1 个**（`· 角色1` / `· 角色2`，同批创建 + 各 1 条「选完即关闭」） |
| 我方 `[ERROR]` / 新增 WARN 族（`--warn-diff`） | 0 / 无 | **0 / 无** |

`INIT_OK: patched=[…4 项…], missing=[], source=init`（v1.0.0 时；v1.0.1 起 +`DismissAfterSelectionComplete`）；
全局 878 条 `[ERROR]` 与修前同源（871 条 `ModManager.CheckSteamBranchSupport` 噪声 + 6 条游戏原生
`NHeavyBluntVfx.PlaySequence` NRE + 启动器/存档噪声；`All rewards have been taken…` 是跨 r199/r204 既有族）。
唯一**新增**噪声 = v1.0.0 的 `Failed to dismiss blocking rune selection screen: ObjectDisposedException` ×5
⇒ **v1.0.1 已补 ④ 收敛**（待下一局日志确认该 WARN 归零）。

**✅ v1.1.0（⑤ 瓦库席位自动作答）—— 2026-10-05 实机通过**
（dll sha256 `5A82D03D29A2…` —— ⚠ 本 mod **不是确定性构建**，每次 rebuild 的 hash 都会变，这里只是当次快照；
判"改动真的进去了"看 `build_all_mods.ps1` 的部署字节校验 + 关键日志文案。
证据 `logs-archive/godot__20261005-173455__r205.log`，marker r205 / `INIT_OK`）：

启动 `瓦库席位符文界面自动作答: 已就绪`；两处符文批次里 **角色1（真人）** 得到
`符文界面归属席位不是「后台托管瓦库」，交真人作答`、**角色2（瓦库）** 得到
`已登记自动作答` → `自动作答: options=[棱彩:TAP_DANCE_RUNE, …], picked=TAP_DANCE_RUNE[棱彩]` →
`自动作答完成（界面已关闭）`；其中一屏先 `单击后仍未关闭，重试一次` 再成功关闭（保护窗/晚半拍的重试链路按设计生效）。
用户反馈「大概没问题」。⇒ **⑤ 关单**。

验证契约（用户侧，留档）：

```
SETUP:   本地多控 2 席（其中 1 席是【后台托管】的瓦库形态）+ 海克斯大乱斗；部署位 dll = v1.1.0
ACTION:  走到弹符文选择的地方（第一幕开局 / 事件给的符文）
PASS:    ① 启动日志有 `瓦库席位符文界面自动作答: 已就绪`；
         ② 瓦库那一屏出现 `已登记自动作答` + `自动作答: … picked=…` + `自动作答完成（界面已关闭）` 三条；
         ③ 瓦库的符文界面**一闪而过**（真人不用点），且它确实拿到了符文（后续回合能看到/打出该符文）；
         ④ 真人自己那一屏**照旧等你自己点**（有 `符文界面归属席位不是「后台托管瓦库」，交真人作答`）。
FAIL:    瓦库那一屏仍需手点 ⇒ 看有没有 `…没有可选项…` / `…单击后仍未关闭…` / `…一直没轮到显示…`
         三种 WARN 之一（连同上下文发我），或看 `自动作答: 未就绪`（= 字段改名）。
期望 0： 我方 [ERROR] / ### Exception ### / 幽灵弹层 / waitForRemoteHextechChoice 仍等待
```

**❌ v1.2.0 首测结果（2026-10-08，marker r238）**：用户「海克斯我也**不能 roll 了**」。
日志（`logs-archive/godot__20261008-214742__r238.log`）三条锚点全绿
（`后缀已生效` / `非权威席位改为只读…: 席位=角色1, 权威=角色2`），**但判据错了**：权威是**托管瓦库席位**（角色2
正是被 ⑤ `已登记自动作答` 的那一席）⇒ 真人（角色1）被收成只读。⇒ v1.2.1 改判据（见上）。

**⭐ v1.2.1（⑥⑦ 敌人 hex 调整面板归属，判据修正）—— 待实机复测**

验证契约（用户侧，留档）：

```
SETUP:   本地多控 2 席（1 席真人 + 1 席【后台托管瓦库】最好，正是本次复现配置）+ 海克斯大乱斗；部署位 dll = v1.2.1
ACTION:  走到某幕开局 roll 出「本幕怪物 hex」的地方（符文界面里带敌人 hex 预览的那一屏）
PASS:    ① 启动日志 `INIT_OK: patched=[…7 项…]` + `敌人 hex 调整面板：已按「只有权威席位可调」收口…`；
         ② 日志有 `后缀已生效`（⑥ 挂上了）+ `非可调席位改为只读…: 席位=<托管席位>, 可调席位=<你的席位>, 原权威=<托管席位>`
            ⇒ **可调席位 = 你自己那一屏**；
         ③ 你自己那一屏有 reroll/移除按钮，托管那一屏只有预览；
         ④ 在**你自己那一屏** reroll 后点确定 ⇒ **实际生效的就是你刚 roll 的那个**（不再是"之前那个"）；
         ⑤ 符文选择 / 回合推进照旧正常（没有卡在敌人 hex 这一屏）。
FAIL:    你那一屏还是没有 reroll 按钮 ⇒ 把 `非可调席位改为只读…` 那一行（连同它前面的 `后缀已生效`）发我：
         `可调席位` 若不是你 → `IsAutomatedWakuuSeat` 对托管席位的判定没命中（看有没有
         `自动作答: 已登记`/`交真人作答` 那几行，它们的判据同源）；若整行缺失 ⇒ ⑥ 没跑（看 `后缀已生效`）。
期望 0： 我方 [ERROR] / ### Exception ### / 幽灵弹层 / waitForRemoteHextechChoice 仍等待 / 卡死在这一屏
```

### ⑧ 锻造器路径（v1.2.3，2026-10-11）—— 商店的【随机属性锻造器】

用户原话：**「商店瓦库买海克斯符文属性锻造器还是不会自动选」**（此前 v1.2.2 已"修"过一次，复测仍不行）。

**真因**（反编译 `lib/0.111.0/HextechRunes.dll` 核对，`HextechRunes.decompiled.cs` 行号）：

| 环节 | 事实 |
|---|---|
| 购买入口 | `MerchantRelicEntry.OnTryPurchase` 被海克斯前缀拦下 → `PurchaseRandomForge`（行 25646）→ `HextechForgeSelectionCoordinator.SelectForge(player, options, "shop", syncMultiplayerChoice: false)`（行 25665） |
| 建屏 | `SelectForge` → `SelectLocalForge`（行 64570）→ **`CreateForgeSelectionScreenAsync(options)`（行 64607）** 自建屏（`HEXTECH_FORGE_SELECTION_TITLE` + `metadataMode: Forge`） |
| 与 ③ 的关系 | 这个建屏方法**不是** ③a/③b 挂的那两个 `Create*RuneSelectionScreenAsync` ⇒ 归属字段永远是 0 |
| 结果 | ⑤ 从来没有为这一屏排程 ⇒ 瓦库那一屏**只能真人手点**（v1.2.2 的 ③b 解耦修的是"自带标题"，但这一屏压根不经过 ③b） |

⚠ **教训**：v1.2.2 的根因判断是**猜的**（"自带标题的屏被 ③b 提前 return"），没有先去反编译看这条链的调用点。
复测日志（`logs-archive/godot__20261011-145339__r267.log`）里 ③/⑤ 的锚点**一条都没有**
（`将为该席位创建符文界面` / `符文界面已标注归属` / `选完即关闭` 全缺）正是这个的实证 ——
**"没有我们自己的锚点"本身就是定位线索**（对比 `godot__20261011-132615__r265.log`：普通符文屏三条齐全）。
另注：海克斯自己的 `HextechLog` 默认**不输出**（`HEXTECH_VERBOSE_LOG=1` 才打，源码 `HextechLog.Verbose`），
所以"日志里没有海克斯的 ForgeChoice 行"**不能**当证据。

**修法**：⑧a `SelectLocalForge` 前缀（唯一建屏前的那一步）记下归属席位 → ⑧b `CreateForgeSelectionScreenAsync`
前缀消费并登记 `_seatForNextScreen`（自带标题 ⇒ 只登记、不改标题）⇒ ⑤ 照旧代答。
覆盖面顺带包含 `obtain-random-forges` / `{source}` / `reward` 三个调用点（同一个 `SelectLocalForge`）。

**⚠ 未覆盖 / 已知限制**：`HextechRelicOptionSelectionCoordinator.SelectRelicOption`（遗物三选一）
用的是**原版** `NChooseARelicSelection.ShowScreen(options)`（不是海克斯自己的屏）⇒ ⑤ 代答不适用；
海克斯内部目前**没有**调用它（只有一行对外暴露的包装 `SelectRelicOption`），暂不处理。

验证契约（用户侧，留档）：

```
SETUP:   本地多控 2 席（1 席真人 + 1 席【后台托管瓦库】）+ 海克斯大乱斗；部署位 = 主 mod marker r268 +
         补丁 mod v1.2.3（槽位 json 的 version 应为 1.2.3）
ACTION:  进商店 → 让瓦库买到【随机属性锻造器】（250 金；日志 `瓦库商店自动买遗物成功: … relic=RANDOM_FORGE_SHOP_RELIC`）
PASS:    ① 启动日志 `INIT_OK: patched=[…9 项…]`（含 SelectLocalForge / CreateForgeSelectionScreenAsync）；
         ② 买下后紧跟 `将为该席位创建锻造选择界面…` + `符文界面已登记归属（锻造选择屏：自带标题…）`；
         ③ 再跟 `瓦库席位符文界面已登记自动作答` → `瓦库符文界面自动作答: … picked=…` →
            `自动作答完成（界面已关闭）` ⇒ **界面一闪而过、不用你点**，且瓦库确实拿到了那件锻造遗物；
         ④ 真人自己买同款时**照旧等你自己点**（有 `符文界面归属席位不是「后台托管瓦库」，交真人作答`）。
FAIL:    买完界面停住要手点 ⇒ 看有没有 ② 那两条：只有 ① 没有 ② ⇒ ⑧a/⑧b 没命中（贴日志）；
         有 ② 没有 ③ ⇒ 看 `自动作答: 未就绪`（字段改名）或 `…没有可选项…` / `…单击后仍未关闭…`（贴日志）。
期望 0： 我方 [ERROR] / ### Exception ### / 幽灵弹层 / 卡在锻造界面
```

## 复现 / 验证步骤（用户侧）

1. 关游戏 → 确认 `mods\HextechRunesLocalCoopFix\` 槽位里 `HextechRunesLocalCoopFix.{dll,json}` 都在（游戏内 mod 列表里启用它）。
2. 进「本地·单人多角色」，开一局带**海克斯大乱斗**的 run，走到会弹符文选择的地方（第一幕开局 / 事件给的符文）。
3. 期望：每个席位各弹一次符文界面，标题带「· 角色1 / 角色2」；选完一个自动露出下一个；两个都选完流程继续。
   **其中后台托管的瓦库席位由本补丁自己选**（它那一屏一闪而过），真人席位照旧自己点。
   该幕 roll 了「本幕怪物 hex」时还有一条：**只有「可调席位」那一屏带 reroll/移除按钮**
   （v1.2.1 起可调席位 = 真人席位；托管瓦库席位只读），另一屏只有预览；
   在你那一屏 reroll + 确定 ⇒ 生效的就是你刚 roll 的那个（见上方验证契约）。
4. 取日志看上面三个锚点。

## 构建 / 部署

由主仓库脚本自动发现（**2026-10-05 起本 mod 已迁入主仓库 `compat-mods/`**；
发现规则 = pain 根下含 `*.csproj` 的目录 + 主仓库 `compat-mods\*\*.csproj`）：

```powershell
cd D:\Download\pain\STS2_DualRoleAdventure-itriedtofix\compat-mods\HextechRunesLocalCoopFix
dotnet build HextechRunesLocalCoopFix.csproj -c Release      # 0 警告 0 错误

cd D:\Download\pain\STS2_DualRoleAdventure-itriedtofix
.\Scripts\Tools\build_all_mods.ps1 -Only HextechRunesLocalCoopFix -DeployOnly
```

槽位：`<game>\mods\HextechRunesLocalCoopFix\HextechRunesLocalCoopFix.dll`
（槽位 json 的 `id` 必须与 dll 主文件名一致；脚本只部署 dll，json 首次需手工放入槽位）。

## 已知限制

- ③ 的归属标注依赖「`CreateLocalRuneSelectionScreenAsync` → `CreateRuneSelectionScreenAsync`
  在同一个同步窗口内先后执行」（两个方法都是异步的，`NOverlayStack` 可用时它们一路同步跑下去）。
  极端情况下（取不到弹层栈而让出帧、又插进了别的席位创建）标题可能不标或标错席位 ——
  只影响显示，不影响可玩性（日志里会打出实际标题）。

### 瓦库席位的符文界面（v1.1.0 已自动作答）

补丁①按「本地席位」放宽，而**瓦库席位也是本地席位** ⇒ 瓦库的那一份符文界面同样会弹出来。
用户 2026-10-05 实测确认它当时需要真人手点（**不会软锁**，但要多点一下），故 v1.1.0 补了 ⑤：
**后台托管中的瓦库席位由本补丁自己选**（延迟 1.2s → 按候选 `Pressed` → 校验关闭，详见上方「⑤ 细节」）；
真人席位照旧自己点。

- 选符文规则 = 稀有度优先 + 同档取最左（读不到稀有度就取最左）。**这有意做得简单**：
  真要"按牌组/协同挑符文"得先把符文数据引进来（本轮不做），换规则只要改 `WakuuRuneAutoAnswer.PickIndex`。
- ⑤ 的归属绑定与 ③ 共用「同一个同步窗口」的前提（见上面第一条限制）⇒ 极端并发下**理论上有错绑可能**；
  表现是「本该真人点的那一屏被代选」或「瓦库那屏仍需真人点」，日志里都点名了席位，遇到再按日志修。
- 另有未适配的屏：**没有符文候选的屏**（敌人 hex 预览 / 「无可选符文」提示）仍要真人点一下确认
  （日志 `…没有可选项…交回真人`）；这类屏不归属到某个席位，本轮刻意不碰。
- 另注：海克斯**自己也识别瓦库形态**（`HextechRunes.VakuuTurnController` / `HextechAutoPlayHelper.AutoPlayOrMoveToResultPile`），
  瓦库 + 海克斯的交叉点可能不止符文界面，以实机日志为准。

### 敌人 hex 调整面板的归属（✅ v1.2.0 已收口为「只有权威席位可调」；⭐ 待实机复测）

用户原话（2026-10-08）：「**每层给敌方 buff 可以 roll 一次，roll 完点确定却还是之前那个**」。
海克斯每幕开局会 roll「本幕怪物 hex」，并允许**权威席位**在符文界面里顺手调整（reroll / 移除）。

**病灶的机制**（反编译 `HextechRunes.dll` v0.111.0 核对过方法/字段名，另用反射离线核对过一遍）：

| 环节 | 原版判定 | 补丁① 之后实际发生 |
|---|---|---|
| 建调整面板 | `CreateEnemyHexAdjustmentOptionsForSelection` 里 `controlsEnabled = syncContext != null && IsLocalPlayer(runManager, syncContext.AuthorityPlayer)` | 补丁①把**两席**都判成「本机玩家」⇒ `controlsEnabled` **对两屏都为 true** ⇒ 两屏**都可 reroll**、都挂 `Changed`（每次改动就 `SendEnemyHexAdjustment(…, isFinal: false)`） |
| 收尾 | `CompleteLocalEnemyHexAdjustmentSync` 里 `if (IsLocalPlayer(runManager, syncContext.AuthorityPlayer))` 才发 final | 同样两屏都发 ⇒ 而 `SendEnemyHexAdjustment` 开头 `if (syncContext.FinalSent) return;` ⇒ **只有先作答的那一屏的 final 生效**，后一屏的 reroll 被静默丢弃（原版只在「面板不可调」那一屏才挂远端镜像，所以它显示的还是自己那份） |

⇒ 表现完全吻合用户描述：在**后作答**的那一屏 reroll + 确定 ⇒ 实际生效的是**先作答那屏**（= 调整前的初始值）。

**修法（= ⑥⑦，用户 2026-10-08 拍板选 A）**：把原版「只有权威席位可调」在单进程里还原出来 ——
按**这一屏的席位**（`selection.Player` 与「可调席位」，两边都从实参反射读）决定这屏的 options 是否只读；
只读屏**不发 final**（判据 = 界面私有字段 `_enemyHexControlsEnabled`，实例准确，不依赖任何"归属猜测"）。

**⚠ v1.2.0 首测踩到的坑（用户实测「海克斯我也不能 roll 了」）**：v1.2.0 直接把「可调席位」当成
`syncContext.AuthorityPlayer`，但**权威席位很可能正是那个【后台托管的瓦库席位】**
（实测日志 `席位=角色1, 权威=角色2`，而角色2 就是被 ⑤ 自动作答的托管瓦库）⇒ 两处失真叠在一起：
① 托管席位不会去点这个面板；② 它的符文界面由 ⑤ 自动作答（1.2s）**必定早于真人** ⇒ 结果 = **真人那一屏只剩预览、没有 reroll 按钮**。
⇒ **v1.2.1 修正「可调席位」的判据**（见 <c>ResolveControllerSeat</c>）：
**权威席位若是真人本地席位就给它**；否则给**第一个真人本地席位**（按 run 内顺序，稳定）；一个真人都没有（全托管）就退回权威席位＝不干预。

- **刻意不做远端镜像**：原版「非权威客户端只读镜像」靠一条**远端**消息（`ReceiveEnemyHexAdjustments`）。
  本地多控是单进程回环，权威席位是自己发自己的选择、**原版不回环给自己的客户端** ⇒ 那个 `RemoteReceiveTask`
  很可能永远等不到 ⇒ **软锁**（比现在这个显示 bug 严重得多）。所以只读屏只显示**创建时那份快照**，
  权威席位后续 reroll 不会同步到它那一屏（显示层面的一处小不一致，不影响实际数值）。
- **失败姿态 = 全上或全不上**：两处补丁 + 3 个反射成员（`_enemyHexControlsEnabled` / `CurrentMonsterHexSlots` /
  `RemoteReceiveTask`）任一缺失 ⇒ 整套退化（保留旧行为）。**半挂状态会造出「只读屏照样发 final」这种更糟的组合**。

## 其它未做（按需再加）

- 把受控位自动切到「当前这一屏的归属席位」（省一次按 Tab）；
- 把弹层栈里的多个符文界面按席位号排序（让作答顺序固定为角色1 → 角色2）。
