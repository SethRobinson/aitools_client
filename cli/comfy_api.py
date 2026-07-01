"""ComfyUI HTTP api: submit, poll history, download outputs, cleanup."""
import io
import os
import time
from pathlib import Path
from urllib.parse import urlencode

import requests

import auth
from util import die

HTTP_TIMEOUT = 30
HISTORY_POLL_INTERVAL = 0.5
HISTORY_TIMEOUT = 120


def submit(server_url, api_workflow, client_id, verbose=False):
    payload = {"prompt": api_workflow, "client_id": client_id}
    try:
        r = requests.post(f"{server_url}/prompt", json=payload,
                          headers=auth.headers_for(server_url), timeout=HTTP_TIMEOUT)
    except requests.RequestException as e:
        die(f"submit request failed: {e}", 2)
    if r.status_code != 200:
        die(f"submit failed: HTTP {r.status_code}\n{r.text[:1000]}", 2)
    data = r.json()
    if "prompt_id" not in data:
        die(f"submit response missing prompt_id: {data}", 2)
    if verbose:
        print(f"submitted: prompt_id={data['prompt_id']} queue#={data.get('number','?')}")
    return data["prompt_id"]


def fetch_outputs(server_url, prompt_id):
    deadline = time.time() + HISTORY_TIMEOUT
    while time.time() < deadline:
        try:
            r = requests.get(f"{server_url}/history/{prompt_id}",
                             headers=auth.headers_for(server_url), timeout=HTTP_TIMEOUT)
        except requests.RequestException as e:
            die(f"history request failed: {e}", 2)
        if r.status_code == 200:
            data = r.json()
            entry = data.get(prompt_id)
            if entry:
                status = entry.get("status") or {}
                outputs = entry.get("outputs") or {}
                if status.get("status_str") == "error":
                    msgs = []
                    for m in status.get("messages") or []:
                        if isinstance(m, list) and len(m) >= 2 and m[0] == "execution_error":
                            msgs.append(str(m[1].get("exception_message", m[1])))
                    die(f"generation reported error: {'; '.join(msgs) or 'unknown'}", 3)
                if outputs:
                    images = []
                    for _node_id, out in outputs.items():
                        for key, value in out.items():
                            if not isinstance(value, list):
                                continue
                            for img in value:
                                if not isinstance(img, dict) or "filename" not in img:
                                    continue
                                if "ait_ignore" in (img.get("filename") or ""):
                                    continue
                                images.append(img)
                    if images:
                        return images
        time.sleep(HISTORY_POLL_INTERVAL)
    die("timed out waiting for outputs in /history", 2)


def download_image(server_url, image_ref):
    qs = urlencode({
        "filename": image_ref.get("filename", ""),
        "subfolder": image_ref.get("subfolder", ""),
        "type": image_ref.get("type", "output"),
    })
    try:
        r = requests.get(f"{server_url}/view?{qs}",
                         headers=auth.headers_for(server_url), timeout=HTTP_TIMEOUT)
    except requests.RequestException as e:
        die(f"download failed: {e}", 2)
    if r.status_code != 200:
        die(f"download failed: HTTP {r.status_code}", 2)
    return r.content


VIDEO_EXTS = {".mp4", ".webm", ".mov", ".avi", ".mkv", ".gif"}


def save_extension(src_filename):
    """Extension the output will be saved with: videos keep their original
    container, everything else is normalized to .png."""
    ext = os.path.splitext(src_filename)[1].lower()
    return ext if ext in VIDEO_EXTS else ".png"


def save_image(data, src_filename, out_path: Path):
    """Images are written as PNG (preserves alpha) — re-encoded via Pillow
    unless already PNG. Videos are written as-is (raw container bytes)."""
    src_ext = os.path.splitext(src_filename)[1].lower()
    if src_ext == ".png" or src_ext in VIDEO_EXTS:
        out_path.write_bytes(data)
        return
    from PIL import Image
    img = Image.open(io.BytesIO(data))
    img.save(out_path, format="PNG")


def cleanup(server_url, prompt_id):
    try:
        requests.post(
            f"{server_url}/history",
            json={"clear": True, "prompt_id": prompt_id},
            headers=auth.headers_for(server_url),
            timeout=5,
        )
    except Exception:
        pass
