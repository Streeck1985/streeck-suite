# =========================================================================
# Genera assets\streeck.ico (icono de la suite y de la bandeja) a partir del diseño del usuario (assets\streeck_512.png).
# La clase Icon de .NET (la que usa la bandeja) no sabe leer entradas PNG pequeñas, así que se reescriben
# como mapas de bits de 32 bits; solo la de 256 px se queda en PNG.
# Ejecutar solo al cambiar el diseño; el .ico resultante se incluye en el proyecto.
# =========================================================================

Add-Type -AssemblyName System.Drawing

$assets = Join-Path $PSScriptRoot "..\assets"
$source512 = Join-Path $assets "streeck_512.png"

function Resize-Bitmap([System.Drawing.Bitmap]$src, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, 0, 0, $size, $size)
    $g.Dispose()
    return $bmp
}

function ConvertTo-IconDib([System.Drawing.Bitmap]$bmp) {
    $size = $bmp.Width
    $ms = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $ms
    # BITMAPINFOHEADER; la altura cuenta dos veces (imagen + máscara AND)
    $w.Write([UInt32]40); $w.Write([Int32]$size); $w.Write([Int32]($size * 2)); $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]0); $w.Write([UInt32]0); $w.Write([Int32]0); $w.Write([Int32]0); $w.Write([UInt32]0); $w.Write([UInt32]0)
    for ($y = $size - 1; $y -ge 0; $y--) {          # Filas de abajo arriba, píxeles BGRA
        for ($x = 0; $x -lt $size; $x++) {
            $c = $bmp.GetPixel($x, $y)
            $w.Write([byte]$c.B); $w.Write([byte]$c.G); $w.Write([byte]$c.R); $w.Write([byte]$c.A)
        }
    }
    $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)  # Máscara AND a cero (la transparencia la da el alfa)
    $w.Write((New-Object byte[] ($maskRow * $size)))
    $w.Flush()
    return , $ms.ToArray()
}

$big = New-Object System.Drawing.Bitmap $source512

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($size in $sizes) {
    # Todos los tamaños se reducen desde el de 512 px: las versiones pequeñas del .ico de origen
    # (16-24 px) traen una sola "S" y el usuario quiere las dos capas también en la bandeja
    $bmp = Resize-Bitmap $big $size
    if ($size -ge 256) {
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $data = $ms.ToArray()
    } else {
        $data = ConvertTo-IconDib $bmp
    }
    $bmp.Dispose()
    , $data
}

# Formato ICO: cabecera, una entrada de 16 bytes por tamaño y las imágenes a continuación
$out = Join-Path $assets "streeck.ico"
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$images[$i].Length); $w.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Close()
Write-Host "Icono generado: $out"
