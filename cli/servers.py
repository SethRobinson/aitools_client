"""Probe ComfyUI servers via /queue and pick the lowest-loaded one."""
import requests

import auth
from util import die

QUEUE_PROBE_TIMEOUT = 2.0


def probe_server(url):
    """Return current queue depth (running + pending), or None if unreachable."""
    try:
        r = requests.get(f"{url}/queue", headers=auth.headers_for(url),
                         timeout=QUEUE_PROBE_TIMEOUT)
        if r.status_code != 200:
            return None
        d = r.json()
        return len(d.get("queue_running", [])) + len(d.get("queue_pending", []))
    except Exception:
        return None


def pick_server(servers, verbose=False):
    """Return (url, depth) of the reachable server with the lowest queue."""
    best = None
    for url in servers:
        depth = probe_server(url)
        if verbose:
            status = "unreachable" if depth is None else f"queue {depth}"
            print(f"  {url}: {status}")
        if depth is None:
            continue
        if best is None or depth < best[1]:
            best = (url, depth)
    if best is None:
        die("no reachable ComfyUI servers", 2)
    return best
