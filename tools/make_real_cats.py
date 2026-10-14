"""Builds the "真貓散步" (real cat) sprite sheets from Pixabay green-screen videos.

Requires: ffmpeg on PATH, Pillow, numpy, and a free Pixabay API key in the
PIXABAY_API_KEY environment variable (https://pixabay.com/api/docs/).

Usage: PIXABAY_API_KEY=... python tools/make_real_cats.py

Clips can also be local green-screen videos (e.g. ones you generated yourself):
use {"file": "tools/source/my-cat.mp4", "author": "...", ...} instead of "id".
See docs/cat-video-brief.md for what such videos should look like.

Local clips (the project owner's AI-generated cats) live in
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

# Each cat is a list of clips of the same cat. Roles:
# "walk":    walking in place, facing right; the app moves it across the screen.
# "sit":     idling in place; played forward then backward (ping-pong).
# "act":     the sitting cat doing something (meowing, waving, drinking); one per visit.
# "sitdown": arriving and sitting down (played backwards to stand up if the cat has no "standup").
#            "walks": False if the cat doesn't walk first (e.g. stretches); "sitStart": seconds where sitting starts.
# "standup": getting up from sitting and walking off; "walkStart": seconds where walking starts.
# "fit": "auto" sizes every clip to match the walk clip (for clips framed differently):
#   the first walking sit-down links walking and sitting size, then sitting clips match
#   the sitting size and walking/standing clips the walking size. Without it the clips
#   share one SCALE (all framed alike). "size": extra factor for clips that can't be
#   matched (e.g. drinking, a pose that's in no other clip).
CATS = [
    {
        # The project owner's AI-generated orange tabby; all clips framed alike.
        "id": "realcat", "name": "真貓橘子（走過來陪你）", "prefix": "orange",
        "clips": [
            {"file": "tools/source/orange-walk.mp4", "role": "walk", "fps": 24},
            {"file": "tools/source/orange-walk-to-sit.mp4", "role": "sitdown", "fps": 10},
            # Stops, stretches, then sits: doesn't walk, so the cat stays put while it plays.
            {"file": "tools/source/orange-stretch.mp4", "role": "sitdown", "fps": 10, "walks": False, "sitStart": 5.0},
            {"file": "tools/source/orange-sit.mp4", "role": "sit", "fps": 8, "start": 0.0, "length": 5.0},
            {"file": "tools/source/orange-meow.mp4", "role": "act", "fps": 10, "start": 0.0, "length": 6.5},
            {"file": "tools/source/orange-wave-v2.mp4", "role": "act", "fps": 10, "start": 0.0, "length": 6.5},
            {"file": "tools/source/orange-head-tilt.mp4", "role": "act", "fps": 10, "start": 0.0, "length": 5.1},
            {"file": "tools/source/orange-drink.mp4", "role": "act", "fps": 10, "start": 0.5, "length": 7.0},
        ],
    },
    {
        # AI-generated calico (三花), by the project owner. Clips are framed differently.
        "id": "realcat2", "name": "真貓三花（走過來陪你）", "fit": "auto",
        "clips": [
            {"file": "tools/source/cat2-walk.mp4", "role": "walk", "fps": 24},
            {"file": "tools/source/cat2-walk-to-sit.mp4", "role": "sitdown", "fps": 10, "sitStart": 3.6},
            {"file": "tools/source/cat2-stretch.mp4", "role": "sitdown", "fps": 10, "walks": False, "sitStart": 4.0},
            {"file": "tools/source/cat2-sit-to-walk.mp4", "role": "standup", "fps": 10, "walkStart": 3.0},
            {"file": "tools/source/cat2-sit.mp4", "role": "sit", "fps": 8, "start": 0.0, "length": 5.0},
            {"file": "tools/source/cat2-meow.mp4", "role": "act", "fps": 10, "start": 0.0, "length": 6.5},
            {"file": "tools/source/cat2-wave.mp4", "role": "act", "fps": 10, "start": 0.0, "length": 6.5},
            {"file": "tools/source/cat2-head-tilt.mp4", "role": "act", "fps": 10, "start": 0.0, "length": 5.1},
            {"file": "tools/source/cat2-drink.mp4", "role": "act", "fps": 10, "start": 0.5, "length": 7.0, "size": 0.8},
        ],
    },
    {
        # AI-generated Siamese (暹羅), by the project owner.
        "id": "realcat3", "name": "真貓暹羅（走過來陪你）", "fit": "auto",
        "clips": [
            {"file": "tools/source/cat3-walk.mp4", "role": "walk", "fps": 24},
            {"file": "tools/source/cat3-walk-to-sit.mp4", "role": "sitdown", "fps": 10, "sitStart": 2.9},
            {"file": "tools/source/cat3-stretch.mp4", "role": "sitdown", "fps": 10, "walks": False, "sitStart": 4.0},
            {"file": "tools/source/cat3-sit-to-walk.mp4", "role": "standup", "fps": 10, "walkStart": 2.8},
            {"file": "tools/source/cat3-sit.mp4", "role": "sit", "fps": 8, "start": 0.0, "length": 5.0},
            {"file": "tools/source/cat3-meow.mp4", "role": "act", "fps": 10, "start": 0.0, "length": 6.5},
            {"file": "tools/source/cat3-wave.mp4", "role": "act", "fps": 10, "start": 0.0, "length": 6.5},
            {"file": "tools/source/cat3-head-tilt.mp4", "role": "act", "fps": 10, "start": 0.0, "length": 5.1},
            {"file": "tools/source/cat3-drink.mp4", "role": "act", "fps": 10, "start": 0.5, "length": 7.0, "size": 1.0},
        ],
    },
    {
        # AI-generated British Shorthair (英短), by the project owner. The sitting clips
        # have a vignetted screen (paler round the cat), so they get a softer key.
        "id": "realcat4", "name": "真貓英短（走過來陪你）", "fit": "auto",
        "clips": [
            {"file": "tools/source/cat4-walk.mp4", "role": "walk", "fps": 24},
            {"file": "tools/source/cat4-walk-to-sit.mp4", "role": "sitdown", "fps": 10, "sitStart": 3.5},
            {"file": "tools/source/cat4-stretch.mp4", "role": "sitdown", "fps": 10, "walks": False, "sitStart": 4.2},
            {"file": "tools/source/cat4-sit-to-walk.mp4", "role": "standup", "fps": 10, "walkStart": 3.2},
            {"file": "tools/source/cat4-sit.mp4", "role": "sit", "fps": 8, "start": 0.0, "length": 5.0, "key": (25, 55)},
            {"file": "tools/source/cat4-meow.mp4", "role": "act", "fps": 10, "start": 0.0, "length": 6.5, "key": (25, 55)},
            {"file": "tools/source/cat4-wave.mp4", "role": "act", "fps": 10, "start": 0.0, "length": 6.5, "key": (25, 55)},
            {"file": "tools/source/cat4-head-tilt.mp4", "role": "act", "fps": 10, "start": 0.0, "length": 5.1, "key": (25, 55)},
            {"file": "tools/source/cat4-drink.mp4", "role": "act", "fps": 10, "start": 0.5, "length": 7.0, "size": 1.0},
        ],
    },
]
COLUMNS = 8
# Source pixels -> sprite pixels, shared by all clips. On a 1080p screen the sitting
# cat is drawn ~370 px tall from ~230 px sprites: a bit soft, but keeps the app small.
SCALE = 0.32
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
    raws = [np.array(Image.open(p).convert("RGB")) for p in sorted(Path(tmp).glob("*.png"))]
    key = clip.get("key", (18, 60))
    if key == "auto":
        # Pale or gradient screens: key relative to how green the background really is.
        border = np.concatenate([g.ravel() for f in raws[:: max(1, len(raws) // 6)]
                                 for g in (greenness(f[:24]), greenness(f[:, :24]), greenness(f[:, -24:]))])
        bg = float(np.percentile(border, 10))
        key = (bg * 0.4, bg * 0.8)
        print(f"  {Path(video).name}: background greenness {bg:.0f} -> key {key[0]:.0f}..{key[1]:.0f}")
    frames = [green_key(f, *key) for f in raws]
    return [despeckle(f) for f in frames] if clip.get("despeckle") else frames


def despeckle(frame):
    """Drops faint specks left on a pale or uneven screen: keeps only what is near solid cat."""
    solid = Image.fromarray(((frame[..., 3] > 80) * 255).astype(np.uint8))
    near = np.array(solid.filter(ImageFilter.MinFilter(5)).filter(ImageFilter.MaxFilter(13))) > 0
    frame[~near] = 0
    return frame


def greenness(rgb):
    f = rgb.astype(np.float32)
    return f[..., 1] - np.maximum(f[..., 0], f[..., 2])


def body_height(frames):
    """
    Size of the cat, robust to a raised tail: the tallest column of solid pixels
    (back to paw for a walking cat, head to floor for a sitting one), median over frames.
    """
    return float(np.median([(f[..., 3] > 128).sum(axis=0).max() for f in frames]))


def sitting_height(frames):
    """Height of a sitting cat (ears to floor; its tail lies on the floor), median over frames."""
    def rows(f):
        r = np.nonzero((f[..., 3] > 128).sum(axis=1) >= 8)[0]
        return r.max() - r.min()
    return float(np.median([rows(f) for f in frames]))


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


def sit_start(clip, frames):
    if "sitStart" in clip:
        return min(len(frames) - 1, round(clip["sitStart"] * clip["fps"]))
    # Sitting down raises the head: it starts where the cat starts getting taller.
    heights = [np.ptp(np.nonzero(f[..., 3].max(axis=1) > 20)[0]) for f in frames]
    walk_h = np.median(heights[:max(3, len(heights) // 5)])
    return next((i for i, h in enumerate(heights) if h > walk_h * 1.06), len(frames) - 1)


def fit_sizes(cat, clips):
    """
    For cats whose clips are framed differently: per-clip size factors that make the
    cat as big as in the walk clip. Returns {clip index: factor} (walk clip = 1).
    """
    walk = next(i for i, (c, _) in enumerate(clips) if c["role"] == "walk")
    W = body_height(clips[walk][1])
    factors = {walk: 1.0}
    sit_sizes = []  # the sitting cat's size, in walk-clip pixels
    for i, (c, frames) in enumerate(clips):
        if c["role"] == "sitdown" and c.get("walks", True):
            stand = sit_start(c, frames)
            factors[i] = W / body_height(frames[:max(3, stand // 2)])
            sit_sizes.append(sitting_height(frames[-4:]) * factors[i])
        if c["role"] == "standup":
            go = round(c["walkStart"] * c["fps"])
            sit_sizes.append(sitting_height(frames[:4]) * W / body_height(frames[go:]))
    sit = float(np.exp(np.mean(np.log(sit_sizes))))
    for i, (c, frames) in enumerate(clips):
        if i in factors:
            continue
        if "size" in c:
            factors[i] = c["size"]
        elif c["role"] == "sitdown":
            factors[i] = sit / sitting_height(frames[-4:])
        elif c["role"] == "standup":
            go = round(c["walkStart"] * c["fps"])
            factors[i] = float(np.sqrt(sit / sitting_height(frames[:4]) * W / body_height(frames[go:])))
        else:
            # Sitting clips start (and end) in the plain sitting pose.
            factors[i] = sit / sitting_height(frames[:3])
    print(f"  {cat['id']}: walking {W:.0f} px, sitting {sit:.0f} px (estimates {[round(x) for x in sit_sizes]})")
    return factors, W


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for old in OUT.glob("*.png"):
        old.unlink()
    sprites, credits, sets = [], [], []
    for cat in CATS:
        clips = []
        for clip in cat["clips"]:
            auto = cat.get("fit") == "auto"
            clip = {"key": "auto" if auto else (18, 60), "despeckle": auto, **clip}
            video, meta = download(clip)
            with tempfile.TemporaryDirectory() as tmp:
                clips.append((clip, extract(video, clip, tmp), meta))
        if cat.get("fit") == "auto":
            factors, walk_h = fit_sizes(cat, [(c, f) for c, f, _ in clips])
            base = 170 / walk_h  # walking cat about 170 px tall in the sheet, like the orange cat
        else:
            factors, base = {i: 1.0 for i in range(len(clips))}, SCALE
        roles = {"walk": [], "sitdown": [], "standup": [], "sit": [], "act": []}
        walk_sprite, ground_speed, walk_scale = None, None, base
        for i, (clip, frames, meta) in enumerate(clips):
            name = f"real{clip['id']}" if "id" in clip else "real_" + Path(clip["file"]).stem.replace("-", "_")
            scale = base * factors[i]
            extra = {}
            if clip["role"] == "walk":
                shifts = paw_shifts(frames)
                planted = shifts[(shifts > 2) & (shifts < 85)]
                # Mean, not median: some generated videos repeat or skip a frame now and then.
                ground_speed = float(np.mean(planted)) * clip["fps"]  # source px per second
                walk_scale = scale
                print(f"{name}: planted paws slide {np.mean(planted):.1f} px/frame -> {ground_speed:.0f} px/s")
                s, n, err = best_walk_loop(frames)
                frames = frames[s:s + n]
                print(f"{name}: walk loop frames {s}..{s + n} (match error {err:.3f})")
            if clip["role"] == "sitdown":
                stand = sit_start(clip, frames)
                print(f"{name}: sits down from frame {stand} of {len(frames)}")
            if clip["role"] == "standup":
                stand = min(len(frames) - 1, round(clip["walkStart"] * clip["fps"]))
                print(f"{name}: walks from frame {stand} of {len(frames)}")
            soften_edges(frames)
            images = crop_and_scale(frames, clip.get("height"), scale)
            w, h, size = save_sheet(images, name)
            print(f"{name}: {len(images)} frames {w}x{h} (scale {scale:.3f}), {size // 1024} KB")
            sprite = {"name": name, "frames": len(images), "columns": COLUMNS, "size": h, "width": w, "height": h,
                      "frameMs": round(1000 / clip["fps"]), "pingPong": clip["role"] == "sit",
                      "anchorX": round(anchor_x(images), 3)}
            if clip["role"] == "sitdown":
                sprite["walkFrames"] = stand              # sitting starts here (standing up = reverse from the end to here)
                sprite["walks"] = clip.get("walks", True)  # does the cat walk (in place) before sitting?
            if clip["role"] == "standup":
                sprite["walkFrames"] = stand              # standing up is done here; from here on it walks (in place)
                # The cat moves within the frame while getting up: where it sits at the
                # start and where it walks at the end, to line up with the sit and walk clips.
                sprite["startAnchorX"] = round(anchor_x(images[:3]), 3)
                sprite["endAnchorX"] = round(anchor_x(images[stand:]), 3)
            sprites.append(sprite)
            if clip["role"] == "walk":
                walk_sprite = sprite
            roles[clip["role"]].append(name)
            credits.append({"sprite": name, "pixabayId": clip.get("id"), "author": meta["user"] or AUTHOR, "url": meta["pageURL"]})

        if walk_sprite is not None:
            # Walk speed in body widths per second, measured from the planted paws so
            # they don't slide when the app moves the cat across the screen.
            walk_sprite["speed"] = round(ground_speed * walk_scale / walk_sprite["width"], 3) if ground_speed else 0.4
            print("walk speed", walk_sprite["speed"], "body widths/s")
        sets.append({"id": cat["id"], "name": cat["name"], "emoji": roles["sit"], "walkers": roles["walk"],
                     "sitDowns": roles["sitdown"], "standUps": roles["standup"], "acts": roles["act"]})

    catalog = {"sprites": sprites, "sets": sets, "props": [], "credits": credits}
    (OUT / "realcats.json").write_text(json.dumps(catalog, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print("total", sum(p.stat().st_size for p in OUT.glob("*.png")) // 1024, "KB")


if __name__ == "__main__":
    main()
