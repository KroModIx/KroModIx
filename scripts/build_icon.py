"""
KroModIx App-Icon-Generator (v2 — aufgewertet).

Design: Drei gestapelte, gerundete Cards in Kroste-Gold-Verlauf auf einem
dunklen Grund mit subtiler Vertical-Gradient. Symbolisiert die zentrale
Idee des Apps: uebereinander gelegte Mod-Karten pro Spiel. Ein kleiner
gefuellter Stern oben rechts als „Plugin aktiv"-Akzent.

Erzeugt:
- KroModIx/KroModIx/Assets/kromodix.png (512x512, master)
- KroModIx/KroModIx/Assets/kromodix.ico (Multi-Res 16..256)
"""

import os
from math import cos, sin, pi
from PIL import Image, ImageDraw, ImageFilter

OUT_DIR = "/home/OsteL/Entwicklung/Org.KroModIx/KroModIx/KroModIx/Assets"

# ------- Kroste-Palette -----------------------------------------------------
GOLD_HI    = (245, 205, 110, 255)  # heller Gold-Highlight
GOLD       = (224, 177,  76, 255)  # #E0B14C  Kroste-Gold (App-Akzent)
GOLD_DK    = (176, 138,  55, 255)  # #B08A37  dunkler Rand/Schatten
BG_TOP     = ( 30,  36,  46, 255)  # #1E242E
BG_BOT     = ( 12,  16,  22, 255)  # #0C1016
BORDER     = ( 60,  66,  76, 255)  # #3C424C
CARD_LINE  = ( 68,  50,  20, 255)  # subtiles Detail auf den Cards
TRANSP     = (0, 0, 0, 0)

CORNER_HI = 96  # Base-Radius auf 512


def vertical_gradient(size: int, top: tuple, bot: tuple) -> Image.Image:
    """Vertikaler Gradient — reine PIL-Loesung mit paste je Zeile
    (schneller als per-Pixel-putpixel-Loop)."""
    img = Image.new("RGBA", (size, size), TRANSP)
    line = Image.new("RGBA", (size, 1), TRANSP)
    for y in range(size):
        t = y / max(1, size - 1)
        r = int(top[0] + (bot[0] - top[0]) * t)
        g = int(top[1] + (bot[1] - top[1]) * t)
        b = int(top[2] + (bot[2] - top[2]) * t)
        line.paste((r, g, b, 255), (0, 0, size, 1))
        img.paste(line, (0, y))
    return img


def rounded_mask(size: int, radius: int) -> Image.Image:
    m = Image.new("L", (size, size), 0)
    ImageDraw.Draw(m).rounded_rectangle(
        [(0, 0), (size - 1, size - 1)], radius=radius, fill=255)
    return m


def draw_card(draw: ImageDraw.ImageDraw, box, fill_top, fill_bot, radius):
    """Card mit Faux-Gradient (2 gestackte rounded rects mit unterschiedlicher
    Farbe geben schon einen tuechtigen Gold-Verlauf-Effekt) + duenne Innenlinie."""
    x1, y1, x2, y2 = box
    draw.rounded_rectangle([x1, y1, x2, y2], radius=radius, fill=fill_bot,
                           outline=GOLD_DK, width=3)
    mid = y1 + (y2 - y1) // 2
    draw.rounded_rectangle([x1 + 2, y1 + 2, x2 - 2, mid],
                           radius=radius - 2, fill=fill_top)
    draw.line([(x1 + 20, y2 - 20), (x2 - 20, y2 - 20)],
              fill=CARD_LINE, width=3)


def draw_star(draw: ImageDraw.ImageDraw, cx: int, cy: int, r: int, fill):
    pts = []
    for i in range(10):
        angle = -pi / 2 + i * pi / 5
        rr = r if i % 2 == 0 else r * 0.42
        pts.append((cx + rr * cos(angle), cy + rr * sin(angle)))
    draw.polygon(pts, fill=fill, outline=GOLD_DK)


def make_icon(size: int) -> Image.Image:
    """Rendert intern auf 512 und resampled — kleine Groessen bleiben scharf."""
    hi = 512

    # (1) Base
    grad = vertical_gradient(hi, BG_TOP, BG_BOT)
    mask = rounded_mask(hi, CORNER_HI)
    base = Image.new("RGBA", (hi, hi), TRANSP)
    base.paste(grad, (0, 0), mask)

    border_layer = Image.new("RGBA", (hi, hi), TRANSP)
    ImageDraw.Draw(border_layer).rounded_rectangle(
        [(0, 0), (hi - 1, hi - 1)], radius=CORNER_HI, outline=BORDER, width=6)
    base.alpha_composite(border_layer)

    # (2) Drei gestapelte Cards — versetzt fuer Stapel-Look
    cards_layer = Image.new("RGBA", (hi, hi), TRANSP)
    dc = ImageDraw.Draw(cards_layer)
    card_r = 26
    draw_card(dc, (110, 195, 400, 445), GOLD, GOLD_DK, card_r)  # unten/hinten
    draw_card(dc, (140, 155, 372, 405), GOLD_HI, GOLD, card_r)  # mittig
    draw_card(dc, (95, 105, 355, 355), GOLD_HI, GOLD, card_r)   # vorne/oben

    # (3) Sanfter Schatten unter dem Stapel
    shadow = Image.new("RGBA", (hi, hi), TRANSP)
    ImageDraw.Draw(shadow).ellipse([(90, 435), (410, 465)], fill=(0, 0, 0, 90))
    shadow = shadow.filter(ImageFilter.GaussianBlur(radius=8))
    base.alpha_composite(shadow)
    base.alpha_composite(cards_layer)

    # (4) Stern oben rechts
    star_layer = Image.new("RGBA", (hi, hi), TRANSP)
    draw_star(ImageDraw.Draw(star_layer), cx=420, cy=100, r=50, fill=GOLD_HI)
    base.alpha_composite(star_layer)

    if size != hi:
        base = base.resize((size, size), Image.LANCZOS)
    return base


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    master = make_icon(512)
    master_path = os.path.join(OUT_DIR, "kromodix.png")
    master.save(master_path, "PNG", optimize=True)
    print(f"wrote {master_path} 512x512")

    ico_sizes = [16, 24, 32, 48, 64, 128, 256]
    ico_images = [make_icon(s) for s in ico_sizes]
    ico_path = os.path.join(OUT_DIR, "kromodix.ico")
    ico_images[-1].save(ico_path, format="ICO",
                        sizes=[(s, s) for s in ico_sizes])
    print(f"wrote {ico_path} {ico_sizes}")


if __name__ == "__main__":
    main()
