Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinM {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

$p = Get-Process -Name 'Configuration Management' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Host 'NO WINDOW'; exit }
[WinM]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -m 400
$r = New-Object WinM+RECT
[WinM]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$w = $r.R - $r.L
$h = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)

# Coarse ASCII luminance map of the bottom-left region (list area).
$step = 12
$y0 = [int]($h * 0.45)
$y1 = [int]($h * 0.99)
$x1 = [int]($w * 0.62)
for ($y = $y0; $y -lt $y1; $y += $step) {
    $row = ""
    for ($x = 0; $x -lt $x1; $x += $step) {
        $c = $bmp.GetPixel($x, $y)
        $lum = (0.3 * $c.R + 0.59 * $c.G + 0.11 * $c.B)
        if ($lum -gt 220) { $ch = '#' }
        elseif ($lum -gt 150) { $ch = '+' }
        elseif ($lum -gt 90) { $ch = '.' }
        elseif ($lum -gt 40) { $ch = '-' }
        else { $ch = ' ' }
        $row += $ch
    }
    Write-Host ("y={0,4} {1}" -f $y, $row)
}
$g.Dispose()
$bmp.Dispose()