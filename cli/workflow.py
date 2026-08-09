"""Load workflow JSON, convert to API format (with on-disk cache), apply
@replace directives, substitute <AITOOLS_*> placeholders, override seeds."""
import json
from pathlib import Path

import requests

import auth
from util import die

HTTP_TIMEOUT = 30

PLACEHOLDERS_BLANK_BY_DEFAULT = [
    "<AITOOLS_AUDIO_PROMPT>",
    "<AITOOLS_AUDIO_NEGATIVE_PROMPT>",
    "<AITOOLS_SEGMENTATION_PROMPT>",
] + [f"<AITOOLS_INPUT_{i}>" for i in range(1, 12)] \
  + [f"<AITOOLS_PROMPT_{i}>" for i in range(1, 9)]


def looks_like_full_workflow(data):
    return isinstance(data, dict) and isinstance(data.get("nodes"), list)


def load_or_convert_workflow(workflow_dir: Path, workflow_name: str,
                             server_url: str, force: bool, verbose: bool):
    """Return (api_dict, source_path). Caches converted JSON next to source."""
    src = workflow_dir / workflow_name
    if not src.exists():
        die(f"workflow not found: {src}", 1)
    src_text = src.read_text(encoding="utf-8")
    src_data = json.loads(src_text)

    if not looks_like_full_workflow(src_data):
        if verbose:
            print(f"workflow {src.name} is already in API format")
        return src_data

    cache = src.with_name(src.stem + "_cached_api_version.json")
    if not force and cache.exists() and cache.stat().st_mtime >= src.stat().st_mtime:
        if verbose:
            print(f"using cached API workflow: {cache.name}")
        return json.loads(cache.read_text(encoding="utf-8"))

    if verbose:
        print(f"converting {src.name} -> API format via {server_url}")
    try:
        r = requests.post(
            f"{server_url}/workflow/convert",
            data=src_text.encode("utf-8"),
            headers={"Content-Type": "application/json",
                     **auth.headers_for(server_url)},
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
    cache.write_text(api_text, encoding="utf-8")
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


def prune_unfilled_inputs(api_workflow, verbose=False):
    """Remove loader nodes whose inputs still hold an <AITOOLS_INPUT_N>
    placeholder (an optional @upload slot with no source), cascade-remove
    inputs that referenced them, and renumber ComfyUI autogrow list inputs
    ("group.item_N") so indices stay contiguous from 0. Mirrors
    PicTextToImage.PruneWorkflowInputs in the Unity app. Call BEFORE the
    blank-by-default placeholder pass, which would otherwise erase the
    markers this detection relies on."""
    if not isinstance(api_workflow, dict):
        return api_workflow
    removed = []
    for node_id in list(api_workflow.keys()):
        node = api_workflow.get(node_id)
        inputs = node.get("inputs") if isinstance(node, dict) else None
        if not isinstance(inputs, dict):
            continue
        if any(isinstance(v, str) and "<AITOOLS_INPUT_" in v for v in inputs.values()):
            removed.append(node_id)
            del api_workflow[node_id]
    for node in api_workflow.values():
        inputs = node.get("inputs") if isinstance(node, dict) else None
        if not isinstance(inputs, dict):
            continue
        doomed = [k for k, v in inputs.items()
                  if isinstance(v, list) and len(v) == 2 and str(v[0]) in removed]
        for k in doomed:
            del inputs[k]
        if doomed:
            _renumber_autogrow_inputs(inputs)
    if removed and verbose:
        print(f"pruned unused loader node(s): {', '.join(removed)}")
    return api_workflow


def _renumber_autogrow_inputs(inputs):
    """ComfyUI autogrow inputs are named "group.item_N"; after pruning, each
    group's remaining indices must be contiguous from 0."""
    groups = {}
    for key in list(inputs.keys()):
        dot = key.find(".")
        us = key.rfind("_")
        if dot <= 0 or us <= dot:
            continue
        tail = key[us + 1:]
        if not tail.isdigit():
            continue
        groups.setdefault(key[:us + 1], []).append((int(tail), key))
    for stem, entries in groups.items():
        entries.sort()
        if all(idx == i for i, (idx, _key) in enumerate(entries)):
            continue
        values = [inputs.pop(key) for _idx, key in entries]
        for i, value in enumerate(values):
            inputs[f"{stem}{i}"] = value


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
