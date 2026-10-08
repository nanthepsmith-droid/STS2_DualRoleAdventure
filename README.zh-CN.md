# DualRoleAdventure (LocalMultiControl)

[English](README.md) | 简体中文

一个《杀戮尖塔 2》（Slay the Spire 2）Mod：把官方联机多人流程改造成**本地**多角色体验——一名玩家在一台机器上操控 **2~12 名角色**（可重复选角），随时切换；游戏底层仍运行真实的多人流程，但**不经过任何网络**。

> **接续维护的分支。** 原作者为 [liwenhao0427](https://github.com/liwenhao0427)（磁石战士Ω），维护至 v1.30（2026 年 6 月）后停更。本仓库作为独立的社区接续分支继续维护与分发（[原创意工坊条目](https://steamcommunity.com/sharedfiles/filedetails/?id=3747538947)）。感谢磁石战士Ω 打下的基础，也感谢 [GuyGinat](https://github.com/GuyGinat) 的 v1.31 社区接续！

## 功能特性

- 本地 2~12 名角色组队，从正常多人菜单进入（`多人模式 → 创建 → 单人多角色`）
- **每日挑战也能本地多角色**（v1.43）：联机菜单卡片行下方的「本地·每日挑战」按钮，最多 4 席，角色按日期自动分配，不上传每日榜
- **联机 AI 队友 Co-op Bots 兼容**（v1.43，可选）：装了它也能与本 mod 并存，可指定哪些席位交给联机机器人
- 战斗内外随时切人：`Tab` / `Shift+Tab`（旧按键 `[` `]` `R` `T` `/` 仍可用）
- 每个角色独立的一切：卡组、能量、金币、药水、遗物、事件选项、奖励领取
- 完整流程：大厅 → 战斗 → 奖励 → 地图 → 事件 → 商店 → 休息区 → 宝箱 → 下一幕 → 存档/续玩
- **瓦库（Vakuu，AI 代打）**：可把任意角色交给内置自动出牌，单个或全员
- 可选**幽灵手牌**叠层（`F8`）：在自己手牌后方查看其他角色的手牌，位置可实时调整（`Ctrl+方向键`）
- 纯代码 Mod：`has_dll=true, has_pck=false`，无需素材包

安装、操作与玩法细节见**[玩家指南（简体中文）](PLAYER_GUIDE.zh-CN.md)**（另有[英文版](PLAYER_GUIDE.md)）。

## 兼容性

- 当前针对游戏版本 **v0.111.0**（2026 年 8 月）。游戏每次更新后本仓库会尽快发布适配版——这是本分支的首要职责。
- **Oddmelt**：自 v1.33 起内置兼容守卫——重建战斗手牌 UI 时自动跳过 Oddmelt 未注册卡池的隐藏 Gauge 输入卡（此前会导致切人失败回滚），无需再安装单独的修复 mod。

## 安装

**Steam 创意工坊：** 工坊上有多个「本地多角色」条目，各自独立、更新节奏不同：原作者的[原条目](https://steamcommunity.com/sharedfiles/filedetails/?id=3747538947)、[GuyGinat 的社区接续条目](https://steamcommunity.com/sharedfiles/filedetails/?id=3772900244)，以及[上一节介绍的另一个已授权分支](https://steamcommunity.com/sharedfiles/filedetails/?id=3810171053)。注意：**本仓库的修正版只通过本仓库的 Releases 分发**（不随任何工坊条目更新）。

**手动安装（本仓库构建）：** 从 [Releases](https://github.com/nanthepsmith-droid/STS2_DualRoleAdventure/releases) 下载 `DualRoleAdventure.dll` + `DualRoleAdventure.json`，放入：

```
<杀戮尖塔2安装目录>\mods\DualRoleAdventure\
```

## 另见：另一个**已获授权**的分支（创意工坊）

**[本地多角色 · 全盛瓦库版 | Local Multi-Control · Prime Vakuu](https://steamcommunity.com/sharedfiles/filedetails/?id=3810171053)**

- 该分支自述为「**基于 liwenhao0427（磁石战士Ω）的 Local Multi-Control 与 GuyGinat 的社区维护版修改，已获授权**」——**获得授权的是它**（经原作者与前任维护者同意），这正是我们愿意推荐它的原因：工坊上"改过的版本"不止一个，**是否获得授权**决定它能不能被信任。
  ⚠ 说清楚边界：**本仓库自身并没有这类授权**，只是原作者停更后按社区惯例继续维护的一条分支；两条接续线各自独立、互不隶属。
  （GuyGinat 的接续条目目前更新较慢，按其说法更像"休眠"——等游戏更新导致不兼容时大概会复活）；
- 它的侧重与本站不同：对**瓦库（AI 代打）**链路做了较多增强。本仓库维护者已实测过，体验确实更好，值得一试；
- ⚠ 它**只在创意工坊发布，没有 GitHub 仓库**；本仓库的修正版仍**只通过本仓库 Releases 分发**，两者互不依赖，按需二选一或都装（注意：同机同时装两个"本地多角色"类 mod 会冲突，装之前先禁用另一个）。

## 从源码构建

环境要求：.NET SDK 9、一份杀戮尖塔 2 游戏。

1. 把 `LocalMultiControl.csproj` 里的 `<Sts2Dir>` 指向你的游戏安装目录。取值优先级：
   `-p:Sts2Dir=<路径>` > 环境变量 `STS2_DIR` > 内置默认值（Windows `D:/SteamLibrary/...`）。
   Linux / WSL 下请显式传入，例如
   `dotnet build LocalMultiControl.csproj -c Release -p:Sts2Dir=/mnt/d/SteamLibrary/"steamapps/common/Slay the Spire 2"`。
   csproj 里路径统一用正斜杠，同一份文件在两个平台都能用（反斜杠在 Linux 下不是分隔符）。
2. 构建：

```bash
dotnet restore LocalMultiControl.csproj
dotnet build LocalMultiControl.csproj -c Release
dotnet format LocalMultiControl.csproj --verify-no-changes   # 风格门禁
```

3. 构建会把 `DualRoleAdventure.dll` 复制到仓库根目录。**只把这个 dll** 拷进游戏的 `mods/` 下的槽位目录，
  且 dll 文件名必须与槽位 json 的 `id` 同名。新槽位直接用 `DualRoleAdventure.json` 即可；
  已有槽位里**不要再塞一个 id 不同的 json**（游戏会重复加载同一个 mod）。

开发时如需游戏 API 参考，把 `sts2.dll` 反编译到 `src/`（已 gitignore，只读参考）：

```bash
dotnet tool install -g ilspycmd --version 9.1.0.7988
ilspycmd -p --nested-directories -o ~/sts2-src "<游戏目录>/data_sts2_windows_x86_64/sts2.dll"
cp -r ~/sts2-src/MegaCrit/Sts2/. src/
```

## 反馈问题

请到 [GitHub Issues](https://github.com/nanthepsmith-droid/STS2_DualRoleAdventure/issues) 提交，并附上：幕数、所在场景/房间、准确复现步骤，以及日志文件 `%APPDATA%\SlayTheSpire2\logs\godot.log`（Mod 日志带 `[LocalMultiControl]` 前缀）。

## 文档

- [玩家指南（简体中文）](PLAYER_GUIDE.zh-CN.md) — 安装、操作、玩法
- [玩家指南（英文）](PLAYER_GUIDE.md)
- [更新日志](CHANGELOG.md) — 版本历史
- [TODO](TODO.md) — 已知问题与排查中事项
- [docs/architecture.md](docs/architecture.md) — Mod 内部原理
- [../maintenance-docs/维护现状分析.md](../maintenance-docs/维护现状分析.md) — v1.32 发布前的项目现状分析（含各阶段维护史；维护性改进产物文档在 `pain/maintenance-docs/`，不入本仓库）
- [docs/design/](docs/design/) — 原设计文档（英译版）
- [docs/archive/](docs/archive/) — 原中文文档原样保留

## 命名说明（Vakuu 与 Wakuu）

游戏里这个角色的官方拼写是 **Vakuu**（游戏程序集里是 `Vakuu` / `VakuuCardSelector`）。本 Mod 早期开发把它写成了 **“Wakuu”**，而这个错误拼写至今仍留在**内部 C# 标识符与文件名**里（`LocalWakuuAutopilotConfig`、`WakuuConfigData` 等）。

现状：

- **玩家可见文案是对的**：全部界面文本、本 README、玩家指南与工坊条目都使用 **Vakuu**。
- **配置键已统一**（`vakuu_autopilot.json`）：`wakuuBrain` / `wakuuViewMode` / `wakuuPlayQueue` / `wakuuPlayOverlap` / `fastWakuuPlay` / `keepWakuuFormRelic` 已改成 `vakuu*` 拼写，并带**向后兼容迁移** —— 旧配置里的旧键照常生效，下次保存时自动改写成新键。
- **内部标识符有意保留旧拼写**：重命名约 95 个源文件对玩法没有任何收益，却要动整个代码库，因此**没有修正计划**。日志 / 源码 / 堆栈里看到 `Wakuu`，按 `Vakuu` 理解即可。

即：只有配置键值得改，且已经改完；其余属外观问题，刻意保留。

## 致谢与许可

- 原作者：**liwenhao0427（磁石战士Ω）** — 全部设计及 v0.1~v1.30 的实现。如果这个 Mod 对你有帮助，欢迎请原作者喝杯咖啡：

  <img src="donate-original-author.jpeg" alt="给原作者捐赠" width="200" />

- 维护者（v1.31）：[GuyGinat](https://github.com/GuyGinat) — 首个社区接手版本、英文文档、工坊条目 3772900244
- 维护者（v1.32+）：[nanthepsmith-droid](https://github.com/nanthepsmith-droid) — 游戏 v0.110/0.111 适配、战斗选牌串行化与前台修复

### AI 辅助开发说明

v1.32+ 的维护工作**大量使用了 AI 编程助手**完成：所有改动均在人类维护者的指导下产生，经人工审核与实机测试后才发布。给 AI 的协作规则见 [`AGENTS.md`](AGENTS.md)；驱动 v1.32 发布的项目分析见 [`../maintenance-docs/维护现状分析.md`](../maintenance-docs/维护现状分析.md)。

目前尚无正式开源许可证。在 LICENSE 文件落地之前，请将源码视为*仅供个人使用的源码可用（source-available）*状态——二次分发衍生品前请先询问。
