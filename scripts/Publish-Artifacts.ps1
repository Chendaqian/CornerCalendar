#requires -Version 7
<#
.SYNOPSIS
    CornerCalendar 一键制品生成脚本。

.DESCRIPTION
    生成两种制品（均不生成 PDB）：
      1. self-contained  自包含多文件目录（内含 .NET 运行时，客户机器无需安装任何东西，直接可用）
      2. framework       框架依赖多文件（exe + 应用 dll，客户需自行安装 .NET 8 桌面运行时）

    制品输出到 <仓库根>\release\<制品名>\ 目录。

.PARAMETER Configuration
    构建配置，默认 Release。

.PARAMETER Runtime
    目标 RID，默认 win-x64。

.PARAMETER OutputRoot
    制品输出根目录，默认 <仓库根>\release。

.PARAMETER Version
    制品版本号（如 1.2.0）：以 -p:Version 注入二进制并用于制品命名。
    缺省时读取工程文件中的 Version —— 版本号以 csproj 为唯一来源，
    CI 的 tag 发布即走该缺省路径（不再由 tag 名决定版本）。

.EXAMPLE
    pwsh scripts\Publish-Artifacts.ps1

.EXAMPLE
    pwsh scripts\Publish-Artifacts.ps1 -Configuration Debug -OutputRoot D:\out

.EXAMPLE
    pwsh scripts\Publish-Artifacts.ps1 -Version 1.2.0
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputRoot = '',
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'

# ---------- 路径解析 ----------
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Csproj = Join-Path $RepoRoot 'src\CornerCalendar\CornerCalendar.csproj'
if (-not $OutputRoot) { $OutputRoot = Join-Path $RepoRoot 'release' }
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)

if (-not (Test-Path -LiteralPath $Csproj -PathType Leaf)) {
    throw "找不到项目文件：$Csproj"
}

# ---------- 关闭正在运行的程序 ----------
$runningProcesses = @(Get-Process -Name 'CornerCalendar' -ErrorAction SilentlyContinue)
foreach ($process in $runningProcesses) {
    Write-Host "==> 检测到 CornerCalendar 正在运行（PID: $($process.Id)），正在关闭..."
    try {
        Stop-Process -Id $process.Id -Force -ErrorAction Stop
        Wait-Process -Id $process.Id -Timeout 10 -ErrorAction SilentlyContinue
    }
    catch {
        throw "无法关闭 CornerCalendar 进程（PID: $($process.Id)）：$($_.Exception.Message)"
    }
}

# ---------- 版本号（用于制品命名与文件版本）：优先命令行 -Version，其次读取 csproj ----------
if (-not $Version) {
    $Version = (& dotnet msbuild $Csproj -nologo -getProperty:Version | Out-String).Trim()
    if (-not $Version) { $Version = '1.0.0' }
}
Write-Host "==> 制品版本：$Version"
Write-Host "==> 输出目录：$OutputRoot"

# ---------- 发布单个制品变体 ----------
function Publish-Variant {
    param(
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [bool]$SelfContained,
        [string[]]$ExtraArgs = @()
    )

    $outDir = [System.IO.Path]::GetFullPath((Join-Path $OutputRoot $Name))

    # 安全检查：清理目标必须严格位于输出根目录之内
    if (-not $outDir.StartsWith("$OutputRoot\", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "制品目录超出允许范围，已中止：$outDir"
    }
    if (Test-Path -LiteralPath $outDir) {
        Write-Host "==> 清理旧制品：$outDir"
        Remove-Item -LiteralPath $outDir -Recurse -Force
    }

    # 发布选项（多文件、Release 不生成 PDB）已在 CornerCalendar.csproj 中配置，此处不再重复指定
    $publishArgs = @(
        'publish', $Csproj
        '-c', $Configuration
        '-r', $Runtime
        '--self-contained', $SelfContained.ToString().ToLowerInvariant()
        '-p:Version=' + $Version                  # 文件版本与制品目录名保持一致
        '-nologo', '-v', 'minimal'
        '-o', $outDir
    )
    $publishArgs += $ExtraArgs

    Write-Host "==> 发布制品：$Name"
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "发布失败：$Name（退出码 $LASTEXITCODE）"
    }

    # 兜底：删除任何残留的 PDB
    Get-ChildItem -LiteralPath $outDir -Recurse -Filter '*.pdb' | Remove-Item -Force
}

# ---------- 制品 1：自包含（含 .NET 运行时，开箱即用）----------
Publish-Variant `
    -Name "CornerCalendar-v$Version-$Runtime-self-contained" `
    -SelfContained $true

# ---------- 制品 2：框架依赖（客户需自行安装 .NET 8 桌面运行时）----------
Publish-Variant `
    -Name "CornerCalendar-v$Version-$Runtime-framework" `
    -SelfContained $false

# ---------- 汇总 ----------
Write-Host ''
Write-Host '==================== 制品汇总 ===================='
Get-ChildItem -LiteralPath $OutputRoot |
    Sort-Object Name |
    Select-Object Name, @{
        N = '大小(MB)'
        E = {
            if ($_.PSIsContainer) {
                [math]::Round((Get-ChildItem -LiteralPath $_.FullName -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
            } else {
                [math]::Round($_.Length / 1MB, 1)
            }
        }
    } |
    Format-Table -AutoSize

Write-Host '提示：framework 制品要求客户机器已安装 .NET 8 桌面运行时（.NET 8 Desktop Runtime，Microsoft.WindowsDesktop.App）。'
