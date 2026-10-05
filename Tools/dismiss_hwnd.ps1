param([long]$Hwnd, [string]$Keys = "{ESC}")
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class WHit {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
}
'@
Add-Type -AssemblyName System.Windows.Forms
$h = [IntPtr]$Hwnd
if ([WHit]::IsWindowVisible($h)) {
  [void][WHit]::SetForegroundWindow($h)
  Start-Sleep -Milliseconds 400
  [System.Windows.Forms.SendKeys]::SendWait($Keys)
  Write-Host ("sent {0} to hwnd {1}" -f $Keys, $Hwnd)
} else {
  Write-Host "window gone"
}
