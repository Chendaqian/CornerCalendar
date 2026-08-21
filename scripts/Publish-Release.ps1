#requires -Version 7
<#
.SYNOPSIS
    提交、推送代码并创建 CornerCalendar GitHub Release tag。

.DESCRIPTION
    脚本先构建版本探针程序集，从 CornerCalendar.dll 的 AssemblyName.Version
    读取版本，然后提交工作区改动、推送当前分支、创建 v{版本} 注释 tag 并推送到 GitHub。

    tag 推送后由 .github/workflows/release.yml 自动执行：构建两个 Windows 制品、
    上传 zip，并创建标题为 v{版本} 的 Release 和变更内容。

.PARAMETER Remote
    Git 远程仓库名称，默认 origin。

.PARAMETER Configuration
    版本探针构建配置，默认 Release。

.PARAMETER CommitMessage
    提交信息。缺省为 "chore: release v{版本}"。

.PARAMETER WaitForRelease
    等待 GitHub Actions 完成并校验 Release 已创建。需要已登录 GitHub CLI（gh auth login）。

.EXAMPLE
    pwsh scripts\Publish-Release.ps1

.EXAMPLE
    pwsh scripts\Publish-Release.ps1 -CommitMessage 'feat: update calendar' -WaitForRelease

.EXAMPLE
    pwsh scripts\Publish-Release.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string]$Remote = 'origin',
    [string]$Configuration = 'Release',
    [string]$CommitMessage = '',
    [switch]$WaitForRelease
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

function Invoke-Gh {
    param(
        [Parameter(Mandatory)] [string[]]$Arguments
    )

    $result = @(& gh @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI 命令失败：gh $($Arguments -join ' ')`n$($result -join [Environment]::NewLine)"
    }

    return $result
}

function Get-ReleaseRun {
    param(
        [Parameter(Mandatory)] [string]$Tag
    )

    $json = Invoke-Gh -Arguments @(
        'run', 'list',
        '--workflow', 'release.yml',
        '--limit', '20',
        '--json', 'databaseId,headBranch,event,status,conclusion'
    )
    $runs = ($json -join [Environment]::NewLine) | ConvertFrom-Json
    return @($runs | Where-Object {
        $_.event -eq 'push' -and $_.headBranch -eq $Tag
    } | Select-Object -First 1)
}

function Wait-ForRelease {
    param(
        [Parameter(Mandatory)] [string]$Tag,
        [int]$TimeoutMinutes = 20
    )

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw '未找到 GitHub CLI（gh），无法等待 Release。请安装 gh 或不使用 -WaitForRelease。'
    }

    Write-Host '==> 等待 GitHub Actions 创建 Release...'
    $deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)
    $run = $null
    while ([DateTime]::UtcNow -lt $deadline -and $null -eq $run) {
        $run = @(Get-ReleaseRun -Tag $Tag) | Select-Object -First 1
        if ($null -eq $run) {
            Start-Sleep -Seconds 5
        }
    }

    if ($null -eq $run) {
        throw "在 $TimeoutMinutes 分钟内没有找到 tag $Tag 对应的 Release 工作流。"
    }

    Write-Host "==> 监控工作流运行：$($run.databaseId)"
    $runState = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        $runState = (Invoke-Gh -Arguments @(
            'run', 'view', $run.databaseId,
            '--json', 'status,conclusion'
        ) -join [Environment]::NewLine) | ConvertFrom-Json

        if ($runState.status -eq 'completed') {
            if ($runState.conclusion -ne 'success') {
                throw "GitHub Actions 发布失败，状态：$($runState.conclusion)。请使用 gh run view $($run.databaseId) 检查详情。"
            }
            break
        }

        Start-Sleep -Seconds 10
    }

    if ($null -eq $runState -or $runState.status -ne 'completed') {
        throw "GitHub Actions 在 $TimeoutMinutes 分钟内未完成。请使用 gh run view $($run.databaseId) 继续查看。"
    }

    $releaseJson = Invoke-Gh -Arguments @('release', 'view', $Tag, '--json', 'name,url')
    $release = ($releaseJson -join [Environment]::NewLine) | ConvertFrom-Json
    Write-Host "Release 已创建：$($release.name)"
    Write-Host "Release 地址：$($release.url)"
}

$remoteNames = @(Invoke-Git -Arguments @('-C', $repoRoot, 'remote')) |
    ForEach-Object { $_.ToString().Trim() } |
    Where-Object { $_ }
if ($remoteNames -notcontains $Remote) {
    throw "Git 远程仓库不存在：$Remote"
}

$branch = (Invoke-Git -Arguments @('-C', $repoRoot, 'branch', '--show-current') |
    Select-Object -First 1).ToString().Trim()
if (-not $branch) {
    throw '当前处于 detached HEAD，无法自动推送分支。请切换到要发布的分支后重试。'
}

if (Test-Path -LiteralPath $probeRoot) {
    Remove-Item -LiteralPath $probeRoot -Recurse -Force -WhatIf:$false
}
New-Item -ItemType Directory -Path $probeRoot -Force -WhatIf:$false | Out-Null

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

    $status = @(Invoke-Git -Arguments @('-C', $repoRoot, 'status', '--porcelain'))
    if ($status.Count -gt 0) {
        Write-Host "==> 待提交改动：$($status.Count) 项"
        if ($CommitMessage -eq '') {
            $CommitMessage = "chore: release v$version"
        }
        Write-Host "==> 提交信息：$CommitMessage"
    }
    else {
        Write-Host '==> 工作区没有未提交改动，将直接推送当前分支。'
    }

    if (-not $PSCmdlet.ShouldProcess("$Remote/$branch 和 $Remote/$tag", '提交、推送代码并创建 Release tag')) {
        Write-Host 'WhatIf：不会提交、推送代码或创建 tag。'
        return
    }

    if ($status.Count -gt 0) {
        Invoke-Git -Arguments @('-C', $repoRoot, 'add', '--all') | Out-Null
        Invoke-Git -Arguments @('-C', $repoRoot, 'commit', '-m', $CommitMessage) | Out-Null
    }
    Invoke-Git -Arguments @('-C', $repoRoot, 'push', $Remote, $branch) | Out-Null

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
    Write-Host 'GitHub Actions 将自动构建两个制品并创建 Release。'
    if ($WaitForRelease) {
        Wait-ForRelease -Tag $tag
    }
}
finally {
    if (Test-Path -LiteralPath $probeRoot) {
        Remove-Item -LiteralPath $probeRoot -Recurse -Force -WhatIf:$false
    }
}
