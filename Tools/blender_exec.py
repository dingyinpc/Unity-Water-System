"""直接驱动 Blender 的 MCP 插件，绕开 MCP 工具层。

为什么要有这个：会话里 `blender_*` 工具可能不在（MCP 服务没注册 / 没 /mcp reload /
本轮就是整个 blender server 都不在列表里）。但插件本身在 Blender 里监听
127.0.0.1:9876，协议很短 —— 一条 **\\0 结尾的 JSON**：

    请求 {"type":"execute","code":"<python>","strict_json":true}
    响应 {"status":"ok","result":{...}}

代码在 Blender 主线程执行，返回值靠**给 `result` 赋一个 dict**（`return` 会 SyntaxError，
`result` 不是 dict 会被打回）。这两条是从插件源码
`extensions/user_default/mcp/mcp_to_blender_server.py` 里读出来的，不是猜的。

用法:
    python Tools/blender_exec.py path/to/code.py      # 发一个文件
    echo 'result={"ok":1}' | python Tools/blender_exec.py -
"""
import json
import socket
import sys

HOST, PORT = "127.0.0.1", 9876


def exec_code(code, timeout=1800):
    req = json.dumps({"type": "execute", "code": code, "strict_json": True}) + "\0"
    s = socket.create_connection((HOST, PORT), timeout=timeout)
    s.settimeout(timeout)
    try:
        s.sendall(req.encode("utf-8"))
        buf = b""
        while b"\0" not in buf:
            chunk = s.recv(65536)
            if not chunk:
                break
            buf += chunk
    finally:
        s.close()
    if not buf:
        raise RuntimeError("Blender 没有回包（插件没起 / 主线程被卡住）")
    return json.loads(buf.split(b"\0", 1)[0].decode("utf-8"))


def main():
    arg = sys.argv[1] if len(sys.argv) > 1 else "-"
    code = sys.stdin.read() if arg == "-" else open(arg, encoding="utf-8").read()
    try:
        resp = exec_code(code)
    except ConnectionRefusedError:
        print("9876 没人监听 —— Blender 没开，或开的时候没带 BLENDER_USER_RESOURCES"
              "（裸跑 blender.exe 会读 %APPDATA%，那份配置里没装 mcp 扩展，"
              "插件不加载而且不报任何错）", file=sys.stderr)
        return 2
    print(json.dumps(resp, ensure_ascii=False, indent=2))
    return 0 if resp.get("status") == "ok" else 1


if __name__ == "__main__":
    sys.exit(main())
