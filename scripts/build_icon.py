"""
KroModIx App-Icon-Generator.

Design: Ein stilisiertes Puzzleteil (Plugin-Metapher) in Kroste-Gold (#E0B14C)
auf abgerundetem dunklem Grund (#161C23). Puzzle = zentrale Plugin-Idee des
KroModIx-Apps. Funktioniert auch als 16x16-Favicon.

Erzeugt:
- /home/OsteL/Entwicklung/Org.KroModIx/KroModIx/KroModIx/Assets/kromodix.png (256x256, master)
- /home/OsteL/Entwicklung/Org.KroModIx/KroModIx/KroModIx/Assets/kromodix.ico (Multi-Res 16..256)
"""

from PIL import Image, ImageDraw

OUT_DIR = "/home/OsteL/Entwicklung/Org.KroModIx/KroModIx/KroModIx/Assets"

GOLD    = (224, 177, 76, 255)   # #E0B14C  Kroste-Gold (App-Akzent)
GOLD_D  = (176, 138, 55, 255)   # #B08A37  dunkler Rand
SURFACE = (22, 28, 35, 255)     # #161C23  Grund
BORDER  = (46, 52, 60, 255)     # #2E343C
TRANSP  = (0, 0, 0, 0)

CORNER = 48  # Grundstein-Radius (auf 256)


def make_icon(size: int) -> Image.Image:
    """Baut das Icon in der angegebenen Kantenlaenge."""
    scale = size / 256

    # Hochaufloesend zeichnen und dann downsamplen — sonst wird das Puzzleteil
    # bei kleinen Groessen zu grob (JPEG-Look).
    hi = max(256, size)
    hi_scale = hi / 256

    img = Image.new("RGBA", (hi, hi), TRANSP)
    d = ImageDraw.Draw(img)

    # Grund: abgerundetes Quadrat
    corner = int(CORNER * hi_scale)
    d.rounded_rectangle(
        [(0, 0), (hi - 1, hi - 1)],
        radius=corner,
        fill=SURFACE,
        outline=BORDER,
        width=max(1, int(2 * hi_scale)),
    )

    # --- Puzzleteil in Gold ---------------------------------------------------
    # Zentraler Body: leicht abgerundetes Quadrat, ca. 55% der Icon-Kante.
    body = int(140 * hi_scale)
    cx = hi // 2
    cy = hi // 2
    body_r = int(10 * hi_scale)
    b_left = cx - body // 2
    b_top = cy - body // 2
    b_right = cx + body // 2
    b_bottom = cy + body // 2

    d.rounded_rectangle(
        [(b_left, b_top), (b_right, b_bottom)],
        radius=body_r,
        fill=GOLD,
    )

    # Nase oben (konvex) — Kreis, halb im Body versenkt
    knob = int(48 * hi_scale)
    d.ellipse(
        [(cx - knob // 2, b_top - knob // 2),
         (cx + knob // 2, b_top + knob // 2)],
        fill=GOLD,
    )

    # Nase rechts (konvex)
    d.ellipse(
        [(b_right - knob // 2, cy - knob // 2),
         (b_right + knob // 2, cy + knob // 2)],
        fill=GOLD,
    )

    # Aussparung links (konkav) — Kreis in SURFACE-Farbe
    hole = int(48 * hi_scale)
    d.ellipse(
        [(b_left - hole // 2, cy - hole // 2),
         (b_left + hole // 2, cy + hole // 2)],
        fill=SURFACE,
    )

    # Aussparung unten (konkav)
    d.ellipse(
        [(cx - hole // 2, b_bottom - hole // 2),
         (cx + hole // 2, b_bottom + hole // 2)],
        fill=SURFACE,
    )

    # Feiner Rand am Puzzleteil (Body + Knobs), damit die Silhouette auch
    # klein sauber ist. Wir zeichnen die Umrisse leicht dunkler, um dem
    # Body Kontur zu geben, ohne den Gold-Look zu zerstoeren.
    stroke = max(1, int(2 * hi_scale))
    # Ich zeichne den Body-Umriss NICHT durchlaufend, weil er sonst die
    # Nase-Uebergaenge kaputt macht — nur einen dezenten Schatten simulieren
    # via zweitem Ellipsen-Kreis der Nasen und einer 1px-Kontur des Body.
    d.rounded_rectangle(
        [(b_left, b_top), (b_right, b_bottom)],
        radius=body_r,
        outline=GOLD_D,
        width=stroke,
    )

    # Auf Zielgroesse skalieren (LANCZOS = beste Downsampling-Qualitaet)
    if hi != size:
        img = img.resize((size, size), Image.LANCZOS)

    return img


def main():
    import os
    os.makedirs(OUT_DIR, exist_ok=True)

    # 1) Master-PNG 256x256
    master = make_icon(256)
    master.save(f"{OUT_DIR}/kromodix.png", "PNG")
    print(f"Wrote kromodix.png (256x256) to {OUT_DIR}")

    # 2) Multi-Res ICO fuer Windows-Exe
    sizes = [16, 24, 32, 48, 64, 128, 256]
    icons = [make_icon(s) for s in sizes]
    icons[0].save(
        f"{OUT_DIR}/kromodix.ico",
        format="ICO",
        sizes=[(s, s) for s in sizes],
        append_images=icons[1:],
    )
    print(f"Wrote kromodix.ico (multi-res: {sizes})")


if __name__ == "__main__":
    main()
