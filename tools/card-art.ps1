# Card art importer: pick a card, paste (Ctrl+V), open or drop an image, frame it, and save it
# as pixel-art card art (Textures/Items/art_<set>_<nn>.png, 63 x 46, the size the card face draws).
#   - the list shows every card without art (tick the box to show all)
#   - drag the preview to move the crop, mouse wheel to zoom (below 100% the whole image shrinks
#     onto the card's dark ground)
#   - a plain background (a solid colour round the edge, or transparency) is detected and can be
#     removed and replaced with a solid colour or a pixel-art pattern; right-click the preview to
#     pick the colour to remove yourself
#   - the original image and settings are kept in art-source/ (not in git), so a card can be
#     re-done later: selecting it loads them back
# Run tools\card-art.cmd, or: powershell -STA -ExecutionPolicy Bypass -File tools\card-art.ps1
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class PixelArt
{
    // dark ground behind transparent parts, the same as the card face's
    const double BG_R = 26 / 255.0, BG_G = 22 / 255.0, BG_B = 32 / 255.0;
    static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

    public static int[] ReadPixels(Bitmap bmp)
    {
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int[] px = new int[bmp.Width * bmp.Height];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        bmp.UnlockBits(data);
        return px;
    }

    public static Bitmap FromPixels(int[] px, int w, int h)
    {
        Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        BitmapData data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(px, 0, data.Scan0, px.Length);
        bmp.UnlockBits(data);
        return bmp;
    }

    /// <summary>The share of the source's width and height the crop covers at this zoom.</summary>
    public static double[] CropFraction(int sw, int sh, int w, int h, double zoom)
    {
        double a = (double)w / h, cw, ch;
        if ((double)sw / sh > a) { ch = sh; cw = sh * a; } else { cw = sw; ch = sw / a; }
        return new double[] { cw / zoom / sw, ch / zoom / sh };
    }

    /// <summary>
    /// Crops the source around (cx, cy) (0-1 of its size), averages it down to w x h, adjusts
    /// saturation, contrast and sharpness, and reduces it to a palette of the given size.
    /// mask marks removed background pixels (or null). ground -1 keeps the old look: anything
    /// transparent or removed blends softly into the card's dark ground. ground 0 (solid) or a
    /// pattern (1+) instead cuts the subject out with hard pixel edges, optionally outlined,
    /// over an exact ground the palette reduction never touches.
    /// </summary>
    public static int[] Process(int[] src, byte[] mask, int sw, int sh, int w, int h, double zoom, double cx, double cy,
        int colors, double saturation, double contrast, double sharpen, bool dither, int ground, int groundColor, bool outline)
    {
        double[] f = CropFraction(sw, sh, w, h, zoom);
        double cw = f[0] * sw, ch = f[1] * sh;
        double left = cx * sw - cw / 2, top = cy * sh - ch / 2, stepX = cw / w, stepY = ch / h;
        int n = w * h;
        double[][] c = { new double[n], new double[n], new double[n] };
        bool[] fg = new bool[n];

        // area average of the source under each output pixel
        for (int y = 0; y < h; y++)
        {
            double y0 = top + y * stepY, y1 = y0 + stepY;
            for (int x = 0; x < w; x++)
            {
                double x0 = left + x * stepX, x1 = x0 + stepX;
                double tr = 0, tg = 0, tb = 0, ta = 0, tw = 0;
                for (int iy = (int)Math.Floor(y0); iy < (int)Math.Ceiling(y1); iy++)
                {
                    double wy = Math.Min(y1, iy + 1) - Math.Max(y0, iy);
                    if (wy <= 0) continue;
                    for (int ix = (int)Math.Floor(x0); ix < (int)Math.Ceiling(x1); ix++)
                    {
                        double wx = Math.Min(x1, ix + 1) - Math.Max(x0, ix);
                        if (wx <= 0) continue;
                        // zoomed out past the image's edge, or removed: the ground shows
                        bool outside = iy < 0 || ix < 0 || iy >= sh || ix >= sw;
                        int si = outside ? 0 : iy * sw + ix;
                        int p = outside || (mask != null && mask[si] != 0) ? 0 : src[si];
                        double a = ((p >> 24) & 255) / 255.0, wt = wx * wy;
                        tr += ((p >> 16) & 255) / 255.0 * a * wt;
                        tg += ((p >> 8) & 255) / 255.0 * a * wt;
                        tb += (p & 255) / 255.0 * a * wt;
                        ta += a * wt;
                        tw += wt;
                    }
                }
                int i = y * w + x;
                if (ground < 0)
                {
                    fg[i] = true;
                    c[0][i] = (tr + BG_R * (tw - ta)) / tw; c[1][i] = (tg + BG_G * (tw - ta)) / tw; c[2][i] = (tb + BG_B * (tw - ta)) / tw;
                }
                else if (ta >= tw * 0.5)
                {
                    fg[i] = true;   // mostly subject: its own colour, without the ground mixed in
                    c[0][i] = tr / ta; c[1][i] = tg / ta; c[2][i] = tb / ta;
                }
            }
        }

        // saturation and contrast
        for (int i = 0; i < n; i++)
        {
            if (!fg[i]) continue;
            double lum = 0.299 * c[0][i] + 0.587 * c[1][i] + 0.114 * c[2][i];
            for (int k = 0; k < 3; k++)
                c[k][i] = Clamp((lum + (c[k][i] - lum) * saturation - 0.5) * contrast + 0.5);
        }

        // unsharp mask: downsampling softens edges that pixel art keeps crisp
        if (sharpen > 0)
        {
            for (int k = 0; k < 3; k++)
            {
                double[] blur = new double[n];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        double t = 0; int m = 0;
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int yy = y + dy, xx = x + dx;
                                if (yy < 0 || xx < 0 || yy >= h || xx >= w || !fg[yy * w + xx]) continue;
                                t += c[k][yy * w + xx]; m++;
                            }
                        blur[y * w + x] = m > 0 ? t / m : 0;
                    }
                for (int i = 0; i < n; i++) if (fg[i]) c[k][i] = Clamp(c[k][i] + sharpen * (c[k][i] - blur[i]));
            }
        }

        // the palette comes from the subject only
        int fgCount = 0;
        for (int i = 0; i < n; i++) if (fg[i]) fgCount++;
        double[][] sub = { new double[fgCount], new double[fgCount], new double[fgCount] };
        for (int i = 0, j = 0; i < n; i++)
            if (fg[i]) { sub[0][j] = c[0][i]; sub[1][j] = c[1][i]; sub[2][j] = c[2][i]; j++; }
        double[][] palette = fgCount > 0 ? KMeans(sub, colors) : null;

        int[] outPx = new int[n];
        double spread = dither && palette != null ? 0.5 / Math.Pow(palette[0].Length, 1 / 3.0) : 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!fg[i])
                {
                    outPx[i] = outline && TouchesSubject(fg, w, h, x, y) ? Ink : GroundPixel(ground, groundColor, x, y, w, h);
                    continue;
                }
                double o = dither ? ((Bayer[(y % 4) * 4 + x % 4] + 0.5) / 16 - 0.5) * spread : 0;
                int best = Nearest(palette, c[0][i] + o, c[1][i] + o, c[2][i] + o);
                outPx[i] = unchecked((int)0xFF000000) | (Byte(palette[0][best]) << 16) | (Byte(palette[1][best]) << 8) | Byte(palette[2][best]);
            }
        return outPx;
    }

    // outline colour: a near-black ink, as the frame's keylines
    static readonly int Ink = unchecked((int)0xFF18141E);

    static bool TouchesSubject(bool[] fg, int w, int h, int x, int y)
    {
        return (x > 0 && fg[y * w + x - 1]) || (x < w - 1 && fg[y * w + x + 1])
            || (y > 0 && fg[(y - 1) * w + x]) || (y < h - 1 && fg[(y + 1) * w + x]);
    }

    public static readonly string[] Patterns = { "Solid colour", "Sunburst", "Diagonal stripes", "Dots",
        "Circuit grid", "Dither fade", "Scanlines", "Checker", "Halftone" };

    /// <summary>One pixel of a ground: a solid colour, or a pattern in shades of it.</summary>
    public static int GroundPixel(int ground, int color, int x, int y, int w, int h)
    {
        int dark = Shade(color, 0.62), light = Shade(color, 1.3);
        int cx = w / 2, cy = h / 2;
        switch (ground)
        {
            case 1: // sunburst: 16 rays from the centre
            {
                double a = Math.Atan2(y - cy + 0.5, x - cx + 0.5);
                return (int)Math.Floor((a + Math.PI) / (2 * Math.PI) * 16) % 2 == 0 ? color : dark;
            }
            case 2: return ((x + y) / 3) % 2 == 0 ? color : dark;
            case 3: return (y % 4 == 1 && (x + (y / 4 % 2) * 2) % 4 == 1) ? light : color;
            case 4: // circuit: a grid of traces with pads where they cross
                if (x % 8 == 3 && y % 8 == 3) return light;
                return x % 8 == 3 || y % 8 == 3 ? color : dark;
            case 5: // light at the top to dark at the bottom, ordered dither between the bands
            {
                double t = (double)y / (h - 1) * 2;
                double th = (Bayer[(y % 4) * 4 + x % 4] + 0.5) / 16;
                if (t < 1) return t - 0 > th ? color : light;
                return t - 1 > th ? dark : color;
            }
            case 6: return y % 3 == 2 ? dark : color;
            case 7: return ((x / 4) + (y / 4)) % 2 == 0 ? color : dark;
            case 8: // halftone: dots on a staggered 5-pixel grid that grow toward the corners
            {
                int gy = y / 5 * 5 + 2, shift = (y / 5) % 2 == 0 ? 0 : 2;
                int gx = (x + shift) / 5 * 5 + 2 - shift;
                double d = Math.Sqrt((gx - cx) * (gx - cx) + (gy - cy) * (gy - cy)) / Math.Sqrt(cx * cx + cy * cy);
                double r = d * 2.4;
                return (x - gx) * (x - gx) + (y - gy) * (y - gy) <= r * r ? dark : color;
            }
            default: return color;
        }
    }

    static int Shade(int color, double k)
    {
        Func<int, int> ch = v => k <= 1 ? (int)(v * k) : (int)(v + (255 - v) * (k - 1));
        return unchecked((int)0xFF000000) | (ch((color >> 16) & 255) << 16) | (ch((color >> 8) & 255) << 8) | ch(color & 255);
    }

    /// <summary>
    /// Looks for a plain background around the image's edge: returns {kind, colour, percent of the
    /// edge}, kind 0 none, 1 a solid colour, 2 transparent.
    /// </summary>
    public static int[] DetectBackground(int[] px, int w, int h)
    {
        var edge = new System.Collections.Generic.List<int>();
        int step = Math.Max(1, (w + h) / 800);
        for (int x = 0; x < w; x += step) { edge.Add(px[x]); edge.Add(px[(h - 1) * w + x]); }
        for (int y = 0; y < h; y += step) { edge.Add(px[y * w]); edge.Add(px[y * w + w - 1]); }

        int clear = 0;
        var buckets = new System.Collections.Generic.Dictionary<int, int>();
        foreach (int p in edge)
        {
            if (((p >> 24) & 255) < 16) { clear++; continue; }
            int b = ((p >> 19) & 31) << 10 | ((p >> 11) & 31) << 5 | ((p >> 3) & 31);
            int v; buckets.TryGetValue(b, out v); buckets[b] = v + 1;
        }
        if (clear * 2 >= edge.Count) return new int[] { 2, 0, clear * 100 / edge.Count };
        if (buckets.Count == 0) return new int[] { 0, 0, 0 };

        int top = 0, topCount = -1;
        foreach (var kv in buckets) if (kv.Value > topCount) { top = kv.Key; topCount = kv.Value; }
        int key = unchecked((int)0xFF000000) | ((top >> 10) * 8 + 4) << 16 | (((top >> 5) & 31) * 8 + 4) << 8 | ((top & 31) * 8 + 4);
        long sr = 0, sg = 0, sb = 0; int near = 0;
        foreach (int p in edge)
            if (Near(p, key, 40 * 40)) { near++; sr += (p >> 16) & 255; sg += (p >> 8) & 255; sb += p & 255; }
        int mean = unchecked((int)0xFF000000) | (int)(sr / near) << 16 | (int)(sg / near) << 8 | (int)(sb / near);
        int pct = near * 100 / edge.Count;
        return new int[] { pct >= 60 ? 1 : 0, mean, pct };
    }

    static bool Near(int p, int key, int tol2)
    {
        if (((p >> 24) & 255) < 16) return true;
        int dr = ((p >> 16) & 255) - ((key >> 16) & 255), dg = ((p >> 8) & 255) - ((key >> 8) & 255), db = (p & 255) - (key & 255);
        return dr * dr + dg * dg + db * db <= tol2;
    }

    /// <summary>
    /// The background to remove: pixels near the key colour connected to the image's edge
    /// (flood fill), or, with everywhere, all of them, including gaps enclosed by the subject.
    /// </summary>
    public static byte[] BackgroundMask(int[] px, int w, int h, int key, int tolerance, bool everywhere)
    {
        byte[] m = new byte[w * h];
        int tol2 = tolerance * tolerance;
        if (everywhere)
        {
            for (int i = 0; i < m.Length; i++) if (Near(px[i], key, tol2)) m[i] = 1;
            return m;
        }
        int[] stack = new int[w * h];
        int sp = 0;
        for (int x = 0; x < w; x++) { Seed(px, m, stack, ref sp, x, key, tol2); Seed(px, m, stack, ref sp, (h - 1) * w + x, key, tol2); }
        for (int y = 0; y < h; y++) { Seed(px, m, stack, ref sp, y * w, key, tol2); Seed(px, m, stack, ref sp, y * w + w - 1, key, tol2); }
        while (sp > 0)
        {
            int i = stack[--sp], x = i % w, y = i / w;
            if (x > 0) Seed(px, m, stack, ref sp, i - 1, key, tol2);
            if (x < w - 1) Seed(px, m, stack, ref sp, i + 1, key, tol2);
            if (y > 0) Seed(px, m, stack, ref sp, i - w, key, tol2);
            if (y < h - 1) Seed(px, m, stack, ref sp, i + w, key, tol2);
        }
        return m;
    }

    static void Seed(int[] px, byte[] m, int[] stack, ref int sp, int i, int key, int tol2)
    {
        if (m[i] != 0 || !Near(px[i], key, tol2)) return;
        m[i] = 1; stack[sp++] = i;
    }

    // k-means colour palette, seeded k-means++ style (fixed seed: the same settings give the same art)
    static double[][] KMeans(double[][] c, int k)
    {
        int n = c[0].Length;
        Random rng = new Random(7);
        double[][] p = { new double[k], new double[k], new double[k] };
        double[] d = new double[n];
        int count = 1;
        for (int j = 0; j < 3; j++) p[j][0] = c[j][n / 2];
        for (int i = 0; i < n; i++) d[i] = Dist(c, i, p, 0);
        while (count < k)
        {
            double total = 0;
            for (int i = 0; i < n; i++) total += d[i];
            if (total <= 1e-9) break;   // fewer distinct colours than asked for
            double t = rng.NextDouble() * total;
            int pick = n - 1;
            for (int i = 0; i < n; i++) { t -= d[i]; if (t <= 0) { pick = i; break; } }
            for (int j = 0; j < 3; j++) p[j][count] = c[j][pick];
            for (int i = 0; i < n; i++) d[i] = Math.Min(d[i], Dist(c, i, p, count));
            count++;
        }
        double[][] q = { new double[count], new double[count], new double[count] };
        for (int j = 0; j < 3; j++) Array.Copy(p[j], q[j], count);

        int[] assign = new int[n];
        for (int iter = 0; iter < 20; iter++)
        {
            double[][] sum = { new double[count], new double[count], new double[count] };
            int[] members = new int[count];
            for (int i = 0; i < n; i++)
            {
                int best = Nearest(q, c[0][i], c[1][i], c[2][i]);
                assign[i] = best; members[best]++;
                for (int j = 0; j < 3; j++) sum[j][best] += c[j][i];
            }
            for (int m = 0; m < count; m++)
                if (members[m] > 0)
                    for (int j = 0; j < 3; j++) q[j][m] = sum[j][m] / members[m];
        }
        return q;
    }

    static double Dist(double[][] c, int i, double[][] p, int m)
    {
        double dr = c[0][i] - p[0][m], dg = c[1][i] - p[1][m], db = c[2][i] - p[2][m];
        return 0.3 * dr * dr + 0.59 * dg * dg + 0.11 * db * db;
    }

    static int Nearest(double[][] p, double r, double g, double b)
    {
        int best = 0; double bestD = double.MaxValue;
        for (int m = 0; m < p[0].Length; m++)
        {
            double dr = r - p[0][m], dg = g - p[1][m], db = b - p[2][m];
            double dd = 0.3 * dr * dr + 0.59 * dg * dg + 0.11 * db * db;
            if (dd < bestD) { bestD = dd; best = m; }
        }
        return best;
    }

    static double Clamp(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }
    static int Byte(double v) { return (int)Math.Round(Clamp(v) * 255); }

    /// <summary>The board's mini card art: every other pixel of the centre 52 x 44.</summary>
    public static int[] Mini(int[] px, int w, int h)
    {
        int mw = 26, mh = 22, ox = (w - mw * 2) / 2, oy = (h - mh * 2) / 2;
        int[] o = new int[mw * mh];
        for (int y = 0; y < mh; y++)
            for (int x = 0; x < mw; x++)
                o[y * mw + x] = px[(oy + y * 2) * w + ox + x * 2];
        return o;
    }

    /// <summary>A blocky enlargement for the previews.</summary>
    public static Bitmap Upscale(int[] px, int w, int h, int s)
    {
        int[] o = new int[w * s * h * s];
        for (int y = 0; y < h * s; y++)
            for (int x = 0; x < w * s; x++)
                o[y * w * s + x] = px[(y / s) * w + x / s];
        return FromPixels(o, w * s, h * s);
    }
}
'@

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$itemsDir = Join-Path $root 'Textures\Items'
$sourceDir = Join-Path $root 'art-source'
$ART_W = 63; $ART_H = 46; $SCALE = 6; $MINI_SCALE = 6

# ---- cards
$data = Get-Content (Join-Path $root 'Data\sets.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$cards = foreach ($set in $data.sets) {
    foreach ($card in $set.cards) {
        $key = if ($card.art) { $card.art } else { 'art_{0}_{1:00}' -f $set.id, [int]$card.number }
        [pscustomobject]@{ set = $set; card = $card; key = $key
            label = '{0} {1:00}' -f $set.code, [int]$card.number }
    }
}
function Has-Art($c) { Test-Path (Join-Path $itemsDir "$($c.key).png") }

# ---- state
$script:src = $null; $script:srcPx = $null
$script:zoom = 1.0; $script:cx = 0.5; $script:cy = 0.5
$script:result = $null; $script:current = $null
$script:drag = $null
$script:bgKey = 0; $script:mask = $null; $script:maskId = ''
$script:groundColor = [Drawing.Color]::FromArgb(46, 111, 115).ToArgb()   # the game's teal

# the project's shared colours: the colour picker's custom colours and the last background
# colour, kept in tools/card-art-colors.json (in git) so every card and session uses them
$colorsFile = Join-Path $PSScriptRoot 'card-art-colors.json'
$script:customColors = [int[]]@()
function ConvertFrom-Hex($hex) { [Drawing.ColorTranslator]::FromHtml($hex) }
function ConvertTo-Hex($color) { '#{0:X2}{1:X2}{2:X2}' -f $color.R, $color.G, $color.B }
if (Test-Path $colorsFile) {
    try {
        $saved = Get-Content $colorsFile -Raw | ConvertFrom-Json
        # the colour dialog's custom colours are 0x00BBGGRR
        $script:customColors = [int[]]@($saved.customColors | ForEach-Object { [Drawing.ColorTranslator]::ToWin32((ConvertFrom-Hex $_)) })
        if ($saved.background) { $script:groundColor = (ConvertFrom-Hex $saved.background).ToArgb() }
    }
    catch { }   # a broken file: start from the defaults
}
function Save-Colors {
    @{ background = ConvertTo-Hex ([Drawing.Color]::FromArgb($script:groundColor))
        customColors = @($script:customColors | ForEach-Object { ConvertTo-Hex ([Drawing.ColorTranslator]::FromWin32($_)) }) } |
        ConvertTo-Json | Set-Content $colorsFile -Encoding UTF8
}

# ---- window
$form = New-Object Windows.Forms.Form
$form.Text = "Zam's Collectibles - Card Art"
$form.Size = New-Object Drawing.Size(1060, 860)
$form.StartPosition = 'CenterScreen'
$form.KeyPreview = $true
$form.AllowDrop = $true
$font = New-Object Drawing.Font('Segoe UI', 9)
$form.Font = $font

function Add-Control($ctl, $x, $y, $w, $h) {
    $ctl.Location = New-Object Drawing.Point($x, $y); $ctl.Size = New-Object Drawing.Size($w, $h)
    $form.Controls.Add($ctl); $ctl
}

$setFilter = Add-Control (New-Object Windows.Forms.ComboBox) 12 12 220 24
$setFilter.DropDownStyle = 'DropDownList'
[void]$setFilter.Items.Add('All sets')
foreach ($s in $data.sets) { [void]$setFilter.Items.Add("Tier $($s.tier): $($s.name)") }
$setFilter.SelectedIndex = 0

$showAll = Add-Control (New-Object Windows.Forms.CheckBox) 242 12 150 24
$showAll.Text = 'Show cards with art'

$list = Add-Control (New-Object Windows.Forms.ListView) 12 44 380 760
$list.View = 'Details'; $list.FullRowSelect = $true; $list.HideSelection = $false; $list.MultiSelect = $false
[void]$list.Columns.Add('Card', 60); [void]$list.Columns.Add('Name', 172)
[void]$list.Columns.Add('Rarity', 85); [void]$list.Columns.Add('Art', 38)

$pasteBtn = Add-Control (New-Object Windows.Forms.Button) 410 12 130 28
$pasteBtn.Text = 'Paste (Ctrl+V)'
$openBtn = Add-Control (New-Object Windows.Forms.Button) 548 12 110 28
$openBtn.Text = 'Open file...'
$sourceLabel = Add-Control (New-Object Windows.Forms.Label) 668 18 370 20
$sourceLabel.Text = 'No image: paste, open or drop one here'

$cardLabel = Add-Control (New-Object Windows.Forms.Label) 410 48 620 22
$cardLabel.Font = New-Object Drawing.Font('Segoe UI', 11, [Drawing.FontStyle]::Bold)
$cardLabel.Text = 'Pick a card on the left'

$face = Add-Control (New-Object Windows.Forms.PictureBox) 410 76 ($ART_W * $SCALE) ($ART_H * $SCALE)
$face.BackColor = [Drawing.Color]::FromArgb(26, 22, 32); $face.Cursor = 'SizeAll'
$faceCaption = Add-Control (New-Object Windows.Forms.Label) 410 (80 + $ART_H * $SCALE) 380 20
$faceCaption.Text = 'Card face (63 x 46). Drag to move, mouse wheel to zoom.'

$mini = Add-Control (New-Object Windows.Forms.PictureBox) 810 76 (26 * $MINI_SCALE) (22 * $MINI_SCALE)
$mini.BackColor = $face.BackColor
$miniCaption = Add-Control (New-Object Windows.Forms.Label) 810 (80 + 22 * $MINI_SCALE) 220 20
$miniCaption.Text = 'Board mini card'

# what the card is and says, to help choose its art
$details = Add-Control (New-Object Windows.Forms.RichTextBox) 810 (108 + 22 * $MINI_SCALE) 230 (270 - 22 * $MINI_SCALE)
$details.ReadOnly = $true; $details.BorderStyle = 'None'; $details.BackColor = $form.BackColor; $details.TabStop = $false
$details.ScrollBars = 'Vertical'

function Show-Details($c) {
    $card = $c.card
    $details.Clear()
    $add = {
        param($text, $style)
        $details.SelectionStart = $details.TextLength
        $details.SelectionFont = New-Object Drawing.Font('Segoe UI', 9, $style)
        $details.AppendText($text)
    }
    $kind = switch ($card.kind) {
        'augment' { "$((Get-Culture).TextInfo.ToTitleCase($card.slot)) augment" }
        'mod' { if ($card.modFor -and $card.modFor -ne 'any') { "Mod for $($card.modFor)" } else { 'Mod (any augment)' } }
        default { (Get-Culture).TextInfo.ToTitleCase($card.kind) }
    }
    & $add "$kind, $($card.rarity)`n" ([Drawing.FontStyle]::Bold)
    $stats = @()
    if ($card.kind -ne 'hack' -or $card.power) { $stats += "Power $($card.power)" }
    if ($card.glitch) { $stats += "Glitch $($card.glitch)" }
    if ($card.stability) { $stats += "Stability $($card.stability)" }
    if ($stats) { & $add (($stats -join '   ') + "`n") ([Drawing.FontStyle]::Regular) }
    if ($card.text) { & $add "`n$($card.text)`n" ([Drawing.FontStyle]::Regular) }
    if ($card.flavor) { & $add "`n`"$($card.flavor)`"`n" ([Drawing.FontStyle]::Italic) }
    & $add "`n$($c.set.name) (Tier $($c.set.tier))" ([Drawing.FontStyle]::Regular)
    $details.SelectionStart = 0
}

# sliders: name, min, max, default, display
$sliders = @{}
$y = 390
foreach ($def in @(
        @('Zoom', 25, 400, 100, { "$_%" }),
        @('Colors', 4, 48, 20, { "$_" }),
        @('Saturation', 50, 200, 115, { "$_%" }),
        @('Contrast', 50, 200, 110, { "$_%" }),
        @('Sharpen', 0, 100, 35, { "$_%" }))) {
    $name = $def[0]
    $lbl = Add-Control (New-Object Windows.Forms.Label) 410 ($y + 4) 80 20
    $lbl.Text = $name
    $bar = Add-Control (New-Object Windows.Forms.TrackBar) 490 $y 420 30
    $bar.Minimum = $def[1]; $bar.Maximum = $def[2]; $bar.Value = $def[3]; $bar.TickStyle = 'None'
    $val = Add-Control (New-Object Windows.Forms.Label) 915 ($y + 4) 60 20
    $sliders[$name] = @{ bar = $bar; value = $val; fmt = $def[4]; default = $def[3] }
    $y += 36
}
$dither = Add-Control (New-Object Windows.Forms.CheckBox) 490 $y 100 24
$dither.Text = 'Dither'
$resetBtn = Add-Control (New-Object Windows.Forms.Button) 600 $y 120 26
$resetBtn.Text = 'Reset settings'

# background: what was found round the edge, what to remove, and what goes behind the subject
$y += 40
$bgTitle = Add-Control (New-Object Windows.Forms.Label) 410 $y 90 20
$bgTitle.Text = 'Background'; $bgTitle.Font = New-Object Drawing.Font('Segoe UI', 9, [Drawing.FontStyle]::Bold)
$bgFound = Add-Control (New-Object Windows.Forms.Label) 500 $y 540 20
$y += 26
$removeBox = Add-Control (New-Object Windows.Forms.CheckBox) 410 $y 120 24
$removeBox.Text = 'Remove colour'
$keySwatch = Add-Control (New-Object Windows.Forms.Panel) 530 ($y + 2) 20 20
$keySwatch.BorderStyle = 'FixedSingle'
$tolLabel = Add-Control (New-Object Windows.Forms.Label) 560 ($y + 4) 65 20
$tolLabel.Text = 'Tolerance'
$tolerance = Add-Control (New-Object Windows.Forms.TrackBar) 625 $y 200 30
$tolerance.Minimum = 4; $tolerance.Maximum = 140; $tolerance.Value = 48; $tolerance.TickStyle = 'None'
$gapsBox = Add-Control (New-Object Windows.Forms.CheckBox) 835 $y 200 24
$gapsBox.Text = 'Also gaps inside the subject'
$y += 34
$groundBox = Add-Control (New-Object Windows.Forms.ComboBox) 410 $y 190 24
$groundBox.DropDownStyle = 'DropDownList'
[void]$groundBox.Items.Add('Card dark (soft edges)')
foreach ($pattern in [PixelArt]::Patterns) { [void]$groundBox.Items.Add($pattern) }
$groundBox.SelectedIndex = 0
$colorBtn = Add-Control (New-Object Windows.Forms.Button) 610 ($y - 1) 110 26
$colorBtn.Text = 'Colour...'
$outlineBox = Add-Control (New-Object Windows.Forms.CheckBox) 730 $y 160 24
$outlineBox.Text = 'Outline the subject'
$y += 30
$bgHint = Add-Control (New-Object Windows.Forms.Label) 410 $y 620 20
$bgHint.Text = 'Right-click the preview to pick the colour to remove.'
$bgHint.ForeColor = [Drawing.Color]::DimGray

$y += 30
$saveBtn = Add-Control (New-Object Windows.Forms.Button) 410 $y 200 34
$saveBtn.Text = 'Save art (Ctrl+S)'; $saveBtn.Enabled = $false
$nextBox = Add-Control (New-Object Windows.Forms.CheckBox) 620 ($y + 6) 200 24
$nextBox.Text = 'Go to the next card after saving'; $nextBox.Checked = $true
$status = Add-Control (New-Object Windows.Forms.Label) 830 ($y + 10) 210 20

# ---- list
function Fill-List {
    $list.BeginUpdate(); $list.Items.Clear()
    foreach ($c in $cards) {
        if ($setFilter.SelectedIndex -gt 0 -and $c.set -ne $data.sets[$setFilter.SelectedIndex - 1]) { continue }
        $has = Has-Art $c
        if ($has -and -not $showAll.Checked) { continue }
        $row = New-Object Windows.Forms.ListViewItem($c.label)
        [void]$row.SubItems.Add($c.card.name); [void]$row.SubItems.Add($c.card.rarity)
        [void]$row.SubItems.Add($(if ($has) { 'yes' } else { '' }))
        $row.Tag = $c
        if ($c.card.rarity -eq 'ultra') { $row.ForeColor = [Drawing.Color]::DarkGoldenrod }
        elseif ($c.card.rarity -eq 'rare') { $row.ForeColor = [Drawing.Color]::RoyalBlue }
        [void]$list.Items.Add($row)
    }
    $list.EndUpdate()
    $missing = @($cards | Where-Object { -not (Has-Art $_) }).Count
    $form.Text = "Zam's Collectibles - Card Art ($missing of $($cards.Count) cards need art)"
}

# ---- image
function Set-Source($bmp, $name) {
    if ($script:src) { $script:src.Dispose() }
    $script:src = $bmp
    $script:srcPx = if ($bmp) { [PixelArt]::ReadPixels($bmp) } else { $null }
    $sourceLabel.Text = if ($bmp) { "$name ($($bmp.Width) x $($bmp.Height))" } else { 'No image: paste, open or drop one here' }
    if (-not $bmp) { $bgFound.Text = '' }
}

function Load-Bitmap($path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $img = [Drawing.Image]::FromStream((New-Object IO.MemoryStream(, $bytes)))
    New-Object Drawing.Bitmap($img)   # a copy that doesn't hold the file open
}

function Clamp-Crop {
    $f = [PixelArt]::CropFraction($script:src.Width, $script:src.Height, $ART_W, $ART_H, $script:zoom)
    # zoomed in, the crop stays inside the image; zoomed out, the image stays inside the crop
    $script:cx = [Math]::Min([Math]::Max(1 - $f[0] / 2, $f[0] / 2), [Math]::Max([Math]::Min(1 - $f[0] / 2, $f[0] / 2), $script:cx))
    $script:cy = [Math]::Min([Math]::Max(1 - $f[1] / 2, $f[1] / 2), [Math]::Max([Math]::Min(1 - $f[1] / 2, $f[1] / 2), $script:cy))
}

function Render {
    foreach ($s in $sliders.Values) { $s.value.Text = ($s.bar.Value | ForEach-Object $s.fmt) }
    $saveBtn.Enabled = $script:src -and $script:current
    if (-not $script:src) { $face.Image = $null; $mini.Image = $null; $script:result = $null; return }
    $script:zoom = $sliders.Zoom.bar.Value / 100.0
    Clamp-Crop
    $keySwatch.BackColor = [Drawing.Color]::FromArgb($script:bgKey)
    $colorBtn.BackColor = [Drawing.Color]::FromArgb($script:groundColor)
    $colorBtn.ForeColor = if ($colorBtn.BackColor.GetBrightness() -gt 0.5) { [Drawing.Color]::Black } else { [Drawing.Color]::White }
    $colorBtn.Enabled = $groundBox.SelectedIndex -gt 0
    $outlineBox.Enabled = $groundBox.SelectedIndex -gt 0
    $mask = $null
    if ($removeBox.Checked) {
        # the mask only changes with the image, colour or tolerance: keep it between renders
        $id = "$($script:src.GetHashCode())/$($script:bgKey)/$($tolerance.Value)/$($gapsBox.Checked)"
        if ($id -ne $script:maskId) {
            $script:mask = [PixelArt]::BackgroundMask($script:srcPx, $script:src.Width, $script:src.Height, $script:bgKey, $tolerance.Value, $gapsBox.Checked)
            $script:maskId = $id
        }
        $mask = $script:mask
    }
    $script:result = [PixelArt]::Process($script:srcPx, $mask, $script:src.Width, $script:src.Height, $ART_W, $ART_H,
        $script:zoom, $script:cx, $script:cy, $sliders.Colors.bar.Value, $sliders.Saturation.bar.Value / 100.0,
        $sliders.Contrast.bar.Value / 100.0, $sliders.Sharpen.bar.Value / 100.0, $dither.Checked,
        $groundBox.SelectedIndex - 1, $script:groundColor, $outlineBox.Checked)
    $old = $face.Image; $face.Image = [PixelArt]::Upscale($script:result, $ART_W, $ART_H, $SCALE); if ($old) { $old.Dispose() }
    $old = $mini.Image; $mini.Image = [PixelArt]::Upscale([PixelArt]::Mini($script:result, $ART_W, $ART_H), 26, 22, $MINI_SCALE); if ($old) { $old.Dispose() }
}

function Paste-Image {
    $obj = [Windows.Forms.Clipboard]::GetDataObject()
    if (-not $obj) { return }
    $bmp = $null; $name = 'Clipboard'
    if ($obj.GetDataPresent('PNG')) {   # keeps transparency, unlike the plain bitmap
        $stream = $obj.GetData('PNG')
        if ($stream -is [IO.Stream]) { $bmp = New-Object Drawing.Bitmap([Drawing.Image]::FromStream($stream)) }
    }
    if (-not $bmp -and [Windows.Forms.Clipboard]::ContainsImage()) { $bmp = New-Object Drawing.Bitmap([Windows.Forms.Clipboard]::GetImage()) }
    if (-not $bmp -and [Windows.Forms.Clipboard]::ContainsFileDropList()) {
        $file = [Windows.Forms.Clipboard]::GetFileDropList() | Select-Object -First 1
        try { $bmp = Load-Bitmap $file; $name = Split-Path $file -Leaf } catch { }
    }
    if (-not $bmp) { $status.Text = 'No image on the clipboard'; return }
    Use-NewImage $bmp $name
}

function Use-NewImage($bmp, $name) {
    Set-Source $bmp $name
    $script:cx = 0.5; $script:cy = 0.5; $sliders.Zoom.bar.Value = 100
    $status.Text = ''
    Find-Background
    Render
}

# a plain background round the edge: remove it, ready to be replaced
function Find-Background {
    $found = [PixelArt]::DetectBackground($script:srcPx, $script:src.Width, $script:src.Height)
    switch ($found[0]) {
        1 {
            $script:bgKey = $found[1]
            $removeBox.Checked = $true
            $bgFound.Text = 'Solid background found: #{0:X6} ({1}% of the edge)' -f ($found[1] -band 0xFFFFFF), $found[2]
        }
        2 {
            $removeBox.Checked = $false
            $bgFound.Text = 'Transparent background found: it is replaced automatically'
        }
        default {
            $removeBox.Checked = $false
            $bgFound.Text = 'No plain background found (right-click the preview to pick one)'
        }
    }
}

# ---- saving: the art, plus the original and its settings for redoing later
function Save-Art {
    if (-not ($script:result -and $script:current)) { return }
    $c = $script:current
    $art = [PixelArt]::FromPixels($script:result, $ART_W, $ART_H)
    $art.Save((Join-Path $itemsDir "$($c.key).png"), [Drawing.Imaging.ImageFormat]::Png); $art.Dispose()

    if (-not (Test-Path $sourceDir)) { [void](New-Item -ItemType Directory $sourceDir) }
    $srcPath = Join-Path $sourceDir "$($c.key).png"
    $copy = New-Object Drawing.Bitmap($script:src); $copy.Save($srcPath, [Drawing.Imaging.ImageFormat]::Png); $copy.Dispose()
    $settings = @{ zoom = $sliders.Zoom.bar.Value; cx = $script:cx; cy = $script:cy; colors = $sliders.Colors.bar.Value
        saturation = $sliders.Saturation.bar.Value; contrast = $sliders.Contrast.bar.Value
        sharpen = $sliders.Sharpen.bar.Value; dither = $dither.Checked
        remove = $removeBox.Checked; key = $script:bgKey; tolerance = $tolerance.Value; gaps = $gapsBox.Checked
        ground = $groundBox.SelectedIndex; groundColor = $script:groundColor; outline = $outlineBox.Checked }
    $settings | ConvertTo-Json | Set-Content (Join-Path $sourceDir "$($c.key).json") -Encoding UTF8

    $status.Text = "Saved $($c.key).png"
    $index = if ($list.SelectedIndices.Count) { $list.SelectedIndices[0] } else { 0 }
    Fill-List
    if ($nextBox.Checked -and $list.Items.Count) {
        # without "show all" the saved card leaves the list, so the same index is the next card
        if ($showAll.Checked) { $index++ }
        $row = $list.Items[[Math]::Min($index, $list.Items.Count - 1)]
        $row.Selected = $true; $row.EnsureVisible()
    }
    else { Select-Card $c }
}

function Select-Card($c) {
    $script:current = $c
    $cardLabel.Text = "$($c.label)  $($c.card.name)"
    Show-Details $c
    $srcPath = Join-Path $sourceDir "$($c.key).png"
    $setPath = Join-Path $sourceDir "$($c.key).json"
    if (Test-Path $srcPath) {
        Set-Source (Load-Bitmap $srcPath) "Saved original for $($c.key)"
        if (Test-Path $setPath) {
            $s = Get-Content $setPath -Raw | ConvertFrom-Json
            $sliders.Zoom.bar.Value = $s.zoom; $sliders.Colors.bar.Value = $s.colors
            $sliders.Saturation.bar.Value = $s.saturation; $sliders.Contrast.bar.Value = $s.contrast
            $sliders.Sharpen.bar.Value = $s.sharpen; $dither.Checked = $s.dither
            $script:cx = $s.cx; $script:cy = $s.cy
            if ($null -ne $s.ground) {
                $script:bgKey = $s.key; $tolerance.Value = $s.tolerance; $gapsBox.Checked = $s.gaps
                $removeBox.Checked = $s.remove
                $groundBox.SelectedIndex = $s.ground; $script:groundColor = $s.groundColor; $outlineBox.Checked = $s.outline
                $bgFound.Text = 'Saved background settings'
            }
        }
    }
    else { Set-Source $null $null }   # never carry one card's image over to the next
    Render
}

# ---- events
$list.Add_SelectedIndexChanged({ if ($list.SelectedItems.Count) { Select-Card $list.SelectedItems[0].Tag } })
$setFilter.Add_SelectedIndexChanged({ Fill-List })
$showAll.Add_CheckedChanged({ Fill-List })
$pasteBtn.Add_Click({ Paste-Image })
$openBtn.Add_Click({
        $dlg = New-Object Windows.Forms.OpenFileDialog
        $dlg.Filter = 'Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|All files|*.*'
        if ($dlg.ShowDialog() -eq 'OK') {
            try { Use-NewImage (Load-Bitmap $dlg.FileName) (Split-Path $dlg.FileName -Leaf) }
            catch { $status.Text = "Can't read that image" }
        }
    })
foreach ($target in @($form, $face, $list)) {
    $target.AllowDrop = $true
    $target.Add_DragEnter({ if ($_.Data.GetDataPresent([Windows.Forms.DataFormats]::FileDrop)) { $_.Effect = 'Copy' } })
    $target.Add_DragDrop({
            $file = $_.Data.GetData([Windows.Forms.DataFormats]::FileDrop) | Select-Object -First 1
            try { Use-NewImage (Load-Bitmap $file) (Split-Path $file -Leaf) } catch { $status.Text = "Can't read that image" }
        })
}
$form.Add_KeyDown({
        if ($_.Control -and $_.KeyCode -eq 'V') { Paste-Image; $_.Handled = $true }
        elseif ($_.Control -and $_.KeyCode -eq 'S') { Save-Art; $_.Handled = $true }
    })
foreach ($s in $sliders.Values) { $s.bar.Add_ValueChanged({ Render }) }
$dither.Add_CheckedChanged({ Render })
foreach ($ctl in @($removeBox, $gapsBox, $outlineBox)) { $ctl.Add_CheckedChanged({ Render }) }
$tolerance.Add_ValueChanged({ Render })
$groundBox.Add_SelectedIndexChanged({ Render })
$colorBtn.Add_Click({
        $dlg = New-Object Windows.Forms.ColorDialog
        $dlg.Color = [Drawing.Color]::FromArgb($script:groundColor); $dlg.FullOpen = $true
        $dlg.CustomColors = $script:customColors
        $ok = $dlg.ShowDialog() -eq 'OK'
        $script:customColors = $dlg.CustomColors   # custom colours can be added even when cancelling
        if ($ok) { $script:groundColor = $dlg.Color.ToArgb() }
        Save-Colors
        if ($ok) { Render }
    })
$resetBtn.Add_Click({
        foreach ($s in $sliders.Values) { $s.bar.Value = $s.default }
        $dither.Checked = $false; $script:cx = 0.5; $script:cy = 0.5
        $tolerance.Value = 48; $gapsBox.Checked = $false; $outlineBox.Checked = $false
        if ($script:src) { Find-Background }
        Render
    })
$saveBtn.Add_Click({ Save-Art })

# right-click: the source pixel under the cursor becomes the colour to remove
function Pick-Background($px, $py) {
    if (-not $script:src) { return }
    $w = $script:src.Width; $h = $script:src.Height
    $f = [PixelArt]::CropFraction($w, $h, $ART_W, $ART_H, $script:zoom)
    $sx = [int][Math]::Floor(($script:cx - $f[0] / 2 + $px / $face.Width * $f[0]) * $w)
    $sy = [int][Math]::Floor(($script:cy - $f[1] / 2 + $py / $face.Height * $f[1]) * $h)
    if ($sx -lt 0 -or $sy -lt 0 -or $sx -ge $w -or $sy -ge $h) { return }
    $script:bgKey = $script:srcPx[$sy * $w + $sx]
    $bgFound.Text = 'Picked #{0:X6} to remove' -f ($script:bgKey -band 0xFFFFFF)
    if ($removeBox.Checked) { Render } else { $removeBox.Checked = $true }
}

# drag the preview to move the crop, wheel to zoom
$face.Add_MouseDown({
        if ($_.Button -eq 'Right') { Pick-Background $_.X $_.Y; return }
        $script:drag = @{ x = $_.X; y = $_.Y }
    })
$face.Add_MouseUp({ $script:drag = $null })
$face.Add_MouseMove({
        if (-not ($script:drag -and $script:src)) { return }
        $f = [PixelArt]::CropFraction($script:src.Width, $script:src.Height, $ART_W, $ART_H, $script:zoom)
        $script:cx -= ($_.X - $script:drag.x) / $face.Width * $f[0]
        $script:cy -= ($_.Y - $script:drag.y) / $face.Height * $f[1]
        $script:drag = @{ x = $_.X; y = $_.Y }
        Render
    })
$face.Add_MouseEnter({ $face.Focus() })
$face.Add_MouseWheel({
        $bar = $sliders.Zoom.bar
        $bar.Value = [Math]::Max($bar.Minimum, [Math]::Min($bar.Maximum, $bar.Value + [Math]::Sign($_.Delta) * 10))
    })

Fill-List
Render
[void]$form.ShowDialog()
