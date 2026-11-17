# -*- coding: utf-8 -*-
"""Screenshot a URL at given viewport using Edge headless + CDP over WebSocket.

usage: cdp_shot.py <url> <out.png> <width> <height>
"""
import base64, json, os, socket, struct, subprocess, sys, time, urllib.request

URL = sys.argv[1]
OUT = sys.argv[2]
W = int(sys.argv[3])
H = int(sys.argv[4])
PORT = 9333

EDGE = r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
if not os.path.exists(EDGE):
    EDGE = r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"

PROFILE = os.path.abspath("tools/edge-shot-profile")
os.makedirs(PROFILE, exist_ok=True)

proc = subprocess.Popen([
    EDGE,
    "--headless=new", "--no-sandbox", "--disable-gpu",
    "--disable-extensions", "--disable-background-networking",
    "--no-first-run", "--no-default-browser-check",
    "--remote-debugging-port=%d" % PORT,
    "--user-data-dir=" + PROFILE,
    "--window-size=%d,%d" % (W, H),
    "about:blank",
], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def http_json(path, timeout=5):
    url = "http://127.0.0.1:%d%s" % (PORT, path)
    return json.loads(urllib.request.urlopen(url, timeout=timeout).read().decode("utf-8"))


# wait for devtools
target = None
for _ in range(60):
    time.sleep(0.5)
    try:
        pages = http_json("/json/list")
        for p in pages:
            if p.get("type") == "page":
                target = p
                break
        if target:
            break
    except Exception:
        continue

if not target:
    proc.kill()
    print("ERROR: no devtools page target")
    sys.exit(1)

ws_url = target["webSocketDebuggerUrl"]


# --- minimal websocket client (RFC 6455, client->server masked) ---
def ws_connect(ws_url):
    from urllib.parse import urlparse
    u = urlparse(ws_url)
    sock = socket.create_connection((u.hostname, u.port), timeout=15)
    key = base64.b64encode(os.urandom(16)).decode()
    req = ("GET %s HTTP/1.1\r\nHost: %s:%d\r\nUpgrade: websocket\r\n"
           "Connection: Upgrade\r\nSec-WebSocket-Key: %s\r\n"
           "Sec-WebSocket-Version: 13\r\n\r\n") % (u.path, u.hostname, u.port, key)
    sock.sendall(req.encode())
    buf = b""
    while b"\r\n\r\n" not in buf:
        chunk = sock.recv(4096)
        if not chunk:
            break
        buf += chunk
    return sock


def ws_send(sock, payload):
    data = payload.encode("utf-8")
    mask = os.urandom(4)
    header = bytearray([0x81])  # FIN + text
    n = len(data)
    if n < 126:
        header.append(0x80 | n)
    elif n < 65536:
        header.append(0x80 | 126)
        header.extend(struct.pack(">H", n))
    else:
        header.append(0x80 | 127)
        header.extend(struct.pack(">Q", n))
    header.extend(mask)
    masked = bytes(b ^ mask[i % 4] for i, b in enumerate(data))
    sock.sendall(bytes(header) + masked)


def _recv_exact(sock, n):
    buf = b""
    while len(buf) < n:
        c = sock.recv(n - len(buf))
        if not c:
            raise IOError("closed")
        buf += c
    return buf


def ws_recv(sock):
    h = _recv_exact(sock, 2)
    opcode = h[0] & 0x0F
    ln = h[1] & 0x7F
    if ln == 126:
        ln = struct.unpack(">H", _recv_exact(sock, 2))[0]
    elif ln == 127:
        ln = struct.unpack(">Q", _recv_exact(sock, 8))[0]
    data = _recv_exact(sock, ln) if ln else b""
    if opcode == 0x9:  # ping -> pong
        mask = os.urandom(4)
        ph = bytearray([0x8A, 0x80 | len(data)])
        ph.extend(mask)
        sock.sendall(bytes(ph) + bytes(b ^ mask[i % 4] for i, b in enumerate(data)))
        return ws_recv(sock)
    return opcode, data


sock = ws_connect(ws_url)

_mid = [0]


def send_cmd(method, params=None):
    _mid[0] += 1
    msg = {"id": _mid[0], "method": method}
    if params:
        msg["params"] = params
    ws_send(sock, json.dumps(msg))
    while True:
        op, data = ws_recv(sock)
        if op != 1:
            continue
        resp = json.loads(data.decode("utf-8"))
        if resp.get("id") == msg["id"]:
            if "error" in resp:
                raise RuntimeError(str(resp["error"]))
            return resp.get("result", {})


try:
    send_cmd("Emulation.setDeviceMetricsOverride", {
        "width": W, "height": H, "deviceScaleFactor": 2,
        "mobile": W < 700, "screenWidth": W, "screenHeight": H,
    })
    send_cmd("Page.enable")
    send_cmd("Page.navigate", {"url": URL})
    time.sleep(6)  # let XHR poll fill real sensor data
    shot = send_cmd("Page.captureScreenshot",
                    {"format": "png", "fromSurface": True})
    png = base64.b64decode(shot["data"])
    with open(OUT, "wb") as f:
        f.write(png)
    print("saved %s (%d bytes)" % (OUT, len(png)))
finally:
    try:
        sock.close()
    except Exception:
        pass
    proc.kill()
    try:
        proc.wait(timeout=5)
    except Exception:
        pass
