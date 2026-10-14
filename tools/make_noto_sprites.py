"""Downloads Google's Noto animated cat emoji and converts them to PNG sprite sheets.

Source: https://googlefonts.github.io/noto-emoji-animation/  (CC BY 4.0, by Google)
Output: src/CatSaver/Assets/Noto/<codepoint>.png + sprites.json

Usage: python tools/make_noto_sprites.py   (requires Pillow)
"""
import io
import json
import urllib.request
from pathlib import Path

from PIL import Image

# Played in this order, each for a few loops.
EMOJI = [
    ("1f63a", "grinning cat"),
    ("1f638", "grinning cat with smiling eyes"),
    ("1f63b", "smiling cat with heart-eyes"),
    ("1f63d", "kissing cat"),
]
SIZE = 384        # frame size in pixels
FRAME_STEP = 2    # keep every 2nd frame (~15 fps) to keep the sheets small
COLUMNS = 8

out_dir = Path(__file__).resolve().parent.parent / "src" / "CatSaver" / "Assets" / "Noto"
out_dir.mkdir(parents=True, exist_ok=True)

meta = []
for code, name in EMOJI:
    url = f"https://fonts.gstatic.com/s/e/notoemoji/latest/{code}/512.webp"
    with urllib.request.urlopen(url) as r:
        im = Image.open(io.BytesIO(r.read()))

    frames, durations = [], []
    for i in range(0, im.n_frames, FRAME_STEP):
        im.seek(i)
        frames.append(im.convert("RGBA").resize((SIZE, SIZE), Image.LANCZOS))
        durations.append(im.info.get("duration", 33) * FRAME_STEP)

    rows = (len(frames) + COLUMNS - 1) // COLUMNS
    sheet = Image.new("RGBA", (COLUMNS * SIZE, rows * SIZE), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        sheet.paste(f, ((i % COLUMNS) * SIZE, (i // COLUMNS) * SIZE))

    # Emoji art is flat-coloured, so a 256-colour palette with alpha looks the same
    # and is several times smaller.
    sheet.quantize(colors=256, method=Image.Quantize.FASTOCTREE).save(out_dir / f"{code}.png", optimize=True)
    if code == EMOJI[0][0]:
        # App/tray icon: first frame of the first cat.
        im.seek(0)
        icon = im.convert("RGBA")
        icon_path = out_dir.parent / "catsaver.ico"
        icon.save(icon_path, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

    meta.append({
        "name": code,
        "description": name,
        "frames": len(frames),
        "columns": COLUMNS,
        "size": SIZE,
        "frameMs": round(sum(durations) / len(durations)),
    })
    print(code, len(frames), "frames", (out_dir / f"{code}.png").stat().st_size // 1024, "KB")

(out_dir / "sprites.json").write_text(json.dumps(meta, indent=2) + "\n")
