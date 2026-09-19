#!/usr/bin/env bash
# 只看上次快照之后的 Editor.log —— 历史行号全是过期编译的错误，会误导
# 用法: check_compile.sh [快照文件]
LOG="/c/Users/18689/AppData/Local/Unity/Editor/Editor.log"
POS_FILE="${1:-Logs/.editorlog_pos}"
pos=$(cat "$POS_FILE" 2>/dev/null || echo 0)
size=$(wc -c < "$LOG")
echo "log grew $((size - pos)) bytes since snapshot"
tail -c +"$pos" "$LOG" | tr -d '\000' | grep -E "error CS[0-9]+|CompilationPipeline|Scripts have compile errors|No errors" | tail -20
echo "$size" > "$POS_FILE"
