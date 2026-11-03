# @name cap_fresh
# cap_fresh.ps1 — 采集一张"确实是新帧"的截图（R74 教训：Seq/95 可能复用旧帧）
# 用法（在别的脚本里 dot-source 后调用）：
#   . Tools/verify/cap_fresh.ps1
#   $p = Capture-Fresh -Out "Docs/screenshots/xxx.png" -Prev "Docs/screenshots/prev.png"
param(
    [string]$Out,
    [string]$Prev = ""
)

function Capture-Fresh {
    param([string]$Out, [string]$Prev = "")

    $menu = { param($m) powershell -NoProfile -ExecutionPolicy Bypass -File Tools/bridge_menu.ps1 -Menu $m | Out-Null }

    for ($try = 0; $try -lt 3; $try++) {
        & $menu 'Tools/Seq/95 静帧(离屏阻塞1920x1080, 写Docs/Temp)'
        Start-Sleep -Seconds 5
        $line = (Select-String -Path Logs/water_probe.txt -Pattern 'SEQ95 still' | Select-Object -Last 1).Line
        $src = [regex]::Match($line, '(E:.*\.png)').Groups[1].Value
        if (-not (Test-Path $src)) { continue }
        Copy-Item $src $Out -Force

        if (-not $Prev -or -not (Test-Path $Prev)) { return $Out }

        $diff = python -c "import sys,cv2,numpy as np; a=cv2.imdecode(np.fromfile(r'$Prev',np.uint8),1).astype(np.int16); b=cv2.imdecode(np.fromfile(r'$Out',np.uint8),1).astype(np.int16); print('%.2f'%((np.abs(a-b).sum(2)>8).mean()*100))"
        Write-Output "    try$($try+1): 与上一张差异 ${diff}%"
        if ([double]$diff -gt 0.05) { return $Out }
        Start-Sleep -Seconds 2
    }
    Write-Output "    !! 连续 3 次都拿到同一帧（可能是编辑器失焦/未进 Play）"
    return $Out
}
