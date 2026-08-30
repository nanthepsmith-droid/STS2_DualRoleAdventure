> 本文件是 CodeBuddy skill `dualroleadventure-development/references/` 同名文件的仓库内只读副本，
> 供纯人工维护者阅读；请与 skills 目录对应文件保持同步更新。

# tools（`D:\Download\pain\tools`）

## dll_check.py —— DLL 部署字节校验（**每次部署后必跑**）

.NET PE 里：类型名在元数据 `#Strings`（**UTF-8**），字符串字面量在 `#US`（**UTF-16**）。

- marker（字符串字面量）→ 用 **UTF-16** 搜
- 补丁类型名 → 用 **UTF-8** 搜
- 错误写法（如 `__runOriginal`）→ 用 `--absent-utf8` 断言**不存在**

```powershell
cd D:\Download\pain\tools
python dll_check.py --deployed --marker 2026-08-28-r27 `
    --utf8 RewardsSetSynchronizerSelectLocalRewardPatch `
    --absent-utf8 __runOriginal --expect-deployed
```

- `--deployed`：直接校验默认部署位（`mods\DualRoleAdventure\DualRoleAdventurefixed.dll`）。
- 不加 `--deployed` 时把 DLL 路径作为第一个位置参数传入（可校验 zip 解出来的临时副本）。
- `--expect-deployed`：断言部署位与仓库根 `DualRoleAdventure.dll` **字节一致**。
- 退出码 0/1，可脚本化。

## log_parser.py —— 日志分析

- 抽错误/异常/Harmony 堆栈。
- BOM 自动识别 UTF-16；`--encoding` 手动指定。
- `--kw` 用正则过滤关键字行。

## 其它

- `pck_tool.py`：PCK 解析。
- `json_remap.py`：JSON key 批量重映射。

## 顺带一提：PowerShell 里跑 git 的两个坑

- `git commit -m "..."` 的消息里若含 **`/`**，git 会把它当路径参数报
  `fatal: /: '/' is outside repository`。改用消息文件：`git commit -F <file>`。
- `git rev-parse @{u}` 在 PowerShell 里会被当成**哈希表字面量**解析报错。改用
  `git rev-list --left-right --count origin/master...master` 看 ahead/behind（左=落后，右=领先）。
