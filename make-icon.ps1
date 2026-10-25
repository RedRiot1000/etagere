# Génère src\app.ico (étagère sur fond dégradé violet → cyan) — à lancer une fois avant build.ps1
Add-Type -AssemblyName System.Drawing
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$sizes = 16, 24, 32, 48, 64, 128, 256
function RoundRect($x, $y, $w, $h, $r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure(); $p
}
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $k = $s / 256.0
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $s, $s), ([System.Drawing.Color]::FromArgb(139, 92, 246)), ([System.Drawing.Color]::FromArgb(6, 182, 212))
    $g.FillPath($bg, (RoundRect (8 * $k) (8 * $k) (240 * $k) (240 * $k) (56 * $k)))
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $soft = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(200, 255, 255, 255))
    # deux planches
    $g.FillPath($white, (RoundRect (52 * $k) (122 * $k) (152 * $k) (14 * $k) (7 * $k)))
    $g.FillPath($white, (RoundRect (52 * $k) (198 * $k) (152 * $k) (14 * $k) (7 * $k)))
    # livres / boîtes de jeux
    $g.FillPath($white, (RoundRect (64 * $k) (56 * $k) (26 * $k) (62 * $k) (6 * $k)))
    $g.FillPath($soft, (RoundRect (96 * $k) (70 * $k) (26 * $k) (48 * $k) (6 * $k)))
    $g.FillPath($white, (RoundRect (128 * $k) (62 * $k) (26 * $k) (56 * $k) (6 * $k)))
    $g.FillPath($soft, (RoundRect (78 * $k) (148 * $k) (40 * $k) (46 * $k) (8 * $k)))
    $g.FillPath($white, (RoundRect (126 * $k) (140 * $k) (50 * $k) (54 * $k) (8 * $k)))
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    , $ms.ToArray()
}
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($s % 256)); $w.Write([byte]($s % 256)); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$len); $w.Write([UInt32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
[System.IO.File]::WriteAllBytes((Join-Path $here 'src\app.ico'), $out.ToArray())
[System.IO.File]::WriteAllBytes((Join-Path $here 'src\icon256.png'), $pngs[-1])
