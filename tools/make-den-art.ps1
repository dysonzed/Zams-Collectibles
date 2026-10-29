# Builds the Downside Den location art from the source painting in Art/:
#   Art/den_scene_source.*  the scene at any 16:9 size
#   den_scene.png  960 x 540  the scene behind the shop (vanilla scenes are 960 x 540)
#   den_card.png   296 x 168  the picture on the travel screen's location card, cropped
#                             around Dan's booth
# Saved to Textures/Items, where the game loads them as crisp pixel art.
#
# The layout follows Dr. Jackson's shop, because the Den uses the same board and counter
# places: display board x 692-952 y 30-402, counter x 245-690 y 268-350 (in 960 x 540).
# Usage: powershell -ExecutionPolicy Bypass -File tools\make-den-art.ps1
Add-Type -AssemblyName System.Drawing
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$dest = Join-Path $root "Textures\Items"
$source = Get-ChildItem (Join-Path $root "Art") -Filter "den_scene_source.*" | Select-Object -First 1
if (-not $source) { throw "Art/den_scene_source.png (or .jpg) not found" }

function Resample($img, $srcRect, $w, $h, $mode) {
    $b = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($b)
    $g.InterpolationMode = $mode
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($img, (New-Object System.Drawing.Rectangle 0, 0, $w, $h), $srcRect, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()
    return $b
}

$img = [System.Drawing.Image]::FromFile($source.FullName)
# Down to the game's 960 x 540. The source is usually an upscale of pixel art at a non-whole
# factor, so a smooth filter recovers it better than nearest-neighbour would.
$scene = Resample $img (New-Object System.Drawing.Rectangle 0, 0, $img.Width, $img.Height) 960 540 ([System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic)
$img.Dispose()
$scene.Save((Join-Path $dest "den_scene.png"), [System.Drawing.Imaging.ImageFormat]::Png)

# Travel card: a 2x crop around the sign and Dan's booth, kept pixel-sharp.
$card = Resample $scene (New-Object System.Drawing.Rectangle 40, 10, 592, 336) 296 168 ([System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor)
$card.Save((Join-Path $dest "den_card.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$card.Dispose(); $scene.Dispose()
Write-Host "Den art written to $dest from $($source.Name)"
