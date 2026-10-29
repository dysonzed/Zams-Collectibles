# Builds preview.png (the Mods menu banner and Steam Workshop thumbnail, must stay under 1 MB)
# from the item sprites: composed at 160 x 90 game pixels, scaled 4x with hard pixels.
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-preview.ps1
Add-Type -AssemblyName System.Drawing
$root = Join-Path $PSScriptRoot ".."
$items = Join-Path $root "Textures\Items"
$W = 160; $H = 90; $SCALE = 4

function C($hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

$b = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($b)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half

# inventory grid background, like the game's storage windows
$g.Clear((C "#242227"))
$line = New-Object System.Drawing.SolidBrush (C "#343434")
for ($x = 0; $x -lt $W; $x += 16) { $g.FillRectangle($line, $x, 0, 1, $H) }
for ($y = 0; $y -lt $H; $y += 16) { $g.FillRectangle($line, 0, $y, $W, 1) }

function Put($name, $x, $y) {
    $im = [System.Drawing.Image]::FromFile((Join-Path $items "$name.png"))
    $g.DrawImage($im, $x, $y, $im.Width, $im.Height)
    $im.Dispose()
}

# card art in a cream frame on the right
$art = [System.Drawing.Image]::FromFile((Join-Path $items "den_scene.png"))   # Downside Dan
$frame = New-Object System.Drawing.SolidBrush (C "#d5d2b8")
$outline = New-Object System.Drawing.SolidBrush (C "#292234")
$g.FillRectangle($outline, 103, 6, 51, 78)
$g.FillRectangle($frame, 104, 7, 49, 76)
$g.DrawImage($art, (New-Object System.Drawing.Rectangle 106, 9, 45, 63), (New-Object System.Drawing.Rectangle 420, 100, 90, 126), [System.Drawing.GraphicsUnit]::Pixel)
$g.FillRectangle($outline, 106, 74, 45, 1)
$g.FillRectangle($outline, 106, 77, 30, 1)
$art.Dispose()

# products on the grid
Put "collector_box" 0 40
Put "booster_pack_red" 40 40
Put "booster_pack_blue" 56 40
Put "complete_set" 72 48
Put "card_back_red" 0 72
Put "card_back_blue" 16 72
Put "card_back_red_worn" 32 72

# title, drawn crisp at 1x so it scales up as pixel text
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::SingleBitPerPixelGridFit
$title = New-Object System.Drawing.Font "Segoe UI", 11, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$small = New-Object System.Drawing.Font "Segoe UI", 9, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
$shadow = New-Object System.Drawing.SolidBrush (C "#292234")
$cream = New-Object System.Drawing.SolidBrush (C "#d5d2b8")
$accent = New-Object System.Drawing.SolidBrush (C "#cc7755")
$g.DrawString("Zam's Collectibles", $title, $shadow, 4, 4)
$g.DrawString("Zam's Collectibles", $title, $cream, 3, 3)
$g.DrawString("Aug Warriors TCG", $small, $accent, 4, 17)
$g.DrawString("by Zamco Electronics", $small, $cream, 4, 27)
$g.Dispose()

$big = New-Object System.Drawing.Bitmap ($W * $SCALE), ($H * $SCALE)
$g2 = [System.Drawing.Graphics]::FromImage($big)
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g2.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g2.DrawImage($b, 0, 0, $W * $SCALE, $H * $SCALE)
$out = Join-Path $root "preview.png"
$big.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
$g2.Dispose(); $big.Dispose(); $b.Dispose()
Write-Host "Preview written to $out ($([Math]::Round((Get-Item $out).Length / 1KB)) KB)"
