<#
  rea.ps1 —— REA（Reverse Engineering Assistant v4.0.0）包装脚本
  ------------------------------------------------------------------
  用途：在本项目内直接调用 REA 的 CLI（二进制取证 / 字符串 / 反编译 / 交叉引用 / 一键报告）。
  固化路径：E:\Git hub close\REA\bin\rea.exe（2026-10-16 从 rea-src\target\release 拷出，
  防止 cargo clean 或移动源码目录后 MCP/CLI 失效；MCP 配置已指向该路径）。
  MCP：OpenCode 全局配置已登记 rea MCP（重启 OpenCode 后生效），共 12 个工具。
  详细说明：E:\Git hub close\REA\REA-使用说明.md

  用法示例（在项目根目录执行；本机默认执行策略受限，需用 -ExecutionPolicy Bypass）：
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/rea.ps1 --version
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/rea.ps1 info <file>            # 文件头/哈希/缓解措施/节区
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/rea.ps1 strings <file>         # 提取字符串与安全指标
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/rea.ps1 decompile <file> [sym] # 反编译为伪 C
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/rea.ps1 xrefs <file> <addr>    # 交叉引用
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/rea.ps1 <file>                 # 一键完整分析（旁边生成 re-<name>.md/pdf）

  注意：视频、AssetBundle、GPU Shader 字节码不适合 REA（视频对照走抽帧 + 像素比对流程）。
#>

$candidates = @(
    'E:\Git hub close\REA\bin\rea.exe',
    'E:\Git hub close\REA\rea-src\target\release\rea.exe'
)
$exe = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $exe) {
    Write-Error '未找到 rea.exe（期望 E:\Git hub close\REA\bin\rea.exe）；请检查或重新编译 REA。'
    exit 1
}

& $exe @args
exit $LASTEXITCODE
