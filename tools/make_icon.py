"""Regenerates src/CatSaver/Assets/catsaver.ico (a cat face with a water drop).

Usage: python tools/make_icon.py   (requires Pillow)
"""
from pathlib import Path
from PIL import Image, ImageDraw

S = 1024  # draw large, then downsample for smooth edges
FUR, DARK, PINK, INK, WATER = (246, 165, 90), (120, 70, 35), (255, 160, 175), (70, 50, 45), (90, 180, 255)

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(img)
ow = 34  # outline width

# ears
for sign in (-1, 1):
    cx = S / 2
    ear = [(cx + sign * 400, 470), (cx + sign * 360, 90), (cx + sign * 110, 300)]
    d.polygon(ear, fill=FUR, outline=DARK, width=ow)
    inner = [(cx + sign * 345, 420), (cx + sign * 330, 200), (cx + sign * 180, 320)]
    d.polygon(inner, fill=PINK)

# head
d.ellipse((90, 220, S - 90, S - 60), fill=FUR, outline=DARK, width=ow)
# eyes
for x in (S / 2 - 180, S / 2 + 180):
    d.ellipse((x - 60, 500, x + 60, 650), fill=INK)
    d.ellipse((x - 35, 520, x + 10, 565), fill="white")
# nose + mouth
d.polygon([(S / 2 - 35, 680), (S / 2 + 35, 680), (S / 2, 720)], fill=PINK)
d.arc((S / 2 - 80, 690, S / 2, 770), 0, 180, fill=INK, width=18)
d.arc((S / 2, 690, S / 2 + 80, 770), 0, 180, fill=INK, width=18)
# water drop badge
d.ellipse((700, 690, 960, 950), fill=WATER, outline=(40, 110, 200), width=26)
d.polygon([(735, 760), (925, 760), (830, 600)], fill=WATER)
d.line([(740, 760), (830, 610), (920, 760)], fill=(40, 110, 200), width=26)
d.ellipse((760, 780, 820, 840), fill=(220, 240, 255))

out = Path(__file__).resolve().parent.parent / "src" / "CatSaver" / "Assets" / "catsaver.ico"
img.resize((256, 256), Image.LANCZOS).save(out, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
img.resize((256, 256), Image.LANCZOS).save(out.with_suffix(".png")  # preview only)
print("wrote", out)
