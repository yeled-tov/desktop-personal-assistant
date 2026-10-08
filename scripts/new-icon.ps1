$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class DesktopAssistantIconNative { [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr handle); }'
$appRoot = Split-Path -Parent $PSScriptRoot
$iconPath = Join-Path $appRoot 'src\DesktopAssistant.App\app.ico'
if (-not (Test-Path $iconPath)) {
$bitmap = [System.Drawing.Bitmap]::new(64, 64)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(99, 91, 255))
$font = [System.Drawing.Font]::new('Segoe UI', 36, [System.Drawing.FontStyle]::Bold)
$graphics.DrawString('D', $font, [System.Drawing.Brushes]::White, 12, 5)
$iconHandle = $bitmap.GetHicon()
try { $stream = [System.IO.File]::Create($iconPath); [System.Drawing.Icon]::FromHandle($iconHandle).Save($stream); $stream.Dispose() }
finally { $graphics.Dispose(); $font.Dispose(); $bitmap.Dispose(); [void][DesktopAssistantIconNative]::DestroyIcon($iconHandle) }
}
