# aitools_cli

A command-line front-end (Windows + Linux) for the same ComfyUI servers used
by Seth's AI Tools (the Unity app one directory up). Generates an image from a
text prompt using a workflow JSON or one of the existing presets.

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
   add_server|http://hal:7860
   add_server|http://hal:7861
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
   choice (already present in the existing `comfyui_env`).

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
    -n "ugly, blurry" --server http://hal:7861
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

### Flags

| Flag | Purpose |
|---|---|
| `-p, --preset NAME` | Preset file from `../Presets/` (or absolute path) |
| `-w, --workflow FILE` | Workflow JSON from `../ComfyUI/` (mutex with `-p`) |
| `--set-var NAME=VALUE` | Override a preset `%var%` (repeatable; wins over joblist assignments) |
| `-i, --input PATH` | Input image file (image1 — required for presets that `@upload image1`) |
| `-i2, --input2 PATH` | Second input image file (image2 — required for two-input presets) |
| `-n, --negative TEXT` | Negative prompt (overrides preset default) |
| `-s, --seed INT` | Seed (default: random in `0..2⁶³-1`) |
| `-c, --config PATH` | Config file path (default: `./config.txt`) |
| `--server URL` | Skip queue probe, use this server |
| `--server-token TOKEN` | Bearer token for `--server` (ComfyUI-Login); ignored without `--server` |
| `--no-cache` | Force workflow re-conversion (refresh `_cached_api_version.json`) |
| `--keep-server-files` | Skip the `/history` clear cleanup call |
| `-v, --verbose` | Verbose output |

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
  - `@upload|image1|inputN|` — uploads `-i` to ComfyUI's `/temp/` folder
    and routes the path into `<AITOOLS_INPUT_N>` (N = 1..4). Source must
    be `image1`, `image`, or `image2`. A preset may use both `image1` and
    `image2` for two-input workflows (e.g. `Image To Image Klein Edit 2
    Input` — pass `-i` and `-i2`). `temp1`/`temp2`/`temp3` aren't supported.
  - `@resize|x|W|y|H|aspect_correct|0_or_1|` — resize the input image to
    `W×H` before upload. `aspect_correct|1` center-crops to the target
    aspect first; `aspect_correct|0` stretches.
  - `@resize_if_larger|...|` — same args as `@resize`, but only acts when
    the image exceeds either dimension.
  - `@invert_alpha|` — post-process the *output* image, flipping its alpha
    channel. Useful when a mask workflow gives you the inverse of what you
    want (e.g. you want to keep the background, not the subject). Any slot
    arg is ignored — it always acts on the saved output.

In short: single-step presets work for text-to-image and for image-in
workflows that need a single input image (img2img, mask, inpaint, etc.).
Multi-step chains, LLM calls, and presets that pull from `temp1`/`temp2`/
`temp3` slots still error out with a clear explanation.

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
Image-input presets work via `-i <path>` (and optionally `-i2 <path>`):
- `@upload|image1|inputN|` — **supported** (`-i` input)
- `@upload|image2|inputN|` — **supported** (`-i2` input). Two-input presets
  (e.g. `Image To Image Klein Edit 2 Input`) require both flags.
- `@resize|...|` and `@resize_if_larger|...|` — **supported** (no-slot form;
  always applied to `image1`. `image2` is uploaded as-is.)

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
