# DualRoleAdventure (LocalMultiControl)

[English](README.md) | 简体中文

一个《杀戮尖塔 2》（Slay the Spire 2）Mod：把官方联机多人流程改造成**本地**多角色体验——一名玩家在一台机器上操控 **2~12 名角色**（可重复选角），随时切换；游戏底层仍运行真实的多人流程，但**不经过任何网络**。

> **接续维护的分支。** 原作者为 [liwenhao0427](https://github.com/liwenhao0427)（磁石战士Ω），维护至 v1.30（2026 年 6 月）后停更。本仓库作为独立的社区接续分支继续维护与分发（[原创意工坊条目](https://steamcommunity.com/sharedfiles/filedetails/?id=3747538947)）。感谢磁石战士Ω 打下的基础，也感谢 [GuyGinat](https://github.com/GuyGinat) 的 v1.31 社区接续！

## 功能特性

- 本地 2~12 名角色组队，从正常多人菜单进入（`多人模式 → 创建 → 单人多角色`）
- 战斗内外随时切人：`Tab` / `Shift+Tab`（旧按键 `[` `]` `R` `T` `/` 仍可用）
- 每个角色独立的一切：卡组、能量、金币、药水、遗物、事件选项、奖励领取
- 完整流程：大厅 → 战斗 → 奖励 → 地图 → 事件 → 商店 → 休息区 → 宝箱 → 下一幕 → 存档/续玩
- **瓦库（Vakuu，AI 代打）**：可把任意角色交给内置自动出牌，单个或全员
- 可选**幽灵手牌**叠层（`F8`）：在自己手牌后方查看其他角色的手牌，位置可实时调整（`Ctrl+方向键`）
- 纯代码 Mod：`has_dll=true, has_pck=false`，无需素材包

安装、操作与玩法细节见**[玩家指南](PLAYER_GUIDE.md)**（英文）。

## 兼容性

- 当前针对游戏版本 **v0.111.0**（2026 年 8 月）。游戏每次更新后本仓库会尽快发布适配版——这是本分支的首要职责。
- **Oddmelt**：自 v1.33 起内置兼容守卫——重建战斗手牌 UI 时自动跳过 Oddmelt 未注册卡池的隐藏 Gauge 输入卡（此前会导致切人失败回滚），无需再安装单独的修复 mod。

## 安装

**Steam 创意工坊：** [GuyGinat 的社区接续条目](https://steamcommunity.com/sharedfiles/filedetails/?id=3772900244)已恢复更新，推荐订阅该条目。原作者的工坊条目（[3747538947](https://steamcommunity.com/sharedfiles/filedetails/?id=3747538947)）也可能继续更新，可自行关注；注意本仓库的修正版仅通过本仓库的 Releases 分发。

**手动安装（本仓库构建）：** 从 [Releases](https://github.com/nanthepsmith-droid/STS2_DualRoleAdventure/releases) 下载 `DualRoleAdventure.dll` + `DualRoleAdventure.json`，放入：

```
<杀戮尖塔2安装目录>\mods\DualRoleAdventure\
```

## 从源码构建

环境要求：.NET SDK 9、一份杀戮尖塔 2 游戏。

1. 把 `LocalMultiControl.csproj` 里的 `<Sts2Dir>` 指向你的游戏安装目录。
2. 构建：

```bash
dotnet restore LocalMultiControl.csproj
dotnet build LocalMultiControl.csproj -c Release
dotnet format LocalMultiControl.csproj --verify-no-changes   # 风格门禁
```

3. 构建会把 `DualRoleAdventure.dll` 复制到仓库根目录。把它和 `DualRoleAdventure.json` 一起部署到游戏的 `mods/DualRoleAdventure/` 目录（`copy_pck_to_game.ps1` 可代劳，按需改目标路径）。

开发时如需游戏 API 参考，把 `sts2.dll` 反编译到 `src/`（已 gitignore，只读参考）：

```bash
dotnet tool install -g ilspycmd --version 9.1.0.7988
ilspycmd -p --nested-directories -o ~/sts2-src "<游戏目录>/data_sts2_windows_x86_64/sts2.dll"
cp -r ~/sts2-src/MegaCrit/Sts2/. src/
```

## 反馈问题

请到 [GitHub Issues](https://github.com/nanthepsmith-droid/STS2_DualRoleAdventure/issues) 提交，并附上：幕数、所在场景/房间、准确复现步骤，以及日志文件 `%APPDATA%\SlayTheSpire2\logs\godot.log`（Mod 日志带 `[LocalMultiControl]` 前缀）。

## 文档

- [玩家指南（英文）](PLAYER_GUIDE.md) — 安装、操作、玩法
- [更新日志](CHANGELOG.md) — 版本历史
- [TODO](TODO.md) — 已知问题与排查中事项
- [docs/architecture.md](docs/architecture.md) — Mod 内部原理
- [../maintenance-docs/维护现状分析.md](../maintenance-docs/维护现状分析.md) — v1.32 发布前的项目现状分析（含各阶段维护史；维护性改进产物文档在 `pain/maintenance-docs/`，不入本仓库）
- [docs/design/](docs/design/) — 原设计文档（英译版）
- [docs/archive/](docs/archive/) — 原中文文档原样保留

## 致谢与许可

- 原作者：**liwenhao0427（磁石战士Ω）** — 全部设计及 v0.1~v1.30 的实现。如果这个 Mod 对你有帮助，欢迎请原作者喝杯咖啡：

  <img src="donate-original-author.jpeg" alt="给原作者捐赠" width="200" />

- 维护者（v1.31）：[GuyGinat](https://github.com/GuyGinat) — 首个社区接手版本、英文文档、工坊条目 3772900244
- 维护者（v1.32+）：[nanthepsmith-droid](https://github.com/nanthepsmith-droid) — 游戏 v0.110/0.111 适配、战斗选牌串行化与前台修复

### AI 辅助开发说明

v1.32+ 的维护工作**大量使用了 AI 编程助手**完成：所有改动均在人类维护者的指导下产生，经人工审核与实机测试后才发布。给 AI 的协作规则见 [`AGENTS.md`](AGENTS.md)；驱动 v1.32 发布的项目分析见 [`../maintenance-docs/维护现状分析.md`](../maintenance-docs/维护现状分析.md)。

目前尚无正式开源许可证。在 LICENSE 文件落地之前，请将源码视为*仅供个人使用的源码可用（source-available）*状态——二次分发衍生品前请先询问。
