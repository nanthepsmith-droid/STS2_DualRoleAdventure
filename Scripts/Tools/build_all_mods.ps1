# build_all_mods.ps1 - 一键构建 + 部署 + 校验全部 mod（主 mod + 自动发现的其余 mod 仓库）
#
# 对应《mod系统维护性改进实施方案》任务 1.5（修复仓库统一，保守方案）：
#   消除多个独立 mod 仓库的分散部署：一条命令完成
#     1) 逐个 dotnet build -c Release（主 mod 走 LocalMultiControl.csproj，其余走各自 csproj）
#     2) 逐个部署到游戏 mods 槽位（主 mod 部署为 DualRoleAdventurefixed.dll，沿用既有 json 槽位，不覆盖 json）
#     3) 逐个 SHA256 字节校验 + 尝试解析 marker
#
# 【mod 集合是动态的】(2026-09-08)
#   默认**自动发现**：扫描 $ReposRoot 下所有含 *.csproj 的目录即为一个 mod 仓库，
#   新增 mod 无需改本脚本；产物名读 csproj 的 <AssemblyName>，槽位默认同名。
#   需要「例外」时改同目录的 mod_registry.json：
#     enabled=false -> 官方已修复 / 不再使用的 mod，跳过构建与部署
#                      （注意：游戏仍会加载槽位里残留的 dll，脚本会 WARN，需手动清槽位）
#     slot / dll    -> 覆盖槽位目录名 / 部署后的 dll 文件名
#
# 用法:
#   .\Scripts\Tools\build_all_mods.ps1                 # 全量：构建 + 部署 + 校验
#   .\Scripts\Tools\build_all_mods.ps1 -List           # 只列出发现的 mod 与启用/部署状态
#   .\Scripts\Tools\build_all_mods.ps1 -Only Act4FinalAscentFixes   # 只处理指定 mod（可多个）
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
    # 兼容旧用法：显式给出清单时只处理这些（缺省 = 自动发现）
    [string[]]$FixRepos = @(),
    # 已知的非 mod 目录（无 csproj 也会自动排除，这里只是加速 + 防止将来误放 csproj）
    [string[]]$SkipDirs = @("tools", "skills", "maintenance-docs", "sts2src", "sts2dll", "docs"),
    [string]$Registry = "Scripts\Tools\mod_registry.json",
    [switch]$List,
    [string[]]$Only = @(),
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

# 槽位健康：扫描全部 mods 下 json 的 id，重复 id 会导致游戏报 DUPLICATE_ID / 只加载其一。
# 只 WARN 不 FAIL（历史备份槽如 dualroleadventureold 是否清理由人工决定）。
function Find-DuplicateModIds {
    $seen = @{}
    $dup = @{}
    foreach ($json in (Get-ChildItem -LiteralPath $modsDir -Filter *.json -File -Recurse -ErrorAction SilentlyContinue)) {
        try {
            $m = [regex]::Match((Get-Content -LiteralPath $json.FullName -Raw -Encoding UTF8), '"id"\s*:\s*"([^"]+)"')
            if (-not $m.Success) { continue }
            $id = $m.Groups[1].Value
            if ($seen.ContainsKey($id)) {
                if (-not $dup.ContainsKey($id)) { $dup[$id] = @($seen[$id]) }
                $dup[$id] += $json.FullName
            } else {
                $seen[$id] = $json.FullName
            }
        } catch { }
    }
    if ($dup.Count -gt 0) {
        Write-Host "" -ForegroundColor Yellow
        foreach ($id in $dup.Keys) {
            Write-Host "[!] 发现重复的 mod id '$id'（游戏会报 DUPLICATE_ID，只加载其中一个）:" -ForegroundColor Yellow
            foreach ($p in $dup[$id]) { Write-Host "      $p" -ForegroundColor Yellow }
        }
        Write-Host "      如果这是备份/测试槽位且与正式槽 id 相同，请改备份 json 的 id 或删除槽位。" -ForegroundColor Yellow
    }
}
Find-DuplicateModIds

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

# 读取可选注册表 mod_registry.json（不存在 = 全部启用，走自动发现）
function Read-ModRegistry {
    $map = @{}
    $repoRootHere = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $path = if ([System.IO.Path]::IsPathRooted($Registry)) { $Registry } else { Join-Path $repoRootHere $Registry }
    if (-not (Test-Path -LiteralPath $path)) { return $map }
    try {
        $json = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($m in $json.mods) {
            $map[$m.name] = @{
                enabled = if ($m.PSObject.Properties['enabled']) { [bool]$m.enabled } else { $true }
                slot    = if ($m.PSObject.Properties['slot']) { [string]$m.slot } else { "" }
                dll     = if ($m.PSObject.Properties['dll']) { [string]$m.dll } else { "" }
                note    = if ($m.PSObject.Properties['note']) { [string]$m.note } else { "" }
            }
        }
    } catch {
        Write-Host "[!] 读取 mod_registry.json 失败（按全部启用处理）: $($_.Exception.Message)" -ForegroundColor Yellow
    }
    return $map
}

# 自动发现 mod 仓库：$ReposRoot 下每个含 *.csproj 的目录即一个 mod（主仓库除外，单独处理）
function Discover-Mods {
    param([string]$Root, [hashtable]$RegistryMap)

    $found = @()
    foreach ($dir in (Get-ChildItem -LiteralPath $Root -Directory)) {
        if ($dir.Name.StartsWith(".")) { continue }
        if ($SkipDirs -contains $dir.Name) { continue }
        if ($dir.Name -eq $MainRepo) { continue }

        $csproj = Join-Path $dir.FullName ($dir.Name + ".csproj")
        if (-not (Test-Path -LiteralPath $csproj)) {
            $any = @(Get-ChildItem -LiteralPath $dir.FullName -Filter *.csproj -File)
            if ($any.Count -eq 0) { continue }
            $csproj = $any[0].FullName
        }

        # 产物名以 csproj 的 <AssemblyName> 为准（不一定等于目录名）
        $assemblyName = $dir.Name
        try {
            $text = [System.IO.File]::ReadAllText($csproj)
            $m = [regex]::Match($text, "<AssemblyName>(.*?)</AssemblyName>")
            if ($m.Success -and $m.Groups[1].Value.Trim()) { $assemblyName = $m.Groups[1].Value.Trim() }
        } catch { }

        $enabled = $true
        $slot = $dir.Name
        $slotDll = "$assemblyName.dll"
        $note = ""
        if ($RegistryMap.ContainsKey($dir.Name)) {
            $entry = $RegistryMap[$dir.Name]
            $enabled = $entry.enabled
            if ($entry.slot) { $slot = $entry.slot }
            if ($entry.dll) { $slotDll = $entry.dll }
            $note = $entry.note
        }

        $found += [pscustomobject]@{
            Name     = $dir.Name
            RepoDir  = $dir.FullName
            Csproj   = $csproj
            OutDll   = Join-Path $dir.FullName "$assemblyName.dll"
            Slot     = $slot
            SlotDll  = $slotDll
            Enabled  = $enabled
            Note     = $note
        }
    }
    return $found
}

# 槽位健康检查：禁用的 mod 若槽位仍残留 dll，游戏照样加载；新 mod 槽位缺 json 则根本不会被识别
function Test-SlotHygiene {
    param($Mod)
    $slotDir = Join-Path $modsDir $Mod.Slot
    if (-not (Test-Path -LiteralPath $slotDir)) { return }
    if (-not $Mod.Enabled) {
        $leftover = @(Get-ChildItem -LiteralPath $slotDir -Filter *.dll -File -ErrorAction SilentlyContinue)
        if ($leftover.Count -gt 0) {
            Write-Host "[!] $($Mod.Name) 已在注册表禁用，但槽位仍有 dll，游戏仍会加载它：" -ForegroundColor Yellow
            $leftover | ForEach-Object { Write-Host "      $($_.FullName)" -ForegroundColor Yellow }
            Write-Host "      确认不再需要后请手动删除该槽位目录。" -ForegroundColor Yellow
        }
        return
    }
    $json = @(Get-ChildItem -LiteralPath $slotDir -Filter *.json -File -ErrorAction SilentlyContinue)
    if ($json.Count -eq 0) {
        Write-Host "[!] $($Mod.Name) 槽位缺少 *.json，游戏不会把它识别为 mod: $slotDir" -ForegroundColor Yellow
    }
}

# 单仓库构建：返回 dll 路径（构建产物在仓库根）或 $null
# 可选传 $TestCsproj：构建成功后跑 dotnet test，0 失败才算构建通过（任务 2.1 单元测试门槛）
function Invoke-BuildRepo {
    param([string]$RepoDir, [string]$Csproj, [string]$OutDll, [string]$TestCsproj = "")
    Write-Step "构建 $RepoDir ..."
    Push-Location $RepoDir
    try {
        dotnet build $Csproj -c Release | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Host "[X] 构建失败: $RepoDir（exit=$LASTEXITCODE）" -ForegroundColor Red
            return $null
        }
        if ($TestCsproj) {
            # RequireGameInstall=true：部署门禁语义，缺游戏安装判失败（本地开发默认 Skip）
            dotnet test $TestCsproj -c Release --nologo -p:RequireGameInstall=true | Out-Host
            if ($LASTEXITCODE -ne 0) {
                Write-Host "[X] 单元测试未全绿，禁止部署: $RepoDir（exit=$LASTEXITCODE）" -ForegroundColor Red
                return $null
            }
            Write-Ok "单元测试全绿: $TestCsproj"
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

# CLR/PE/Assembly 兼容性门禁（AGENTS.md §9 门禁 4/5）：构建后、部署前
function Invoke-CompatCheck {
    param([string]$RepoDir, [string]$OutDll)
    $script = Join-Path $RepoDir "Scripts\Tools\clr_compat_check.py"
    if (-not (Test-Path -LiteralPath $script)) {
        Write-Host "[!] 跳过兼容性检查（无 clr_compat_check.py）: $RepoDir" -ForegroundColor Yellow
        return $true
    }
    Write-Step "CLR/PE 兼容性检查: $OutDll"
    Push-Location $RepoDir
    try {
        python $script --mod-dll $OutDll | Out-Host
        $code = $LASTEXITCODE
    } finally { Pop-Location }
    if ($code -ne 0) {
        Write-Host "[X] 兼容性检查未通过，禁止部署（exit=$code）" -ForegroundColor Red
        return $false
    }
    Write-Ok "兼容性检查通过"
    return $true
}

# 部署后字节/结构校验（AGENTS.md §9 门禁 6/7）：缺文件 = FAIL，不允许跳过即绿
function Invoke-DllCheck {
    param([string]$SrcDll, [string]$SlotDir, [string]$SlotDllName)
    $script = Join-Path $ReposRoot "tools\dll_check.py"
    if (-not (Test-Path -LiteralPath $script)) {
        Write-Host "[!] 跳过 dll_check（无 tools\dll_check.py）" -ForegroundColor Yellow
        return $true
    }
    $dst = Join-Path $SlotDir $SlotDllName
    Write-Step "部署后校验: $dst"
    python $script --deployed --expect-deployed --slot-dll $dst --root-dll $SrcDll | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[X] 部署校验未通过（exit=$LASTEXITCODE）" -ForegroundColor Red
        return $false
    }
    Write-Ok "部署校验通过"
    return $true
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
$mainTestCsproj = Join-Path $mainRepoDir "tests\LocalMultiControl.Tests\LocalMultiControl.Tests.csproj"
$mainOut = Join-Path $mainRepoDir "DualRoleAdventure.dll"
$mainSlotDir = Join-Path $modsDir $MainSlot
if (-not (Test-Path -LiteralPath $mainCsproj)) { Write-Err "主仓库 csproj 不存在: $mainCsproj" }

# ---------------------------------------------------------------- mod 集合：自动发现 + 注册表
$registryMap = Read-ModRegistry
if ($FixRepos.Count -gt 0) {
    # 兼容旧用法：显式清单优先
    $mods = @()
    foreach ($name in $FixRepos) {
        $repoDir = Join-Path $ReposRoot $name
        $csproj = Join-Path $repoDir "$name.csproj"
        if (-not (Test-Path -LiteralPath $csproj)) {
            Write-Host "[!] 跳过（无 csproj）: $repoDir" -ForegroundColor Yellow
            continue
        }
        $mods += [pscustomobject]@{
            Name = $name; RepoDir = $repoDir; Csproj = $csproj
            OutDll = Join-Path $repoDir "$name.dll"; Slot = $name; SlotDll = "$name.dll"
            Enabled = $true; Note = "(显式清单)"
        }
    }
} else {
    $mods = @(Discover-Mods -Root $ReposRoot -RegistryMap $registryMap)
}
if ($Only.Count -gt 0) { $mods = @($mods | Where-Object { $Only -contains $_.Name }) }
# 主 mod 是否参与本轮（-Only 未指定时总是参与）
$mainSelected = ($Only.Count -eq 0) -or ($Only -contains $MainRepo) -or ($Only -contains "main")

function Get-SlotState([string]$SrcDll, [string]$Slot, [string]$SlotDllName) {
    $dst = Join-Path (Join-Path $modsDir $Slot) $SlotDllName
    if (-not (Test-Path -LiteralPath $dst)) { return @("no", "-") }
    if (-not (Test-Path -LiteralPath $SrcDll)) { return @("yes", "?") }
    if ((Get-FileHash $dst).Hash -eq (Get-FileHash $SrcDll).Hash) { return @("yes", "yes") }
    return @("yes", "NO")
}

if ($List) {
    Write-Host ""
    Write-Host ("{0,-32} {1,-7} {2,-28} {3,-30} {4,-6} {5,-6} {6}" -f "MOD", "ENABLED", "SLOT", "DEPLOYED_DLL", "SLOT?", "SAME?", "NOTE")
    Write-Host ("-" * 128)
    $ms = Get-SlotState $mainOut $MainSlot $MainSlotDll
    Write-Host ("{0,-32} {1,-7} {2,-28} {3,-30} {4,-6} {5,-6} {6}" -f $MainRepo, "yes", $MainSlot, $MainSlotDll, $ms[0], $ms[1], "(主 mod)")
    foreach ($m in $mods) {
        $s = Get-SlotState $m.OutDll $m.Slot $m.SlotDll
        Write-Host ("{0,-32} {1,-7} {2,-28} {3,-30} {4,-6} {5,-6} {6}" -f $m.Name,
            $(if ($m.Enabled) { "yes" } else { "NO" }), $m.Slot, $m.SlotDll, $s[0], $s[1], $m.Note)
        Test-SlotHygiene $m
    }
    $disabled = @($mods | Where-Object { -not $_.Enabled }).Count
    Write-Host ""
    Write-Host ("共 {0} 个 mod 仓库（含主 mod），禁用 {1} 个。新增 mod 无需改脚本：把仓库放到 {2} 下即可被自动发现。" -f ($mods.Count + 1), $disabled, $ReposRoot)
    exit 0
}

if ($mainSelected -and ($mode -eq "all" -or $mode -eq "build")) {
    if (-not (Invoke-BuildRepo $mainRepoDir $mainCsproj $mainOut $mainTestCsproj)) { $fail++ }
    elseif (-not (Invoke-CompatCheck $mainRepoDir $mainOut)) { $fail++ }
}
if ($mainSelected -and ($mode -eq "all" -or $mode -eq "deploy")) {
    if (-not (Test-Path -LiteralPath $mainOut)) { Write-Err "主 mod 构建产物缺失: $mainOut" }
    if (-not (Invoke-DeployOne $mainOut $mainSlotDir $MainSlotDll)) { $fail++ }
    elseif (-not (Invoke-DllCheck $mainOut $mainSlotDir $MainSlotDll)) { $fail++ }
}
if ($mainSelected -and ($mode -eq "check")) {
    if (-not (Invoke-CheckOne $mainOut $mainSlotDir $MainSlotDll)) { $fail++ }
}

# 其余 mod 仓库（自动发现，注册表控制启用/禁用 → 构建 → 部署 → 校验）
foreach ($m in $mods) {
    Test-SlotHygiene $m
    if (-not $m.Enabled) {
        Write-Host "[=] 已禁用（mod_registry.json），跳过: $($m.Name)" -ForegroundColor DarkGray
        continue
    }
    $slotDir = Join-Path $modsDir $m.Slot
    if ($mode -eq "all" -or $mode -eq "build") {
        if (-not (Invoke-BuildRepo $m.RepoDir $m.Csproj $m.OutDll)) { $fail++; continue }
    }
    if ($mode -eq "all" -or $mode -eq "deploy") {
        if (-not (Test-Path -LiteralPath $m.OutDll)) {
            Write-Host "[X] 构建产物缺失: $($m.OutDll)" -ForegroundColor Red
            $fail++
            continue
        }
        if (-not (Invoke-DeployOne $m.OutDll $slotDir $m.SlotDll)) { $fail++ }
    }
    if ($mode -eq "check") {
        if (-not (Invoke-CheckOne $m.OutDll $slotDir $m.SlotDll)) { $fail++ }
    }
}

Write-Host ""
if ($fail -gt 0) {
    Write-Host "[X] 完成，共 $fail 个 mod 失败。" -ForegroundColor Red
    exit 1
}
Write-Ok "全部完成（mode=$mode）。"
exit 0
