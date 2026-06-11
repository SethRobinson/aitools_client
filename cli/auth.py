"""Optional per-server ComfyUI bearer-token auth.

Mirrors the Unity client (Config.cs GetComfyAuthToken/ApplyComfyAuth): a
configured server may carry a token (e.g. the bcrypt string printed by the
ComfyUI-Login custom node). config.parse_config registers tokens here; every
ComfyUI request/websocket then attaches an `Authorization: Bearer <token>`
header. Servers with no token are unaffected (helpers return empty).
"""

# Maps a configured server base URL (normalized: stripped, no trailing /) to
# its token. Populated while parsing config.txt, consulted by every request.
_TOKENS = {}


def register(server_url, token):
    """Associate a bearer token with a configured server base URL."""
    if server_url and token:
        _TOKENS[server_url] = token


def reset():
    """Forget all registered tokens (mainly for tests / re-parsing)."""
    _TOKENS.clear()


def token_for(any_url):
    """Token for whichever registered server URL is the longest prefix of
    any_url (so a /prompt or /view request URL still resolves), or None."""
    if not any_url or not _TOKENS:
        return None
    best = None
    for base in _TOKENS:
        if any_url.startswith(base) and (best is None or len(base) > len(best)):
            best = base
    return _TOKENS[best] if best is not None else None


def headers_for(any_url):
    """`requests` header dict: {} or {'Authorization': 'Bearer <token>'}."""
    token = token_for(any_url)
    return {"Authorization": f"Bearer {token}"} if token else {}


def ws_header_for(any_url):
    """websocket-client header list: [] or ['Authorization: Bearer <token>'].

    ComfyUI-Login checks the Authorization header before the ?token query
    param, so the header alone authenticates the ws:// connection.
    """
    token = token_for(any_url)
    return [f"Authorization: Bearer {token}"] if token else []
