"""Parser for Presets/*.txt files used by the Unity app.

Supports the subset relevant to single-step text-to-image:
  - COMMAND_START|joblist           (one workflow line + %var% assigns)
  - COMMAND_START|default_prompt    (used iff user prompt is empty)
  - COMMAND_START|default_negative_prompt
  - COMMAND_START|default_pre_prompt
  - COMMAND_START|default_post_prompt
  - @replace|find|with|             (only directive supported)

Anything outside this subset (LLM, image upload, multi-job chains, etc.)
produces a clear, specific error so the user knows why their preset can't run.
"""
import re
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional, List, Tuple, Dict

from util import die


PRESETS_DIR = Path(__file__).resolve().parent.parent / "Presets"

SUPPORTED_BLOCKS = {
    "joblist",
    "default_prompt",
    "default_negative_prompt",
    "default_pre_prompt",
    "default_post_prompt",
}
SILENTLY_IGNORED_BLOCKS = {"summarize_prompt", "recent_interactions"}

VAR_PATTERN = re.compile(r"%([a-zA-Z_][a-zA-Z0-9_]*)%")
VAR_ASSIGN_PATTERN = re.compile(r'^%([a-zA-Z_][a-zA-Z0-9_]*)%\s*=\s*(.*)$')

# Per-directive error reasons. Anything not in here AND not @replace
# is reported as "unknown directive".
UNSUPPORTED_DIRECTIVE_REASONS = {
    "upload":             "needs server image upload (input images) — not yet supported by aitools_cli",
    "setimage":           "needs Unity image-slot manipulation — not supported by aitools_cli",
    "resize":             "needs image processing — not yet supported by aitools_cli",
    "resize_if_larger":   "needs image processing — not yet supported by aitools_cli",
    "invert_alpha":       "needs image processing — not yet supported by aitools_cli",
    "fill_mask_if_blank": "needs Unity mask handling — not supported by aitools_cli",
    "copy":               "needs cross-job variable mutation — aitools_cli runs a single workflow",
    "add":                "needs cross-job variable mutation — aitools_cli runs a single workflow",
    "set":                "needs cross-job variable mutation — aitools_cli runs a single workflow",
    "clear":              "needs cross-job variable mutation — aitools_cli runs a single workflow",
    "stopjob":            "only meaningful inside the Unity job pipeline",
    "no_undo":            "only meaningful inside the Unity job pipeline",
    "lock_gpu":           "needs Unity GPU locking — not supported by aitools_cli",
    "parse_llm_prompts":  "needs LLM integration — not supported by aitools_cli",
}


@dataclass
class PresetData:
    source_path: Path
    workflow: str
    replaces: List[Tuple[str, str]] = field(default_factory=list)
    variables: Dict[str, str] = field(default_factory=dict)
    default_prompt: Optional[str] = None
    default_negative_prompt: Optional[str] = None
    default_pre_prompt: Optional[str] = None
    default_post_prompt: Optional[str] = None


def resolve_preset_path(name_or_path: str) -> Path:
    """Resolve a preset reference: literal path, or name relative to ../Presets,
    with .txt auto-appended if missing."""
    p = Path(name_or_path)
    candidates = [p]
    if not p.is_absolute():
        candidates.append(PRESETS_DIR / name_or_path)
    extra = []
    for c in candidates:
        if c.suffix.lower() != ".txt":
            extra.append(c.with_suffix(c.suffix + ".txt") if c.suffix else c.with_suffix(".txt"))
    candidates.extend(extra)
    for c in candidates:
        if c.exists():
            return c
    die(f"preset not found: {name_or_path} (looked in {PRESETS_DIR} too)", 1)


def load_preset(name_or_path: str) -> PresetData:
    path = resolve_preset_path(name_or_path)
    text = path.read_text()
    blocks = _split_blocks(text, path)

    data = PresetData(source_path=path, workflow="")

    for name, body in blocks:
        if name in SILENTLY_IGNORED_BLOCKS:
            continue
        if name not in SUPPORTED_BLOCKS:
            die(
                f"preset {path.name}: unsupported block 'COMMAND_START|{name}' — "
                f"aitools_cli only handles {sorted(SUPPORTED_BLOCKS)}",
                1,
            )
        if name == "joblist":
            _parse_joblist(body, data, path)
        elif name == "default_prompt":
            data.default_prompt = body.strip()
        elif name == "default_negative_prompt":
            data.default_negative_prompt = body.strip()
        elif name == "default_pre_prompt":
            data.default_pre_prompt = body.strip()
        elif name == "default_post_prompt":
            data.default_post_prompt = body.strip()

    if not data.workflow:
        die(f"preset {path.name}: joblist block has no workflow line", 1)

    return data


def _split_blocks(text: str, path: Path):
    """Yield (name, body) tuples for each COMMAND_START|name ... COMMAND_END block."""
    out = []
    pattern = re.compile(
        r"COMMAND_START\|(?P<name>[^\n|]+)\n(?P<body>.*?)COMMAND_END",
        re.DOTALL,
    )
    last_end = 0
    for m in pattern.finditer(text):
        # Sanity: any non-whitespace text between blocks is suspect
        between = text[last_end:m.start()].strip()
        if between and not all(line.strip().startswith("#") or not line.strip()
                               for line in between.splitlines()):
            die(f"preset {path.name}: stray text between command blocks: {between[:100]!r}", 1)
        out.append((m.group("name").strip(), m.group("body")))
        last_end = m.end()
    if not out:
        die(f"preset {path.name}: no COMMAND_START blocks found", 1)
    # Check for a dangling COMMAND_START with no matching COMMAND_END
    if "COMMAND_START" in text[last_end:]:
        die(f"preset {path.name}: COMMAND_START without matching COMMAND_END", 1)
    return out


def _parse_joblist(body: str, data: PresetData, path: Path):
    workflow_lines = []
    for raw in body.splitlines():
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        if line.startswith("command "):
            die(
                f"preset {path.name}: joblist contains a 'command ...' line "
                f"({_short(line)}) — aitools_cli does not support multi-step "
                f"command sequences (LLM, image ops, etc.)",
                1,
            )
        if line == "call_llm":
            die(
                f"preset {path.name}: joblist contains 'call_llm' — "
                f"LLM integration is not supported by aitools_cli",
                1,
            )
        m = VAR_ASSIGN_PATTERN.match(line)
        if m:
            name = m.group(1)
            rhs = m.group(2).strip()
            if (rhs.startswith('"') and rhs.endswith('"')) or \
               (rhs.startswith("'") and rhs.endswith("'")):
                rhs = rhs[1:-1]
            data.variables[name] = rhs
            continue
        workflow_lines.append(line)

    if len(workflow_lines) > 1:
        die(
            f"preset {path.name}: joblist has {len(workflow_lines)} workflow "
            f"lines — aitools_cli only supports single-step presets. "
            f"First two lines: {_short(workflow_lines[0])} | {_short(workflow_lines[1])}",
            1,
        )
    if not workflow_lines:
        die(f"preset {path.name}: joblist has no workflow line", 1)

    _parse_workflow_line(workflow_lines[0], data, path)


def _parse_workflow_line(line: str, data: PresetData, path: Path):
    """Parse `<workflow.json> [@directive|args| ...]` into data.workflow + data.replaces."""
    # Split on whitespace-then-@ to keep @ args containing spaces intact.
    # Format is loose; the C# parser splits on '@' which means '@' inside
    # a @replace arg would break things — we follow the same loose convention.
    parts = line.split("@")
    head = parts[0].strip()
    if not head or not head.lower().endswith(".json"):
        die(
            f"preset {path.name}: joblist workflow line must start with a .json "
            f"filename — got: {_short(line)}",
            1,
        )
    data.workflow = head

    for chunk in parts[1:]:
        chunk = chunk.rstrip()
        if not chunk:
            continue
        # Form: directive|arg1|arg2|...|  (trailing | is conventional)
        pieces = chunk.split("|")
        directive = pieces[0].strip()
        # Trim trailing empty piece from terminator |
        args = pieces[1:]
        if args and args[-1] == "":
            args = args[:-1]
        _handle_directive(directive, args, data, path)


def _handle_directive(directive: str, args: List[str], data: PresetData, path: Path):
    d = directive.strip()
    if d == "replace":
        if len(args) != 2:
            die(
                f"preset {path.name}: @replace expects 2 args, got {len(args)}: "
                f"{args}",
                1,
            )
        data.replaces.append((args[0], args[1]))
        return
    if d in UNSUPPORTED_DIRECTIVE_REASONS:
        die(f"preset {path.name}: @{d} {UNSUPPORTED_DIRECTIVE_REASONS[d]}", 1)
    if d.startswith("llm_"):
        die(f"preset {path.name}: @{d} needs LLM integration — not supported by aitools_cli", 1)
    die(f"preset {path.name}: unknown directive '@{d}'", 1)


def substitute_variables(text: str, variables: Dict[str, str], verbose=False):
    """Replace %name% tokens. Unknown vars are left as-is (matches C#)."""
    if not text:
        return text
    def repl(m):
        name = m.group(1)
        if name in variables:
            return variables[name]
        if verbose:
            print(f"  warning: undefined variable %{name}% (left as-is)")
        return m.group(0)
    return VAR_PATTERN.sub(repl, text)


def expand_replaces(replaces, variables, verbose=False):
    """Apply %var% substitution to both args of every @replace pair."""
    return [
        (substitute_variables(f, variables, verbose),
         substitute_variables(w, variables, verbose))
        for (f, w) in replaces
    ]


def _short(s, n=80):
    s = s.replace("\n", "\\n")
    return s if len(s) <= n else s[:n - 1] + "…"
