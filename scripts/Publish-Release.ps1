#requires -Version 7
<#
.SYNOPSIS
    根据当前 CornerCalendar 程序集版本创建并推送 GitHub Release tag。

.DESCRIPTION
    脚本先构建版本探针程序集，从 CornerCalendar.dll 的 AssemblyName.Version
    读取版本，然后创建 v{版本} 注释 tag 并推送到 GitHub。

    tag 推送后由 .github/workflows/release.yml 自动执行：构建两个 Windows 制品、
    上传 zip，并使用 GitHub generate_release_notes 自动生成 Release 描述。

    发布前必须先提交并推送源代码以及 CornerCalendar.csproj 的版本修改。

.PARAMETER Remote
    Git 远程仓库名称，默认 origin。

.PARAMETER Configuration
    版本探针构建配置，默认 Release。

.EXAMPLE
    pwsh scripts\Publish-Release.ps1

.EXAMPLE
    pwsh scripts\Publish-Release.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string]$Remote = 'origin',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$csproj = Join-Path $repoRoot 'src\CornerCalendar\CornerCalendar.csproj'
$probeRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'CornerCalendar-release-version-probe'

if (-not (Test-Path -LiteralPath $csproj -PathType Leaf)) {
    throw "找不到项目文件：$csproj"
}

function Invoke-Git {
    param(
        [Parameter(Mandatory)] [string[]]$Arguments
    )

    $result = @(& git @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Git 命令失败：git $($Arguments -join ' ')`n$($result -join [Environment]::NewLine)"
    }

    return $result
}

# 不允许在未提交的工作区创建版本 tag，避免 Release 指向缺少本地改动的提交。
$status = @(Invoke-Git -Arguments @('-C', $repoRoot, 'status', '--porcelain'))
if ($status.Count -gt 0) {
    throw "工作区存在未提交改动，请先提交后再发布：`n$($status -join [Environment]::NewLine)"
}

$remoteNames = @(Invoke-Git -Arguments @('-C', $repoRoot, 'remote')) |
    ForEach-Object { $_.ToString().Trim() } |
    Where-Object { $_ }
if ($remoteNames -notcontains $Remote) {
    throw "Git 远程仓库不存在：$Remote"
}

if (Test-Path -LiteralPath $probeRoot) {
    Remove-Item -LiteralPath $probeRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $probeRoot -Force | Out-Null

try {
    Write-Host "==> 构建程序集版本探针：$Configuration"
    $buildArguments = @(
        'build', $csproj,
        '-c', $Configuration,
        '-r', 'win-x64',
        '--self-contained', 'false',
        '-p:DebugType=None',
        '-o', $probeRoot,
        '-nologo',
        '-v', 'minimal'
    )
    & dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "程序集版本探针构建失败，退出码：$LASTEXITCODE"
    }

    $assemblyPath = Join-Path $probeRoot 'CornerCalendar.dll'
    if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
        throw "找不到程序集：$assemblyPath"
    }

    $assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version
    if ($null -eq $assemblyVersion) {
        throw "无法读取程序集版本：$assemblyPath"
    }

    $version = $assemblyVersion.ToString()
    if ($version -match '^(\d+\.\d+\.\d+)\.0$') {
        $version = $Matches[1]
    }
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "程序集版本不是支持的三段版本号：$version"
    }

    $tag = "v$version"
    Write-Host "==> 程序集版本：$version"
    Write-Host "==> Release tag：$tag"

    $localTag = @(Invoke-Git -Arguments @('-C', $repoRoot, 'tag', '--list', $tag)) |
        ForEach-Object { $_.ToString().Trim() } |
        Where-Object { $_ -eq $tag }
    if ($localTag.Count -gt 0) {
        throw "本地 tag 已存在：$tag。不会覆盖已有 Release。"
    }

    $remoteTag = @(Invoke-Git -Arguments @('-C', $repoRoot, 'ls-remote', '--tags', $Remote, "refs/tags/$tag")) |
        Where-Object { $_.ToString().Trim() }
    if ($remoteTag.Count -gt 0) {
        throw "远程 tag 已存在：$Remote/$tag。不会覆盖已有 Release。"
    }

    if (-not $PSCmdlet.ShouldProcess("$Remote/$tag", "创建并推送 GitHub Release tag")) {
        Write-Host "WhatIf：不会创建或推送 tag。"
        return
    }

    Invoke-Git -Arguments @('-C', $repoRoot, 'tag', '-a', $tag, '-m', $tag) | Out-Null
    try {
        Invoke-Git -Arguments @('-C', $repoRoot, 'push', $Remote, $tag) | Out-Null
    }
    catch {
        # tag 是本次脚本刚刚创建的，推送失败时清掉本地临时 tag，便于修复网络后重试。
        & git -C $repoRoot tag -d $tag | Out-Null
        throw
    }

    Write-Host ''
    Write-Host "发布 tag 已推送：$tag"
    Write-Host 'GitHub Actions 将自动构建制品并生成 Release 描述。'
}
finally {
    if (Test-Path -LiteralPath $probeRoot) {
        Remove-Item -LiteralPath $probeRoot -Recurse -Force
    }
}
