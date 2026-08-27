# aitools_cli

A command-line front-end (Windows + Linux) for the same ComfyUI servers used
by Seth's AI Tools (the Unity app one directory up). Generates images and
movies from a text prompt (plus optional image/video references) using a
workflow JSON or one of the existing presets. For video, see
"Generating movies (MiniMax H3)" below.

It mirrors what `PicTextToImage.cs` + `PresetManager.cs` do in the Unity app:
load a workflow, ask the ComfyUI server to convert it to API format (cached on
disk), apply preset `@replace` directives + `<AITOOLS_PROMPT>` placeholders,
submit to `/prompt`, follow progress over a WebSocket, then download the
resulting image via `/view`.

## Setup

1. Copy `config.example.txt` to `config.txt` and list your ComfyUI servers:
   ```
   cp config.example.txt config.txt      # Linux
   copy config.example.txt config.txt    # Windows
   ```
   ```
   default_workflow|text_to_img_zimage.json
   add_server|http://gpu-box.lan:7860
   add_server|http://gpu-box.lan:7861
   ```
   Unreachable servers are silently skipped; the lowest-queue one wins. If
   `config.txt` is missing, the CLI will tell you and point at the example.

   If a server is protected by the
   [ComfyUI-Login](https://github.com/liusida/ComfyUI-Login) custom node,
   append its bearer token (the `$2b$12$...` string it prints to its
   console as *"For direct API calls, use token=..."*):
   ```
   add_server|http://secured-box.lan:8188|token=$2b$12$qUfJfV942n...
   ```
   It is sent as an `Authorization: Bearer` header on every request and
   the WebSocket. Servers without a token work unchanged.

2. Servers must be running with `--listen` and have the
   [comfyui-workflow-to-api-converter-endpoint](https://github.com/SethRobinson/comfyui-workflow-to-api-converter-endpoint)
   custom node installed (used to convert "full" workflows on the fly).

3. Python deps: `requests`, `websocket-client`, `Pillow` (see
   `requirements.txt`).

   **Windows:** just run `aitools_cli.bat` — the first run creates a local
   `venv\` folder next to the script and installs the requirements
   automatically (re-installs if `requirements.txt` changes). Needs Python 3
   on PATH (the `py` launcher or `python`).

   **Linux:** `pip install -r requirements.txt` into your environment of
   choice.

4. (Optional, Linux) `chmod +x aitools_cli.py` and add `cli/` to your PATH or
   symlink the script.

## Usage

```
aitools_cli.py "<prompt>" <output> [options]     # Linux
aitools_cli.bat "<prompt>" <output> [options]    # Windows
```

Image output is **always** written as PNG (extension is forced to `.png` to
keep any alpha channel intact). Video output (e.g. from a `SaveVideo` node)
is saved as-is with its original container extension (`.mp4`, `.webm`, ...).
If the workflow produces multiple outputs, the extras are saved as
`name_2.png`, `name_3.png`, ...

### Examples

Basic run using the default workflow from `config.txt`:
```
aitools_cli.py "a giant pig riding a dolphin" pig.png
```

Use a preset from `../Presets/` — name resolution accepts the bare file
stem, with or without `.txt`:
```
aitools_cli.py "a cat" cat.png -p "Prompt To Image (Z-Image)"
```

Override the preset's negative prompt and pin a server:
```
aitools_cli.py "a cat" cat.png -p "Prompt To Image (Z-Image)" \
    -n "ugly, blurry" --server http://gpu-box.lan:7861
```

Reproducible: same seed = same image:
```
aitools_cli.py "a cat" cat.png -s 42
```

Override preset `%var%` values from the command line (repeatable). For
example, force a 512-tall image on the normally-1024 SDXL tile preset:
```
aitools_cli.py "a cat" cat.png \
    -p "Prompt To Image (SDXL) TileX" \
    --set-var height=512 --set-var width=768
```

Verbose mode shows server probe, effective prompt, every applied `@replace`,
seed, prompt id, and live per-step progress:
```
aitools_cli.py "a cat" cat.png -p "Prompt To Image (Z-Image)" -v
```

Image-input preset (auto-mask the subject, returning an RGBA PNG with the
mask burned into alpha):
```
aitools_cli.py "" subject_masked.png \
    -p "Image To Image Mask Subject" \
    -i photo.jpg
```

Single-image edit using the Klein 9B model:
```
aitools_cli.py "make her hair red" edited.png \
    -p "Image To Image Klein Edit 1 Input" \
    -i portrait.jpg
```

Two-image preset — combine/edit using two source images. Use `-i2` for the
second image; presets that declare both `@upload|image1|...` and
`@upload|image2|...` (e.g. `Image To Image Klein Edit 2 Input`) require
both inputs:
```
aitools_cli.py "put the cat from image2 into image1" combined.png \
    -p "Image To Image Klein Edit 2 Input" \
    -i background.jpg -i2 cat.jpg
```

Video-input preset:
```
aitools_cli.py "restyle this clip as a pencil animation" out.mp4 \
    -p "Video To Video (Bernini)" \
    --video clip.mp4
```

Movie generation (text to video, animating a start frame, reference photos
and clips) has its own section below: "Generating movies (MiniMax H3)".

### Flags

| Flag | Purpose |
|---|---|
| `-p, --preset NAME` | Preset file from `../Presets/` (or absolute path) |
| `-w, --workflow FILE` | Workflow JSON from `../ComfyUI/` (mutex with `-p`) |
| `--set-var NAME=VALUE` | Override a preset `%var%` (repeatable; wins over joblist assignments) |
| `-i, --input PATH` | Input image; REPEATABLE, fills the preset's declared image slots in order (`-i a.png -i b.png`) |
| `-i2..-i10 PATH` | Image bound to that exact source slot (`-i2` = image2, etc.) |
| `--video PATH` | Input video; repeatable (fills `video`, then `video2`) |
| `--video2 PATH` | Video bound to the second reference clip slot |
| `--width N`, `--height N` | Render-size override for size-controllable (video) presets; snapped to /32, clamped 256..2048 |
| `--duration SECONDS` | Video length override (MiniMax H3 5s presets only; snapped to the 24fps 17k+5 frame grid, ~5.2..15.1s) |
| `--no-aspect-fit` | Disable the automatic start-frame aspect fit (see the movies section) |
| `--dry-run` | Build and validate the final API JSON with no server contact; writes `<output>.api.json` |
| `--prune-input NAME` | Remove a named node input from the graph before submit (repeatable) |
| `--no-clip-audio N` | Declare reference clip N (1 or 2) silent so its audio input is pruned (when ffprobe can't auto-detect) |
| `--alpha-key COLOR[:SIM[:BLEND]]` | Also write `<output>_alpha.webm` with this background color keyed to transparency (green/blue/#RRGGBB) |
| `--alpha-from-luma` | Also write `<output>_alpha.webm` with alpha from luminance (emissive VFX on black backgrounds) |
| `--sprite-sheet [COLS]` | Also write `<output>_sheet.png`, an RGBA flipbook atlas (near-square grid when COLS omitted) |
| `--sheet-fps N` | Resample the sprite sheet to N fps (fewer, smaller frames) |
| `-n, --negative TEXT` | Negative prompt (overrides preset default) |
| `-s, --seed INT` | Seed (default: random in `0..2⁶³-1`) |
| `-c, --config PATH` | Config file path (default: `./config.txt`) |
| `--server URL` | Skip queue probe, use this server |
| `--server-token TOKEN` | Bearer token for `--server` (ComfyUI-Login); ignored without `--server` |
| `--no-cache` | Force workflow re-conversion (refresh `_cached_api_version.json`) |
| `--keep-server-files` | Skip the `/history` clear cleanup call |
| `-v, --verbose` | Verbose output |

Passing more inputs than the preset declares, mixing several `-i` with
numbered `-iN` flags, or targeting one slot twice is an error (the message
lists the preset's available slots), so a mistyped command can't silently
drop a reference.

## Generating movies (MiniMax H3)

MiniMax H3 is the app's default video model: 24fps mp4 with native stereo
audio, including spoken dialog (11 languages). Clips are ~5s by default and
can run up to ~15s. H3 has NO negative-prompt path (`-n` is ignored by these
presets), fps is fixed at 24, and the model files may only be installed on
some of your ComfyUI servers. Output is written as `.mp4` (the CLI adjusts your
output extension automatically).

There are four ways to make a movie. All are single-step presets, so they
work fully from the CLI:

### 1. Text to video

```
aitools_cli.py "a golden retriever news anchor reads the evening news, deadpan" out.mp4 \
    -p "Prompt To Video (MiniMax H3) 5s"
```

### 2. Animate a start frame (image to video)

The `-i` image becomes the EXACT first frame of the movie:

```
aitools_cli.py "she looks up from the book and says 'finally, some quiet'" out.mp4 \
    -p "Image To Video (MiniMax H3) 5s" -i portrait.png
```

H3 stretches the start frame to the render canvas, so by default the CLI
refits the canvas to your image's aspect ratio at the preset's pixel budget
(printed as `start-frame aspect fit: ...`). Pass explicit `--width`/`--height`
or `--no-aspect-fit` to take manual control.

### 3. Reference photos to video (subject does something new)

Generates the SUBJECT of the photos doing something new; it does not animate
the exact frame (use mode 2 for that). Up to 9 photos via repeated `-i`;
refer to them in the prompt as `<Picture 1>`, `<Picture 2>`, ... in the order
given:

```
aitools_cli.py "<Picture 1> and <Picture 2> ride a tandem bicycle through Tokyo" out.mp4 \
    -p "Reference To Video (MiniMax H3) 5s" -i alice.png -i bob.png
```

### 4. Reference video (+ photos) to video

A source clip drives motion/camera/voice while photos pin identity/setting.
`--video` is the primary clip (`<Video 1>`, its soundtrack is `<Audio 1>`),
`--video2` adds a second clip, and repeated `-i` adds up to 9 photos
(`<Picture 1>`..). Give each reference ONE job in the prompt:

```
aitools_cli.py "<Picture 1> performs the dance from <Video 1>, keep <Audio 1> as the soundtrack" out.mp4 \
    -p "Reference Video To Video (MiniMax H3) 5s" --video dance.mp4 -i face.png
```

Reference clips are consumed at 24fps, 2-15s. Silent clips would normally
hard-abort the server's audio extraction; the CLI auto-detects a missing
audio stream with ffprobe (the repo's bundled
`../utils/ffmpeg/bin/ffprobe.exe` on Windows, `ffprobe` on PATH on Linux)
and prunes that clip's audio input automatically, printing what it did. If
ffprobe is unavailable, pass `--no-clip-audio 1` (or `2`) for silent clips.

### Quality

The default t2v/i2v presets run an 8-step turbo LoRA (~70s for a 5s clip on
a fast GPU), and the default reference presets (modes 3 and 4) run an 8-step
Ref2V turbo distill. For the full 20-step render (~2x slower, best quality),
pick the `Quality` preset variants:

- `Prompt To Video (MiniMax H3 Quality) 5s`
- `Image To Video (MiniMax H3 Quality) 5s` / `15s`
- `Reference To Video (MiniMax H3 Quality) 5s`
- `Reference Video To Video (MiniMax H3 Quality) 5s` / `15s`

Rough wall-clock for a 5s clip, uncontended: turbo t2v/i2v ~70s, Quality
t2v/i2v ~163s, 20-step single-clip reference video ~4min (the turbo
reference default is roughly half that); 15s clips cost several times the
5s figure.

### Size and duration

- Default canvas is 864x480 (or the aspect-fitted equivalent). `--width` /
  `--height` override it (snapped to /32). The model is trained up to
  ~1.03MP; the best large canvases are 1152x640 (landscape), 640x1152
  (portrait), and 896x896 (square). Render cost scales with pixel count
  (~2x at 1152x640).
- `--duration SECONDS` works on any H3 `5s` preset and snaps to the model's
  frame grid (~5.2s minimum, ~15.1s maximum), e.g. `--duration 8`. The `15s`
  presets are fixed-length; the CLI will tell you to use the 5s preset with
  `--duration` instead.
- `-s SEED` makes renders reproducible, same as images.

### Validating commands offline (--dry-run)

`--dry-run` runs the whole pipeline (preset parsing, input validation,
aspect fit, overrides, graph pruning) without contacting a server and writes
the exact API JSON that would be submitted to `<output>.api.json`:

```
aitools_cli.py "test" out.mp4 -p "Image To Video (MiniMax H3) 5s" -i photo.png --dry-run
```

Useful for checking a command line (or letting an AI agent check its own)
before spending minutes of GPU time. Inputs are still opened and validated;
upload paths are faked as `temp/dryrun_*`.

Concurrent renders are safe: each submission gets a unique output filename
prefix, so several CLI runs (even against the same server) can't download
each other's files.

If a video render dies INSTANTLY with a ComfyUI "CUDA error: invalid
argument", that one server instance is in a wedged state (it needs a
ComfyUI restart; it is not a model or GPU-type problem). Retry pinned to a
different instance with `--server`, since automatic server selection can
land on the wedged one.

## Transparent-background movies (VFX for games)

H3 can't output an alpha channel directly, but three pipelines get you
game-ready transparent clips. Transparent output comes in two forms, both
made locally with the repo's bundled ffmpeg:

- `<output>_alpha.webm` — VP9 WebM with a real alpha channel (plays
  transparently in engines that support it, e.g. Unity's VideoPlayer)
- `<output>_sheet.png` — an RGBA sprite-sheet flipbook atlas for particle
  systems (grid, frame count, and playback fps are printed; unused cells are
  transparent). Use `--sheet-fps 12` to thin the frames — a full 124-frame
  clip makes a huge atlas and the CLI warns when it would exceed common
  8192px GPU texture caps.

### Recipe A: emissive VFX (explosions, fire, magic, sparks)

Generate on a PURE BLACK background and either use additive blending in your
engine (black simply disappears — no alpha needed at all), or bake
alpha-from-luminance:

```
aitools_cli.py "a single massive explosion: bright orange fireball, flying sparks, billowing smoke, centered, camera locked off, pure black background, nothing else visible" \
    explosion.mp4 -p "Prompt To Video (MiniMax H3) 5s" \
    --width 640 --height 640 --alpha-from-luma --sprite-sheet --sheet-fps 12
```

This is the best-quality option for glowing effects: no key fringing, and
semi-transparent glow falls off naturally. (Dark smoke disappears with it —
use Recipe C when the smoke matters.)

### Recipe B: chroma key (solid objects with defined edges)

Generate on a solid green (or blue) background and key it out; despill is
applied automatically:

```
aitools_cli.py "a treasure chest opens and gold coins burst out, centered, camera locked off, pure solid bright green background, evenly lit, no shadows" \
    chest.mp4 -p "Prompt To Video (MiniMax H3) 5s" --alpha-key green
```

Tune tolerance with `--alpha-key green:0.35:0.15` (COLOR:similarity:blend) if
too much or too little is keyed. `#RRGGBB` colors also work.

### Recipe C: AI matting (any background, smoke/hair/soft edges)

The `Video Remove Background (BiRefNet)` preset runs a matting model
server-side (MIT-licensed, auto-downloads on first use) and outputs a
transparent `.webm` directly — no special background needed when generating:

```
aitools_cli.py "" matted.webm -p "Video Remove Background (BiRefNet)" --video anyclip.mp4
aitools_cli.py "" matted.webm -p "Video Remove Background (BiRefNet)" --video anyclip.mp4 --sprite-sheet --sheet-fps 12
```

Output has no audio (it's a VFX asset). Non-24fps sources: add
`--set-var vid_fps=N` to keep the original timing.

### Converting existing videos: vid2alpha.py

The same conversions work on any local file, no server needed:

```
vid2alpha.py explosion.mp4 --luma --sheet          # black bg -> webm + atlas
vid2alpha.py greenscreen.mp4 --key green:0.35      # keyed webm only
vid2alpha.py matted.webm --sheet 8 --sheet-fps 12  # atlas from an alpha webm
```

### Prompting tips for keyable/mattable H3 clips

Ask for: a SINGLE centered subject, "camera locked off" (no pans), "pure
black background" or "pure solid bright green background, evenly lit, no
shadows", and "nothing else visible". Avoid ground planes and cast shadows —
they key badly and matte as part of the subject.

## Preset support

When `-p` is used, the user's prompt gets the preset's `default_pre_prompt`
prepended and `default_post_prompt` appended (both space-joined). The
preset's `default_negative_prompt` is used unless `-n` is given.

CLI `--set-var NAME=VALUE` overrides wins over any `%name%=...` assignment
in the joblist (applies to every `%name%` substitution downstream, including
`@replace`, `@resize`, and placeholder expansion).

Inside the preset's `joblist` block these are supported:
- `%name%="value"` (or `%name%=value`) variable assignments
- One workflow line: `<workflow.json> [@directive|args| ...]`
- `%var%` substitution in directive args, including built-ins `%prompt%`
  and `%negative_prompt%`
- Directives:
  - `@replace|find|with|` — string substitution on the workflow JSON
  - `@upload|<source>|inputN|[optional|]` — uploads a CLI-supplied file to
    ComfyUI's `/temp/` folder and routes the path into `<AITOOLS_INPUT_N>`
    (N = 1..11). Suppliable sources: `image1`..`image10` (repeatable `-i`
    fills them in declared order; numbered `-i2`..`-i10` bind exact slots),
    `video` and `video2` (repeatable `--video` / `--video2`). `image` is an
    alias for `image1`, `video1` for `video`. A trailing `optional` flag
    means a missing source is fine: that slot's loader node is pruned from
    the graph at submit time instead of erroring (this is how the universal
    H3 reference workflow serves every photo/clip combination).
    `temp1`/`temp2`/`temp3` aren't supported.
  - `@prune_input|name|` — remove that named input key from every node in
    the API JSON before submit (same as the `--prune-input` flag; used for
    per-clip audio pruning on H3 reference workflows).
  - `@resize|x|W|y|H|aspect_correct|0_or_1|` — resize the input image to
    `W×H` before upload. `aspect_correct|1` center-crops to the target
    aspect first; `aspect_correct|0` stretches.
  - `@resize_if_larger|...|` — same args as `@resize`, but only acts when
    the image exceeds either dimension.
  - `@invert_alpha|` — post-process the *output* image, flipping its alpha
    channel. Useful when a mask workflow gives you the inverse of what you
    want (e.g. you want to keep the background, not the subject). Any slot
    arg is ignored — it always acts on the saved output.

In short: single-step presets work for text-to-image, image-in workflows
(img2img, mask, inpaint, etc.), and all four MiniMax H3 movie modes (up to
9 reference photos + 2 reference clips). Multi-step chains, LLM calls, and
presets that pull from `temp1`/`temp2`/`temp3` slots still error out with a
clear explanation.

## Missing features (vs. the Unity app)

The Unity app supports a much wider preset/script vocabulary. The CLI
deliberately implements only the text-to-image subset. Each item below will
either error out clearly when encountered, or (for the silently-ignored
blocks) is parsed and discarded.

### Block types — silently ignored
These are LLM/Adventure-mode features and don't affect text-to-image, so
their presence in a preset is harmless:
- `summarize_prompt` — summarization prompt for the LLM (Adventure mode)
- `recent_interactions` — integer controlling LLM history depth

### Multi-step orchestration — error
- More than one workflow line in a single `joblist` (chained workflows)
- `command ...` lines (built-in command sequences)
- Multiline `@end`-terminated arguments
- Mid-job control flow: `@stopjob`, `@no_undo`, `@lock_gpu`

### Image / input-slot features
- `@upload|image1..image10|inputN|` — **supported** (repeatable `-i`, or
  numbered `-i2`..`-i10` for exact slots). Two-input presets
  (e.g. `Image To Image Klein Edit 2 Input`) take `-i` + `-i2`.
- `@upload|video|inputN|` / `@upload|video2|inputN|` — **supported**
  (repeatable `--video`, or `--video2` for the second clip).
- `@upload|...|optional|` — **supported** (unfilled slots prune their loader
  nodes from the graph).
- `@prune_input|name|` — **supported** (also via `--prune-input`).
- `@resize|...|` and `@resize_if_larger|...|` — **supported** (no-slot form;
  always applied to `image1`. Other images upload as-is.)

Still missing:
- `@upload|temp1|...|`, `@upload|temp2|...|`, `@upload|temp3|...|` — multi-step
  presets that pass intermediate results between jobs (Qwen Edit From
  Temp1+Temp2, etc.) — the CLI only runs a single job at a time
- `@setimage|%var%|src|` — copy an image into a named variable
- `@fill_mask_if_blank` — auto-fill an empty inpaint mask

### Variable mutation across steps — error
These exist to pass values between sequential jobs in a chain, which the
CLI doesn't run:
- `@copy|src|dst|` — copy a variable's value
- `@add|src|dst|` — append a variable's value
- `@set|%var%|value|` — set a custom text variable
- `@clear|%var%|` — clear a variable

### LLM integration — error
- `call_llm` — invoke the configured LLM with the current prompt state
- `@llm_prompt_reset` — clear LLM conversation history
- `@llm_prompt_set_base_prompt|text|` — set the LLM system prompt
- `@llm_prompt_pop_first` — drop the oldest LLM interaction
- `@llm_prompt_add_from_user|text|` — append a user-side message
- `@llm_prompt_add_from_assistant|text|` — append an assistant-side message
- `@llm_prompt_add_to_last_interaction|text|` — extend the last LLM message
- `@llm_add_image|slot|` — attach an image to the next LLM message (vision)
- `@parse_llm_prompts` — parse `SET_PROMPT1:`..`SET_PROMPT8:` tags from the
  LLM reply into per-job prompt slots

### Built-in variables not exposed
Only `%prompt%` and `%negative_prompt%` are pre-populated. The Unity app
also exposes the following — they are *not* errors when referenced (unknown
`%var%` tokens are left as-is, matching the C# behavior), but they will
never resolve to anything useful here:
- `%audio_prompt%`, `%audio_negative_prompt%`, `%segmentation_prompt%`
- `%llm_prompt%`, `%llm_reply%`
- `%prompt_1%` … `%prompt_8%` (extended prompt slots for multi-segment work)
- `%global_prompt%`, `%prepend_prompt%`, `%append_prompt%`
- `%temp_text1%` … `%temp_text4%`, `%requirements%`

### Other niceties not ported
- No undo/history of previous generations
- No Unity-side image post-processing (mask edits, alpha tricks)
- No batch / queue management beyond submitting one job at a time
- No GPU locking / reservation across multiple submissions

## File layout

```
cli/
  aitools_cli.py      # entry point: argparse + glue
  aitools_cli.bat     # Windows launcher: creates venv/, installs deps, runs the script
  requirements.txt    # Python deps (requests, websocket-client, Pillow)
  config.py           # config.txt parser
  auth.py             # optional per-server bearer-token auth
  presets.py          # Presets/*.txt parser
  workflow.py         # load/convert/cache + @replace + placeholders + seed
  alpha.py            # transparency post-processing (chroma key / luma -> webm alpha, sprite sheets)
  vid2alpha.py        # standalone converter: existing video -> transparent webm / sprite sheet
  servers.py          # /queue probe + selection
  comfy_api.py        # /prompt, /history, /view, cleanup
  progress.py         # WebSocket loop + status display
  util.py             # die(), server_label()
  config.txt          # server list + default workflow
```

The first conversion of any workflow writes
`../ComfyUI/<workflow>_cached_api_version.json` next to the source — same
location and behavior as the Unity app.

## Exit codes

- `0` — success
- `1` — user / config error (bad args, missing preset, unsupported directive)
- `2` — server / network error (no servers reachable, HTTP failure, timeout)
- `3` — generation reported an error from ComfyUI
- `130` — Ctrl-C
