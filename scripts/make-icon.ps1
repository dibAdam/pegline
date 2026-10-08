# Draws the Pegline icon in code and writes a multi-size .ico plus a PNG for
# the README. A teal tile, one white card with screenshot crop marks, held on a
# line by a wooden peg.
# Usage: powershell -ExecutionPolicy Bypass -File scripts\make-icon.ps1
param(
    [string]$Ico = (Join-Path $PSScriptRoot '..\src\Pegline\Assets\Pegline.ico'),
    [string]$Png = (Join-Path $PSScriptRoot '..\docs\icon.png')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-Rounded([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function C([int]$a, [int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }

# Draws on a 1024 unit canvas. $detailed adds the line and the crop marks,
# which turn to mush below 40 pixels.
function Draw-Icon([int]$size, [bool]$detailed) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.InterpolationMode = 'HighQualityBicubic'
    $u = $size / 1024.0
    $g.ScaleTransform($u, $u)

    # Tile
    $tile = New-Rounded 72 72 880 880 196
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 72, 72), (New-Object System.Drawing.PointF 952, 952), (C 255 45 212 191), (C 255 14 116 144)
    $g.FillPath($fill, $tile)
    $g.SetClip($tile)
    $sheen = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 72), (New-Object System.Drawing.PointF 0, 560), (C 70 255 255 255), (C 0 255 255 255)
    $g.FillRectangle($sheen, 72, 72, 880, 488)

    # The line, sagging a little
    if ($detailed) {
        $rope = New-Object System.Drawing.Pen (C 150 8 47 56), 16
        $rope.StartCap = 'Round'; $rope.EndCap = 'Round'
        $g.DrawBezier($rope, 40, 300, 360, 392, 664, 392, 984, 300)
    }
    $g.ResetClip()

    # The card, tilted, with a soft shadow
    $state = $g.Save()
    $g.TranslateTransform(512, 352)
    $g.RotateTransform(-5)
    $cw = 540; $ch = 430
    if (-not $detailed) { $cw = 600; $ch = 470 }
    for ($i = 0; $i -lt 10; $i++) {
        $s = New-Rounded (-$cw / 2 - $i * 4) (18 + $i * 3) ($cw + $i * 8) ($ch + $i * 6) (56 + $i * 4)
        $g.FillPath((New-Object System.Drawing.SolidBrush (C 9 4 40 50)), $s)
    }
    $card = New-Rounded (-$cw / 2) 0 $cw $ch 56
    $paper = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $ch), (C 255 255 255 255), (C 255 236 246 248)
    $g.FillPath($paper, $card)

    # Crop marks: the screenshot inside the card
    $mark = New-Object System.Drawing.Pen (C 255 14 116 144), 30
    $mark.StartCap = 'Round'; $mark.EndCap = 'Round'; $mark.LineJoin = 'Round'
    $in = 86; $len = 96
    if (-not $detailed) { $in = 92; $len = 120; $mark.Width = 46 }
    $l = -$cw / 2 + $in; $r = $cw / 2 - $in; $t = $in + 20; $b = $ch - $in
    $g.DrawLines($mark, @((New-Object System.Drawing.PointF $l, ($t + $len)), (New-Object System.Drawing.PointF $l, $t), (New-Object System.Drawing.PointF ($l + $len), $t)))
    $g.DrawLines($mark, @((New-Object System.Drawing.PointF ($r - $len), $t), (New-Object System.Drawing.PointF $r, $t), (New-Object System.Drawing.PointF $r, ($t + $len))))
    $g.DrawLines($mark, @((New-Object System.Drawing.PointF $r, ($b - $len)), (New-Object System.Drawing.PointF $r, $b), (New-Object System.Drawing.PointF ($r - $len), $b)))
    $g.DrawLines($mark, @((New-Object System.Drawing.PointF ($l + $len), $b), (New-Object System.Drawing.PointF $l, $b), (New-Object System.Drawing.PointF $l, ($b - $len))))
    $g.Restore($state)

    # The wooden peg, gripping the card and the line
    $pw = 84; $ph = 232
    if (-not $detailed) { $pw = 112; $ph = 250 }
    $px = 512 - $pw / 2; $py = 214
    for ($i = 0; $i -lt 6; $i++) {
        $s = New-Rounded ($px - $i * 3 + 6) ($py + 10 + $i * 2) ($pw + $i * 6) ($ph + $i * 4) (30 + $i * 3)
        $g.FillPath((New-Object System.Drawing.SolidBrush (C 14 60 30 0)), $s)
    }
    $peg = New-Rounded $px $py $pw $ph 30
    $wood = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF $px, 0), (New-Object System.Drawing.PointF ($px + $pw), 0), (C 255 253 186 116), (C 255 234 108 24)
    $g.FillPath($wood, $peg)
    $split = New-Object System.Drawing.Pen (C 170 154 52 18), 7
    $g.DrawLine($split, 512, $py + 22, 512, $py + $ph - 22)
    # The spring
    $band = New-Rounded ($px - 8) ($py + $ph * 0.42) ($pw + 16) 34 14
    $steel = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, ($py + $ph * 0.42)), (New-Object System.Drawing.PointF 0, ($py + $ph * 0.42 + 34)), (C 255 236 240 243), (C 255 140 152 160)
    $g.FillPath($steel, $band)

    $g.Dispose()
    return $bmp
}

function Render([int]$size) {
    $detailed = $size -ge 40
    $work = [Math]::Max(256, $size * 4)
    $big = Draw-Icon $work $detailed
    $small = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($small)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.CompositingQuality = 'HighQuality'
    $g.DrawImage($big, 0, 0, $size, $size)
    $g.Dispose(); $big.Dispose()
    return $small
}

function PngBytes($bmp) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = @()
foreach ($s in $sizes) { $b = Render $s; $images += , (PngBytes $b); $b.Dispose() }

New-Item -ItemType Directory -Force -Path (Split-Path $Ico) | Out-Null
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Resolve-Path (Split-Path $Ico)).Path + '\' + (Split-Path $Ico -Leaf), $out.ToArray())

New-Item -ItemType Directory -Force -Path (Split-Path $Png) | Out-Null
$preview = Render 512
$preview.Save(((Resolve-Path (Split-Path $Png)).Path + '\' + (Split-Path $Png -Leaf)), [System.Drawing.Imaging.ImageFormat]::Png)
$small = Render 32
$small.Save(((Resolve-Path (Split-Path $Png)).Path + '\icon-32.png'), [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Wrote $Ico and $Png"
