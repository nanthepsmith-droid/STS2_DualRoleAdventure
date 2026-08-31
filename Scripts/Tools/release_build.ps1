# release_build.ps1 - 任务 2.4 发布自动化：一条命令产出发布包
#
# 对应《mod系统维护性改进实施方案》任务 2.4：
#   消除「版本号三处同步 / 构建门禁 / 手工打包 / SHA256」的手工步骤。
#
# 用法:
#   .\Scripts\Tools\release_build.ps1 -Version 1.39.0
#   .\Scripts\Tools\release_build.ps1 -Version 1.39.0 -UpdateMarker   # 自动更新 Entry.cs 的 BuildMarker 并重新构建
#   .\Scripts\Tools\release_build.ps1 -Version 1.39.0 -DryRun          # 只打印计划，不改任何文件、不构建
#
# 行为:
#   1. 校验 semver（x.y.z）
#   2. 三处版本同步（根 / workshop\content / mod_manifest 的 json；
#      UTF-8 带 BOM 正则替换 version 字段，字节保真——json 中文是历史双重编码乱码，
#      不可 ConvertFrom-Json 解析重写，见 BuildRelease.ps1 注释）
#   3. 生成 marker 建议串（Revival vX.Y.Z (game vX.Y.Z, marker=YYYY-MM-DD-rN)），
#      可从 Entry.cs 当前 marker 自动取下一 rN
#   4. dotnet build -c Release -warnaserror（0 警告 0 错误门禁）
#   5. 拷贝 dll + json 到 workshop\content\
#   6. 打 zip 到 release\DualRoleAdventure-v{major}.{minor}.zip
#   7. 打印 SHA256（源 dll / zip / zip 内 dll）供发布时核对

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,               # semver x.y.z，如 1.39.0
    [string]$MarkerSuffix = "",     # 可选 rN；缺省自动从 Entry.cs 当前 marker rN + 1
    [switch]$UpdateMarker,          # 自动把 Entry.cs 的 BuildMarker 更新为新 marker 串
    [switch]$DryRun,                # 只打印计划，不改文件、不构建
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
$zipTag = "v{0}.{1}" -f $verMajor, $verMinor
$releaseName = "DualRoleAdventure-$zipTag"
$zipPath = Join-Path $releaseRoot "$releaseName.zip"
Write-Step "打包: $zipPath"
if (-not $DryRun) {
    if (-not (Test-Path -LiteralPath $releaseRoot)) { New-Item -ItemType Directory -Path $releaseRoot | Out-Null }
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    $tempDir = Join-Path $releaseRoot "$releaseName.tmp"
    if (Test-Path -LiteralPath $tempDir) { Remove-Item -LiteralPath $tempDir -Recurse -Force }
    New-Item -ItemType Directory -Path $tempDir | Out-Null
    Copy-Item -LiteralPath $dllPath -Destination (Join-Path $tempDir "DualRoleAdventure.dll") -Force
    Copy-Item -LiteralPath $rootJson -Destination (Join-Path $tempDir "DualRoleAdventure.json") -Force
    Compress-Archive -Path (Join-Path $tempDir "*") -DestinationPath $zipPath -Force
    Remove-Item -LiteralPath $tempDir -Recurse -Force
    Write-Ok "  发布包已生成: $zipPath"
} else {
    Write-Host "  [DRY] 打包 -> $zipPath"
}

# ---------------------------------------------------------------- 7. SHA256（dll / zip / zip 内 dll）
Write-Step "SHA256 指纹（发布时核对）"
if (-not $DryRun) {
    $dllSha = (Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash.ToLower()
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

Write-Host ""
Write-Ok "全部完成。发布包: $zipPath"
Write-Host "接下来（人工）：1) 检查/补充 CHANGELOG；2) 提交 json 版本与构建产物改动；3) 如发 GitHub Release 可复用 BuildRelease.ps1 -PublishGitHub。"
