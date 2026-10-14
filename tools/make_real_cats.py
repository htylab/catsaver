"""Builds the "真貓散步" (real cat) sprite sheets from Pixabay green-screen videos.

Requires: ffmpeg on PATH, Pillow, numpy, and a free Pixabay API key in the
PIXABAY_API_KEY environment variable (https://pixabay.com/api/docs/).

Usage: PIXABAY_API_KEY=... python tools/make_real_cats.py

Clips can also be local green-screen videos (e.g. ones you generated yourself):
use {"file": "tools/source/my-cat.mp4", "author": "...", ...} instead of "id".
See docs/cat-video-brief.md for what such videos should look like.

Local clips (the project owner's AI-generated orange tabby) live in
tools/source/. Pixabay downloads go to tools/.cache/ (git-ignored) and are not
committed. Only the processed, background-removed sprite sheets go into
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
# role "sit":  a cat idling in place; played forward then backward (ping-pong).
# role "act":  the cat doing something (meowing, waving, drinking); one is played now and then.
# role "sitdown": the cat arriving and sitting down (also played backwards to stand up).
#   "walks": False if the cat doesn't walk first (e.g. stretches); "sitStart": seconds where sitting starts.
CLIPS = [
    # One AI-generated orange tabby (by the project owner) in several clips, all
    # framed alike, so one shared "scale" keeps the cat the same size in every clip.
    {"file": "tools/source/orange-walk.mp4", "role": "walk", "fps": 24},
    {"file": "tools/source/orange-walk-to-sit.mp4", "role": "sitdown", "fps": 12},
    # Stops, stretches, then sits: doesn't walk, so the cat stays put while it plays.
    {"file": "tools/source/orange-stretch.mp4", "role": "sitdown", "fps": 12, "walks": False, "sitStart": 5.0},
    {"file": "tools/source/orange-sit.mp4", "role": "sit", "fps": 8, "start": 0.0, "length": 5.0},
    {"file": "tools/source/orange-meow.mp4", "role": "act", "fps": 10, "start": 0.0, "length": 6.5},
    {"file": "tools/source/orange-wave-v2.mp4", "role": "act", "fps": 10, "start": 0.0, "length": 6.5},
    {"file": "tools/source/orange-head-tilt.mp4", "role": "act", "fps": 10, "start": 0.0, "length": 5.1},
    {"file": "tools/source/orange-drink.mp4", "role": "act", "fps": 10, "start": 0.5, "length": 7.0},
]
COLUMNS = 8
SCALE = 0.4            # source pixels -> sprite pixels, shared by all clips
AUTHOR = "htylab (AI-generated)"
EDGE_FADE = 28         # px: soften a tail/paw that runs off the edge of the source frame


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


def soften_edges(frames):
    """If the cat touches the source frame edge (e.g. a tail cut off), fade it out there."""
    h, w = frames[0].shape[:2]
    ramp_x = np.minimum(np.minimum(np.arange(w), np.arange(w)[::-1]) / EDGE_FADE, 1.0)
    ramp_y = np.minimum(np.arange(h) / EDGE_FADE, 1.0)  # top edge only; the bottom is the floor
    for f in frames:
        a = f[..., 3].astype(np.float32)
        a *= ramp_x[None, :]
        a *= ramp_y[:, None]
        f[..., 3] = a.astype(np.uint8)


def crop_and_scale(frames, height=None, scale=SCALE):
    alpha = np.max([f[..., 3] for f in frames], axis=0)
    ys, xs = np.nonzero(alpha > 20)
    y0, y1, x0, x1 = ys.min(), ys.max() + 1, xs.min(), xs.max() + 1
    pad = 4
    y0, x0 = max(0, y0 - pad), max(0, x0 - pad)
    y1, x1 = min(alpha.shape[0], y1 + pad), min(alpha.shape[1], x1 + pad)
    if height:
        scale = height / (y1 - y0)
    w, h = round((x1 - x0) * scale), round((y1 - y0) * scale)
    return [Image.fromarray(f[y0:y1, x0:x1]).resize((w, h), Image.LANCZOS) for f in frames]


def paw_shifts(frames, max_shift=90):
    """
    Per-frame horizontal shift of the paws on the ground (bottom strip of the matte).
    For a cat walking in place, planted paws slide backwards at exactly the speed the
    cat would really walk, so this gives the ground speed. Positive = moving left.
    """
    a = np.array([f[..., 3] for f in frames]).astype(np.float32)
    rows = np.nonzero(a.max(axis=(0, 2)) > 20)[0]
    bottom = rows.max()
    band = a[:, max(0, bottom - 14):bottom + 1, :].sum(axis=1)
    shifts = []
    for p, q in zip(band, band[1:]):
        best = (-1.0, 0)
        for d in range(-max_shift, max_shift + 1):
            c = (p[d:] * q[:len(q) - d]).sum() if d >= 0 else (p[:d] * q[-d:]).sum()
            if c > best[0]:
                best = (c, d)
        shifts.append(best[1])
    return np.array(shifts)


def body_x(frame):
    """Horizontal centre of the head and body (upper 70% of the cat) in pixels."""
    a = frame[..., 3].astype(np.float32)
    ys = np.nonzero(a.max(axis=1) > 20)[0]
    if len(ys) == 0:
        return 0.0
    top, bottom = ys.min(), ys.max()
    upper = a[top: top + int((bottom - top) * 0.7) + 1]
    cols = upper.sum(axis=0)
    return float((cols * np.arange(len(cols))).sum() / max(cols.sum(), 1))


def anchor_x(images):
    """Horizontal centre of the cat's head and body (upper 70%, so a sideways tail
    doesn't pull it), as a fraction of the frame width. Used to line clips up."""
    alpha = np.max([np.array(im)[..., 3] for im in images], axis=0).astype(np.float32)
    upper = alpha[: int(alpha.shape[0] * 0.7)]
    cols = upper.sum(axis=0)
    return float((cols * np.arange(len(cols))).sum() / max(cols.sum(), 1) / alpha.shape[1])


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
    sprites, credits = [], []
    roles = {"walk": [], "sitdown": [], "sit": [], "act": []}
    walk_sprite, ground_speed = None, None
    for clip in CLIPS:
        name = f"real{clip['id']}" if "id" in clip else "real_" + Path(clip["file"]).stem.replace("-", "_")
        video, meta = download(clip)
        with tempfile.TemporaryDirectory() as tmp:
            frames = extract(video, clip, tmp)
        extra = {}
        if clip["role"] == "walk":
            shifts = paw_shifts(frames)
            planted = shifts[(shifts > 2) & (shifts < 85)]
            ground_speed = float(np.median(planted)) * clip["fps"]  # source px per second
            print(f"{name}: planted paws slide {np.median(planted):.1f} px/frame -> {ground_speed:.0f} px/s")
            s, n, err = best_walk_loop(frames)
            frames = frames[s:s + n]
            print(f"{name}: walk loop frames {s}..{s + n} (match error {err:.3f})")
        if clip["role"] == "sitdown":
            if "sitStart" in clip:
                stand = min(len(frames) - 1, round(clip["sitStart"] * clip["fps"]))
            else:
                # Sitting down raises the head: it starts where the cat starts getting taller.
                heights = [np.ptp(np.nonzero(f[..., 3].max(axis=1) > 20)[0]) for f in frames]
                walk_h = np.median(heights[:max(3, len(heights) // 5)])
                stand = next((i for i, h in enumerate(heights) if h > walk_h * 1.06), len(frames) - 1)
            print(f"{name}: sits down from frame {stand} of {len(frames)}")
        soften_edges(frames)
        images = crop_and_scale(frames, clip.get("height"))
        w, h, size = save_sheet(images, name)
        print(f"{name}: {len(images)} frames {w}x{h}, {size // 1024} KB")
        sprite = {"name": name, "frames": len(images), "columns": COLUMNS, "size": h, "width": w, "height": h,
                  "frameMs": round(1000 / clip["fps"]), "pingPong": clip["role"] == "sit",
                  "anchorX": round(anchor_x(images), 3)}
        if clip["role"] == "sitdown":
            sprite["startAnchorX"] = round(body_x(np.array(images[0])) / w, 3)
            sprite["endAnchorX"] = round(body_x(np.array(images[-1])) / w, 3)
            sprite["walkFrames"] = stand              # sitting starts here (standing up = reverse from the end to here)
            sprite["walks"] = clip.get("walks", True)  # does the cat walk (in place) before sitting?
        sprites.append(sprite)
        if clip["role"] == "walk":
            walk_sprite = sprite
        roles[clip["role"]].append(name)
        credits.append({"sprite": name, "pixabayId": clip.get("id"), "author": meta["user"] or AUTHOR, "url": meta["pageURL"]})

    if walk_sprite is not None:
        # Walk speed in body widths per second, measured from the planted paws so
        # they don't slide when the app moves the cat across the screen.
        walk_sprite["speed"] = round(ground_speed * SCALE / walk_sprite["width"], 3) if ground_speed else 0.4
        print("walk speed", walk_sprite["speed"], "body widths/s")

    catalog = {
        "sprites": sprites,
        "sets": [{"id": "realcat", "name": "真貓橘子（走過來陪你）", "emoji": roles["sit"], "walkers": roles["walk"],
                  "sitDowns": roles["sitdown"], "acts": roles["act"]}],
        "props": [],
        "credits": credits,
    }
    (OUT / "realcats.json").write_text(json.dumps(catalog, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("total", sum(p.stat().st_size for p in OUT.glob("*.png")) // 1024, "KB")


if __name__ == "__main__":
    main()
