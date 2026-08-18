Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinC {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

$p = Get-Process -Name 'Configuration Management' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) {
    Start-Process -FilePath 'Configuration Management\bin\Debug\net10.0-windows\Configuration Management.exe'
    Start-Sleep -Seconds 5
    $p = Get-Process -Name 'Configuration Management' -ErrorAction SilentlyContinue | Select-Object -First 1
}
if (-not $p) { Write-Host 'NO WINDOW'; exit }
[WinC]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -m 400
$r = New-Object WinC+RECT
[WinC]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$w = $r.R - $r.L
$h = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)

# Find the vertical scrollbar x by scanning a line at mid-height.
$c = $null
$vx = -1
for ($x = 0; $x -lt $w; $x += 1) {
    $c = $bmp.GetPixel($x, 800)
    if ($c.R -eq 192 -and $c.G -eq 192 -and $c.B -eq 192) { $vx = $x; break }
}
Write-Host ("window {0}x{1}, vscroll first 192 at x={2}" -f $w, $h, $vx)
if ($vx -lt 0) { $vx = 1140 }

# Fine 2D grid around the bottom-right corner of the list scroll area.
$y0 = [int]($h * 0.9); $y1 = [int]($h * 0.97)
$x0 = $vx - 40; $x1 = $vx + 40
Write-Host ("region x {0}..{1}, y {2}..{3}" -f $x0, $x1, $y0, $y1)
for ($y = $y0; $y -lt $y1; $y += 2) {
    $row = ""
    for ($x = $x0; $x -lt $x1; $x += 2) {
        $c = $bmp.GetPixel($x, $y)
        $lum = [int](0.3 * $c.R + 0.59 * $c.G + 0.11 * $c.B)
        if ($c.R -eq $c.G -and $c.G -eq $c.B) { $ch = '{0}' -f [char]([int]('0' + [math]::Min(9, [math]::Floor($lum / 26)))) }
        else { $ch = 'R' }
        $row += $ch
    }
    Write-Host ("y={0,4} {1}" -f $y, $row)
}
Write-Host "scale: 0..9 = gray levels (0=black,9=white), R=color"
$g.Dispose()
$bmp.Dispose()