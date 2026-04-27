"""Image input handling: load from disk, optional client-side resize, upload
to the ComfyUI server's `/upload/image` endpoint.

Mirrors the Unity behavior (PicMain.cs:2819-2897 for resize math,
ResizeTool.cs:107-138 for the centered crop, ComfyUIFileUploader.cs:109-205
for upload semantics)."""
import io
import uuid
from pathlib import Path

import requests
from PIL import Image

from util import die

UPLOAD_TIMEOUT = 60


def load_input_image(path: Path) -> Image.Image:
    if not path.exists():
        die(f"input image not found: {path}", 1)
    try:
        img = Image.open(path)
        img.load()
    except Exception as e:
        die(f"could not read input image {path}: {e}", 1)
    # Keep alpha if present, else stay RGB.
    if img.mode == "RGBA" or "A" in img.getbands():
        return img.convert("RGBA")
    return img.convert("RGB")


def apply_resize(img: Image.Image, op, verbose: bool = False) -> Image.Image:
    """Apply a single ResizeOp. Returns the (possibly new) image."""
    w, h = op.width, op.height
    if op.only_if_larger and img.width <= w and img.height <= h:
        if verbose:
            print(f"  resize: skipped (image {img.width}x{img.height} <= {w}x{h})")
        return img

    if op.aspect_correct:
        cropped = _center_crop_to_aspect(img, w, h)
        out = cropped.resize((w, h), Image.LANCZOS)
        if verbose:
            print(f"  resize: {img.width}x{img.height} -> crop {cropped.width}x{cropped.height} -> {w}x{h} (aspect-correct)")
    else:
        out = img.resize((w, h), Image.LANCZOS)
        if verbose:
            print(f"  resize: {img.width}x{img.height} -> {w}x{h} (stretch)")
    return out


def _center_crop_to_aspect(img: Image.Image, target_w: int, target_h: int) -> Image.Image:
    """Center-crop to match target aspect ratio. Mirrors ResizeTool.cs:107-138."""
    src_aspect = img.width / img.height
    dst_aspect = target_w / target_h
    if dst_aspect < src_aspect:
        # Source is wider — crop the width
        new_w = int(img.height * dst_aspect)
        new_h = img.height
        x = (img.width - new_w) // 2
        y = 0
    else:
        # Source is taller (or equal) — crop the height
        new_w = img.width
        new_h = int(img.width / dst_aspect)
        x = 0
        y = (img.height - new_h) // 2
    return img.crop((x, y, x + new_w, y + new_h))


def invert_alpha_bytes(png_bytes: bytes, verbose: bool = False) -> bytes:
    """Invert the alpha channel of a PNG. RGB inputs are promoted to RGBA
    with full alpha first (so inverting yields a fully transparent image —
    typically not what you want, but at least well-defined)."""
    img = Image.open(io.BytesIO(png_bytes))
    img.load()
    if img.mode != "RGBA":
        img = img.convert("RGBA")
    r, g, b, a = img.split()
    a = a.point(lambda v: 255 - v)
    out = Image.merge("RGBA", (r, g, b, a))
    buf = io.BytesIO()
    out.save(buf, format="PNG")
    if verbose:
        print(f"  inverted alpha ({img.width}x{img.height})")
    return buf.getvalue()


def upload_image(server_url: str, img: Image.Image, verbose: bool = False) -> str:
    """Upload `img` as PNG to ComfyUI's /upload/image. Returns the path string
    that should be used in the workflow (e.g. 'temp/aitools_cli_<uuid>.png')."""
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    buf.seek(0)
    fname = f"aitools_cli_{uuid.uuid4()}.png"
    try:
        r = requests.post(
            f"{server_url}/upload/image",
            files={"image": (fname, buf.getvalue(), "image/png")},
            data={"type": "temp", "overwrite": "true"},
            timeout=UPLOAD_TIMEOUT,
        )
    except requests.RequestException as e:
        die(f"image upload failed: {e}", 2)
    if r.status_code != 200:
        die(f"image upload failed: HTTP {r.status_code}\n{r.text[:500]}", 2)
    try:
        body = r.json()
    except ValueError:
        die(f"image upload returned non-JSON response: {r.text[:200]}", 2)
    name = body.get("name")
    subfolder = body.get("subfolder") or ""
    folder_type = body.get("type") or "temp"
    if not name:
        die(f"image upload response missing 'name': {body}", 2)
    # ComfyUI returns subfolder="" when the file lands directly in the type's
    # root folder (e.g. /temp/). LoadImage-style nodes still expect the prefix,
    # so fall back to the type field. Mirrors PicMain.cs's hardcoded "temp/".
    prefix = subfolder or folder_type
    server_path = f"{prefix}/{name}" if prefix else name
    if verbose:
        print(f"  uploaded {img.width}x{img.height} -> {server_path}")
    return server_path
