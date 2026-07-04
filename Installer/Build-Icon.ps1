# Build a multi-resolution .ico from a source PNG.
#
# This builds the ICO by hand using the standard ICONDIR/ICONDIRENTRY layout
# and embedding PNG-encoded images at each requested size. Windows Vista and
# later read PNG-in-ICO entries natively, which keeps the alpha clean at large
# sizes without bloating the file with BMP+mask blobs.

param(
    [string]$SourcePng           = "$PSScriptRoot\..\MusicWidget\Assets\AppIcon.png",
    [string]$OutputIco           = "$PSScriptRoot\..\MusicWidget\Assets\AppIcon.ico",
    [int[]] $Sizes               = @(16, 24, 32, 48, 64, 128, 256),
    # Fraction of the side length used as the rounded-rectangle corner radius.
    # The source artwork's black background already has a ~20% radius — masking
    # at the same fraction cleans up the leftover square corners that would
    # otherwise show as a black box around the icon in Explorer/Task Manager.
    [double]$CornerRadiusFraction = 0.22
)

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $SourcePng)) {
    throw "Source PNG not found: $SourcePng"
}

# Build a rounded-rectangle GraphicsPath whose corners arc inside the rect.
function New-RoundedRectPath {
    param(
        [System.Drawing.RectangleF]$Rect,
        [single]$Radius
    )
    $d = $Radius * 2
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    if ($Radius -le 0) {
        $path.AddRectangle($Rect) | Out-Null
        return $path
    }
    $path.AddArc($Rect.X,                       $Rect.Y,                        $d, $d, 180, 90) | Out-Null
    $path.AddArc($Rect.Right - $d,              $Rect.Y,                        $d, $d, 270, 90) | Out-Null
    $path.AddArc($Rect.Right - $d,              $Rect.Bottom - $d,              $d, $d,   0, 90) | Out-Null
    $path.AddArc($Rect.X,                       $Rect.Bottom - $d,              $d, $d,  90, 90) | Out-Null
    $path.CloseFigure() | Out-Null
    return $path
}

$srcBitmap = [System.Drawing.Bitmap]::FromFile((Resolve-Path $SourcePng))
try {
    $pngBlobs = New-Object System.Collections.Generic.List[byte[]]
    foreach ($size in $Sizes) {
        $scaled = New-Object System.Drawing.Bitmap $size, $size
        try {
            $g = [System.Drawing.Graphics]::FromImage($scaled)
            try {
                $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
                $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $g.Clear([System.Drawing.Color]::Transparent)

                # Clip the drawing region to a rounded rectangle so the four
                # opaque corners of the source PNG become transparent.
                $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size
                $radius = [single]([math]::Max(1, [int]([math]::Round($size * $CornerRadiusFraction))))
                $path = New-RoundedRectPath -Rect $rect -Radius $radius
                try {
                    $region = New-Object System.Drawing.Region $path
                    try {
                        $g.SetClip($region, [System.Drawing.Drawing2D.CombineMode]::Replace)
                        $g.DrawImage($srcBitmap, 0, 0, $size, $size)
                    }
                    finally { $region.Dispose() }
                }
                finally { $path.Dispose() }
            }
            finally { $g.Dispose() }

            $ms = New-Object System.IO.MemoryStream
            try {
                $scaled.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
                $pngBlobs.Add($ms.ToArray())
            }
            finally { $ms.Dispose() }
        }
        finally { $scaled.Dispose() }
    }
}
finally { $srcBitmap.Dispose() }

# Assemble the ICO container.
$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $out
try {
    # ICONDIR
    $bw.Write([uint16]0)                          # Reserved
    $bw.Write([uint16]1)                          # Type = 1 (icon)
    $bw.Write([uint16]$Sizes.Count)               # Number of images

    $headerSize = 6 + (16 * $Sizes.Count)
    $offset = $headerSize
    for ($i = 0; $i -lt $Sizes.Count; $i++) {
        $size = $Sizes[$i]
        $blob = $pngBlobs[$i]
        $w = if ($size -ge 256) { 0 } else { [byte]$size }
        $h = if ($size -ge 256) { 0 } else { [byte]$size }
        $bw.Write([byte]$w)                       # Width  (0 = 256)
        $bw.Write([byte]$h)                       # Height (0 = 256)
        $bw.Write([byte]0)                        # Color count (0 = >=256 colors)
        $bw.Write([byte]0)                        # Reserved
        $bw.Write([uint16]1)                      # Color planes
        $bw.Write([uint16]32)                     # Bits per pixel
        $bw.Write([uint32]$blob.Length)           # Image data size
        $bw.Write([uint32]$offset)                # Image data offset
        $offset += $blob.Length
    }

    foreach ($blob in $pngBlobs) {
        $bw.Write($blob)
    }

    [System.IO.File]::WriteAllBytes((Join-Path (Split-Path $OutputIco -Parent) (Split-Path $OutputIco -Leaf)), $out.ToArray())
}
finally {
    $bw.Dispose()
    $out.Dispose()
}

Write-Host "Wrote $OutputIco ($($pngBlobs.Count) entries: $($Sizes -join ', '))"
