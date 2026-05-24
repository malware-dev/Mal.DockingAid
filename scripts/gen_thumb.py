"""Generate thumb.png for Mal.DockingAid mod.

Uses the actual SE sprite assets (Circle_Hollow.dds and ArrowSingle.dds —
the same `CircleHollow` and `AH_BoreSight` textures the LCD app draws with)
so the thumb is recognisably the indicator rather than a hand-drawn lookalike.

SE sprites are premultiplied-alpha and white-on-transparent. We tint by
substituting a solid colour for RGB and reusing the sprite's alpha, then
alpha-composite with standard blending — algebraically equivalent to the
game's premultiplied draw (`color·α + dst·(1−α)`) for this case.
"""

from __future__ import annotations

import math
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

W, H = 720, 450
OUT = Path(__file__).parent.parent / "Mal.DockingAid" / "thumb.png"

SE_TEXTURES = Path(r"E:/Steam/steamapps/common/SpaceEngineers/Content/Textures")
CIRCLE_HOLLOW_DDS = SE_TEXTURES / "Sprites" / "Circle_Hollow.dds"
ARROW_SINGLE_DDS  = SE_TEXTURES / "GUI" / "Icons" / "buttons" / "ArrowSingle.dds"

# SE LCD palette
BG_TOP     = (6, 14, 24)
BG_BOT     = (12, 28, 46)
SCANLINE   = (14, 32, 52)
BRACKET    = (60, 130, 175)
CHROME     = (90, 130, 160)
FOREGROUND = (200, 220, 235)
ACCENT     = (255, 175, 70)   # amber, SE LCD orange
ACTIVE     = (255, 200, 100)  # lit amber for indicator notches

# ---------------------------------------------------------------------------
# Helpers

def gradient_bg(im: Image.Image, top, bot):
    """Vertical linear gradient."""
    px = im.load()
    h = im.height
    for y in range(h):
        t = y / (h - 1)
        r = int(top[0] + (bot[0] - top[0]) * t)
        g = int(top[1] + (bot[1] - top[1]) * t)
        b = int(top[2] + (bot[2] - top[2]) * t)
        for x in range(im.width):
            px[x, y] = (r, g, b)


def scanlines(draw, im, color, step=3):
    for y in range(0, im.height, step):
        draw.line([(0, y), (im.width, y)], fill=color, width=1)


def corner_brackets(draw, im, color, length=46, weight=2, margin=14):
    w, h = im.width, im.height
    for cx, cy, dx, dy in [
        (margin,    margin,    +1, +1),  # TL
        (w-margin,  margin,    -1, +1),  # TR
        (margin,    h-margin,  +1, -1),  # BL
        (w-margin,  h-margin,  -1, -1),  # BR
    ]:
        draw.line([(cx, cy), (cx + dx * length, cy)], fill=color, width=weight)
        draw.line([(cx, cy), (cx, cy + dy * length)], fill=color, width=weight)


def load_font(name, size):
    return ImageFont.truetype(f"C:/Windows/Fonts/{name}", size)


def tint(sprite_rgba, color):
    """Tint an SE sprite (white on premultiplied alpha) with `color` (r,g,b).

    Replaces RGB with the solid tint and reuses the sprite's alpha — produces
    the same composite result as SE's premultiplied draw when paired with
    standard alpha_composite.
    """
    out = Image.new("RGBA", sprite_rgba.size, color + (0,))
    out.putalpha(sprite_rgba.split()[3])
    return out


def paste_sprite(target, sprite, cx, cy, size, rotation_deg=0, color=(255, 255, 255)):
    """Tint, resize, rotate, and alpha-composite a sprite onto target.

    `cx, cy` aligns the sprite's CONTENT bounding box centre (not the sprite
    box centre) with the target point. ArrowSingle's chevron sits ~2px off-
    centre in its 64×64 box; using the bbox keeps a rotated > and < pair
    properly straddling their rail instead of inheriting that asymmetry.
    """
    s = tint(sprite, color)
    if size != s.size[0]:
        s = s.resize((size, size), Image.LANCZOS)
    if rotation_deg:
        s = s.rotate(rotation_deg, resample=Image.BICUBIC, expand=True)
    bbox = s.split()[3].getbbox()
    if bbox is None:
        ccx, ccy = s.size[0] / 2, s.size[1] / 2
    else:
        ccx = (bbox[0] + bbox[2]) / 2
        ccy = (bbox[1] + bbox[3]) / 2
    x = int(round(cx - ccx))
    y = int(round(cy - ccy))
    target.alpha_composite(s, (x, y))


def paste_ellipse(target, sprite, cx, cy, w, h, color=(255, 255, 255)):
    """Stretch the (square) CircleHollow sprite into an ellipse."""
    s = tint(sprite, color)
    s = s.resize((w, h), Image.LANCZOS)
    x = int(round(cx - w / 2))
    y = int(round(cy - h / 2))
    target.alpha_composite(s, (x, y))


# ---------------------------------------------------------------------------
# Compose

circle_hollow = Image.open(CIRCLE_HOLLOW_DDS).convert("RGBA")
arrow_single  = Image.open(ARROW_SINGLE_DDS).convert("RGBA")

base = Image.new("RGBA", (W, H), (0, 0, 0, 255))
gradient_bg(base, BG_TOP, BG_BOT)
bdraw = ImageDraw.Draw(base)
scanlines(bdraw, base, SCANLINE, step=3)
corner_brackets(bdraw, base, BRACKET, length=52, weight=2, margin=14)

# Indicator block (left-center)
cx, cy = 178, 225
R = 128                 # reticle pixel radius (half the sprite edge)
RAIL_W = 3              # rail thickness; SE uses ~2% of reticle radius
CHEV_PX = 28            # chevron sprite size on the screen

# Cross rails — SE draws these as SquareSimple rectangles tinted by railColor.
# We just draw rectangles directly (same visual result, no sprite needed).
bdraw.rectangle([cx - R, cy - RAIL_W // 2, cx + R, cy + RAIL_W // 2 + (RAIL_W % 2)],
                fill=CHROME)
bdraw.rectangle([cx - RAIL_W // 2, cy - R, cx + RAIL_W // 2 + (RAIL_W % 2), cy + R],
                fill=CHROME)

# Reticle — the actual CircleHollow sprite, tinted chrome (matching the LCD app
# now that DrawReticle uses _palette.Body).
paste_ellipse(base, circle_hollow, cx, cy, R * 2, R * 2, color=CHROME)

# Active alignment in progress.
pitch_off_y = -28       # pitch notch above centre
yaw_off_x   = +26       # yaw notch right of centre
gap = 12                # rail-to-chevron-centre — tightened

# Sprite-based chevrons. ArrowSingle is a ">" pointing right by default; the
# LCD app rotates by (angle ± π/2) for placement on the cross / rim. PIL's
# rotate uses CCW degrees, opposite of the LCD code's CW math convention —
# negate to match.

# Pitch notch pair (> at the left of rail pointing right; < at right pointing left)
paste_sprite(base, arrow_single, cx - gap, cy + pitch_off_y, CHEV_PX, 0,   ACTIVE)
paste_sprite(base, arrow_single, cx + gap, cy + pitch_off_y, CHEV_PX, 180, ACTIVE)

# Yaw notch pair (v above rail pointing down; ^ below pointing up)
paste_sprite(base, arrow_single, cx + yaw_off_x, cy - gap, CHEV_PX, -90, ACTIVE)
paste_sprite(base, arrow_single, cx + yaw_off_x, cy + gap, CHEV_PX,  90, ACTIVE)

# Roll chevron pair on the rim — slight CW from top. Both pieces shifted
# radially inward by half a chevron height so the pair sits tucked just
# under the rim.
ROLL_CHEV = CHEV_PX - 4
roll_a = math.radians(34)
rdx, rdy = math.sin(roll_a), -math.cos(roll_a)
shift = ROLL_CHEV // 4
in_x,  in_y  = cx + rdx * (R - 14 - shift), cy + rdy * (R - 14 - shift)
out_x, out_y = cx + rdx * (R + 14 - shift), cy + rdy * (R + 14 - shift)
ang_deg = math.degrees(roll_a)
paste_sprite(base, arrow_single, in_x,  in_y,  ROLL_CHEV, 90 - ang_deg,  ACTIVE)
paste_sprite(base, arrow_single, out_x, out_y, ROLL_CHEV, -90 - ang_deg, ACTIVE)

# Projected target ring — actual circle (not foreshortened), tint ACCENT.
tr_cx, tr_cy = cx + 22, cy - 14
TR_D = 150
paste_ellipse(base, circle_hollow, tr_cx, tr_cy, TR_D, TR_D, color=ACCENT)

# ---------------------------------------------------------------------------
# Title block (right side)

title = load_font("impact.ttf", 90)
sub   = load_font("consola.ttf", 15)

# Title position — pulled left by one capital-M of the title font for breathing
# room on the right margin.
m_bbox = bdraw.textbbox((0, 0), "M", font=title)
m_w = m_bbox[2] - m_bbox[0]
title_x = 392 - m_w + 12
title_y = 118

bdraw.text((title_x, title_y),       "DOCKING", font=title, fill=FOREGROUND)
bdraw.text((title_x, title_y + 84),  "AID",     font=title, fill=ACCENT)

bdraw.text((title_x, title_y + 84 + 96),
           "CONNECTOR ALIGNMENT LCD APP",
           font=sub, fill=CHROME)

base.convert("RGB").save(OUT, "PNG")
print(f"wrote {OUT}  ({W}x{H})")
