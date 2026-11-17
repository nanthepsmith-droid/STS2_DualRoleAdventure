# build_dual.ps1 - 双版本（正式版 / beta）出包：薄 loader + 每个分支一份完整载荷
#
# 形态（与生态里内容 mod 一致，样本：HextechRunes / Koishi）：
#   mods\DualRoleAdventure\
#     DualRoleAdventure.json                        manifest（min_game_version = 0.111.0）
#     DualRoleAdventure.dll                         ← 薄 loader（Loader\ 工程，唯一被游戏加载的程序集）
#     lib\0.111.0\DualRoleAdventure.Payload.dll     ← **stable/0.111 代码线**（git 1824cb5 起）
#     lib\0.113.0\DualRoleAdventure.Payload.dll     ← **master（beta 线）**
#
# 为什么 0.111 那份不用 master + 条件编译：
#   0.111 的原版 API 与 0.113 差得不少（选择器栈 / 商店库存 / 商店房间节点 / 愚者 / 恐惧 / 闪电战…）。
#   把 0.113 代码用 #if 掰回 0.111 既别扭又会漏（实机踩过：MerchantSynchronizer、NStandardMerchantRoom、
#   Fool 职业在 0.111 根本不存在）。正确做法是**各自从当年的代码线长出来**：
#   `git 1824cb5`（r349，支持 0.111 的最后代码）对着 0.111.1 **0 错误**编过（已实测）。
#
# 用法：
#   .\Scripts\Tools\build_dual.ps1                      # 出包到 dist\
#   .\Scripts\Tools\build_dual.ps1 -Deploy              # 再部署到游戏 mods 目录
#   .\Scripts\Tools\build_dual.ps1 -SkipStable          # 只出 beta 载荷 + loader（stable 未就绪时）
#
# 前提：
#   · `..\..\_worktrees\stable0111`（或 -StableWorktree）里有一份 stable/0.111 的检出；
#     没有就跑： git worktree add D:\Download\pain\_worktrees\stable0111 1824cb5
#   · beta 载荷对着 0.113 的程序集编（切到正式版后默认构建会引用 0.111，所以这里**必须**显式给 Sts2Dir）

param(
    [string]$RepoDir = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$StableWorktree = "D:\Download\pain\_worktrees\stable0111",
    [string]$BetaGameDir = "D:\Download\pain\game-archives\v0.113.1",
    # ⚠ 必须是 **0.111 的归档**，不能写"当前安装目录"：用户随时会在正式版/beta 之间切换，
    # 一旦装的是 0.113，稳定载荷就会对着 0.113 的 dll 编 ⇒ 一堆"0.111 没有这个成员"的错（2026-11-17 踩过）。
    [string]$StableGameDir = "D:\Download\pain\game-archives\v0.111.1",
    [string]$OutDir = "",
    [string]$ModsDir = "D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods",
    [string]$BetaVariant = "0.113.0",
    [string]$StableVariant = "0.111.0",
    [string]$ModId = "",
    [switch]$SkipStable,
    # 复用仓库根已构建好的 beta 载荷（`build_all_mods.ps1` 刚跑过构建+单测时用，避免重复构建）
    [switch]$SkipBetaBuild,
    [switch]$Deploy
)

$ErrorActionPreference = "Stop"
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

function Write-Step { param([string]$m) Write-Host "[*] $m" -ForegroundColor Cyan }
function Write-Ok { param([string]$m) Write-Host "[OK] $m" -ForegroundColor Green }
function Write-Err { param([string]$m) Write-Host "[X] $m" -ForegroundColor Red; exit 1 }

if (-not $OutDir) { $OutDir = Join-Path $RepoDir "dist" }
$payloadName = "DualRoleAdventure.Payload.dll"
# 包内文件名（部署时会改成 `<槽位id>.dll`）
$loaderName = "DualRoleAdventure.dll"
# 编译产物名：**必须与载荷内部程序集名不同**（beta 载荷复用主工程产物时内部名就是 `DualRoleAdventure`，
# 同名同版本会在同一个 ALC 里 LoadFromAssemblyPath 时抛 FileLoadException）
$loaderAssemblyName = "DualRoleAdventure.Loader"

function Assert-GameDir {
    param([string]$Dir, [string]$Label)
    $dll = Join-Path $Dir "data_sts2_windows_x86_64\sts2.dll"
    if (-not (Test-Path -LiteralPath $dll)) { Write-Err "$Label 程序集不存在: $dll" }
    $info = Join-Path $Dir "release_info.json"
    $version = if (Test-Path -LiteralPath $info) {
        (Get-Content -LiteralPath $info -Raw -Encoding UTF8 | ConvertFrom-Json).version
    } else { "<unknown>" }
    Write-Ok "$Label 版本 = $version"
}

# 编一份载荷（AssemblyName 用命令行覆盖，不改动任何代码线的 csproj）
function Build-Payload {
    param([string]$ProjectDir, [string]$GameDir, [string]$Tag, [string]$Destination)

    $project = Join-Path $ProjectDir "LocalMultiControl.csproj"
    if (-not (Test-Path -LiteralPath $project)) { Write-Err "找不到工程: $project" }

    # Godot.NET.Sdk 的输出目录是 `.godot\mono\temp\bin\Release\`，且工程的 CopyDllToProjectRoot
    # 会把它拷到**工程根**。所以以"工程根那份"为准（构建前先删，避免拿到上一次的旧文件）。
    $rootCopy = Join-Path $ProjectDir $payloadName
    Remove-Item -Force $rootCopy -ErrorAction SilentlyContinue

    Write-Step "构建 $Tag 载荷 …"
    $gameArg = ($GameDir -replace '\\', '/')
    & dotnet build $project -c Release -v:q "-p:Sts2Dir=$gameArg" "-p:AssemblyName=DualRoleAdventure.Payload"
    if ($LASTEXITCODE -ne 0) { Write-Err "$Tag 载荷构建失败" }

    $built = $rootCopy
    if (-not (Test-Path -LiteralPath $built)) {
        $built = Get-ChildItem -LiteralPath $ProjectDir -Recurse -Filter $payloadName -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $built -or -not (Test-Path -LiteralPath $built)) { Write-Err "$Tag 载荷产物不存在（工程 $ProjectDir）" }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -LiteralPath $built -Destination (Join-Path $Destination $payloadName) -Force

    # 清掉本轮中间目录与仓库根被拷出来的载荷（避免混进 git / 干扰常规构建）
    Remove-Item -Recurse -Force (Join-Path $ProjectDir "obj-payload"), (Join-Path $ProjectDir "bin-payload") -ErrorAction SilentlyContinue
    Remove-Item -Force (Join-Path $ProjectDir $payloadName) -ErrorAction SilentlyContinue

    $hash = (Get-FileHash -LiteralPath (Join-Path $Destination $payloadName) -Algorithm SHA256).Hash
    Write-Ok "$Tag 载荷 → $(Join-Path $Destination $payloadName)  ($($hash.Substring(0,12))…)"
}

Write-Host ""
Write-Step "检查输入"
Assert-GameDir -Dir $BetaGameDir -Label "beta"
if (-not $SkipStable) {
    Assert-GameDir -Dir $StableGameDir -Label "正式版"
    if (-not (Test-Path -LiteralPath (Join-Path $StableWorktree "LocalMultiControl.csproj"))) {
        Write-Err "stable 代码线未检出: $StableWorktree`n  先跑: git worktree add $StableWorktree 1824cb5"
    }
}

Write-Host ""
Write-Step "清理输出目录"
$packageDir = Join-Path $OutDir "DualRoleAdventure"
Remove-Item -Recurse -Force $packageDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

Write-Host ""
if ($SkipBetaBuild) {
    # 仓库根的 `DualRoleAdventure.dll` 就是**载荷**（主工程 AssemblyName），由 build_all_mods 的构建阶段产出
    $repoPayload = Join-Path $RepoDir "DualRoleAdventure.dll"
    if (-not (Test-Path -LiteralPath $repoPayload)) { Write-Err "复用 beta 载荷失败：$repoPayload 不存在（先跑一次构建）" }
    $dest = Join-Path $packageDir "lib\$BetaVariant"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item -LiteralPath $repoPayload -Destination (Join-Path $dest $payloadName) -Force
    Write-Ok "beta($BetaVariant) 载荷（复用已构建产物）→ $(Join-Path $dest $payloadName)"
} else {
    Build-Payload -ProjectDir $RepoDir -GameDir $BetaGameDir -Tag "beta($BetaVariant)" `
        -Destination (Join-Path $packageDir "lib\$BetaVariant")
}

if (-not $SkipStable) {
    Build-Payload -ProjectDir $StableWorktree -GameDir $StableGameDir -Tag "正式版($StableVariant)" `
        -Destination (Join-Path $packageDir "lib\$StableVariant")
}

Write-Host ""
Write-Step "构建薄 loader …"
$loaderProjectDir = Join-Path $RepoDir "Loader"
$loaderGameArg = ($StableGameDir -replace '\\', '/')
# 用默认输出目录（`Loader\bin` / `Loader\obj`）——它们已被 .gitignore 与 static_checks 的 SKIP_DIRS 覆盖；
# 别用 `bin-loader` 这种自定义名字，否则会被 S8（换行卫生）当成源码扫到。
& dotnet build (Join-Path $loaderProjectDir "DualRoleAdventure.Loader.csproj") -c Release -v:q "-p:Sts2Dir=$loaderGameArg"
if ($LASTEXITCODE -ne 0) { Write-Err "loader 构建失败" }

$loaderOut = Join-Path $loaderProjectDir "bin\Release\net9.0\$loaderAssemblyName.dll"
if (-not (Test-Path -LiteralPath $loaderOut)) {
    $loaderOut = Get-ChildItem -LiteralPath (Join-Path $loaderProjectDir "bin") -Recurse -Filter "$loaderAssemblyName.dll" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $loaderOut -or -not (Test-Path -LiteralPath $loaderOut)) { Write-Err "loader 产物不存在（$loaderProjectDir）" }
Copy-Item -LiteralPath $loaderOut -Destination (Join-Path $packageDir $loaderName) -Force
Remove-Item -Recurse -Force (Join-Path $loaderProjectDir "bin"), (Join-Path $loaderProjectDir "obj") -ErrorAction SilentlyContinue
Write-Ok "loader → $(Join-Path $packageDir $loaderName)"

Write-Host ""
Write-Step "写 manifest"
Copy-Item -LiteralPath (Join-Path $RepoDir "DualRoleAdventure.json") -Destination (Join-Path $packageDir "DualRoleAdventure.json") -Force
Write-Ok "manifest 已写入"

Write-Host ""
Write-Host "== 包内容 ==" -ForegroundColor Cyan
Get-ChildItem -LiteralPath $packageDir -Recurse -File | ForEach-Object {
    "{0,10:N0}  {1}" -f $_.Length, $_.FullName.Replace("$packageDir\", "")
}

if ($Deploy) {
    Write-Host ""
    $slotDir = Join-Path $ModsDir "DualRoleAdventure"
    Write-Step "部署到 $slotDir"

    # dll 文件名必须等于**槽位 manifest 的 id**（游戏按 `mod.path + id + ".dll"` 找）。
    # 本机槽位用的是改名后的 `DualRoleAdventurefixed.json`（id `DualRoleAdventurefixed`），
    # 工坊包是 `DualRoleAdventure` —— 所以这里读槽位 json、不写死。
    $effectiveId = $ModId
    if (-not $effectiveId) {
        $slotJson = Get-ChildItem -LiteralPath $slotDir -Filter "*.json" -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($slotJson) {
            $effectiveId = (Get-Content -LiteralPath $slotJson.FullName -Raw -Encoding UTF8 | ConvertFrom-Json).id
            Write-Ok "槽位 id（取自 $($slotJson.Name)）= $effectiveId"
        }
    }
    if (-not $effectiveId) { $effectiveId = "DualRoleAdventurefixed" }

    New-Item -ItemType Directory -Force -Path $slotDir | Out-Null
    $deployedLoader = Join-Path $slotDir "$effectiveId.dll"
    Copy-Item -LiteralPath (Join-Path $packageDir $loaderName) -Destination $deployedLoader -Force

    $deployed = @($deployedLoader)
    foreach ($variant in @($BetaVariant, $StableVariant)) {
        if ($SkipStable -and $variant -eq $StableVariant) { continue }
        $src = Join-Path $packageDir "lib\$variant"
        if (-not (Test-Path -LiteralPath $src)) { continue }
        $dst = Join-Path $slotDir "lib\$variant"
        New-Item -ItemType Directory -Force -Path $dst | Out-Null
        $name = "$effectiveId.Payload.dll"
        Copy-Item -LiteralPath (Join-Path $src $payloadName) -Destination (Join-Path $dst $name) -Force
        $deployed += (Join-Path $dst $name)
    }

    foreach ($file in $deployed) {
        if (-not (Test-Path -LiteralPath $file)) { Write-Err "部署校验失败（文件不存在）: $file" }
    }
    Write-Ok "已部署: $effectiveId.dll + lib（$(($deployed.Count - 1)) 份载荷）"
}

Write-Host ""
Write-Ok "完成。包目录: $packageDir"
