Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class WinV {
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
[WinV]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -m 400
$r = New-Object WinV+RECT
[WinV]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$w = $r.R - $r.L
$h = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap($w, $h)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)

# Locate the vertical scrollbar (thumb color differs from card) by scanning a mid-height line.
$vx = -1
for ($x = 0; $x -lt $w; $x += 1) {
    $c = $bmp.GetPixel($x, [int]($h * 0.68))
    # look for the gray-ish thumb near the right part of the list (x < 75% width)
    if ($x -lt ($w * 0.75) -and $c.R -ne $c.G -and $c.G -ne $c.B) { }
    if ($x -gt ($w * 0.4) -and $x -lt ($w * 0.78)) {
        if (($c.R -eq $c.G) -and ($c.G -eq $c.B) -and $c.R -ge 150 -and $c.R -le 230) { $vx = $x; break }
    }
}
Write-Host ("window {0}x{1}, scrollbar thumb approx x={2}" -f $w, $h, $vx)
if ($vx -lt 0) { Write-Host 'no scrollbar found'; $vx = [int]($w * 0.72) }

# Scan the bottom-right corner region (below/right of the vertical scrollbar) with exact RGB.
$y0 = [int]($h * 0.90); $y1 = [int]($h * 0.97)
$x0 = $vx - 20; $x1 = $vx + 24
for ($y = $y0; $y -lt $y1; $y += 3) {
    $row = ""
    for ($x = $x0; $x -lt $x1; $x += 3) {
        $c = $bmp.GetPixel($x, $y)
        $row += ("[{0},{1}={2:X2}{3:X2}{4:X2}]" -f $x, $y, $c.R, $c.G, $c.B)
    }
    Write-Host ("y={0} {1}" -f $y, $row)
}
$g.Dispose()
$bmp.Dispose()