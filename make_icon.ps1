Add-Type -AssemblyName System.Drawing

$dir = "d:\Downloads\myCodes\projects\verci\src\VerciWin.App\Assets"
if (!(Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force }
$icoPath = Join-Path $dir "TrayIcon.ico"

$bmp = New-Object System.Drawing.Bitmap 32, 32
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)

# Dark background circle
$brushBg = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(230, 26, 26, 46))
$g.FillEllipse($brushBg, 1, 1, 30, 30)

# Accent ring
$penAccent = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 144, 144, 200), 2)
$g.DrawEllipse($penAccent, 2, 2, 28, 28)

# Center letter V
$font = New-Object System.Drawing.Font("Arial", 14, [System.Drawing.FontStyle]::Bold)
$brushText = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 232, 232, 240))
$sf = New-Object System.Drawing.StringFormat
$sf.Alignment = [System.Drawing.StringAlignment]::Center
$sf.LineAlignment = [System.Drawing.StringAlignment]::Center
$rect = New-Object System.Drawing.RectangleF(0, 0, 32, 32)
$g.DrawString("V", $font, $brushText, $rect, $sf)

$g.Dispose()

$hIcon = $bmp.GetHicon()
$icon = [System.Drawing.Icon]::FromHandle($hIcon)
$fs = New-Object System.IO.FileStream($icoPath, [System.IO.FileMode]::Create)
$icon.Save($fs)
$fs.Close()
$icon.Dispose()
$bmp.Dispose()

Write-Host "TrayIcon.ico generated at $icoPath"
