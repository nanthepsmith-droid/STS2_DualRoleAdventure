> 本文件是 CodeBuddy skill `dualroleadventure-development/references/` 同名文件的仓库内只读副本，
> 供纯人工维护者阅读；请与 skills 目录对应文件保持同步更新。

# 发布流程（AGENTS.md §7 + 实操踩坑）

> 2026-08-21 起：当前维护者**不计划上传创意工坊**，发布以 **GitHub Releases** 为准。
> workshop 相关步骤仅作记录保留，SteamCMD 上传需维护者本人 Steam 登录。

## 流程

1. **升版本号**（三处 + marker）：
   - `DualRoleAdventure.json` → semver `x.y.z`（**游戏对非 semver 会告警**）
   - `mod_manifest.json`
   - `workshop/content/DualRoleAdventure.json`
   - `Scripts/Entry.cs` 的 `BuildMarker`（形如 `Revival v1.38 (game v0.111.0, marker=...)`）
2. **更新 `CHANGELOG.md`**（切一个带日期的版本小节）；若玩家可见行为变了，同步 `PLAYER_GUIDE.md`。
3. `dotnet build -c Release`（0 警告 0 错误）；把根 `DualRoleAdventure.dll` + `DualRoleAdventure.json`
   拷进 `workshop/content/`（工坊上传不做了，但保持目录内容一致）。
4. `workshop/steamcmd_item_fork.vdf` 的 `changenote`（`publishedfileid` 一旦分配就不要改）。
5. **打 zip**：`Scripts\Tools\BuildRelease.ps1 -Version v1.38`
6. 提交 → 推 `origin` → 打 tag `v1.38` → 创建 GitHub Release 并上传 zip。
7. **永不碰原作者的工坊条目 3747538947**。

## 关键约定：发布名 vs JSON 版本号

- **zip 名 / git tag** = `v1.38`（v{major}.{minor}）
- **JSON 里的 version** = semver `1.38.0`

两者**不要互相覆盖**。历史发布包（v1.37）里装的一直是 semver `1.37.0`。

## BuildRelease.ps1 的两个坑（已修，别再踩回去）

1. **不要解析/重写 `DualRoleAdventure.json`。**
   该文件的中文是历史遗留**双重编码乱码**，且乱码里含**未转义的引号** →
   文件本身不是合法 JSON，`ConvertFrom-Json` 会直接抛异常（脚本原第 53 行）。
   现在脚本只用正则取 `version`，打包时**按字节原样复制**。
2. **不要把 JSON 的 version 改写成 `v1.38`。** 原脚本会这么做，与 semver 要求和历史包内容都冲突。
   现在发布名与 JSON semver 分离，两者 major.minor 不一致时脚本会告警。

产物：`release/DualRoleAdventure-v1.38.zip`，内含 `DualRoleAdventure.dll` + `DualRoleAdventure.json`
两项（`release/` 已被 gitignore，zip 属构建产物不入库）。

## 发版后校验（别跳过）

```powershell
# 1) 解出 zip 里的 DLL，确认与仓库根产物同哈希
# 2) 对包内 DLL 跑 dll_check
cd D:\Download\pain\tools
python dll_check.py <zip解出的dll> --marker <marker> \
    --utf8 <新补丁类名> --absent-utf8 __runOriginal
# 3) 确认包内 JSON 与仓库根 JSON 字节一致、version 是 semver
```

## 环境相关的坑

- **`gh` CLI 未安装**（2026-08-29 确认）。无法用命令行创建 GitHub Release。
  要么装 gh：
  ```powershell
  gh release create v1.38 "release\DualRoleAdventure-v1.38.zip" `
      --title "v1.38 — Bug-fix release" --notes-file "release\RELEASE_NOTES-v1.38.md"
  ```
  要么到 GitHub 网页 Releases → New Release → 选已有 tag → 粘贴说明 → 上传 zip。
- **杀毒软件会杀 `.ps1`**（2026-08-29 用户反馈）。运行 `BuildRelease.ps1` 若异常，
  先确认文件是否被隔离；实在不行手工打包（就两个文件，直接 `Compress-Archive`）。
- **git 推送偶发 `schannel: failed to receive handshake, SSL/TLS connection failed`**。
  多为瞬时，**重试即可成功**。不要为此改 git 配置、不要关 `http.sslVerify`。
- PowerShell 里 `git commit -m` 消息含 `/` 会报 `fatal: /: '/' is outside repository`，
  改用 `git commit -F <消息文件>`。写完记得删掉临时文件（放在 `.git/` 下不入版本库）。

## 发布文案习惯

- 标题：`v1.38 — Bug-fix release`（简短英文）
- 正文：**中英双语**（先英文后中文），含 Fixed 列表、安装步骤（解压到 `mods\DualRoleAdventure\`，
  不要多套一层目录）、以及"启动日志里应出现 marker 行"的验证方法。
- 模板留一份在 `release/RELEASE_NOTES-v1.38.md`（该目录被 gitignore）。
