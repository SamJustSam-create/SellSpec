<#
.SYNOPSIS
  Renders assets\logo.svg into the application icon (multi-size .ico with PNG frames) and the 256 px sidebar PNG.

.DESCRIPTION
  Uses WPF (PresentationCore) to rasterise the SVG, so no external tool is needed. Only the subset of SVG that the
  logo uses is understood: <path> elements with solid or linear-gradient fills (gradientUnits="userSpaceOnUse"),
  fill-rule, fill-opacity and optional strokes; <g> elements with translate/rotate/scale transforms; and the
  data-min-size="N" / data-max-size="N" attributes, which include an element only in frames at least / at most
  N px wide. Both output files are written together at the end. Re-run after editing the SVG.

.PARAMETER OutDir
  Where SellSpec.ico and SellSpec.png are written. Default: src\SellSpec.App\Assets.

.PARAMETER Svg
  The master logo. Default: assets\logo.svg.
#>
param(
    [string]$OutDir,
    [string]$Svg
)
$ErrorActionPreference = 'Stop'

# WPF needs a single-threaded apartment; relaunch when the host is not one (e.g. some CI shells).
if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    $arguments = @('-STA', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($Svg) { $arguments += @('-Svg', $Svg) }
    if ($OutDir) { $arguments += @('-OutDir', $OutDir) }
    & powershell.exe @arguments
    exit $LASTEXITCODE
}

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
if (-not $Svg) { $Svg = Join-Path $root 'assets\logo.svg' }
if (-not $OutDir) { $OutDir = Join-Path $root 'src\SellSpec.App\Assets' }
New-Item -ItemType Directory -Force $OutDir | Out-Null

function ConvertTo-Color([string]$hex, [double]$opacity) {
    $color = [System.Windows.Media.ColorConverter]::ConvertFromString($hex)
    if ($opacity -lt 1) { $color.A = [byte]([Math]::Round($opacity * 255)) }
    return $color
}

function Read-SvgDocument([string]$path) {
    [xml]$doc = Get-Content -Raw $path
    $viewBox = ($doc.svg.viewBox -split '\s+') | ForEach-Object { [double]$_ }
    $gradients = @{}
    foreach ($gradient in $doc.svg.SelectNodes("//*[local-name()='linearGradient']")) {
        $brush = New-Object System.Windows.Media.LinearGradientBrush
        $brush.MappingMode = 'Absolute'
        $brush.StartPoint = New-Object System.Windows.Point ([double]$gradient.x1), ([double]$gradient.y1)
        $brush.EndPoint = New-Object System.Windows.Point ([double]$gradient.x2), ([double]$gradient.y2)
        foreach ($stop in $gradient.SelectNodes("*[local-name()='stop']")) {
            [void]$brush.GradientStops.Add((New-Object System.Windows.Media.GradientStop (ConvertTo-Color $stop.'stop-color' 1), ([double]$stop.offset)))
        }
        $brush.Freeze()
        $gradients[$gradient.id] = $brush
    }
    return @{ Doc = $doc; ViewBox = $viewBox; Gradients = $gradients }
}

function Get-FillBrush($svg, [string]$fill, [double]$opacity) {
    if (-not $fill -or $fill -eq 'none') { return $null }
    if ($fill -match 'url\(#([^)]+)\)') {
        $brush = $svg.Gradients[$Matches[1]].Clone()
        if ($opacity -lt 1) { $brush.Opacity = $opacity }
        $brush.Freeze()
        return $brush
    }
    $solid = New-Object System.Windows.Media.SolidColorBrush (ConvertTo-Color $fill $opacity)
    $solid.Freeze()
    return $solid
}

function ConvertTo-Transform([string]$spec) {
    # SVG applies the right-most function first; a WPF TransformGroup applies its children in order, so reverse.
    $group = New-Object System.Windows.Media.TransformGroup
    $functions = [regex]::Matches($spec, '(\w+)\s*\(([^)]*)\)')
    for ($i = $functions.Count - 1; $i -ge 0; $i--) {
        $name = $functions[$i].Groups[1].Value
        $values = @(($functions[$i].Groups[2].Value -split '[\s,]+' | Where-Object { $_ -ne '' }) | ForEach-Object { [double]$_ })
        switch ($name) {
            'translate' { [void]$group.Children.Add((New-Object System.Windows.Media.TranslateTransform $values[0], $(if ($values.Count -gt 1) { $values[1] } else { 0 }))) }
            'scale'     { [void]$group.Children.Add((New-Object System.Windows.Media.ScaleTransform $values[0], $(if ($values.Count -gt 1) { $values[1] } else { $values[0] }))) }
            'rotate'    { [void]$group.Children.Add((New-Object System.Windows.Media.RotateTransform $values[0], $(if ($values.Count -gt 2) { $values[1] } else { 0 }), $(if ($values.Count -gt 2) { $values[2] } else { 0 }))) }
            default     { throw "Unsupported transform '$name' in $Svg." }
        }
    }
    return $group
}

function Draw-Node($svg, $context, $node, [int]$frameSize, [string]$inheritedFill, [double]$inheritedOpacity) {
    foreach ($child in $node.ChildNodes) {
        if ($child.NodeType -ne 'Element') { continue }
        $minSize = $child.GetAttribute('data-min-size')
        if ($minSize -and [int]$minSize -gt $frameSize) { continue }
        $maxSize = $child.GetAttribute('data-max-size')
        if ($maxSize -and [int]$maxSize -lt $frameSize) { continue }
        $fill = if ($child.HasAttribute('fill')) { $child.GetAttribute('fill') } else { $inheritedFill }
        $opacity = if ($child.HasAttribute('fill-opacity')) { [double]$child.GetAttribute('fill-opacity') } else { $inheritedOpacity }
        switch ($child.LocalName) {
            'defs' { }
            'g' {
                $pushed = $false
                if ($child.HasAttribute('transform')) { $context.PushTransform((ConvertTo-Transform $child.GetAttribute('transform'))); $pushed = $true }
                Draw-Node $svg $context $child $frameSize $fill $opacity
                if ($pushed) { $context.Pop() }
            }
            'path' {
                # SVG fills with the nonzero rule unless fill-rule says otherwise; WPF's parser wants F1 / F0 up front.
                $rule = if ($child.GetAttribute('fill-rule') -eq 'evenodd') { 'F0 ' } else { 'F1 ' }
                $geometry = [System.Windows.Media.Geometry]::Parse($rule + $child.GetAttribute('d'))
                $pen = $null
                if ($child.HasAttribute('stroke') -and $child.GetAttribute('stroke') -ne 'none') {
                    $width = if ($child.HasAttribute('stroke-width')) { [double]$child.GetAttribute('stroke-width') } else { 1.0 }
                    $pen = New-Object System.Windows.Media.Pen (Get-FillBrush $svg $child.GetAttribute('stroke') 1), $width
                    $pen.LineJoin = 'Round'
                    $pen.Freeze()
                }
                $context.DrawGeometry((Get-FillBrush $svg $fill $opacity), $pen, $geometry)
            }
            default { throw "Unsupported element <$($child.LocalName)> in $Svg." }
        }
    }
}

function Render-Frame($svg, [int]$size) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    # Fit the whole viewBox into the square frame, centred, whatever its origin or aspect ratio.
    $minX, $minY, $width, $height = $svg.ViewBox
    $scale = $size / [Math]::Max($width, $height)
    $offsetX = ($size - $width * $scale) / 2
    $offsetY = ($size - $height * $scale) / 2
    $placement = New-Object System.Windows.Media.TransformGroup
    [void]$placement.Children.Add((New-Object System.Windows.Media.TranslateTransform (-$minX), (-$minY)))
    [void]$placement.Children.Add((New-Object System.Windows.Media.ScaleTransform $scale, $scale))
    [void]$placement.Children.Add((New-Object System.Windows.Media.TranslateTransform $offsetX, $offsetY))
    $context.PushTransform($placement)
    Draw-Node $svg $context $svg.Doc.DocumentElement $size '#000000' 1.0
    $context.Pop()
    $context.Close()
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size, $size, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $bitmap.Freeze()
    return $bitmap
}

function ConvertTo-PngBytes($bitmap) {
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    [void]$encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    # The leading comma returns the byte[] as one object instead of streaming 20,000 bytes through the pipeline.
    return ,$stream.ToArray()
}

$logo = Read-SvgDocument $Svg
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = @()
foreach ($size in $sizes) {
    [byte[]]$bytes = ConvertTo-PngBytes (Render-Frame $logo $size)
    if ($bytes.Length -lt 100 -or $bytes[1] -ne 0x50) { throw "Frame $size did not encode as PNG ($($bytes.Length) bytes)." }
    $frames += ,@{ Size = $size; Bytes = $bytes }
}
$png = ($frames | Where-Object { $_.Size -eq 256 }).Bytes

# ICO container: header, one directory entry per frame, then the PNG payloads.
$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $ico
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($frame in $frames) {
    $dimension = if ($frame.Size -ge 256) { 0 } else { $frame.Size }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
    $offset += $frame.Bytes.Length
}
foreach ($frame in $frames) { $writer.Write($frame.Bytes) }
$writer.Flush()
$icoBytes = $ico.ToArray()
$writer.Dispose(); $ico.Dispose()

# Everything is rendered before anything is written, and both files are staged beside their targets and then moved
# into place back to back, so an interrupted run never leaves a half-written file or a lone updated one behind.
$outputs = @(
    @{ Name = 'SellSpec.ico'; Bytes = $icoBytes },
    @{ Name = 'SellSpec.png'; Bytes = $png }
)
foreach ($output in $outputs) {
    $output.Temp = Join-Path $OutDir ($output.Name + '.' + [System.IO.Path]::GetRandomFileName() + '.tmp')
    [System.IO.File]::WriteAllBytes($output.Temp, $output.Bytes)
}
foreach ($output in $outputs) {
    Move-Item -LiteralPath $output.Temp -Destination (Join-Path $OutDir $output.Name) -Force
}
Write-Host "Rendered $Svg to $(Join-Path $OutDir 'SellSpec.ico') ($($frames.Count) frames) and SellSpec.png"
