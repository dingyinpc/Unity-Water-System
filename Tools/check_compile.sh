#!/usr/bin/env bash
# 编译判定。两个来源，各治一类盲区：
#   1) C# 错误看 Library/Bee/tundra.log.json —— 它是**本工程专属**的。
#      Editor.log 是所有 Unity 编辑器**共享**的（这台机器常开 4 个），会串台。
#      同时报它的年龄：日志很旧 = 根本没重新编译过，别把"没错误"当成"编译通过了"。
#   2) Shader 错误只看 Editor.log 的增量 —— shader 编译不进 Bee 日志。
#      历史行号全是过期错误，所以必须按上次快照位置截断。
# 用法: check_compile.sh [Editor.log 快照文件]
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BEE="$ROOT/Library/Bee/tundra.log.json"
LOG="/c/Users/18689/AppData/Local/Unity/Editor/Editor.log"
POS_FILE="${1:-$ROOT/Logs/.editorlog_pos}"

rc=0

if [ -f "$BEE" ]; then
  cs=$(grep -c -i -E "error cs[0-9]+" "$BEE" 2>/dev/null || true)
  # 判据不是"日志年不年轻"（没人改代码时它本来就该是旧的），
  # 而是"有没有源文件比日志更新" —— 有，才说明编译没跟上或被推迟（多半还在 Play 模式里）
  stale=$(find "$ROOT/Assets" -name "*.cs" -newer "$BEE" 2>/dev/null | head -5)
  echo "bee: cs errors ${cs:-0}"
  if [ -n "$stale" ]; then
    echo "  !! 这些源文件比构建日志新，编译没跟上（在 Play 模式里改代码会被推迟）:"
    echo "$stale" | sed 's|^|     |'
    rc=1
  fi
  if [ "${cs:-0}" -gt 0 ]; then
    grep -i -E "error cs[0-9]+" "$BEE" | tail -10
    rc=1
  fi
else
  echo "bee: tundra.log.json 不存在（Unity 从未编过？先刷新资产）"
  rc=1
fi

if [ -f "$LOG" ]; then
  pos=$(cat "$POS_FILE" 2>/dev/null || echo 0)
  size=$(wc -c < "$LOG")
  sh=$(tail -c +"$pos" "$LOG" | tr -d '\000' \
        | grep -E "Shader error in|undeclared identifier|maximum count" | tail -10)
  echo "editor.log: grew $((size - pos)) bytes"
  if [ -n "$sh" ]; then echo "$sh"; rc=1; fi
  echo "$size" > "$POS_FILE"
else
  echo "editor.log 读不到：$LOG"
fi

if [ "$rc" -eq 0 ]; then echo "=> clean"; else echo "=> 有问题，见上"; fi
exit $rc
