param(
 [string]$Source = "$PSScriptRoot\..\src\SongSense.App\Assets\SongSense.png",
 [string]$Destination = "$PSScriptRoot\..\src\SongSense.App\Assets\SongSense.ico"
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$sourceImage = [System.Drawing.Image]::FromFile([System.IO.Path]::GetFullPath($Source))
$frames = [System.Collections.Generic.List[byte[]]]::new()
$sizes = @(16,24,32,48,64,128,256)
try {
 foreach ($size in $sizes) {
  $bitmap = [System.Drawing.Bitmap]::new($size,$size,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $stream = [System.IO.MemoryStream]::new()
  try {
   $graphics.Clear([System.Drawing.Color]::Transparent)
   $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
   $graphics.DrawImage($sourceImage,0,0,$size,$size)
   $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
   $frames.Add($stream.ToArray())
  } finally { $graphics.Dispose(); $bitmap.Dispose(); $stream.Dispose() }
 }
} finally { $sourceImage.Dispose() }
$file = [System.IO.File]::Create([System.IO.Path]::GetFullPath($Destination))
$writer = [System.IO.BinaryWriter]::new($file)
try {
 $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
 $offset = 6 + 16 * $frames.Count
 for ($index=0; $index -lt $frames.Count; $index++) {
  $dimension = if ($sizes[$index] -eq 256) { 0 } else { $sizes[$index] }
  $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
  $writer.Write([byte]0); $writer.Write([byte]0)
  $writer.Write([uint16]1); $writer.Write([uint16]32)
  $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
  $offset += $frames[$index].Length
 }
 foreach ($frame in $frames) { $writer.Write($frame) }
} finally { $writer.Dispose(); $file.Dispose() }
Write-Output "Icono generado: $Destination"
