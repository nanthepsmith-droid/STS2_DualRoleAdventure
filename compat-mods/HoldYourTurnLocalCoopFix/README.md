# HoldYourTurnLocalCoopFix

「别急着结束回合！」（[HoldYourTurn](https://steamcommunity.com/sharedfiles/filedetails/?id=3810229386)，工坊 `3810229386`，
作者 snoe1024）与**本地多控**（DualRoleAdventure / LocalMultiControl）并用时的兼容补丁。

- **默认行为**：装了本补丁就生效（没有配置项）。**不装**则退回原状：第二个及以后的真人点结束回合没反应。
- **只在本地多控会话里生效**：真实联机 / 单人一律交回 HoldYourTurn 原逻辑。

---

## 它修的是什么

HoldYourTurn v0.2.3 的 `TurnHolder.OnCombatSetUp(CombatState)` 在战斗开始时遍历玩家，
**只挑第一个 `LocalContext.IsMe(player)` 为真的席位**，然后就 `break`：

```csharp
foreach (Player player in state.Players) {
    if (!LocalContext.IsMe(player)) continue;
    ... 订阅该玩家的 EnergyChanged / Hand.CardAdded ...
    _player = player;   // ← 永远只有第一个"本机玩家"
    break;
}
```

此后它的三处门禁都只认这一个 `_player`：

| 位置 | 逻辑 | 后果 |
|---|---|---|
| `TurnHolder.CanPressTurnEnd` | 要求 `CombatManager.IsPartOfPlayerTurn(_player)` | 轮到别人时**恒 false** |
| `NEndTurnButton.RefreshEnabled` 的 Postfix | `if (!CanPressTurnEnd) Disable()` | 按钮被它**再禁用**（主 mod 刚刷好也没用） |
| `NEndTurnButton.CallReleaseLogic` 的 Prefix | `!IsEnabled \|\| !CanPressTurnEnd` → `return false` | 点击被**静默吞掉** |

本地多控里每个席位都是"本机玩家"，但它只绑了第一个 ⇒ 第二人之后永远点不动。

## 怎么修的

只补 `TurnHolder.CanPressTurnEnd` 的 getter（Harmony 前缀）：

- 本次 Run **不是**本地多控回环会话 → 不干预；
- 它绑定的那一席 **就是**当前前台 → 不干预（保留"获得额外能量/抽牌后 1.5s 内防误点"）；
- 当前前台那一席确实在自己的玩家回合里 → **直接给 `true`**，把判定交回原版 / 主 mod。

**刻意不去改它的 `_player` 绑定**：`Subscriptions` / `_gainSequence` / `_undoRequested` 是整套按单席设计的台账，
中途改绑会连带出"撤销结束回合"的错乱。代价是：**非绑定席位没有那 1.5s 防误点保护**（比"点不动"好得多）。

## 日志锚点

启动时（挂上补丁）：

```
[HoldYourTurnLocalCoopFix] INIT_OK: 已接管 HoldYourTurn.HoldYourTurnCode.TurnHolder.CanPressTurnEnd 的 getter
```

运行中（每个前台/绑定组合只打一次）：

```
[HoldYourTurnLocalCoopFix] HoldYourTurn 兼容：非绑定席位放行结束回合(foreground=…, bound=…)。
```

降级时（HoldYourTurn 缺席 / 改名 / 结构变了）：

```
[HoldYourTurnLocalCoopFix] INIT_DEGRADED: … 本补丁不干预。
```

## 部署

- 槽位：`<game>\mods\HoldYourTurnLocalCoopFix\HoldYourTurnLocalCoopFix.dll` + 同名 `.json`
  （⚠ **部署脚本只拷 dll，`.json` 要手工同步**；`id` 必须与 dll 主文件名一致）。
- 构建：`Scripts\Tools\build_all_mods.ps1` 会自动发现 `compat-mods\` 下的这个 csproj；
  也可以单独 `dotnet build compat-mods\HoldYourTurnLocalCoopFix\HoldYourTurnLocalCoopFix.csproj -c Release`。
- 分发：随主 Release 的独立附件 `compat-mods-v<major>.<minor>.zip`（`release_build.ps1 -CompatOnly`）。

## 已知限制

- 只覆盖"**结束回合按钮**"这一条链：HoldYourTurn 其它功能（加成后禁按钮的计时、药水提示、智能模式）
  对非绑定席位不生效 —— 它们本来就只针对"那一位"。
- HoldYourTurn 如果将来把 `_player` 改成按 `LocalContext` 动态取，本补丁会自动退化成"永远不干预"（无害）。
