# Placeholder card art, 63 x 88 pixel art in the game's palette, saved to Textures/Items
# as art_<set>_<number>.png. Real art can replace these files one by one.
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-card-art.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot "..\Textures\Items"
$W = 63; $H = 88

function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
$OUTLINE = "#292234"
$BAYER = @(@(0, 8, 2, 10), @(12, 4, 14, 6), @(3, 11, 1, 9), @(15, 7, 13, 5))

function Px($b, $x, $y, $hex) { if ($x -ge 0 -and $y -ge 0 -and $x -lt $b.Width -and $y -lt $b.Height) { $b.SetPixel($x, $y, (C $hex)) } }

# Ellipse lit from the top left: three tones picked by position.
function ShadedEllipse($b, $cx, $cy, $rx, $ry, $dark, $mid, $light) {
    for ($y = [Math]::Floor($cy - $ry); $y -le [Math]::Ceiling($cy + $ry); $y++) {
        for ($x = [Math]::Floor($cx - $rx); $x -le [Math]::Ceiling($cx + $rx); $x++) {
            $nx = ($x + 0.5 - $cx) / $rx; $ny = ($y + 0.5 - $cy) / $ry
            if ($nx * $nx + $ny * $ny -gt 1) { continue }
            $l = -($nx + $ny) * 0.7
            Px $b $x $y $(if ($l -gt 0.45) { $light } elseif ($l -lt -0.3) { $dark } else { $mid })
        }
    }
}

function Poly($b, $pts, $hex) {
    for ($y = 0; $y -lt $b.Height; $y++) { for ($x = 0; $x -lt $b.Width; $x++) {
        $inside = $false; $px = $x + 0.5; $py = $y + 0.5
        $j = $pts.Count - 1
        for ($i = 0; $i -lt $pts.Count; $i++) {
            $xi = $pts[$i][0]; $yi = $pts[$i][1]; $xj = $pts[$j][0]; $yj = $pts[$j][1]
            if ((($yi -gt $py) -ne ($yj -gt $py)) -and ($px -lt ($xj - $xi) * ($py - $yi) / ($yj - $yi) + $xi)) { $inside = -not $inside }
            $j = $i
        }
        if ($inside) { Px $b $x $y $hex }
    } }
}

# Outlines a transparent figure layer the way the game's sprites are outlined: one pixel,
# outside the shape, in the outline colour.
function OutlineOutside($b) {
    $edge = New-Object System.Collections.Generic.List[object]
    for ($y = 0; $y -lt $b.Height; $y++) { for ($x = 0; $x -lt $b.Width; $x++) {
        if ($b.GetPixel($x, $y).A -ne 0) { continue }
        foreach ($d in @(@(1, 0), @(-1, 0), @(0, 1), @(0, -1))) {
            $nx = $x + $d[0]; $ny = $y + $d[1]
            if ($nx -ge 0 -and $ny -ge 0 -and $nx -lt $b.Width -and $ny -lt $b.Height -and $b.GetPixel($nx, $ny).A -ne 0) { $edge.Add(@($x, $y)); break }
        }
    } }
    foreach ($p in $edge) { Px $b $p[0] $p[1] $OUTLINE }
}

function Composite($dst, $src) {
    for ($y = 0; $y -lt $H; $y++) { for ($x = 0; $x -lt $W; $x++) {
        $c = $src.GetPixel($x, $y); if ($c.A -ne 0) { $dst.SetPixel($x, $y, $c) }
    } }
}

# ================================================================ #01 Rust Rat
# A cyber-eyed rat on top of a junk pile at dusk, chewing a bolt.
$bg = New-Object System.Drawing.Bitmap $W, $H

# dusk sky, dithered bands
$sky = @("#242227", "#2f2b3a", "#392e3c", "#4a2f38", "#64343b")
for ($y = 0; $y -lt $H; $y++) { for ($x = 0; $x -lt $W; $x++) {
    $t = [Math]::Min(1.0, $y / 60.0) * ($sky.Count - 1)
    $i = [Math]::Min($sky.Count - 1, [Math]::Floor($t + $BAYER[$y % 4][$x % 4] / 16.0))
    Px $bg $x $y $sky[$i]
} }
# low orange sun with a haze ring
for ($y = 0; $y -lt $H; $y++) { for ($x = 0; $x -lt $W; $x++) {
    $d = [Math]::Sqrt(($x - 44) * ($x - 44) + ($y - 30) * ($y - 30))
    if ($d -lt 7) { Px $bg $x $y $(if ($x + $y -lt 70) { "#d59f75" } else { "#cc7755" }) }
    elseif ($d -lt 9 -and (($x + $y) % 2 -eq 0)) { Px $bg $x $y "#9b4a3d" }
} }
# distant junk skyline: crane, stacked cars, girders
Poly $bg @(@(0, 52), @(6, 47), @(10, 49), @(14, 44), @(20, 46), @(26, 43), @(30, 47), @(38, 45), @(44, 48), @(50, 44), @(56, 46), @(63, 42), @(63, 64), @(0, 64)) "#303a41"
for ($y = 22; $y -le 44; $y++) { Px $bg 12 $y "#303a41" }          # crane mast
for ($x = 12; $x -le 26; $x++) { Px $bg $x 22 "#303a41" }          # crane arm
for ($y = 23; $y -le 30; $y++) { Px $bg 25 $y "#303a41" }          # cable
Px $bg 24 31 "#303a41"; Px $bg 25 31 "#303a41"; Px $bg 26 31 "#303a41"

# foreground junk pile
$pile = New-Object System.Drawing.Bitmap $W, $H
Poly $pile @(@(0, 66), @(8, 60), @(18, 58), @(30, 59), @(42, 57), @(52, 60), @(63, 63), @(63, 88), @(0, 88)) "#44464c"
Poly $pile @(@(0, 72), @(12, 66), @(26, 68), @(40, 65), @(63, 70), @(63, 88), @(0, 88)) "#303a41"
for ($x = 9; $x -le 50; $x += 1) { if (($x * 7) % 5 -lt 2) { Px $pile $x (60 + ($x % 3)) "#5f6868" } }
# tyre
ShadedEllipse $pile 12 74 7 5 "#242227" "#303a41" "#44464c"
ShadedEllipse $pile 12 74 3 2 "#2f2b3a" "#2f2b3a" "#2f2b3a"
# rusty can
for ($y = 66; $y -le 75; $y++) { for ($x = 44; $x -le 50; $x++) { Px $pile $x $y $(if ($x -le 45) { "#cc7755" } elseif ($x -ge 49) { "#853737" } else { "#9c3738" }) } }
for ($x = 44; $x -le 50; $x++) { Px $pile $x 66 "#d5d2b8"; Px $pile $x 70 "#64343b" }
# pipe
for ($x = 24; $x -le 38; $x++) { Px $pile $x 77 "#80929b"; Px $pile $x 78 "#5f6868"; Px $pile $x 79 "#44464c" }
Px $pile 38 77 "#d5d2b8"
# loose bolts
foreach ($p in @(@(20, 63), @(35, 62), @(55, 68), @(29, 84), @(50, 82))) { Px $pile $p[0] $p[1] "#a8aea5"; Px $pile ($p[0] + 1) $p[1] "#80929b" }
OutlineOutside $pile
Composite $bg $pile

# the rat
$rat = New-Object System.Drawing.Bitmap $W, $H
$FUR_D = "#4f464b"; $FUR = "#79746e"; $FUR_L = "#a8aea5"; $PINK = "#ad696b"
# tail: a curling line from the rump
$tail = @(@(17, 55), @(14, 54), @(11, 52), @(9, 49), @(8, 46), @(9, 43), @(11, 41), @(13, 41))
foreach ($p in $tail) { Px $rat $p[0] $p[1] $PINK }
Px $rat 16 56 $PINK; Px $rat 15 55 $PINK
# body and haunch
ShadedEllipse $rat 28 52 12 6.5 $FUR_D $FUR $FUR_L
ShadedEllipse $rat 21 53 6 5 $FUR_D $FUR $FUR_L
# head with a pointed snout
ShadedEllipse $rat 41 48 6.5 5 $FUR_D $FUR $FUR_L
Poly $rat @(@(44, 45), @(52, 49), @(44, 52)) $FUR
Px $rat 52 49 $PINK; Px $rat 51 49 $PINK
# ear
ShadedEllipse $rat 38 42 3 3 $FUR_D $FUR $FUR_L
Px $rat 38 42 $PINK; Px $rat 38 43 $PINK
# cyber augment: steel plate over the eye, glowing red lens
for ($x = 40; $x -le 46; $x++) { Px $rat $x 45 "#80929b"; Px $rat $x 46 "#5f6868" }
Px $rat 40 45 "#d5d2b8"; Px $rat 41 45 "#d5d2b8"
Px $rat 44 47 "#e0505a"; Px $rat 45 47 "#ff8a80"
# whiskers
Px $rat 49 47 $FUR_L; Px $rat 50 46 $FUR_L; Px $rat 49 51 $FUR_L; Px $rat 50 52 $FUR_L
# bolt in the mouth
Px $rat 52 51 "#a8aea5"; Px $rat 53 51 "#80929b"; Px $rat 53 52 "#80929b"; Px $rat 52 52 "#5f6868"
# feet
foreach ($fx in @(20, 25, 33, 38)) { Px $rat $fx 58 $FUR_D; Px $rat ($fx + 1) 58 $PINK }
OutlineOutside $rat
Composite $bg $rat
# glint off the lens, drawn after the outline so it can sit outside the head
Px $bg 46 46 "#ff8a80"

$bg.Save((Join-Path $out "art_scrapyard_01.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$bg.Dispose(); $pile.Dispose(); $rat.Dispose()
Write-Host "Card art written to $out"
