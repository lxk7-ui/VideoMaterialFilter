param(
    [string[]]$Packages = @("opencv-python", "ctranslate2", "av", "rapidocr-onnxruntime", "onnxruntime"),
    [string]$Out = (Join-Path $PSScriptRoot "wheels"),
    [int]$Parts = 6
)
# Fetches the newest Windows / CPython 3.11 wheel of each package with parallel range requests
# (single connections to PyPI's CDN crawl at ~50 KB/s here) and checks the sha256 from PyPI.
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force $Out | Out-Null
$targets = foreach ($pkg in $Packages) {
    $meta = Invoke-RestMethod -Uri "https://pypi.org/pypi/$pkg/json" -TimeoutSec 60
    $files = $meta.urls | Where-Object { $_.packagetype -eq "bdist_wheel" }
    $pick = $files | Where-Object { $_.filename -match "cp311-cp311-win_amd64\.whl$" } | Select-Object -First 1
    if (-not $pick) { $pick = $files | Where-Object { $_.filename -match "abi3-win_amd64\.whl$" } | Select-Object -First 1 }
    if (-not $pick) { $pick = $files | Where-Object { $_.filename -match "py3-none-any\.whl$" } | Select-Object -First 1 }
    if (-not $pick) { throw "no wheel for $pkg" }
    Write-Host ("{0} {1}: {2} ({3:N1} MB)" -f $pkg, $meta.info.version, $pick.filename, ($pick.size / 1MB))
    [pscustomobject]@{ Name = $pick.filename; Url = $pick.url; Size = [long]$pick.size; Sha = $pick.digests.sha256 }
}

$jobs = @()
foreach ($t in $targets) {
    $dst = Join-Path $Out $t.Name
    if ((Test-Path $dst) -and (Get-Item $dst).Length -eq $t.Size) { continue }
    $partDir = Join-Path $Out ($t.Name + ".parts")
    New-Item -ItemType Directory -Force $partDir | Out-Null
    $chunk = [long][math]::Ceiling($t.Size / $Parts)
    for ($i = 0; $i -lt $Parts; $i++) {
        $start = $i * $chunk
        $end = [math]::Min($t.Size, $start + $chunk) - 1
        $jobs += Start-Job -ArgumentList $t.Url, $partDir, $i, $start, $end -ScriptBlock {
            param($url, $partDir, $i, $start, $end)
            $part = Join-Path $partDir ("part{0:D2}" -f $i)
            $tmp = "$part.tmp"
            $need = $end - $start + 1
            for ($try = 0; $try -lt 300; $try++) {
                $have = if (Test-Path $part) { (Get-Item $part).Length } else { 0 }
                if ($have -ge $need) { return }
                # --max-time keeps a stalled connection from hanging forever; the loop resumes it.
                & curl.exe -s -L --connect-timeout 15 --max-time 90 -r ("{0}-{1}" -f ($start + $have), $end) -o $tmp $url
                if (Test-Path $tmp) {
                    $bytes = [IO.File]::ReadAllBytes($tmp)
                    $fs = [IO.File]::Open($part, [IO.FileMode]::Append)
                    $fs.Write($bytes, 0, $bytes.Length)
                    $fs.Close()
                    Remove-Item $tmp -Force
                }
            }
            throw "part $i failed"
        }
    }
}

while ($jobs.Count -and @($jobs | Where-Object { $_.State -eq "Running" }).Count) {
    $got = (Get-ChildItem $Out -Recurse -File | Where-Object { $_.DirectoryName -like "*.parts" } | Measure-Object Length -Sum).Sum
    Write-Host ("{0:HH:mm:ss}  {1:N1} MB in parts, {2} jobs running" -f (Get-Date), ($got / 1MB), @($jobs | Where-Object { $_.State -eq "Running" }).Count)
    Start-Sleep -Seconds 15
}

foreach ($t in $targets) {
    $dst = Join-Path $Out $t.Name
    $partDir = Join-Path $Out ($t.Name + ".parts")
    if (Test-Path $partDir) {
        $fs = [IO.File]::Create($dst)
        Get-ChildItem $partDir -Filter "part??" | Sort-Object Name | ForEach-Object {
            $bytes = [IO.File]::ReadAllBytes($_.FullName)
            $fs.Write($bytes, 0, $bytes.Length)
        }
        $fs.Close()
    }
    $hash = (Get-FileHash $dst -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $t.Sha) { throw "sha256 mismatch for $($t.Name)" }
    if (Test-Path $partDir) { Remove-Item $partDir -Recurse -Force }
    Write-Host "OK $($t.Name)"
}
