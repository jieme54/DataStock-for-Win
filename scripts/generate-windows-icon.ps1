$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$projectRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $projectRoot "Resources\DataStockIcon.png"
$appIconPath = Join-Path $projectRoot "Resources\AppIcon.ico"
$dataIconPath = Join-Path $projectRoot "Resources\DataStockIcon.ico"
$sizes = @(16, 24, 32, 48, 64, 128, 256)

function New-IconPngBytes {
    param(
        [System.Drawing.Bitmap]$Source,
        [int]$Size
    )

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $scale = [Math]::Min($Size / [double]$Source.Width, $Size / [double]$Source.Height)
        $width = [int][Math]::Round($Source.Width * $scale)
        $height = [int][Math]::Round($Source.Height * $scale)
        $x = [int](($Size - $width) / 2)
        $y = [int](($Size - $height) / 2)
        $graphics.DrawImage($Source, $x, $y, $width, $height)

        $stream = New-Object System.IO.MemoryStream
        try {
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            return $stream.ToArray()
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$source = [System.Drawing.Bitmap]::FromFile($sourcePath)
try {
    $pngEntries = @()
    foreach ($size in $sizes) {
        $pngEntries += ,(New-IconPngBytes -Source $source -Size $size)
    }

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$sizes.Count)

        $offset = 6 + (16 * $sizes.Count)
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $size = [int]$sizes[$i]
            $png = [byte[]]$pngEntries[$i]
            $directorySize = if ($size -eq 256) { 0 } else { $size }

            $writer.Write([byte]$directorySize)
            $writer.Write([byte]$directorySize)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$png.Length)
            $writer.Write([UInt32]$offset)
            $offset += $png.Length
        }

        foreach ($png in $pngEntries) {
            $writer.Write([byte[]]$png)
        }

        $iconBytes = $stream.ToArray()
        [System.IO.File]::WriteAllBytes($appIconPath, $iconBytes)
        [System.IO.File]::WriteAllBytes($dataIconPath, $iconBytes)
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}
finally {
    $source.Dispose()
}
