"""Load workflow JSON, convert to API format (with on-disk cache), apply
@replace directives, substitute <AITOOLS_*> placeholders, override seeds."""
import json
from pathlib import Path

import requests

from util import die

HTTP_TIMEOUT = 30

PLACEHOLDERS_BLANK_BY_DEFAULT = [
    "<AITOOLS_AUDIO_PROMPT>",
    "<AITOOLS_AUDIO_NEGATIVE_PROMPT>",
    "<AITOOLS_SEGMENTATION_PROMPT>",
] + [f"<AITOOLS_INPUT_{i}>" for i in range(1, 5)] \
  + [f"<AITOOLS_PROMPT_{i}>" for i in range(1, 9)]


def looks_like_full_workflow(data):
    return isinstance(data, dict) and isinstance(data.get("nodes"), list)


def load_or_convert_workflow(workflow_dir: Path, workflow_name: str,
                             server_url: str, force: bool, verbose: bool):
    """Return (api_dict, source_path). Caches converted JSON next to source."""
    src = workflow_dir / workflow_name
    if not src.exists():
        die(f"workflow not found: {src}", 1)
    src_text = src.read_text()
    src_data = json.loads(src_text)

    if not looks_like_full_workflow(src_data):
        if verbose:
            print(f"workflow {src.name} is already in API format")
        return src_data

    cache = src.with_name(src.stem + "_cached_api_version.json")
    if not force and cache.exists() and cache.stat().st_mtime >= src.stat().st_mtime:
        if verbose:
            print(f"using cached API workflow: {cache.name}")
        return json.loads(cache.read_text())

    if verbose:
        print(f"converting {src.name} -> API format via {server_url}")
    try:
        r = requests.post(
            f"{server_url}/workflow/convert",
            data=src_text.encode("utf-8"),
            headers={"Content-Type": "application/json"},
            timeout=HTTP_TIMEOUT,
        )
    except requests.RequestException as e:
        die(f"workflow conversion request failed: {e}", 2)
    if r.status_code != 200:
        die(
            "workflow conversion failed (is the comfyui-workflow-to-api-converter-endpoint "
            f"custom node installed?): HTTP {r.status_code}\n{r.text[:500]}",
            2,
        )
    api_text = r.text
    cache.write_text(api_text)
    if verbose:
        print(f"cached: {cache.name}")
    return json.loads(api_text)


def apply_replaces(api_workflow, replaces, verbose=False):
    """Apply a list of (find, replace) substitutions to the workflow.
    Done on the JSON-as-string to mirror PicTextToImage.cs:584-594.
    Returns the (possibly re-parsed) workflow dict."""
    if not replaces:
        return api_workflow
    text = json.dumps(api_workflow)
    for find, repl in replaces:
        if find not in text:
            print(f"warning: @replace could not find '{find}' in workflow")
            continue
        text = text.replace(find, repl)
        if verbose:
            print(f"  @replace applied: {_short(find)} -> {_short(repl)}")
    return json.loads(text)


def _short(s, n=60):
    s = s.replace("\n", "\\n")
    return s if len(s) <= n else s[:n - 1] + "…"


def replace_placeholders(node, replacements):
    if isinstance(node, dict):
        return {k: replace_placeholders(v, replacements) for k, v in node.items()}
    if isinstance(node, list):
        return [replace_placeholders(v, replacements) for v in node]
    if isinstance(node, str):
        for ph, val in replacements.items():
            if ph in node:
                node = node.replace(ph, val)
        return node
    return node


def override_seeds(node, seed):
    if isinstance(node, dict):
        for k, v in node.items():
            if k in ("seed", "noise_seed") and isinstance(v, int) and not isinstance(v, bool):
                node[k] = seed
            else:
                override_seeds(v, seed)
    elif isinstance(node, list):
        for v in node:
            override_seeds(v, seed)


def build_node_titles(api_workflow):
    titles = {}
    if not isinstance(api_workflow, dict):
        return titles
    for node_id, node in api_workflow.items():
        if not isinstance(node, dict):
            continue
        meta_title = (node.get("_meta") or {}).get("title")
        titles[str(node_id)] = meta_title or node.get("class_type") or str(node_id)
    return titles
