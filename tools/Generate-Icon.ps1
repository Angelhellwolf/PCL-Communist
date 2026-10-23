#Requires -PSEdition Desktop
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName WindowsBase, PresentationCore, PresentationFramework
$imageRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'Plain Craft Launcher 2\Images'
$redRoot = Join-Path $imageRoot 'Red'
New-Item -ItemType Directory -Path $redRoot -Force | Out-Null

# Render native WPF vector geometry at every icon resolution.
$symbol = [Windows.Media.FormattedText]::new([string][char]0x262D, [Globalization.CultureInfo]::InvariantCulture,
    [Windows.FlowDirection]::LeftToRight, [Windows.Media.Typeface]::new('Segoe UI Symbol'), 200,
    [Windows.Media.Brushes]::White, 1.0)
$emblem = $symbol.BuildGeometry([Windows.Point]::new(0, 0))
$bounds = $emblem.Bounds
$emblemScale = [Math]::Min(168 / $bounds.Width, 168 / $bounds.Height)
$emblem.Transform = [Windows.Media.MatrixTransform]::new($emblemScale, 0, 0, $emblemScale,
    128 - ($bounds.X + $bounds.Width / 2) * $emblemScale,
    147 - ($bounds.Y + $bounds.Height / 2) * $emblemScale)
$emblem.Freeze()
$star = [Windows.Media.Geometry]::Parse('M128,28 L132.3,39.2 L144.3,39.8 L134.9,47.4 L138,59 L128,52.5 L118,59 L121.1,47.4 L111.7,39.8 L123.7,39.2 Z')
$red = [Windows.Media.LinearGradientBrush]::new([Windows.Media.ColorConverter]::ConvertFromString('#CE1D2E'), [Windows.Media.ColorConverter]::ConvertFromString('#860A17'), 90)
$gold = [Windows.Media.LinearGradientBrush]::new([Windows.Media.ColorConverter]::ConvertFromString('#FFE69D'), [Windows.Media.ColorConverter]::ConvertFromString('#E6AF43'), 90)
$rim = [Windows.Media.Pen]::new([Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString('#C9E0B566')), 2)

function Get-IconPng([int]$Size) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    $context.PushTransform([Windows.Media.ScaleTransform]::new($Size / 256.0, $Size / 256.0))
    $context.DrawRoundedRectangle($red, $null, [Windows.Rect]::new(0, 0, 256, 256), 48, 48)
    if ($Size -ge 48) {
        $context.DrawRoundedRectangle($null, $rim, [Windows.Rect]::new(13, 13, 230, 230), 37, 37)
    }
    $context.DrawGeometry($gold, $null, $star)
    $context.DrawGeometry($gold, $null, $emblem)
    $context.Pop()
    $context.Close()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($Size, $Size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    try { $encoder.Save($stream); return ,$stream.ToArray() }
    finally { $stream.Dispose() }
}

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = @($sizes | ForEach-Object { ,(Get-IconPng $_) })
$iconStream = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($iconStream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
    [IO.File]::WriteAllBytes((Join-Path $imageRoot 'icon.ico'), $iconStream.ToArray())
} finally { $writer.Dispose(); $iconStream.Dispose() }
[IO.File]::WriteAllBytes((Join-Path $redRoot 'Splash.png'), $frames[-1])
Write-Output 'Created red-and-gold hammer-and-sickle splash and 7-resolution application icon.'
