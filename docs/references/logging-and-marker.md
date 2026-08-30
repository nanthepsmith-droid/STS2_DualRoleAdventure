> 本文件是 CodeBuddy skill `dualroleadventure-development/references/` 同名文件的仓库内只读副本，
> 供纯人工维护者阅读；请与 skills 目录对应文件保持同步更新。

# 日志与 marker

## 日志位置与编码

- 实时日志：`%APPDATA%\SlayTheSpire2\logs\godot.log`。用户常把副本存桌面
  （`错误日志N.txt` / `正确日志.txt`）。
- 编码：桌面副本可能 UTF-8 也可能 UTF-16（带 BOM）。`tools\log_parser.py` 已支持 BOM 自动识别，
  也可用 `--encoding` 手动指定。
- 日志前缀统一 `[LocalMultiControl]`（`LocalMultiControlLogger.Info/Warn/Error`）。
  用 `Log.Info`，**`Log.Debug` 默认不可见**。

## marker 机制（每轮迭代必须升）

`Scripts\Entry.cs` 里的 `BuildMarker` 常量，形如：

```csharp
private const string BuildMarker = "Revival v1.38 (game v0.111.0, marker=2026-08-28-r27)";
```

- 每迭代一轮就升一次 marker（r26→r27…），启动日志里 grep `marker=` 即可确认游戏加载的是哪一版。
- **这是唯一可靠的"改动有没有生效"判断依据**。用户报告"没修好"时，第一件事是确认日志里的 marker
  是不是你刚部署的那一版。

## 启动段应该长什么样

依次出现：

1. `[LocalMultiControl] 开始初始化 Harmony 补丁。`
2. `[LocalMultiControl] Revival v1.38 (game v0.111.0, marker=...)` ← BuildMarker
3. `[LocalMultiControl] Harmony 补丁统计: 已打补丁方法数=N` + 清单
4. `[LocalMultiControl] Mod 初始化完成。`

第 2 步和第 4 步之间若出现 Harmony 异常 = 补丁目标/签名写坏了，mod 会整体加载失败。

## 已内置的诊断

- `Harmony 补丁统计: 已打补丁方法数=N`，并列出关键方法（SelectCards / FromHand /
  FromSimpleGrid / FromChooseACard / FromCombatPile / ShouldSelectLocalCard）的已打补丁清单。
  注意：**只列这几类**，其它目标需自己加日志或用 `dll_check.py` 确认。
- 战斗/切换相关：`战斗UI已刷新到当前角色`、`控制上下文已更新`、`sender切换`、
  `回合结束按钮重评`、`瓦库选择器栈探针` 等。

## 排查套路

- 用 `tools\log_parser.py` 抽错误/异常/Harmony 堆栈；`--kw` 用正则过滤。
- 想确认"某个补丁到底跑没跑过"：给它加一条特征日志，然后 grep 该日志出现次数。
  **一次都没有 = 补丁根本没应用**（多半是坑 1：缺类级 `[HarmonyPatch]`）。
