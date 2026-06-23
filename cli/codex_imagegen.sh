#!/usr/bin/env bash
# codex_imagegen.sh — generate an image via ChatGPT's (codex / gpt-5.5) native
# image generation tool, headless. Falls OUT (nonzero exit) on refusal/error so
# callers can fall back to local ComfyUI (aitools_cli).
#
# Usage:
#   codex_imagegen.sh "a prompt describing the image" /abs/path/out.png
#
# Exit codes:
#   0  success — image written to out.png
#   2  codex refused (content policy, etc.) — fall back to ComfyUI
#   1  other failure (timeout, no image, codex error) — fall back to ComfyUI
#
# Notes:
#   - Output is flat RGB (no alpha). For a transparent sprite, generate on a
#     flat chroma key and color-key in-engine, OR run the result through
#     aitools_cli's "Image To Image Mask Subject" preset.
#   - codex CANNOT do seamless/tiling textures or alpha extraction — use
#     aitools_cli directly for those; don't route them here.
#   - Each call is a full codex session (~15-20K tokens, ~40-60s). Heavy.

set -euo pipefail

PROMPT="${1:?usage: codex_imagegen.sh \"prompt\" out.png}"
OUT="${2:?usage: codex_imagegen.sh \"prompt\" out.png}"
TIMEOUT="${CODEX_IMAGEGEN_TIMEOUT:-300}"
CODEX_HOME="${CODEX_HOME:-$HOME/.codex}"
GENDIR="$CODEX_HOME/generated_images"

mkdir -p "$(dirname "$OUT")"
mkdir -p "$GENDIR"

# Marker file: anything newer than this is from THIS run.
MARKER="$(mktemp)"
trap 'rm -f "$MARKER"' EXIT
touch "$MARKER"
sleep 1  # ensure mtime resolution doesn't catch the marker itself

INSTR="Generate one image using ONLY your native image generation tool.

Image to generate: ${PROMPT}

Rules:
- Do NOT write code. Do NOT run shell commands. Do NOT search the web.
- Just produce the image with the image tool, nothing else.
- If you cannot or will not generate this image for ANY reason (content
  policy, safety refusal, or error), reply with exactly the single word
  REFUSED on its own line and generate nothing."

CODEX_OUT=""
set +e
# Low reasoning effort: image_gen needs no deep reasoning, and the user's
# global xhigh default makes this ~3x slower for no benefit.
CODEX_OUT="$(cd /tmp && timeout "$TIMEOUT" codex exec \
    --skip-git-repo-check \
    --sandbox read-only \
    -c model_reasoning_effort="low" \
    "$INSTR" 2>&1)"
CODEX_RC=$?
set -e

# Newest png created after our marker.
NEWIMG="$(find "$GENDIR" -type f -name '*.png' -newer "$MARKER" \
    -printf '%T@ %p\n' 2>/dev/null | sort -nr | head -1 | cut -d' ' -f2-)"

if [[ -n "$NEWIMG" && -f "$NEWIMG" ]]; then
    cp "$NEWIMG" "$OUT"
    echo "codex_imagegen: ok -> $OUT (src: $NEWIMG)" >&2
    exit 0
fi

# No image produced. Distinguish refusal from other failure.
if grep -qiw 'REFUSED' <<<"$CODEX_OUT"; then
    echo "codex_imagegen: REFUSED by codex content policy — fall back to ComfyUI" >&2
    exit 2
fi

if [[ $CODEX_RC -eq 124 ]]; then
    echo "codex_imagegen: timed out after ${TIMEOUT}s — fall back to ComfyUI" >&2
else
    echo "codex_imagegen: no image produced (rc=$CODEX_RC) — fall back to ComfyUI" >&2
    echo "--- codex output (tail) ---" >&2
    tail -15 <<<"$CODEX_OUT" >&2
fi
exit 1
