"""Probe ComfyUI servers via /queue and pick the lowest-loaded one."""
import random
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
    """Return (url, depth) of a reachable server with the lowest queue.

    Among servers tied for the lowest depth we choose RANDOMLY rather than
    always taking the first reachable one. Otherwise, when many jobs launch
    at the same instant (e.g. a build script nohup-firing 14 renders at
    once), every client probes BEFORE any of them submits, sees all queues
    equal, and deterministically piles onto the same server — serializing
    the whole batch on one GPU while the other cards idle. Random
    tie-breaking spreads a simultaneous burst ~evenly across the
    equal-lowest servers; when one card is genuinely busier it still has a
    higher depth and is correctly avoided.
    """
    reachable = []
    for url in servers:
        depth = probe_server(url)
        if verbose:
            status = "unreachable" if depth is None else f"queue {depth}"
            print(f"  {url}: {status}")
        if depth is not None:
            reachable.append((url, depth))
    if not reachable:
        die("no reachable ComfyUI servers", 2)
    low = min(depth for _, depth in reachable)
    return random.choice([pair for pair in reachable if pair[1] == low])
