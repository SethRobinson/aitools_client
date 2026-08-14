#!/usr/bin/env python3
"""
aitools_cli — generate media from the command line via a ComfyUI server.

Mirrors what the Unity app (PicTextToImage.cs + PresetManager.cs) does:
load a workflow JSON (directly or via a Presets/*.txt file), ask a ComfyUI
server to convert it to API format (cached on disk), apply preset @replace
directives, substitute <AITOOLS_PROMPT>-style placeholders, submit to /prompt,
follow progress over a WebSocket, then download the result via /view.
"""

import argparse
import json
import math
import os
import random
import sys
import uuid
from pathlib import Path

# Make sibling modules importable when run via shebang from anywhere.
SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPT_DIR))

# Windows consoles can default to a legacy codepage (cp1252) that can't
# encode emoji found in some node titles (e.g. VHS nodes) — don't crash,
# just substitute the characters the console can't show.
for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(errors="replace")

import re

import alpha
import auth
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
        description="Generate media from a text prompt using a ComfyUI workflow or preset.",
    )
    p.add_argument("prompt", help="Text prompt")
    p.add_argument(
        "output",
        help="Output path; images are saved as PNG, videos keep their source extension",
    )
    p.add_argument("-n", "--negative", default=None,
                   help="Negative prompt (overrides preset's default_negative_prompt)")
    p.add_argument("-w", "--workflow", default=None,
                   help="Workflow JSON name (mutually exclusive with -p)")
    p.add_argument("-p", "--preset", default=None,
                   help="Preset file from Presets/ (e.g. \"Prompt To Image (Z-Image)\")")
    p.add_argument("--set-var", action="append", default=[], metavar="NAME=VALUE",
                   dest="set_var",
                   help="Override a preset %%var%% (repeatable). "
                        "Example: --set-var height=512 --set-var width=768")
    p.add_argument("-i", "--input", action="append", default=[],
                   help="Input image file; repeat to fill the preset's image "
                        "slots in order (-i a.png -i b.png -> image1, image2; "
                        "reference presets whose photos start at image2 fill "
                        "from there)")
    p.add_argument("-i2", "--input2", default=None, dest="input2",
                   help="Image bound to source image2 (alias kept for "
                        "two-input presets, e.g. \"Image To Image Klein Edit 2 Input\")")
    for _n in range(3, 11):
        p.add_argument(f"-i{_n}", f"--input{_n}", default=None, dest=f"input{_n}",
                       help=(f"Image bound to source image{_n}" if _n <= 4
                             else argparse.SUPPRESS))
    p.add_argument("--video", action="append", default=[],
                   help="Input video file; repeat for presets with a second "
                        "clip slot (--video a.mp4 --video b.mp4 -> video, video2)")
    p.add_argument("--video2", default=None, dest="video2",
                   help="Video bound to source video2 (second reference clip)")
    p.add_argument("--width", type=int, default=None,
                   help="Override the render width (video presets; snapped to "
                        "/32, clamped 256..2048)")
    p.add_argument("--height", type=int, default=None,
                   help="Override the render height (video presets; snapped to "
                        "/32, clamped 256..2048)")
    p.add_argument("--duration", type=float, default=None, metavar="SECONDS",
                   help="Override video duration in seconds (MiniMax H3: 24fps "
                        "frames snapped up to the 17k+5 grid, ~5.2..15.1s; use "
                        "with the 5s presets)")
    p.add_argument("--no-aspect-fit", action="store_true", dest="no_aspect_fit",
                   help="Don't refit the canvas to the start-frame image's "
                        "aspect ratio (video start-frame presets fit by default)")
    p.add_argument("--dry-run", action="store_true", dest="dry_run",
                   help="Build and validate the final API workflow JSON without "
                        "contacting any server; writes <output>.api.json")
    p.add_argument("--prune-input", action="append", default=[], dest="prune_input",
                   metavar="NAME",
                   help="Remove a named input from every node before submit "
                        "(repeatable), e.g. ref_video_audios.ref_video_audio_0")
    p.add_argument("--no-clip-audio", action="append", type=int, default=[],
                   dest="no_clip_audio", metavar="N", choices=(1, 2),
                   help="Declare reference clip N (1 or 2) silent: prune its "
                        "audio input so the server doesn't abort (used when "
                        "ffprobe isn't available to auto-detect)")
    p.add_argument("--alpha-key", default=None, dest="alpha_key",
                   metavar="COLOR[:SIM[:BLEND]]",
                   help="After saving a video output, also write "
                        "<output>_alpha.webm with this background color "
                        "chroma-keyed to transparency (green, blue, or "
                        "#RRGGBB; similarity default 0.30, blend 0.10)")
    p.add_argument("--alpha-from-luma", action="store_true", dest="alpha_from_luma",
                   help="After saving a video output, also write "
                        "<output>_alpha.webm with alpha from luminance (for "
                        "emissive VFX rendered on a pure black background)")
    p.add_argument("--sprite-sheet", nargs="?", const=0, type=int, default=None,
                   dest="sprite_sheet", metavar="COLS",
                   help="Also write <output>_sheet.png, an RGBA flipbook atlas "
                        "of every frame (COLS columns; omit the value for a "
                        "near-square grid)")
    p.add_argument("--sheet-fps", type=float, default=None, dest="sheet_fps",
                   metavar="N", help="Resample the sprite sheet to N fps (fewer frames)")
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
    p.add_argument("--server-token", default=None, dest="server_token",
                   metavar="TOKEN",
                   help="Bearer token for --server (ComfyUI-Login). Ignored "
                        "without --server; for config servers use |token= instead")
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


_IMAGE_SOURCE_RE = re.compile(r"^image(\d+)$")


def _flag_for_source(source):
    """The CLI flag that binds a given @upload source (for error messages)."""
    if source == "image1":
        return "-i"
    m = _IMAGE_SOURCE_RE.match(source)
    if m:
        return f"-i{m.group(1)}"
    if source == "video":
        return "--video"
    if source == "video2":
        return "--video2"
    return f"(source '{source}' — not suppliable from the CLI)"


def build_source_paths(args, preset):
    """Map CLI input flags onto the preset's declared @upload sources.

    Returns {source_name: local_path}. Dies on ambiguous or over-supplied
    combinations so a mistyped command can't silently drop a reference:
      - numbered flags (-i2..-i10, --video2) bind to their exact source name;
      - repeated -i fills the preset's declared image sources in sorted order
        (reference-video presets declare image2..image10, so the first -i is
        photo ref 1 there);
      - repeated --video fills 'video' then 'video2';
      - multiple -i mixed with numbered -iN flags is ambiguous -> error;
      - assigning one source twice (e.g. rv2v -i x -i2 y) -> error.
    """
    numbered = {}
    for n in range(2, 11):
        path = getattr(args, f"input{n}")
        if path:
            numbered[f"image{n}"] = path

    declared = {u.source for u in preset.uploads} if preset else set()
    image_sources = sorted((s for s in declared if _IMAGE_SOURCE_RE.match(s)),
                           key=lambda s: int(_IMAGE_SOURCE_RE.match(s).group(1)))
    video_sources = [s for s in ("video", "video2") if s in declared]
    label = preset.source_path.name if preset else "(none)"

    def img_capacity():
        if not image_sources:
            return "this preset declares no image inputs"
        return ("this preset's image inputs: "
                + ", ".join(f"{s} ({_flag_for_source(s)})" for s in image_sources))

    def vid_capacity():
        if not video_sources:
            return "this preset declares no video inputs"
        return ("this preset's video inputs: "
                + ", ".join(f"{s} ({_flag_for_source(s)})" for s in video_sources))

    if not declared:
        supplied = []
        if args.input:
            supplied.append("-i")
        supplied.extend(_flag_for_source(s) for s in numbered)
        if args.video:
            supplied.append("--video")
        if args.video2:
            supplied.append("--video2")
        if supplied:
            die(
                f"{'/'.join(supplied)} given but "
                f"{'preset ' + label + ' declares no @upload inputs' if preset else 'no preset was specified (-w workflows take no CLI inputs)'}",
                1,
            )
        return {}

    if len(args.input) > 1 and numbered:
        die("mixing repeated -i with numbered -iN flags is ambiguous — "
            "use one style for this command", 1)

    assignments = {}
    for source in sorted(numbered, key=lambda s: int(_IMAGE_SOURCE_RE.match(s).group(1))):
        if source not in declared:
            die(f"{_flag_for_source(source)} given but preset {label} has no "
                f"'{source}' @upload — {img_capacity()}", 1)
        assignments[source] = numbered[source]

    if args.input and not image_sources:
        die(f"-i given but {img_capacity()} ({label})", 1)
    for idx, path in enumerate(args.input):
        if idx >= len(image_sources):
            die(f"too many image inputs ({len(args.input)}) — {img_capacity()} ({label})", 1)
        source = image_sources[idx]
        if source in assignments:
            die(f"image source '{source}' assigned twice: -i fills '{source}' "
                f"on this preset and {_flag_for_source(source)} was also given — "
                f"use repeated -i or numbered flags, not both for one slot", 1)
        assignments[source] = path

    if args.video2:
        if "video2" not in declared:
            die(f"--video2 given but preset {label} has no 'video2' @upload — "
                f"{vid_capacity()}", 1)
        assignments["video2"] = args.video2
    if args.video and not video_sources:
        die(f"--video given but {vid_capacity()} ({label})", 1)
    for idx, path in enumerate(args.video):
        if idx >= len(video_sources):
            die(f"too many --video inputs ({len(args.video)}) — {vid_capacity()} ({label})", 1)
        source = video_sources[idx]
        if source in assignments:
            die(f"video source '{source}' assigned twice: --video fills "
                f"'{source}' and --video2 was also given", 1)
        assignments[source] = path

    for source in sorted({u.source for u in preset.uploads if not u.optional}):
        if source not in assignments and presets.SUPPLIABLE_SOURCE_RE.match(source):
            die(f"preset {label} needs a '{source}' input; pass it with "
                f"{_flag_for_source(source)} <path>", 1)

    return assignments


# MiniMax H3's trained pixel maximum (~1.03MP, hard cap 1344x768). Overrides
# above this still run, but quality/identity degrade — warn with the skill
# docs' recommended sizes.
H3_MAX_TRAINED_PIXELS = 1344 * 768


def snap_dim(value, flag):
    """Snap a dimension override to /32 and clamp to 256..2048, mirroring
    PicMain.ApplyDimensionOverrideToJoblist in the Unity app."""
    snapped = int(round(value / 32.0)) * 32
    snapped = max(256, min(2048, snapped))
    if snapped != value:
        print(f"{flag} {value} adjusted to {snapped} (must be a multiple of 32 in 256..2048)")
    return snapped


def warn_pixel_budget(w, h):
    if w * h > H3_MAX_TRAINED_PIXELS:
        print(f"warning: {w}x{h} exceeds MiniMax H3's trained maximum (~1.03MP; "
              f"hard cap 1344x768). Quality may degrade — good large canvases: "
              f"1152x640 landscape, 640x1152 portrait, 896x896 square.")


def duration_to_frames(seconds):
    """MiniMax H3 length: a 24fps frame count snapped UP to the 17k+5 grid and
    clamped to 124..362 (~5.2s..15.1s). Mirrors ApplyH3DurationOverride."""
    frames = max(1, math.ceil(seconds * 24))
    k = max(0, math.ceil((frames - 5) / 17))
    frames = 17 * k + 5
    clamped = min(362, max(124, frames))
    if clamped != frames:
        print(f"--duration {seconds:g}s is outside MiniMax H3's range — "
              f"clamped to {clamped} frames (~{clamped / 24.0:.1f}s)")
    return clamped


def compute_aspect_fit(src_w, src_h, budget_w, budget_h):
    """Refit the budget_w*budget_h pixel budget to the source aspect ratio,
    snapped to /32 and clamped 256..2048. Used for start-frame video presets:
    H3's i2v node stretches the first frame to the canvas, so a mismatched
    aspect would squish it (mirrors the Unity app's aspect-source behavior)."""
    budget = budget_w * budget_h
    aspect = src_w / float(src_h)
    w = int(round(math.sqrt(budget * aspect) / 32.0)) * 32
    h = int(round(math.sqrt(budget / aspect) / 32.0)) * 32
    return (max(256, min(2048, w)), max(256, min(2048, h)))


def _int_or_none(value):
    try:
        return int(str(value).strip())
    except (TypeError, ValueError):
        return None


def apply_aspect_fit(args, preset, source_paths, vars_in_replaces,
                     all_vars, critical_vars):
    """Start-frame aspect auto-fit: when a video preset pins the -i image as
    the first frame (image1 -> input1) and no explicit --width/--height was
    given, refit the preset's default pixel budget to the image's aspect so
    the frame isn't stretched. Disable with --no-aspect-fit."""
    if (not preset or args.no_aspect_fit
            or args.width is not None or args.height is not None):
        return
    # image1 -> input1 marks the start-frame family; reference presets route
    # image1 to input3+ and pin no frame, so they keep exact preset dims.
    if not any(u.source == "image1" and u.slot_idx == 0 for u in preset.uploads):
        return
    if "video" not in preset.workflow.lower():
        return  # plain img2img presets keep their exact preset dimensions
    if not (vars_in_replaces & {"width", "vid_width"}
            and vars_in_replaces & {"height", "vid_height"}):
        return
    local_path = source_paths.get("image1")
    if not local_path:
        return
    src_w, src_h = images.read_image_size(Path(local_path))
    budget_w = _int_or_none(all_vars.get("vid_width") or all_vars.get("width")) or 864
    budget_h = _int_or_none(all_vars.get("vid_height") or all_vars.get("height")) or 480
    fit_w, fit_h = compute_aspect_fit(src_w, src_h, budget_w, budget_h)
    if (fit_w, fit_h) == (budget_w, budget_h):
        return
    for name in ("width", "vid_width"):
        all_vars[name] = str(fit_w)
        critical_vars[name] = "aspect-fit"
    for name in ("height", "vid_height"):
        all_vars[name] = str(fit_h)
        critical_vars[name] = "aspect-fit"
    print(f"start-frame aspect fit: source {src_w}x{src_h} -> canvas "
          f"{fit_w}x{fit_h} (use --no-aspect-fit or --width/--height to override)")


# The universal H3 reference workflow wires each clip's soundtrack into these
# autogrow inputs on the MiniMaxH3ReferenceToVideo node; a silent source
# hard-aborts pre-sampling, so silent clips get their audio input pruned.
CLIP_AUDIO_INPUTS = {
    "video": ("ref_video_audios.ref_video_audio_0", 1),
    "video2": ("ref_video_audios.ref_video_audio_1", 2),
}


def _workflow_has_input(api_workflow, name):
    if not isinstance(api_workflow, dict):
        return False
    for node in api_workflow.values():
        inputs = node.get("inputs") if isinstance(node, dict) else None
        if isinstance(inputs, dict) and name in inputs:
            return True
    return False


def collect_silent_clip_prunes(args, source_paths, api_workflow,
                               existing_prunes, verbose):
    """Return audio-input prune names for supplied reference clips that have
    no audio stream (ffprobe auto-detect) or were declared silent with
    --no-clip-audio N. Only fires on graphs that actually wire clip audio
    (the H3 reference workflow), so e.g. Bernini v2v is untouched."""
    supplied_clips = {clip_no for source, (_n, clip_no) in CLIP_AUDIO_INPUTS.items()
                      if source_paths.get(source)}
    for clip_no in args.no_clip_audio:
        if clip_no not in supplied_clips:
            print(f"warning: --no-clip-audio {clip_no} ignored (no clip {clip_no} supplied)")
    auto = []
    for source, (input_name, clip_no) in CLIP_AUDIO_INPUTS.items():
        local_path = source_paths.get(source)
        if not local_path:
            continue
        if not _workflow_has_input(api_workflow, input_name):
            continue
        if input_name in existing_prunes:
            continue
        if clip_no in args.no_clip_audio:
            auto.append(input_name)
            print(f"clip {clip_no}: audio reference pruned (--no-clip-audio; "
                  f"H3 will synthesize the soundtrack from the prompt)")
            continue
        has_audio = images.video_has_audio(Path(local_path), verbose)
        if has_audio is False:
            auto.append(input_name)
            print(f"{Path(local_path).name} has no audio stream - pruning its "
                  f"audio reference (H3 will synthesize the soundtrack from the prompt)")
        elif has_audio is None:
            print(f"note: ffprobe unavailable - can't check {Path(local_path).name} "
                  f"for an audio stream. If the clip is silent the server will "
                  f"abort; pass --no-clip-audio {clip_no} in that case.")
    return auto


def main():
    args = build_argparser().parse_args()

    if args.preset and args.workflow:
        die("use one of -p/--preset or -w/--workflow, not both", 1)

    # Validate transparency flags before any GPU time is spent.
    if args.alpha_key and args.alpha_from_luma:
        die("--alpha-key and --alpha-from-luma are mutually exclusive", 1)
    if args.sheet_fps is not None and args.sprite_sheet is None:
        die("--sheet-fps only makes sense with --sprite-sheet", 1)
    if args.alpha_key:
        alpha.parse_color_spec(args.alpha_key)

    cfg_path = Path(args.config)
    if args.dry_run and not cfg_path.exists():
        # Dry-run never contacts a server, so a missing config only matters
        # if it was supposed to supply the default workflow.
        cfg = {"default_workflow": None, "servers": []}
    else:
        cfg = parse_config(cfg_path)

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
                print(f"  @uploads: " + ", ".join(
                    f"{u.source}->input{u.slot_idx + 1}" for u in preset.uploads))
            if preset.resizes:
                print(f"  @resizes: {len(preset.resizes)}")
    else:
        workflow_name = args.workflow or cfg["default_workflow"]
        if not workflow_name:
            die("no workflow specified (use -p, -w, or set default_workflow in config)", 1)

    # Map CLI input flags onto the preset's declared @upload sources (dies on
    # missing/ambiguous/over-supplied inputs). Optional uploads
    # (@upload|...|optional|) never block: an unfilled optional slot gets its
    # loader node pruned from the graph before submission.
    source_paths = build_source_paths(args, preset)
    all_upload_sources = sorted({u.source for u in preset.uploads}) if preset else []
    if args.verbose and source_paths:
        for source in sorted(source_paths):
            print(f"input {source}: {source_paths[source]}")

    effective_prompt, effective_negative = assemble_prompts(args, preset)
    if args.verbose:
        print(f"effective prompt: {effective_prompt!r}")
        print(f"effective negative: {effective_negative!r}")

    # Pick server.
    if args.dry_run:
        server_url = None
        if args.verbose:
            print("dry-run: skipping server selection (no network)")
    elif args.server:
        server_url = args.server.rstrip("/")
        if args.server_token:
            auth.register(server_url, args.server_token)
        if args.verbose:
            tok = " (with token)" if args.server_token else ""
            print(f"using override server: {server_url}{tok}")
    else:
        if args.server_token and args.verbose:
            print("warning: --server-token ignored without --server "
                  "(config servers carry their token via |token=)")
        if not cfg["servers"]:
            die("no servers in config", 1)
        if args.verbose:
            print("probing servers:")
        server_url, depth = servers.pick_server(cfg["servers"], args.verbose)
        if args.verbose:
            print(f"chose: {server_url} (queue {depth})")

    # Workflow load + convert + cache.
    api_workflow = workflow.load_or_convert_workflow(
        WORKFLOW_DIR, workflow_name, server_url, args.no_cache, args.verbose,
        offline=args.dry_run,
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

    # --width/--height/--duration convenience overrides + start-frame aspect
    # fit. These set both %var% naming families (t2v presets use width/height/
    # length, the other video presets vid_width/vid_height/vid_length; the
    # unused family is inert) and register as critical vars so a @replace that
    # fails to fire is a hard error instead of a silent default-size render.
    critical_vars = {}
    extra_replaces = []
    vars_in_replaces = presets.vars_used_in_replaces(preset.replaces) if preset else set()

    def add_dim_override(flag_label, value, names):
        for name in names:
            if name in overrides:
                die(f"{flag_label} conflicts with --set-var {name}= — use one or the other", 1)
        if not vars_in_replaces & set(names):
            knob = "/".join(f"%{n}%" for n in names)
            if preset:
                die(f"{flag_label} has no effect: preset {preset.source_path.name} "
                    f"has no {knob} @replace (not a size-controllable preset)", 1)
            die(f"{flag_label} needs a preset with @replace directives (use -p)", 1)
        for name in names:
            all_vars[name] = str(value)
            critical_vars[name] = flag_label

    eff_width = eff_height = None
    if args.width is not None:
        eff_width = snap_dim(args.width, "--width")
        add_dim_override("--width", eff_width, ("width", "vid_width"))
    if args.height is not None:
        eff_height = snap_dim(args.height, "--height")
        add_dim_override("--height", eff_height, ("height", "vid_height"))
    if eff_width or eff_height:
        w = eff_width or _int_or_none(all_vars.get("vid_width") or all_vars.get("width"))
        h = eff_height or _int_or_none(all_vars.get("vid_height") or all_vars.get("height"))
        if w and h:
            warn_pixel_budget(w, h)

    if args.duration is not None:
        frames = duration_to_frames(args.duration)
        if not preset:
            die("--duration needs a preset (-p) — raw workflows carry no length knob", 1)
        length_default = preset.variables.get("vid_length") or preset.variables.get("length")
        if length_default and length_default.strip() != "124":
            die(f"--duration doesn't work with fixed-duration preset "
                f"{preset.source_path.name} (default {length_default} frames) — "
                f"use the matching 5s preset with --duration instead", 1)
        for name in ("length", "vid_length"):
            if name in overrides:
                die(f"--duration conflicts with --set-var {name}= — use one or the other", 1)
        if vars_in_replaces & {"length", "vid_length"}:
            for name in ("length", "vid_length"):
                all_vars[name] = str(frames)
                critical_vars[name] = "--duration"
        else:
            # Preset has no length @replace at all (e.g. the rv2v 5s preset,
            # which deliberately opts out of duration overrides in the app):
            # append a synthetic replace against the workflow's shipped
            # default, mirroring Unity's ApplyH3DurationOverride.
            extra_replaces.append(presets.ReplaceOp(
                find='"length": 124', repl=f'"length": {frames}',
                required_by="--duration"))
        print(f"duration: {args.duration:g}s -> {frames} frames (~{frames / 24.0:.1f}s @ 24fps)")

    apply_aspect_fit(args, preset, source_paths, vars_in_replaces,
                     all_vars, critical_vars)

    # Apply preset @replace directives (with %var% substitution) on the JSON.
    expanded = []
    if preset and preset.replaces:
        expanded = presets.expand_replaces(preset.replaces, all_vars,
                                           args.verbose, critical_vars)
    expanded.extend(extra_replaces)
    if expanded:
        api_workflow = workflow.apply_replaces(api_workflow, expanded, args.verbose)

    # Handle preset @upload + @resize: for each unique source the preset
    # references, load that local file, run resizes (image1 only — see
    # README), upload, then map the server path into <AITOOLS_INPUT_N> for
    # every slot that source was routed to.
    input_path_replacements = {}
    if preset and preset.uploads:
        for source in all_upload_sources:
            local_path = source_paths.get(source)
            if not local_path:
                # Only reachable for optional uploads (required ones died above):
                # the slot stays unfilled and prune_unfilled_inputs drops its loader.
                if args.verbose:
                    print(f"optional {source} input not provided - its loader will be pruned")
                continue
            if source.startswith("video"):
                if args.dry_run:
                    if not Path(local_path).exists():
                        die(f"input file not found: {local_path}", 1)
                    server_path = f"temp/dryrun_{source}{Path(local_path).suffix or '.mp4'}"
                else:
                    if args.verbose:
                        print(f"uploading {source} input: {local_path}")
                    server_path = images.upload_file(server_url, Path(local_path), args.verbose)
            else:
                if args.verbose:
                    print(f"loading {source} input: {local_path}")
                img = images.load_input_image(Path(local_path))
                if source == "image1":
                    for op in presets.resolve_resizes(preset.resizes, all_vars, args.verbose):
                        img = images.apply_resize(img, op, args.verbose)
                elif preset.resizes and args.verbose:
                    print(f"  note: @resize directives only apply to image1, "
                          f"not {source}")
                if args.dry_run:
                    # Images are re-encoded to PNG on upload, so the fake path
                    # is always .png.
                    server_path = f"temp/dryrun_{source}.png"
                else:
                    server_path = images.upload_image(server_url, img, args.verbose)
            for upload in preset.uploads:
                if upload.source == source:
                    input_path_replacements[
                        f"<AITOOLS_INPUT_{upload.slot_idx + 1}>"
                    ] = server_path

    # Silent reference clips: the H3 reference graph hard-aborts server-side
    # when a wired audio input has a silent source ("VHS failed to extract
    # audio"). Probe supplied clips with ffprobe and prune per-clip, mirroring
    # the Unity app's automatic @prune_input behavior.
    prune_names = list(preset.prune_inputs) if preset else []
    prune_names += args.prune_input
    prune_names += collect_silent_clip_prunes(
        args, source_paths, api_workflow, set(prune_names), args.verbose)

    # Standard placeholder substitution (<AITOOLS_PROMPT>, etc.)
    seed = args.seed if args.seed is not None else random.randint(0, 2**63 - 1)
    api_workflow = workflow.replace_placeholders(api_workflow, {
        "<AITOOLS_PROMPT>": effective_prompt,
        "<AITOOLS_NEGATIVE_PROMPT>": effective_negative,
        **input_path_replacements,
    })
    # Prune unfilled optional loaders BEFORE the blank-by-default pass erases
    # the <AITOOLS_INPUT_N> markers the pruner keys on. Named prunes (preset
    # @prune_input, --prune-input, silent-clip auto-detect) run right after,
    # matching Unity's PruneWorkflowInputs order.
    if (preset and any(u.optional for u in preset.uploads)) or prune_names:
        api_workflow = workflow.prune_unfilled_inputs(api_workflow, args.verbose)
    if prune_names:
        api_workflow = workflow.prune_named_inputs(api_workflow, prune_names, args.verbose)
    blank_replacements = {ph: "" for ph in workflow.PLACEHOLDERS_BLANK_BY_DEFAULT}
    api_workflow = workflow.replace_placeholders(api_workflow, blank_replacements)
    workflow.override_seeds(api_workflow, seed)
    api_workflow = workflow.substitute_unique_id(api_workflow, args.verbose)

    if args.verbose:
        print(f"seed: {seed}")

    if args.dry_run:
        out = Path(args.output)
        target = out.with_name(out.name + ".api.json")
        target.write_text(json.dumps(api_workflow, indent=2), encoding="utf-8")
        if args.verbose:
            print(json.dumps(api_workflow, indent=2))
        if args.alpha_key or args.alpha_from_luma or args.sprite_sheet is not None:
            print("dry-run: alpha/sprite-sheet post-processing skipped "
                  "(runs after a real render)")
        print(f"dry-run: wrote {target} ({len(api_workflow)} nodes); nothing submitted")
        return

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

    if err == progress.WS_LOST:
        print("websocket dropped - falling back to /history polling")
        err = comfy_api.poll_history_until_done(server_url, prompt_id, label,
                                                args.verbose)
    if err:
        die(err, 3)

    output_images = comfy_api.fetch_outputs(server_url, prompt_id)

    # Images are always written as PNG to preserve any alpha channel;
    # videos keep their original container/extension (e.g. .mp4).
    out_base = Path(args.output)
    saved = []
    for i, img in enumerate(output_images):
        src_filename = img.get("filename", "")
        ext = comfy_api.save_extension(src_filename)
        if i == 0:
            target = out_base.with_suffix(ext)
            if args.verbose and target != out_base:
                print(f"output extension adjusted: {target}")
        else:
            target = out_base.with_name(f"{out_base.stem}_{i+1}{ext}")
        if args.verbose:
            print(f"downloading {src_filename} -> {target}")
        data = comfy_api.download_image(server_url, img)
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

    # Transparency post-processing (--alpha-key / --alpha-from-luma /
    # --sprite-sheet): local ffmpeg conversions of the first video output.
    if args.alpha_key or args.alpha_from_luma or args.sprite_sheet is not None:
        if main_out.suffix.lower() not in comfy_api.VIDEO_EXTS:
            die(f"--alpha-key/--alpha-from-luma/--sprite-sheet need a video "
                f"output, but this workflow produced {main_out.name} "
                f"(the file is saved; only the conversion was skipped)", 1)
        mode = "key" if args.alpha_key else ("luma" if args.alpha_from_luma else None)
        if mode:
            alpha.convert_to_alpha_webm(
                main_out, main_out.with_name(main_out.stem + "_alpha.webm"),
                mode, args.alpha_key, args.verbose)
        if args.sprite_sheet is not None:
            if mode is None and main_out.suffix.lower() != ".webm":
                die("--sprite-sheet without --alpha-key/--alpha-from-luma needs "
                    "a source that already carries alpha (e.g. a .webm from the "
                    "\"Video Remove Background (BiRefNet)\" preset)", 1)
            alpha.make_sprite_sheet(
                main_out, main_out.with_name(main_out.stem + "_sheet.png"),
                mode, args.alpha_key, args.sprite_sheet, args.sheet_fps,
                args.verbose)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\ninterrupted", file=sys.stderr)
        sys.exit(130)
