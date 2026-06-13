"""Crop pieces of thumb.png to use as visual legend in WORKSHOP.md.

Coordinates lifted from gen_thumb.py — indicator block centred at (178, 225)
with reticle radius 128, target ring centred at (200, 211) with diameter 150.
"""

from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "Mal.DockingAid" / "thumb.png"
OUT_DIR = ROOT / "docs" / "img"
OUT_DIR.mkdir(parents=True, exist_ok=True)

src = Image.open(SRC).convert("RGB")

CROPS = {
    # The whole reticle (outer ring + cross + target ring inside it).
    "reticle.png": (40, 90, 320, 365),
    # Tight on the orange target ring.
    "target-ring.png": (115, 128, 290, 298),
    # Pitch + yaw chevron cluster (cross notches) inside the inner ring.
    "pitch-yaw.png": (140, 168, 245, 258),
    # Roll chevron pair on the upper-right of the rim.
    "roll.png": (210, 90, 295, 175),
}

for fn, box in CROPS.items():
    crop = src.crop(box)
    crop.save(OUT_DIR / fn)
    print(f"wrote {fn}  {crop.size[0]}x{crop.size[1]}")
