# regenerate_src.ps1 - 游戏更新后一键重生成反编译参考源码（sts2src/src）
#
# 对应《mod系统维护性改进实施方案》任务 1.3 产出物 A：
#   把 AGENTS.md §5 的人工步骤 1/2（读 release_info.json、重反编译）变成一条命令。
#
# 行为：
#   1. 读 <GameDir>\release_info.json，打印游戏版本 / commit / 日期
#   2. 检测 ilspycmd（缺则提示安装，或 -InstallIlspy 自动装）
#   3. ilspycmd -p --nested-directories 反编译 sts2.dll 到临时目录
#   4. 自动定位反编译源码根（MegaCrit\Sts2 优先，其次 Sts2，否则共同目录前缀）
#   5. 把 *.cs 按相对路径覆盖拷贝到 <repo 上级>\sts2src\src（只动 .cs，不碰 .gd/.tscn/.uid）
#   6. 打印新旧树 diff 统计（unchanged / changed / added / removed）与新的 src 文件数
#
# 用法示例：
#   .\Scripts\Tools\regenerate_src.ps1                       # 默认游戏目录 + 目标 src
#   .\Scripts\Tools\regenerate_src.ps1 -CheckOnly            # 只读版本信息 + 检查 ilspycmd，不反编译
#   .\Scripts\Tools\regenerate_src.ps1 -InstallIlspy         # 顺便自动安装 ilspycmd
#   .\Scripts\Tools\regenerate_src.ps1 -KeepTemp             # 保留反编译临时目录供人工检查
#   .\Scripts\Tools\regenerate_src.ps1 -GameDir "D:\Steam\...\Slay the Spire 2"
#
# 注意：真实重生成耗时较长（全量反编译约几分钟），建议游戏更新时才跑。
# 本脚本不做破坏性删除：只覆盖拷贝 .cs，旧树中多出的 .cs（removed）仅统计不删除。

param(
    [string]$GameDir = "D:\SteamLibrary\steamapps\common\Slay the Spire 2",
    [string]$SrcTarget = "$PSScriptRoot\..\..\sts2src\src",
    [string]$IlspyVersion = "9.1.0.7988",
    [switch]$InstallIlspy,
    [switch]$KeepTemp,
    [switch]$CheckOnly,
    [string]$TempOutDir = ""
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$msg) Write-Host "[*] $msg" -ForegroundColor Cyan }
function Write-Ok   { param([string]$msg) Write-Host "[OK] $msg" -ForegroundColor Green }
function Write-Err  { param([string]$msg) Write-Host "[X] $msg" -ForegroundColor Red; exit 1 }

# 解析路径（支持相对路径按 $PSScriptRoot 换算）
# 注意：本环境 Resolve-Path 返回值类型不稳定，一律用 Test-Path + 字符串拼接，不依赖 .Path。
function Resolve-Abs {
    param([string]$Path)
    if ([System.IO.Path]::IsPathRooted($Path)) { return $Path.TrimEnd('\') }
    return (Join-Path $PSScriptRoot $Path).TrimEnd('\')
}

# 计算目录下全部 *.cs 的相对路径 -> SHA256 快照
function Get-CsSnapshot {
    param([string]$Root)
    $snap = @{}
    Get-ChildItem -LiteralPath $Root -Recurse -Filter *.cs -ErrorAction SilentlyContinue | ForEach-Object {
        $rel = $_.FullName.Substring($Root.Length + 1)
        $snap[$rel] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
    return $snap
}

# 定位反编译输出里的源码根：优先 MegaCrit\Sts2 / Sts2，否则取所有 .cs 的共同目录前缀
function Get-SourceRoot {
    param([string]$Root)
    foreach ($rel in @("MegaCrit\Sts2", "Sts2", "")) {
        $p = if ($rel) { Join-Path $Root $rel } else { $Root }
        if (-not (Test-Path $p)) { continue }
        $cs = @(Get-ChildItem -LiteralPath $p -Recurse -Filter *.cs -ErrorAction SilentlyContinue)
        if ($cs.Count -gt 0) {
            return @{ Path = $p; Count = $cs.Count }
        }
    }
    return $null
}

# ---------------------------------------------------------------- 1. 读取版本信息
$gameDir = $GameDir.TrimEnd('\')
if (-not (Test-Path -LiteralPath $gameDir)) { Write-Err "游戏目录不存在: $GameDir" }
$releaseInfoPath = Join-Path $gameDir "release_info.json"
$dllPath = Join-Path $gameDir "data_sts2_windows_x86_64\sts2.dll"
if (-not (Test-Path $releaseInfoPath)) { Write-Err "未找到 release_info.json: $releaseInfoPath" }
if (-not (Test-Path $dllPath))         { Write-Err "未找到 sts2.dll: $dllPath" }

$info = Get-Content -LiteralPath $releaseInfoPath -Raw -Encoding UTF8 | ConvertFrom-Json
Write-Host ""
Write-Host "=== 游戏版本信息（release_info.json） ===" -ForegroundColor Green
Write-Host ("  version : {0}" -f $info.version)
Write-Host ("  commit  : {0}" -f $info.commit)
Write-Host ("  date    : {0}" -f $info.date)
Write-Host ("  branch  : {0}" -f $info.branch)
Write-Host ""

# ---------------------------------------------------------------- 2. ilspycmd 检测
$ilspy = Get-Command ilspycmd -ErrorAction SilentlyContinue
if (-not $ilspy) {
    if ($InstallIlspy) {
        Write-Step "未找到 ilspycmd，自动安装 ilspycmd @ $IlspyVersion ..."
        dotnet tool install -g ilspycmd --version $IlspyVersion
        if ($LASTEXITCODE -ne 0) {
            Write-Err "ilspycmd 安装失败，请手动执行: dotnet tool install -g ilspycmd --version $IlspyVersion"
        }
        $ilspy = Get-Command ilspycmd -ErrorAction SilentlyContinue
        if (-not $ilspy) { Write-Err "安装后仍找不到 ilspycmd，请重开终端或检查 dotnet tool 全局路径" }
    } else {
        Write-Err ("未找到 ilspycmd。请先安装：`n  dotnet tool install -g ilspycmd --version {0}`n（或用 -InstallIlspy 自动安装）" -f $IlspyVersion)
    }
}

# ---------------------------------------------------------------- 2.5 CheckOnly 快速检查
if ($CheckOnly) {
    Write-Ok "环境检查通过：ilspycmd 可用（$($ilspy.Source)）。"
    Write-Ok "-CheckOnly 模式，未执行反编译。可用 -InstallIlspy 自动安装，去掉 -CheckOnly 触发完整重生成。"
    exit 0
}

# ---------------------------------------------------------------- 3. 目标与临时目录
$srcTarget = Resolve-Abs $SrcTarget
if (-not (Test-Path -LiteralPath $srcTarget)) { Write-Err "目标 src 目录不存在: $srcTarget（预期为 pain\sts2src\src）" }

if (-not $TempOutDir) {
    $TempOutDir = Join-Path $env:TEMP ("sts2-decompile-" + [DateTime]::Now.ToString("yyyyMMdd-HHmmss"))
}
$outRoot = Join-Path $TempOutDir "out"
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null

# ---------------------------------------------------------------- 4. 反编译
Write-Step ("反编译 {0}`n        → {1}" -f $dllPath, $outRoot)
& $ilspy.Source -p --nested-directories -o $outRoot $dllPath
if ($LASTEXITCODE -ne 0) {
    Write-Err ("ilspycmd 反编译失败（exit={0}）。请检查 sts2.dll 版本与 ilspycmd 兼容性。" -f $LASTEXITCODE)
}

$srcRootInfo = Get-SourceRoot $outRoot
if (-not $srcRootInfo) { Write-Err "反编译输出中未找到任何 .cs 文件，请用 -KeepTemp 检查临时目录" }
$srcRoot = $srcRootInfo.Path
Write-Ok ("反编译完成，源码根 = {0}（{1} 个 .cs）" -f $srcRoot, $srcRootInfo.Count)

# 输出根下但不在源码根内的 .cs（如全局命名空间的 --y__*.cs），单独提示
$orphan = @(Get-ChildItem -LiteralPath $outRoot -Recurse -Filter *.cs -ErrorAction SilentlyContinue |
    Where-Object { -not $_.FullName.StartsWith($srcRoot) })
if ($orphan.Count -gt 0) {
    Write-Host ("[!] 以下 {0} 个 .cs 在源码根之外（可能属全局命名空间），需手动处理：" -f $orphan.Count) -ForegroundColor Yellow
    $orphan | Select-Object -First 10 | ForEach-Object { Write-Host ("    " + $_.FullName.Substring($outRoot.Length + 1)) -ForegroundColor Yellow }
}

# ---------------------------------------------------------------- 5. 覆盖拷贝 .cs
$before = Get-CsSnapshot $srcTarget
Write-Step "覆盖拷贝 .cs 到 $srcTarget ..."
Get-ChildItem -LiteralPath $srcRoot -Recurse -Filter *.cs | ForEach-Object {
    $rel = $_.FullName.Substring($srcRoot.Length + 1)
    $dest = Join-Path $srcTarget $rel
    $destDir = Split-Path $dest -Parent
    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }
    Copy-Item -LiteralPath $_.FullName -Destination $dest -Force
}
$after = Get-CsSnapshot $srcTarget

# ---------------------------------------------------------------- 6. diff 统计
$unchanged = 0; $changed = 0; $added = 0; $removed = 0
$changedList = @()
foreach ($rel in $after.Keys) {
    if ($before.ContainsKey($rel)) {
        if ($before[$rel] -eq $after[$rel]) { $unchanged++ } else { $changed++; $changedList += $rel }
    } else { $added++ }
}
foreach ($rel in $before.Keys) {
    if (-not $after.ContainsKey($rel)) { $removed++ }
}

Write-Host ""
Write-Host "=== 反编译结果统计 ===" -ForegroundColor Green
Write-Host ("  新 src/ 文件数  : {0}（旧 {1}）" -f $after.Count, $before.Count)
Write-Host ("  unchanged : {0}" -f $unchanged)
Write-Host ("  changed   : {0}" -f $changed)
Write-Host ("  added     : {0}" -f $added)
Write-Host ("  removed   : {0}（未删除，仅统计；如需清理请手工处理）" -f $removed)
if ($changedList.Count -gt 0 -and $changedList.Count -le 20) {
    Write-Host "  变化文件（前 20）："
    $changedList | ForEach-Object { Write-Host ("    - $_") }
} elseif ($changedList.Count -gt 20) {
    Write-Host ("  变化文件共 {0} 个（前 20）：" -f $changedList.Count)
    $changedList | Select-Object -First 20 | ForEach-Object { Write-Host ("    - $_") }
}

# ---------------------------------------------------------------- 7. 收尾
if ($KeepTemp) {
    Write-Ok "临时反编译目录已保留: $TempOutDir"
} else {
    Remove-Item -LiteralPath $TempOutDir -Recurse -Force
    Write-Ok "已清理临时目录: $TempOutDir"
}
Write-Ok ("完成。下一步：跑 check_string_targets.py 核对字符串/反射目标：`n  python Scripts\Tools\check_string_targets.py --repo .")
