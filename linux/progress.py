"""Single-line in-place status display + WebSocket progress watcher."""
import json
import sys

import websocket

import auth

WS_RECV_TIMEOUT = 120


class StatusLine:
    """Overwriteable single line via \\r. Pads with spaces to clear leftovers."""

    def __init__(self):
        self.last_len = 0

    def write(self, text, final=False):
        pad = max(0, self.last_len - len(text))
        sys.stdout.write("\r" + text + (" " * pad))
        if final:
            sys.stdout.write("\n")
            self.last_len = 0
        else:
            self.last_len = len(text)
        sys.stdout.flush()


def connect_ws(server_url, client_id):
    ws_url = (server_url
              .replace("http://", "ws://", 1)
              .replace("https://", "wss://", 1)
              + f"/ws?clientId={client_id}")
    ws = websocket.WebSocket()
    ws.settimeout(WS_RECV_TIMEOUT)
    ws.connect(ws_url, header=auth.ws_header_for(server_url))
    return ws


def watch_progress(ws, prompt_id, node_titles, label, verbose=False):
    """Block until job `prompt_id` finishes. Returns None on success or an
    error string on failure."""
    status = StatusLine()
    current_node = ""
    seen_our_job = False

    while True:
        try:
            msg = ws.recv()
        except websocket.WebSocketTimeoutException:
            continue
        except (websocket.WebSocketConnectionClosedException, ConnectionResetError):
            return "websocket closed unexpectedly"

        if isinstance(msg, (bytes, bytearray)):
            continue
        try:
            evt = json.loads(msg)
        except (ValueError, TypeError):
            continue

        t = evt.get("type")
        data = evt.get("data") or {}
        evt_pid = data.get("prompt_id")

        if t == "executing":
            if evt_pid is not None and evt_pid != prompt_id:
                continue
            seen_our_job = True
            node = data.get("node")
            if node is None:
                status.write(f"[{label}] done", final=True)
                return None
            current_node = node_titles.get(str(node), str(node))
            status.write(f"[{label}] {current_node}")

        elif t == "progress":
            if not seen_our_job:
                continue
            value = data.get("value", 0)
            maxv = data.get("max", 0) or 1
            pct = int(value * 100 / maxv)
            label_node = current_node or "..."
            status.write(f"[{label}] {label_node}  step {value}/{maxv}  ({pct}%)")

        elif t == "execution_cached":
            if evt_pid is not None and evt_pid != prompt_id:
                continue
            seen_our_job = True
            if verbose:
                nodes = data.get("nodes") or []
                status.write(f"[{label}] using {len(nodes)} cached node(s)")

        elif t == "execution_start":
            if evt_pid is None or evt_pid == prompt_id:
                seen_our_job = True
                status.write(f"[{label}] starting...")

        elif t == "execution_error":
            if evt_pid is not None and evt_pid != prompt_id:
                continue
            err = data.get("exception_message") or "unknown error"
            status.write(f"[{label}] ERROR", final=True)
            return err

        elif t == "execution_success":
            if evt_pid is None or evt_pid == prompt_id:
                status.write(f"[{label}] done", final=True)
                return None

        elif t == "status" and verbose:
            rem = ((data.get("status") or {}).get("exec_info") or {}).get("queue_remaining")
            if rem is not None and not seen_our_job:
                status.write(f"[{label}] waiting in queue (remaining: {rem})")
