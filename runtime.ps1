param(
    # Package folder; its runtime\ subfolder is (re)built here.
    [Parameter(Mandatory = $true)][string]$Pkg,
    [string]$PythonSrc = (Join-Path $env:APPDATA "uv\python\cpython-3.11.15-windows-x86_64-none"),
    # Folder with model.bin etc. of Systran/faster-whisper-small (see _build\dl-model.ps1).
    [string]$ModelSrc = (Join-Path (Split-Path $PSScriptRoot) "_build\models\faster-whisper-small"),
    # Optional folder of already downloaded wheels, used before the network.
    [string]$Wheels = (Join-Path (Split-Path $PSScriptRoot) "_build\wheels"),
    [switch]$KeepPython
)
# Prepares 素材筛选器\runtime so the app runs on a PC with nothing installed:
#   runtime\python   a relocatable CPython 3.11 (python-build-standalone, via uv) + locked packages
#                    + the VC++ 2015-2022 runtime next to python.exe (onnxruntime / ctranslate2 crash
#                      with msvcp140 older than 14.40, which many office PCs still have)
#   runtime\models   the speech model, so nothing is downloaded on first use
#   runtime\app      the recognition engine
$ErrorActionPreference = "Stop"
$rt = Join-Path $Pkg "runtime"
$py = Join-Path $rt "python"
New-Item -ItemType Directory -Force (Join-Path $rt "app"), (Join-Path $rt "models") | Out-Null

if (-not $KeepPython -or -not (Test-Path (Join-Path $py "python.exe"))) {
    if (Test-Path $py) { Remove-Item -LiteralPath $py -Recurse -Force }
    Copy-Item -LiteralPath $PythonSrc -Destination $py -Recurse
    # uv marks its Pythons as externally managed; this private copy is ours to install into.
    $marker = Join-Path $py "Lib\EXTERNALLY-MANAGED"
    if (Test-Path $marker) { Remove-Item -LiteralPath $marker -Force }
    $uvArgs = @("pip", "install", "--python", (Join-Path $py "python.exe"), "--link-mode", "copy", "-r", (Join-Path $PSScriptRoot "requirements-lock.txt"))
    if (Test-Path $Wheels) { $uvArgs += @("--find-links", $Wheels) }
    & uv @uvArgs
    if ($LASTEXITCODE -ne 0) { throw "package install failed" }
}

foreach ($f in "msvcp140.dll", "msvcp140_1.dll", "msvcp140_2.dll", "msvcp140_atomic_wait.dll", "msvcp140_codecvt_ids.dll",
               "concrt140.dll", "vcruntime140.dll", "vcruntime140_1.dll") {
    $s = Join-Path $env:SystemRoot "System32\$f"
    if (-not (Test-Path $s)) { throw "VC++ runtime file missing on this PC: $f" }
    if ([version](Get-Item $s).VersionInfo.FileVersion -lt [version]"14.40") { throw "$f on this PC is older than 14.40" }
    Copy-Item $s (Join-Path $py $f) -Force
}

# Not needed by the engine: package installers, Tk GUI toolkit, IDLE, docs data, headers.
$trim = @("Lib\site-packages\pip", "Lib\site-packages\setuptools", "Lib\site-packages\_distutils_hack",
          "Lib\site-packages\distutils-precedence.pth", "Lib\ensurepip", "Lib\idlelib", "Lib\tkinter", "Lib\turtledemo",
          "Lib\turtle.py", "Lib\pydoc_data", "Lib\venv", "tcl", "include", "libs", "Scripts",
          "DLLs\_tkinter.pyd", "DLLs\tcl86t.dll", "DLLs\tk86t.dll")
foreach ($t in $trim) {
    $p = Join-Path $py $t
    if (Test-Path $p) { Remove-Item -LiteralPath $p -Recurse -Force }
}
Get-ChildItem (Join-Path $py "Lib\site-packages") -Directory -Filter "pip-*.dist-info" | Remove-Item -Recurse -Force
Get-ChildItem (Join-Path $py "Lib\site-packages") -Directory -Filter "setuptools-*.dist-info" | Remove-Item -Recurse -Force

Copy-Item (Join-Path $PSScriptRoot "engine\shaixuan.py") (Join-Path $rt "app\shaixuan.py") -Force
$model = Join-Path $rt "models\faster-whisper-small"
if (-not (Test-Path (Join-Path $model "model.bin"))) { Copy-Item -LiteralPath $ModelSrc -Destination $model -Recurse }

# Precompile everything. unchecked-hash .pyc files stay valid after zipping/unzipping (zip keeps
# timestamps only to 2 s and in local time), so the first start does not recompile anything.
Get-ChildItem $rt -Recurse -Directory -Filter "__pycache__" | Remove-Item -Recurse -Force
$env:PYTHONDONTWRITEBYTECODE = $null
& (Join-Path $py "python.exe") -I -m compileall -q -j 0 --invalidation-mode unchecked-hash (Join-Path $py "Lib") (Join-Path $rt "app") | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "compileall reported problems (usually harmless test files)" }

$mb = [math]::Round((Get-ChildItem $rt -Recurse -File | Measure-Object Length -Sum).Sum / 1MB, 1)
Write-Host "runtime ready: $rt ($mb MB)"
