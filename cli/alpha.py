"""Transparency post-processing for generated movies, via the repo's bundled
ffmpeg: chroma-key / luminance-alpha conversion to transparent VP9 WebM, and
RGBA PNG sprite-sheet (flipbook) generation for game particle systems.

Used by aitools_cli's --alpha-key / --alpha-from-luma / --sprite-sheet flags
and by the standalone vid2alpha.py converter.
"""
import math
import re
import shutil
import subprocess
import sys
from pathlib import Path

from util import die
import images

FFMPEG_BUNDLED = Path(__file__).resolve().parent.parent / "utils" / "ffmpeg" / "bin" / "ffmpeg.exe"

# Named key colors -> (0xRRGGBB, despill type)
KEY_COLORS = {
    "green": ("0x00FF00", "green"),
    "blue": ("0x0000FF", "blue"),
    "black": ("0x000000", None),
}

CONVERT_TIMEOUT = 600  # vp9 encoding of a multi-minute clip can be slow


def find_ffmpeg():
    """Path to an ffmpeg executable, or die. Prefers the repo's bundled
    Windows binary, falls back to PATH (the Linux case)."""
    if sys.platform == "win32" and FFMPEG_BUNDLED.exists():
        return str(FFMPEG_BUNDLED)
    found = shutil.which("ffmpeg")
    if not found:
        die(f"ffmpeg not found (looked for {FFMPEG_BUNDLED} and PATH) - "
            f"needed for --alpha-key/--alpha-from-luma/--sprite-sheet", 1)
    return found


def _run_ffmpeg(cmd, verbose=False):
    """Run an ffmpeg command; die with the stderr tail on failure. Returns
    captured stderr (ffmpeg logs stats there)."""
    if verbose:
        print("  ffmpeg " + " ".join(str(c) for c in cmd[1:]))
    try:
        r = subprocess.run([str(c) for c in cmd], capture_output=True,
                           text=True, timeout=CONVERT_TIMEOUT)
    except (OSError, subprocess.TimeoutExpired) as e:
        die(f"ffmpeg failed to run: {e}", 1)
    if r.returncode != 0:
        die(f"ffmpeg conversion failed:\n{r.stderr.strip()[-800:]}", 1)
    return r.stderr


def probe_video(path: Path):
    """Return {'width', 'height', 'fps', 'duration'} for the first video
    stream (fps/duration may be None if unprobeable)."""
    ffprobe = images.find_ffprobe()
    if not ffprobe:
        die("ffprobe not found - needed for sprite-sheet layout", 1)
    try:
        r = subprocess.run(
            [ffprobe, "-v", "error", "-select_streams", "v:0",
             "-show_entries", "stream=width,height,r_frame_rate",
             "-show_entries", "format=duration",
             "-of", "default=noprint_wrappers=1", str(path)],
            capture_output=True, text=True, timeout=30,
        )
    except (OSError, subprocess.TimeoutExpired) as e:
        die(f"ffprobe failed on {path}: {e}", 1)
    if r.returncode != 0:
        die(f"ffprobe failed on {path}: {r.stderr.strip()[:300]}", 1)
    info = {"width": None, "height": None, "fps": None, "duration": None}
    for line in r.stdout.splitlines():
        key, _, value = line.partition("=")
        value = value.strip()
        if key == "width":
            info["width"] = int(value)
        elif key == "height":
            info["height"] = int(value)
        elif key == "r_frame_rate" and "/" in value:
            num, den = value.split("/", 1)
            if float(den or 1) > 0:
                info["fps"] = float(num) / float(den)
        elif key == "duration":
            try:
                info["duration"] = float(value)
            except ValueError:
                pass
    return info


def parse_color_spec(spec):
    """Parse 'COLOR[:similarity[:blend]]' where COLOR is green|blue|black or
    #RRGGBB / 0xRRGGBB. Returns (ffmpeg_color, similarity, blend, despill)."""
    parts = spec.split(":")
    color = parts[0].strip().lower()
    try:
        similarity = float(parts[1]) if len(parts) > 1 else 0.30
        blend = float(parts[2]) if len(parts) > 2 else 0.10
    except ValueError:
        die(f"--alpha-key expects COLOR[:similarity[:blend]] with numeric "
            f"similarity/blend, got: {spec!r}", 1)
    if len(parts) > 3:
        die(f"--alpha-key expects COLOR[:similarity[:blend]], got: {spec!r}", 1)
    if color in KEY_COLORS:
        ff_color, despill = KEY_COLORS[color]
    else:
        m = re.fullmatch(r"(?:#|0x)([0-9a-f]{6})", color)
        if not m:
            die(f"--alpha-key color must be green, blue, black, or #RRGGBB - "
                f"got: {parts[0]!r}", 1)
        hexval = m.group(1)
        ff_color = "0x" + hexval.upper()
        r_, g_, b_ = (int(hexval[i:i + 2], 16) for i in (0, 2, 4))
        # Despill only makes sense for a clearly green- or blue-dominant key.
        despill = ("green" if g_ > max(r_, b_) + 32 else
                   "blue" if b_ > max(r_, g_) + 32 else None)
    return ff_color, similarity, blend, despill


def _key_filter_chain(mode, color_spec):
    """The per-frame filter chain (before fps/tile), as a filter_complex
    fragment consuming [0:v] and producing [keyed].

    mode: 'key' (chromakey + despill), 'luma' (alpha = luminance, for
    black-background emissive VFX), or None (source already has alpha)."""
    if mode == "key":
        color, sim, blend, despill = parse_color_spec(color_spec)
        chain = f"chromakey={color}:{sim:g}:{blend:g}"
        if despill:
            chain += f",despill=type={despill}"
        return f"[0:v]{chain},format=rgba[keyed]"
    if mode == "luma":
        return "[0:v]split[c][a];[a]format=gray[am];[c][am]alphamerge,format=rgba[keyed]"
    return "[0:v]format=rgba[keyed]"


def _input_args(src: Path):
    """Input arguments; webm sources decode through libvpx so their alpha
    channel survives (the native vp9 decoder drops it)."""
    if src.suffix.lower() == ".webm":
        return ["-c:v", "libvpx-vp9", "-i", src]
    return ["-i", src]


def convert_to_alpha_webm(src: Path, dst: Path, mode, color_spec=None,
                          verbose=False):
    """Write a transparent VP9 WebM (yuva420p). Keeps source audio as Opus
    when present. mode: 'key' or 'luma' (see _key_filter_chain)."""
    ffmpeg = find_ffmpeg()
    cmd = [ffmpeg, "-y", "-v", "error", "-stats"] + _input_args(src) + [
        "-filter_complex", _key_filter_chain(mode, color_spec),
        "-map", "[keyed]", "-map", "0:a?",
        "-c:v", "libvpx-vp9", "-pix_fmt", "yuva420p",
        "-auto-alt-ref", "0",          # REQUIRED for vp9 alpha
        "-crf", "23", "-b:v", "0", "-row-mt", "1",
        "-c:a", "libopus", dst,
    ]
    _run_ffmpeg(cmd, verbose)
    if not dst.exists() or dst.stat().st_size == 0:
        die(f"ffmpeg produced no output: {dst}", 1)
    print(f"Saved: {dst}  ({dst.stat().st_size:,} bytes, VP9 alpha webm)")


def _count_filtered_frames(ffmpeg, src, chain, sheet_fps, verbose):
    """Exact output frame count: run the chain (plus optional fps decimation)
    into the null muxer and parse ffmpeg's final frame counter."""
    full = chain + (f";[keyed]fps={sheet_fps:g}[cnt]" if sheet_fps else ";[keyed]null[cnt]")
    stderr = _run_ffmpeg([ffmpeg, "-v", "info"] + _input_args(src) + [
        "-filter_complex", full, "-map", "[cnt]", "-an", "-f", "null", "-",
    ], verbose)
    matches = re.findall(r"frame=\s*(\d+)", stderr)
    if not matches:
        die("could not determine frame count for sprite sheet", 1)
    return int(matches[-1])


def make_sprite_sheet(src: Path, dst: Path, mode, color_spec=None, cols=0,
                      sheet_fps=None, verbose=False):
    """Write an RGBA PNG flipbook atlas of every (keyed) frame. cols=0 picks
    a near-square grid. Prints grid/frame/fps info the game needs."""
    ffmpeg = find_ffmpeg()
    info = probe_video(src)
    chain = _key_filter_chain(mode, color_spec)
    frames = _count_filtered_frames(ffmpeg, src, chain, sheet_fps, verbose)
    if frames < 1:
        die("sprite sheet: source has no frames", 1)
    if not cols or cols < 1:
        cols = max(1, math.ceil(math.sqrt(frames)))
    rows = max(1, math.ceil(frames / cols))
    if info["width"] and (cols * info["width"] > 8192 or rows * info["height"] > 8192):
        print(f"warning: sprite sheet would be {cols * info['width']}x"
              f"{rows * info['height']}px - many GPUs cap textures at 8192px. "
              f"Reduce frames with --sheet-fps (e.g. --sheet-fps 12) or render "
              f"a smaller video.")
    fps_part = f"fps={sheet_fps:g}," if sheet_fps else ""
    full = chain + f";[keyed]{fps_part}tile={cols}x{rows}:color=0x00000000[sheet]"
    _run_ffmpeg([ffmpeg, "-v", "error"] + _input_args(src) + [
        "-filter_complex", full, "-map", "[sheet]", "-frames:v", "1", "-y", dst,
    ], verbose)
    if not dst.exists() or dst.stat().st_size == 0:
        die(f"ffmpeg produced no sprite sheet: {dst}", 1)
    eff_fps = sheet_fps or info["fps"] or 24.0
    print(f"Saved: {dst}  ({dst.stat().st_size:,} bytes)")
    print(f"  sprite sheet: {cols}x{rows} grid, {frames} frames of "
          f"{info['width']}x{info['height']}, play at {eff_fps:g} fps "
          f"(unused cells transparent)")
    return cols, rows, frames, eff_fps
