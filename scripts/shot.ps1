param(
  [Parameter(Mandatory)][string]$TitleLike,
  [Parameter(Mandatory)][string]$Out,
  [int]$Pad = 0
)
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;using System.Runtime.InteropServices;using System.Text;
public class Win {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int cb);
  public struct RECT { public int L, T, R, B; }
  public static RECT Frame(IntPtr h) { RECT r; DwmGetWindowAttribute(h, 9, out r, Marshal.SizeOf(typeof(RECT))); return r; }
}
'@
$hit = [IntPtr]::Zero
$cb = [Win+EnumProc]{ param($h, $l)
  if ([Win]::IsWindowVisible($h)) {
    $sb = New-Object System.Text.StringBuilder 256
    [Win]::GetWindowText($h, $sb, 256) | Out-Null
    if ($sb.ToString() -like "*$TitleLike*") { $script:hit = $h; return $false }
  }
  return $true
}
[Win]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
if ($hit -eq [IntPtr]::Zero) { throw "no visible window matching '$TitleLike'" }
$r = [Win]::Frame($hit)
$x = $r.L - $Pad; $y = $r.T - $Pad
$w = ($r.R - $r.L) + 2 * $Pad; $h2 = ($r.B - $r.T) + 2 * $Pad
$bmp = New-Object System.Drawing.Bitmap($w, $h2)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
"saved $Out  ($w x $h2)"
