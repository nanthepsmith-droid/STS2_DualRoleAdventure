# build_all_mods.ps1 - 一键构建 + 部署 + 校验全部 mod（主 mod + 6 个兼容 fix）
#
# 对应《mod系统维护性改进实施方案》任务 1.5（修复仓库统一，保守方案）：
#   消除 4~6 个独立 fix 仓库的分散部署：一条命令完成
#     1) 逐个 dotnet build -c Release（主 mod 走 LocalMultiControl.csproj，fix 走各自 csproj）
#     2) 逐个部署到游戏 mods 槽位（主 mod 部署为 DualRoleAdventurefixed.dll，沿用既有 json 槽位，不覆盖 json）
#     3) 逐个 SHA256 字节校验 + 尝试解析 marker
#
# 用法:
#   .\Scripts\Tools\build_all_mods.ps1                 # 全量：构建 + 部署 + 校验
#   .\Scripts\Tools\build_all_mods.ps1 -BuildOnly      # 只构建，不部署
#   .\Scripts\Tools\build_all_mods.ps1 -DeployOnly     # 只部署（用仓库根已有 dll，不构建）
#   .\Scripts\Tools\build_all_mods.ps1 -CheckOnly      # 只校验已部署文件与仓库根 dll 是否一致
#   .\Scripts\Tools\build_all_mods.ps1 -GameDir "D:\Steam\...\Slay the Spire 2"
#
# 退出码: 0 = 全部成功; 1 = 有构建/部署/校验失败; 2 = 用法/环境错误
#
# 注意: 本文件含中文注释，必须保存为带 BOM 的 UTF-8（PowerShell 5.1 无 BOM 时按 GBK 解析会语法错）。

param(
    [string]$ReposRoot = "D:\Download\pain",
    [string]$GameDir = "D:\SteamLibrary\steamapps\common\Slay the Spire 2",
    [string]$MainRepo = "STS2_DualRoleAdventure-itriedtofix",
    [string]$MainSlot = "DualRoleAdventure",
    [string]$MainSlotDll = "DualRoleAdventurefixed.dll",
    [string[]]$FixRepos = @(
        "Act4FinalAscentFixes",
        "LexNinja2LaserFix",
        "OddmeltGaugeCardRenderFix",
        "TouhouAncientsAncientFixes",
        "TouhouAncientsMiniHakkeroFixed",
        "YuWanCardWhiteScarfFix"
    ),
    [switch]$BuildOnly,
    [switch]$DeployOnly,
    [switch]$CheckOnly
)
$ErrorActionPreference = "Stop"

function Write-Step { param([string]$msg) Write-Host "[*] $msg" -ForegroundColor Cyan }
function Write-Ok   { param([string]$msg) Write-Host "[OK] $msg" -ForegroundColor Green }
function Write-Err  { param([string]$msg) Write-Host "[X] $msg" -ForegroundColor Red; exit 1 }

# 模式互斥
$mode = if ($CheckOnly) { "check" } elseif ($BuildOnly) { "build" } elseif ($DeployOnly) { "deploy" } else { "all" }

$gameDir = $GameDir.TrimEnd('\')
$modsDir = Join-Path $gameDir "mods"
if (-not (Test-Path -LiteralPath $modsDir)) { Write-Err "mods 目录不存在: $modsDir" }

# 游戏进程检查（运行中会锁 DLL，构建后可部署；DeployOnly/CheckOnly 也要求未运行才能安全读部署位? 读不锁，但部署锁）
function Test-GameRunning {
    $p = Get-Process -Name "sts2_windows_x86_64", "Slay the Spire 2", "sts2" -ErrorAction SilentlyContinue
    return ($null -ne $p)
}

function Get-Marker([string]$dllPath) {
    # BuildMarker 形如 "...marker=2026-08-30-r30"，在元数据里是 UTF-16 字符串
    try {
        $bytes = [System.IO.File]::ReadAllBytes($dllPath)
        $text = [System.Text.Encoding]::Unicode.GetString($bytes)
        if ($text -match "marker=([\w-]+)") { return $Matches[1] }
    } catch { }
    return $null
}

# 单仓库构建：返回 dll 路径（构建产物在仓库根）或 $null
function Invoke-BuildRepo {
    param([string]$RepoDir, [string]$Csproj, [string]$OutDll)
    Write-Step "构建 $RepoDir ..."
    Push-Location $RepoDir
    try {
        dotnet build $Csproj -c Release | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[X] 构建失败: $RepoDir（exit=$LASTEXITCODE）" -ForegroundColor Red
            return $null
        }
    } finally {
        Pop-Location
    }
    if (-not (Test-Path -LiteralPath $OutDll)) {
        Write-Host "[X] 未找到构建产物: $OutDll" -ForegroundColor Red
        return $null
    }
    Write-Ok "构建完成: $OutDll"
    return $OutDll
}

# 部署单个 dll 到槽位并校验
function Invoke-DeployOne {
    param([string]$SrcDll, [string]$SlotDir, [string]$SlotDllName)
    $dst = Join-Path $SlotDir $SlotDllName
    if (-not (Test-Path -LiteralPath $SlotDir)) {
        New-Item -ItemType Directory -Path $SlotDir -Force | Out-Null
    }
    if (Test-GameRunning) {
        Write-Err "游戏正在运行，DLL 被锁定。请先关闭游戏再部署。"
    }
    Copy-Item -LiteralPath $SrcDll -Destination $dst -Force
    $a = Get-FileHash $SrcDll
    $b = Get-FileHash $dst
    if ($a.Hash -ne $b.Hash) {
        Write-Host "[X] 哈希不一致: $dst" -ForegroundColor Red
        return $false
    }
    $marker = Get-Marker $dst
    Write-Ok "已部署并校验一致: $dst ($($a.Hash.Substring(0,12))...)"
    if ($marker) { Write-Host "      marker = $marker" -ForegroundColor Yellow }
    return $true
}

# CheckOnly：校验仓库根 dll 与部署位 dll 是否一致
function Invoke-CheckOne {
    param([string]$SrcDll, [string]$SlotDir, [string]$SlotDllName)
    $dst = Join-Path $SlotDir $SlotDllName
    if (-not (Test-Path -LiteralPath $dst)) {
        Write-Host "[X] 部署位文件不存在: $dst" -ForegroundColor Red
        return $false
    }
    $a = Get-FileHash $SrcDll
    $b = Get-FileHash $dst
    if ($a.Hash -ne $b.Hash) {
        Write-Host "[X] 部署位与仓库根不一致: $dst" -ForegroundColor Red
        return $false
    }
    Write-Ok "校验一致: $dst ($($a.Hash.Substring(0,12))...)"
    return $true
}

$mainRepoDir = Join-Path $ReposRoot $MainRepo
$fail = 0

# 主 mod（DualRoleAdventure / LocalMultiControl）
$mainCsproj = Join-Path $mainRepoDir "LocalMultiControl.csproj"
$mainOut = Join-Path $mainRepoDir "DualRoleAdventure.dll"
$mainSlotDir = Join-Path $modsDir $MainSlot
if (-not (Test-Path -LiteralPath $mainCsproj)) { Write-Err "主仓库 csproj 不存在: $mainCsproj" }

if ($mode -eq "all" -or $mode -eq "build") {
    if (-not (Invoke-BuildRepo $mainRepoDir $mainCsproj $mainOut)) { $fail++ }
}
if ($mode -eq "all" -or $mode -eq "deploy") {
    if (-not (Test-Path -LiteralPath $mainOut)) { Write-Err "主 mod 构建产物缺失: $mainOut" }
    if (-not (Invoke-DeployOne $mainOut $mainSlotDir $MainSlotDll)) { $fail++ }
}
if ($mode -eq "check") {
    if (-not (Invoke-CheckOne $mainOut $mainSlotDir $MainSlotDll)) { $fail++ }
}

# 各 fix 仓库
foreach ($name in $FixRepos) {
    $repoDir = Join-Path $ReposRoot $name
    $csproj = Join-Path $repoDir "$name.csproj"
    $outDll = Join-Path $repoDir "$name.dll"
    $slotDir = Join-Path $modsDir $name
    if (-not (Test-Path -LiteralPath $csproj)) {
        Write-Host "[!] 跳过（无 csproj）: $repoDir" -ForegroundColor Yellow
        continue
    }
    if ($mode -eq "all" -or $mode -eq "build") {
        if (-not (Invoke-BuildRepo $repoDir $csproj $outDll)) { $fail++; continue }
    }
    if ($mode -eq "all" -or $mode -eq "deploy") {
        if (-not (Test-Path -LiteralPath $outDll)) { Write-Host "[X] 构建产物缺失: $outDll" -ForegroundColor Red; $fail++; continue }
        if (-not (Invoke-DeployOne $outDll $slotDir "$name.dll")) { $fail++ }
    }
    if ($mode -eq "check") {
        if (-not (Invoke-CheckOne $outDll $slotDir "$name.dll")) { $fail++ }
    }
}

Write-Host ""
if ($fail -gt 0) {
    Write-Host "[X] 完成，共 $fail 个 mod 失败。" -ForegroundColor Red
    exit 1
}
Write-Ok "全部完成（mode=$mode）。"
exit 0
