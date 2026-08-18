Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinF {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

$p = Get-Process -Name 'Configuration Management' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Host 'NO WINDOW'; exit }
[WinF]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -m 400
$r = New-Object WinF+RECT
[WinF]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$w = $r.R - $r.L
$h = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)

# Fine scan along horizontal lines to find vertical scrollbar (right edge of list)
foreach ($y in 600, 800, 1000, 1100, 1140) {
    $line = ""
    for ($x = 500; $x -lt $w; $x += 3) {
        $c = $bmp.GetPixel($x, $y)
        $lum = [int](0.3 * $c.R + 0.59 * $c.G + 0.11 * $c.B)
        $line += ("{0}" -f $lum).PadLeft(3)
    }
    Write-Host ("Y={0}" -f $y)
    Write-Host $line
}
$g.Dispose()
$bmp.Dispose()