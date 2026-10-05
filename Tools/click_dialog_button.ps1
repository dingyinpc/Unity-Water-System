param([long]$Hwnd, [string]$ButtonLike = "Don't Save|Cancel")
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class WBtn {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
'@
$pat = $ButtonLike -split '\|'
$cb = {
  param($h, $l)
  if ([WBtn]::IsWindowVisible($h)) {
    $n = [WBtn]::GetWindowTextLength($h)
    if ($n -gt 0) {
      $sb = New-Object System.Text.StringBuilder ($n + 2)
      [void][WBtn]::GetWindowText($h, $sb, $sb.Capacity)
      $t = $sb.ToString()
      Write-Host ("  child {0} | '{1}'" -f $h, $t)
      foreach ($p in $pat) {
        if ($t -eq $p) {
          Write-Host ("  -> BM_CLICK '{0}'" -f $t)
          [void][WBtn]::SendMessage($h, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
          return $false
        }
      }
    }
  }
  return $true
}
[WBtn]::EnumChildWindows([IntPtr]$Hwnd, $cb, [IntPtr]::Zero) | Out-Null
Write-Host done
