"""Parser for linux/config.txt — `key|value` lines, # comments, blank lines."""
from pathlib import Path

import auth
from util import die


def parse_config(path: Path):
    cfg = {"default_workflow": None, "servers": []}
    if not path.exists():
        example = path.with_name("config.example.txt")
        hint = (f" — copy {example.name} to {path.name} and edit it"
                if example.exists() else "")
        die(f"config not found: {path}{hint}", 1)
    for raw in path.read_text().splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        parts = line.split("|")
        key = parts[0].strip()
        if key == "default_workflow" and len(parts) >= 2:
            cfg["default_workflow"] = parts[1].strip()
        elif key == "add_server" and len(parts) >= 2:
            url = parts[1].strip().rstrip("/")
            cfg["servers"].append(url)
            # Any later field starting with "token=" is an optional bearer
            # token for a protected ComfyUI (e.g. the ComfyUI-Login node).
            # Other extra fields (e.g. a display name) are ignored here.
            for extra in parts[2:]:
                extra = extra.strip()
                if extra.startswith("token="):
                    auth.register(url, extra[len("token="):].strip())
    return cfg
