# 启动本工程编辑器。存在的理由：路径里有空格，命令行传参已经被咬过两次
#   · Start-Process -ArgumentList @('-projectPath','E:\…\风景…')  → 空格处被拆开，
#     Unity 报 "Couldn't set project path to: E:/Unity xianmu/…" 然后 return code 0 退出
#   · 在 bash 里再套一层反引号转义 → 引号被带进路径尾部，变成 …/"E:/Unity
# 所以只在**文件里**写引号，不经 shell 二次转义。
$ErrorActionPreference = 'Stop'
$exe  = 'E:\unity\2022.3.57f1c2\Editor\Unity.exe'
$proj = 'E:\Unity xianmu\Unity3D风景的项目立项'
if (-not (Test-Path $exe)) { throw "找不到编辑器: $exe" }
if (-not (Test-Path (Join-Path $proj 'ProjectSettings'))) { throw "不像一个 Unity 工程: $proj" }

$running = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {
    $_.CommandLine -like '*Unity3D*' -and $_.CommandLine -notlike '*AssetImportWorker*'
}
if ($running) {
    "ALREADY RUNNING pid=" + ($running.ProcessId -join ',')
    exit 0
}

# -logFile 显式钉到本工程 Logs：共享的 %LOCALAPPDATA%\Unity\Editor\Editor.log
# 会被同时开着的别的工程覆盖，出问题时取不到自己那段。
Start-Process -FilePath $exe -ArgumentList @(
    '-projectPath', ('"' + $proj + '"'),
    '-logFile', ('"' + (Join-Path $proj 'Logs\Editor-thisproject.log') + '"')
)
"STARTED"
