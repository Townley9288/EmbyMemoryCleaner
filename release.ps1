# EmbyMemoryCleaner 发版脚本
# 用法:
#   .\release.ps1 -Version 1.0.1.1 -Changelog "修复 XXX 问题"
#
# 它会自动:
#   1. 检查工作区干净
#   2. 改 csproj 版本号
#   3. 编译
#   4. 算 MD5
#   5. 在 manifest.json 顶部插入新版本记录
#   6. git commit + push + tag
#   7. gh release create 上传 DLL

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string]$Version,

    [Parameter(Mandatory=$true)]
    [string]$Changelog,

    [string]$TargetAbi = "4.9.0.0"
)

$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot

Write-Host "=== EmbyMemoryCleaner 发版 v$Version ===" -ForegroundColor Cyan

# 1. 检查 git 工作区
$dirty = git status --porcelain
if ($dirty) {
    Write-Host "工作区不干净，请先 commit 或 stash:" -ForegroundColor Red
    Write-Host $dirty
    exit 1
}

# 2. 改版本号
Write-Host "[1/6] 更新 csproj 版本号..." -ForegroundColor Yellow
$csproj = Get-Content .\EmbyMemoryCleaner.csproj -Raw
$csproj = $csproj -replace '<Version>[\d.]+</Version>', "<Version>$Version</Version>"
$csproj = $csproj -replace '<FileVersion>[\d.]+</FileVersion>', "<FileVersion>$Version</FileVersion>"
Set-Content -Path .\EmbyMemoryCleaner.csproj -Value $csproj -NoNewline -Encoding UTF8

# 3. 编译
Write-Host "[2/6] 编译 Release..." -ForegroundColor Yellow
$buildOut = dotnet build -c Release --no-incremental 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "编译失败:" -ForegroundColor Red
    Write-Host $buildOut
    exit 1
}

$dllPath = "bin\Release\net6.0\EmbyMemoryCleaner.dll"
if (-not (Test-Path $dllPath)) { Write-Host "未找到 DLL"; exit 1 }

# 4. 计算 MD5
Write-Host "[3/6] 计算 MD5..." -ForegroundColor Yellow
$md5 = (Get-FileHash $dllPath -Algorithm MD5).Hash.ToLower()
Write-Host "MD5: $md5"

# 5. 在 manifest.json 顶部插入新版本
Write-Host "[4/6] 更新 manifest.json..." -ForegroundColor Yellow
$timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
$newEntry = [ordered]@{
    version    = $Version
    changelog  = $Changelog
    targetAbi  = $TargetAbi
    sourceUrl  = "https://github.com/Townley9288/EmbyMemoryCleaner/releases/download/v$Version/EmbyMemoryCleaner.dll"
    checksum   = $md5
    timestamp  = $timestamp
}

$manifest = Get-Content .\manifest.json -Raw | ConvertFrom-Json
# manifest 是数组，第一个元素是插件信息
$plugin = $manifest[0]
$plugin.versions = @($newEntry) + $plugin.versions
$json = $manifest | ConvertTo-Json -Depth 10
Set-Content -Path .\manifest.json -Value $json -Encoding UTF8

# 6. git commit + tag + push
Write-Host "[5/6] git commit + tag + push..." -ForegroundColor Yellow
git add EmbyMemoryCleaner.csproj manifest.json
git commit -m "Release v$Version"
git push
git tag "v$Version"
git push origin "v$Version"

# 7. 发 Release
Write-Host "[6/6] 创建 GitHub Release..." -ForegroundColor Yellow
$notes = @"
$Changelog

MD5: ``$md5``

## 安装方式

直接下载 ``EmbyMemoryCleaner.dll`` 放到 Emby plugins 目录后重启 Emby Server。

或在 Emby 后台 → 高级 → 插件 → 插件源 添加：
``````
https://raw.githubusercontent.com/Townley9288/EmbyMemoryCleaner/main/manifest.json
``````
"@

gh release create "v$Version" $dllPath --title "v$Version" --notes $notes

Write-Host "" -ForegroundColor Green
Write-Host "=== 发版成功 ===" -ForegroundColor Green
Write-Host "Release: https://github.com/Townley9288/EmbyMemoryCleaner/releases/tag/v$Version"
Write-Host "manifest.json 已更新，安装了插件源的 Emby 实例下次检查会看到更新"
