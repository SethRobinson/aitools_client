#!/usr/bin/env python3
"""
aitools_cli — generate an image from the command line via a ComfyUI server.

Mirrors what the Unity app (PicTextToImage.cs + PresetManager.cs) does:
load a workflow JSON (directly or via a Presets/*.txt file), ask a ComfyUI
server to convert it to API format (cached on disk), apply preset @replace
directives, substitute <AITOOLS_PROMPT>-style placeholders, submit to /prompt,
follow progress over a WebSocket, then download the result via /view.
"""

import argparse
import os
import random
import sys
import uuid
from pathlib import Path

# Make sibling modules importable when run via shebang from anywhere.
SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPT_DIR))

import re

import comfy_api
import images
import presets
import progress
import servers
import workflow
from config import parse_config
from util import die, server_label

_VAR_NAME_RE = re.compile(r"^[a-zA-Z_][a-zA-Z0-9_]*$")

DEFAULT_CONFIG = SCRIPT_DIR / "config.txt"
WORKFLOW_DIR = SCRIPT_DIR.parent / "ComfyUI"


def build_argparser():
    p = argparse.ArgumentParser(
        description="Generate an image from a text prompt using a ComfyUI workflow or preset.",
    )
    p.add_argument("prompt", help="Text prompt")
    p.add_argument("output", help="Output image file (always saved as PNG)")
    p.add_argument("-n", "--negative", default=None,
                   help="Negative prompt (overrides preset's default_negative_prompt)")
    p.add_argument("-w", "--workflow", default=None,
                   help="Workflow JSON name (mutually exclusive with -p)")
    p.add_argument("-p", "--preset", default=None,
                   help="Preset file from Presets/ (e.g. \"Prompt To Image (Z Image)\")")
    p.add_argument("--set-var", action="append", default=[], metavar="NAME=VALUE",
                   dest="set_var",
                   help="Override a preset %%var%% (repeatable). "
                        "Example: --set-var height=512 --set-var width=768")
    p.add_argument("-i", "--input", default=None,
                   help="Input image file (required for presets that use @upload)")
    p.add_argument("-s", "--seed", type=int, default=None,
                   help="Seed (default: random)")
    p.add_argument("-c", "--config", default=str(DEFAULT_CONFIG),
                   help="Config file path")
    p.add_argument("--no-cache", action="store_true",
                   help="Force workflow re-conversion")
    p.add_argument("--keep-server-files", action="store_true",
                   help="Skip /history clear cleanup")
    p.add_argument("--server", default=None,
                   help="Override server URL (skip queue probe)")
    p.add_argument("-v", "--verbose", action="store_true", help="Verbose output")
    return p


def assemble_prompts(args, preset):
    """Compute effective prompt + negative prompt by merging CLI args + preset."""
    base = args.prompt or (preset.default_prompt if preset else "") or ""
    pre = (preset.default_pre_prompt if preset else "") or ""
    post = (preset.default_post_prompt if preset else "") or ""
    parts = [s for s in (pre, base, post) if s]
    effective_prompt = " ".join(parts).strip()

    if args.negative is not None:
        effective_negative = args.negative
    elif preset and preset.default_negative_prompt is not None:
        effective_negative = preset.default_negative_prompt
    else:
        effective_negative = ""

    return effective_prompt, effective_negative


def parse_set_var_overrides(entries):
    """Parse a list of '--set-var NAME=VALUE' strings into an ordered dict.

    - Splits on the first '=' so values may contain '='.
    - NAME must be a valid %var% identifier ([a-zA-Z_][a-zA-Z0-9_]*).
    - VALUE has surrounding single/double quotes stripped (mirrors joblist RHS
      quoting in presets._parse_joblist).
    - Later entries win over earlier ones (last --set-var wins).
    """
    overrides = {}
    for raw in entries:
        if "=" not in raw:
            die(f"--set-var expects NAME=VALUE, got: {raw!r}", 1)
        name, value = raw.split("=", 1)
        name = name.strip()
        if not _VAR_NAME_RE.match(name):
            die(
                f"--set-var NAME must be a valid identifier "
                f"([a-zA-Z_][a-zA-Z0-9_]*), got: {name!r}",
                1,
            )
        value = value.strip()
        if (value.startswith('"') and value.endswith('"')) or \
           (value.startswith("'") and value.endswith("'")):
            value = value[1:-1]
        overrides[name] = value
    return overrides


def main():
    args = build_argparser().parse_args()

    if args.preset and args.workflow:
        die("use one of -p/--preset or -w/--workflow, not both", 1)

    cfg = parse_config(Path(args.config))

    # Load preset (if any) and decide on workflow name.
    preset = presets.load_preset(args.preset) if args.preset else None
    if preset:
        workflow_name = preset.workflow
        if args.verbose:
            print(f"preset: {preset.source_path.name}")
            print(f"  workflow: {workflow_name}")
            print(f"  vars: {preset.variables or '(none)'}")
            print(f"  @replaces: {len(preset.replaces)}")
            if preset.uploads:
                print(f"  @uploads: input slots {[i+1 for i in preset.uploads]}")
            if preset.resizes:
                print(f"  @resizes: {len(preset.resizes)}")
    else:
        workflow_name = args.workflow or cfg["default_workflow"]
        if not workflow_name:
            die("no workflow specified (use -p, -w, or set default_workflow in config)", 1)

    # Validate -i vs preset's @upload requirement.
    if preset and preset.uploads and not args.input:
        die(
            f"preset {preset.source_path.name} needs an input image; "
            f"pass one with -i <path>",
            1,
        )
    if args.input and not (preset and preset.uploads):
        if args.verbose:
            print(f"warning: -i {args.input!r} ignored "
                  f"({'preset has no @upload' if preset else 'no preset specified'})")

    effective_prompt, effective_negative = assemble_prompts(args, preset)
    if args.verbose:
        print(f"effective prompt: {effective_prompt!r}")
        print(f"effective negative: {effective_negative!r}")

    # Pick server.
    if args.server:
        server_url = args.server.rstrip("/")
        if args.verbose:
            print(f"using override server: {server_url}")
    else:
        if not cfg["servers"]:
            die("no servers in config", 1)
        if args.verbose:
            print("probing servers:")
        server_url, depth = servers.pick_server(cfg["servers"], args.verbose)
        if args.verbose:
            print(f"chose: {server_url} (queue {depth})")

    # Workflow load + convert + cache.
    api_workflow = workflow.load_or_convert_workflow(
        WORKFLOW_DIR, workflow_name, server_url, args.no_cache, args.verbose
    )

    # Build the variable namespace once (preset vars + built-ins).
    all_vars = dict(preset.variables) if preset else {}
    all_vars.setdefault("prompt", effective_prompt)
    all_vars.setdefault("negative_prompt", effective_negative)

    # Apply CLI --set-var overrides last so they win over the preset and built-ins.
    overrides = parse_set_var_overrides(args.set_var)
    if overrides:
        if args.verbose:
            print(f"--set-var overrides: {len(overrides)}")
            for name, value in overrides.items():
                prior = all_vars.get(name)
                prior_str = f"{prior!r}" if prior is not None else "(new)"
                print(f"  %{name}% = {value!r}  (was {prior_str})")
        all_vars.update(overrides)

    # Apply preset @replace directives (with %var% substitution) on the JSON.
    if preset and preset.replaces:
        expanded = presets.expand_replaces(preset.replaces, all_vars, args.verbose)
        api_workflow = workflow.apply_replaces(api_workflow, expanded, args.verbose)

    # Handle preset @upload + @resize: load the input image, run all resizes
    # in preset order, upload, then map the server path into <AITOOLS_INPUT_N>.
    input_path_replacements = {}
    if preset and preset.uploads:
        if args.verbose:
            print(f"loading input image: {args.input}")
        img = images.load_input_image(Path(args.input))
        for op in presets.resolve_resizes(preset.resizes, all_vars, args.verbose):
            img = images.apply_resize(img, op, args.verbose)
        server_path = images.upload_image(server_url, img, args.verbose)
        for slot_idx in preset.uploads:
            input_path_replacements[f"<AITOOLS_INPUT_{slot_idx + 1}>"] = server_path

    # Standard placeholder substitution (<AITOOLS_PROMPT>, etc.)
    seed = args.seed if args.seed is not None else random.randint(0, 2**63 - 1)
    placeholder_replacements = {
        "<AITOOLS_PROMPT>": effective_prompt,
        "<AITOOLS_NEGATIVE_PROMPT>": effective_negative,
        **input_path_replacements,
    }
    for ph in workflow.PLACEHOLDERS_BLANK_BY_DEFAULT:
        placeholder_replacements.setdefault(ph, "")
    api_workflow = workflow.replace_placeholders(api_workflow, placeholder_replacements)
    workflow.override_seeds(api_workflow, seed)

    if args.verbose:
        print(f"seed: {seed}")

    # Connect WS before submit so we don't miss early events on a fast server.
    client_id = str(uuid.uuid4())
    try:
        ws = progress.connect_ws(server_url, client_id)
    except Exception as e:
        die(f"websocket connect failed: {e}", 2)

    node_titles = workflow.build_node_titles(api_workflow)
    label = server_label(server_url)
    try:
        prompt_id = comfy_api.submit(server_url, api_workflow, client_id, args.verbose)
        err = progress.watch_progress(ws, prompt_id, node_titles, label, args.verbose)
    finally:
        try:
            ws.close()
        except Exception:
            pass

    if err:
        die(err, 3)

    output_images = comfy_api.fetch_outputs(server_url, prompt_id)

    # Always write PNG to preserve any alpha channel.
    out_path = Path(args.output).with_suffix(".png")
    if args.verbose and out_path.name != args.output:
        print(f"output forced to PNG: {out_path}")
    saved = []
    for i, img in enumerate(output_images):
        if i == 0:
            target = out_path
        else:
            target = out_path.with_name(f"{out_path.stem}_{i+1}.png")
        if args.verbose:
            print(f"downloading {img.get('filename')} -> {target}")
        data = comfy_api.download_image(server_url, img)
        src_filename = img.get("filename", "")
        if preset and preset.invert_alpha:
            data = images.invert_alpha_bytes(data, args.verbose)
            src_filename = "x.png"  # bytes are now PNG regardless of source
        comfy_api.save_image(data, src_filename, target)
        saved.append(target)

    if not args.keep_server_files:
        comfy_api.cleanup(server_url, prompt_id)

    main_out = saved[0]
    extra = f"  + {len(saved) - 1} more" if len(saved) > 1 else ""
    print(f"Saved: {main_out}  ({main_out.stat().st_size:,} bytes){extra}")


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\ninterrupted", file=sys.stderr)
        sys.exit(130)
