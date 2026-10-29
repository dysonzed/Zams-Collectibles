# Generates the item sprites in Textures/Items, in the game's own pixel style:
# 16 px per inventory cell, a #292234 outline, muted 4-tone ramps lit from the top left,
# and a transparent margin so items sit inside their cells like the vanilla ones.
# The cell centres must stay opaque: the game derives an item's shape from them.
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-placeholder-sprites.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot "..\Textures\Items"
New-Item -ItemType Directory -Force $out | Out-Null

function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

# Colours sampled from the game's own item sprites.
$OUTLINE = "#292234"
$CREAM = "#d5d2b8"; $CREAM_DARK = "#aaa982"
$SLATE = @{ k = "#303a41"; m = "#44464c"; n = "#5f6868"; s = "#80929b" }
# Ramps go dark (1) to light (4).
$RAMPS = @{
    red    = @("#64343b", "#853737", "#9c3738", "#cc7755")
    teal   = @("#275654", "#2f756f", "#45918b", "#67aaa5")
    purple = @("#453943", "#6b5269", "#8c6a95", "#a272ac")
}

function Save($bmp, $name) {
    $bmp.Save((Join-Path $out "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# Draws an ASCII template. '.' is transparent, 'o' outline, 'w'/'c' cream and dark cream,
# 's'/'g' slate light/dark, '1'-'4' the given ramp.
function FromTemplate($rows, $width, $ramp) {
    $map = @{ o = $OUTLINE; w = $CREAM; c = $CREAM_DARK; s = $SLATE.s; g = $SLATE.n }
    for ($i = 0; $i -lt 4; $i++) { $map["$($i + 1)"] = $ramp[$i] }
    $bmp = New-Object System.Drawing.Bitmap $width, $rows.Count
    for ($y = 0; $y -lt $rows.Count; $y++) {
        if ($rows[$y].Length -ne $width) { throw "row $y is $($rows[$y].Length) wide, expected $width" }
        for ($x = 0; $x -lt $width; $x++) {
            $ch = [string]$rows[$y][$x]
            if ($ch -ne ".") { $bmp.SetPixel($x, $y, (C $map[$ch])) }
        }
    }
    return $bmp
}

# ---------------------------------------------------------------- card backs (1x1)
$card = @(
    "................",
    "....oooooooo....",
    "...owwwwwwwwo...",
    "...ow443332co...",
    "...ow433322co...",
    "...ow333222co...",
    "...ow33ww22co...",
    "...ow3w11w2co...",
    "...ow3w11w2co...",
    "...ow33ww21co...",
    "...ow322221co...",
    "...ow222211co...",
    "...ow222111co...",
    "...owccccccco...",
    "....oooooooo....",
    "................"
)
# Condition variants of a card back. The game picks one from the card's condition:
# pristine (no suffix), _light, _worn, _ruined. Same damage on every copy, since a 16 px icon
# has no room for more; the card viewer shows the per-copy wear.
function FadeBmp($b, $amount) {
    $o = C $OUTLINE
    for ($y = 0; $y -lt $b.Height; $y++) { for ($x = 0; $x -lt $b.Width; $x++) {
        $c = $b.GetPixel($x, $y)
        if ($c.A -eq 0 -or ($c.R -eq $o.R -and $c.G -eq $o.G -and $c.B -eq $o.B)) { continue }
        $g = $c.R * 0.3 + $c.G * 0.59 + $c.B * 0.11
        $r = [int]($c.R + ($g * 1.05 - $c.R) * $amount); $gg = [int]($c.G + ($g - $c.G) * $amount); $bb = [int]($c.B + ($g * 0.88 - $c.B) * $amount)
        $b.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, [Math]::Min(255, $r), [Math]::Min(255, $gg), [Math]::Min(255, $bb)))
    } }
}
# Sets pixels from a flat list of x,y pairs; no colour clears them.
function SetPx($b, $hex, [int[]]$xy) {
    for ($i = 0; $i -lt $xy.Count; $i += 2) {
        $col = if ($hex) { C $hex } else { [System.Drawing.Color]::Transparent }
        $b.SetPixel($xy[$i], $xy[$i + 1], $col)
    }
}

function CardBack($name, $ramp) {
    Save (FromTemplate $card 16 $ramp) $name

    # light: two rubbed corners
    $b = FromTemplate $card 16 $ramp
    SetPx $b "#e8e4d6" 10,3,5,12
    Save $b "$($name)_light"

    # worn: every corner rubbed, a crease, a nicked edge, faded colours
    $b = FromTemplate $card 16 $ramp
    FadeBmp $b 0.3
    SetPx $b "#e8e4d6" 5,3,10,3,5,12,10,12
    SetPx $b "#cfc8b0" 5,10,6,9,7,8,8,7,9,6,10,5
    SetPx $b $null 12,9
    SetPx $b $OUTLINE 11,9
    Save $b "$($name)_worn"

    # ruined: heavily faded, torn top-right corner, stained, creased
    $b = FromTemplate $card 16 $ramp
    FadeBmp $b 0.6
    SetPx $b "#e8e4d6" 5,3,5,12,10,12,6,12
    SetPx $b "#cfc8b0" 5,10,6,9,7,8,8,7,9,6,10,5
    SetPx $b $null 10,1,11,1,12,2,12,3,12,4
    SetPx $b $OUTLINE 10,2,11,2,11,3,11,4
    SetPx $b "#6b523a" 6,7,7,6,6,6
    SetPx $b $null 3,7
    SetPx $b $OUTLINE 4,7
    Save $b "$($name)_ruined"
}

CardBack "card_back_red" $RAMPS.red
CardBack "card_back_blue" $RAMPS.teal

# ---------------------------------------------------------------- booster packs (1x2)
# Foil pack with crimped ends, a cream logo plate and a shine stripe on the left.
$pack = @(
    "................",
    "..oooooooooooo..",
    "..owswswswswso..",
    "..osgsgsgsgsgo..",
    "..oooooooooooo..",
    "..o4433333221o..",
    "..o4w3333322to..",
    "..o4w3333322to..",
    "..o4oooooooo1o..",
    "..o4owwwwwwo1o..",
    "..o4oww22wwo1o..",
    "..o4ow2442wo1o..",
    "..o4oww22wwo1o..",
    "..o4occcccco1o..",
    "..o4oooooooo1o..",
    "..o4333333221o..",
    "..o4w33333221o..",
    "..o4w33333221o..",
    "..o1111111111o..",
    "..o4w33333221o..",
    "..o4333333221o..",
    "..o4333333221o..",
    "..o3333332211o..",
    "..o3333322211o..",
    "..o3322222111o..",
    "..o2222221111o..",
    "..oooooooooooo..",
    "..owswswswswso..",
    "..osgsgsgsgsgo..",
    "..oooooooooooo..",
    "................",
    "................"
) | ForEach-Object { $_ -replace "t", "1" }
Save (FromTemplate $pack 16 $RAMPS.purple) "booster_pack"
Save (FromTemplate $pack 16 $RAMPS.red) "booster_pack_red"
Save (FromTemplate $pack 16 $RAMPS.teal) "booster_pack_blue"

# ---------------------------------------------------------------- boxes (2x2), drawn from shapes

function NewBmp($w, $h) { New-Object System.Drawing.Bitmap $w, $h }
function Rect($b, $x0, $y0, $x1, $y1, $hex) {
    $col = C $hex
    for ($y = $y0; $y -le $y1; $y++) { for ($x = $x0; $x -le $x1; $x++) { $b.SetPixel($x, $y, $col) } }
}
function Px($b, $x, $y, $hex) { $b.SetPixel($x, $y, (C $hex)) }
function ClearPx($b, $x, $y) { $b.SetPixel($x, $y, [System.Drawing.Color]::Transparent) }

# Turns every opaque pixel that touches a transparent one (or the image edge) into outline.
function OutlinePass($b) {
    $edge = New-Object System.Collections.Generic.List[object]
    for ($y = 0; $y -lt $b.Height; $y++) { for ($x = 0; $x -lt $b.Width; $x++) {
        if ($b.GetPixel($x, $y).A -eq 0) { continue }
        foreach ($d in @(@(1, 0), @(-1, 0), @(0, 1), @(0, -1))) {
            $nx = $x + $d[0]; $ny = $y + $d[1]
            if ($nx -lt 0 -or $ny -lt 0 -or $nx -ge $b.Width -or $ny -ge $b.Height -or $b.GetPixel($nx, $ny).A -eq 0) { $edge.Add(@($x, $y)); break }
        }
    } }
    foreach ($p in $edge) { Px $b $p[0] $p[1] $OUTLINE }
}

# Display case: slate lid on top, a glass front showing four cards, a cream label plate.
function DrawCase($b) {
    $t = $RAMPS.teal; $r = $RAMPS.red
    Rect $b 2 4 29 9 $SLATE.n          # lid
    Rect $b 3 5 28 5 $SLATE.s          # lid highlight
    Rect $b 2 9 29 9 $SLATE.k          # lid lip
    Rect $b 2 10 29 29 $SLATE.m        # front
    Rect $b 3 10 3 28 $SLATE.n         # lit left edge
    Rect $b 28 10 28 28 $SLATE.k       # shaded right edge
    Rect $b 3 28 28 28 $SLATE.k        # shaded bottom

    Rect $b 5 12 26 22 $SLATE.k        # window frame
    Rect $b 6 13 25 21 $t[0]           # glass
    $cards = @(@(8, $r), @(13, $t), @(18, $r), @(23, $t))
    foreach ($cd in $cards) {
        $x = $cd[0]; $ramp = $cd[1]
        Rect $b ($x - 1) 15 ($x + 1) 21 $ramp[2]
        Rect $b ($x - 1) 15 ($x + 1) 15 $CREAM
        Px $b ($x - 1) 16 $ramp[3]
        Px $b $x 18 $CREAM
    }
    # glass reflections
    foreach ($i in 0..4) { Px $b (9 + $i) (17 - $i) $t[3]; Px $b (10 + $i) (17 - $i) $t[3] }
    foreach ($i in 0..2) { Px $b (19 + $i) (21 - $i) $t[3] }
    Rect $b 6 22 25 22 $SLATE.s        # sill highlight

    Rect $b 11 24 20 26 $CREAM         # label plate
    Rect $b 11 26 20 26 $CREAM_DARK
    Rect $b 13 25 18 25 $SLATE.n       # label text

    foreach ($p in @(@(2, 4), @(29, 4), @(2, 29), @(29, 29))) { ClearPx $b $p[0] $p[1] }
}

$b = NewBmp 32 32
DrawCase $b
OutlinePass $b
Save $b "collector_box"

# Sealed set: the same case wrapped with an orange band and a seal sticker.
$b = NewBmp 32 32
DrawCase $b
$o = $RAMPS.red
Rect $b 14 4 17 29 $o[3]
Rect $b 14 4 14 29 $CREAM
Rect $b 17 4 17 29 $o[2]
Rect $b 13 15 18 20 $CREAM         # sticker
Rect $b 14 14 17 21 $CREAM
Rect $b 14 16 17 19 $o[1]
Rect $b 15 16 16 16 $o[3]
Rect $b 14 21 17 21 $CREAM_DARK
foreach ($i in 0..3) { Px $b (5 + $i) (11 - $i) $CREAM }   # shrink-wrap glint
OutlinePass $b
Save $b "complete_set"

# ---------------------------------------------------------------- booster boxes (2x2), per set colour
# Sealed: a shrink-wrapped box in the set's colour with a cream label.
# Opened: the same box with its flaps up and the crimped tops of the packs showing.
function BoosterBox($ramp, $opened) {
    $b = NewBmp 32 32
    Rect $b 3 6 28 29 $ramp[2]                     # front
    Rect $b 4 10 4 28 $ramp[3]                     # lit left edge
    Rect $b 27 10 27 28 $ramp[1]                   # shaded right edge
    Rect $b 4 28 27 28 $ramp[1]                    # shaded bottom
    Rect $b 6 14 25 21 $CREAM                      # label
    Rect $b 6 21 25 21 $CREAM_DARK
    Rect $b 8 16 11 16 $ramp[0]; Rect $b 8 18 10 18 $SLATE.n
    foreach ($d in 0..2) { Rect $b (17 - $d) (15 + $d) (18 + $d) (15 + $d) $ramp[1]; Rect $b (17 - $d) (20 - $d) (18 + $d) (20 - $d) $ramp[1] }   # logo diamond
    Px $b 17 17 $ramp[3]; Px $b 18 17 $ramp[3]
    if ($opened) {
        Rect $b 4 3 27 11 $ramp[0]                 # inside of the box
        for ($x = 5; $x -le 25; $x += 3) {          # crimped pack tops
            Rect $b $x 5 ($x + 1) 10 "#8d8da0"
            Px $b $x 5 "#c9c9d6"; Px $b $x 7 "#c9c9d6"; Px $b $x 9 "#c9c9d6"
        }
        Rect $b 5 0 26 2 $ramp[2]                  # back flap, up
        Rect $b 0 4 2 9 $ramp[3]                   # side flaps, open
        Rect $b 29 4 31 9 $ramp[1]
    }
    else {
        Rect $b 3 3 28 9 $ramp[3]                  # lid
        Rect $b 3 9 28 9 $ramp[1]                  # lid seam
        foreach ($i in 0..4) { Px $b (6 + $i) (8 - $i) $CREAM }      # shrink-wrap glints
        foreach ($i in 0..2) { Px $b (22 + $i) (26 - $i) $CREAM }
        foreach ($p in @(@(3, 3), @(28, 3))) { ClearPx $b $p[0] $p[1] }
    }
    foreach ($p in @(@(3, 29), @(28, 29))) { ClearPx $b $p[0] $p[1] }
    OutlinePass $b
    return $b
}
foreach ($c in @("red", "teal", "purple")) {
    Save (BoosterBox $RAMPS[$c] $false) "booster_box_$c"
    Save (BoosterBox $RAMPS[$c] $true) "booster_box_open_$c"
}

# ---------------------------------------------------------------- collection binder (2x2)
# A closed ring binder: slate cover, spine with rings on the left, cream label, page edges.
$b = NewBmp 32 32
Rect $b 4 2 27 29 $SLATE.n                         # cover
Rect $b 5 3 26 3 $SLATE.s                          # lit top edge
Rect $b 26 4 26 28 $SLATE.k                        # shaded right edge
Rect $b 5 28 26 28 $SLATE.k
Rect $b 27 4 28 28 $CREAM                          # page edges, sticking out
foreach ($y in 6..26) { if ($y % 3 -eq 0) { Px $b 28 $y $CREAM_DARK } }
Rect $b 4 2 8 29 $SLATE.k                          # spine
Rect $b 5 3 5 28 $SLATE.m
foreach ($ry in @(7, 15, 23)) { Rect $b 6 $ry 9 ($ry + 1) $SLATE.s; Px $b 9 $ry $CREAM }   # rings
Rect $b 12 8 23 15 $CREAM                          # label
Rect $b 12 15 23 15 $CREAM_DARK
Rect $b 14 10 20 10 $RAMPS.teal[1]; Rect $b 14 12 18 12 $SLATE.n
foreach ($d in 0..1) { Rect $b (17 - $d) (20 + $d) (18 + $d) (20 + $d) $RAMPS.teal[2]; Rect $b (17 - $d) (23 - $d) (18 + $d) (23 - $d) $RAMPS.teal[2] }   # emblem
foreach ($p in @(@(4, 2), @(4, 29))) { ClearPx $b $p[0] $p[1] }
OutlinePass $b
Save $b "card_binder"

# ---------------------------------------------------------------- deck box (1x1)
# A small tuck box with an open lid and the top of a deck showing.
$b = NewBmp 16 16
Rect $b 3 4 12 14 $RAMPS.purple[2]                 # box
Rect $b 3 4 3 14 $RAMPS.purple[3]
Rect $b 12 4 12 14 $RAMPS.purple[1]
Rect $b 4 2 11 4 $CREAM                            # cards peeking out
Rect $b 4 4 11 4 $CREAM_DARK
Rect $b 5 8 10 10 $CREAM                           # label
Rect $b 6 9 9 9 $RAMPS.red[2]
Rect $b 3 13 12 13 $RAMPS.purple[1]
OutlinePass $b
Save $b "deck_box"

# ---------------------------------------------------------------- art placeholder (card viewer)
# Card art is 63 x 88 (5:7), shown at 3x. Cards without their own art show this.
$b = NewBmp 63 88
Rect $b 0 0 62 87 "#242227"
for ($x = 0; $x -lt 63; $x += 8) { Rect $b $x 0 $x 87 "#343434" }
for ($y = 0; $y -lt 88; $y += 8) { Rect $b 0 $y 62 $y "#343434" }
$cx = 31; $cy = 43
for ($y = -14; $y -le 14; $y++) { for ($x = -14; $x -le 14; $x++) {
    $d = [Math]::Abs($x) + [Math]::Abs($y)
    if ($d -le 14) { Px $b ($cx + $x) ($cy + $y) $(if ($d -ge 12) { $OUTLINE } elseif ($d -ge 9) { $SLATE.n } else { $SLATE.k }) }
} }
Rect $b ($cx - 1) ($cy - 5) ($cx + 1) ($cy + 2) $SLATE.s
Rect $b ($cx - 1) ($cy + 4) ($cx + 1) ($cy + 5) $SLATE.s
Save $b "art_placeholder"

Write-Host "Sprites written to $out"
