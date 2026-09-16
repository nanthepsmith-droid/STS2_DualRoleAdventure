# preflight.ps1 - 本机一键门禁（AGENTS.md §9 全链）→ **单一判定**
#
# 目的：把"全靠手动"变成一条命令，输出 PASS/FAIL 表 + 退出码，而不是一堆日志。
# 分层（默认最轻，逐级加重）：
#
#   默认（静态层，秒级、零副作用）
#     G1 离线静态自检      static_checks.py          产物入库 / ps1 BOM / 元数据 version / 补丁类级 / 源码隔离 / marker
#     G2 字符串/反射目标核对 check_string_targets.py  需反编译源码（..\sts2src\src）；缺则 SKIP，明细写临时文件
#     G3 diff 预审(可选)    ..\tools\diff_lint.py     需 -Lint；只标疑似（其自身文档写明"不进 CI"）
#
#   -Build（会重建仓库根 DLL）
#     G4 全仓库构建 + 主 mod 单测门槛  build_all_mods.ps1 -BuildOnly
#        （内含：构建 0 warn/0 err、源码隔离、dotnet test、clr_compat_check 门禁 4/5）
#
#   -Deploy（含 -Build；要求游戏已关闭）
#     G5 构建 + 部署 + 槽位身份 + 部署字节校验  build_all_mods.ps1（全量模式 = 门禁 1~7/10）
#
#   -WithLogs（实机跑过一轮后再用；需 ..\tools\ 的日志工具）
#     G6 初始化终态        log_parser.py --init-status   → INIT_STATUS=OK（门禁 9）
#     G7 健康度计数        log_scan.py --preset health --strict（期望 0 命中项被命中 → FAIL）
#
# 用法:
#   .\Scripts\Tools\preflight.ps1                    # 只跑静态层
#   .\Scripts\Tools\preflight.ps1 -Lint              # 静态层 + diff 预审
#   .\Scripts\Tools\preflight.ps1 -Build             # 静态层 + 构建/单测/CLR-ABI
#   .\Scripts\Tools\preflight.ps1 -Deploy            # 全量（构建+部署+字节校验）
#   .\Scripts\Tools\preflight.ps1 -Deploy -WithLogs  # 再加运行期日志门禁
#   .\Scripts\Tools\preflight.ps1 -ToolsDir D:\Download\pain\tools
#
# 退出码: 0 = 全过（或仅 SKIP）；1 = 有 FAIL；2 = 用法/环境错误
#
# 注意: 本文件含中文，必须保存为带 UTF-8 BOM 的 UTF-8（PowerShell 5.1 否则按 GBK 解析报语法错）。

param(
    [switch]$Build,                                   # 加构建 / 单测 / CLR-ABI
    [switch]$Deploy,                                  # 加部署 + 字节校验（隐含 -Build）
    [switch]$WithLogs,                                # 加运行期日志门禁
    [switch]$Lint,                                    # 加 diff_lint 预审
    [string]$ToolsDir = "",                           # 仓库外工具目录；缺省 = 仓库上一级的 tools\
    [string]$RepoDir = ""                             # 仓库根；缺省 = 本脚本的上两级
)

$ErrorActionPreference = "Stop"
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
# 本机控制台是 GBK：让 python 工具统一输出 UTF-8（否则中文经管道会乱码）
$env:PYTHONIOENCODING = "utf-8"

if (-not $RepoDir) { $RepoDir = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path }
else { $RepoDir = (Resolve-Path $RepoDir).Path }
if (-not $ToolsDir) { $ToolsDir = Join-Path (Split-Path $RepoDir -Parent) "tools" }

$script:Results = @()
function Add-Result {
    param([string]$Id, [string]$Name, [string]$Status, [string]$Detail = "")
    $script:Results += [pscustomobject]@{ Id = $Id; Name = $Name; Status = $Status; Detail = $Detail }
}

# 中英混排按显示宽度对齐（CJK 记 2 列），否则表格会参差不齐
function Get-DisplayWidth([string]$Text) {
    $w = 0
    foreach ($ch in $Text.ToCharArray()) {
        $c = [int]$ch
        if (($c -ge 0x1100 -and $c -le 0x115F) -or ($c -ge 0x2E80 -and $c -le 0xA4CF) -or
            ($c -ge 0xAC00 -and $c -le 0xD7A3) -or ($c -ge 0xF900 -and $c -le 0xFAFF) -or
            ($c -ge 0xFE30 -and $c -le 0xFE6F) -or ($c -ge 0xFF00 -and $c -le 0xFF60) -or
            ($c -ge 0xFFE0 -and $c -le 0xFFE6)) { $w += 2 } else { $w += 1 }
    }
    return $w
}
function Format-Col([string]$Text, [int]$Width) {
    return $Text + (' ' * [Math]::Max(0, $Width - (Get-DisplayWidth $Text)))
}

function Invoke-Native {
    param(
        [string]$Id,
        [string]$Name,
        [string]$Exe,
        [string[]]$CmdArgs,
        [string]$SkipReason = ""
    )
    if ($SkipReason) {
        Write-Host ("[SKIP] {0}  {1} —— {2}" -f $Id, $Name, $SkipReason) -ForegroundColor DarkGray
        Add-Result $Id $Name "SKIP" $SkipReason
        return
    }
    Write-Host ("[*] {0}  {1}" -f $Id, $Name) -ForegroundColor Cyan
    Write-Host ("      {0} {1}" -f $Exe, ($CmdArgs -join ' ')) -ForegroundColor DarkGray
    # ⚠ 原生程序往 stderr 写提示时，PowerShell 在 ErrorActionPreference=Stop 下会抛 NativeCommandError
    #    把整条 preflight 打断；这里临时降级，并把 ErrorRecord 当纯文本打印（避免整屏红色 "python.exe :"）。
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    Push-Location $RepoDir
    try {
        $raw = & $Exe @CmdArgs 2>&1
        $code = $LASTEXITCODE
        foreach ($line in $raw) {
            if ($line -is [System.Management.Automation.ErrorRecord]) {
                # 原生 stderr 被包成 ErrorRecord：真正的内容在 Exception.Message 里；
                # 只有类型名、没有正文的那种包装记录直接跳过（真正的 stderr 文本会作为独立字符串出现）
                $msg = $line.Exception.Message
                if ([string]::IsNullOrWhiteSpace($msg) -or $msg -eq $line.Exception.GetType().FullName) { continue }
                Write-Host $msg -ForegroundColor DarkGray
            } else {
                Write-Host $line
            }
        }
    } finally {
        Pop-Location
        $ErrorActionPreference = $prevEap
    }
    if ($code -eq 0) {
        Write-Host ("[PASS] {0}" -f $Id) -ForegroundColor Green
        Add-Result $Id $Name "PASS" ""
    } else {
        Write-Host ("[FAIL] {0}（exit={1}）" -f $Id, $code) -ForegroundColor Red
        Add-Result $Id $Name "FAIL" ("exit=" + $code)
    }
}

Write-Host ""
Write-Host ("preflight —— 仓库: {0}" -f $RepoDir) -ForegroundColor White
Write-Host ("            外部工具目录: {0}" -f $ToolsDir) -ForegroundColor DarkGray
Write-Host ("            python: " + (Get-Command python -ErrorAction SilentlyContinue).Source) -ForegroundColor DarkGray
Write-Host ("-" * 100) -ForegroundColor DarkGray

# ---------------------------------------------------------------- G1 离线静态自检
$staticChecks = Join-Path $RepoDir "Scripts\Tools\static_checks.py"
Invoke-Native "G1" "离线静态自检（不需要游戏安装）" "python" @($staticChecks, "--repo", ".") `
    $(if (-not (Test-Path -LiteralPath $staticChecks)) { "缺少 $staticChecks" } else { "" })

# ---------------------------------------------------------------- G2 字符串/反射目标核对
$srcDir = Join-Path (Split-Path $RepoDir -Parent) "sts2src\src"
$stringTargets = Join-Path $RepoDir "Scripts\Tools\check_string_targets.py"
$stringTargetsOut = Join-Path $env:TEMP "preflight-string-targets.md"
Invoke-Native "G2" "字符串/反射目标核对（游戏更新断档检测）" "python" `
    @($stringTargets, "--repo", ".", "--out", $stringTargetsOut) `
    $(if (-not (Test-Path -LiteralPath $stringTargets)) { "缺少 $stringTargets" }
      elseif (-not (Test-Path -LiteralPath $srcDir)) { "缺反编译源码 $srcDir（先跑 regenerate_src.ps1）" }
      else { "" })
if (@($script:Results | Where-Object { $_.Id -eq "G2" -and $_.Status -eq "FAIL" }).Count -gt 0) {
    Write-Host ("      明细: {0}" -f $stringTargetsOut) -ForegroundColor Yellow
}

# ---------------------------------------------------------------- G3 diff 预审（可选）
if ($Lint) {
    $diffLint = Join-Path $ToolsDir "diff_lint.py"
    Invoke-Native "G3" "diff 预审（AGENTS 硬规矩）" "python" @($diffLint) `
        $(if (-not (Test-Path -LiteralPath $diffLint)) { "缺少 $diffLint" } else { "" })
} else {
    Add-Result "G3" "diff 预审（AGENTS 硬规矩）" "SKIP" "未指定 -Lint"
}

# ---------------------------------------------------------------- G4/G5 构建 / 部署
$buildAll = Join-Path $RepoDir "Scripts\Tools\build_all_mods.ps1"
if ($Deploy) {
    Invoke-Native "G5" "全量门禁：构建 + 部署 + 槽位身份 + 字节校验" "powershell" `
        @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $buildAll) `
        $(if (-not (Test-Path -LiteralPath $buildAll)) { "缺少 $buildAll" } else { "" })
    Add-Result "G4" "全仓库构建 + 单测门槛" "PASS" "已包含在 G5 内"
} elseif ($Build) {
    Invoke-Native "G4" "全仓库构建 + 主 mod 单测门槛" "powershell" `
        @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $buildAll, "-BuildOnly") `
        $(if (-not (Test-Path -LiteralPath $buildAll)) { "缺少 $buildAll" } else { "" })
} else {
    Add-Result "G4" "全仓库构建 + 单测门槛" "SKIP" "未指定 -Build / -Deploy"
}

# ---------------------------------------------------------------- G6/G7 运行期日志
if ($WithLogs) {
    $logParser = Join-Path $ToolsDir "log_parser.py"
    Invoke-Native "G6" "初始化终态（INIT_STATUS=OK）" "python" @($logParser, "--init-status") `
        $(if (-not (Test-Path -LiteralPath $logParser)) { "缺少 $logParser" } else { "" })
    $logScan = Join-Path $ToolsDir "log_scan.py"
    Invoke-Native "G7" "健康度计数（preset health --strict）" "python" @($logScan, "--preset", "health", "--strict") `
        $(if (-not (Test-Path -LiteralPath $logScan)) { "缺少 $logScan" } else { "" })
} else {
    Add-Result "G6" "初始化终态（INIT_STATUS=OK）" "SKIP" "未指定 -WithLogs"
    Add-Result "G7" "健康度计数（preset health --strict）" "SKIP" "未指定 -WithLogs"
}

# ---------------------------------------------------------------- 汇总
Write-Host ("-" * 100) -ForegroundColor DarkGray
Write-Host ((Format-Col "ID" 5) + " " + (Format-Col "门禁" 52) + " " + (Format-Col "结果" 6) + " 备注") -ForegroundColor White
foreach ($r in $script:Results) {
    $color = switch ($r.Status) { "PASS" { "Green" } "FAIL" { "Red" } default { "DarkGray" } }
    Write-Host ((Format-Col $r.Id 5) + " " + (Format-Col $r.Name 52) + " " + (Format-Col $r.Status 6) + " " + $r.Detail) -ForegroundColor $color
}
$fails = @($script:Results | Where-Object { $_.Status -eq "FAIL" }).Count
$skips = @($script:Results | Where-Object { $_.Status -eq "SKIP" }).Count
$pass = @($script:Results | Where-Object { $_.Status -eq "PASS" }).Count
Write-Host ""
if ($fails -gt 0) {
    Write-Host ("[X] preflight 失败：{0} FAIL / {1} PASS / {2} SKIP" -f $fails, $pass, $skips) -ForegroundColor Red
    exit 1
}
Write-Host ("[OK] preflight 通过：{0} PASS / {1} SKIP" -f $pass, $skips) -ForegroundColor Green
if ($skips -gt 0) {
    Write-Host "     （SKIP 项按需加 -Build / -Deploy / -WithLogs / -Lint 打开；门禁 1~7/10 需要本机游戏安装）" -ForegroundColor DarkGray
}
exit 0
