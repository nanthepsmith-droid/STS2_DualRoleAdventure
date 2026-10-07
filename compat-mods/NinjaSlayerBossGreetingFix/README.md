# NinjaSlayerBossGreetingFix

修第三方 mod **NinjaSlayer** 的「Boss 问候」在**本地双角色 / 本地多控**下把战斗搞卡死的独立补丁 mod
（不属于 `DualRoleAdventure` 主 mod，主仓库保持不动）。

## 现象

本地多角色模式下，迎战 **ArknightsBoss** 的「剑·戟·矛」Boss（玩家俗称**碳长矛 / 玉双剑 / 枣大刀**，
`ARKNIGHTS_BOSS_ENCOUNTER_SWORD_GLAIVE_SPEAR_BOSS`）时：

> 开局**不抽牌**、**不能换人**、**点结束回合无效**（战斗停在 `NotPlayPhase`）。

**单人模式完全正常**，只有本地多角色模式触发。同类 Boss 战（任何会走 NinjaSlayer Boss 问候的遭遇）都会中招。

## 根因（2026-10-05 定性，marker `2026-10-04-r205`）

日志锚点（`logs-archive/godot__20261005-151110__r205.log`）：

```
9413 [WARN] [NinjaSlayer] Boss greeting cinematic failed safely: System.NullReferenceException
9414      at NinjaSlayer.Code.ExternalAnimations.BossGreetingSync.<get_Participants>b__36_0(UInt64 id)
9417      at NinjaSlayer.Code.ExternalAnimations.BossGreetingSync.AdvanceHost()
9421 [ERROR] Combat #1 turn loop died while its combat is in progress; the combat is stuck until the room is restarted:
9422      at NinjaSlayer.Code.ExternalAnimations.BossGreetingSync.<get_Participants>b__36_0(UInt64 id)
9429      at NinjaSlayer.Code.Patches.BossGreetingStartPatch.GreetThenStart(...)
9437      at MegaCrit.Sts2.Core.Combat.CombatManager.StartCombatInternal(CombatTurnState turnState)
```

`NinjaSlayer.Code.ExternalAnimations.BossGreetingSync.Participants`（NinjaSlayer 1.0.12 反编译）：

```csharp
private IEnumerable<ulong> Participants => _players.Where(id => {
    if (_disconnected.Contains(id)) return false;
    if (id == _network.NetId) return true;
    var host = _network as INetHostGameService;
    if (host != null) return host.NetHost.ConnectedPeerIds.Contains(id);  // ← NetHost 为 null 时 NRE
    return false;
});
```

链路：

1. `_players` 来自 `combatState.Players.Select(p => p.NetId)` —— 本地双角色下有**两个**席位
   （`…326` 主机 + `…327` 本地第二角色）。
2. 本 mod 的本地回环网络服务 `LocalLoopbackHostGameService` **`Type = NetGameType.Host` 但 `NetHost => null`**
   （本地回环不经过 socket，没有真实网络主机）。
3. 于是对**非本机 NetId** 的席位，走到 `host.NetHost.ConnectedPeerIds` ⇒ `NullReferenceException`。
4. 异常先被 `TryPlay` 的兜底 catch 记为 WARN，但紧接着 `sync.Complete()` **再次**做同样的判定
   ⇒ 第二次 NRE 落在 catch 之外 ⇒ 冒穿 `BossGreetingStartPatch.GreetThenStart`（只 catch `OperationCanceledException`）
   ⇒ 冒穿 `Hook.BeforeCombatStart` ⇒ `CombatManager.StartCombatInternal` 抛异常 ⇒ 回合循环死亡 ⇒ 卡在 `NotPlayPhase`。

**单人为什么没事**：`_players` 只有自己（`id == _network.NetId`）⇒ 在第二个 `if` 就 `return true`，
永远不触碰 `NetHost`。

> 注：即使**只修 NRE、不收缩参与者**也没用 —— 本地回环的 `SendMessage` 只做本地日志、不会真的投递给第二席位，
> 第二席位永远不会回 `Ready`/`Done`，`Participants.All(_ready.Contains)` 恒不成立 ⇒ 依旧卡死。
> 所以正确修法是让参与者只含「唯一的真实进程」。

## 它怎么做

后缀补丁 `BossGreetingSync.get_Participants`：

- 仅当 `network is INetHostGameService && network.NetHost == null`（= 假联机/本地回环）时，
  把返回的参与者集合替换为 `{ network.NetId }`（网络服务自身）；
- 真实联机（`NetHost != null`）**完全不动**，原逻辑照走。

补丁按「类型全名 + 属性名 + 私有字段名」反射定位，NinjaSlayer 未安装 / 版本改名时**只告警不做事**，
不会影响游戏启动。

## 日志锚点

```
[NinjaSlayerBossGreetingFix] initialized
[NinjaSlayerBossGreetingFix] 未找到 NinjaSlayer.BossGreetingSync（未安装或尚未加载），跳过补丁。
[NinjaSlayerBossGreetingFix] 已补丁 BossGreetingSync.get_Participants（本地回环 NetHost=null 时收缩参与者）。
[NinjaSlayerBossGreetingFix] 本地回环（Host 但 NetHost=null）下收缩 Boss 问候参与者为网络服务自身: netId=…
```

排查：`python tools\log_scan.py --kw NinjaSlayerBossGreetingFix --file <godot.log> --show`

修好后**不应该**再出现 `BossGreetingSync.<get_Participants>` 的 NRE 栈，
也不应该再出现 `turn loop died while its combat is in progress`。

## 实机验证

**✅ 通过（2026-10-05，`logs-archive/godot__20261005-152634__r205.log`，marker r205 / `INIT_OK`；用户「没问题了」）**：

```
[NinjaSlayerBossGreetingFix] 已补丁 BossGreetingSync.get_Participants（本地回环 NetHost=null 时收缩参与者）。
[NinjaSlayerBossGreetingFix] initialized
[NinjaSlayerBossGreetingFix] 本地回环（Host 但 NetHost=null）下收缩 Boss 问候参与者为网络服务自身: netId=76561198422527326   ×3
[NinjaSlayer] Boss greeting released combat-start hooks: room=…:ARKNIGHTS_BOSS_ENCOUNTER_SWORD_GLAIVE_SPEAR_BOSS, played=False.
```

同一 Boss（`Creating NCombatRoom … SWORD_GLAIVE_SPEAR_BOSS`）对比上一局：

| 指标 | 修前（15:11 那局） | 修后（15:26 那局） |
|---|---|---|
| `turn loop died while its combat is in progress` | 3 | **0** |
| `BossGreetingSync` 异常栈 | 18 | **0**（只剩 1 条我们自己的 INFO） |
| `忽略切人请求(player-state-button)` | 多条 | **0** |
| `Combat becomes NotPlayPhase` 之后的 `PlayPhase` | 无（永久卡住） | **有**（`… unpausing action queues`） |
| `回合开始兜底重评结束回合按钮` | 0 | 4（两个回合） |

`played=False` 表示这局没满足播放问候动画的条件（`IsGreetingPending` 为 false），
但这正是原实现会 NRE 的路径（`while (!sync.Started) sync.Ready()` 与 `pending` 无关）——
即补丁救的正是「即使不播动画也会卡死」的那一步。

## 构建 / 部署

由主仓库 `STS2_DualRoleAdventure-itriedtofix\Scripts\Tools\build_all_mods.ps1` 自动发现
（pain 根下含 `*.csproj` 的目录 = 一个 mod）：

```powershell
cd D:\Download\pain\STS2_DualRoleAdventure-itriedtofix
.\Scripts\Tools\build_all_mods.ps1 -Only NinjaSlayerBossGreetingFix
```

槽位：`<game>\mods\NinjaSlayerBossGreetingFix\NinjaSlayerBossGreetingFix.dll`
（槽位 json 的 `id` 必须与 dll 主文件名一致）。
