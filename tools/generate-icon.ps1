#requires -Version 5.1
<#
    Generiert PowerClean.ico aus WPF-Vektorzeichnung.
    Modernes Icon: Squircle mit Akzent-Gradient, weißer Lightning-Bolt + 2 Sparkles.
#>

[CmdletBinding()]
param(
    [string]$OutputPath
)

if (-not $OutputPath) {
    $repoRoot = (Resolve-Path "$PSScriptRoot/..").Path
    $OutputPath = Join-Path $repoRoot 'src\Cleaner.App\Assets\PowerClean.ico'
}

Write-Host "Output: $OutputPath"

Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName WindowsBase

function New-DrawingVisual {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $ctx = $visual.RenderOpen()

    # Squircle-Hintergrund mit Diagonal-Gradient
    $bgRect = New-Object System.Windows.Rect(0, 0, 256, 256)
    $bgGeom = New-Object System.Windows.Media.RectangleGeometry($bgRect, 56, 56)
    $bgBrush = New-Object System.Windows.Media.LinearGradientBrush(
        ([System.Windows.Media.Color]::FromRgb(0xC0, 0x76, 0xFF)),
        ([System.Windows.Media.Color]::FromRgb(0x4C, 0x1D, 0x95)),
        (New-Object System.Windows.Point(0.0, 0.0)),
        (New-Object System.Windows.Point(1.0, 1.0)))
    $bgBrush.Freeze()
    $ctx.DrawGeometry($bgBrush, $null, $bgGeom)

    # Lightning bolt (weiß)
    $bolt = [System.Windows.Media.Geometry]::Parse(
        "M 152,32 L 80,140 L 118,140 L 96,224 L 178,108 L 138,108 Z")
    $boltBrush = New-Object System.Windows.Media.SolidColorBrush(([System.Windows.Media.Colors]::White))
    $boltBrush.Freeze()
    $ctx.DrawGeometry($boltBrush, $null, $bolt)

    # Sparkle oben rechts
    $sparkleColor = [System.Windows.Media.Color]::FromArgb(230, 255, 255, 255)
    $sparkleBrush = New-Object System.Windows.Media.SolidColorBrush($sparkleColor)
    $sparkleBrush.Freeze()
    $sparkle1 = [System.Windows.Media.Geometry]::Parse(
        "M 206,50 L 211,68 L 229,72 L 211,76 L 206,94 L 201,76 L 183,72 L 201,68 Z")
    $ctx.DrawGeometry($sparkleBrush, $null, $sparkle1)

    # Sparkle unten links (kleiner)
    $sparkleColor2 = [System.Windows.Media.Color]::FromArgb(204, 255, 255, 255)
    $sparkleBrush2 = New-Object System.Windows.Media.SolidColorBrush($sparkleColor2)
    $sparkleBrush2.Freeze()
    $sparkle2 = [System.Windows.Media.Geometry]::Parse(
        "M 44,196 L 47,209 L 60,212 L 47,215 L 44,228 L 41,215 L 28,212 L 41,209 Z")
    $ctx.DrawGeometry($sparkleBrush2, $null, $sparkle2)

    $ctx.Close()
    return $visual
}

function Render-Png {
    param([int]$Size, [System.Windows.Media.DrawingVisual]$Visual)

    $scale = $Size / 256.0
    $container = New-Object System.Windows.Media.DrawingVisual
    $ctx = $container.RenderOpen()
    $ctx.PushTransform((New-Object System.Windows.Media.ScaleTransform($scale, $scale)))
    $ctx.DrawDrawing($Visual.Drawing)
    $ctx.Pop()
    $ctx.Close()

    $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap(
        $Size, $Size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($container)

    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $frame = [System.Windows.Media.Imaging.BitmapFrame]::Create($rtb)
    $encoder.Frames.Add($frame)
    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    $bytes = $stream.ToArray()
    Write-Host "  rendered $Size x $Size = $($bytes.Length) bytes"
    return ,$bytes
}

# ---- main ----

$visual = New-DrawingVisual

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngs = New-Object 'System.Collections.Generic.Dictionary[int,byte[]]'

foreach ($size in $sizes) {
    $bytes = Render-Png -Size $size -Visual $visual
    $pngs.Add($size, $bytes)
}

$outDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    Write-Host "Created $outDir"
}

$count = $sizes.Count
$headerSize = 6
$entrySize = 16
$dataOffset = $headerSize + ($entrySize * $count)

$fs = [System.IO.File]::Create($OutputPath)
$bw = New-Object System.IO.BinaryWriter($fs)
try {
    $bw.Write([uint16]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]$count)

    $currentOffset = $dataOffset
    foreach ($s in $sizes) {
        $png = $pngs[$s]
        $b = if ($s -ge 256) { [byte]0 } else { [byte]$s }
        $bw.Write([byte]$b)
        $bw.Write([byte]$b)
        $bw.Write([byte]0)
        $bw.Write([byte]0)
        $bw.Write([uint16]1)
        $bw.Write([uint16]32)
        $bw.Write([uint32]$png.Length)
        $bw.Write([uint32]$currentOffset)
        $currentOffset += $png.Length
    }

    foreach ($s in $sizes) {
        $bw.Write($pngs[$s])
    }
}
finally {
    $bw.Dispose()
    $fs.Dispose()
}

$finalSize = (Get-Item $OutputPath).Length
Write-Host "Wrote $OutputPath ($finalSize bytes)"
