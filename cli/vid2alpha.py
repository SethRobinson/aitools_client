#!/usr/bin/env python3
"""
vid2alpha — convert an EXISTING video into transparent game-ready assets
using the repo's bundled ffmpeg (no server contact).

Outputs (independent, composable):
  --key COLOR[:sim[:blend]]  or  --luma   -> <base>_alpha.webm (VP9 with alpha)
  --sheet [COLS]                          -> <base>_sheet.png (RGBA flipbook atlas)

--key chroma-keys a solid background color (green/blue/#RRGGBB) with despill;
--luma sets alpha from luminance (best for explosions/fire/magic rendered on a
pure black background); --sheet with neither reuses the source's own alpha
(e.g. a webm produced by the "Video Remove Background (BiRefNet)" preset).

Examples:
  vid2alpha.py explosion.mp4 --luma --sheet
  vid2alpha.py greenscreen.mp4 --key green:0.35:0.1
  vid2alpha.py matted.webm --sheet 8 --sheet-fps 12
"""
import argparse
import sys
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(SCRIPT_DIR))

for _stream in (sys.stdout, sys.stderr):
    if hasattr(_stream, "reconfigure"):
        _stream.reconfigure(errors="replace")

import alpha
from util import die


def main():
    p = argparse.ArgumentParser(
        description="Convert a video to a transparent WebM and/or an RGBA "
                    "PNG sprite sheet (local ffmpeg, no server).")
    p.add_argument("input", help="Source video (mp4, webm, ...)")
    p.add_argument("output_base", nargs="?", default=None,
                   help="Base path for outputs (default: source path without "
                        "extension); writes <base>_alpha.webm / <base>_sheet.png")
    p.add_argument("--key", default=None, metavar="COLOR[:SIM[:BLEND]]",
                   help="Chroma-key this background color to transparency "
                        "(green, blue, black, or #RRGGBB; similarity default "
                        "0.30, blend 0.10)")
    p.add_argument("--luma", action="store_true",
                   help="Alpha from luminance (for emissive VFX on a pure "
                        "black background)")
    p.add_argument("--sheet", nargs="?", const=0, type=int, default=None,
                   metavar="COLS",
                   help="Also write an RGBA sprite-sheet PNG (COLS columns; "
                        "omit for a near-square grid)")
    p.add_argument("--sheet-fps", type=float, default=None, dest="sheet_fps",
                   metavar="N", help="Resample the sheet to N fps (fewer frames)")
    p.add_argument("-v", "--verbose", action="store_true", help="Verbose output")
    args = p.parse_args()

    if args.key and args.luma:
        die("--key and --luma are mutually exclusive", 1)
    if args.key is None and not args.luma and args.sheet is None:
        die("nothing to do: pass --key/--luma (transparent webm) and/or --sheet", 1)
    if args.sheet_fps is not None and args.sheet is None:
        die("--sheet-fps only makes sense with --sheet", 1)

    src = Path(args.input)
    if not src.exists():
        die(f"input not found: {src}", 1)
    base = Path(args.output_base) if args.output_base else src.with_suffix("")

    mode = "key" if args.key else ("luma" if args.luma else None)

    if mode:
        alpha.convert_to_alpha_webm(src, base.with_name(base.name + "_alpha.webm"),
                                    mode, args.key, args.verbose)
    if args.sheet is not None:
        alpha.make_sprite_sheet(src, base.with_name(base.name + "_sheet.png"),
                                mode, args.key, args.sheet, args.sheet_fps,
                                args.verbose)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\ninterrupted", file=sys.stderr)
        sys.exit(130)
