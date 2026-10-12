"""
Generates TimeLock's brand assets from code.

There is no hand-drawn artwork in this repository. Everything in assets/ and the
logo used in the README is produced by this script, so the mark can be
re-rendered at any size or tweaked in one place.

    py -3 tools/make_logo.py

Outputs
    assets/timelock.png    512x512 app icon (transparent corner radius)
    assets/timelock.ico    multi-resolution icon for the executables
    assets/icon-sizes.png  legibility strip at 16/24/32/48
    docs/logo-light-bg.png horizontal lockup, dark text (GitHub light theme)
    docs/logo-dark-bg.png  horizontal lockup, light text (GitHub dark theme)
    docs/favicon.png       64x64, small enough to use inline

Design notes
    The palette is lifted straight from the lock screen in Client.cs so the
    icon and the app look like the same product: deep navy tile, cyan accent.

    The mark is a deliberately plain padlock. A timer or progress ring inside
    the body was tried and dropped -- it turns to mush below about 32px, and the
    whole point is that this has to read in a taskbar. The cyan-on-dark
    treatment is what gives it identity.
"""

import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

# --------------------------------------------------------------------------
# Config
# --------------------------------------------------------------------------
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ASSETS = os.path.join(ROOT, "assets")
DOCS = os.path.join(ROOT, "docs")

SS = 4  # supersample factor; Pillow does not antialias shapes

PALETTE = {
    "bg_top":        (26, 30, 42),    # #1A1E2A
    "bg_bottom":     (11, 12, 17),    # #0B0C11
    "accent":        (94, 200, 255),  # #5EC8FF
    "accent_shackle": (72, 176, 228), # a shade darker than the body, for depth
    "keyhole":       (9, 10, 14),
}

FONT_CANDIDATES_BOLD = [
    r"C:\Windows\Fonts\segoeuib.ttf",
    r"C:\Windows\Fonts\segoeui.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
    "/System/Library/Fonts/Helvetica.ttc",
]
FONT_CANDIDATES_REG = [
    r"C:\Windows\Fonts\segoeui.ttf",
    "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    "/System/Library/Fonts/Helvetica.ttc",
]


def load_font(candidates, size):
    for path in candidates:
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except OSError:
                continue
    return None  # Pillow's bitmap default; caller decides if that is acceptable


# --------------------------------------------------------------------------
# Helpers
# --------------------------------------------------------------------------
def vertical_gradient(size, top, bottom):
    """1px-tall gradient stretched to size -- cheap and perfectly smooth."""
    ramp = np.linspace(0.0, 1.0, size, dtype=np.float32)[:, None]
    top = np.array(top, dtype=np.float32)
    bottom = np.array(bottom, dtype=np.float32)
    row = top[None, :] * (1.0 - ramp) + bottom[None, :] * ramp
    strip = np.repeat(row[:, None, :], 1, axis=1)
    strip = np.repeat(strip, size, axis=1)
    return Image.fromarray(strip.astype(np.uint8), "RGB")


def rounded_mask(size, radius, supersample):
    """Alpha mask with rounded corners, drawn supersampled then downscaled."""
    s = size * supersample
    m = Image.new("L", (s, s), 0)
    ImageDraw.Draw(m).rounded_rectangle(
        (0, 0, s - 1, s - 1), radius=radius * supersample, fill=255
    )
    return m.resize((size, size), Image.LANCZOS)


# --------------------------------------------------------------------------
# The mark
# --------------------------------------------------------------------------
def draw_mark(size, supersample=SS):
    """Return an RGBA icon of `size` px with a transparent rounded tile."""
    S = size * supersample
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

    tile_radius = 0.215 * S

    # --- tile: gradient clipped to a rounded square -----------------------
    grad = vertical_gradient(S, PALETTE["bg_top"], PALETTE["bg_bottom"]).convert("RGBA")
    tile_mask = rounded_mask(S, tile_radius, 1)
    img.paste(grad, (0, 0), tile_mask)

    # --- soft accent glow behind the padlock, for a little depth ----------
    glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    gc = PALETTE["accent"]
    gx0, gy0 = 0.5 * S - 0.30 * S, 0.52 * S - 0.30 * S
    gx1, gy1 = 0.5 * S + 0.30 * S, 0.52 * S + 0.30 * S
    gd.ellipse((gx0, gy0, gx1, gy1), fill=(gc[0], gc[1], gc[2], 46))
    glow = glow.filter(ImageFilter.GaussianBlur(radius=0.085 * S))
    glow.putalpha(Image.composite(glow.getchannel("A"), Image.new("L", (S, S), 0), tile_mask))
    img = Image.alpha_composite(img, glow)

    d = ImageDraw.Draw(img)

    # --- faint inner border ----------------------------------------------
    # A hint of a rim light along the top edge. Windows does not draw borders
    # around app icons, so keep this barely there -- a strong cyan outline
    # reads as "this tile is selected" rather than as part of the mark.
    d.rounded_rectangle(
        (int(0.010 * S), int(0.010 * S), int(S - 0.010 * S), int(S - 0.010 * S)),
        radius=int(tile_radius * 0.94),
        outline=(gc[0], gc[1], gc[2], 20),
        width=max(1, int(0.004 * S)),
    )

    cx = 0.5 * S
    accent = PALETTE["accent"]
    shackle_col = PALETTE["accent_shackle"]

    # --- shackle: the upper half of a ring, sitting on the body -----------
    # Everything handed to ImageDraw is cast to int. A float `radius` makes
    # rounded_rectangle silently skip its fill, which looks like the padlock
    # body mysteriously vanishing.
    def R(*vals):
        return tuple(int(round(v)) for v in vals)

    shackle_r = int(0.170 * S)
    shackle_w = int(0.086 * S)
    body_top = int(0.455 * S)

    shackle = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    sd = ImageDraw.Draw(shackle)
    sd.arc(
        R(cx - shackle_r, body_top - shackle_r, cx + shackle_r, body_top + shackle_r),
        start=180, end=360,
        fill=shackle_col + (255,),
        width=shackle_w,
    )
    # round off the flat ends the arc leaves behind
    for dx in (-1, 1):
        ex = cx + dx * shackle_r
        sd.ellipse(
            R(ex - shackle_w / 2, body_top - shackle_w / 2,
              ex + shackle_w / 2, body_top + shackle_w / 2),
            fill=shackle_col + (255,),
        )
    shackle = shackle.filter(ImageFilter.GaussianBlur(radius=max(1, int(0.003 * S))))
    shackle.putalpha(Image.composite(shackle.getchannel("A"), Image.new("L", (S, S), 0), tile_mask))
    img = Image.alpha_composite(img, shackle)

    d = ImageDraw.Draw(img)

    # --- body --------------------------------------------------------------
    bw, bh = int(0.475 * S), int(0.365 * S)
    bx0 = cx - bw // 2
    by0 = body_top - int(0.012 * S)
    d.rounded_rectangle(
        R(bx0, by0, bx0 + bw, by0 + bh),
        radius=int(0.076 * S),
        fill=accent + (255,),
    )
    # top highlight so the body is not a flat slab
    hl = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    hd = ImageDraw.Draw(hl)
    hd.rounded_rectangle(
        R(bx0 + 0.030 * S, by0 + 0.026 * S, bx0 + bw - 0.030 * S, by0 + bh * 0.46),
        radius=int(0.048 * S),
        fill=(255, 255, 255, 34),
    )
    hl = hl.filter(ImageFilter.GaussianBlur(radius=max(1, int(0.012 * S))))
    hl.putalpha(Image.composite(hl.getchannel("A"), Image.new("L", (S, S), 0), tile_mask))
    img = Image.alpha_composite(img, hl)

    d = ImageDraw.Draw(img)

    # --- keyhole -----------------------------------------------------------
    kh_y = by0 + int(0.125 * S)
    kh_r = int(0.052 * S)
    d.ellipse(R(cx - kh_r, kh_y - kh_r, cx + kh_r, kh_y + kh_r),
              fill=PALETTE["keyhole"] + (255,))
    slot_h = int(0.135 * S)
    slot_w = int(0.040 * S)
    d.polygon(
        [
            (int(cx - slot_w / 2), int(kh_y + kh_r * 0.35)),
            (int(cx + slot_w / 2), int(kh_y + kh_r * 0.35)),
            (int(cx + slot_w * 0.95), int(kh_y + slot_h)),
            (int(cx - slot_w * 0.95), int(kh_y + slot_h)),
        ],
        fill=PALETTE["keyhole"] + (255,),
    )

    return img.resize((size, size), Image.LANCZOS)


# --------------------------------------------------------------------------
# Wordmark lockup
# --------------------------------------------------------------------------
# GitHub READMEs render on either a light or a dark theme, so the lockup is
# produced twice: dark text for light backgrounds, light text for dark ones.
# The README picks between them with <picture> + prefers-color-scheme.
LOCKUP_COLORS = {
    "on_light": ((32, 36, 46), (110, 118, 138)),    # wordmark, tagline
    "on_dark":  ((238, 242, 250), (126, 134, 156)),
}


def draw_lockup(on="on_light", height=280):
    """Horizontal icon + wordmark + tagline, transparent background."""
    H = height * 2
    pad = int(H * 0.09)
    icon = draw_mark(int(H * 0.76))

    word = "TimeLock"
    tag = "timed lock for Windows"

    bold = load_font(FONT_CANDIDATES_BOLD, int(H * 0.40))
    reg = load_font(FONT_CANDIDATES_REG, int(H * 0.145))
    if bold is None:
        print("  ! no usable bold font found; wordmark falls back to the bitmap default")
        bold = ImageFont.load_default()
    if reg is None:
        reg = bold

    measure = ImageDraw.Draw(Image.new("RGBA", (8, 8)))
    wb = measure.textbbox((0, 0), word, font=bold)
    tb = measure.textbbox((0, 0), tag, font=reg)
    ww, wh = wb[2] - wb[0], wb[3] - wb[1]
    tw, th = tb[2] - tb[0], tb[3] - tb[1]

    gap = int(H * 0.17)
    line_gap = int(H * 0.07)

    text_w = max(ww, tw)
    block_h = wh + line_gap + th
    W = pad + icon.width + gap + text_w + pad
    Ht = pad + block_h + pad

    canvas = Image.new("RGBA", (W, Ht), (0, 0, 0, 0))
    d = ImageDraw.Draw(canvas)

    icon_y = (Ht - icon.height) // 2
    canvas.alpha_composite(icon, (pad, icon_y))

    tx = pad + icon.width + gap
    ty = (Ht - block_h) // 2

    word_col, tag_col = LOCKUP_COLORS[on]

    # Draw using the bbox offsets so ascenders/descenders are not clipped.
    d.text((tx - wb[0], ty - wb[1]), word, font=bold, fill=word_col + (255,))
    d.text((tx - tb[0], ty + wh + line_gap - tb[1]), tag, font=reg, fill=tag_col + (255,))

    return canvas.resize((max(1, W // 2), max(1, Ht // 2)), Image.LANCZOS)


# --------------------------------------------------------------------------
def main():
    os.makedirs(ASSETS, exist_ok=True)
    os.makedirs(DOCS, exist_ok=True)

    print("Rendering icon...")
    icon_512 = draw_mark(512)
    icon_512.save(os.path.join(ASSETS, "timelock.png"))
    print("  assets/timelock.png   512x512")

    icon_1024 = draw_mark(1024)
    icon_1024.save(
        os.path.join(ASSETS, "timelock.ico"),
        format="ICO",
        sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    print("  assets/timelock.ico   16/24/32/48/64/128/256")

    print("Rendering lockups...")
    for on, filename in (
        ("on_light", "logo-light-bg.png"),    # dark text, for GitHub light theme
        ("on_dark",  "logo-dark-bg.png"),     # light text, for GitHub dark theme
    ):
        lockup = draw_lockup(on)
        lockup.save(os.path.join(DOCS, filename))
        print("  docs/%-20s %dx%d" % (filename, lockup.width, lockup.height))

    fav = draw_mark(64)
    fav.save(os.path.join(DOCS, "favicon.png"))
    print("  docs/favicon.png      64x64")

    # A tiny preview strip is handy for eyeballing small-size legibility.
    strip_w = 16 + 8 + 24 + 8 + 32 + 8 + 48
    strip = Image.new("RGBA", (strip_w, 64), (255, 255, 255, 255))
    x = 0
    for s in (16, 24, 32, 48):
        small = draw_mark(s)
        strip.alpha_composite(small, (x, (64 - s) // 2))
        x += s + 8
    strip.save(os.path.join(ASSETS, "icon-sizes.png"))
    print("  assets/icon-sizes.png (16/24/32/48 legibility check)")

    print("Done.")


if __name__ == "__main__":
    sys.exit(main())
