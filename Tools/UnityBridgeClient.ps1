# UnityBridgeClient.ps1 — 发送 JSON 命令到 Unity 编辑器桥（ws://127.0.0.1:6400）
# 用法：
#   .\Tools\UnityBridgeClient.ps1 -Command '{"id":"1","action":"ping","params":{}}'
#   .\Tools\UnityBridgeClient.ps1 -Action createScene -Params @{ name="Main"; savePath="Scenes" }
# 说明：这是 Unity MCP（gamedevtoi）同一协议的底层客户端，用于在无 VS Code 环境下驱动 Unity。

param(
    [string]$Command = "",
    [string]$Action = "",
    [hashtable]$Params = @{},
    [int]$Port = 6400,
    [int]$TimeoutSec = 120
)

function Send-BridgeMessage([string]$json, [int]$port) {
    $ws = [System.Net.WebSockets.ClientWebSocket]::new()
    $ct = [System.Threading.CancellationToken]::None
    try {
        [void]$ws.ConnectAsync("ws://127.0.0.1:$port", $ct).GetAwaiter().GetResult()
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
        $seg = [System.ArraySegment[byte]]::new($bytes)
        [void]$ws.SendAsync($seg, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, $ct).GetAwaiter().GetResult()

        $buf = New-Object byte[] 131072
        $ms = [System.IO.MemoryStream]::new()
        do {
            $res = $ws.ReceiveAsync([System.ArraySegment[byte]]::new($buf), $ct).GetAwaiter().GetResult()
            $ms.Write($buf, 0, $res.Count)
        } while (-not $res.EndOfMessage)
        return [System.Text.Encoding]::UTF8.GetString($ms.ToArray())
    }
    finally {
        try { [void]$ws.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, "done", $ct).GetAwaiter().GetResult() } catch {}
        $ws.Dispose()
    }
}

# 等待端口就绪
$deadline = (Get-Date).AddSeconds($TimeoutSec)
while (-not (Test-NetConnection -ComputerName 127.0.0.1 -Port $Port -InformationLevel Quiet -WarningAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 1500
}
if ((Get-Date) -ge $deadline) { Write-Error "Bridge 端口 $Port 在 ${TimeoutSec}s 内未就绪"; exit 1 }

if (-not $Command) {
    $p = @{}
    foreach ($k in $Params.Keys) { $p[$k] = $Params[$k] }
    $body = @{ id = [guid]::NewGuid().ToString("N"); action = $Action; params = $p } | ConvertTo-Json -Depth 6 -Compress
    $Command = $body
}

$resp = Send-BridgeMessage $Command $Port
Write-Output $resp