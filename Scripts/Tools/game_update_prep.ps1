# game_update_prep.ps1 - 游戏版本更新（如 0.111 -> 0.112）的「更新前快照 + 更新后对账」
#
# 为什么需要：
#   游戏更新会替换 sts2.dll / 0Harmony.dll / release_info.json，而反编译参考树（sts2src）是**手工重生成**的。
#   如果更新前没留基线，更新后无法区分三件事：
#     a) 目标消失是游戏改的，还是我们上一轮就改坏的；
#     b) ilspy 反编译失败是 dll 变了，还是工具版本问题；
#     c) "这局 ERRRO 变多" 是更新引起还是本来如此。
#   所以：**更新前先 -Snapshot（并把 baselines/game-version-baseline.json 提交）**，
#         更新后跑 -Verify 拿一张对账表，再按 AGENTS.md §5 走适配。
#
# 用法：
#   .\Scripts\Tools\game_update_prep.ps1 -Snapshot      # 更新前：记基线 + 归档旧 dll + 备份存档 zip
#   .\Scripts\Tools\game_update_prep.ps1 -Verify        # 更新后：版本/哈希对账 + 补丁目标 + 字符串目标 + 静态层
#   .\Scripts\Tools\game_update_prep.ps1 -Verify -SkipTests    # 只想看静态结论时
#   .\Scripts\Tools\game_update_prep.ps1 -Snapshot -NoArchive  # 只写 json，不拷贝 dll / 不备份存档
#   .\Scripts\Tools\game_update_prep.ps1 -Snapshot -NoSaves    # 归档 dll 但不备份存档
#
# 更新日顺序（**别跳步**）：
#   0) 更新前：git status 干净、记住 master 的 sha（回滚点）-> -Snapshot -> 提交 baseline json
#   1) 更新后：-Verify（此时 sts2src 还是**旧版**，脚本会明确标注字符串核对仅供参考）
#   2) .\Scripts\Tools\regenerate_src.ps1        # 几分钟，重生成 sts2src
#   3) -Verify 再跑一次（这次字符串核对才是准的）
#   4) dotnet build 修编译错误（编译错误 = 成员改名/被删）-> 修字符串/反射目标
#   5) .\Scripts\Tools\preflight.ps1 -Deploy     # 全套门禁 + 部署 + 字节校验
#   6) 实机抽查（references/实机回归清单.md 的通用期望 0 清单）
#
# 退出码：0 = 对账完成（有差异也会打印，差异本身不是错误）；1 = 用法/环境错误（缺基线、缺游戏目录等）

param(
    [string]$GameDir = "D:\SteamLibrary\steamapps\common\Slay the Spire 2",
    [string]$ArchiveDir = "D:\Download\pain\game-archives",
    [string]$BaselineRel = "Scripts\Tools\baselines\game-version-baseline.json",
    [switch]$Snapshot,
    [switch]$Verify,
    [switch]$SkipTests,
    [switch]$NoArchive,
    [switch]$NoSaves
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path

# python 侧输出是 UTF-8，控制台默认 GBK ⇒ 不显式指定会拿到乱码（解析数字/中文都会失败）
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

function Write-Step { param([string]$msg) Write-Host "[*] $msg" -ForegroundColor Cyan }
function Write-Ok   { param([string]$msg) Write-Host "[OK] $msg" -ForegroundColor Green }
function Write-Warn2 { param([string]$msg) Write-Host "[!] $msg" -ForegroundColor Yellow }
function Write-Err  { param([string]$msg) Write-Host "[X] $msg" -ForegroundColor Red; exit 1 }

function Get-Sha256 {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Short {
    param([string]$Hash)
    if (-not $Hash) { return "-" }
    if ($Hash.Length -gt 12) { return $Hash.Substring(0, 12) + "..." }
    return $Hash
}

function Get-AssemblyVersion {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try { return [System.Reflection.AssemblyName]::GetAssemblyName($Path).Version.ToString() }
    catch { return "unknown" }
}

# 游戏侧事实：版本号**只认 release_info.json**（sts2.dll 的 AssemblyVersion 恒为 0.1.0.0）
function Get-GameInfo {
    $releaseInfoPath = Join-Path $GameDir "release_info.json"
    $binDir = Join-Path $GameDir "data_sts2_windows_x86_64"
    $sts2Dll = Join-Path $binDir "sts2.dll"
    if (-not (Test-Path -LiteralPath $releaseInfoPath)) { Write-Err "未找到 release_info.json: $releaseInfoPath" }
    if (-not (Test-Path -LiteralPath $sts2Dll))          { Write-Err "未找到 sts2.dll: $sts2Dll" }
    $info = Get-Content -LiteralPath $releaseInfoPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $dllItem = Get-Item -LiteralPath $sts2Dll
    # ConvertFrom-Json 会把 ISO 时间串自动转成 DateTime（再转回字符串就换了格式）=> 统一回 ISO
    $dateStr = if ($info.date -is [datetime]) { $info.date.ToString("yyyy-MM-ddTHH:mm:sszzz") } else { [string]$info.date }
    return [ordered]@{
        version              = [string]$info.version
        commit               = [string]$info.commit
        date                 = $dateStr
        branch               = [string]$info.branch
        main_assembly_hash   = $info.main_assembly_hash
        sts2_dll_sha256      = (Get-Sha256 $sts2Dll)
        sts2_dll_size        = $dllItem.Length
        sts2_dll_mtime       = $dllItem.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
        harmony              = (Get-AssemblyVersion (Join-Path $binDir "0Harmony.dll"))
        godotsharp           = (Get-AssemblyVersion (Join-Path $binDir "GodotSharp.dll"))
    }
}

# 仓库侧事实：marker / 版本 / HEAD / 目标计数
function Get-RepoInfo {
    $entry = Join-Path $repoRoot "Scripts\Entry.cs"
    $marker = $null
    if (Test-Path -LiteralPath $entry) {
        $m = Select-String -LiteralPath $entry -Pattern 'marker=([^")]+)' | Select-Object -First 1
        if ($m) { $marker = $m.Matches[0].Groups[1].Value }
    }
    $version = $null
    $rootJson = Join-Path $repoRoot "DualRoleAdventure.json"
    if (Test-Path -LiteralPath $rootJson) {
        $version = (Get-Content -LiteralPath $rootJson -Raw -Encoding UTF8 | ConvertFrom-Json).version
    }
    $head = $null
    try { $head = (git -C $repoRoot rev-parse --short HEAD 2>$null) } catch { $head = "unknown" }

    # 目标计数从静态层 JSON 里取（S4 = 补丁类/目标行，S7 = 语义标识），避免第二套解析口径
    $patchClasses = $null; $patchRows = $null; $descriptors = $null
    $out = & python (Join-Path $repoRoot "Scripts\Tools\static_checks.py") --repo $repoRoot --json 2>$null
    # 注意：静态层有 FAIL 时退出码是 1，但 JSON 照样完整 ⇒ **不能**用 $LASTEXITCODE 当解析条件
    if ($out) {
        $payload = ($out -join "`n") | ConvertFrom-Json
        foreach ($c in $payload.checks) {
            if ($c.id -eq "S4" -and $c.extra) {
                $patchClasses = $c.extra.patch_classes
                $patchRows = $c.extra.target_rows
            }
            if ($c.id -eq "S7" -and $c.extra) { $descriptors = $c.extra.descriptors }
        }
    }

    # 字符串/反射目标基线（STALE 应为 0；更新后这个数变了就是游戏改名/删成员）
    $stringTotal = $null; $stringStale = $null
    $out2 = & python (Join-Path $repoRoot "Scripts\Tools\check_string_targets.py") --repo $repoRoot --json 2>$null
    if ($out2) {
        $sp = ($out2 -join "`n") | ConvertFrom-Json
        $stringTotal = @($sp.rows).Count
        $stringStale = $sp.stale
    }

    return [ordered]@{
        marker                    = $marker
        version                   = $version
        head                      = $head
        patch_classes             = $patchClasses
        patch_rows                = $patchRows
        semantic_descriptors      = $descriptors
        string_targets            = $stringTotal
        string_stale              = $stringStale
        targets_baseline_sha256   = (Get-Sha256 (Join-Path $repoRoot "Scripts\Tools\baselines\targets.baseline.txt"))
    }
}

# ---------------------------------------------------------------- 参数检查
if (-not $Snapshot -and -not $Verify) {
    Write-Host "用法："
    Write-Host "  .\Scripts\Tools\game_update_prep.ps1 -Snapshot   # 游戏更新**前**跑：记基线 + 归档旧 dll"
    Write-Host "  .\Scripts\Tools\game_update_prep.ps1 -Verify     # 游戏更新**后**跑：对账 + 目标核对"
    Write-Host ""
    Write-Host "更新日完整顺序见本脚本头部注释 / AGENTS.md §5。"
    exit 1
}
if (-not (Test-Path -LiteralPath $GameDir)) { Write-Err "游戏目录不存在: $GameDir" }
$baselinePath = Join-Path $repoRoot $BaselineRel

# ---------------------------------------------------------------- -Snapshot
if ($Snapshot) {
    Write-Step "读取游戏与仓库事实 ..."
    $game = Get-GameInfo
    $repo = Get-RepoInfo

    $archivePath = $null
    if (-not $NoArchive) {
        $archivePath = Join-Path $ArchiveDir $game.version
        New-Item -ItemType Directory -Force -Path $archivePath | Out-Null
        $binDir = Join-Path $GameDir "data_sts2_windows_x86_64"
        foreach ($f in @("sts2.dll", "0Harmony.dll", "GodotSharp.dll")) {
            $src = Join-Path $binDir $f
            if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination (Join-Path $archivePath $f) -Force }
        }
        Copy-Item -LiteralPath (Join-Path $GameDir "release_info.json") -Destination (Join-Path $archivePath "release_info.json") -Force
        Write-Ok "已归档旧版本二进制: $archivePath"
        Write-Host "     （更新后若要 diff 反编译源码，可 ilspycmd 反编译这里的 sts2.dll）"

        # 顺手留一份"更新前已知可用的我们自己"：适配期出问题时可以拿它做 A/B
        $ownDll = Join-Path $repoRoot "DualRoleAdventure.dll"
        if (Test-Path -LiteralPath $ownDll) {
            $ownName = "DualRoleAdventure-" + ($(if ($repo.marker) { $repo.marker } else { "unknown" })) + ".dll"
            Copy-Item -LiteralPath $ownDll -Destination (Join-Path $archivePath $ownName) -Force
            Write-Ok "已归档当前主 mod dll: $ownName"
        }

        # 存档备份：更新后 mod 内容/结构不兼容时，原版可能把存档改名成 *.VAL.corrupt（历史踩过）
        if (-not $NoSaves) {
            $savesRoot = Join-Path $env:APPDATA "SlayTheSpire2\steam"
            if (Test-Path -LiteralPath $savesRoot) {
                $stamp = (Get-Date).ToString("yyyyMMdd-HHmm")
                $zip = Join-Path $archivePath ("saves-" + $game.version + "-" + $stamp + ".zip")
                try {
                    Compress-Archive -Path (Join-Path $savesRoot '*') -DestinationPath $zip -CompressionLevel Optimal -Force
                    Write-Ok ("已备份存档: {0}（{1:N1} MB）" -f $zip, ((Get-Item -LiteralPath $zip).Length / 1MB))
                } catch {
                    Write-Warn2 "存档备份失败（不影响基线记录）：$($_.Exception.Message)"
                }
            } else {
                Write-Warn2 "未找到存档目录 $savesRoot，跳过存档备份。"
            }
        }
    }

    $baseline = [ordered]@{
        recorded_at = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
        game        = $game
        repo        = $repo
        own_dll_sha256 = (Get-Sha256 (Join-Path $repoRoot "DualRoleAdventure.dll"))
        archive     = $archivePath
    }
    # ConvertTo-Json 用 Environment.NewLine => Windows 上带 CRLF，而 .gitattributes 要求 json 是 LF（S8 会判 FAIL）
    $json = (($baseline | ConvertTo-Json -Depth 6) -replace "`r`n", "`n")
    [System.IO.File]::WriteAllText($baselinePath, $json + "`n", (New-Object System.Text.UTF8Encoding($false)))
    Write-Ok "已写入基线: $baselinePath"
    Write-Host ""
    Write-Host "游戏：$($game.version)（commit $($game.commit) / $($game.date)）"
    Write-Host "  sts2.dll  sha256=$($game.sts2_dll_sha256)  size=$($game.sts2_dll_size)  mtime=$($game.sts2_dll_mtime)"
    Write-Host "  Harmony=$($game.harmony)  GodotSharp=$($game.godotsharp)"
    Write-Host "仓库：marker=$($repo.marker) version=$($repo.version) HEAD=$($repo.head)"
    Write-Host "      补丁类=$($repo.patch_classes) 目标行=$($repo.patch_rows) 语义标识=$($repo.semantic_descriptors)"
    Write-Host ""
    Write-Warn2 "记得把 $BaselineRel 一起提交（它是更新后对账的唯一参照）。"
    Write-Warn2 "更新前也建议确认 git status 干净、记下当前 master 的 sha 作为回滚点。"
    exit 0
}

# ---------------------------------------------------------------- -Verify
if (-not (Test-Path -LiteralPath $baselinePath)) {
    Write-Err "缺少基线 $BaselineRel —— 说明**更新前没跑 -Snapshot**。现在只能拿游戏现值当基线（差异不可归因，慎用）。"
}
$baseline = Get-Content -LiteralPath $baselinePath -Raw -Encoding UTF8 | ConvertFrom-Json

Write-Step "读取游戏现值 ..."
$game = Get-GameInfo
$repo = Get-RepoInfo

$rows = @()
function Add-Row {
    param([string]$Item, [string]$Base, [string]$Now, [string]$Verdict)
    $script:rows += [pscustomobject]@{ 项目 = $Item; 基线 = $Base; 现在 = $Now; 结论 = $Verdict }
}

$versionChanged = ($baseline.game.version -ne $game.version)
$dllChanged = ($baseline.game.sts2_dll_sha256 -ne $game.sts2_dll_sha256)
Add-Row "游戏版本" $baseline.game.version $game.version ($(if ($versionChanged) { "已更新" } else { "未变" }))
Add-Row "sts2.dll sha" (Short($baseline.game.sts2_dll_sha256)) (Short($game.sts2_dll_sha256)) ($(if ($dllChanged) { "已替换" } else { "未变" }))
Add-Row "Harmony" $baseline.game.harmony $game.harmony ($(if ($baseline.game.harmony -ne $game.harmony) { "版本变了 -> 复核 Harmony 兼容" } else { "未变" }))
Add-Row "GodotSharp" $baseline.game.godotsharp $game.godotsharp ($(if ($baseline.game.godotsharp -ne $game.godotsharp) { "版本变了 -> 复核 GodotSharp/API" } else { "未变" }))

# 反编译源是否比 dll 新（旧源会让字符串核对给出假 STALE / 假 verified）
$srcProbe = Join-Path (Split-Path $repoRoot -Parent) "sts2src\src\Core\Combat\CombatManager.cs"
$srcStale = $false
if (Test-Path -LiteralPath $srcProbe) {
    $srcTime = (Get-Item -LiteralPath $srcProbe).LastWriteTime
    $dllTime = [datetime]::ParseExact($game.sts2_dll_mtime, "yyyy-MM-dd HH:mm:ss", $null)
    $srcStale = ($srcTime -lt $dllTime)
    Add-Row "反编译源 freshness" "dll mtime $($game.sts2_dll_mtime)" "src mtime $($srcTime.ToString('yyyy-MM-dd HH:mm:ss'))" ($(if ($srcStale) { "源比 dll 旧 -> 先跑 regenerate_src.ps1，字符串核对仅供参考" } else { "OK" }))
} else {
    Write-Warn2 "未找到 $srcProbe —— 反编译源目录可能不在 pain\sts2src\src，字符串核对会退化为无源模式。"
}

# 1) 补丁目标 + 程序集兼容（读 sts2.dll 元数据，**不依赖反编译源** -> 更新后立刻有效）
if ($SkipTests) {
    Add-Row "补丁目标/程序集兼容单测" "-" "-" "已跳过（-SkipTests）"
} else {
    Write-Step "跑单测（ExpectedPatchTargets / AssemblyCompatibility 会直接读 sts2.dll 元数据）..."
    $testOut = & dotnet test (Join-Path $repoRoot "tests\LocalMultiControl.Tests\LocalMultiControl.Tests.csproj") `
        -c Release -p:RequireGameInstall=true 2>&1
    $testOk = ($LASTEXITCODE -eq 0)
    $failedNames = ($testOut | Select-String -Pattern '^\s*(失败|Failed)\s+(\S+)' | ForEach-Object { $_.Line.Trim() } | Select-Object -First 12)
    Add-Row "单测（1416 条，含目标存在性）" "全绿" ($(if ($testOk) { "全绿" } else { "有失败" })) ($(if ($testOk) { "PASS" } else { "FAIL -> 看下方明细" }))
    if (-not $testOk) {
        $testOut | Select-Object -Last 25 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkYellow }
        if ($failedNames) { Write-Host "    失败用例（前 12）：" ; $failedNames | ForEach-Object { Write-Host "      $_" } }
    }
}

# 2) 字符串 / 反射目标核对（依赖反编译源）
Write-Step "核对字符串 / 反射目标 ..."
$stringOut = & python (Join-Path $repoRoot "Scripts\Tools\check_string_targets.py") --repo $repoRoot --json 2>$null
$stringOk = ($LASTEXITCODE -eq 0)
if ($stringOut) {
    $sp = ($stringOut -join "`n") | ConvertFrom-Json
    $staleRows = @($sp.rows | Where-Object { $_.status -eq "STALE-TYPE" -or $_.status -eq "STALE-METHOD" })
    $verdict = if ($staleRows.Count -eq 0) { "无 STALE" } else { "STALE $($staleRows.Count) 条 -> 逐个改新名/加回退" }
    if ($srcStale) { $verdict += "（注意：源是旧版，可能假阳性）" }
    Add-Row "字符串/反射目标" "STALE=0" "总数=$(@($sp.rows).Count) verified=$($sp.counts.verified) STALE=$($staleRows.Count)" $verdict
    if ($staleRows.Count -gt 0) {
        Write-Host "    STALE 明细（前 15）：" -ForegroundColor DarkYellow
        $staleRows | Select-Object -First 15 | ForEach-Object {
            Write-Host ("      {0}:{1} {2}.{3} [{4}]" -f $_.file, $_.lineno, $_.type, $_.member, $_.status)
        }
    }
} else {
    Add-Row "字符串/反射目标" "STALE=0" "工具无输出" "FAIL -> 手工跑 check_string_targets.py 看原因"
}

# 3) 离线静态层（S7 会显示「补丁/字符串目标集合」相对仓库基线的增减）
Write-Step "离线静态层（S7 目标基线增减）..."
$staticOut = & python (Join-Path $repoRoot "Scripts\Tools\static_checks.py") --repo $repoRoot --json 2>$null
if ($staticOut) {
    $st = ($staticOut -join "`n") | ConvertFrom-Json
    $s7 = $st.checks | Where-Object { $_.id -eq "S7" }
    $s4 = $st.checks | Where-Object { $_.id -eq "S4" }
    Add-Row "静态层（fail/warn）" "0/0" "$($st.fail)/$($st.warn)" ($(if ($st.fail -eq 0) { "PASS" } else { "FAIL（S1~S10 明细见 static_checks 输出）" }))
    if ($s4 -and $s4.extra) {
        Add-Row "补丁类 / 目标行" "$($baseline.repo.patch_classes) / $($baseline.repo.patch_rows)" `
            "$($s4.extra.patch_classes) / $($s4.extra.target_rows)" `
            ($(if ($s4.level -eq "PASS") { "OK" } else { "FAIL -> 读 static_checks S4 明细" }))
    }
    if ($s7 -and $s7.extra) {
        # ⚠ 别写 @($x).Count —— JSON 里 0 是标量，@(0).Count == 1（PowerShell 经典假数）
        $added = [int]$s7.extra.added; $removed = [int]$s7.extra.removed
        Add-Row "目标基线 S7（新增/消失）" "一致" "新增 $added / 消失 $removed" `
            ($(if ($s7.level -eq "PASS") { "OK" } else { "变化 -> 人审（更新导致 or 我们刚改的）后 --update-baseline" }))
    }
}

Write-Host ""
Write-Host "=== 更新对账 ===" -ForegroundColor Green
# 逐行打印（不用 Format-Table：`Format-*` 的格式化对象在管道里/被 Select-Object 截断时会抛
# "FormatEntryData 无效或顺序不正确"，而这个脚本经常被 `| Select-Object -Last N` 调用）
foreach ($r in $rows) {
    $color = "Gray"
    if ($r.结论 -match 'FAIL|STALE [1-9]|已更新|已替换|旧|变化') { $color = "Red" }
    elseif ($r.结论 -match 'OK|PASS|未变|无 STALE') { $color = "Green" }
    Write-Host ("  - {0}：{1}" -f $r.项目, $r.结论) -ForegroundColor $color
    Write-Host ("      基线 = {0}" -f $r.基线)
    Write-Host ("      现在 = {0}" -f $r.现在)
}
Write-Host ""
if (-not $versionChanged -and -not $dllChanged) {
    Write-Warn2 "游戏看起来**还没更新**（版本与 dll 哈希都和基线一致）—— 那就等更新后再跑一次。"
} else {
    Write-Ok "游戏已换版本。下一步（顺序别跳）："
    Write-Host "  1. .\Scripts\Tools\regenerate_src.ps1          # 重生成反编译源（几分钟）"
    Write-Host "  2. .\Scripts\Tools\game_update_prep.ps1 -Verify # 再跑一次，字符串核对才准"
    Write-Host "  3. dotnet build LocalMultiControl.csproj -c Release   # 编译错误 = 成员改名/被删"
    Write-Host "  4. 修完再 .\Scripts\Tools\preflight.ps1 -Deploy"
    Write-Host "  5. 实机抽查（references/实机回归清单.md 的通用期望 0 清单）"
}
exit 0
