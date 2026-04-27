"""Parser for linux/config.txt — `key|value` lines, # comments, blank lines."""
from pathlib import Path

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
            cfg["servers"].append(parts[1].strip().rstrip("/"))
    return cfg
