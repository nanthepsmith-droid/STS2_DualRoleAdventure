param(
    [string]$Version,
    [switch]$PublishGitHub,
    [switch]$PushGit,
    [string]$ReleaseNotes
)

$ErrorActionPreference = "Stop"

<#
    发布名（zip 名 / git tag）用 v{major}.{minor}（如 v1.38），而 DualRoleAdventure.json 里的
    version 字段必须是 semver x.y.z（如 1.38.0）——游戏对非 semver 会告警，且历史发布包里装的
    一直是 semver。两者分离，不要互相覆盖。

    注意：DualRoleAdventure.json 的中文是历史遗留的双重编码乱码，其中含**未转义的引号**，
    导致该文件用 ConvertFrom-Json 解析会直接抛异常。因此本脚本**不解析、不重写**该 JSON，
    只用正则取 version 值来决定发布名，打包时按字节原样复制，避免破坏既有字节。
#>
function Get-ReleaseNameFromSemver {
    param(
        [string]$CurrentVersion
    )

    if ($CurrentVersion -notmatch "^(\d+)\.(\d+)\.(\d+)$") {
        throw "Invalid current version: '$CurrentVersion'. Expected semver like 1.38.0 in DualRoleAdventure.json."
    }

    return ("v{0}.{1}" -f $Matches[1], $Matches[2])
}

function Get-NextReleaseName {
    param(
        [string]$CurrentVersion
    )

    if ($CurrentVersion -notmatch "^(\d+)\.(\d+)\.(\d+)$") {
        throw "Invalid current version: '$CurrentVersion'. Expected semver like 1.38.0 in DualRoleAdventure.json."
    }

    $major = [int]$Matches[1]
    $minor = [int]$Matches[2] + 1
    return ("v{0}.{1}" -f $major, $minor.ToString("00"))
}

function Get-JsonSemver {
    param(
        [string]$JsonPath
    )

    $raw = Get-Content -LiteralPath $JsonPath -Raw -Encoding UTF8
    if ($raw -match '"version"\s*:\s*"(?<v>[^"]+)"') {
        return $Matches["v"]
    }

    throw "Cannot find a version field in $JsonPath"
}

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$dllPath = Join-Path $projectRoot "DualRoleAdventure.dll"
$jsonPath = Join-Path $projectRoot "DualRoleAdventure.json"
$releaseRoot = Join-Path $projectRoot "release"

if (!(Test-Path -LiteralPath $dllPath)) {
    throw "Missing DLL: $dllPath"
}

if (!(Test-Path -LiteralPath $jsonPath)) {
    throw "Missing JSON: $jsonPath"
}

# 只读取 semver 用于决定发布名；不解析/不重写 JSON（见 Get-ReleaseNameFromSemver 注释）。
$currentSemver = Get-JsonSemver -JsonPath $jsonPath

if ([string]::IsNullOrWhiteSpace($Version)) {
    $targetVersion = Get-NextReleaseName -CurrentVersion $currentSemver
}
else {
    if ($Version -notmatch "^v\d+\.\d+$") {
        throw "Invalid target version: $Version. Expected release-name format: v1.38 (the JSON keeps semver 1.38.0)."
    }

    $targetVersion = $Version
}

# 发布名（v1.38）与 JSON 内 semver（1.38.0）一致性提示：major.minor 应对得上。
$expectedReleaseName = Get-ReleaseNameFromSemver -CurrentVersion $currentSemver
if ($expectedReleaseName -ne $targetVersion) {
    Write-Host "WARNING: DualRoleAdventure.json version is '$currentSemver' (release name '$expectedReleaseName'), but you requested '$targetVersion'." -ForegroundColor Yellow
    Write-Host "         The JSON is shipped as-is; bump it to semver (e.g. $($targetVersion.TrimStart('v')).0) first if this is unintended." -ForegroundColor Yellow
}

if (!(Test-Path -LiteralPath $releaseRoot)) {
    New-Item -ItemType Directory -Path $releaseRoot | Out-Null
}

$releaseName = "DualRoleAdventure-$targetVersion"
$releaseDir = Join-Path $releaseRoot $releaseName
$zipPath = Join-Path $releaseRoot "$releaseName.zip"

if (Test-Path -LiteralPath $releaseDir) {
    Remove-Item -LiteralPath $releaseDir -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Path $releaseDir | Out-Null
Copy-Item -LiteralPath $dllPath -Destination (Join-Path $releaseDir "DualRoleAdventure.dll") -Force
Copy-Item -LiteralPath $jsonPath -Destination (Join-Path $releaseDir "DualRoleAdventure.json") -Force

Compress-Archive -Path (Join-Path $releaseDir "*") -DestinationPath $zipPath -Force

Write-Host "Release folder created: $releaseDir"
Write-Host "Release zip created: $zipPath"
Write-Host "Release name/tag: $targetVersion (DualRoleAdventure.json keeps semver '$currentSemver', shipped as-is)"
Write-Host "Shipped files: DualRoleAdventure.dll, DualRoleAdventure.json"

if ($PublishGitHub) {
    $gitVersion = git --version 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "git is required for PublishGitHub mode."
    }

    $ghVersion = gh --version 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "gh CLI is required for PublishGitHub mode."
    }

    git add "DualRoleAdventure.json" | Out-Null
    git commit -m "发布 $targetVersion" 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Skip commit (maybe no changes to commit)."
    }

    git tag --list $targetVersion | Out-Null
    $existingTag = git tag --list $targetVersion
    if ([string]::IsNullOrWhiteSpace($existingTag)) {
        git tag -a $targetVersion -m "Release $targetVersion"
    }

    if ($PushGit) {
        git push origin master --follow-tags
    }

    $releaseBody = $ReleaseNotes
    if ([string]::IsNullOrWhiteSpace($releaseBody)) {
        $releaseBody = "Automated release $targetVersion"
    }

    cmd /c "gh release view $targetVersion >nul 2>nul"
    if ($LASTEXITCODE -eq 0) {
        gh release upload $targetVersion $zipPath --clobber
        Write-Host "GitHub release asset updated for $targetVersion"
    }
    else {
        gh release create $targetVersion $zipPath --title $targetVersion --notes $releaseBody
        Write-Host "GitHub release created for $targetVersion"
    }
}
