"""Builds the "真貓散步" (real cat) sprite sheets from Pixabay green-screen videos.

Requires: ffmpeg on PATH, Pillow, numpy, and a free Pixabay API key in the
PIXABAY_API_KEY environment variable (https://pixabay.com/api/docs/).

Usage: PIXABAY_API_KEY=... python tools/make_real_cats.py

Clips can also be local green-screen videos (e.g. ones you generated yourself):
use {"file": "tools/source/my-cat.mp4", "author": "...", ...} instead of "id".
See docs/cat-video-brief.md for what such videos should look like.

The source videos are downloaded to tools/.cache/ (git-ignored) and are NOT
committed: only the processed, background-removed sprite sheets go into
src/CatSaver/Assets/RealCats/. Videos are under the Pixabay Content License
(https://pixabay.com/service/license-summary/); credits are in realcats.json.
"""
import io
import json
import os
import subprocess
import sys
import tempfile
import urllib.parse
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
CACHE = ROOT / "tools" / ".cache"
OUT = ROOT / "src" / "CatSaver" / "Assets" / "RealCats"

# role "walk": a cat walking in place, facing right; the app moves it across the screen.
#   "speed" = how far it walks per second, in body widths (tune so the paws don't slide).
# role "sit":  a cat idling in place; played forward then backward (ping-pong).
CLIPS = [
    {"id": 318833, "role": "walk", "quality": "tiny", "fps": 15, "height": 240, "speed": 0.55},
    {"id": 359118, "role": "walk", "quality": "medium", "fps": 15, "height": 240, "speed": 0.45},
    {"id": 116648, "role": "sit", "quality": "medium", "fps": 8, "height": 320, "start": 0.0, "length": 4.0, "key": (8, 40)},
    {"id": 116190, "role": "sit", "quality": "medium", "fps": 8, "height": 320, "start": 1.5, "length": 4.0},
    {"id": 302856, "role": "sit", "quality": "tiny", "fps": 8, "height": 320, "start": 2.0, "length": 4.0, "key": (3, 22)},
]
COLUMNS = 8


def api_hit(video_id):
    key = os.environ.get("PIXABAY_API_KEY")
    if not key:
        sys.exit("Set PIXABAY_API_KEY (free key from https://pixabay.com/api/docs/).")
    url = "https://pixabay.com/api/videos/?" + urllib.parse.urlencode({"key": key, "id": video_id})
    data = json.loads(subprocess.run(["curl", "-sf", "-A", "curl/8", url], check=True, capture_output=True).stdout)
    return data["hits"][0]


def download(clip):
    if "file" in clip:
        return ROOT / clip["file"], {"user": clip.get("author", ""), "pageURL": clip.get("url", "")}
    CACHE.mkdir(parents=True, exist_ok=True)
    path = CACHE / f"{clip['id']}.mp4"
    meta_path = CACHE / f"{clip['id']}.json"
    if not path.exists() or not meta_path.exists():
        hit = api_hit(clip["id"])
        subprocess.run(["curl", "-sf", "-A", "curl/8", "-o", str(path), hit["videos"][clip["quality"]]["url"]], check=True)
        meta_path.write_text(json.dumps({"user": hit["user"], "pageURL": hit["pageURL"], "tags": hit["tags"]}))
    return path, json.loads(meta_path.read_text())


def green_key(rgb, lo, hi):
    """
    Keys out green by "greenness" = G - max(R, B), which copes with uneven or
    gradient green screens and keeps dark/black fur. Also removes green spill.
    """
    f = rgb.astype(np.float32)
    r, g, b = f[..., 0], f[..., 1], f[..., 2]
    greenness = g - np.maximum(r, b)
    alpha = 1 - np.clip((greenness - lo) / (hi - lo), 0, 1)
    # Shrink the matte by a pixel to drop the green fringe around fur.
    alpha = np.array(Image.fromarray((alpha * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(3))) / 255.0
    # Despill only clearly green-tinted pixels, so real fur colours stay intact.
    f[..., 1] = np.where(greenness > 6, np.maximum(r, b) + 6, g)
    out = np.dstack([f, alpha * 255]).astype(np.uint8)
    out[alpha < 0.08] = 0
    return out


def extract(video, clip, tmp):
    trim = []
    if "start" in clip:
        trim = ["-ss", str(clip["start"]), "-t", str(clip["length"])]
    subprocess.run(["ffmpeg", "-v", "error", "-y", *trim, "-i", str(video), "-vf", f"fps={clip['fps']}",
                    str(Path(tmp) / "%04d.png")], check=True)
    lo, hi = clip.get("key", (18, 60))
    return [green_key(np.array(Image.open(p).convert("RGB")), lo, hi) for p in sorted(Path(tmp).glob("*.png"))]


def best_walk_loop(frames, min_len=8, max_len=40):
    """Finds (start, length) whose first and last frames match best, i.e. one clean step cycle."""
    small = [np.array(Image.fromarray(f[..., 3]).resize((96, 54))).astype(np.float32) / 255 for f in frames]
    best = None
    for s in range(0, max(1, len(frames) - min_len)):
        for n in range(min_len, max_len + 1):
            if s + n >= len(frames):
                break
            d = np.abs(small[s] - small[s + n]).mean()
            if best is None or d < best[0] - 1e-6:
                best = (d, s, n)
    return best[1], best[2], best[0]


def crop_and_scale(frames, height):
    alpha = np.max([f[..., 3] for f in frames], axis=0)
    ys, xs = np.nonzero(alpha > 20)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    pad = 4
    y0, x0 = max(0, y0 - pad), max(0, x0 - pad)
    y1, x1 = min(alpha.shape[0], y1 + pad), min(alpha.shape[1], x1 + pad)
    w = round((x1 - x0) * height / (y1 - y0))
    return [Image.fromarray(f[y0:y1, x0:x1]).resize((w, height), Image.LANCZOS) for f in frames]


def save_sheet(images, name):
    w, h = images[0].size
    rows = (len(images) + COLUMNS - 1) // COLUMNS
    sheet = Image.new("RGBA", (COLUMNS * w, rows * h), (0, 0, 0, 0))
    for i, im in enumerate(images):
        sheet.paste(im, ((i % COLUMNS) * w, (i // COLUMNS) * h))
    path = OUT / f"{name}.png"
    # 256-colour palette with alpha (dithered): several times smaller, and at
    # on-screen size the difference from true colour is hard to see.
    sheet.quantize(colors=256, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.FLOYDSTEINBERG).save(path, optimize=True)
    return w, h, path.stat().st_size


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for old in OUT.glob("*.png"):
        old.unlink()
    sprites, walkers, sitters, credits = [], [], [], []
    for clip in CLIPS:
        video, meta = download(clip)
        with tempfile.TemporaryDirectory() as tmp:
            frames = extract(video, clip, tmp)
        if clip["role"] == "walk":
            s, n, err = best_walk_loop(frames)
            frames = frames[s:s + n]
            print(f"{clip['id']}: walk loop frames {s}..{s + n} (match error {err:.3f})")
        images = crop_and_scale(frames, clip["height"])
        name = f"real{clip['id']}" if "id" in clip else "real_" + Path(clip["file"]).stem.replace("-", "_")
        w, h, size = save_sheet(images, name)
        print(f"{clip['id']}: {len(images)} frames {w}x{h}, {size // 1024} KB")
        sprites.append({"name": name, "frames": len(images), "columns": COLUMNS, "size": h, "width": w, "height": h,
                        "frameMs": round(1000 / clip["fps"]), "pingPong": clip["role"] == "sit",
                        "speed": clip.get("speed", 0)})
        (walkers if clip["role"] == "walk" else sitters).append(name)
        credits.append({"sprite": name, "pixabayId": clip.get("id"), "author": meta["user"], "url": meta["pageURL"]})
    catalog = {
        "sprites": sprites,
        "sets": [{"id": "realcat", "name": "真貓散步（真實的貓）", "emoji": sitters, "walkers": walkers}],
        "props": [],
        "credits": credits,
    }
    (OUT / "realcats.json").write_text(json.dumps(catalog, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("total", sum(p.stat().st_size for p in OUT.glob("*.png")) // 1024, "KB")


if __name__ == "__main__":
    main()
