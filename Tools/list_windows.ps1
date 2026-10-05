Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public static class WEnum2 {
  public delegate bool CB(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
}
'@
$cb = {
  param($h, $l)
  if ([WEnum2]::IsWindowVisible($h)) {
    $n = [WEnum2]::GetWindowTextLength($h)
    if ($n -gt 0) {
      $sb = New-Object System.Text.StringBuilder ($n + 2)
      [void][WEnum2]::GetWindowText($h, $sb, $sb.Capacity)
      $t = $sb.ToString()
      if ($t -match 'Unity|Save|Scene|Dialog|Untitled') { Write-Host ("{0} | {1}" -f $h, $t) }
    }
  }
  return $true
}
[WEnum2]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
Write-Host done
