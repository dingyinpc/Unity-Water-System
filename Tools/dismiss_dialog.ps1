param([string]$TitleLike = "Save Scene", [string]$Keys = "{ESC}")
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class WDlg {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
}
'@
Add-Type -AssemblyName System.Windows.Forms
$hit = [IntPtr]::Zero
$cb = {
  param($h, $l)
  if ([WDlg]::IsWindowVisible($h) -and $hit -eq [IntPtr]::Zero) {
    $n = [WDlg]::GetWindowTextLength($h)
    if ($n -gt 0) {
      $sb = New-Object System.Text.StringBuilder ($n + 2)
      [void][WDlg]::GetWindowText($h, $sb, $sb.Capacity)
      if ($sb.ToString().IndexOf($TitleLike, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { $hit = $h; return $false }
    }
  }
  return $true
}
[WDlg]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
if ($hit -ne [IntPtr]::Zero) {
  [void][WDlg]::SetForegroundWindow($hit)
  Start-Sleep -Milliseconds 300
  [System.Windows.Forms.SendKeys]::SendWait($Keys)
  Write-Host ("sent '{0}' to '{1}' hwnd={2}" -f $Keys, $TitleLike, $hit)
} else {
  Write-Host "dialog not found"
}
