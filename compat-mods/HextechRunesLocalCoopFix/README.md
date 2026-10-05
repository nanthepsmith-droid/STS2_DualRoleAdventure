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

**选哪一个符文**：`稀有度优先（棱彩 > 金 > 银）+ 同档取最左`；读不到稀有度（第三方符文 / 海克斯改名）就取最左。
稀有度正是海克斯自己分档用的轴（每幕权重 + 银/金/棱彩卡框），查询走它自己的
`HextechCatalog.TryGetPlayerRuneRarity`（反射）。每次作答把候选连同稀有度打进日志
（`options=[银:…, 金:…], picked=…[金]`）⇒ 想换规则只要改 `WakuuRuneAutoAnswer.PickIndex`。

## 日志锚点

```
[HextechRunesLocalCoopFix] 检测到海克斯程序集加载: HextechRunes，尝试挂补丁。
[HextechRunesLocalCoopFix] INIT_OK: patched=[HextechRuneSelectionCoordinator.IsLocalPlayer,
    HextechRuneSelectionScreen.RelicsSelected, HextechRuneSelectionCoordinator.CreateLocalRuneSelectionScreenAsync,
    HextechRuneSelectionCoordinator.CreateRuneSelectionScreenAsync,
    HextechRuneSelectionScreen.DismissAfterSelectionComplete], missing=[], source=init
[HextechRunesLocalCoopFix] 瓦库席位符文界面自动作答: 已就绪（后台托管瓦库席位由我们代选，真人席位照旧自己点）。
[HextechRunesLocalCoopFix] 本地席位已按「本机玩家」放行（…）: player=76561198422527327
[HextechRunesLocalCoopFix] 将为该席位创建符文界面（…）: player=76561198422527327
[HextechRunesLocalCoopFix] 符文界面已标注归属（…）: player=76561198422527327, title=<默认标题> · 角色2
[HextechRunesLocalCoopFix] 瓦库席位符文界面已登记自动作答（延迟 1.2s 避开界面自带的 1s 点击保护窗）: seat=角色2(…327), screen=HextechRuneSelectionScreen
[HextechRunesLocalCoopFix] 瓦库符文界面自动作答: seat=角色2(…327), mode=符文选择, options=[银:RUNE_A, 金:RUNE_B, 银:RUNE_C], picked=RUNE_B[金], rule=稀有度优先+同档取最左, screen=HextechRuneSelectionScreen
[HextechRunesLocalCoopFix] 瓦库符文界面自动作答完成（界面已关闭）: seat=角色2(…327), screen=HextechRuneSelectionScreen
[HextechRunesLocalCoopFix] 符文界面选完即关闭（…）: screen=HextechRuneSelectionScreen, 最近创建席位=76561198422527327
```

| 观察 | 含义 |
|---|---|
| `INIT_OK` + `patched=[…5 项…]`, `missing=[]` | 补丁全挂上 |
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
（dll sha256 `5A82D03D29A2…`；证据 `logs-archive/godot__20261005-173455__r205.log`，marker r205 / `INIT_OK`）：

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

## 复现 / 验证步骤（用户侧）

1. 关游戏 → 确认 `mods\HextechRunesLocalCoopFix\` 槽位里 `HextechRunesLocalCoopFix.{dll,json}` 都在（游戏内 mod 列表里启用它）。
2. 进「本地·单人多角色」，开一局带**海克斯大乱斗**的 run，走到会弹符文选择的地方（第一幕开局 / 事件给的符文）。
3. 期望：每个席位各弹一次符文界面，标题带「· 角色1 / 角色2」；选完一个自动露出下一个；两个都选完流程继续。
   **其中后台托管的瓦库席位由本补丁自己选**（它那一屏一闪而过），真人席位照旧自己点。
4. 取日志看上面三个锚点。

## 构建 / 部署

由主仓库脚本自动发现（pain 根下含 `*.csproj` 的目录 = 一个 mod）：

```powershell
cd D:\Download\pain\HextechRunesLocalCoopFix
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

### 敌人 hex 调整面板可能出现在两个席位上（未处理，按需再加）

海克斯每幕开局会 roll「本幕怪物 hex」，并允许**权威席位**在符文界面里顺手调整（reroll / 移除）。
原版联机下只有权威席位的客户端会弹这个界面；本补丁把两席都判成本机后，两边界面都会带这个面板
（`ControlsEnabled` 对两席都成立），而「最终值由谁决定」由**先作答的那一屏**说了算
（海克斯内部 `FinalSent` 只认第一次 final 发送）。表现：第二屏可能显示的是调整前的初始值、且它的调整不生效。

- 影响面：只是这一个可选面板的归属/显示；符文选择与流程推进都正常。
- 不影响的情况：该幕没有 roll 新怪物 hex、或用户没动这个面板时，行为与之前完全一致。
- 要彻底对齐原版（只有权威席位可调 + 其它屏只读镜像）需要再补 3 个补丁点
  （`CreateEnemyHexAdjustmentOptionsForSelection` / `CompleteLocalEnemyHexAdjustmentSync` / `SendEnemyHexAdjustment`），
  属"看得见问题再修"的范畴。

## 其它未做（按需再加）

- 把受控位自动切到「当前这一屏的归属席位」（省一次按 Tab）；
- 把弹层栈里的多个符文界面按席位号排序（让作答顺序固定为角色1 → 角色2）。
