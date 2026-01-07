# Create Icon.bmp from logo.png for MonoGame
$pngPath = "logo.png"
$bmpPath = "Icon.bmp"

Add-Type -AssemblyName System.Drawing

# Load the PNG image
$png = [System.Drawing.Image]::FromFile($pngPath)

# Create a 32-bit ARGB bitmap (required for MonoGame)
$bmp = New-Object System.Drawing.Bitmap($png.Width, $png.Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

# Draw the PNG onto the bitmap
$graphics = [System.Drawing.Graphics]::FromImage($bmp)
$graphics.DrawImage($png, 0, 0, $png.Width, $png.Height)
$graphics.Dispose()

# Save as BMP
$bmp.Save($bmpPath, [System.Drawing.Imaging.ImageFormat]::Bmp)

# Cleanup
$png.Dispose()
$bmp.Dispose()

Write-Host "Successfully created $bmpPath from $pngPath"

