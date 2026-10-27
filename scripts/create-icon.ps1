$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assetFolder = Join-Path $root 'src/Nacre/Assets'
New-Item -ItemType Directory -Path $assetFolder -Force | Out-Null
$images = New-Object 'Collections.Generic.List[byte[]]'
$sizes = @(16,24,32,48,64,128,256)
foreach ($size in $sizes) {
    $bitmap = New-Object Drawing.Bitmap $size,$size
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform(($size / 256.0), ($size / 256.0))
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc(8,8,96,96,180,90); $path.AddArc(152,8,96,96,270,90)
    $path.AddArc(152,152,96,96,0,90); $path.AddArc(8,152,96,96,90,90); $path.CloseFigure()
    $brush = New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml('#326B79'))
    $graphics.FillPath($brush, $path)
    $ring = New-Object Drawing.Pen ([Drawing.ColorTranslator]::FromHtml('#78A6AE')),5
    $graphics.DrawArc($ring,34,34,188,188,25,145)
    $graphics.DrawArc($ring,34,34,188,188,205,145)
    $pen = New-Object Drawing.Pen ([Drawing.ColorTranslator]::FromHtml('#F1F6F5')),18
    $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
    $points = [Drawing.PointF[]]@([Drawing.PointF]::new(81,176),[Drawing.PointF]::new(81,80),[Drawing.PointF]::new(175,176),[Drawing.PointF]::new(175,80))
    $graphics.DrawLines($pen,$points)
    $memory = New-Object IO.MemoryStream
    $bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png); $images.Add($memory.ToArray())
    $memory.Dispose(); $pen.Dispose(); $ring.Dispose(); $brush.Dispose(); $path.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$output = [IO.File]::Create((Join-Path $assetFolder 'Nacre.ico'))
$writer = New-Object IO.BinaryWriter $output
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([uint16]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$index].Length); $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach ($image in $images) { $writer.Write($image) }
} finally { $writer.Dispose(); $output.Dispose() }
