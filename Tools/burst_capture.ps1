# burst_capture.ps1 — 单进程连拍：触发菜单后按固定间隔截图+落盘
# 为什么存在：每次 powershell 冷启动 ≈2s，桥菜单一来一回就把涟漪环 2s 的
# 可见窗口全吃光了。这里一次启动里发完整序列，间隔精确到 0.1s 量级。
# 用法：powershell -NoProfile -File Tools/burst_capture.ps1 -TriggerMenu "Tools/Pool/99 测试跳水" -Prefix "_pool_ring3"
param(
    [string]$TriggerMenu = "",
    [double]$TriggerToFirstShot = 1.15,   # 触发→第一张的间隔（跳水入水 ≈0.83s + 余量）
    [int]$Shots = 4,
    [double]$Interval = 0.55,
    [string]$Prefix = "_pool_burst",
    [int]$Port = 6400
)

$S = "Docs/screenshots"
function Send([string]$menu) {
    $json = '{"id":"b' + [System.Guid]::NewGuid().ToString("N").Substring(0,6) + '","action":"runMenuItem","params":{"menuPath":"' + $menu + '"}}'
    $ws = [System.Net.WebSockets.ClientWebSocket]::new()
    $ct = [System.Threading.CancellationToken]::None
    try {
        [void]$ws.ConnectAsync("ws://127.0.0.1:$Port", $ct).GetAwaiter().GetResult()
        $b = [System.Text.Encoding]::UTF8.GetBytes($json)
        [void]$ws.SendAsync([System.ArraySegment[byte]]::new($b), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).GetAwaiter().GetResult()
        $buf = New-Object byte[] 65536
        $ms = [System.IO.MemoryStream]::new()
        do { $r = $ws.ReceiveAsync([System.ArraySegment[byte]]::new($buf), $ct).GetAwaiter().GetResult(); $ms.Write($buf, 0, $r.Count) } while (-not $r.EndOfMessage)
        Write-Output ("  <- " + [System.Text.Encoding]::UTF8.GetString($ms.ToArray()))
    } finally { try { [void]$ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "d", $ct).GetAwaiter().GetResult() } catch {}; $ws.Dispose() }
}

$cap = "Tools/Pool/95 Play 中截图(离屏固定比例)"

$t0 = Get-Date
if ($TriggerMenu -ne "") {
    Write-Output ("TRIGGER " + $TriggerMenu)
    Send $TriggerMenu
    Start-Sleep -Milliseconds ([int]($TriggerToFirstShot * 1000))
}

for ($i = 0; $i -lt $Shots; $i++) {
    Send $cap
    Start-Sleep -Milliseconds ([int]($Interval * 1000))
    $dst = Join-Path $S ($Prefix + "_" + $i + ".png")
    Copy-Item (Join-Path $S "_pool_view.png") $dst -Force
    Write-Output ("SHOT " + $i + " -> " + $dst + "  t=+" + [int](((Get-Date) - $t0).TotalMilliseconds) + "ms")
}
Write-Output "DONE"
