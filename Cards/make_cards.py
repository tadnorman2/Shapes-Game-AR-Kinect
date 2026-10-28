"""Makes ShapesCards.pdf, the printable Shapes cards, one per page.

The patterns come from Assets/Scripts/Core/CardCodes.cs and the arena names from
Assets/Scripts/Core/ArenaLayout.cs, so the printed cards always match the game.
Needs Python 3 with Pillow: python Cards/make_cards.py
"""
import os
import re

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
CORE = os.path.join(HERE, "..", "Assets", "Scripts", "Core")

DPI = 300
PAGE = (int(8.5 * DPI), int(11 * DPI))  # US Letter; fits A4 too when printed at actual size.
MARKER_CM = 15.0  # The black square's side.
MARGIN_CM = 2.5  # White around it, which the game needs to see the edge.


def cm(value):
    return round(value / 2.54 * DPI)


def codes():
    text = open(os.path.join(CORE, "CardCodes.cs")).read()
    found = re.search(r"Codes\s*=\s*\{([^}]*)\}", text)
    return [int(c, 16) for c in re.findall(r"0x([0-9a-fA-F]+)", found.group(1))]


def arena_names():
    text = open(os.path.join(CORE, "ArenaLayout.cs")).read()
    return re.findall(r'new ArenaLayout\("([^"]+)"', text)


def font(size):
    for name in ("DejaVuSans-Bold.ttf", "Arial Bold.ttf", "arialbd.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default()


def page(number, code, arena):
    image = Image.new("L", PAGE, 255)
    draw = ImageDraw.Draw(image)
    side = cm(MARKER_CM)
    margin = cm(MARGIN_CM)
    left = (PAGE[0] - side) // 2
    top = cm(3.0)
    cell = side / 6

    # A light line to cut along, around the card and its white margin.
    draw.rectangle([left - margin, top - margin, left + side + margin, top + side + margin + cm(2.0)], outline=200, width=4)
    for row in range(6):
        for column in range(6):
            border = row in (0, 5) or column in (0, 5)
            black = border or (code >> ((row - 1) * 4 + column - 1)) & 1
            if black:
                x0 = left + round(column * cell)
                y0 = top + round(row * cell)
                draw.rectangle([x0, y0, left + round((column + 1) * cell) - 1, top + round((row + 1) * cell) - 1], fill=0)

    # The label goes at the card's bottom: the arena's "away" is toward the top.
    label = f"Shapes card {number}: {arena}"
    big = font(cm(0.8))
    width = draw.textlength(label, font=big)
    draw.text(((PAGE[0] - width) / 2, top + side + cm(0.6)), label, fill=0, font=big)

    notes = [
        "Print at actual size (100%, not 'fit to page'). The black square should measure 15 cm.",
        "Matte paper or card, flat on the table, with the white border showing all round.",
        "Cut along the grey line if you like. The arena appears around the card, its far side",
        "toward the card's top.",
    ]
    small = font(cm(0.38))
    y = top + side + margin + cm(3.0)
    for line in notes:
        w = draw.textlength(line, font=small)
        draw.text(((PAGE[0] - w) / 2, y), line, fill=90, font=small)
        y += cm(0.6)
    return image


def main():
    names = arena_names()
    pages = [page(i + 1, code, names[i % len(names)]) for i, code in enumerate(codes()[: len(names)])]
    out = os.path.join(HERE, "ShapesCards.pdf")
    pages[0].save(out, save_all=True, append_images=pages[1:], resolution=DPI)
    print(f"Wrote {len(pages)} cards to {out}")


if __name__ == "__main__":
    main()
