# AGENTS.md — LocalMultiControl Collaboration Rules

Rules for automated coding agents (and humans) working in this repository. Goal: changes that are stable, verifiable, and easy to roll back.

## 1. Scope & hard constraints

Allowed to modify (everything else needs an explicit ask):

```
Scripts/            mod 源码
tests/              单元测试（可以新增 regression test；修 bug 优先补测试）
*.csproj            项目文件（含依赖与源码隔离规则）
*.json              mod 元数据（DualRoleAdventure.json / mod_manifest.json）
docs/, workshop/    文档与创意工坊素材
AGENTS.md, CHANGELOG.md, README*.md, PLAYER_GUIDE*.md, TODO.md
```

- Decompiled game source is **read-only reference, never compiled, never committed**:
  `src/`（仓库内，若存在）与 `sts2src/`（仓库外，`D:\Download\pain\sts2src\src`）。
  二者都被 `LocalMultiControl.csproj` 的 `<Compile Remove="..."/>` 排除；
  改 csproj 时**不得**删掉这些排除项，新增反编译目录必须同步加排除（见 §9 源码隔离门禁）。
- No destructive git operations (`reset --hard`, force-push, `checkout --` over user changes). Never push to `upstream` (GuyGinat's fork) or the original author's repo; pushes go to `origin` (nanthepsmith-droid's repo). `lanternx` is a read-only reference remote.
- **Do not commit or push unless the user explicitly asks.** Default = leave changes in the working tree.
  When the user does ask, commit per logical change with a clear Chinese message.
- Language: **Chinese** for all new code comments, commits, logs, and documentation. Original Chinese documents are preserved under `docs/archive/`.

## 2. Build, format, deploy

Run from the repo root:

```bash
dotnet restore LocalMultiControl.csproj
dotnet build LocalMultiControl.csproj -c Debug     # or -c Release for shipping
dotnet format LocalMultiControl.csproj --verify-no-changes
```

Then the gates that must pass before any deploy (see §9 for the full list):

```bash
python Scripts/Tools/clr_compat_check.py --mod-dll DualRoleAdventure.dll   # PE/CLR/ABI 结构校验
dotnet test tests/LocalMultiControl.Tests/LocalMultiControl.Tests.csproj   # 纯逻辑 + 程序集 ABI
```

`Scripts/Tools/build_all_mods.ps1` runs build → tests → clr_compat_check → deploy → SHA256 in one shot.
**Deployment scripts must never bypass the gates** — do not hand-roll a copy step to "save time".

The mod set is **dynamic** — never hand-maintain a repo list in the script:

- 默认**自动发现**：`D:\Download\pain` 下任何含 `*.csproj` 的目录就是一个 mod 仓库，
  产物名取 csproj 的 `<AssemblyName>`，槽位默认同名。**新增 mod 只需建仓库，不用改脚本。**
- 例外写在 `Scripts/Tools/mod_registry.json`：`enabled=false` 停用已废弃的 mod（官方已修复的那种）、
  `slot`/`dll` 覆盖槽位名、`note` 备注。
- 常用：`-List` 看全部 mod 的启用/部署/一致状态；`-Only <name>` 只处理指定 mod。
- 注意：**禁用 ≠ 卸载**。禁用的 mod 若槽位还留着 dll，游戏照样加载，脚本会 WARN 让你手动删槽位；
  新 mod 槽位缺 `*.json` 也会 WARN（游戏不会把它识别为 mod）。

- The build copies the DLL to the repo root: `DualRoleAdventure.dll`. **Always deploy/ship the root artifact**, not `.godot/mono/temp/...`.
- Deploy = copy `DualRoleAdventure.dll` + `DualRoleAdventure.json` to `<game>\mods\DualRoleAdventure\` (`copy_pck_to_game.ps1`, or plain copy). No pck export — this is a dll-only mod.
- If the copy fails with *permission denied*, the game is running and holds the DLL lock; retry after it closes.

## 3. Runtime verification

- Log file: `%APPDATA%\SlayTheSpire2\logs\godot.log`.
- Log via `Log.Info` with the unified prefix `[LocalMultiControl]` (`Log.Debug` is invisible by default). Add logs for anything you fix.
- Startup logs a machine-readable anchor sequence (human-readable Chinese lines are kept alongside):

  ```
  [LocalMultiControl] INIT_BEGIN
  [LocalMultiControl] BUILD_ID   ...      # BuildMarker
  [LocalMultiControl] BUILD_IDENTITY ...  # commit=<git> state=clean|dirty built=<UTC>
  [LocalMultiControl] GAME_ID    ...      # 游戏版本 + 进程架构
  [LocalMultiControl] COMPAT_RESULT PASS|WARN|FAIL ...
  [LocalMultiControl] PATCH_RESULT <n>/<n> critical=... optional_missing=...
  [LocalMultiControl] INIT_OK            # 只有成功才会出现
  [LocalMultiControl] INIT_FAILED        # 只有失败才会出现，随后 mod 抛异常（游戏侧标记 MOD_ERROR.ASSEMBLY_LOAD）
  ```

  **Never grep `Mod 初始化完成` to decide health**: on failure that line is *not* printed.
  `INIT_OK` 与 `INIT_FAILED` 互斥，是唯一终态判据（`log_parser.py --init-status` 会直接给出）。
- Harmony 异常只会被 catch 并计入致命清单，不会中断流程；真正决定是否失败的是上面的终态行。
- Automated unit tests exist for pure logic & metadata: `tests/LocalMultiControl.Tests/` (NUnit,
  net9.0; covers the PureLogic layer, picking strategies, potion rule tables, patch-domain grouping).
  Run them with `dotnet test tests/LocalMultiControl.Tests/LocalMultiControl.Tests.csproj`
  (also enforced as a deploy gate by `build_all_mods.ps1`; there is no CI, tests run locally).
  They reference the game assemblies but do **not** exercise live Godot/Harmony behavior.
- Everything tests cannot cover (Harmony patches in the running game: combat UI, reward attribution,
  event flows, foreground switching) is still verified by the maintainer playtesting. Provide focused,
  step-by-step test scripts and read the log after each round.

## 4. Harmony & domain conventions

- Prefer `Postfix` for added behavior, `Prefix` for guards; keep hot-path patches lightweight (no heavy reflection or allocation per frame).
- Control-switch and choice-submission paths must be **idempotent** — repeated triggers must not corrupt ordering or state.
- Never let local mirror/UI state pollute the authoritative game state (run state, piles, synchronizers).
- Patch naming: `PrefixXxx` / `PostfixXxx`; file per game type/scene under `Scripts/Patch/`.

## 5. Game-patch adaptation playbook

When the game updates and the mod breaks:

1. Regenerate the decompiled reference source in one command
   (`Scripts/Tools/regenerate_src.ps1`: prints `<game>\release_info.json`, decompiles `sts2.dll`
   with ilspycmd, overwrites `sts2src\src`, and prints a before/after diff):
   ```
   # PowerShell, from the repo root
   .\Scripts\Tools\regenerate_src.ps1                # -CheckOnly to skip decompile; -InstallIlspy to auto-install
   ```
   Manual equivalent (only if ilspycmd must be run by hand):
   ```bash
   dotnet tool install -g ilspycmd --version 9.1.0.7988   # newer majors may fail to install
   ilspycmd -p --nested-directories -o ~/sts2-src "<game>/data_sts2_windows_x86_64/sts2.dll"
   cp -r ~/sts2-src/MegaCrit/Sts2/. src/
   ```
2. Build; fix compile errors using the decompiled source as ground truth (compile errors = renamed/removed members).
3. Validate every **string-based** target with `Scripts/Tools/check_string_targets.py` — Harmony
   `[HarmonyPatch(typeof(X), "str")]`, `AccessTools.*`, and reflection `GetMethod("...")` all fail at
   runtime, not compile time. The tool exits non-zero when a target went stale:
   ```
   python Scripts\Tools\check_string_targets.py --repo .   # --json for CI; exit 1 = stale
   ```
   Legacy fallbacks (new-name-first with old-name reflection fallback) are reported as
   `LEGACY-FALLBACK` and do not fail the run — keep that pattern when fixing breakages (see
   `InvokeBeginRunIfAllPlayersReady` in `Scripts/Patch/LoadRunLobbyPatch.cs`).
4. Fix pattern: call the **new** member name first, with a reflection fallback to the old name.
5. Record every fixed breakage in `CHANGELOG.md`.

## 6. Code style

- `using` order: system → third-party → project namespaces; remove unused.
- File-scoped namespaces: `namespace LocalMultiControl.Scripts.Patch;`
- Types/methods/properties `PascalCase`; locals/params `camelCase`; private fields `_camelCase`.
- Explicit types over `var`; `<Nullable>enable</Nullable>` — handle null branches.
- Custom Godot node subclasses must be `partial` (source generators).

## 7. Release flow

> 2026-08-21：当前维护者**不计划上传创意工坊**，发布以 GitHub Releases 为准；第 3–4 步仅作参考保留。

1. Bump the version: `DualRoleAdventure.json` (`x.y.z` semver — the game warns on non-semver), `mod_manifest.json`, Workshop title `Vx.xx`, `Entry.cs` build marker.
2. Update `CHANGELOG.md` (cut a dated release section) and `PLAYER_GUIDE.md` if player-facing behavior changed.
3. `dotnet build -c Release`; copy `DualRoleAdventure.dll` + `DualRoleAdventure.json` into `workshop/content/`.
4. Update `workshop/steamcmd_item_fork.vdf` (`changenote`; `publishedfileid` stays once assigned). The **maintainer** runs the SteamCMD upload — it needs their Steam login.
5. Commit, push to `origin`, optionally create a GitHub release (zip via `Scripts/Tools/BuildRelease.ps1`).
6. Never touch the original author's Workshop item (3747538947).

## 8. Documentation map

- `README.md` — project front door; `PLAYER_GUIDE.md` — player-facing usage; `CHANGELOG.md` — history; `TODO.md` — open issues.
- `docs/architecture.md`, `docs/console-commands.md`, `docs/design/*` — developer docs.
- `docs/archive/*.zh.md` — original Chinese documents, preserved verbatim; do not edit them.

## 9. Hard gates (run before every deploy)

The goal is a single verdict, not a pile of logs. Every gate below must report PASS;
WARN is tolerated only where marked.

| # | 门禁 | 命令 | 失败后果 |
|---|---|---|---|
| 1 | 构建（0 警告 0 错误） | `dotnet build LocalMultiControl.csproj -c Release` | 禁止部署 |
| 2 | 源码隔离 | `LocalMultiControl.csproj` 的 `<Compile Remove="src/**" />` / `sts2src/**`；构建日志不得出现反编译游戏源码 | 禁止部署（体积膨胀 / 类型冲突） |
| 3 | 单元测试 | `dotnet test tests/.../LocalMultiControl.Tests.csproj` | 禁止部署（由 `build_all_mods.ps1` 强制） |
| 4 | CLR/PE 结构 | `python Scripts/Tools/clr_compat_check.py --mod-dll DualRoleAdventure.dll` | 禁止部署（截断 DLL / 非托管 DLL / 架构不符 / 运行时版本不符） |
| 5 | Assembly ABI | 同上（对比 mod 引用的 sts2 与游戏目录实际 sts2 的名称+版本+PublicKeyToken） | 禁止部署（版本漂移） |
| 6 | 部署字节校验 | `python ..\tools\dll_check.py --deployed --marker <marker> --expect-deployed` | 退出码非 0 即失败；**缺文件 = FAIL，不允许“跳过即绿”** |
| 7 | marker 身份 | `deploy_dll.ps1` 解析 marker，缺失/畸形 = FAIL | 无法证明产物身份 |
| 8 | Critical 补丁 | 运行期 `PATCH_RESULT`：Critical 缺失 → `INIT_FAILED` + 抛异常 | mod 在主菜单报红 |
| 9 | 初始化终态 | `python ..\tools\log_parser.py <log> --init-status` → `INIT_STATUS=OK` | `FAILED` 即为不可用构建 |

Notes:

- Gate 4/5 的期望值（TargetFramework、游戏目录）来自**单一来源**：`--game-dir` 参数 → 环境变量
  `STS2_DIR` → csproj `<Sts2Dir>` → 默认路径；期望 TFM 由 `--expect-tfm` 给出，默认 `net9.0`。
  不要在多个文件里各写一份 `net9.0` / `D:\SteamLibrary\...`。
- Gate 8 的 Critical 清单在 `Scripts/Entry.cs`（`CriticalPatchTargets`）；可选目标缺失只报 WARN。
  清单必须写**完整类型名**（`Namespace.Type.Method`，重载写 `.../参数个数`），
  由 `tests/.../ExpectedPatchTargetsTests.cs` 拿 sts2.dll 元数据逐条核对——**改清单前先跑单测**，
  写错会在实机误报 Critical 缺失并让 mod 报红。
- 从 dll 里抠字符串（marker 等）的脚本必须扫 **UTF-16 的两种字节对齐**（#US 堆起始偏移可能为奇数），
  否则会假阴性；`dll_check.py` 与 `deploy_dll.ps1` 都已按此实现（r92 修复）。
- 可选第三方依赖（Koishi / SkadaHelper 等）加载失败**永远只是 WARN**，不得判为致命。

## 10. Fix verification contract

> 每次修 bug 或加功能，交付必须带一份「验证契约」，格式固定、字段齐全。
> 目标是让复现/验证可机械执行，而不是「帮我试试看还坏不坏」。

```
BUG / 改动:
EXPECTED:           期望行为（一句话，能用日志锚点表达的写锚点）
SETUP:              前置（存档/角色/进第几幕/开不开什么 mod）
ACTION:             操作步骤（1. 2. 3.）
OBSERVE:            执行后看什么（游戏画面 + 日志位置）
PASS CONDITION:     通过 = 什么现象 + 什么日志锚点
FAIL CONDITION:      失败 = 什么现象 + 什么日志锚点
LOG ANCHORS:        相关固定 token（INIT_OK / PATCH_RESULT / SELECT_OWNER 等）
```

Example（炉心融解选牌卡死）:

```
BUG: 双角色同时选牌死锁
EXPECTED: 两个请求串行完成，各自进选牌
SETUP: 双人 run，瓦库在火堆/遗物触发选牌
ACTION: 1.进战斗 2.让 P1 选牌的同时 P2 触发选牌
OBSERVE: 看 P2 是否卡在“等待选择”
PASS CONDITION: 两笔选择先后落定，回合正常推进；日志出 SELECT_COMPLETE x2
FAIL CONDITION: P2 永久等待；日志停在 SELECT_QUEUE 无后续
LOG ANCHORS: SELECT_ENTER / SELECT_QUEUE / SELECT_OWNER / SELECT_COMPLETE / TURN_RESUME
```

交付时本契约写进 commit message 或随改动的说明里；验证靠固定 token 的可直接放进 `log_parser.py --kw`。
