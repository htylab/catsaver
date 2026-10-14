"""Downloads Google's Noto animated emoji and converts them to PNG sprite sheets.

Source: https://googlefonts.github.io/noto-emoji-animation/  (CC BY 4.0, by Google)
Output: src/CatSaver/Assets/Noto/<codepoint>.png + sprites.json, and the app icon.

Usage: python tools/make_noto_sprites.py   (requires Pillow)

To add a cat set, add its emoji to SPRITES and a new entry to SETS.
Codepoints are listed on the page above (use the "512.webp" download).
"""
import io
import json
import urllib.request
from pathlib import Path

from PIL import Image

# codepoint -> (description, frame size in px)
SPRITES = {
    "1f63a": ("grinning cat", 384),
    "1f638": ("grinning cat with smiling eyes", 384),
    "1f63b": ("smiling cat with heart-eyes", 384),
    "1f63d": ("kissing cat", 384),
    "1f640": ("weary cat", 384),
    "1f63f": ("crying cat", 384),
    "1f63e": ("pouting cat", 384),
    "1f639": ("cat with tears of joy", 384),
    "1f63c": ("cat with wry smile", 384),
    "1f431": ("cat face", 384),
    "1f981": ("lion", 384),
    "1f405": ("tiger", 384),
    # Drink props shown next to the cat.
    "1f9cb": ("bubble tea", 256),
    "1f375": ("teacup without handle", 256),
    "1f4a7": ("droplet", 256),
}

# Cat "sets": each plays its emoji in order, a few seconds each.
SETS = [
    {"id": "happy", "name": "開心貓", "emoji": ["1f63a", "1f638", "1f63b", "1f63d"]},
    {"id": "drama", "name": "戲精貓（催你喝水）", "emoji": ["1f640", "1f63f", "1f63e", "1f639"]},
    {"id": "cool", "name": "傲嬌貓", "emoji": ["1f63c", "1f431", "1f639"]},
    {"id": "bigcat", "name": "大貓（獅子、老虎）", "emoji": ["1f981", "1f405"]},
]
PROPS = ["1f9cb", "1f375", "1f4a7"]

FRAME_STEP = 2    # keep every 2nd frame (~15 fps) to keep the sheets small
COLUMNS = 8
ICON_SOURCE = "1f63a"

out_dir = Path(__file__).resolve().parent.parent / "src" / "CatSaver" / "Assets" / "Noto"
out_dir.mkdir(parents=True, exist_ok=True)
for old in out_dir.glob("*.png"):
    old.unlink()

sprites = []
for code, (name, size) in SPRITES.items():
    url = f"https://fonts.gstatic.com/s/e/notoemoji/latest/{code}/512.webp"
    with urllib.request.urlopen(url) as r:
        im = Image.open(io.BytesIO(r.read()))

    frames, durations = [], []
    for i in range(0, im.n_frames, FRAME_STEP):
        im.seek(i)
        frames.append(im.convert("RGBA").resize((size, size), Image.LANCZOS))
        durations.append(im.info.get("duration", 33) * FRAME_STEP)

    rows = (len(frames) + COLUMNS - 1) // COLUMNS
    sheet = Image.new("RGBA", (COLUMNS * size, rows * size), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        sheet.paste(f, ((i % COLUMNS) * size, (i // COLUMNS) * size))

    # Emoji art is flat-coloured, so a 256-colour palette with alpha looks the same
    # and is several times smaller.
    sheet.quantize(colors=256, method=Image.Quantize.FASTOCTREE).save(out_dir / f"{code}.png", optimize=True)

    if code == ICON_SOURCE:
        im.seek(0)
        im.convert("RGBA").save(out_dir.parent / "catsaver.ico",
                                sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

    sprites.append({
        "name": code,
        "description": name,
        "frames": len(frames),
        "columns": COLUMNS,
        "size": size,
        "frameMs": round(sum(durations) / len(durations)),
    })
    print(code, name, len(frames), "frames", (out_dir / f"{code}.png").stat().st_size // 1024, "KB")

catalog = {"sprites": sprites, "sets": SETS, "props": PROPS}
(out_dir / "sprites.json").write_text(json.dumps(catalog, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
total = sum(p.stat().st_size for p in out_dir.glob("*.png"))
print("total", total // 1024, "KB")
