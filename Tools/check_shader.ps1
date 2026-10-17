# check_shader.ps1 — 只看"这一轮新产生"的 shader 编译错误（PowerShell 版 check_shader_errors.sh）
#
# 教训（《05》★ 同源）：shader compiler 日志是**追加**的，同一份 shader 的旧错误会一直留在
# 文件里 —— 若不做**基线增量**对比，就会把三小时前修掉的错误当成当前错误，白查半天。
# 另一条：一次刷新会**新起**若干 unityShaderCompiler 子进程并新建各自日志，
# 只 diff 基线里已有的文件会漏掉新进程 → 必须遍历"现在存在的全部日志"。
#
# 用法:
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tools/check_shader.ps1 -Mode mark     # 改代码前先记住位置
#   powershell -NoProfile -ExecutionPolicy Bypass -File Tools/check_shader.ps1 -Mode report   # 报告新增硬错误
param([string]$Mode = "report")
$Snap = "Logs/.shader_baseline.json"
$Logs = Get-ChildItem Logs\shadercompiler-UnityShaderCompiler.exe*.log -ErrorAction SilentlyContinue

function Read-Shared([string]$p) {
  $fs=[System.IO.File]::Open($p,'Open','Read','ReadWrite')
  $sr=New-Object System.IO.StreamReader($fs); $t=$sr.ReadToEnd(); $sr.Close(); $fs.Close(); return $t
}

if ($Mode -eq "mark") {
  $base = @{}
  foreach ($f in $Logs) { $base[$f.Name] = $f.Length }
  $base | ConvertTo-Json | Set-Content $Snap -Encoding utf8
  "baseline marked : $($Logs.Count) compiler logs"
  exit 0
}

if (-not (Test-Path $Snap)) { "no baseline — 先跑 -Mode mark"; exit 2 }
$base = (Get-Content $Snap -Raw | ConvertFrom-Json)
$hits = @()
foreach ($f in $Logs) {
  $before = 0; if ($base.PSObject.Properties[$f.Name]) { $before = [int]$base.($f.Name) }
  if ((Test-Path $f.FullName) -and ($f.Length -gt $before)) {
    $txt = Read-Shared $f.FullName
    $new = if ($txt.Length -gt $before) { $txt.Substring($before) } else { $txt }
    foreach ($L in (($new -split "`r?`n") | Where-Object { $_ -match 'error: 2' })) {
      $i = $L.IndexOf('error: 2'); $head = [regex]::Match($L,'file=\S+ name=\S+ pass=\S+').Value
      $hits += "+$($f.Length-$before) | $head | " + $L.Substring($i, [Math]::Min(120, $L.Length-$i))
    }
  }
}
if ($hits.Count) { "shader errors: FAIL"; $hits | Select-Object -First 10; exit 1 }
"shader errors: clean (no new hard errors since baseline)"
