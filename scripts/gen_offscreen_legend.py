"""Render a small legend tile showing the off-screen-target arrow.

The actual LCD swaps to this Triangle sprite whenever the projected target
ring would sit outside the viewport — pinned to the rim, pointing at the
ring. We compose a single representative frame: the arrow tucked against a
viewport edge, pointing outward.

Same colour and sprite the LCD uses (Triangle.dds tinted ACCENT / ACTIVE).
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "docs" / "img" / "offscreen.png"
TRIANGLE_DDS = Path(r"E:/Steam/steamapps/common/SpaceEngineers/Content/Textures/Sprites/Triangle.dds")

W, H = 140, 140

# Match the LCD palette from gen_thumb.py
BG_TOP   = (6, 14, 24)
BG_BOT   = (12, 28, 46)
SCANLINE = (14, 32, 52)
BRACKET  = (60, 130, 175)
ACTIVE   = (255, 200, 100)


def gradient_bg(im, top, bot):
    px = im.load()
    for y in range(im.height):
        t = y / (im.height - 1)
        px_color = (
            int(top[0] + (bot[0] - top[0]) * t),
            int(top[1] + (bot[1] - top[1]) * t),
            int(top[2] + (bot[2] - top[2]) * t),
        )
        for x in range(im.width):
            px[x, y] = px_color


def scanlines(draw, im, color, step=3):
    for y in range(0, im.height, step):
        draw.line([(0, y), (im.width, y)], fill=color, width=1)


def tint(sprite_rgba, color):
    out = Image.new("RGBA", sprite_rgba.size, color + (0,))
    out.putalpha(sprite_rgba.split()[3])
    return out


def paste_sprite(target, sprite, cx, cy, size, rotation_deg, color):
    s = tint(sprite, color)
    s = s.resize((size, size), Image.LANCZOS)
    if rotation_deg:
        s = s.rotate(rotation_deg, resample=Image.BICUBIC, expand=True)
    bbox = s.split()[3].getbbox() or (0, 0, *s.size)
    ccx = (bbox[0] + bbox[2]) / 2
    ccy = (bbox[1] + bbox[3]) / 2
    target.alpha_composite(s, (int(round(cx - ccx)), int(round(cy - ccy))))


triangle = Image.open(TRIANGLE_DDS).convert("RGBA")

base = Image.new("RGBA", (W, H), (0, 0, 0, 255))
gradient_bg(base, BG_TOP, BG_BOT)
draw = ImageDraw.Draw(base)
scanlines(draw, base, SCANLINE, step=3)

# Inset border lines, like a viewport edge, on the right side so the arrow
# can pin against it.
edge_x = W - 18
draw.line([(edge_x, 8), (edge_x, H - 8)], fill=BRACKET, width=2)

# Triangle pinned just inside the right edge, pointing right (outward).
# Triangle.dds defaults to pointy-up (-Y); PIL.rotate is CCW, so -90deg aims
# it right (+X) — the same direction the LCD's `atan2(dy, dx) + π/2` math
# produces for an off-viewport target sitting to the right.
ARROW_PX = 60
paste_sprite(base, triangle, edge_x - 22, H // 2, ARROW_PX, -90, ACTIVE)

base.convert("RGB").save(OUT, "PNG")
print(f"wrote {OUT}  ({W}x{H})")
