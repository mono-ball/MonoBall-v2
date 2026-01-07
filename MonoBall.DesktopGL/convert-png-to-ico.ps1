# Convert PNG to ICO format with multiple sizes
$pngPath = "logo.png"
$icoPath = "logo.ico"

Add-Type -AssemblyName System.Drawing

# Load the PNG image
$png = [System.Drawing.Image]::FromFile($pngPath)

# Define icon sizes (Windows ICO format supports multiple sizes)
$sizes = @(16, 32, 48, 256)

# Create memory stream for ICO file
$iconStream = New-Object System.IO.MemoryStream
$iconWriter = New-Object System.IO.BinaryWriter($iconStream)

# Write ICO header
$iconWriter.Write([UInt16]0)  # Reserved (must be 0)
$iconWriter.Write([UInt16]1)   # Type (1 = ICO)
$iconWriter.Write([UInt16]$sizes.Count)  # Number of images

# Calculate offset to image data
$offset = 6 + ($sizes.Count * 16)

# Write directory entries
foreach ($size in $sizes) {
    $resized = New-Object System.Drawing.Bitmap($png, $size, $size)
    $pngStream = New-Object System.IO.MemoryStream
    $resized.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $pngStream.ToArray()
    
    # Width (0 = 256)
    if ($size -eq 256) {
        $iconWriter.Write([Byte]0)
    } else {
        $iconWriter.Write([Byte]$size)
    }
    
    # Height (0 = 256)
    if ($size -eq 256) {
        $iconWriter.Write([Byte]0)
    } else {
        $iconWriter.Write([Byte]$size)
    }
    
    $iconWriter.Write([Byte]0)  # Color palette (0 = no palette)
    $iconWriter.Write([Byte]0)  # Reserved
    $iconWriter.Write([UInt16]1)  # Color planes
    $iconWriter.Write([UInt16]32)  # Bits per pixel
    $iconWriter.Write([UInt32]$pngBytes.Length)  # Size of image data
    $iconWriter.Write([UInt32]$offset)  # Offset to image data
    
    $offset += $pngBytes.Length
    $resized.Dispose()
    $pngStream.Dispose()
}

# Write image data
$iconStream.Position = $offset
foreach ($size in $sizes) {
    $resized = New-Object System.Drawing.Bitmap($png, $size, $size)
    $pngStream = New-Object System.IO.MemoryStream
    $resized.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytes = $pngStream.ToArray()
    $iconStream.Write($pngBytes, 0, $pngBytes.Length)
    $resized.Dispose()
    $pngStream.Dispose()
}

# Write ICO file
[System.IO.File]::WriteAllBytes($icoPath, $iconStream.ToArray())

# Cleanup
$png.Dispose()
$iconStream.Dispose()

Write-Host "Successfully converted $pngPath to $icoPath"

