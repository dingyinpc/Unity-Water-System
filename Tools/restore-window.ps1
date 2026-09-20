param([long]$Hwnd = 0, [string]$TitleLike = "Unity3D风景的项目立项")
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class Win {
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
  public static IntPtr Find(string like) {
    IntPtr hit = IntPtr.Zero;
    EnumWindows((h, l) => {
      if (!IsWindowVisible(h)) return true;
      int n = GetWindowTextLength(h); if (n <= 0) return true;
      var sb = new StringBuilder(n + 1); GetWindowText(h, sb, sb.Capacity);
      if (sb.ToString().IndexOf(like, StringComparison.OrdinalIgnoreCase) >= 0) { hit = h; return false; }
      return true;
    }, IntPtr.Zero);
    return hit;
  }
}
"@
if ($Hwnd -eq 0) {
  $found = [Win]::Find($TitleLike)
  if ($found -eq [IntPtr]::Zero) { Write-Output "没找到标题含「$TitleLike」的窗口"; exit 4 }
  $Hwnd = [long]$found
  Write-Output "按标题认领窗口:$TitleLike -> $Hwnd"
}
$h = [IntPtr]$Hwnd
Write-Output ("before iconic: " + [Win]::IsIconic($h))
[Win]::ShowWindow($h, 9) | Out-Null      # SW_RESTORE
Start-Sleep -Milliseconds 600
[Win]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 600
Write-Output ("after  iconic: " + [Win]::IsIconic($h))
Write-Output ("foreground   : " + [Win]::GetForegroundWindow())
