# ThirdPartyHotPathFix

**12 席（本地多控）规模下的第三方热路径性能补丁**，只改性能、不改玩法
（不属于 `DualRoleAdventure` 主 mod，主仓库行为代码一行不动）。

原名 `HookFilterPerfFix`（v1.0.0 只做过滤链跳过，v1.1.0 扩到热路径纯函数记忆化并改名）。

## 现象

12 席 + 11 瓦库的局里 fps 掉到个位数、帧时 200~280ms、GC 25~39%、分配 300~360MB/s。
把 `YUICore` / `figure_Saya` / `Koishi` / `newsanguo` 四家关掉，同一局面立刻回到 40~59 fps。

## 量化（2026-10-31 `dotnet-trace` 采样，主线程）

| 项 | 改前 | 改后（r333 首版） | 归属 |
|---|---|---|---|
| `AsyncLocalValueMap.SetLocalValue`（YUI `Wrap` 每个遗物写两次） | **10.7%** | **0** | YUICore → 已修 |
| `RitsuLib NormalizePublicStem`（4 个 `Regex.Replace`，被 YUI 逐卡调用） | **5.2%** | v1.1.0 记忆化 | RitsuLib/YUI |
| `YuiCoreRuntimeTypeLookup.FindType`（每次 `GetAssemblies` + `GetType`） | **4.3%** | v1.1.0 记忆化 | YUICore |
| `StringHelper.Slugify`（3 个 `Regex.Replace`） | 1.2% | v1.1.0 记忆化 | 原版 |
| `Koishi.SealAllRelicPatch.Filter` | 1.97% | 1.13% → 1.61% | Koishi（**闸门被 tiered JIT 内联绕过**，见下） |
| `newsanguo.AbilitySuppressionPatch.Filter` | 1.57% | 1.46% → 1.21% | newsanguo（同上） |
| `figure_Saya.GeneralizedResistanceSuppressionPatch.Filter` | 1.48% | 1.16% → 1.28% | figure_Saya（同上） |

r334（15:00 局）同一份采样里的**帧时真相**（主 mod `LocalPerfProbe` 逐窗读数）：

```
fps 均 7.7~35.7，帧时 均 27~116ms / 最坏 247~661ms（>200ms 长帧 2~10 帧/5s）
GC 暂停 210~385ms / 5s（占 4~8%），托管分配 95~206MB/s
```

## ① 放宽 listener 列表初始容量（v1.2.0，最大一笔）

原版 `CombatState.IterateHookListeners` / `RunState.IterateHookListeners` 第一行是：

```csharp
List<AbstractModel> list = new List<AbstractModel>(Players.Count * 50);   // 12 席 = 600 槽
```

真实 listener 数 = 12 席的（卡 + 附魔 + 遗物 + 药水 + 球 + 能力）+ 敌人的能力 + mod 订阅者，
**一旦越过 600**，`list.Add(...)` 就会走 `List.AddWithResize` —— 每次分发都分配新数组 + 复制。

实测（2026-10-31，12 席同一场战斗，`List<>.set_Capacity` 自身耗时占主线程）：

| 场景 | `set_Capacity` |
|---|---|
| 关海克斯符文（listener < 600，从不扩容） | **0.15% / 0.29%** |
| 开海克斯符文（符文=遗物，每席位再多十几件 ⇒ 越界） | **28.08% / 30.45%** |

修法：转译 `<IterateHookListeners>d__NN.MoveNext()` 里那个乘数常量（50 → 100，即 12 席 1200 槽）。
**只改分配大小，不碰任何语义**：容量给大了只多占几 KB，给不够就退回原来的扩容行为（不会坏）。
可用 `HOT_PATH_FIX_LISTENER_CAPACITY=<乘数>` 覆盖；IL 形态对不上时只打 WARN、保持原样。

目标写法（Harmony 转译，按名字找状态机、不写死 `d__69`）：

```
... callvirt get_Players ; callvirt get_Count ; ldc.i4.s 50 ; mul ; newobj List<AbstractModel>::.ctor(int32)
```

## ② 过滤链跳过（四家）

三家都挂在 `CombatState.IterateHookListeners` 的**后缀**上，把 listener 序列包成「懒过滤 + 逐条 yield」；
YUICore 的 `YuiRelicTriggerReplay.Wrap` 同挂在该方法上，给**每个遗物 listener** 建 scope 并写两次 `AsyncLocal`。

关键事实：机制不在场时这四条链是**纯空转**（原样 yield 一遍），所以整条跳过是语义等价的：

| 家 | 判据（不在场 ⇒ `return false` 跳过） |
|---|---|
| Koishi | 场上没有生物带 `SealAllPower` |
| figure_Saya | 场上没有生物带 `GeneralizedResistancePower` |
| newsanguo | 敌人身上没铁骑 `IronCavalryPower`、也没人手牌里有 `AntiMagicField` |
| YUICore | 没有玩家的遗物实现 `IAfterRelicTriggeredHook` / `IRelicTriggerRepeater` |

失效键**不靠时间窗口**：战斗侧 `(CombatState, 能力版本号, 我方数, 敌方数)`，版本号由
`Creature.ApplyPowerInternal` / `RemovePowerInternal`（原版唯一的能力增删入口）后缀递增；
遗物侧 `(运行态/战斗实例, 全体遗物总数)`。快照整体发布（volatile 不可变对象）—— hook 分发的 async 续体会换线程。
战斗内遗物快照扫 `CombatState.Allies` 而不是 `runState.Players`：战灵召唤的临时席位不在后者里，
但它们的遗物**确实**会被收进 listener 列表。

## ③ 热路径纯函数记忆化（三方）

`StringHelper.Slugify(string)` / `ModContentRegistry.NormalizePublicStem(string)` /
`YuiCoreRuntimeTypeLookup.FindType(params string[])` 都是纯函数（同入参必同出参），
前缀查缓存 + 后缀写缓存。三条规则：**只缓存成功结果**（null / 抛异常不缓存，「程序集还没加载」
这类一次性失败不能钉死）、键为 null 不缓存不命中、有容量上限（到顶只更新不新增）。

## ⚠ 三家过滤链闸门 = 被 tiered JIT 内联绕过（v1.1.0 实测定性）

这三家的后缀只有一句 `__result = Filter(__result)`，**tier-0 会被调用、tier-1 会被内联进
Harmony 生成的包裹方法**，一旦内联，调用根本不经过被改写的入口 ⇒ 闸门失效。

证据（2026-10-31 15:00 局，日志 + 采样对照）：

- 三家闸门整局只有一行 `跳过=1 激活=0`，**从未出现过「机制在场」**（v1.1.0 起在场必写原因，没有就是没翻过）；
- 同一份采样里 `SealAllRelicPatch.Filter` 自耗时 1.61%、`GeneralizedResistanceSuppressionPatch.Filter` 1.28%、
  `AbilitySuppressionPatch.Filter` 1.21% —— **闸门说跳过，链子照样在跑**；
- 而 YUICore 的 `Wrap`（带迭代体的大方法）一跳就归零 —— 对照组干净。

所以：**给第三方的一行包装方法打前缀只能覆盖 tier-0 的少数调用**。要真正摘掉这三条链，只有
「先卸掉对方的补丁、再以 `[MethodImpl(NoInlining)]` 的自己补丁重新挂上、在场时才回调对方的 `Filter`」，
收益约 4%，风险是动别人的 Harmony 状态 —— **当前判定：不做**（见 TODO 里的取舍）。

对照口径：判断补丁是否生效**只看自己的锚点**（`已补丁…` / `记忆化首次命中` / `机制在场（原因）`），
不要用「采样栈里有没有某个帧」—— 过滤器是懒枚举，`Postfix` 只在创建可枚举对象那一刻在栈上。
排查工具：`python tools/perf_stack_probe.py <speedscope.json> <线程> <叶子关键字>`。

## 红线（不满足就不动手）

- 判不出来（不在战斗 / 拿不到 `DebugOnlyGetState` / 第三方改名 / `ApplyPowerInternal` 补丁没装上）
  ⇒ **不跳过**，三家闸门整体停用也认；
- 记忆化只缓存成功结果，缓存上限到顶后不再新增；
- 第三方类型/方法找不到 ⇒ 只记一行日志，保持对方原行为；
- 任何补丁抛异常都不影响游戏启动。

## 日志锚点

```
[HotPathFix] initialized（过滤链闸门 Koishi=已装 Saya=已装 newsanguo=已装 YUI=已装；记忆化 Slugify=已装 RitsuStem=已装 YUIFindType=已装；能力版本监听=已装）。
[HotPathFix] 已补丁 YUICore YuiRelicTriggerReplay.Wrap（机制不在场时跳过整条过滤链）。
[HotPathFix] 已补丁 RitsuLib ModContentRegistry.NormalizePublicStem（同参数只算一次）。
[HotPathFix] RitsuLib NormalizePublicStem 记忆化首次命中 —— 补丁确实生效（此后同类调用不再重算）。
[HotPathFix] YUICore 遗物触发复读链：机制不在场 → 跳过整条过滤链（跳过=1 激活=0）
[HotPathFix] newsanguo 能力压制过滤：机制在场（敌人身上有铁骑）→ 保留第三方原逻辑（跳过=3 激活=1）
[HotPathFix] 全部闸门与记忆化已就绪（…）。
```

排查要点：
- 只有 `initialized` 没有「已补丁 X」⇒ 那家没装 / 类型改名 / 打补丁失败（同轮日志里会有 WARN）；
- 有「记忆化首次命中」⇒ 该记忆化确实在生效（**没有这一行 = 被内联旁路了**）；
- `机制在场（…）` 会写明原因（能力 / 汉中 / 铁骑 / 遗物接口 / 判不出来），别猜。

`HOT_PATH_FIX=0` 可整体关闭。

## 构建 / 部署

```powershell
cd D:\Download\pain\STS2_DualRoleAdventure-itriedtofix
dotnet build compat-mods\ThirdPartyHotPathFix\ThirdPartyHotPathFix.csproj -c Release
.\Scripts\Tools\preflight.ps1 -Deploy
```

槽位：`<game>\mods\ThirdPartyHotPathFix\<ThirdPartyHotPathFix>.dll` + 同名 `.json`
（部署脚本只拷 dll，**json 要手工同步**；改过名的旧槽位务必删掉，否则旧 dll 还会被加载）。
