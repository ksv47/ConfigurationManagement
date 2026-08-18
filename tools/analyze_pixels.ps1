Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

$p = Get-Process -Name 'Configuration Management' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Host 'NO WINDOW'; exit }
[Win]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -m 500
$r = New-Object Win+RECT
[Win]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
Write-Host ("WINDOW L={0} T={1} R={2} B={3}" -f $r.L, $r.T, $r.R, $r.B)

# Re-capture fresh screenshot of the window client area
$w = $r.R - $r.L
$h = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$bmp.Save('f:\ya\Yandex.Disk\h\Configuration_Management_test\window.png')
Write-Host ("SIZE {0}x{1}" -f $w, $h)

# Sample a horizontal line at bottom (near bottom edge) to detect the light square
$y = $h - 8   # just above bottom border
$line = ""
for ($x = $w - 120; $x -lt $w; $x += 4) {
    $c = $bmp.GetPixel($x, $y)
    $line += ("[{0},{1}:{2:X2}{3:X2}{4:X2}] " -f $x, $y, $c.R, $c.G, $c.B)
}
Write-Host ("LINE y={0}: {1}" -f $y, $line)

# Vertical line at right edge to find the square
$x = $w - 6
$vline = ""
for ($yy = $h - 60; $yy -lt $h; $yy += 3) {
    $c = $bmp.GetPixel($x, $yy)
    $vline += ("[{0},{1}:{2:X2}{3:X2}{4:X2}] " -f $x, $yy, $c.R, $c.G, $c.B)
}
Write-Host ("VLINE x={0}: {1}" -f $x, $vline)

$g.Dispose()
$bmp.Dispose()