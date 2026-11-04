# Draws FanWatch's fan icon and writes a multi-size .ico (PNG-compressed entries).
param([string]$Out = (Join-Path $PSScriptRoot "..\src\FanWatch\fan.ico"))
Add-Type -AssemblyName System.Drawing

function Draw([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $pad = [Math]::Max(1, $size * 0.04)
    $bg = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml("#26282C"))
    $g.FillEllipse($bg, $pad, $pad, $size - 2 * $pad, $size - 2 * $pad)
    $ring = New-Object System.Drawing.Pen ([System.Drawing.ColorTranslator]::FromHtml("#FF6B5E")), ([Math]::Max(1, $size * 0.06))
    $g.DrawEllipse($ring, $pad * 2, $pad * 2, $size - 4 * $pad, $size - 4 * $pad)
    $c = $size / 2.0
    $blade = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml("#E6E6E6"))
    for ($i = 0; $i -lt 3; $i++) {
        $state = $g.Save()
        $g.TranslateTransform($c, $c)
        $g.RotateTransform(120 * $i + 15)
        $g.FillEllipse($blade, -$size * 0.10, -$size * 0.40, $size * 0.20, $size * 0.36)
        $g.Restore($state)
    }
    $hub = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml("#FF6B5E"))
    $r = $size * 0.09
    $g.FillEllipse($hub, $c - $r, $c - $r, 2 * $r, 2 * $r)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    if ($size -ge 256) {
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # Classic DIB entry: BITMAPINFOHEADER (height doubled), 32bpp BGRA bottom-up, then a 1bpp AND mask.
        $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([uint32]40); $bw.Write([int32]$size); $bw.Write([int32]($size * 2))
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
        $maskStride = [int]([Math]::Ceiling($size / 32.0) * 4)
        $bw.Write([uint32]($size * $size * 4 + $maskStride * $size))
        $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $size; $x++) {
                $px = $bmp.GetPixel($x, $y)
                $bw.Write([byte]$px.B); $bw.Write([byte]$px.G); $bw.Write([byte]$px.R); $bw.Write([byte]$px.A)
            }
        }
        $bw.Write((New-Object byte[] ($maskStride * $size)))
        $bw.Flush()
    }
    $bmp.Dispose()
    return ,$ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$images = $sizes | ForEach-Object { ,(Draw $_) }
$fs = [System.IO.File]::Create((Resolve-Path -LiteralPath (Split-Path $Out)).Path + "\" + (Split-Path $Out -Leaf))
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = $images[$i]
    $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s }))); $w.Write([byte]($(if ($s -ge 256) { 0 } else { $s })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$d.Length); $w.Write([uint32]$offset)
    $offset += $d.Length
}
foreach ($d in $images) { $w.Write($d) }
$w.Close()
"wrote $Out"

