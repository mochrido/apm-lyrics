"""Rasterizes the APM Lyrics logo (assets/logo.svg design) to PNGs and a multi-size ICO.

Run:  python assets/render-logo.py   (from the repo root)
Outputs: src/APMLyrics/Assets/app.ico, src/APMLyrics/Assets/logo-256.png, docs/images/logo-256.png
"""
import os
from PIL import Image, ImageDraw, ImageFont

RED = (250, 45, 72, 255)
WHITE = (255, 255, 255, 255)

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

FONTS = [
    r"C:\Windows\Fonts\segoeuib.ttf",
    r"C:\Windows\Fonts\arialbd.ttf",
    r"C:\Windows\Fonts\segoeui.ttf",
]

def load_font(size):
    for path in FONTS:
        if os.path.exists(path):
            try:
                return ImageFont.truetype(path, size)
            except OSError:
                continue
    return ImageFont.load_default()

def render(size):
    """Draws the logo at `size` px on a supersampled canvas (4x) for clean edges."""
    ss = 4
    big = size * ss
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    # Rounded tile: viewBox 64 -> rx 14, inset 4.
    inset = 4 * big / 64
    radius = 14 * big / 64
    d.rounded_rectangle([inset, inset, big - inset, big - inset], radius=radius, fill=RED)

    # "♪A" centered. The note glyph U+266A is thin at 16px, so at small sizes
    # draw the note as a filled ellipse + stem instead of the codepoint.
    text = "A"
    note_as_shape = size <= 32
    font = load_font(int(34 * big / 64))
    bbox = d.textbbox((0, 0), text + ("\u266a" if not note_as_shape else ""), font=font)
    tw = bbox[2] - bbox[0]
    th = bbox[3] - bbox[1]

    if note_as_shape:
        # Measure "A" alone, then leave room for a drawn note on its left.
        bboxA = d.textbbox((0, 0), text, font=font)
        aw = bboxA[2] - bboxA[0]
        note_w = 0.42 * aw
        gap = 0.10 * aw
        total = note_w + gap + aw
        x = (big - total) / 2 - bboxA[0]
        y = (big - th) / 2 - bbox[1]
        d.text((x + note_w + gap, y), text, font=font, fill=WHITE)
        # Note: ellipse head + stem to its right, sized relative to the A.
        head_r = 0.16 * big
        head_cx = x + note_w - head_r * 0.9
        head_cy = y + th * 0.78
        d.ellipse([head_cx - head_r, head_cy - head_r, head_cx + head_r, head_cy + head_r], fill=WHITE)
        stem_w = max(2, int(0.035 * big))
        stem_h = 0.62 * th
        d.rectangle([head_cx + head_r - stem_w, head_cy - stem_h, head_cx + head_r, head_cy], fill=WHITE)
    else:
        text_full = "\u266a" + text
        bboxF = d.textbbox((0, 0), text_full, font=font)
        fw = bboxF[2] - bboxF[0]
        fh = bboxF[3] - bboxF[1]
        d.text(((big - fw) / 2 - bboxF[0], (big - fh) / 2 - bboxF[1]), text_full, font=font, fill=WHITE)

    return img.resize((size, size), Image.LANCZOS)

def main():
    out_dir = os.path.join(ROOT, "src", "APMLyrics", "Assets")
    docs_dir = os.path.join(ROOT, "docs", "images")
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(docs_dir, exist_ok=True)

    sizes = [16, 32, 48, 256]
    images = {s: render(s) for s in sizes}

    images[256].save(os.path.join(out_dir, "app.ico"),
                     format="ICO", sizes=[(s, s) for s in sizes])
    images[256].save(os.path.join(out_dir, "logo-256.png"))
    images[256].save(os.path.join(docs_dir, "logo-256.png"))
    print("wrote app.ico, logo-256.png")

if __name__ == "__main__":
    main()
