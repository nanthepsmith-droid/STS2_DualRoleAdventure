# release_build.ps1 - 任务 2.4 发布自动化：一条命令产出发布包
#
# 对应《mod系统维护性改进实施方案》任务 2.4：
#   消除「版本号三处同步 / 构建门禁 / 手工打包 / SHA256」的手工步骤。
#
# 用法:
#   .\Scripts\Tools\release_build.ps1 -Version 1.39.0
#   .\Scripts\Tools\release_build.ps1 -Version 1.39.0 -UpdateMarker   # 自动更新 Entry.cs 的 BuildMarker 并重新构建
#   .\Scripts\Tools\release_build.ps1 -Version 1.39.0 -DryRun          # 只打印计划，不改任何文件、不构建
#   .\Scripts\Tools\release_build.ps1 -Version 1.41.0 -PublishGitHub -PushGit -ReleaseNotes "..."
#                                                                       # 额外提交版本改动 + 打 tag + 推 origin + 发 GitHub Release
#
# 行为:
#   1. 校验 semver（x.y.z）
#   2. 三处版本同步（根 / workshop\content / mod_manifest 的 json；
#      UTF-8 带 BOM 正则替换 version 字段，字节保真——json 已是干净的双语 UTF-8（可正常
#      ConvertFrom-Json 解析），但仍是正则替换以保持字段排版与 BOM 稳定）
#   3. 生成 marker 建议串（Revival vX.Y.Z (game vX.Y.Z, marker=YYYY-MM-DD-rN)），
#      可从 Entry.cs 当前 marker 自动取下一 rN
#   4. dotnet build -c Release -warnaserror（0 警告 0 错误门禁）
#   5. 拷贝 dll + json 到 workshop\content\
#   5.5 生成 build-info.json（源码 commit + 依赖锁定；打进 zip，并在 release\ 留同名副本）
#   6. 打 zip 到 release\DualRoleAdventure-v{major}.{minor}.zip
#   7. 打印 SHA256（源 dll / zip / zip 内 dll）供发布时核对
#   8. -PublishGitHub：提交版本改动 → 打 tag `v{major}.{minor}` →（-PushGit 时）推 origin
#      → gh release create/upload（原 BuildRelease.ps1 已并入本脚本，2026-09-16；AGENTS.md §7 第 5 步）

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,               # semver x.y.z，如 1.39.0
    [string]$MarkerSuffix = "",     # 可选 rN；缺省自动从 Entry.cs 当前 marker rN + 1
    [switch]$UpdateMarker,          # 自动把 Entry.cs 的 BuildMarker 更新为新 marker 串
    [switch]$DryRun,                # 只打印计划，不改文件、不构建
    [switch]$PublishGitHub,         # 提交版本改动 + 打 tag + gh release（原 BuildRelease.ps1 的能力，2026-09-16 并入）
    [switch]$PushGit,               # 仅与 -PublishGitHub 同用：git push origin master --follow-tags
    [string]$ReleaseNotes = "",     # GitHub Release 正文；缺省 "Automated release <tag>"
    [string]$GameDir = "D:\SteamLibrary\steamapps\common\Slay the Spire 2"
)

$ErrorActionPreference = "Stop"
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

function Write-Step { param([string]$msg) Write-Host "[*] $msg" -ForegroundColor Cyan }
function Write-Ok   { param([string]$msg) Write-Host "[OK] $msg" -ForegroundColor Green }
function Write-Err  { param([string]$msg) Write-Host "[X] $msg" -ForegroundColor Red; exit 1 }

# ---------------------------------------------------------------- 0. 路径
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$entryPath   = Join-Path $projectRoot "Scripts\Entry.cs"
$dllPath     = Join-Path $projectRoot "DualRoleAdventure.dll"
$rootJson    = Join-Path $projectRoot "DualRoleAdventure.json"
$wsJson      = Join-Path $projectRoot "workshop\content\DualRoleAdventure.json"
$manifest    = Join-Path $projectRoot "mod_manifest.json"
$jsonFiles   = @($rootJson, $wsJson, $manifest)
$releaseRoot = Join-Path $projectRoot "release"

# ---------------------------------------------------------------- 1. semver 校验
if ($Version -notmatch "^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$") {
    Write-Err "版本号 '$Version' 不是合法 semver（期望 x.y.z，如 1.39.0）。"
}
$verMajor = [int]$Matches[1]
$verMinor = [int]$Matches[2]

# ---------------------------------------------------------------- 2. 三处版本同步（UTF-8 带 BOM，正则替换保真）
Write-Step "同步版本号到 3 处 json: $Version"
foreach ($json in $jsonFiles) {
    if (-not (Test-Path -LiteralPath $json)) { Write-Err "缺少 json: $json" }
    if (-not $DryRun) {
        $content = [System.IO.File]::ReadAllText($json, [System.Text.Encoding]::UTF8)
        if ($content -notmatch '"version"\s*:\s*"[^"]+"') {
            Write-Err "找不到 version 字段: $json"
        }
        $newContent = [System.Text.RegularExpressions.Regex]::Replace(
            $content, '"version"\s*:\s*"[^"]+"', ('"version":  "' + $Version + '"'))
        [System.IO.File]::WriteAllText($json, $newContent, (New-Object System.Text.UTF8Encoding($true)))
        Write-Ok "  $([System.IO.Path]::GetFileName($json)) version -> $Version"
    } else {
        Write-Host "  [DRY] 同步 $([System.IO.Path]::GetFileName($json)) -> $Version"
    }
}

# ---------------------------------------------------------------- 3. marker 建议串
$releaseInfo = Join-Path $GameDir "release_info.json"
$gameVersion = "v0.111.0"   # 兜底
if (Test-Path -LiteralPath $releaseInfo) {
    try {
        $ri = Get-Content -LiteralPath $releaseInfo -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($ri.version) { $gameVersion = "v" + $ri.version.ToString().TrimStart("v") }
    } catch {
        Write-Host "  (读取 release_info.json 失败，marker 串使用默认 game $gameVersion)"
    }
}

$markerSuffix = ""
if ($MarkerSuffix) {
    if ($MarkerSuffix -notmatch "^r\d+$") { Write-Err "MarkerSuffix 应为 rN 格式，如 r47。" }
    $markerSuffix = $MarkerSuffix
} elseif (Test-Path -LiteralPath $entryPath) {
    $entryText = [System.IO.File]::ReadAllText($entryPath, [System.Text.Encoding]::UTF8)
    $m = [regex]::Match($entryText, "marker=\d{4}-\d{2}-\d{2}-r(\d+)")
    if ($m.Success) { $markerSuffix = "r" + ([int]$m.Groups[1].Value + 1) }
}
if (-not $markerSuffix) { $markerSuffix = "r1" }

$today = Get-Date -Format "yyyy-MM-dd"
$markerSuggestion = "Revival v$Version (game $gameVersion, marker=$today-$markerSuffix)"
Write-Step "marker 建议串: $markerSuggestion"
Write-Host "  (更新到 Scripts\Entry.cs 的 BuildMarker 常量；用 -UpdateMarker 可自动更新并重新构建)"

if ($UpdateMarker) {
    if (-not (Test-Path -LiteralPath $entryPath)) { Write-Err "缺少 Entry.cs: $entryPath" }
    if (-not $DryRun) {
        $entryText = [System.IO.File]::ReadAllText($entryPath, [System.Text.Encoding]::UTF8)
        if ($entryText -notmatch 'BuildMarker = "Revival v[^"]+";') {
            Write-Err "Entry.cs 中未找到 BuildMarker 常量（格式变化需人工处理）。"
        }
        $newEntry = [System.Text.RegularExpressions.Regex]::Replace(
            $entryText, 'BuildMarker = "Revival v[^"]+";',
            ('BuildMarker = "' + $markerSuggestion + '";'))
        [System.IO.File]::WriteAllText($entryPath, $newEntry, (New-Object System.Text.UTF8Encoding($false)))
        Write-Ok "  Entry.cs BuildMarker -> $markerSuggestion"
    } else {
        Write-Host "  [DRY] 更新 Entry.cs BuildMarker -> $markerSuggestion"
    }
}

# ---------------------------------------------------------------- 4. 构建门禁（0 警告 0 错误）
Write-Step "dotnet build -c Release -warnaserror ..."
if (-not $DryRun) {
    Push-Location $projectRoot
    try {
        dotnet build "LocalMultiControl.csproj" -c Release -warnaserror | Out-Host
        if ($LASTEXITCODE -ne 0) {
            Write-Err "构建失败（exit=$LASTEXITCODE），发布门禁要求 0 警告 0 错误。"
        }
        Write-Ok "构建通过：0 警告 0 错误"
    } finally {
        Pop-Location
    }
}

# ---------------------------------------------------------------- 4.5 构建元数据：源码 commit + 依赖锁定
# 产出 build-info.json（发布包内 + release\ 同名副本），回答三个问题：
#   ① 这一版从哪个 commit 构建的？② 用哪套工具链？③ 对着哪几个游戏程序集（含 SHA256）构建？
# 做法参照 CouchCoop 的 build-info.txt（2026-09-19 调研）。我们的"依赖锁定"重点是**游戏侧程序集**——
# Harmony mod 的 ABI 兼容性完全取决于 sts2.dll / 0Harmony.dll 这些文件的版本与哈希。
Write-Step "生成构建元数据 build-info.json（源码 commit + 依赖锁定）"

function Get-Sha256OrEmpty {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) { return "" }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLower()
}

# 原生命令包装：本脚本 $ErrorActionPreference='Stop'，而工具往 stderr 写提示时会抛
# NativeCommandError 打断脚本（见 tools\powershell-pitfalls.md）。这里临时降级，并把
# "非 0 退出"一律当作"取不到值"（build-info 是元数据，取不到不该让发布失败）。
function Invoke-TextCommand {
    param([string]$Exe, [string[]]$CmdArgs)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $out = & $Exe @CmdArgs 2>$null
        if ($LASTEXITCODE -ne 0) { return $null }
        $text = ($out | Out-String).Trim()
        if ([string]::IsNullOrWhiteSpace($text)) { return $null }
        return $text
    } catch {
        return $null
    } finally {
        $ErrorActionPreference = $prev
    }
}

$zipTag = "v{0}.{1}" -f $verMajor, $verMinor
$builtUtc = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

$gitCommit = Invoke-TextCommand "git" @("-C", $projectRoot, "rev-parse", "--short", "HEAD")
$gitCommitFull = Invoke-TextCommand "git" @("-C", $projectRoot, "rev-parse", "HEAD")
if (-not $gitCommit) { $gitCommit = "no-git" }
if (-not $gitCommitFull) { $gitCommitFull = "no-git" }
$gitBranch = Invoke-TextCommand "git" @("-C", $projectRoot, "rev-parse", "--abbrev-ref", "HEAD")
if (-not $gitBranch) { $gitBranch = "no-git" }

# 工作区状态：故意不用 `git status --porcelain` —— porcelain 的 " M path" 首行前导空格是**有意义**的，
# 而 Invoke-TextCommand 里的 .Trim() 会把它吃掉，导致首行路径少一个字符（实测踩中 "ualRoleAdventure.json"）。
# 改用两条输出干净的只读命令（改动的跟踪文件 + 未跟踪文件），再合并去重。
$dirtyPaths = @()
foreach ($chunk in @(
        (Invoke-TextCommand "git" @("-C", $projectRoot, "diff", "--name-only", "HEAD")),
        (Invoke-TextCommand "git" @("-C", $projectRoot, "ls-files", "--others", "--exclude-standard")))) {
    if (-not $chunk) { continue }
    foreach ($line in @($chunk -split "`r?`n")) {
        $p = ($line.Trim()) -replace '\\', '/'
        if (-not [string]::IsNullOrWhiteSpace($p) -and $dirtyPaths -notcontains $p) { $dirtyPaths += $p }
    }
}
$dirtyPaths = @($dirtyPaths | Sort-Object)
# ⚠ 本脚本第 2 步已经改过 3 处版本 json ⇒ 发布时整体 dirty=true 属**预期**；
#    dirtyFilesExcludingVersionJsons 才是"除版本号外还改了别的吗"的真实信号
#    （**直接列路径**而不是只给计数，事后能一眼看出是哪几个文件）。
$versionJsonRel = @("DualRoleAdventure.json", "mod_manifest.json", "workshop/content/DualRoleAdventure.json")
$nonVersionDirty = @($dirtyPaths | Where-Object { $versionJsonRel -notcontains $_ })
$sourceDirty = if ($dirtyPaths.Count -gt 0) { "dirty" } else { "clean" }

$dotnetSdk = Invoke-TextCommand "dotnet" @("--version")
if (-not $dotnetSdk) { $dotnetSdk = "unknown" }
$csprojText = ""
try { $csprojText = [System.IO.File]::ReadAllText((Join-Path $projectRoot "LocalMultiControl.csproj"), [System.Text.Encoding]::UTF8) } catch { }
$godotSdk = "unknown"
$targetFramework = "unknown"
$sdkMatch = [regex]::Match($csprojText, 'Sdk="Godot\.NET\.Sdk/([^"]+)"')
if ($sdkMatch.Success) { $godotSdk = $sdkMatch.Groups[1].Value }
$tfMatch = [regex]::Match($csprojText, '<TargetFramework>([^<]+)</TargetFramework>')
if ($tfMatch.Success) { $targetFramework = $tfMatch.Groups[1].Value }

# 依赖锁定（主）：真正的运行期依赖 = 游戏安装里的这几个程序集（ABI 兼容性就靠它们）。
$gameDataDir = Join-Path $GameDir "data_sts2_windows_x86_64"
$lockedAssemblies = @()
foreach ($asmName in @("sts2.dll", "0Harmony.dll", "Steamworks.NET.dll", "GodotSharp.dll")) {
    $asmPath = Join-Path $gameDataDir $asmName
    if (-not (Test-Path -LiteralPath $asmPath)) { continue }
    $asmItem = Get-Item -LiteralPath $asmPath
    $lockedAssemblies += [ordered]@{
        name           = $asmName
        fileVersion    = "" + $asmItem.VersionInfo.FileVersion
        productVersion = "" + $asmItem.VersionInfo.ProductVersion
        sizeBytes      = $asmItem.Length
        sha256         = (Get-FileHash -LiteralPath $asmPath -Algorithm SHA256).Hash.ToLower()
    }
}

# 依赖锁定（次）：NuGet 解析结果。本工程只有 SDK + HintPath 引用（无 PackageReference），
# 所以这里通常是空的；有 assets 文件就照实记录，没有不算失败。
$nugetPackages = @()
$assetsPath = Join-Path $projectRoot "obj\project.assets.json"
if (Test-Path -LiteralPath $assetsPath) {
    try {
        $assets = Get-Content -LiteralPath $assetsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($lib in $assets.libraries.PSObject.Properties) {
            $nugetPackages += [ordered]@{ id = $lib.Name; sha512 = "" + $lib.Value.sha512 }
        }
    } catch { }
}

$artifacts = [ordered]@{}
foreach ($artifact in @(
        @{ key = "dll"; path = $dllPath; name = "DualRoleAdventure.dll" },
        @{ key = "json"; path = $rootJson; name = "DualRoleAdventure.json" })) {
    $sizeBytes = 0
    if (Test-Path -LiteralPath $artifact.path) { $sizeBytes = (Get-Item -LiteralPath $artifact.path).Length }
    $artifacts[$artifact.key] = [ordered]@{
        name      = $artifact.name
        sha256    = (Get-Sha256OrEmpty $artifact.path)
        sizeBytes = $sizeBytes
    }
}
$dllSha = "" + $artifacts["dll"].sha256

$buildInfo = [ordered]@{
    schemaVersion            = "dualroleadventure-release-build-info/v1"
    modId                    = "DualRoleAdventurefixed"
    version                  = $Version
    tag                      = $zipTag
    marker                   = $markerSuggestion
    builtUtc                 = $builtUtc
    source                   = [ordered]@{
        commit                          = $gitCommitFull
        commitShort                     = $gitCommit
        branch                          = $gitBranch
        dirty                           = $sourceDirty
        dirtyFileCount                  = $dirtyPaths.Count
        dirtyFiles                      = $dirtyPaths
        dirtyFilesExcludingVersionJsons = $nonVersionDirty
    }
    dependencies             = [ordered]@{
        toolchain      = [ordered]@{ dotnetSdk = $dotnetSdk; godotSdk = $godotSdk; targetFramework = $targetFramework }
        gameAssemblies = $lockedAssemblies
        nugetPackages  = $nugetPackages
    }
    game                     = [ordered]@{ version = $gameVersion; installDir = $GameDir; dataDir = $gameDataDir }
    artifacts                = $artifacts
}
$buildInfoJson = $buildInfo | ConvertTo-Json -Depth 8

if ($DryRun) {
    Write-Host "  [DRY] build-info.json 内容预览（不写盘）:"
    Write-Host $buildInfoJson
} else {
    Write-Ok "  build-info 就绪: commit=$gitCommit/$sourceDirty, 游戏程序集锁定 $($lockedAssemblies.Count) 个, 非版本号改动 $($nonVersionDirty.Count) 处"
}

# ---------------------------------------------------------------- 5. 拷贝到 workshop\content\
Write-Step "拷贝 dll + json 到 workshop\content\ ..."
if (-not $DryRun) {
    Copy-Item -LiteralPath $dllPath -Destination (Join-Path (Join-Path $projectRoot "workshop\content") "DualRoleAdventure.dll") -Force
    Copy-Item -LiteralPath $rootJson -Destination $wsJson -Force
    Write-Ok "  workshop\content\DualRoleAdventure.dll + DualRoleAdventure.json 已更新"
} else {
    Write-Host "  [DRY] 拷贝 dll + json -> workshop\content\"
}

# ---------------------------------------------------------------- 6. 打 zip 到 release\
$releaseName = "DualRoleAdventure-$zipTag"
$zipPath = Join-Path $releaseRoot "$releaseName.zip"
$buildInfoCopy = Join-Path $releaseRoot "$releaseName-build-info.json"
Write-Step "打包: $zipPath"
if (-not $DryRun) {
    if (-not (Test-Path -LiteralPath $releaseRoot)) { New-Item -ItemType Directory -Path $releaseRoot | Out-Null }
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    $tempDir = Join-Path $releaseRoot "$releaseName.tmp"
    if (Test-Path -LiteralPath $tempDir) { Remove-Item -LiteralPath $tempDir -Recurse -Force }
    New-Item -ItemType Directory -Path $tempDir | Out-Null
    Copy-Item -LiteralPath $dllPath -Destination (Join-Path $tempDir "DualRoleAdventure.dll") -Force
    Copy-Item -LiteralPath $rootJson -Destination (Join-Path $tempDir "DualRoleAdventure.json") -Force
    # build-info.json 打进发布包（UTF-8 无 BOM），并在 release\ 留一份同名副本便于不开包核对/比对
    [System.IO.File]::WriteAllText((Join-Path $tempDir "build-info.json"), $buildInfoJson, (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText($buildInfoCopy, $buildInfoJson, (New-Object System.Text.UTF8Encoding($false)))
    Compress-Archive -Path (Join-Path $tempDir "*") -DestinationPath $zipPath -Force
    Remove-Item -LiteralPath $tempDir -Recurse -Force
    Write-Ok "  发布包已生成: $zipPath（含 build-info.json）"
} else {
    Write-Host "  [DRY] 打包 -> $zipPath（含 build-info.json；release\ 另留 $([System.IO.Path]::GetFileName($buildInfoCopy)) 副本）"
}

# ---------------------------------------------------------------- 7. SHA256（dll / zip / zip 内 dll）
Write-Step "SHA256 指纹（发布时核对）"
if (-not $DryRun) {
    # $dllSha 已在 4.5 步算好（build-info.json 里是同一份）
    $zipSha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLower()

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zipEntrySha = ""
    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq "DualRoleAdventure.dll" } | Select-Object -First 1
        if ($entry) {
            $sha256 = [System.Security.Cryptography.SHA256]::Create()
            $stream = $entry.Open()
            try {
                $zipEntrySha = ([System.BitConverter]::ToString($sha256.ComputeHash($stream))).Replace("-", "").ToLower()
            } finally {
                $stream.Dispose()
                $sha256.Dispose()
            }
        }
    } finally {
        $zip.Dispose()
    }

    Write-Host "  源 dll     : $dllSha"
    Write-Host "  zip        : $zipSha"
    Write-Host "  zip 内 dll : $zipEntrySha"
    if ($zipEntrySha -and $dllSha -eq $zipEntrySha) {
        Write-Ok "zip 内 dll 与源 dll 一致"
    } else {
        Write-Err "zip 内 dll 与源 dll 不一致，发布包异常！"
    }
} else {
    Write-Host "  [DRY] 打印 dll / zip / zip 内 dll 的 SHA256"
}

# ---------------------------------------------------------------- 8. GitHub 发布（可选，原 BuildRelease.ps1 能力）
if ($PublishGitHub) {
    Write-Step "GitHub 发布：提交版本改动 → tag $zipTag → gh release"
    $pushNote = if ($PushGit) { " + git push origin master --follow-tags" } else { "" }
    if ($DryRun) {
        Write-Host "  [DRY] git add 三处 json → git commit '发布 $zipTag' → git tag $zipTag$pushNote"
        Write-Host "  [DRY] gh release create/upload $zipTag $zipPath"
    } else {
        git --version 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Err "PublishGitHub 需要 git。" }
        gh --version 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Err "PublishGitHub 需要 gh CLI（GitHub CLI）。" }

        Push-Location $projectRoot
        try {
            # 与本脚本第 2 步同步过的三处 json 一起提交（原 BuildRelease.ps1 只 add 根 json）。
            # ⚠ 中文提交信息**不能走命令行 argv**（本机 GBK 控制台会损坏，见 tools\powershell-pitfalls.md）：
            #    写 UTF-8 无 BOM 临时文件（放 .git 内，不入提交）+ `git commit -F`，完成后删除。
            git add "DualRoleAdventure.json" "mod_manifest.json" "workshop/content/DualRoleAdventure.json" | Out-Null
            $msgFile = Join-Path $projectRoot (".git\release_commit_msg.{0}.tmp" -f (Get-Date -Format "yyyyMMddHHmmss"))
            [System.IO.File]::WriteAllText($msgFile, "发布 $zipTag", (New-Object System.Text.UTF8Encoding($false)))
            try {
                git commit -F $msgFile 2>$null | Out-Null
                if ($LASTEXITCODE -ne 0) {
                    Write-Host "  （没有可提交的版本改动，跳过 commit）" -ForegroundColor Yellow
                }
            } finally {
                Remove-Item -LiteralPath $msgFile -Force -ErrorAction SilentlyContinue
            }

            $existingTag = (git tag --list $zipTag)
            if ([string]::IsNullOrWhiteSpace($existingTag)) {
                git tag -a $zipTag -m "Release $zipTag"
                Write-Ok "  已打 tag: $zipTag"
            } else {
                Write-Host "  tag $zipTag 已存在，跳过（不覆盖历史 tag）" -ForegroundColor Yellow
            }

            if ($PushGit) {
                git push origin master --follow-tags
                if ($LASTEXITCODE -ne 0) { Write-Err "git push 失败（exit=$LASTEXITCODE）。" }
                Write-Ok "  已推送 origin master + tags"
            }
        } finally {
            Pop-Location
        }

        $releaseBody = $ReleaseNotes
        if ([string]::IsNullOrWhiteSpace($releaseBody)) { $releaseBody = "Automated release $zipTag" }

        cmd /c "gh release view $zipTag >nul 2>nul"
        if ($LASTEXITCODE -eq 0) {
            gh release upload $zipTag $zipPath --clobber
            Write-Ok "  GitHub Release $zipTag 的资产已更新"
        } else {
            gh release create $zipTag $zipPath --title $zipTag --notes $releaseBody
            Write-Ok "  已创建 GitHub Release $zipTag"
        }
    }
}

Write-Host ""
Write-Ok "全部完成。发布包: $zipPath"
Write-Host "接下来（人工）：1) 检查/补充 CHANGELOG；2) 需要时补 PLAYER_GUIDE；3) 未加 -PublishGitHub 时自行提交版本改动与打 tag。"
