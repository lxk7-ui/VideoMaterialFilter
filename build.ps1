param(
    [string]$Out = (Join-Path $PSScriptRoot "bin\素材筛选器.exe")
)
# Builds 素材筛选器.exe for .NET Framework 4.6+ (built into Windows 10/11) with the Roslyn compiler
# from the .NET SDK, the same way Axe is built. No NuGet packages or Visual Studio needed.
$ErrorActionPreference = "Stop"
$sdk = Get-ChildItem "C:\Program Files\dotnet\sdk" -Directory | Sort-Object { [version]($_.Name -replace '[^0-9.].*$', '') } -Descending | Select-Object -First 1
$csc = Join-Path $sdk.FullName "Roslyn\bincore\csc.dll"
$ref = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1"
if (-not (Test-Path $csc)) { throw "Roslyn compiler not found: $csc" }
if (-not (Test-Path (Join-Path $ref "mscorlib.dll"))) { throw ".NET Framework reference assemblies not found: $ref" }
$refs = @("mscorlib.dll", "System.dll", "System.Core.dll", "System.Xaml.dll", "System.Web.Extensions.dll",
          "WindowsBase.dll", "PresentationCore.dll", "PresentationFramework.dll") | ForEach-Object { "/reference:" + (Join-Path $ref $_) }
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$ico = Join-Path $PSScriptRoot "素材筛选器.ico"
$src = Get-ChildItem (Join-Path $PSScriptRoot "src") -Filter *.cs | ForEach-Object { $_.FullName }
$cscArgs = @("exec", $csc, "/nologo", "/noconfig", "/nostdlib+", "/target:winexe", "/platform:anycpu", "/optimize+",
          "/langversion:latest", "/utf8output", "/codepage:65001", "/warn:4", "/out:$Out")
if (Test-Path $ico) { $cscArgs += "/win32icon:$ico" }
$cscArgs += $refs + $src
$log = & dotnet @cscArgs 2>&1
$log | Where-Object { $_ -match 'error|warning' } | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
Get-Item $Out | Select-Object FullName, Length, LastWriteTime
