param(
    [string]$Version = "v2.1",
    # Folder that receives 素材筛选器\ (and the zip next to it).
    [string]$OutRoot = (Join-Path (Split-Path $PSScriptRoot) "视频素材工具_v2"),
    [switch]$NoZip
)
# Assembles the portable 素材筛选器 folder: exe + icon + docs + default rules + runtime\
# (runtime.ps1 builds runtime\ the first time), then zips it as 素材筛选器_<version>_便携版.zip.
$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "build.ps1") | Out-Null
$app = Join-Path $OutRoot "素材筛选器"
New-Item -ItemType Directory -Force $app | Out-Null
if (-not (Test-Path (Join-Path $app "runtime\python\python.exe"))) {
    & (Join-Path $PSScriptRoot "runtime.ps1") -Pkg $app
} else {
    & (Join-Path $PSScriptRoot "runtime.ps1") -Pkg $app -KeepPython
}

Copy-Item (Join-Path $PSScriptRoot "bin\素材筛选器.exe") $app -Force
Copy-Item (Join-Path $PSScriptRoot "素材筛选器.ico") $app -Force
Copy-Item (Join-Path $PSScriptRoot "package\使用说明.txt") $app -Force
Copy-Item (Join-Path $PSScriptRoot "package\创建桌面快捷方式.vbs") $app -Force
Copy-Item (Join-Path $PSScriptRoot "package\设置.ini") $app -Force
# Anything a test run left behind (the package ships with an empty 筛选记忆 and only the small model).
foreach ($f in "上次设置.json", "运行日志.txt", "_Render.exe", "筛选记忆.txt", "筛选记忆_清空前.txt") {
    $p = Join-Path $app $f
    if (Test-Path $p) { Remove-Item -LiteralPath $p -Force }
}
Get-ChildItem (Join-Path $app "runtime\models") -Directory | Where-Object { $_.Name -ne "faster-whisper-small" } |
    ForEach-Object { Write-Host "removing test model folder $($_.Name)"; Remove-Item -LiteralPath $_.FullName -Recurse -Force }
Get-ChildItem $app -File | Select-Object Name, Length | Format-Table -AutoSize | Out-String | Write-Host
if ($NoZip) { return }

$zip = Join-Path $OutRoot ("素材筛选器_{0}_便携版.zip" -f $Version)
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($app, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)
Get-Item $zip | Select-Object FullName, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-List | Out-String | Write-Host
