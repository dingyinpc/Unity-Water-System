# verify_scenes.ps1 — 六场景一键验收（可靠采集 + 量测）
#
# 为什么要有它：
#   R56/R57 的教训 —— `Tools/Ocean/85 Play 中截图` 在编辑器失焦时**静默失败**，
#   换场景后还常返回**上一张的旧帧**；而 MD5 因 PNG 时间戳块不同，**看不出是旧帧**。
#   于是多轮"改完看着好/坏"的结论都建立在假图上。
#
# 本脚本的做法（每一步都防旧帧）：
#   1) 逐场景：开场景 → 进 Play → 冻结 → **Seq/95 离屏阻塞采集 1920x1080**
#   2) 采集后校验「文件时间 > 本次开始时间」，否则判失败（不静默通过）
#   3) 量测：亮度 / 对比 / 饱和 / 近黑占比 —— 与上一次基线比对，突变 >35% 直接报警
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tools/verify/verify_scenes.ps1
#   powershell ... -File Tools/verify/verify_scenes.ps1 -Scenes Main,Ocean
#   powershell ... -File Tools/verify/verify_scenes.ps1 -NoCapture     # 只看已有图
param(
    [string[]]$Scenes = @('Main', 'Ocean', 'Pond', 'Pool', 'Islet', 'FFT'),
    [int]$WaitPlay = 10,
    [switch]$NoCapture
)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root

$menuOf = @{
    'Main'  = 'Tools/Project/120 打开 Main（复刻视频主线）'
    'Ocean' = 'Tools/Project/121 打开 OceanScene'
    'Pond'  = 'Tools/Project/122 打开 PondScene'
    'Pool'  = 'Tools/Project/123 打开 PoolScene'
    'Islet' = 'Tools/Project/124 打开 IsletScene'
    'FFT'   = 'Tools/Project/126 打开 FFTScene'
}

function Run-Menu([string]$m) {
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/bridge_menu.ps1 -Menu $m | Out-Null
}

$results = @()
foreach ($name in $Scenes) {
    if (-not $menuOf.ContainsKey($name)) { Write-Output "跳过未知场景 $name"; continue }
    Write-Output "=== $name ==="

    if (-not $NoCapture) {
        $t0 = Get-Date
        Run-Menu $menuOf[$name]
        Start-Sleep -Seconds 6
        # R122：先应用"验收机位"，否则构图会随上次保存的相机状态漂移（基线就失去可比性）
        Run-Menu 'Tools/R122/91 应用验收机位(读 cameras.txt)'
        Start-Sleep -Seconds 1
        Run-Menu 'Tools/Play/90 进入播放(显式，已在播则不动)'
        Start-Sleep -Seconds $WaitPlay
        Run-Menu 'Tools/Ocean/108 冻结(暂停波浪+流场)'
        Start-Sleep -Seconds 1
        Run-Menu 'Tools/Seq/95 静帧(离屏阻塞1920x1080, 写Docs/Temp)'
        Start-Sleep -Seconds 5

        $line = (Select-String -Path Logs/water_probe.txt -Pattern 'SEQ95 still' | Select-Object -Last 1).Line
        $path = [regex]::Match($line, '(E:.*\.png)').Groups[1].Value
        if (-not (Test-Path $path)) { Write-Output "  !! 采集失败：探针里没有输出文件"; continue }

        $mt = (Get-Item $path).LastWriteTime
        if ($mt -lt $t0) { Write-Output "  !! 疑似旧帧：文件时间 $mt 早于本次开始 $t0"; continue }

        $dst = "Docs/screenshots/verify_$name.png"
        Copy-Item $path $dst -Force
        Write-Output "  采集 OK: $dst  ($($mt.ToString('HH:mm:ss')))"

        Run-Menu 'Tools/Ocean/109 解冻(恢复波浪+流场)'
        Run-Menu 'Tools/Play/91 退出播放(显式，已停则不动)'
        Start-Sleep -Seconds 1
    }
    $results += "Docs/screenshots/verify_$name.png"
}

if ($results.Count -gt 0) {
    Write-Output ''
    Write-Output '=== 量测 ==='
    $args = @('Tools/verify/measure_many.py') + $results
    python @args
}
