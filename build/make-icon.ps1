<#
.SYNOPSIS
  Generates the SellSpec application icon (multi-size .ico with PNG-compressed frames) and a 256px PNG.
  Pure System.Drawing; no external tools. Re-run after changing colours or the glyph.
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\src\SellSpec.App\Assets')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded square in the brand blue.
    $radius = [Math]::Max(2, [int]($size * 0.22))
    $rect = New-Object System.Drawing.Rectangle 0, 0, ($size - 1), ($size - 1)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 15, 108, 189))
    $g.FillPath($brush, $path)

    # Price-tag silhouette: a white tag rotated 45 degrees with a hole, suggesting "list it / sell it".
    $white = [System.Drawing.Brushes]::White
    $tagW = $size * 0.46; $tagH = $size * 0.62
    $g.TranslateTransform($size / 2, $size / 2)
    $g.RotateTransform(-45)
    $tag = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = $size * 0.06
    $x0 = -$tagW / 2; $y0 = -$tagH / 2
    $tag.AddLine($x0, $y0 + $tagW / 2, 0, $y0)
    $tag.AddLine(0, $y0, $x0 + $tagW, $y0 + $tagW / 2)
    $tag.AddLine($x0 + $tagW, $y0 + $tagW / 2, $x0 + $tagW, $y0 + $tagH)
    $tag.AddLine($x0 + $tagW, $y0 + $tagH, $x0, $y0 + $tagH)
    $tag.CloseFigure()
    $g.FillPath($white, $tag)
    $hole = $size * 0.11
    $g.FillEllipse($brush, -$hole / 2, $y0 + $tagW * 0.30 - $hole / 2, $hole, $hole)
    # Tick mark on the tag body.
    if ($size -ge 32) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 15, 108, 189)), ([Math]::Max(1.5, $size * 0.07))
        $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
        $cy = $y0 + $tagH * 0.68
        $g.DrawLine($pen, (-$tagW * 0.26), $cy, (-$tagW * 0.05), ($cy + $tagH * 0.12))
        $g.DrawLine($pen, (-$tagW * 0.05), ($cy + $tagH * 0.12), ($tagW * 0.30), ($cy - $tagH * 0.14))
        $pen.Dispose()
    }
    $g.ResetTransform()
    $g.Dispose(); $brush.Dispose(); $path.Dispose(); $tag.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = @()
foreach ($s in $sizes) {
    $bmp = New-Frame $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $frames += ,@{ Size = $s; Bytes = $ms.ToArray() }
    if ($s -eq 256) { $bmp.Save((Join-Path $OutDir 'SellSpec.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose(); $ms.Dispose()
}

# ICO container: header + directory entries + PNG payloads.
$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$f.Bytes.Length); $w.Write([uint32]$offset)
    $offset += $f.Bytes.Length
}
foreach ($f in $frames) { $w.Write($f.Bytes) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'SellSpec.ico'), $ico.ToArray())
$w.Dispose(); $ico.Dispose()
Write-Host "Wrote $(Join-Path $OutDir 'SellSpec.ico') ($($frames.Count) frames) and SellSpec.png"
