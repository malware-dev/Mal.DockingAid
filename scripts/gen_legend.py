"""Generate the five legend tiles under docs/img/.

Each tile is a fresh 240x240 mini-LCD - same gradient, scanlines and SE
sprite assets the thumbnail uses - showing one feature of the indicator on
its own. Cropping from thumb.png didn't work: every tile that contained the
reticle ended up looking like every other tile, and the roll tile bled into
the title text. Synthesising each one separately keeps the highlighted
feature focused (rendered in the accent colour) while the contextual frame
stays chrome-dim.
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = ROOT / "docs" / "img"
OUT_DIR.mkdir(parents=True, exist_ok=True)

SE_TEX = Path(r"E:/Steam/steamapps/common/SpaceEngineers/Content/Textures")
CIRCLE_HOLLOW_DDS = SE_TEX / "Sprites" / "Circle_Hollow.dds"
ARROW_SINGLE_DDS  = SE_TEX / "GUI" / "Icons" / "buttons" / "ArrowSingle.dds"
TRIANGLE_DDS      = SE_TEX / "Sprites" / "Triangle.dds"

# Palette - identical to gen_thumb.py so the tiles read as siblings of the
# main thumbnail.
BG_TOP   = (6, 14, 24)
BG_BOT   = (12, 28, 46)
SCANLINE = (14, 32, 52)
BRACKET  = (60, 130, 175)
CHROME   = (90, 130, 160)
ACTIVE   = (255, 200, 100)
ACCENT   = (255, 175, 70)

TILE      = 240
R         = 82       # reticle radius - leaves a small margin to the tile edge
RAIL_W    = 3
CHEV      = 34       # chevron sprite size on tile - sized for legend legibility
TIP_GAP   = 10

CX, CY = TILE // 2, TILE // 2


# ----------------------------------------------------------------------- bg

def make_bg() -> Image.Image:
    im = Image.new("RGBA", (TILE, TILE), (0, 0, 0, 255))
    px = im.load()
    for y in range(TILE):
        t = y / (TILE - 1)
        c = (
            int(BG_TOP[0] + (BG_BOT[0] - BG_TOP[0]) * t),
            int(BG_TOP[1] + (BG_BOT[1] - BG_TOP[1]) * t),
            int(BG_TOP[2] + (BG_BOT[2] - BG_TOP[2]) * t),
            255,
        )
        for x in range(TILE):
            px[x, y] = c
    draw = ImageDraw.Draw(im)
    for y in range(0, TILE, 3):
        draw.line([(0, y), (TILE, y)], fill=SCANLINE, width=1)
    return im


# ----------------------------------------------------------------------- sprite helpers

def tint(sprite: Image.Image, color: tuple[int, int, int]) -> Image.Image:
    out = Image.new("RGBA", sprite.size, color + (0,))
    out.putalpha(sprite.split()[3])
    return out


def paste_sprite(target, sprite, cx, cy, size, rotation_deg, color):
    s = tint(sprite, color).resize((size, size), Image.LANCZOS)
    if rotation_deg:
        s = s.rotate(rotation_deg, resample=Image.BICUBIC, expand=True)
    bbox = s.split()[3].getbbox() or (0, 0, *s.size)
    ccx = (bbox[0] + bbox[2]) / 2
    ccy = (bbox[1] + bbox[3]) / 2
    target.alpha_composite(s, (int(round(cx - ccx)), int(round(cy - ccy))))


def paste_ellipse(target, sprite, cx, cy, w, h, color):
    s = tint(sprite, color).resize((w, h), Image.LANCZOS)
    target.alpha_composite(s, (int(round(cx - w / 2)), int(round(cy - h / 2))))


# ----------------------------------------------------------------------- frame primitives

def draw_reticle_frame(im: Image.Image, circle_hollow: Image.Image, color):
    """Outer ring + cross at tile centre. Used as the context frame on
    every tile except the off-screen arrow."""
    draw = ImageDraw.Draw(im)
    draw.rectangle([CX - R, CY - RAIL_W // 2,
                    CX + R, CY + RAIL_W // 2 + (RAIL_W % 2)], fill=color)
    draw.rectangle([CX - RAIL_W // 2, CY - R,
                    CX + RAIL_W // 2 + (RAIL_W % 2), CY + R], fill=color)
    paste_ellipse(im, circle_hollow, CX, CY, R * 2, R * 2, color)


# ----------------------------------------------------------------------- tiles

def reticle_tile(circle_hollow, arrow_single, triangle):
    """Outer ring + cross only - the static reference frame, drawn in
    chrome the way the LCD actually renders it."""
    im = make_bg()
    draw_reticle_frame(im, circle_hollow, CHROME)
    return im


def target_ring_tile(circle_hollow, arrow_single, triangle):
    """Reticle (chrome) + target ring (accent), offset to suggest
    a small misalignment - same offset feel as the main thumbnail."""
    im = make_bg()
    draw_reticle_frame(im, circle_hollow, CHROME)
    tr_d = int(R * 1.15)
    paste_ellipse(im, circle_hollow, CX + 14, CY - 9, tr_d, tr_d, ACCENT)
    return im


def pitch_yaw_tile(circle_hollow, arrow_single, triangle):
    """Reticle (chrome) + pitch and yaw chevron pairs (accent)."""
    im = make_bg()
    draw_reticle_frame(im, circle_hollow, CHROME)

    pitch_y = CY - 32
    gap_x = CHEV // 2 + TIP_GAP
    paste_sprite(im, arrow_single, CX - gap_x, pitch_y, CHEV, 0,   ACTIVE)
    paste_sprite(im, arrow_single, CX + gap_x, pitch_y, CHEV, 180, ACTIVE)

    yaw_x = CX + 32
    gap_y = CHEV // 2 + TIP_GAP
    paste_sprite(im, arrow_single, yaw_x, CY - gap_y, CHEV, -90, ACTIVE)
    paste_sprite(im, arrow_single, yaw_x, CY + gap_y, CHEV,  90, ACTIVE)
    return im


def roll_tile(circle_hollow, arrow_single, triangle):
    """Reticle (chrome) + roll chevron pair on the rim (accent)."""
    im = make_bg()
    draw_reticle_frame(im, circle_hollow, CHROME)

    # Roll pair offset CW from top - same 34deg as the thumbnail. Bumped
    # away from the rim a bit more than the live LCD so the chevrons are
    # readable at tile scale.
    roll_a = math.radians(34)
    rdx, rdy = math.sin(roll_a), -math.cos(roll_a)
    in_x  = CX + rdx * (R - 14)
    in_y  = CY + rdy * (R - 14)
    out_x = CX + rdx * (R + 14)
    out_y = CY + rdy * (R + 14)
    ang_deg = math.degrees(roll_a)
    paste_sprite(im, arrow_single, in_x,  in_y,  CHEV, 90 - ang_deg,  ACTIVE)
    paste_sprite(im, arrow_single, out_x, out_y, CHEV, -90 - ang_deg, ACTIVE)
    return im


def offscreen_tile(circle_hollow, arrow_single, triangle):
    """A viewport edge with the off-screen Triangle pinned against it."""
    im = make_bg()
    draw = ImageDraw.Draw(im)
    # Right-edge "viewport boundary" so it's visually obvious which way is
    # outward.
    edge_x = TILE - 22
    draw.line([(edge_x, 12), (edge_x, TILE - 12)], fill=BRACKET, width=3)
    # Triangle.dds is pointy-up (-Y); PIL.rotate is CCW so -90deg aims +X,
    # matching the LCD's `atan2(dy, dx) + pi/2` for a right-side off-target.
    paste_sprite(im, triangle, edge_x - 36, TILE // 2, 100, -90, ACTIVE)
    return im


# ----------------------------------------------------------------------- run

def main():
    circle_hollow = Image.open(CIRCLE_HOLLOW_DDS).convert("RGBA")
    arrow_single  = Image.open(ARROW_SINGLE_DDS).convert("RGBA")
    triangle      = Image.open(TRIANGLE_DDS).convert("RGBA")

    tiles = [
        ("reticle.png",     reticle_tile),
        ("target-ring.png", target_ring_tile),
        ("pitch-yaw.png",   pitch_yaw_tile),
        ("roll.png",        roll_tile),
        ("offscreen.png",   offscreen_tile),
    ]
    for fn, factory in tiles:
        im = factory(circle_hollow, arrow_single, triangle)
        im.convert("RGB").save(OUT_DIR / fn)
        print(f"wrote {fn}  {im.size[0]}x{im.size[1]}")


if __name__ == "__main__":
    main()
