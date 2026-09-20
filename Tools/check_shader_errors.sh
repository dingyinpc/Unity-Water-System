#!/usr/bin/env bash
# 只看"这一轮新产生"的 shader 编译错误。
#
# 为什么不用 Editor.log：
#   ① 它在本机被多个 Unity 编辑器共用，会混进别的工程的消息；
#   ② 更致命的是它会**停止增长**（编辑器不 flush 时你等到天黑也没有），
#      于是"日志里没有错误"根本不能当结论用。
#   Logs/shadercompiler-UnityShaderCompiler.exeN.log 由编译子进程各自写，
#   每编一个 shader 追加一条，是实时通道。
#
# 用法:
#   Tools/check_shader_errors.sh mark     # 记一个基线（改文件之前跑）
#   Tools/check_shader_errors.sh          # 报告自基线以来新追加的行里的错误
#
# ⚠ 这些日志是**追加**的，同一份 shader 的旧错误会一直留在文件里。
#   所以"grep 到错误"必须先确认它在基线之后，否则会追一个已经修掉的鬼。
set -euo pipefail
cd "$(dirname "$0")/.."
SNAP="/tmp/shadererr_baseline.txt"

if [[ "${1:-}" == "mark" ]]; then
    for f in Logs/shadercompiler-UnityShaderCompiler.exe*.log; do
        [[ -e "$f" ]] && echo "$(wc -c < "$f") $f"
    done > "$SNAP"
    echo "baseline marked ($(wc -l < "$SNAP") compiler logs)"
    exit 0
fi

[[ -e "$SNAP" ]] || { echo "no baseline — run: $0 mark" >&2; exit 2; }

found=0
while read -r before f; do
    [[ -e "$f" ]] || continue
    now=$(wc -c < "$f")
    (( now <= before )) && continue
    new=$(tail -c +$((before + 1)) "$f")
    hits=$(printf '%s\n' "$new" | grep -a "error:" || true)
    if [[ -n "$hits" ]]; then
        found=1
        echo "### $f (+$((now - before)) bytes)"
        printf '%s\n' "$hits" | sed -E 's/.*(file=[^ ]+ [^ ]*)?.*(error: .*)/\2/' | sort -u
    fi
done < "$SNAP"

if (( found )); then
    echo "=> shader errors: FAIL"
    exit 1
fi
echo "=> shader errors: clean (no new error lines since baseline)"
