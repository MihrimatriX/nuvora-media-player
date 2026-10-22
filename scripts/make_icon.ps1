Add-Type -AssemblyName System.Drawing

$icoPath = "src/Player.App/Assets/app.ico"
$pngPath = "src/Player.App/Assets/app.png"

$sizes = @(256, 128, 64, 48, 32, 16)
$pngBytesList = [System.Collections.Generic.List[byte[]]]::new()

foreach ($sz in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $sz, $sz, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    
    $pad = [float]($sz * 0.06)
    $rect = [System.Drawing.RectangleF]::new($pad, $pad, [float]($sz - 2 * $pad), [float]($sz - 2 * $pad))
    $radius = [float]($sz * 0.22)
    
    # Rounded rect path
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    
    # Background gradient: deep dark violet to obsidian
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (
        [System.Drawing.PointF]::new(0, 0),
        [System.Drawing.PointF]::new($sz, $sz),
        [System.Drawing.Color]::FromArgb(255, 24, 20, 48),
        [System.Drawing.Color]::FromArgb(255, 10, 11, 18)
    )
    $g.FillPath($bgBrush, $path)
    $bgBrush.Dispose()
    
    # Glowing border
    $borderPen = New-Object System.Drawing.Pen (
        (New-Object System.Drawing.Drawing2D.LinearGradientBrush (
            [System.Drawing.PointF]::new(0, 0),
            [System.Drawing.PointF]::new($sz, $sz),
            [System.Drawing.Color]::FromArgb(220, 139, 92, 246),
            [System.Drawing.Color]::FromArgb(180, 14, 165, 233)
        )),
        [float]([Math]::Max(1.5, $sz * 0.035))
    )
    $g.DrawPath($borderPen, $path)
    $borderPen.Dispose()
    $path.Dispose()
    
    # Play prism in center
    $playPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $cx = $sz * 0.52
    $cy = $sz * 0.50
    $r = $sz * 0.26
    
    $p1 = [System.Drawing.PointF]::new([float]($cx - $r * 0.65), [float]($cy - $r * 0.95))
    $p2 = [System.Drawing.PointF]::new([float]($cx + $r * 0.95), [float]($cy))
    $p3 = [System.Drawing.PointF]::new([float]($cx - $r * 0.65), [float]($cy + $r * 0.95))
    $playPath.AddPolygon(@($p1, $p2, $p3))
    
    $playBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (
        $p1, $p3,
        [System.Drawing.Color]::FromArgb(255, 168, 85, 247),
        [System.Drawing.Color]::FromArgb(255, 56, 189, 248)
    )
    $g.FillPath($playBrush, $playPath)
    $playBrush.Dispose()
    
    # Accent inner facet
    $facetPath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $pCenter = [System.Drawing.PointF]::new([float]($cx - $r * 0.15), [float]($cy))
    $facetPath.AddPolygon(@($p1, $p2, $pCenter))
    $facetBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 255, 255, 255))
    $g.FillPath($facetBrush, $facetPath)
    $facetBrush.Dispose()
    $facetPath.Dispose()
    $playPath.Dispose()
    
    $g.Dispose()
    
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    $bmp.Dispose()
    $pngBytesList.Add($bytes)
    
    if ($sz -eq 256) {
        [System.IO.File]::WriteAllBytes($pngPath, $bytes)
    }
}

# Pack into .ico file
$fs = [System.IO.File]::OpenWrite($icoPath)
$bw = New-Object System.IO.BinaryWriter $fs

# ICONDIR header
$bw.Write([uint16]0) # Reserved
$bw.Write([uint16]1) # Type (1 = icon)
$bw.Write([uint16]$sizes.Count) # Count

$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $data = $pngBytesList[$i]
    $w = if ($sz -ge 256) { [byte]0 } else { [byte]$sz }
    $h = if ($sz -ge 256) { [byte]0 } else { [byte]$sz }
    
    $bw.Write($w) # Width
    $bw.Write($h) # Height
    $bw.Write([byte]0) # Color count
    $bw.Write([byte]0) # Reserved
    $bw.Write([uint16]1) # Planes
    $bw.Write([uint16]32) # Bit count
    $bw.Write([uint32]$data.Length) # Bytes in res
    $bw.Write([uint32]$offset) # Image offset
    
    $offset += $data.Length
}

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $bw.Write($pngBytesList[$i])
}

$bw.Flush()
$bw.Close()
$fs.Close()

Write-Output "Created: $icoPath and $pngPath"

