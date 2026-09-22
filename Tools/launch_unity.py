r"""启动本工程自己的 Unity 编辑器。

为什么要专门写一个：
  · 工程路径里有空格**和中文**。`Start-Process -ArgumentList "字符串"` 会按空格切开，
    于是 Unity 拿到的是 `-projectPath E:\Unity`。
    **改成数组形式仍然不够**（第一次就栽在这个"已经修好了"的判断上）：
    Start-Process 把数组用空格拼成命令行时**不会**给单个元素加引号，
    所以每个元素自己必须已经带着一对双引号 —— 见下面 PS 常量的注释。
    失败时它不报错、不建目录，只 `return 0` 退出，所以必须有 `-logFile` 才看得见。
  · 中文再经 bash → cmd → powershell 会被 GBK 重编码一次。走 `-EncodedCommand`
    （UTF-16LE 的 base64）就完全绕开控制台代码页。

用法: python Tools/launch_unity.py
"""
import base64
import subprocess
import sys
import time

UNITY = r"E:\unity\2022.3.57f1c2\Editor\Unity.exe"
PROJ = r"E:\Unity xianmu\Unity3D风景的项目立项"

LOGDIR = r"E:\Blender\tools"   # 必须纯 ASCII：整条命令行会被 GBK 代码页重编码一次

PS = (
    # ★ 每个元素外面还要**自己套一层双引号**：`-ArgumentList @('a','b c')` 会被
    #   Start-Process 用空格拼成一条命令行**且不自动加引号**，
    #   于是工程路径在空格处断成两个 argv —— 实测 Unity 收到
    #   `-projectPath E:\Unity` + `xianmu\...`，然后它"成功切换"到
    #   `<工程>/E:/Unity` 这个不存在的路径并 return 0 退出（不报错、不建目录）。
    "$p = Start-Process -FilePath '%s' -ArgumentList @('-projectPath','%s',"
    "'-logFile','%s') -PassThru "
    "-RedirectStandardOutput '%s' "
    "-RedirectStandardError '%s'\n"
    "Write-Output ('PID=' + $p.Id)\n"
    % (UNITY, '"%s"' % PROJ, '"%s"' % (LOGDIR + r"\unity_launch.log"),
       LOGDIR + r"\unity_stdout.log", LOGDIR + r"\unity_stderr.log")
)


def main():
    out = subprocess.run(
        ["powershell", "-NoProfile", "-EncodedCommand",
         base64.b64encode(PS.encode("utf-16-le")).decode()],
        capture_output=True, text=True)
    print(out.stdout.strip() or out.stderr.strip())
    if "PID=" not in out.stdout:
        return 1
    pid = int(out.stdout.split("PID=")[1].split()[0])
    # 编辑器冷启动 + 脚本重编译要一两分钟，先等窗口出现再交回控制权
    for _ in range(60):
        time.sleep(5)
        q = subprocess.run(
            ["powershell", "-NoProfile", "-Command",
             "(Get-Process -Id %d -ErrorAction SilentlyContinue).MainWindowTitle" % pid],
            capture_output=True, text=True)
        title = q.stdout.strip()
        if title and "Unity" in title:
            print("window ready: PID=%d  %s" % (pid, title))
            return 0
    print("等不到窗口（PID=%d）—— 可能卡在工程选择/导入弹窗" % pid)
    return 2


if __name__ == "__main__":
    sys.exit(main())
