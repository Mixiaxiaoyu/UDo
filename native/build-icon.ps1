$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$assetsPath=Join-Path $PSScriptRoot '..\assets'
$sourceImage=[System.Drawing.Image]::FromFile((Join-Path $assetsPath 'udo.png'))
$frames=[System.Collections.Generic.List[byte[]]]::new()
$sizes=@(16,24,32,48,64,128,256)
try {
    foreach($size in $sizes) {
        $bitmap=[System.Drawing.Bitmap]::new($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppPArgb)
        $graphics=[System.Drawing.Graphics]::FromImage($bitmap)
        $stream=[System.IO.MemoryStream]::new()
        $attributes=[System.Drawing.Imaging.ImageAttributes]::new()
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingMode=[System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $graphics.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $graphics.DrawImage($sourceImage,[System.Drawing.Rectangle]::new(0,0,$size,$size),0,0,$sourceImage.Width,$sourceImage.Height,[System.Drawing.GraphicsUnit]::Pixel,$attributes)
            $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($stream.ToArray())
        } finally { $attributes.Dispose(); $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $sourceImage.Dispose() }
$output=[System.IO.File]::Create((Join-Path $assetsPath 'udo.ico'))
$writer=[System.IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset=6+16*$sizes.Count
    for($i=0;$i -lt $sizes.Count;$i++) {
        $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset+=$frames[$i].Length
    }
    foreach($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
