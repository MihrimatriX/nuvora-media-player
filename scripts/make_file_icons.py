"""Draws the Explorer file-type icons (video, audio, playlist) into src/Player.App/Assets/FileTypes.

Explorer shows these only where it has no thumbnail (an unsupported codec, a list view, small icons), so they look
like documents with a Nuvora badge rather than the app logo itself. Needs Pillow:

    python scripts/make_file_icons.py
"""
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageDraw

SIZES = [256, 128, 64, 48, 40, 32, 24, 20, 16]
OUT = Path(__file__).resolve().parent.parent / "src" / "Player.App" / "Assets" / "FileTypes"
SS = 4  # supersampling factor

# The app icon's gradient (scripts/make_icon.ps1): violet to sky blue.
BADGE_TOP = (139, 92, 246)
BADGE_BOTTOM = (14, 165, 233)


def gradient(size, top, bottom, y0, y1):
    """Vertical gradient from top (at y0) to bottom (at y1)."""
    img = Image.new("RGBA", (size, size))
    px = img.load()
    for y in range(size):
        t = min(1, max(0, (y - y0) / max(1, y1 - y0)))
        c = tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)) + (255,)
        for x in range(size):
            px[x, y] = c
    return img


def page(img, s, small):
    """Document with a folded top-right corner: a dark outline, a light sheet inside it, and the fold."""
    left, right = s * (0.12 if small else 0.17), s * (0.88 if small else 0.83)
    top, bottom = s * 0.05, s * 0.95
    fold = s * (0.26 if small else 0.2)
    stroke = max(SS, round(s * 0.014))

    def sheet(inset):
        m = Image.new("L", (s, s), 0)
        md = ImageDraw.Draw(m)
        md.rounded_rectangle([left + inset, top + inset, right - inset, bottom - inset], radius=s * 0.06, fill=255)
        # Cut the corner off; the fold triangle goes in its place.
        md.polygon([(right - fold + inset * 0.4, top - 1), (s, top - 1), (s, top + fold - inset * 0.4)], fill=0)
        return m

    img.paste((60, 64, 80, 255), (0, 0), sheet(0))
    img.paste((246, 247, 251, 255), (0, 0), sheet(stroke))
    d = ImageDraw.Draw(img)
    d.polygon([(right - fold, top), (right - fold, top + fold), (right, top + fold)], fill=(208, 212, 226, 255))
    d.line([(right - fold, top), (right, top + fold)], fill=(60, 64, 80, 255), width=stroke)
    d.line([(right - fold, top), (right - fold, top + fold), (right, top + fold)], fill=(60, 64, 80, 255), width=stroke)
    return left, right, top, bottom


def glyph(d, kind, cx, cy, g):
    white = (255, 255, 255, 255)
    if kind == "video":
        d.polygon([(cx - g * 0.32, cy - g * 0.42), (cx + g * 0.46, cy), (cx - g * 0.32, cy + g * 0.42)], fill=white)
    elif kind == "audio":
        # Eighth note: head, stem, flag.
        hr = g * 0.2
        hx, hy = cx - g * 0.12, cy + g * 0.28
        d.ellipse([hx - hr * 1.2, hy - hr, hx + hr * 1.2, hy + hr], fill=white)
        sx = hx + hr * 1.05
        d.rectangle([sx - g * 0.07, cy - g * 0.46, sx + g * 0.02, hy], fill=white)
        d.polygon([(sx - g * 0.02, cy - g * 0.46), (sx + g * 0.36, cy - g * 0.2), (sx + g * 0.3, cy - g * 0.06),
                   (sx - g * 0.02, cy - g * 0.26)], fill=white)
    else:
        w = g * 0.11
        for i, y in enumerate([-0.3, 0.0, 0.3]):
            x0 = cx - g * 0.42
            x1 = cx + (g * 0.42 if i < 2 else g * 0.02)
            d.rounded_rectangle([x0, cy + g * y - w / 2, x1, cy + g * y + w / 2], radius=w / 2, fill=white)
        d.polygon([(cx + g * 0.12, cy + g * 0.14), (cx + g * 0.46, cy + g * 0.3), (cx + g * 0.12, cy + g * 0.46)], fill=white)


def draw(kind, size):
    s = size * SS
    small = size <= 24
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    left, right, top, bottom = page(img, s, small)
    d = ImageDraw.Draw(img)

    # Badge in the lower part of the page; larger at small sizes so the glyph stays readable.
    b = s * (0.62 if small else 0.46)
    bx, by = s * 0.5, bottom - b / 2 - s * (0.06 if small else 0.1)
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([bx - b / 2, by - b / 2, bx + b / 2, by + b / 2], radius=b * 0.26, fill=255)
    img.paste(gradient(s, BADGE_TOP, BADGE_BOTTOM, by - b / 2, by + b / 2), (0, 0), mask)
    glyph(d, kind, bx, by, b * 0.62)
    return img.resize((size, size), Image.LANCZOS)


def write_ico(path, images):
    """PNG-compressed ICO (Vista and later), one entry per size."""
    blobs = []
    for img in images:
        buf = BytesIO()
        img.save(buf, format="PNG", optimize=True)
        blobs.append(buf.getvalue())
    header = bytearray()
    header += (0).to_bytes(2, "little") + (1).to_bytes(2, "little") + len(images).to_bytes(2, "little")
    offset = 6 + 16 * len(images)
    entries = bytearray()
    for img, blob in zip(images, blobs):
        w = img.width if img.width < 256 else 0
        entries += bytes([w, w, 0, 0]) + (1).to_bytes(2, "little") + (32).to_bytes(2, "little")
        entries += len(blob).to_bytes(4, "little") + offset.to_bytes(4, "little")
        offset += len(blob)
    path.write_bytes(bytes(header + entries + b"".join(blobs)))


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for kind in ("video", "audio", "playlist"):
        write_ico(OUT / f"{kind}.ico", [draw(kind, size) for size in SIZES])
        print(f"Created: {OUT / (kind + '.ico')}")


if __name__ == "__main__":
    main()
