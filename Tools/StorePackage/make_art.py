"""Store art for Jelly Survival: app icon, Store logos (poster, box art, tile) and hero image.

    python -I Tools/StorePackage/make_art.py              (renders characters with Blender the first time)
    python -I Tools/StorePackage/make_art.py --render     (render again)
    python -I Tools/StorePackage/make_art.py --icon-only  (only the app icon)

Characters and the knife are rendered from the game's own Blender models at a higher pixel density
(render_store_art.py -> artifacts/store/render/), the background is the game's map art (Art/sprites/map*/),
and the title is the in-game logo (Client/Assets/Scripts/UI/TitleLogo.cs) redrawn here rectangle by rectangle.

Writes:
  Art/store/icon/app_icon_1024.png, app_icon_small.png, icon_sizes_preview.png
  Client/Assets/Icon/AppIcon.png                       Unity window / exe icon
  Art/store/logos/poster_2x3_1440x2160.png, poster_2x3_720x1080.png      (2:3 poster art, title in top 2/3)
  Art/store/logos/boxart_1x1_2160.png, boxart_1x1_1080.png               (1:1 box art, title in top 2/3)
  Art/store/logos/tile_1x1_300.png                                       (1:1 app tile icon)
  Art/store/logos/hero_16x9_3840x2160.png, hero_16x9_1920x1080.png       (16:9 super hero art, no text)
  Art/store/logos/title_logo_1600.png                                    (the title alone, transparent)
Sizes follow https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images
"""
import math
import os
import shutil
import subprocess
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
RENDER = os.path.join(ROOT, "artifacts", "store", "render")
ICON_DIR = os.path.join(ROOT, "Art", "store", "icon")
LOGO_DIR = os.path.join(ROOT, "Art", "store", "logos")
BLENDER = os.environ.get("BLENDER", r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")

INK = (26, 24, 30, 255)          # logo outline #1A181E
TILE = (42, 34, 58, 255)         # UI panel ink
TILE_EDGE = (84, 70, 110, 255)
WARN = (255, 64, 51, 255)        # hazard red, warnings only
PAPER = (255, 247, 232, 255)


# ------------------------------------------------------------------ renders

def ensure_renders(force=False):
    if not force and os.path.exists(os.path.join(RENDER, "knife.png")) and \
            os.path.exists(os.path.join(RENDER, "pudding_8_victory2.png")):
        return
    script = os.path.join(HERE, "render_store_art.py")
    for kind in ("jelly", "bear", "pudding"):
        subprocess.run([BLENDER, "-b", "--factory-startup", "--python", script, "--", "chars", kind, RENDER, "768"],
                       check=True, stdout=subprocess.DEVNULL)
    subprocess.run([BLENDER, "-b", os.path.join(ROOT, "Art", "blender", "knife.blend"), "--python", script, "--",
                    "knife", RENDER, "8"], check=True, stdout=subprocess.DEVNULL)


def sprite(name, height=None, flip=False):
    im = Image.open(os.path.join(RENDER, name + ".png")).convert("RGBA")
    im = im.crop(im.getbbox())
    if flip:
        im = im.transpose(Image.FLIP_LEFT_RIGHT)
    if height:
        im = im.resize((max(1, round(im.width * height / im.height)), height), Image.LANCZOS)
    return im


def put(canvas, im, cx, bottom):
    """Paste with the bottom centre at (cx, bottom)."""
    canvas.alpha_composite(im, (round(cx - im.width / 2), round(bottom - im.height)))


# ------------------------------------------------------------------ title logo (port of TitleLogo.cs)

LOGO_INKS = {"J": (226, 71, 90), "E": (63, 111, 230), "L1": (77, 195, 90), "L2": (240, 201, 58), "Y": (154, 93, 219),
             "Paper": PAPER[:3], "Warn": WARN[:3]}
OUTLINE = 0.7
BIG, BIG_GAP = 1.35, 1.6
SMALL, SMALL_GAP = 0.78, 1.6
TAG_PAD, LINE_GAP = 2.2, 2.6


class Pen:
    def __init__(self, parts, ox, oy, s, ink):
        self.parts, self.ox, self.oy, self.s, self.ink = parts, ox, oy, s, ink

    def R(self, x, y, w, h, angle=0.0):
        s = self.s
        self.parts.append((self.ox + x * s, self.oy + y * s, w * s, h * s, angle, self.ink))

    def Seg(self, x1, y1, x2, y2, t):
        ln = math.hypot(x2 - x1, y2 - y1)
        cx, cy = (x1 + x2) / 2, (y1 + y2) / 2
        self.R(cx - ln / 2, cy - t / 2, ln, t, math.degrees(math.atan2(y2 - y1, x2 - x1)))


def gJ(p): p.R(2, 0, 7, 3); p.R(6, 0, 3, 13); p.R(0, 10, 9, 3); p.R(0, 7, 3, 6)
def gE(p): p.R(0, 0, 3, 13); p.R(0, 0, 8, 3); p.R(0, 5, 7, 3); p.R(0, 10, 8, 3)
def gL(p): p.R(0, 0, 3, 13); p.R(0, 10, 8, 3)
def gY(p): p.Seg(1.5, 1, 5.5, 7, 3); p.Seg(9.5, 1, 5.5, 7, 3); p.R(4, 6, 3, 7)
def gS(p): p.R(0, 0, 8, 2); p.R(0, 0, 2, 7); p.R(0, 5, 8, 2); p.R(6, 5, 2, 7); p.R(0, 10, 8, 2)
def gU(p): p.R(0, 0, 2, 12); p.R(6, 0, 2, 12); p.R(0, 10, 8, 2)
def gR(p): p.R(0, 0, 2, 12); p.R(0, 0, 8, 2); p.R(6, 0, 2, 7); p.R(0, 5, 8, 2); p.Seg(4.2, 6.2, 7.2, 11.4, 2)
def gV(p): p.Seg(1, 0.5, 5, 11.5, 2); p.Seg(9, 0.5, 5, 11.5, 2)
def gI(p): p.R(0, 0, 2, 12)
def gA(p): p.R(0, 1, 2, 11); p.R(7, 1, 2, 11); p.R(1, 0, 7, 2); p.R(0, 6, 9, 2)
def gL2(p): p.R(0, 0, 2, 12); p.R(0, 10, 7, 2)


def logo_parts():
    parts = []
    jelly = [(gJ, 9, "J"), (gE, 8, "E"), (gL, 8, "L1"), (gL, 8, "L2"), (gY, 11, "Y")]
    surv = [(gS, 8), (gU, 8), (gR, 8), (gV, 10), (gI, 2), (gV, 10), (gA, 9), (gL2, 7)]
    jelly_w = (sum(w for _, w, _ in jelly) + BIG_GAP * 4) * BIG
    tag_w = (sum(w for _, w in surv) + SMALL_GAP * 7) * SMALL + 2 * TAG_PAD
    total = max(jelly_w, tag_w)
    x = (total - jelly_w) / 2
    for g, w, ink in jelly:
        g(Pen(parts, x, 0, BIG, ink))
        x += (w + BIG_GAP) * BIG
    tag_y = 13 * BIG + LINE_GAP
    x = (total - tag_w) / 2
    parts.append((x, tag_y, tag_w, 12 * SMALL + 2 * TAG_PAD, 0.0, "Warn"))
    x += TAG_PAD
    for g, w in surv:
        g(Pen(parts, x, tag_y + TAG_PAD, SMALL, "Paper"))
        x += (w + SMALL_GAP) * SMALL
    return parts


def rect_poly(x, y, w, h, angle, grow, ox, oy, ppu):
    cx, cy = x + w / 2, y + h / 2
    hw, hh = w / 2 + grow, h / 2 + grow
    a = math.radians(angle)
    ca, sa = math.cos(a), math.sin(a)
    return [((cx + dx * ca - dy * sa) * ppu + ox, (cy + dx * sa + dy * ca) * ppu + oy)
            for dx, dy in ((-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh))]


def draw_logo(width_px):
    """The title logo as an RGBA image width_px wide (transparent around it)."""
    parts = logo_parts()
    xs, ys = [], []
    for x, y, w, h, a, _ in parts:
        for px, py in rect_poly(x, y, w, h, a, OUTLINE, 0, 0, 1):
            xs.append(px)
            ys.append(py)
    minx, miny, maxx, maxy = min(xs), min(ys), max(xs), max(ys)
    ppu = width_px / (maxx - minx)
    ss = 4                                  # supersample for clean edges
    W, H = round(width_px * ss), round((maxy - miny) * ppu * ss)
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    ox, oy = -minx * ppu * ss, -miny * ppu * ss
    for x, y, w, h, a, _ in parts:
        d.polygon(rect_poly(x, y, w, h, a, OUTLINE, ox, oy, ppu * ss), fill=INK)
    for x, y, w, h, a, ink in parts:
        d.polygon(rect_poly(x, y, w, h, a, 0, ox, oy, ppu * ss), fill=LOGO_INKS[ink] + (255,))
    return img.resize((round(width_px), round(H / ss)), Image.LANCZOS)


# ------------------------------------------------------------------ shared pieces

def warning(d, cx, cy, r):
    """The in-game warning diamond with an exclamation mark."""
    o = max(2, r * 0.22)
    d.polygon([(cx, cy - r - o), (cx + r + o, cy), (cx, cy + r + o), (cx - r - o, cy)], fill=INK)
    d.polygon([(cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy)], fill=WARN)
    t = r * 0.28
    d.rectangle((cx - t / 2, cy - r * 0.62, cx + t / 2, cy + r * 0.18), fill=PAPER)
    d.rectangle((cx - t / 2, cy + r * 0.34, cx + t / 2, cy + r * 0.62), fill=PAPER)


def warn_column(canvas, cx, top, bottom, w, alpha=55):
    col = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(col).rectangle((cx - w / 2, top, cx + w / 2, bottom), fill=WARN[:3] + (alpha,))
    canvas.alpha_composite(col)


def knife(canvas, cx, tip_y, height, angle=0.0):
    k = sprite("knife", round(height))
    if angle:
        k = k.rotate(angle, resample=Image.BICUBIC, expand=True)
    canvas.alpha_composite(k, (round(cx - k.width / 2), round(tip_y - k.height)))


def background(name, w, h, focus_x=0.5, focus_y=0.5, dim=0.82, scale=1.0):
    src = Image.open(os.path.join(ROOT, "Art", "sprites", name)).convert("RGBA")
    s = max(w / src.width, h / src.height) * scale
    big = src.resize((round(src.width * s), round(src.height * s)), Image.LANCZOS)
    x0 = min(max(0, round(big.width * focus_x - w / 2)), big.width - w)
    y0 = min(max(0, round(big.height * focus_y - h / 2)), big.height - h)
    bg = big.crop((x0, y0, x0 + w, y0 + h))
    bg.alpha_composite(Image.new("RGBA", bg.size, (14, 12, 22, round(255 * (1 - dim)))))
    return bg, s, x0, y0


# ------------------------------------------------------------------ icon

S = 1024


def full_icon():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    m = 36
    d.rounded_rectangle((m, m, S - m, S - m), radius=200, fill=INK)
    d.rounded_rectangle((m + 22, m + 22, S - m - 22, S - m - 22), radius=180, fill=TILE_EDGE)
    d.rounded_rectangle((m + 40, m + 34, S - m - 40, S - m - 56), radius=162, fill=TILE)
    knife(img, 745, 660, 400, angle=-8)
    put(img, sprite("jelly_1_idle0", 640), 420, 905)
    warning(ImageDraw.Draw(img), 745, 170, 80)
    return img


def small_icon():
    j = sprite("jelly_1_idle0")
    side = max(j.width, j.height)
    sq = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    sq.alpha_composite(j, ((side - j.width) // 2, side - j.height))
    return sq.resize((S, S), Image.LANCZOS)


def make_icons():
    os.makedirs(ICON_DIR, exist_ok=True)
    full, small = full_icon(), small_icon()
    full.save(os.path.join(ICON_DIR, "app_icon_1024.png"))
    small.save(os.path.join(ICON_DIR, "app_icon_small.png"))
    unity_dir = os.path.join(ROOT, "Client", "Assets", "Icon")
    os.makedirs(unity_dir, exist_ok=True)
    shutil.copyfile(os.path.join(ICON_DIR, "app_icon_1024.png"), os.path.join(unity_dir, "AppIcon.png"))
    sizes = [16, 20, 24, 32, 48, 64, 128, 256]
    w = sum(sizes) + 20 * len(sizes) + 20
    prev = Image.new("RGBA", (w, 600), (240, 240, 240, 255))
    ImageDraw.Draw(prev).rectangle((0, 300, w, 600), fill=(32, 32, 32, 255))
    x = 20
    for s in sizes:
        ic = (small if s <= 32 else full).resize((s, s), Image.LANCZOS)
        prev.alpha_composite(ic, (x, 20))
        prev.alpha_composite(ic, (x, 320))
        x += s + 20
    prev.save(os.path.join(ICON_DIR, "icon_sizes_preview.png"))
    return full, small


# ------------------------------------------------------------------ store logos

def cast(canvas, unit, items):
    """items: (render name, centre x, bottom y, height in units, flip). unit = px per character height."""
    for name, cx, bottom, hu, flip in items:
        put(canvas, sprite(name, round(hu * unit), flip), cx, bottom)


def falling(canvas, unit, cx, tip_y, ground_y, length=1.5, angle=0.0):
    warn_column(canvas, cx, 0, ground_y, unit * 0.5)
    knife(canvas, cx, tip_y, unit * length, angle)


def poster(w=1440, h=2160):
    c, *_ = background("map/kitchen_full.png", w, h, focus_x=0.5, focus_y=0.5, dim=0.78)
    u = w / 4.4
    logo = draw_logo(w * 0.86)
    top = h * 0.05
    falling(c, u, w * 0.34, h * 0.565, h * 0.66, length=1.15)
    falling(c, u, w * 0.69, h * 0.53, h * 0.66, length=1.15)
    cast(c, u, [("bear_2_jump1", w * 0.16, h * 0.62, 1.0, False),
                ("jelly_1_jump1", w * 0.52, h * 0.665, 1.0, False),
                ("pudding_4_fall1", w * 0.85, h * 0.60, 0.92, True)])
    c.alpha_composite(logo, (round((w - logo.width) / 2), round(top)))
    return c


def box_art(w=2160, h=2160):
    c, *_ = background("map/kitchen_full.png", w, h, focus_x=0.5, focus_y=0.45, dim=0.78)
    u = w / 5.4
    logo = draw_logo(w * 0.70)
    falling(c, u, w * 0.35, h * 0.615, h * 0.665, length=0.95)
    falling(c, u, w * 0.67, h * 0.60, h * 0.665, length=0.95)
    cast(c, u, [("bear_2_jump1", w * 0.18, h * 0.63, 1.0, False),
                ("jelly_1_jump1", w * 0.51, h * 0.665, 1.0, False),
                ("pudding_4_fall1", w * 0.84, h * 0.61, 0.92, True)])
    c.alpha_composite(logo, (round((w - logo.width) / 2), round(h * 0.05)))
    return c


def hero(w=3840, h=2160):
    c, s, x0, y0 = background("map/kitchen_full.png", w, h, focus_x=0.5, focus_y=0.40, dim=0.86)

    def at(mx, my):                          # map art pixel -> hero pixel
        return mx * s - x0, my * s - y0

    u = h / 5.2
    board_x, board_y = at(935, 662)
    rack_x, rack_y = at(288, 492)
    hood_x, hood_y = at(1422, 492)
    low_x, low_y = at(1116, 838)
    falling(c, u, board_x - u * 0.95, board_y - u * 0.5, board_y)
    falling(c, u, rack_x + u * 0.05, rack_y - u * 1.05, rack_y)
    falling(c, u, hood_x - u * 1.05, h * 0.30, hood_y)
    cast(c, u, [("jelly_1_jump1", board_x + u * 0.3, board_y - u * 0.3, 1.0, False),
                ("bear_2_jump1", w * 0.25, h * 0.36, 0.95, False),
                ("pudding_4_idle0", rack_x, rack_y + 4, 0.9, False),
                ("jelly_7_run2", low_x, low_y + 4, 0.9, True),
                ("bear_5_fall1", hood_x - u * 1.0, h * 0.62, 0.95, True),
                ("pudding_8_victory2", hood_x + u * 0.35, hood_y + 4, 0.9, True)])
    return c


def make_logos(full):
    os.makedirs(LOGO_DIR, exist_ok=True)
    p = poster()
    p.convert("RGB").save(os.path.join(LOGO_DIR, "poster_2x3_1440x2160.png"))
    p.resize((720, 1080), Image.LANCZOS).convert("RGB").save(os.path.join(LOGO_DIR, "poster_2x3_720x1080.png"))
    b = box_art()
    b.convert("RGB").save(os.path.join(LOGO_DIR, "boxart_1x1_2160.png"))
    b.resize((1080, 1080), Image.LANCZOS).convert("RGB").save(os.path.join(LOGO_DIR, "boxart_1x1_1080.png"))
    full.resize((300, 300), Image.LANCZOS).save(os.path.join(LOGO_DIR, "tile_1x1_300.png"))
    hh = hero()
    hh.convert("RGB").save(os.path.join(LOGO_DIR, "hero_16x9_3840x2160.png"))
    hh.resize((1920, 1080), Image.LANCZOS).convert("RGB").save(os.path.join(LOGO_DIR, "hero_16x9_1920x1080.png"))
    draw_logo(1600).save(os.path.join(LOGO_DIR, "title_logo_1600.png"))


def main():
    ensure_renders("--render" in sys.argv)
    full, _ = make_icons()
    if "--icon-only" not in sys.argv:
        make_logos(full)
    print("store art written to", ICON_DIR, "and", LOGO_DIR)


if __name__ == "__main__":
    main()
