# dual_version_probe.ps1 - 双版本（正式版 / beta）兼容性探针对账
#
# 为什么需要：
#   从 0.112 起上游做了一串破坏性重构（选择器栈按玩家拆开、商店购买改名、RelicsAwarded 加参数、
#   遗物格改布局制、新增恐惧/闪电战两个模式与「愚者」角色）。而**正式版与 beta 两个分支同时在线**，
#   一份 dll 要两边都能跑 ⇒ 每次改动都要能回答"这条改动在另一个版本上会不会炸"。
#
#   本脚本对**每个**指定版本做两件事，再把结果摆成一张对比表：
#     ① 编译探针：用该版本的 sts2.dll 编一遍（**不覆盖仓库根产物、不部署**），
#        把"编译期不兼容点"按文件分组列出来（= 该版本上缺失的类型/成员）；
#     ② 字符串/反射目标核对：拿该版本的反编译树跑 check_string_targets.py，
#        列出 STALE 目标（= 运行期才会静默失效的那类）。
#   然后按"在哪些版本上失效"分类：
#     · 所有版本都失效  ⇒ 真失效（我们的锅 / 上游整体改名）→ 必修
#     · 只在较旧版本失效 ⇒ 版本门控候选（新功能；低版本本来没有）→ 用 GameCapabilities 门控
#     · 只在较新版本失效 ⇒ 新版本改名（要加"新名优先 + 旧名回退"）
#
# 用法：
#   .\Scripts\Tools\dual_version_probe.ps1                                  # 自动发现 game-archives\v*\，全部跑
#   .\Scripts\Tools\dual_version_probe.ps1 -Archives v0.111.0,v0.113.1
#   .\Scripts\Tools\dual_version_probe.ps1 -GameDir "D:\Steam...\Slay the Spire 2" -SrcDir "D:\Download\pain\sts2src\src" -Label "当前安装"
#   .\Scripts\Tools\dual_version_probe.ps1 -SkipBuild                       # 只跑字符串/反射目标核对
#   .\Scripts\Tools\dual_version_probe.ps1 -OutFile .\probe.md
#
# 前提（脚本会自己补齐能补的）：
#   · 每个版本目录要有 data_sts2_windows_x86_64\sts2.dll；缺 0Harmony.dll / Steamworks.NET.dll
#     时从当前游戏安装目录补（Gate 口径：只在探测目录里补，不动游戏目录、不动仓库根产物）。
#   · 反编译树按约定名猜：game-archives\v0.111.0 -> ..\sts2src-1110-src\src（minor+patch，无前导 0）；
#     最新的那个版本默认用 ..\sts2src\src。猜不到就只跑编译探针。
#
# 退出码：0 = 对账完成（有差异也打印，差异本身不是错误）；1 = 用法 / 环境错误。

param(
    [string]$RepoDir = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path,
    [string]$ArchiveRoot = "D:\Download\pain\game-archives",
    [string]$GameDir = "D:\SteamLibrary\steamapps\common\Slay the Spire 2",
    [string[]]$Archives = @(),
    [string]$SrcDir = "",
    [string]$Label = "",
    [string]$OutFile = (Join-Path $env:TEMP "dual-version-probe.md"),
    [switch]$SkipBuild,
    [switch]$SkipStringTargets
)

$ErrorActionPreference = "Stop"
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

function Write-Step { param([string]$m) Write-Host "[*] $m" -ForegroundColor Cyan }
function Write-Ok   { param([string]$m) Write-Host "[OK] $m" -ForegroundColor Green }
function Write-Warn2 { param([string]$m) Write-Host "[!] $m" -ForegroundColor Yellow }
function Write-Err  { param([string]$m) Write-Host "[X] $m" -ForegroundColor Red; exit 1 }

function Read-GameVersion {
    param([string]$Dir)
    $info = Join-Path $Dir "release_info.json"
    if (-not (Test-Path -LiteralPath $info)) { return $null }
    try { return [string](Get-Content -LiteralPath $info -Raw -Encoding UTF8 | ConvertFrom-Json).version } catch { return $null }
}

# 反编译树命名约定：sts2src-<minor><patch>-src（0.111.0 -> 1110），最新版本用 sts2src\src
function Resolve-SrcTree {
    param([string]$Version)
    if (-not $Version) { return $null }
    $v = $Version.TrimStart('v', 'V')
    $parts = $v.Split('.')
    if ($parts.Length -lt 2) { return $null }
    $patch = if ($parts.Length -ge 3) { $parts[2] } else { "0" }
    $candidate = Join-Path (Split-Path $RepoDir -Parent) ("sts2src-{0}{1}-src\src" -f $parts[1], $patch)
    if (Test-Path -LiteralPath $candidate) { return $candidate }
    $latest = Join-Path (Split-Path $RepoDir -Parent) "sts2src\src"
    if (Test-Path -LiteralPath $latest) { return $latest }
    return $null
}

function Ensure-ProbeDependencies {
    param([string]$DataDir)
    $binSource = Join-Path $GameDir "data_sts2_windows_x86_64"
    foreach ($name in @("0Harmony.dll", "Steamworks.NET.dll")) {
        $target = Join-Path $DataDir $name
        if (Test-Path -LiteralPath $target) { continue }
        $source = Join-Path $binSource $name
        if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $target -Force }
    }
}

function Invoke-BuildProbe {
    param([string]$VersionDir, [string]$LabelText)

    $dataDir = Join-Path $VersionDir "data_sts2_windows_x86_64"
    if (-not (Test-Path -LiteralPath (Join-Path $dataDir "sts2.dll"))) {
        Write-Warn2 "$LabelText 缺少 $dataDir\sts2.dll，跳过编译探针"
        return $null
    }

    Ensure-ProbeDependencies -DataDir $dataDir

    # 中间/输出目录用**绝对路径**：相对路径是相对"当前工作目录"解析的，脚本被别处调用时会写错地方
    $objDir = Join-Path $RepoDir "obj-probe"
    $binDir = Join-Path $RepoDir "bin-probe"
    Remove-Item -Recurse -Force $objDir, $binDir -ErrorAction SilentlyContinue

    $gameDirArg = ($VersionDir -replace '\\', '/')
    $objArg = ($objDir -replace '\\', '/') + "/"
    $binArg = ($binDir -replace '\\', '/') + "/"
    $log = & dotnet build (Join-Path $RepoDir "LocalMultiControl.csproj") -c Release `
        "-p:Sts2Dir=$gameDirArg" `
        "-p:AssemblyName=DualRoleAdventure.Probe" `
        "-p:BaseIntermediateOutputPath=$objArg" `
        "-p:BaseOutputPath=$binArg" `
        -v:m 2>&1

    $errors = @()
    foreach ($line in $log) {
        $text = [string]$line
        if ($text -notmatch ": error ") { continue }
        if ($text -match '([\w\.]+\.cs)\((\d+),\d+\): (error CS\d+: .*?)(?: \[|$)') {
            $errors += [pscustomobject]@{
                File    = $Matches[1]
                Line    = [int]$Matches[2]
                Message = $Matches[3].Trim()
            }
        }
    }

    Remove-Item -Recurse -Force $objDir, $binDir -ErrorAction SilentlyContinue
    Remove-Item -Force (Join-Path $RepoDir "DualRoleAdventure.Probe.dll") -ErrorAction SilentlyContinue

    $unique = @($errors | Sort-Object File, Line, Message -Unique)
    Write-Ok "$LabelText 编译探针: $($unique.Count) 处不兼容"

    # 注意：直接 `return $unique` 在 0 条时会被 PowerShell 展开成 $null（= 上层误判为"跳过"），
    # 所以用一元逗号包一层，保住"空数组"的语义
    return ,$unique
}

function Invoke-StringTargetProbe {
    param([string]$Tree)

    if (-not $Tree -or -not (Test-Path -LiteralPath $Tree)) { return $null }
    $tool = Join-Path $RepoDir "Scripts\Tools\check_string_targets.py"
    $json = & python $tool --repo $RepoDir --src $Tree --json 2>$null
    if (-not $json) { return $null }

    try { $payload = ($json -join "`n") | ConvertFrom-Json } catch { return $null }
    $stale = @($payload.rows | Where-Object { $_.status -in @("STALE-TYPE", "STALE-METHOD") })
    Write-Ok "字符串/反射目标: STALE=$($stale.Count)（总目标 $($payload.rows.Count)）"
    return [pscustomobject]@{ Stale = $stale; Counts = $payload.counts; Total = $payload.rows.Count }
}

# ------------------------------------------------------------------ 目标收集

$targets = @()
if ($GameDir) {
    $version = Read-GameVersion -Dir $GameDir
    $src = if ($SrcDir) { $SrcDir } else { Resolve-SrcTree -Version $version }
    $targets += [pscustomobject]@{
        Label    = if ($Label) { $Label } else { "当前安装" }
        Version  = $version
        GameDir  = $GameDir
        SrcDir   = $src
    }
}

if ($Archives.Count -eq 0 -and (Test-Path -LiteralPath $ArchiveRoot)) {
    $Archives = @(Get-ChildItem -LiteralPath $ArchiveRoot -Directory |
        Where-Object { $_.Name -like "v*" } |
        Sort-Object Name |
        Select-Object -ExpandProperty Name)
}

foreach ($name in $Archives) {
    $dir = Join-Path $ArchiveRoot $name
    if (-not (Test-Path -LiteralPath $dir)) { Write-Warn2 "跳过不存在的归档: $name"; continue }
    $version = Read-GameVersion -Dir $dir
    $targets += [pscustomobject]@{
        Label   = $name
        Version = $version
        GameDir = $dir
        SrcDir  = Resolve-SrcTree -Version $version
    }
}

if ($targets.Count -eq 0) { Write-Err "没有可探测的版本目录（用 -GameDir / -Archives 指定）" }

# ------------------------------------------------------------------ 跑

$results = @()
foreach ($target in $targets) {
    Write-Step "探测 $($target.Label)（$($target.Version)）"

    $buildErrors = if ($SkipBuild) { $null } else { Invoke-BuildProbe -VersionDir $target.GameDir -LabelText $target.Label }
    $staleProbe = if ($SkipStringTargets) { $null } else { Invoke-StringTargetProbe -Tree $target.SrcDir }

    if (-not $SkipStringTargets -and -not $target.SrcDir) {
        Write-Warn2 "$($target.Label) 找不到反编译树，跳过字符串目标核对（可先跑 regenerate_src.ps1）"
    }

    $results += [pscustomobject]@{
        Label       = $target.Label
        Version     = $target.Version
        BuildErrors = $buildErrors
        Stale       = if ($staleProbe) { $staleProbe.Stale } else { $null }
        StaleCount  = if ($staleProbe) { $staleProbe.Stale.Count } else { $null }
    }
}

# ------------------------------------------------------------------ 分类

function Get-StaleKey { param($row) "$($row.file)|$($row.type)|$($row.member)" }

$staleMatrix = @{}
foreach ($r in $results) {
    if ($null -eq $r.Stale) { continue }
    foreach ($row in $r.Stale) {
        $key = Get-StaleKey $row
        if (-not $staleMatrix.ContainsKey($key)) {
            $staleMatrix[$key] = [pscustomobject]@{
                File    = $row.file
                Line    = $row.lineno
                Type    = $row.type
                Member  = $row.member
                Status  = $row.status
                In      = @{}
            }
        }
        $staleMatrix[$key].In[$r.Label] = $true
    }
}

function Get-VersionKey {
    param([string]$Version)
    $text = ([string]$Version).TrimStart('v', 'V')
    try { return [version]$text } catch { return [version]"0.0.0" }
}

# "最新版本"= 参与对比的最高版本号（可能同时有"当前安装"与归档两份，都算）
$newestVersionText = ($results | Sort-Object { Get-VersionKey $_.Version } | Select-Object -Last 1).Version
$newestLabels = @($results | Where-Object { $_.Version -eq $newestVersionText } | ForEach-Object { $_.Label })

$brokenOnNewest = @()
$olderOnly = @()
foreach ($entry in $staleMatrix.Values) {
    $inNewest = $false
    foreach ($label in $newestLabels) {
        if ($entry.In.ContainsKey($label)) { $inNewest = $true; break }
    }

    if ($inNewest) { $brokenOnNewest += $entry } else { $olderOnly += $entry }
}

# ------------------------------------------------------------------ 输出

$L = New-Object System.Collections.Generic.List[string]
$L.Add("# 双版本兼容探针对账")
$L.Add("")
$L.Add("> 生成时间：$(Get-Date -Format 'yyyy-MM-dd HH:mm')；仓库：``$RepoDir``")
$L.Add("")

$L.Add("## 概览")
$L.Add("")
$L.Add("| 版本 | 编译期不兼容 | 字符串目标 STALE | 反编译树 |")
$L.Add("|---|---|---|---|")
foreach ($r in $results) {
    $build = if ($null -eq $r.BuildErrors) { "(跳过)" } else { "$($r.BuildErrors.Count)" }
    $stale = if ($null -eq $r.StaleCount) { "(跳过/无树)" } else { "$($r.StaleCount)" }
    $target = $targets | Where-Object { $_.Label -eq $r.Label } | Select-Object -First 1
    $tree = if ($target.SrcDir) { "``$($target.SrcDir)``" } else { "—" }
    $L.Add("| $($r.Version)（$($r.Label)） | $build | $stale | $tree |")
}
$L.Add("")

foreach ($r in $results) {
    if ($null -eq $r.BuildErrors -or $r.BuildErrors.Count -eq 0) { continue }
    $L.Add("## 编译期不兼容：$($r.Version)（$($r.Label)）")
    $L.Add("")
    $L.Add("> 含义：这些类型/成员在该版本**不存在**。若在补丁类里 ⇒ 该类会被跳过（功能缺失但能加载）；")
    $L.Add("> 若在**运行期**代码里 ⇒ 该版本上执行到就抛（必须改成能力门控 + 反射分派）。")
    $L.Add("")
    $L.Add("| 文件 | 行 | 错误 |")
    $L.Add("|---|---|---|")
    foreach ($e in ($r.BuildErrors | Group-Object File | ForEach-Object {
        $first = $_.Group[0]
        [pscustomobject]@{ File = $first.File; Line = (($_.Group | ForEach-Object { $_.Line }) -join ", "); Count = $_.Count; Message = (($_.Group | ForEach-Object { $_.Message } | Sort-Object -Unique) -join "<br>") }
    })) {
        $L.Add("| ``$($e.File)`` | $($e.Line) | $($e.Message) |")
    }
    $L.Add("")
}

if ($staleMatrix.Count -gt 0) {
    $L.Add("## 字符串/反射目标（按版本归因）")
    $L.Add("")
    foreach ($group in @(
        @{ Name = "最新版本上也失效 ⇒ 真失效，必修（我们构建就是对着最新版本）"; Rows = $brokenOnNewest },
        @{ Name = "只在较旧版本失效 ⇒ 版本门控候选（新功能；低版本本就没有，用 GameCapabilities 门控）"; Rows = $olderOnly }
    )) {
        if ($group.Rows.Count -eq 0) { continue }
        $L.Add("### $($group.Name)")
        $L.Add("")
        $L.Add("| 文件 | 行 | 类型 | 成员 | 出现版本 |")
        $L.Add("|---|---|---|---|---|")
        foreach ($row in ($group.Rows | Sort-Object File, Line)) {
            $inLabels = ($row.In.Keys | Sort-Object) -join ", "
            $L.Add("| ``$(Split-Path $row.File -Leaf)`` | $($row.Line) | ``$($row.Type)`` | ``$($row.Member)`` | $inLabels |")
        }
        $L.Add("")
    }
}

$L.Add("## 判读口径")
$L.Add("")
$L.Add("- **编译探针**只覆盖编译期引用；运行期字符串/反射目标看第二段（`check_string_targets.py`）。")
$L.Add("- 探针在**旧版本**上编译失败是**预期**的一部分：补丁类会因类型加载失败被跳过（Entry 已容错），")
$L.Add("  真正要关心的是「运行期代码」（非补丁类）里的不兼容点。")
$L.Add("- 新功能（恐惧/闪电战/愚者）在低版本不存在 ⇒ 用 `GameCapabilities` 门控，不要写版本号。")
$L.Add("- 结果落地后同步更新 `开发进度记录.md` 的收工块与 `CHANGELOG.md`。")
$L.Add("")

$text = ($L -join "`n")
Set-Content -LiteralPath $OutFile -Value $text -Encoding utf8

Write-Host ""
Write-Host "== 概览 ==" -ForegroundColor Cyan
foreach ($r in $results) {
    $build = if ($null -eq $r.BuildErrors) { "(跳过)" } else { "$($r.BuildErrors.Count)" }
    $stale = if ($null -eq $r.StaleCount) { "(跳过/无树)" } else { "$($r.StaleCount)" }
    Write-Host ("  {0,-10} 编译不兼容={1,-4} 字符串STALE={2}" -f $r.Version, $build, $stale)
}
Write-Ok "明细已写入: $OutFile"
