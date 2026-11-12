param([string]$Out = (Join-Path $PSScriptRoot "素材筛选器.ico"))
# Draws the app icon (blue rounded square + white funnel, same as the title-bar logo) at the usual
# Windows sizes and packs the PNGs into one .ico file.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$sizes = 256, 128, 64, 48, 40, 32, 24, 20, 16
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $pad = [math]::Max(0.5, $s * 0.03)
    $r = $s * 0.22
    $rect = New-Object System.Drawing.RectangleF $pad, $pad, ($s - 2 * $pad), ($s - 2 * $pad)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 70, 140, 204)), ([System.Drawing.Color]::FromArgb(255, 28, 72, 114)), 45.0
    $g.FillPath($brush, $path)
    # Funnel "M3,4 H21 L14,12.5 V19 L10,21 V12.5 Z" in a 24-unit box, scaled into the middle 62%.
    $k = $s * 0.62 / 18.0
    $ox = ($s - 18.0 * $k) / 2 - 3 * $k
    $oy = ($s - 17.0 * $k) / 2 - 4 * $k
    $pts = @(@(3, 4), @(21, 4), @(14, 12.5), @(14, 19), @(10, 21), @(10, 12.5)) | ForEach-Object { New-Object System.Drawing.PointF ($ox + $_[0] * $k), ($oy + $_[1] * $k) }
    $g.FillPolygon([System.Drawing.Brushes]::White, [System.Drawing.PointF[]]$pts)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    , $ms.ToArray()
}
$fs = [IO.File]::Create($Out)
$bw = New-Object IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $bw.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write([byte[]]$p) }
$bw.Close()
Get-Item $Out | Select-Object FullName, Length
