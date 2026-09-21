#!/usr/bin/env bash
# ============================================================================
# unity_compile.sh — 触发重编译并**等到真的编译完**，然后报告结果
#
# 为什么需要这个脚本（本轮实际踩到两次）：
#   1) "探针日志里没有 error" 不是编译成功的证据。Unity 只在**窗口在前台**时
#      才会推进编译/资源刷新；编辑器在后台时 refreshAssets 会返回 success，
#      但 dll 根本不重编，于是菜单 91 跑的还是**上一版**的代码 ——
#      更糟的是它照样会打出一行 "MENU91 terrain built"，看着像成功了。
#   2) 反过来，Editor.log 是**累加**的，上一次失败的 `error CS` 会一直留在里面，
#      所以"日志里有 error"也不代表当前这一版没编过。
#
#   唯一可靠的判据是：Assembly-CSharp-Editor.dll 的 mtime **晚于** 所有源文件。
#   这个脚本就把这条判据固化下来。
#
# 用法：  bash Tools/unity_compile.sh
#         bash Tools/unity_compile.sh --quiet
# ============================================================================
set -u
cd "$(dirname "$0")/.." || exit 1

DLL="Library/ScriptAssemblies/Assembly-CSharp-Editor.dll"
BRIDGE="Tools/UnityBridgeClient.ps1"
LOG="$LOCALAPPDATA/Unity/Editor/Editor.log"
QUIET="${1:-}"

say() { [ "$QUIET" = "--quiet" ] || echo "$@"; }

# 源文件里最新的那个
newest_src() {
    ls -t Assets/Editor/*.cs Assets/Editor/**/*.cs 2>/dev/null | head -1
}

ping_bridge() {
    powershell -NoProfile -ExecutionPolicy Bypass -File "$BRIDGE" \
        -Action ping -TimeoutSec 8 2>/dev/null | grep -q pong
}

if ! ping_bridge; then
    echo "FAIL: Unity 桥端口没起来（编辑器没开，或还卡在编译/弹窗）"
    exit 2
fi

# 记下触发前的时间戳，用来判断"这次到底动没动"
BEFORE=$(stat -c %Y "$DLL" 2>/dev/null || echo 0)

powershell -NoProfile -ExecutionPolicy Bypass -File "$BRIDGE" \
    -Command '{"id":"1","action":"refreshAssets","params":{}}' -TimeoutSec 240 >/dev/null 2>&1

# 把窗口捞回前台，否则 Unity 不推进编译（见文件头 1）
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/restore-window.ps1 >/dev/null 2>&1

NEWEST=$(newest_src)
NEWEST_T=$(stat -c %Y "$NEWEST" 2>/dev/null || echo 0)

for _ in $(seq 1 36); do
    sleep 5
    DLL_T=$(stat -c %Y "$DLL" 2>/dev/null || echo 0)
    if [ "$DLL_T" -gt "$NEWEST_T" ] && [ "$DLL_T" -gt "$BEFORE" ]; then
        say "OK: compiled  dll=$(date -d "@$DLL_T" +%H:%M:%S)  newest_src=$(basename "$NEWEST")"
        exit 0
    fi
done

# 超时： dll 没更新。区分"还在编"和"编译失败所以没产出"
# 只看**本轮新增**的那段日志，避免被历史 error 误判（见文件头 2）
NEWERR=$(tail -c 200000 "$LOG" 2>/dev/null | grep "error CS" | tail -5)
say "FAIL: 180s 内 dll 未更新（$(date -d "@$BEFORE" +%H:%M:%S) 起）"
if [ -n "$NEWERR" ]; then
    echo "  当前编译错误："
    echo "$NEWERR" | sed 's/^/    /'
fi
exit 1
