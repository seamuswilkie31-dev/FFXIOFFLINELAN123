<#
.SYNOPSIS
    Draws LocalXI's icon (a glowing crystal with a gold ring on a dark tile) and writes
    src\LocalXI.App\Assets\LocalXI.ico plus a 256px PNG preview.

.DESCRIPTION
    Original artwork drawn with WPF, so it needs Windows PowerShell (powershell.exe -STA).
    Every size is rendered separately; the small ones drop the fine details so they stay crisp.
#>
param([string]$OutDir = (Join-Path $PSScriptRoot '..\src\LocalXI.App\Assets'))

Add-Type -AssemblyName PresentationCore, WindowsBase
$ErrorActionPreference = 'Stop'

function Color([string]$hex) { [System.Windows.Media.ColorConverter]::ConvertFromString($hex) }
function Freeze($o) { $o.Freeze(); $o }
function Linear([string]$from, [string]$to, [double]$x1 = 0, [double]$y1 = 0, [double]$x2 = 0, [double]$y2 = 1) {
    Freeze (New-Object System.Windows.Media.LinearGradientBrush((Color $from), (Color $to),
        (New-Object System.Windows.Point($x1, $y1)), (New-Object System.Windows.Point($x2, $y2))))
}
function Solid([string]$hex) { Freeze (New-Object System.Windows.Media.SolidColorBrush((Color $hex))) }
function Geo([string]$path) { [System.Windows.Media.Geometry]::Parse($path) }

function Render([int]$size) {
    $small = $size -le 32
    $visual = New-Object System.Windows.Media.DrawingVisual
    $dc = $visual.RenderOpen()
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform(($size / 256.0), ($size / 256.0))))

    # Tile
    $tile = New-Object System.Windows.Rect(8, 8, 240, 240)
    $border = New-Object System.Windows.Media.Pen((Solid '#3A5068'), 4)
    $dc.DrawRoundedRectangle((Linear '#24364D' '#0D131B'), $border, $tile, 52, 52)

    # Glow behind the crystal
    $glow = New-Object System.Windows.Media.RadialGradientBrush((Color '#7050C8FF'), (Color '#0050C8FF'))
    $dc.DrawEllipse((Freeze $glow), $null, (New-Object System.Windows.Point(128, 124)), 100, 100)

    # Gold ring: the back half goes behind the crystal, the front half in front of it
    $ringWidth = if ($small) { 16 } else { 11 }
    $ringPen = New-Object System.Windows.Media.Pen((Linear '#F4D98E' '#B8862C' 0 0 1 0), $ringWidth)
    $ringCenter = New-Object System.Windows.Point(128, 152)
    $dc.PushClip((New-Object System.Windows.Media.RectangleGeometry((New-Object System.Windows.Rect(0, 0, 256, 152)))))
    $dc.DrawEllipse($null, $ringPen, $ringCenter, 92, 28)
    $dc.Pop()

    # Crystal: four facets around a ridge, lit from the top left
    $top = '128,22'; $right = '186,124'; $bottom = '128,226'; $left = '70,124'; $mid = '120,124'
    $dc.DrawGeometry((Linear '#D9FBFF' '#7FDDFB'), $null, (Geo "M $top L $left L $mid Z"))
    $dc.DrawGeometry((Linear '#6CCDF7' '#2E9BE0'), $null, (Geo "M $top L $right L $mid Z"))
    $dc.DrawGeometry((Linear '#3FA7EA' '#1F6FC0'), $null, (Geo "M $left L $bottom L $mid Z"))
    $dc.DrawGeometry((Linear '#2479CF' '#123E86'), $null, (Geo "M $right L $bottom L $mid Z"))
    if (-not $small) {
        $edge = New-Object System.Windows.Media.Pen((Solid '#B0F0FBFF'), 3)
        $dc.DrawGeometry($null, $edge, (Geo "M $top L $right L $bottom L $left Z M $top L $mid L $bottom M $left L $mid L $right"))
        # Highlight on the bright facet
        $dc.DrawGeometry((Solid '#90FFFFFF'), $null, (Geo 'M 126,40 L 86,116 L 100,116 Z'))
    }

    $dc.PushClip((New-Object System.Windows.Media.RectangleGeometry((New-Object System.Windows.Rect(0, 152, 256, 104)))))
    $dc.DrawEllipse($null, $ringPen, $ringCenter, 92, 28)
    $dc.Pop()

    # Sparkle
    if ($size -ge 48) {
        $dc.DrawGeometry((Solid '#FFF8E7'), $null, (Geo 'M 192,44 Q 195,60 208,63 Q 195,66 192,82 Q 189,66 176,63 Q 189,60 192,44 Z'))
    }

    $dc.Pop()
    $dc.Close()

    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    $encoder.Save($stream)
    , $stream.ToArray()
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($s in $sizes) { , (Render $s) }

# ICO: a directory of PNG images (supported by Windows since Vista).
$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($ico)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write([byte[]]$image) }
$writer.Flush()

[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'LocalXI.ico'), $ico.ToArray())
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'LocalXI-256.png'), $images[-1])
"Wrote LocalXI.ico ($($sizes -join ', ') px) and LocalXI-256.png to $OutDir"
