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
# ⚠ 必须遍历"现在存在的全部日志"，而不是基线里那几份。
#   一次刷新会**新起**若干 UnityShaderCompiler.exeN 子进程并新建各自的日志，
#   只 diff 基线里已有的文件，就会把新进程里的错误整个漏掉 ——
#   实测 Sky_Tropical 的编译记录同时落在基线里没有的 exe1/exe2 里，
#   而检查照样报了 clean。基线里没有的文件 = 全新文件 = 偏移量按 0 算。
for f in Logs/shadercompiler-UnityShaderCompiler.exe*.log; do
    [[ -e "$f" ]] || continue
    before=$(awk -v p="$f" '$2==p {print $1; f=1} END {if(!f) print 0}' "$SNAP")
    now=$(wc -c < "$f")
    (( now <= before )) && continue
    new=$(tail -c +$((before + 1)) "$f")
    hits=$(printf '%s\n' "$new" | grep -a "error:" || true)
    if [[ -n "$hits" ]]; then
        echo "### $f (+$((now - before)) bytes)"
        # Unity 的分级写在消息里：`error: 1` = 警告，`error: 2` = 硬失败。
        # 两者都含 "error:" 子串，混在一起会把"pow 底数可能为负"这种警告报成 FAIL，
        # 于是这个通道很快就不值得信了 —— 必须分开。
        hard=$(printf '%s\n' "$hits"   | grep -a "error: 2" || true)
        warn=$(printf '%s\n' "$hits"   | grep -a "error: 1" || true)
        [[ -n "$warn" ]] && { echo "  WARN:"; printf '%s\n' "$warn" | sed -E 's/.*(error: 1 .*)/    \1/' | sort -u; }
        if [[ -n "$hard" ]]; then
            found=1
            echo "  ERROR:"
            printf '%s\n' "$hard" | sed -E 's/.*(error: 2 .*)/    \1/' | sort -u
        fi
    fi
done

if (( found )); then
    echo "=> shader errors: FAIL"
    exit 1
fi
echo "=> shader errors: clean (no new error lines since baseline)"
