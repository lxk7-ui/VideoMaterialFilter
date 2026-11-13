param(
    [string]$Repo = "Systran/faster-whisper-small",
    [string]$Out = (Join-Path $PSScriptRoot "models\faster-whisper-small"),
    [int]$Parts = 16
)
# Downloads a Hugging Face model with many parallel range requests (a single connection to the
# mirror only gets ~80 KB/s here). Every chunk retries until complete, then model.bin is joined
# and checked against the LFS sha256 from the repo listing.
$ErrorActionPreference = "Stop"
$base = "https://hf-mirror.com"
New-Item -ItemType Directory -Force $Out | Out-Null
$tree = Invoke-RestMethod -Uri "$base/api/models/$Repo/tree/main" -TimeoutSec 60
$want = @("config.json", "model.bin", "tokenizer.json", "vocabulary.txt", "vocabulary.json", "preprocessor_config.json")

foreach ($f in $tree | Where-Object { $want -contains $_.path -and $_.path -ne "model.bin" }) {
    $dst = Join-Path $Out $f.path
    if ((Test-Path $dst) -and (Get-Item $dst).Length -eq $f.size) { continue }
    & curl.exe -s -L --retry 5 --retry-all-errors --connect-timeout 20 -o $dst "$base/$Repo/resolve/main/$($f.path)"
    if ((Get-Item $dst).Length -ne $f.size) { throw "size mismatch: $($f.path)" }
    Write-Host ("got {0} ({1} bytes)" -f $f.path, $f.size)
}

$bin = $tree | Where-Object { $_.path -eq "model.bin" }
$size = [long]$bin.size
$sha = $bin.lfs.oid
$dst = Join-Path $Out "model.bin"
if ((Test-Path $dst) -and (Get-Item $dst).Length -eq $size) { Write-Host "model.bin already there"; return }
$partDir = Join-Path $PSScriptRoot "model-parts"
New-Item -ItemType Directory -Force $partDir | Out-Null
$chunk = [long][math]::Ceiling($size / $Parts)
$url = "$base/$Repo/resolve/main/model.bin"

$jobs = for ($i = 0; $i -lt $Parts; $i++) {
    $start = $i * $chunk
    $end = [math]::Min($size, $start + $chunk) - 1
    Start-Job -ArgumentList $url, $partDir, $i, $start, $end -ScriptBlock {
        param($url, $partDir, $i, $start, $end)
        $part = Join-Path $partDir ("part{0:D2}" -f $i)
        $tmp = "$part.tmp"
        $need = $end - $start + 1
        for ($try = 0; $try -lt 200; $try++) {
            $have = if (Test-Path $part) { (Get-Item $part).Length } else { 0 }
            if ($have -ge $need) { return "part $i done" }
            & curl.exe -s -L --connect-timeout 20 --speed-limit 2000 --speed-time 30 -r ("{0}-{1}" -f ($start + $have), $end) -o $tmp $url
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

while ($true) {
    $got = (Get-ChildItem $partDir -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
    $running = @($jobs | Where-Object { $_.State -eq "Running" }).Count
    Write-Host ("{0:HH:mm:ss}  {1:N1} / {2:N1} MB, {3} parts running" -f (Get-Date), ($got / 1MB), ($size / 1MB), $running)
    if ($running -eq 0) { break }
    Start-Sleep -Seconds 20
}
$jobs | Receive-Job | Write-Host
if (@($jobs | Where-Object { $_.State -ne "Completed" }).Count) { throw "some parts failed" }

$fs = [IO.File]::Create($dst)
for ($i = 0; $i -lt $Parts; $i++) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $partDir ("part{0:D2}" -f $i)))
    $fs.Write($bytes, 0, $bytes.Length)
}
$fs.Close()
$hash = (Get-FileHash $dst -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $sha) { throw "sha256 mismatch: $hash != $sha" }
Remove-Item $partDir -Recurse -Force
Write-Host "model.bin OK ($size bytes, sha256 $hash)"
