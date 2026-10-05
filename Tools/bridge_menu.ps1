# bridge_menu.ps1 — 单参包装：把菜单路径转成桥的 JSON 再发（免 bash→PS 引号纠缠）
# 用法：powershell -NoProfile -File Tools/bridge_menu.ps1 -Menu "Tools/Pool/99 测试跳水"
param([Parameter(Mandatory=$true)][string]$Menu, [int]$Port = 6400)

$json = '{"id":"m1","action":"runMenuItem","params":{"menuPath":"' + $Menu + '"}}'

$ws = [System.Net.WebSockets.ClientWebSocket]::new()
$ct = [System.Threading.CancellationToken]::None
try {
    [void]$ws.ConnectAsync("ws://127.0.0.1:$Port", $ct).GetAwaiter().GetResult()
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    [void]$ws.SendAsync([System.ArraySegment[byte]]::new($bytes), [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).GetAwaiter().GetResult()
    $buf = New-Object byte[] 131072
    $ms = [System.IO.MemoryStream]::new()
    do {
        $res = $ws.ReceiveAsync([System.ArraySegment[byte]]::new($buf), $ct).GetAwaiter().GetResult()
        $ms.Write($buf, 0, $res.Count)
    } while (-not $res.EndOfMessage)
    Write-Output ([System.Text.Encoding]::UTF8.GetString($ms.ToArray()))
}
finally {
    try { [void]$ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "done", $ct).GetAwaiter().GetResult() } catch {}
    $ws.Dispose()
}
