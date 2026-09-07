param(
    [Parameter(Mandatory = $true)]
    [string]$SourcePng,
    [string]$OutputIco = "$PSScriptRoot\AppIcon.ico"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$resolvedSource = (Resolve-Path -LiteralPath $SourcePng).Path
$source = [System.Drawing.Bitmap]::new([string]$resolvedSource)
$images = [System.Collections.Generic.List[byte[]]]::new()

try {
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new(
            $size,
            $size,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::Transparent)
                $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $graphics.InterpolationMode = if ($size -le 24) {
                    [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                } else {
                    [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                }
                $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                $graphics.DrawImage($source, 0, 0, $size, $size)
            } finally {
                $graphics.Dispose()
            }

            # Classic 32-bit DIB frames are intentionally used instead of PNG frames.
            # WinForms/System.Drawing then renders the same icon correctly in the tray,
            # window captions and Explorer on every supported Windows version.
            $stream = [System.IO.MemoryStream]::new()
            $frameWriter = [System.IO.BinaryWriter]::new($stream)
            try {
                $maskRowBytes = [int]([Math]::Ceiling($size / 32.0) * 4)
                $pixelBytes = $size * $size * 4
                $frameWriter.Write([uint32]40)          # BITMAPINFOHEADER size
                $frameWriter.Write([int32]$size)
                $frameWriter.Write([int32]($size * 2)) # XOR image + AND mask
                $frameWriter.Write([uint16]1)
                $frameWriter.Write([uint16]32)
                $frameWriter.Write([uint32]0)           # BI_RGB
                $frameWriter.Write([uint32]$pixelBytes)
                $frameWriter.Write([int32]0)
                $frameWriter.Write([int32]0)
                $frameWriter.Write([uint32]0)
                $frameWriter.Write([uint32]0)

                for ($y = $size - 1; $y -ge 0; $y--) {
                    for ($x = 0; $x -lt $size; $x++) {
                        $pixel = $bitmap.GetPixel($x, $y)
                        $frameWriter.Write([byte]$pixel.B)
                        $frameWriter.Write([byte]$pixel.G)
                        $frameWriter.Write([byte]$pixel.R)
                        $frameWriter.Write([byte]$pixel.A)
                    }
                }

                $frameWriter.Write([byte[]]::new($maskRowBytes * $size))
                $frameWriter.Flush()
                $images.Add($stream.ToArray())
            } finally {
                $frameWriter.Dispose()
                $stream.Dispose()
            }
        } finally {
            $bitmap.Dispose()
        }
    }
} finally {
    $source.Dispose()
}

$directorySize = 6 + (16 * $images.Count)
$offset = $directorySize
$outputDirectory = Split-Path -Parent $OutputIco
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

$file = [System.IO.File]::Open(
    [System.IO.Path]::GetFullPath($OutputIco),
    [System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::None)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$images.Count)

    for ($index = 0; $index -lt $images.Count; $index++) {
        $size = $sizes[$index]
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }

    foreach ($image in $images) {
        $writer.Write($image)
    }
} finally {
    $writer.Dispose()
    $file.Dispose()
}

Write-Output "Windows icon created: $([System.IO.Path]::GetFullPath($OutputIco))"
