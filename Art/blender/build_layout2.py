"""
New map layouts (docs/work/map-layout.md, map layout session, design commit b3c88ca): pieces, sprites and depth
layers for the second layout of all four maps. Output rules: docs/work/map-visual.md section 6.

Run from the repository root (Blender 5.2):
  blender -b --factory-startup --python Art/blender/build_layout2.py -- layout2
Targets: layout2 (all) | kitchen | winter | warehouse | dessert

Everything goes to Art/sprites/<map folder>/layout2/ with the file names, pixel sizes, world rects and pivots of
map-layout.md section 6 (the files marked "again" there keep their names: kitchen_bg, winter_floor, ...). The
folder is separate because main still runs the first layout until the layout branch is merged; after the merge
the files move up into the map folder. Besides the listed files each map gets the depth layers of build_depth.py
re-placed for the new layout (<map>_far/mid/attach/front.png + layers.json) and a check image
Art/preview/<map>_layout2.png.

Coordinates as everywhere: world X = game x, Z = game y, platform pieces 72 px per unit with the platform top
9 px (0.125 u) below the image top, moving things 128 px per unit around their pivot.
"""
import bpy
import json
import math
import os
import random
import subprocess
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import build_sprites as bs  # noqa: E402
import build_depth as bd  # noqa: E402
from build_sprites import (XZ, BG_RECT, FLOOR_RECT, MAP_PPU, CHAR_PPU, _box, add_prism, add_tube, add_ellipsoid,  # noqa
                           ellipse_pts, flat_mat, toon_mat, wood_mat, tex_wall_mat, load_png, save_png, over,
                           frame_camera, render_to, reset_scene, add_camera, add_sun, MapKit, drip_band, hang_chain)
from build_depth import rect, poly, disc, ring, rrect_pts, lit, move_to, fog, blur, crop, ss_image, _rgba, hex01  # noqa


def delete(names):
    for n in names:
        ob = bpy.data.objects.get(n)
        if ob is None:
            raise KeyError(n)
        bpy.data.objects.remove(ob, do_unlink=True)


def top_line(bm, x0, x1, top, y, mat):
    """Bright platform top line (contrast rule), drawn in front."""
    _box(bm, x0 + 0.02, top - 0.05, x1 - 0.02, top, y - 0.01, y, mat)


# ================================================================ kitchen

def kitchen_bg(kit):
    for n, c in bs.build_map(kit.scene).items():
        kit.cols[n] = c
    delete(['Wall', 'Window', 'Hood', 'Chains', 'Board', 'ShelfPlank', 'ShelfBrackets', 'LidDome', 'LidRim',
            'LidKnob', 'LidEyelets'])
    kit.obj('Wall', 'BG', [tex_wall_mat('Wall', '#2B3142', '#272D3D', '#1C202A', 1.0, 1.0, 0.035, glows=(
        (18.2, 6.2, 4.6, '#5A4836', 0.45), (12.0, 10.1, 4.0, '#2E3D5E', 0.45)))], lambda bm: _box(bm, -4, -4, 28, 16, 6.0, 6.2))
    frame = flat_mat('WindowFrame', '#1E1C24')
    glass = flat_mat('Glass', '#1C2847')
    moon = flat_mat('Moon', '#A8B5D2')
    halo = flat_mat('MoonHalo', '#25355A')
    plant = flat_mat('Plant', '#18241F')
    pot = flat_mat('Pot', '#1A1B21')
    kit.obj('Window', 'BG', [frame, glass, halo, moon, plant, pot], lambda bm: (
        _box(bm, 10.2, 8.2, 13.8, 12.0, 5.0, 5.2, 0), _box(bm, 10.4, 8.4, 13.6, 11.8, 4.8, 5.0, 1),
        add_prism(bm, ellipse_pts(12.85, 11.05, 0.6, 0.6, 32), 4.6, 4.8, XZ, 2),
        add_prism(bm, ellipse_pts(12.85, 11.05, 0.38, 0.38, 32), 4.4, 4.6, XZ, 3),
        _box(bm, 11.94, 8.4, 12.06, 11.8, 4.2, 4.4, 0), _box(bm, 10.4, 10.04, 13.6, 10.16, 4.2, 4.4, 0),
        add_prism(bm, [(10.7, 8.4), (11.3, 8.4), (11.4, 8.9), (10.6, 8.9)], 4.0, 4.2, XZ, 5),
        add_prism(bm, ellipse_pts(10.85, 9.2, 0.28, 0.16, 16), 4.0, 4.2, XZ, 4),
        add_prism(bm, ellipse_pts(11.2, 9.35, 0.3, 0.18, 16), 4.0, 4.2, XZ, 4)))
    duct = flat_mat('Duct', '#22252D')
    duct_lip = flat_mat('DuctLip', '#2C3038')
    kit.obj('Duct', 'BG', [duct, duct_lip], lambda bm: (
        _box(bm, 17.2, 7.0, 19.3, 14.5, 4.2, 4.4, 0),
        [_box(bm, 17.1, z, 19.4, z + 0.12, 4.0, 4.2, 1) for z in (8.6, 10.6, 12.4)]))
    tiles = flat_mat('StoveTile', '#252A36')
    kit.obj('Backsplash', 'BG', [tiles], lambda bm: _box(bm, 9.5, 0.0, 26.0, 0.5, 5.5, 5.6))


def kitchen_mid(kit):
    m = [flat_mat('KMid', '#1B1E26'), flat_mat('KEdge', '#363B48'), flat_mat('KJar', '#232B33'),
         flat_mat('KJarFill', '#3A3330'), flat_mat('KLid', '#3A3F4A'), flat_mat('KFace', '#272B35'),
         flat_mat('KHand', '#4A505C'), flat_mat('KGreen', '#1F2A26')]
    # pot rack above the right burners, right of the duct
    def rack(bm, y, mat, o):
        dx, dz = o
        rect(bm, 19.7 + dx, 12.2 + dz, 23.9 + dx, 12.3 + dz, y, mat)
        for sx in (19.9, 23.7):
            rect(bm, sx - 0.04 + dx, 12.3 + dz, sx + 0.04 + dx, 13.2 + dz, y, mat)
        rect(bm, 20.38 + dx, 11.62 + dz, 20.46 + dx, 12.2 + dz, y, mat)
        disc(bm, 20.42 + dx, 11.22 + dz, 0.42, y, mat)
        rect(bm, 21.31 + dx, 11.72 + dz, 21.39 + dx, 12.2 + dz, y, mat)
        poly(bm, [(21.35 + dx + 0.24 * math.sin(a) * (1 - 0.25 * (math.cos(a) + 1)) * 1.4, 11.36 + dz + 0.38 * math.cos(a))
                  for a in (2 * math.pi * k / 28 for k in range(28))], y, mat)
        rect(bm, 22.21 + dx, 11.32 + dz, 22.29 + dx, 12.2 + dz, y, mat)
        disc(bm, 22.25 + dx, 11.16 + dz, 0.21, y, mat, a0=math.pi, a1=2 * math.pi + 0.001, n=20)
        rect(bm, 22.04 + dx, 11.14 + dz, 22.46 + dx, 11.2 + dz, y, mat)
        poly(bm, [(23.16 + dx, 12.2 + dz), (23.24 + dx, 12.2 + dz), (23.42 + dx, 11.5 + dz), (23.34 + dx, 11.5 + dz)], y, mat)
        poly(bm, rrect_pts(22.8 + dx, 10.95 + dz, 23.65 + dx, 11.52 + dz, 0.06), y, mat)
    kit.obj('PotRack', 'MID', m, lambda bm: (lit(bm, rack, 2.2), poly(bm, [
        (21.35 + 0.15 * math.sin(a) * (1 - 0.25 * (math.cos(a) + 1)) * 1.4, 11.38 + 0.28 * math.cos(a))
        for a in (2 * math.pi * k / 24 for k in range(24))], 2.18, 5)))
    # wall clock above the ladle rail
    kit.obj('Clock', 'MID', m, lambda bm: (
        disc(bm, 7.0, 11.85, 0.44, 2.4, 1), disc(bm, 7.0, 11.85, 0.4, 2.38, 0), disc(bm, 7.0, 11.85, 0.33, 2.36, 5),
        poly(bm, [(6.98, 11.85), (7.02, 11.85), (7.02, 12.1), (6.98, 12.1)], 2.34, 6),
        poly(bm, [(7.0, 11.83), (7.2, 11.76), (7.21, 11.8), (7.01, 11.87)], 2.34, 6)))
    # herb bundle on a wall nail above the tower
    kit.obj('Herbs', 'MID', m, lambda bm: (
        disc(bm, 2.6, 11.82, 0.06, 2.4, 1, n=10), rect(bm, 2.58, 11.2, 2.62, 11.8, 2.38, 0),
        [poly(bm, [(2.6 - 0.08, 11.2), (2.6 + 0.08, 11.2), (2.6 + 0.75 * math.sin(a) + 0.12, 11.2 - 1.3 * math.cos(a)),
                   (2.6 + 0.75 * math.sin(a) - 0.12, 11.2 - 1.3 * math.cos(a))], 2.36 - 0.002 * i, 7 if i % 2 else 0)
         for i, a in enumerate((-0.42, -0.21, 0.0, 0.21, 0.42))],
        rect(bm, 2.45, 11.12, 2.75, 11.26, 2.34, 3)))
    # dish rack (round plates) and cutting board + rolling pin on the right wall
    def dishes(bm, y, mat, o):
        dx, dz = o
        rect(bm, 20.15 + dx, 8.62 + dz, 22.75 + dx, 8.72 + dz, y, mat)
        for k in range(6):
            disc(bm, 20.55 + 0.36 * k + dx, 9.17 + dz, 0.5 - 0.03 * (k % 2), y - 0.004 * k, mat, rz=0.5, n=28)
    kit.obj('Dishes', 'MID', m, lambda bm: (lit(bm, dishes, 2.4),
                                            [ring(bm, 20.55 + 0.36 * k, 9.17, 0.28, 0.31, 2.3 - 0.004 * k, 5, 28)
                                             for k in range(6)]))

    def board(bm, y, mat, o):
        dx, dz = o
        disc(bm, 23.05 + dx, 6.3 + dz, 0.6, y, mat, n=32)
        poly(bm, rrect_pts(22.9 + dx, 6.78 + dz, 23.2 + dx, 7.4 + dz, 0.1), y, mat)
        poly(bm, rrect_pts(21.55 + dx, 5.5 + dz, 21.77 + dx, 7.1 + dz, 0.1), y, mat)
    kit.obj('Board', 'MID', m, lambda bm: (lit(bm, board, 2.4), disc(bm, 23.05, 7.2, 0.07, 2.38, 5, n=12),
                                           ring(bm, 23.05, 6.3, 0.48, 0.51, 2.38, 5)))


def kitchen_pieces(kit):
    line_top = flat_mat('ShelfTop', '#F0BE78')
    # floor: cutting board left of 9.5, stove top with four burner rings and knobs from 9.5
    board = wood_mat('Board', '#7E5030', '#5E3A22', '#43281A', plank=1.7, fade_from=-0.3, fade_to=-1.5)
    board_top = flat_mat('BoardTop', '#E2A866')
    stove = tex_wall_mat('Stove', '#30353F', '#2D323C', '#262A32', 2.0, 40.0, 0.01)
    stove_top = flat_mat('StoveTop', '#C9CDD6')
    iron = flat_mat('BurnerIron', '#17191E')
    ring_lit = flat_mat('BurnerRing', '#4A505C')
    knob = flat_mat('Knob', '#1C1E24')
    knob_lit = flat_mat('KnobLit', '#5A606C')
    kit.obj('Floor', 'FLOOR', [board, board_top, stove, stove_top, iron, ring_lit, knob, knob_lit], lambda bm: (
        _box(bm, -2.0, -2.5, 9.5, 0.0, -1.0, 1.0, 0), _box(bm, -2.0, -0.07, 9.5, 0.0, -1.02, -1.0, 1),
        _box(bm, 9.5, -2.5, 26.0, 0.0, -1.0, 1.0, 2), _box(bm, 9.45, -2.5, 9.55, 0.0, -1.03, -1.02, 4),
        _box(bm, 9.5, -0.06, 26.0, 0.0, -1.03, -1.02, 3),
        [(_box(bm, a + 0.1, -0.16, b - 0.1, -0.06, -1.04, -1.03, 4),
          _box(bm, a + 0.25, -0.16, b - 0.25, -0.12, -1.05, -1.04, 5),
          add_prism(bm, ellipse_pts((a + b) / 2, -0.62, 0.17, 0.17, 20), -1.05, -1.04, XZ, 6),
          _box(bm, (a + b) / 2 - 0.03, -0.62, (a + b) / 2 + 0.03, -0.47, -1.06, -1.05, 7))
         for a, b in ((10.0, 13.0), (13.5, 16.5), (17.0, 20.0), (20.5, 23.5))]))
    # spice tower: three shelves (2.4 / 4.8 / 7.2, x 1..4), thin posts, small tins hanging under each shelf
    plank = wood_mat('Shelf', '#9A653D', '#704626', '#55341E', plank=1.5)
    post = flat_mat('TowerPost', '#30333C')
    tin = [flat_mat('TinA', '#5A4A3A'), flat_mat('TinB', '#4A5A52'), flat_mat('TinC', '#5E4646'), flat_mat('TinLid', '#6A707C')]
    kit.obj('Tower', 'TOWER', [plank, line_top, post], lambda bm: (
        [(_box(bm, 1.0, t - 0.24, 4.0, t, -0.3, 0.3, 0), top_line(bm, 1.0, 4.0, t, -0.3, 1)) for t in (2.4, 4.8, 7.2)],
        [_box(bm, px, 0.0, px + 0.12, 7.0, 0.1, 0.3, 2) for px in (0.85, 4.03)]), outline=0.03)
    rnd = random.Random(11)
    kit.obj('Tins', 'TOWER', tin, lambda bm: [
        (_box(bm, tx - 0.13, t - 0.24 - h, tx + 0.13, t - 0.3, -0.25, -0.15, rnd.randrange(3)),
         _box(bm, tx - 0.14, t - 0.33, tx + 0.14, t - 0.27, -0.26, -0.25, 3))
        for t in (2.4, 4.8, 7.2) for tx, h in ((1.6, 0.42), (2.5, 0.34), (3.35, 0.46))], outline=0.02)
    # ladle rail: iron bar (top 9.6) on two wall mounts with S-hooks
    bar = toon_mat('RailBar', '#5A606C', '#353A44', spec=0.5)
    bar_top = flat_mat('RailTop', '#D8DCE4')
    kit.obj('LadleRail', 'LADLE', [bar, bar_top, post], lambda bm: (
        _box(bm, 5.0, 9.46, 9.0, 9.6, -0.1, 0.1, 0), top_line(bm, 5.0, 9.0, 9.6, -0.1, 1),
        [_box(bm, mx - 0.06, 9.15, mx + 0.06, 9.46, 0.05, 0.15, 2) for mx in (5.4, 8.6)],
        [add_tube(bm, [(hx, -0.12, 9.46), (hx + 0.06, -0.12, 9.36), (hx, -0.12, 9.26), (hx - 0.05, -0.12, 9.2)], 0.018)
         for hx in (6.3, 7.1, 7.9)]), outline=0.03)
    # wall cutting board (top 4.8)
    wb = wood_mat('WallBoard', '#A8774A', '#7C532E', '#5C3C20', plank=4.0)
    kit.obj('WallBoard', 'WALLBOARD', [wb, line_top, post], lambda bm: (
        add_prism(bm, rrect_pts(9.5, 4.42, 13.5, 4.8, 0.12), -0.3, 0.3, XZ, 0), top_line(bm, 9.6, 13.4, 4.8, -0.3, 1),
        add_prism(bm, ellipse_pts(13.15, 4.61, 0.07, 0.07, 12), -0.32, -0.3, XZ, 2)), outline=0.03)
    # tray shelf (steel, top 2.4)
    steel = toon_mat('TraySteel', '#7A8290', '#454B57', spec=0.6)
    kit.obj('Tray', 'TRAY', [steel, bar_top, post], lambda bm: (
        _box(bm, 12.5, 2.28, 15.5, 2.4, -0.3, 0.3, 0), top_line(bm, 12.5, 15.5, 2.4, -0.3, 1),
        [add_prism(bm, [(bx - 0.06, 2.28), (bx + 0.06, 2.28), (bx + 0.06, 2.0), (bx - 0.06, 1.94)], -0.2, 0.2, XZ, 2)
         for bx in (13.0, 15.0)]), outline=0.03)
    # range hood: flat top 7.2 (x 16..20.5), sloped body down to 5.6, warm lamp strip
    hood = toon_mat('HoodSteel', '#59606C', '#3A3F4A', spec=0.4, mid=0.7)
    lamp = flat_mat('HoodLamp', '#F0C27A')
    lip = flat_mat('HoodLip', '#2A2D35')
    kit.obj('RangeHood', 'HOOD', [hood, bar_top, lip, lamp], lambda bm: (
        add_prism(bm, [(16.0, 7.2), (20.5, 7.2), (20.7, 5.82), (15.8, 5.82)], -0.3, 0.3, XZ, 0),
        top_line(bm, 16.0, 20.5, 7.2, -0.3, 1), _box(bm, 15.8, 5.62, 20.7, 5.82, -0.32, -0.3, 2),
        _box(bm, 16.6, 5.6, 19.9, 5.66, -0.33, -0.32, 3)), outline=0.03)
    # butter block (sprite, pivot bottom centre)
    butter = toon_mat('Butter', '#F2D16B', '#C8A440', rim=0.3, rim_col='#FFF0A8')
    paper = flat_mat('ButterPaper', '#E8E2D2')
    kit.obj('Butter', 'BUTTER', [butter, paper], lambda bm: (
        _box(bm, -0.58, 0.04, 0.58, 0.96, -0.4, 0.4, 0),
        add_prism(bm, [(-0.6, 0.0), (0.6, 0.0), (0.6, 0.34), (0.45, 0.28), (0.3, 0.36), (0.1, 0.27), (-0.12, 0.35),
                       (-0.32, 0.28), (-0.5, 0.36), (-0.6, 0.3)], -0.42, -0.41, XZ, 1)), outline=0.018, sprite=True)


def kitchen_fx_sprites(out):
    def flame(frame):
        rnd = random.Random(40 + frame)
        tongues = [(-0.9 + 0.36 * k + rnd.uniform(-0.05, 0.05), 0.55 + 0.4 * rnd.random()) for k in range(6)]

        def shade(u, v):
            x = u * 1.5              # units, -1.5..1.5
            y = (v + 1) * 0.5        # units, 0..1
            outer = np.zeros_like(u, dtype=bool)
            mid = np.zeros_like(outer)
            inner = np.zeros_like(outer)
            for cx, h in tongues:
                w = 0.32 * (1 - y / h) ** 0.7
                body = (np.abs(x - cx - 0.05 * np.sin(y * 9 + frame)) < np.where(y < h, w, -1))
                outer |= body
                mid |= body & (np.abs(x - cx) < w * 0.62) & (y < h * 0.78)
                inner |= body & (np.abs(x - cx) < w * 0.32) & (y < h * 0.5)
            outer &= np.abs(x) < 1.48
            base = outer & (y < 0.08)
            out_ = np.zeros(u.shape + (4,), np.float32)
            for mask, col in ((outer, '#FF6A2A'), (mid, '#FFA33A'), (inner, '#FFE58A'), (base, '#5AA8FF')):
                out_[mask, :3] = hex01(col)
                out_[mask, 3] = 1.0
            return out_
        return ss_image(384, 128, shade)
    strip = np.zeros((128, 384 * 4, 4), np.float32)
    for f in range(4):
        strip[:, 384 * f:384 * (f + 1)] = outline_img(flame(f), '#14161D', 2)
    save_png(strip, os.path.join(out, 'burner_flame.png'))

    def glow(u, v):
        x, y = u * 1.5, (v + 1) * 0.1875
        r = np.sqrt((x / 1.4) ** 2 + ((y - 0.06) / 0.16) ** 2)
        a = np.where(r < 0.45, 0.9, np.where(r < 0.75, 0.55, np.where(r < 1.0, 0.25, 0.0)))
        o = _rgba(a.astype(np.float32), hex01('#FF3B2F'))
        o[..., :3] = np.where((r < 0.45)[..., None], hex01('#FF7A4A'), o[..., :3])
        return o
    save_png(ss_image(384, 48, glow), os.path.join(out, 'burner_glow.png'))


def outline_img(img, col, px):
    """Dark outline around a sprite (dilated alpha behind the image)."""
    a = img[..., 3] > 0.5
    d = a.copy()
    for dy in range(-px, px + 1):
        for dx in range(-px, px + 1):
            if dx * dx + dy * dy <= px * px:
                d |= np.roll(np.roll(a, dy, 0), dx, 1)
    base = np.zeros_like(img)
    base[d, :3] = hex01(col)
    base[d, 3] = 1.0
    over(base, img, 0, 0)
    return base


# ================================================================ winter

def winter_build(kit):
    bs.W_SILLS = [(1.0, 5.0), (14.5, 18.5)]
    bs.W_BEAMS = [(6.5, 10.5)]
    bs.W_ICE_FLOOR = (7.0, 13.0)
    bs.build_winter(kit)
    delete(['Bench', 'SillTassels', 'IcePuddle'])
    a, b = bs.W_ICE_FLOOR
    kit.obj('IcePuddle', 'FLOOR', [flat_mat('Ice3', bs.ICE), flat_mat('IceTop3', bs.ICE_TOP), flat_mat('IceShine3', '#7FB2D4')],
            lambda bm: (add_prism(bm, [(a, 0.0), (a, -0.18), (a + 0.25, -0.26), (b - 0.25, -0.26), (b, -0.18), (b, 0.0)],
                                  -1.06, -1.04, XZ, 0),
                        _box(bm, a + 0.05, -0.05, b - 0.05, 0.0, -1.07, -1.06, 1),
                        [add_prism(bm, [(s, -0.08), (s + 0.5, -0.08), (s + 0.42, -0.12), (s - 0.08, -0.12)], -1.08, -1.07, XZ, 2)
                         for s in (7.9, 10.0, 12.0)]), outline=0.03)
    x0, x1 = bs.W_SILLS[0]
    kit.obj('SillTassels', 'SILL', [flat_mat('Tassel', '#6A9CC4')], lambda bm: [
        add_prism(bm, [(tx - 0.05, 2.07), (tx + 0.05, 2.07), (tx, 2.07 - ln)], -0.2, 0.0, XZ)
        for tx, ln in zip([x0 + 0.35 + 0.47 * k for k in range(8)], [0.18, 0.28, 0.14, 0.24, 0.32, 0.16, 0.26, 0.2])],
        outline=0.02)
    stone = tex_wall_mat('Chimney', '#2E333D', '#2A2F38', '#20242B', 0.6, 0.35, 0.04, offset=0.5)
    kit.obj('Chimney', 'BG', [stone], lambda bm: _box(bm, 15.4, 5.9, 17.1, 9.0, 5.4, 5.6))


def winter_mid(kit):
    m = [flat_mat('WMid', '#1A1F2A'), flat_mat('WEdge', '#344056'), flat_mat('WFruit', '#432E25'),
         flat_mat('WFruitLit', '#56392C'), flat_mat('WCalyx', '#22261F'), flat_mat('WLeaf', '#1C2A27'),
         flat_mat('WBerry', '#4A2A30'), flat_mat('WRope', '#2A2B30')]

    def pole(bm, y, mat, o):
        dx, dz = o
        rect(bm, 8.4 + dx, 12.1 + dz, 13.8 + dx, 12.22 + dz, y, mat)
        poly(bm, [(8.68 + dx, 12.22 + dz), (8.72 + dx, 12.22 + dz), (9.02 + dx, 13.3 + dz), (8.98 + dx, 13.3 + dz)], y, mat)
        poly(bm, [(13.48 + dx, 12.22 + dz), (13.52 + dx, 12.22 + dz), (13.22 + dx, 13.3 + dz), (13.18 + dx, 13.3 + dz)], y, mat)
    rnd = random.Random(21)
    strings = [(9.3, 5), (10.5, 4), (11.7, 5), (12.9, 4)]
    kit.obj('DryPole', 'MID', m, lambda bm: (lit(bm, pole, 2.4), [
        (rect(bm, sx - 0.012, 12.1 - 0.3 * n - 0.05, sx + 0.012, 12.1, 2.3, 7),
         [(disc(bm, sx + rnd.uniform(-0.03, 0.03), 11.92 - 0.3 * k, 0.14, 2.28, 2, rz=0.13, n=20),
           disc(bm, sx - 0.04, 11.96 - 0.3 * k, 0.06, 2.26, 3, n=10),
           poly(bm, [(sx - 0.07, 12.03 - 0.3 * k), (sx + 0.07, 12.03 - 0.3 * k), (sx, 12.09 - 0.3 * k)], 2.25, 4))
          for k in range(n)]) for sx, n in strings]))
    kit.obj('Wreath', 'MID', m, lambda bm: (
        ring(bm, 3.0, 8.6, 0.36, 0.6, 2.4, 5),
        [disc(bm, 3.0 + 0.48 * math.cos(a), 8.6 + 0.48 * math.sin(a), 0.07, 2.38, 6, n=10)
         for a in (2 * math.pi * k / 9 + 0.3 for k in range(9))],
        poly(bm, [(2.78, 8.04), (3.0, 8.16), (3.22, 8.04), (3.22, 8.26), (3.0, 8.16), (2.78, 8.26)], 2.36, 6)))

    def arms(bm, y, mat, o):
        dx, dz = o
        for x0, s in ((5.6, 1), (23.0, -1)):
            rect(bm, min(x0, x0 + s * 0.5) + dx, 10.86 + dz, max(x0, x0 + s * 0.5) + dx, 10.94 + dz, y, mat)
            poly(bm, [(x0 + dx, 10.94 + dz), (x0 + dx, 10.5 + dz), (x0 + s * 0.3 + dx, 10.86 + dz)], y, mat)
            rect(bm, x0 - 0.06 + dx, 10.4 + dz, x0 + 0.06 + dx, 11.05 + dz, y, mat)

    def hat(bm, y, mat, o):
        dx, dz = o
        disc(bm, 8.6 + dx, 7.75 + dz, 0.85, y, mat, rz=0.2, n=28)
        disc(bm, 8.6 + dx, 7.85 + dz, 0.42, y, mat, rz=0.42, a0=0.0, a1=math.pi, n=20)
        poly(bm, [(8.52 + dx, 8.22 + dz), (8.68 + dx, 8.22 + dz), (8.6 + dx, 8.4 + dz)], y, mat)

    def sled(bm, y, mat, o):     # hung upright on a nail
        dx, dz = o
        poly(bm, rrect_pts(19.9 + dx, 8.6 + dz, 20.35 + dx, 10.4 + dz, 0.12), y, mat)
        poly(bm, [(19.75 + dx, 8.4 + dz), (19.85 + dx, 8.4 + dz), (19.85 + dx, 10.5 + dz), (19.98 + dx, 10.66 + dz),
                  (19.9 + dx, 10.7 + dz), (19.75 + dx, 10.55 + dz)], y, mat)
        disc(bm, 20.12 + dx, 10.55 + dz, 0.05, y, mat, n=10)
    kit.obj('Props', 'MID', m, lambda bm: (lit(bm, arms, 2.4), lit(bm, hat, 2.4), lit(bm, sled, 2.4),
                                           [rect(bm, 19.95, sz, 20.3, sz + 0.04, 2.38, 1) for sz in (9.0, 9.5, 10.0)]))


def winter_pieces(kit):
    snow = flat_mat('Snow2', bs.SNOW)
    snow_top = flat_mat('SnowTop2', bs.SNOW_TOP)
    wood = wood_mat('Awning', '#6E4E36', '#523825', '#3A2819', plank=1.6)
    strut = flat_mat('Strut', '#3A2A1E')
    crack = flat_mat('Crack', '#1A1410')
    slide = flat_mat('SnowSlide', '#7E90AE')
    # snow awning around its pivot (top centre at the origin): plank + snow cap, struts to the wall
    for coll, cracked in (('AWNING', False), ('AWNINGCRACK', True)):
        def awn(bm, cracked=cracked):
            _box(bm, -1.5, -0.42, 1.5, -0.14, -0.3, 0.3, 0)
            add_prism(bm, drip_band(-1.56, 1.56, 0.0, -0.12, 0.12, 10, 31), -0.33, -0.31, XZ, 1)
            _box(bm, -1.52, -0.05, 1.52, 0.0, -0.34, -0.33, 2)
            for sx in (-1.1, 1.1):
                add_prism(bm, [(sx - 0.05, -0.42), (sx + 0.05, -0.42), (sx + 0.05 + 0.3 * (-sx / 1.1), -0.74),
                               (sx - 0.05 + 0.3 * (-sx / 1.1), -0.74)], -0.2, 0.2, XZ, 3)
            if cracked:
                for cx, pts in ((-0.35, [(-0.4, -0.14), (-0.3, -0.24), (-0.38, -0.32), (-0.28, -0.42)]),
                                (0.6, [(0.55, -0.14), (0.66, -0.26), (0.58, -0.42)])):
                    for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
                        add_prism(bm, [(x0 - 0.02, z0), (x0 + 0.02, z0), (x1 + 0.02, z1), (x1 - 0.02, z1)], -0.36, -0.35, XZ, 4)
                for sx, ln in ((-1.2, 0.3), (-0.2, 0.22), (0.9, 0.34)):
                    add_prism(bm, [(sx - 0.07, -0.12), (sx + 0.07, -0.12), (sx + 0.03, -0.12 - ln), (sx - 0.03, -0.12 - ln)],
                              -0.37, -0.36, XZ, 5)
        kit.obj('Awning' + coll, coll, [wood, snow, snow_top, strut, crack, slide], awn, outline=0.03)
    # stone ledge (top 9.6, x 14.5..18)
    stone = tex_wall_mat('Ledge', '#6A7280', '#626A78', '#3A3F48', 0.9, 0.5, 0.04, offset=0.5)
    kit.obj('Ledge', 'LEDGE', [stone, snow, snow_top], lambda bm: (
        _box(bm, 14.5, 9.1, 18.0, 9.52, -0.3, 0.3, 0),
        add_prism(bm, drip_band(14.46, 18.04, 9.6, 9.5, 0.08, 8, 32), -0.33, -0.31, XZ, 1),
        _box(bm, 14.5, 9.55, 18.0, 9.6, -0.34, -0.33, 2)), outline=0.03)
    # icy gutter (top 7.2, x 19..23.5), small icicle tassels under it
    gut = toon_mat('Gutter2', '#3A4150', '#232833')
    ice = flat_mat('Ice2', bs.ICE)
    ice_top = flat_mat('IceTop2', bs.ICE_TOP)
    tassel = flat_mat('Tassel2', '#6A9CC4')
    kit.obj('Gutter', 'GUTTER', [gut, ice, ice_top], lambda bm: (
        add_prism(bm, [(19.0, 7.1), (23.5, 7.1)] + [(23.5 - 4.5 * (k / 12), 6.92 - 0.12 * math.sin(math.pi * k / 12))
                                                     for k in range(13)][1:-1] + [(19.0, 6.92)], -0.3, 0.3, XZ, 0),
        _box(bm, 19.0, 6.92, 23.5, 7.1, -0.3, 0.3, 0),
        add_prism(bm, drip_band(18.95, 23.55, 7.2, 7.1, 0.1, 10, 33), -0.33, -0.31, XZ, 1),
        _box(bm, 18.98, 7.15, 23.52, 7.2, -0.34, -0.33, 2)), outline=0.03)
    kit.obj('GutterTassels', 'GUTTER', [tassel], lambda bm: [
        add_prism(bm, [(tx - 0.05, 6.85), (tx + 0.05, 6.85), (tx, 6.85 - ln)], -0.2, 0.0, XZ)
        for tx, ln in zip([19.4 + 0.42 * k for k in range(10)], [0.16, 0.22, 0.12, 0.2, 0.22, 0.14, 0.21, 0.17, 0.22, 0.13])],
        outline=0.02)


def winter_fx_sprites(out):
    def dump(u, v):     # falling snow clump, hangs from its top centre
        cov = np.zeros_like(u)
        for cx, cy, r in ((0.0, 0.25, 0.42), (-0.32, 0.35, 0.28), (0.3, 0.38, 0.3), (-0.1, -0.15, 0.3),
                          (0.22, -0.05, 0.24), (-0.4, -0.45, 0.1), (0.35, -0.55, 0.08), (0.0, -0.7, 0.07)):
            cov = np.maximum(cov, ((u - cx) ** 2 + (v - cy) ** 2 < r * r).astype(np.float32))
        o = _rgba(cov, hex01('#A2B2CC'))
        o[..., :3] = np.where(((v > 0.35) & (u < 0.1))[..., None], hex01('#C4D2E6'), o[..., :3])
        return o
    save_png(outline_img(ss_image(256, 256, dump), '#14161D', 2), os.path.join(out, 'snow_dump.png'))

    def streak(u, v):
        w = 1 - np.abs(u) ** 2
        return _rgba(((np.abs(v) < 0.8 * w) & (np.abs(u) < 0.98)).astype(np.float32) * 0.85, (1, 1, 1))
    save_png(ss_image(256, 13, streak), os.path.join(out, 'wind_streak.png'))
    save_png(ss_image(16, 16, lambda u, v: _rgba((u * u + v * v < 0.75).astype(np.float32), (1, 1, 1))),
             os.path.join(out, 'snowflake.png'))


# ================================================================ warehouse

def warehouse_build(kit):
    bs.build_warehouse(kit)
    delete(['Chains', 'Pallet', 'PalletEyes', 'RackPosts', 'RackShelves', 'Floor'])


def warehouse_mid(kit):
    m = [flat_mat('HMid', '#1B2029'), flat_mat('HEdge', '#353D4C'), flat_mat('HTube', '#39404D'),
         flat_mat('HGrill', '#14181E'), flat_mat('HTool', '#2A303A'), flat_mat('HBoard', '#20252D'),
         flat_mat('HGauge', '#2E3440')]

    def tubes(bm, y, mat, o):
        dx, dz = o
        for x0 in (7.4, 14.8):
            for cx in (x0 + 0.25, x0 + 1.55):
                rect(bm, cx - 0.015 + dx, 11.95 + dz, cx + 0.015 + dx, 14.5 + dz, y, mat)
            poly(bm, [(x0 + dx, 11.95 + dz), (x0 + 1.8 + dx, 11.95 + dz), (x0 + 1.7 + dx, 11.7 + dz),
                      (x0 + 0.1 + dx, 11.7 + dz)], y, mat)
    kit.obj('Tubes', 'MID', m, lambda bm: (lit(bm, tubes, 2.4),
                                           [rect(bm, x0 + 0.12, 11.6, x0 + 1.68, 11.7, 2.38, 2) for x0 in (7.4, 14.8)]))

    def vent(bm, y, mat, o):
        dx, dz = o
        poly(bm, rrect_pts(11.15 + dx, 4.15 + dz, 12.85 + dx, 5.85 + dz, 0.12), y, mat)
    kit.obj('Vent', 'MID', m, lambda bm: (lit(bm, vent, 2.4), disc(bm, 12.0, 5.0, 0.72, 2.38, 3),
                                          [disc(bm, 12.0 + 0.66 * sx, 5.0 + 0.66 * sz, 0.05, 2.36, 1, n=10)
                                           for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]))

    def pipe(bm, y, mat, o):
        dx, dz = o
        rect(bm, -2.0 + dx, 12.5 + dz, 26.0 + dx, 12.64 + dz, y, mat)
        rect(bm, 23.83 + dx, 9.5 + dz, 23.97 + dx, 12.5 + dz, y, mat)
        for bx in [0.5 + 3.0 * k for k in range(9)]:
            rect(bm, bx + dx, 12.46 + dz, bx + 0.12 + dx, 12.68 + dz, y, mat)
    kit.obj('Pipes', 'MID', m, lambda bm: (lit(bm, pipe, 2.5), disc(bm, 23.9, 9.32, 0.24, 2.48, 1),
                                           disc(bm, 23.9, 9.32, 0.2, 2.46, 6),
                                           poly(bm, [(23.89, 9.32), (23.91, 9.32), (24.02, 9.46), (24.0, 9.47)], 2.44, 2)))

    # tool pegboard between the wall shelf (3.6) and the catwalk (6.0): wrench, hammer, saw
    def peg(bm, y, mat, o):
        dx, dz = o
        rect(bm, 0.5 + dx, 4.25 + dz, 3.4 + dx, 5.4 + dz, y, mat)
    kit.obj('Pegboard', 'MID', m, lambda bm: (
        lit(bm, peg, 2.4), rect(bm, 0.55, 4.3, 3.35, 5.35, 2.38, 5),
        [disc(bm, 0.75 + 0.3 * i, 4.45 + 0.28 * j, 0.025, 2.36, 0, n=8) for i in range(9) for j in range(4)],
        poly(bm, [(0.9, 4.45), (1.0, 4.45), (1.0, 5.0), (1.12, 5.12), (1.0, 5.25), (0.88, 5.25), (0.78, 5.12), (0.9, 5.0)],
             2.34, 4),
        rect(bm, 1.55, 4.4, 1.63, 5.05, 2.34, 4), rect(bm, 1.38, 5.0, 1.8, 5.2, 2.34, 4),
        poly(bm, [(2.25, 4.42), (3.05, 4.62), (3.05, 4.9), (2.25, 4.9)], 2.34, 4), rect(bm, 2.05, 4.55, 2.28, 4.95, 2.33, 4)))


def warehouse_pieces(kit):
    # floor: concrete + safety band, conveyor frame at x 6..19 (belt surface is the conveyor_belt tile)
    conc = tex_wall_mat('Concrete2', '#42474F', '#3D424A', '#30343B', 3.2, 40.0, 0.02)
    conc_top = flat_mat('ConcreteTop2', '#CDBF9F')
    yellow = flat_mat('Safety2', '#8C7834')
    black = flat_mat('SafetyDark2', '#202228')
    frame = flat_mat('ConvFrame', '#2A2E36')
    roller = flat_mat('Roller', '#4A505C')
    recess = flat_mat('ConvRecess', '#15171C')
    kit.obj('Floor', 'FLOOR', [conc, conc_top, yellow, black, frame, roller, recess], lambda bm: (
        _box(bm, -2.0, -2.5, 26.0, 0.0, -1.0, 1.0, 0), _box(bm, -2.0, -0.05, 26.0, 0.0, -1.02, -1.0, 1),
        _box(bm, -2.0, -0.44, 26.0, -0.22, -1.02, -1.0, 2),
        [add_prism(bm, [(x, -0.44), (x + 0.22, -0.44), (x + 0.44, -0.22), (x + 0.22, -0.22)], -1.04, -1.02, XZ, 3)
         for x in [-2.0 + 0.5 * k for k in range(57)] if not 5.8 < x < 19.0],
        _box(bm, 6.0, -0.25, 19.0, 0.0, -1.05, -1.04, 6),
        _box(bm, 5.9, -0.62, 19.1, -0.25, -1.05, -1.04, 4),
        [add_prism(bm, ellipse_pts(rx, -0.44, 0.13, 0.13, 16), -1.06, -1.05, XZ, 5) for rx in
         [6.3 + 0.8 * k for k in range(17)]]))
    # workbench: steel top (1.2), four legs, low crossbar, empty underneath
    steel = toon_mat('BenchSteel', '#5E6676', '#3A404C', spec=0.4, mid=0.7)
    top = flat_mat('SteelTop', '#D8DCE4')
    leg = flat_mat('BenchLeg', '#3A4454')
    kit.obj('Workbench', 'BENCH', [steel, top, leg], lambda bm: (
        _box(bm, 0.0, 1.0, 6.0, 1.2, -0.3, 0.3, 0), top_line(bm, 0.0, 6.0, 1.2, -0.3, 1),
        [_box(bm, lx, 0.0, lx + 0.14, 1.0, -0.2, 0.2, 2) for lx in (0.15, 2.2, 3.66, 5.71)],
        _box(bm, 0.15, 0.22, 5.85, 0.3, 0.0, 0.2, 2)), outline=0.03)
    # wall shelf (3.6) and catwalk grating (6.0)
    plate = toon_mat('ShelfPlate', '#6A7282', '#424A58', spec=0.3, mid=0.7)
    kit.obj('WallShelf', 'WSHELF', [plate, top, leg], lambda bm: (
        _box(bm, 0.5, 3.48, 3.5, 3.6, -0.3, 0.3, 0), top_line(bm, 0.5, 3.5, 3.6, -0.3, 1),
        [add_prism(bm, [(bx - 0.06, 3.48), (bx + 0.06, 3.48), (bx + 0.06, 3.2), (bx - 0.06, 3.14)], -0.2, 0.2, XZ, 2)
         for bx in (1.0, 3.0)]), outline=0.03)
    grate = flat_mat('Grate', '#4A5262')
    hole = flat_mat('GrateHole', '#1E232C')
    kit.obj('Catwalk', 'CATWALK', [grate, top, hole, leg], lambda bm: (
        _box(bm, 2.5, 5.82, 6.0, 6.0, -0.3, 0.3, 0), top_line(bm, 2.5, 6.0, 6.0, -0.3, 1),
        [_box(bm, hx, 5.86, hx + 0.12, 5.94, -0.32, -0.31, 2) for hx in [2.62 + 0.2 * k for k in range(17)]],
        [add_prism(bm, [(tx, 5.82), (tx + 0.06, 5.82), (tx + 0.44, 5.6), (tx + 0.38, 5.6)], -0.2, 0.2, XZ, 3)
         for tx in (2.7, 3.6, 4.5, 5.4)], _box(bm, 2.6, 5.56, 5.9, 5.62, -0.2, 0.2, 3)), outline=0.03)
    # three-level rack (2.4 / 4.8 / 7.2, x 19..23.5)
    post = flat_mat('Post2', '#3A4454')
    post_hole = flat_mat('PostHole2', '#1E232C')
    beam = toon_mat('RackBeam2', '#9C5A30', '#6A3A20', spec=0.3, mid=0.7)
    beam_top = flat_mat('RackTop2', '#F0B474')
    rplate = flat_mat('RackPlate2', '#2C323C')
    kit.obj('RackPosts', 'RACK3', [post, post_hole], lambda bm: [
        (_box(bm, px, 0.0, px + 0.14, 7.2, 0.0, 0.2, 0),
         [_box(bm, px + 0.05, hz, px + 0.09, hz + 0.08, -0.02, 0.0, 1) for hz in [0.3 + 0.3 * k for k in range(23)]])
        for px in (19.0, 23.36)], outline=0.03)
    kit.obj('RackShelves', 'RACK3', [beam, beam_top, rplate], lambda bm: [
        (_box(bm, 19.0, t - 0.24, 23.5, t, -0.3, -0.1, 0), top_line(bm, 19.0, 23.5, t, -0.3, 1),
         [_box(bm, px, t - 0.3, px + 0.2, t - 0.02, -0.33, -0.31, 2) for px in (18.97, 23.33)])
        for t in (2.4, 4.8, 7.2)], outline=0.03)
    # crane pallet around its pivot (top centre at the origin): pallet 4.0 wide, two chains, trolley on the rail
    deck = wood_mat('PalletWood2', '#86663F', '#664B2D', '#4A3420', plank=1.25)
    block = flat_mat('PalletBlock2', '#5E452C')
    deck_top = flat_mat('PalletTop2', '#E8C48A')
    chain = flat_mat('Chain2', '#59606C')
    chain_d = flat_mat('ChainDark2', '#3E434D')
    trolley = toon_mat('Trolley', '#A88A3A', '#6E5A24', spec=0.3)
    wheel = flat_mat('Wheel', '#2A2E36')
    kit.obj('Pallet', 'CRANE', [deck, block, deck_top], lambda bm: (
        _box(bm, -2.0, -0.18, 2.0, 0.0, -0.3, 0.3, 0),
        [_box(bm, bx, -0.4, bx + 0.6, -0.18, -0.25, 0.25, 1) for bx in (-2.0, -0.3, 1.4)],
        _box(bm, -2.0, -0.46, 2.0, -0.4, -0.3, 0.3, 0), top_line(bm, -2.0, 2.0, 0.0, -0.3, 2)), outline=0.03)
    kit.obj('CraneChains', 'CRANE', [chain, chain_d], lambda bm: [hang_chain(bm, cx, 0.18, 4.82, 0.4) for cx in (-1.5, 1.5)])
    kit.obj('Trolley', 'CRANE', [trolley, wheel], lambda bm: (
        _box(bm, -1.75, 4.8, 1.75, 5.02, 0.3, 0.5, 0),
        [add_prism(bm, ellipse_pts(wx, 5.04, 0.09, 0.09, 14), 0.25, 0.3, XZ, 1) for wx in (-1.3, -0.6, 0.6, 1.3)],
        [_box(bm, cx - 0.06, 4.6, cx + 0.06, 4.8, 0.3, 0.5, 0) for cx in (-1.5, 1.5)]), outline=0.02)


def warehouse_fx_sprites(out):
    w, h = 72, 18      # 1.0 x 0.25 u tile, repeats along x
    img = np.zeros((h, w, 4), np.float32)
    img[...] = (*hex01('#2A2D33'), 1.0)
    for gx in range(0, w, 12):
        img[2:h - 3, gx:gx + 3, :3] = hex01('#1B1D22')
        img[2:h - 3, gx + 3:gx + 4, :3] = hex01('#3A3E46')
    img[h - 2:h, :, :3] = hex01('#B9AE92')       # top edge (rows bottom-up: last rows = top)
    img[:2, :, :3] = hex01('#15171C')
    save_png(img, os.path.join(out, 'conveyor_belt.png'))


# ================================================================ dessert

def dessert_build(kit):
    bs.build_dessert(kit)
    delete(['Menu'])
    board = flat_mat('Menu2', '#232A28')
    board_frame = flat_mat('MenuFrame2', '#3A2C24')
    chalk = flat_mat('Chalk2', '#3E4C48')
    kit.obj('Menu', 'BG', [board_frame, board, chalk], lambda bm: (
        _box(bm, 21.0, 8.8, 23.6, 11.4, 5.0, 5.2, 0), _box(bm, 21.15, 8.95, 23.45, 11.25, 4.8, 5.0, 1),
        [_box(bm, 21.45, mz, 21.45 + ln, mz + 0.07, 4.7, 4.8, 2) for mz, ln in (
            (10.8, 1.5), (10.4, 1.0), (10.05, 1.3), (9.65, 0.8), (9.3, 1.4))]))
    slot = flat_mat('LiftSlot', '#1C1720')
    slot_lit = flat_mat('LiftSlotLit', '#2E2633')
    kit.obj('LiftSlot', 'BG', [slot, slot_lit], lambda bm: (
        _box(bm, 16.95, 0.0, 17.55, 8.1, 4.6, 4.8, 0), _box(bm, 16.95, 0.0, 17.0, 8.1, 4.5, 4.6, 1),
        _box(bm, 17.5, 0.0, 17.55, 8.1, 4.5, 4.6, 1)))


def dessert_mid(kit):
    m = [flat_mat('DMid', '#211B24'), flat_mat('DEdge', '#3C3242'), flat_mat('DGlass', '#2C2A39'),
         flat_mat('DCake', '#3E3236'), flat_mat('DCream', '#4C4249'), flat_mat('DMac', '#3E4A44'),
         flat_mat('DMac2', '#4A3C48'), flat_mat('DBulb', '#3A3440'), flat_mat('DWire', '#1A161C')]
    x0 = 4.6

    def cab(bm, y, mat, o):
        dx, dz = o
        rect(bm, x0 + dx, 8.85 + dz, x0 + 4.8 + dx, 11.15 + dz, y, mat)
        poly(bm, [(x0 + 1.75 + dx, 11.15 + dz), (x0 + 3.05 + dx, 11.15 + dz), (x0 + 2.9 + dx, 11.22 + dz),
                  (x0 + 1.9 + dx, 11.22 + dz)], y, mat)
        disc(bm, x0 + 2.4 + dx, 11.22 + dz, 0.5, y, mat, rz=0.55, a0=0.0, a1=math.pi, n=20)
    kit.obj('Cabinet', 'MID', m, lambda bm: (
        lit(bm, cab, 2.5), rect(bm, x0 + 0.15, 9.0, x0 + 4.65, 11.0, 2.48, 2), rect(bm, x0 + 0.15, 9.92, x0 + 4.65, 9.98, 2.46, 1),
        poly(bm, rrect_pts(x0 + 0.4, 9.0, x0 + 1.4, 9.5, 0.08), 2.44, 3), rect(bm, x0 + 0.4, 9.42, x0 + 1.4, 9.52, 2.42, 4),
        [poly(bm, [(cx - 0.2, 9.0), (cx + 0.2, 9.0), (cx + 0.24, 9.22), (cx - 0.24, 9.22)], 2.44, 3) for cx in (x0 + 2.0, x0 + 2.65)],
        [disc(bm, cx, 9.3, 0.17, 2.42, 4, rz=0.12, n=16) for cx in (x0 + 2.0, x0 + 2.65)],
        poly(bm, rrect_pts(x0 + 3.2, 9.0, x0 + 4.5, 9.32, 0.14), 2.44, 3), ring(bm, x0 + 3.85, 9.16, 0.04, 0.12, 2.42, 4, 16),
        [disc(bm, x0 + 0.55 + 0.42 * k, 10.13, 0.15, 2.44, 5 + k % 2, rz=0.11, n=16) for k in range(5)],
        poly(bm, [(x0 + 2.85, 9.98), (x0 + 4.55, 9.98), (x0 + 4.35, 10.3), (x0 + 3.05, 10.3)], 2.44, 3),
        disc(bm, x0 + 2.4, 11.22, 0.38, 2.46, 2, rz=0.42, a0=0.0, a1=math.pi, n=20),
        poly(bm, rrect_pts(x0 + 2.1, 11.22, x0 + 2.7, 11.48, 0.05), 2.44, 3)))

    def wire(bm):
        for a, b in bd.GARLAND:
            pts = [(a + (b - a) * i / 16, bd.GARLAND_TOP - 4 * bd.GARLAND_SAG * (i / 16) * (1 - i / 16)) for i in range(17)]
            for (xa, za), (xb, zb) in zip(pts, pts[1:]):
                poly(bm, [(xa, za - 0.012), (xb, zb - 0.012), (xb, zb + 0.012), (xa, za + 0.012)], 2.3, 8)
            disc(bm, a, bd.GARLAND_TOP, 0.05, 2.28, 1, n=10)
        for x, z in bd.garland_points():
            rect(bm, x - 0.025, z + 0.06, x + 0.025, z + 0.1, 2.27, 8)
            disc(bm, x, z, 0.06, 2.26, 7, rz=0.085, n=12)
    kit.obj('Garland', 'MID', m, wire)


def dessert_pieces(kit):
    # cherry cake top (9.6, x 13.5..16): sponge layers, cream top, cherry at the back below the top line
    sponge = flat_mat('Sponge', '#C99A62')
    jam = flat_mat('Jam', '#A04A5A')
    cream = flat_mat('Cream', '#EADFCB')
    cream_top = flat_mat('CreamTop', '#FFF6E4')
    cherry = toon_mat('Cherry', '#C8324A', '#7E1E30', spec=0.6)
    kit.obj('Cake', 'CAKE', [sponge, jam, cream, cream_top], lambda bm: (
        _box(bm, 13.5, 8.98, 16.0, 9.2, -0.3, 0.3, 0), _box(bm, 13.5, 9.2, 16.0, 9.27, -0.31, -0.3, 1),
        _box(bm, 13.5, 9.27, 16.0, 9.45, -0.3, 0.3, 0),
        add_prism(bm, drip_band(13.46, 16.04, 9.6, 9.46, 0.1, 7, 41), -0.33, -0.31, XZ, 2),
        top_line(bm, 13.48, 16.02, 9.6, -0.33, 3)), outline=0.03)
    kit.obj('CakeCherry', 'CAKE', [cherry], lambda bm: [add_ellipsoid(bm, (cx, -0.36, 9.235), (0.09, 0.05, 0.08))
                                                        for cx in (13.85, 14.45, 15.05, 15.65)], outline=0.015, smooth=True)
    # wafer shelf (7.2, x 18.5..22.5)
    wafer = flat_mat('Wafer2', '#B98A52')
    wafer_line = flat_mat('WaferLine2', '#8A6236')
    wafer_top = flat_mat('WaferTop2', '#F2D29A')
    kit.obj('Wafer', 'WAFER', [wafer, wafer_line, wafer_top], lambda bm: (
        _box(bm, 18.5, 6.88, 22.5, 7.2, -0.3, 0.3, 0),
        [_box(bm, wx, 6.9, wx + 0.04, 7.15, -0.31, -0.3, 1) for wx in [18.7 + 0.3 * k for k in range(13)]],
        _box(bm, 18.5, 7.02, 22.5, 7.05, -0.31, -0.3, 1), top_line(bm, 18.5, 22.5, 7.2, -0.32, 2)), outline=0.03)
    # cake stand plate around its pivot (top centre): plate 2.5 wide with a doily edge
    plate = toon_mat('LiftPlate', '#E8DCEA', '#A898B0', spec=0.4, mid=0.7)
    doily = flat_mat('Doily', '#F6EEF6')
    rim = flat_mat('PlateRim', '#FFFFFF')
    kit.obj('LiftPlate', 'LIFT', [plate, doily, rim], lambda bm: (
        add_prism(bm, [(-1.25, 0.0), (1.25, 0.0), (1.1, -0.16), (0.35, -0.22), (-0.35, -0.22), (-1.1, -0.16)], -0.3, 0.3, XZ, 0),
        [add_prism(bm, ellipse_pts(sx, -0.12, 0.07, 0.07, 10, math.pi, 2 * math.pi), -0.32, -0.31, XZ, 1)
         for sx in [-1.05 + 0.21 * k for k in range(11)]],
        top_line(bm, -1.25, 1.25, 0.0, -0.33, 2)), outline=0.03)


def dessert_fx_sprites(out):
    # pedestal column tile 0.6 x 1.0 u at 72 px: fluted pastel stripes, repeats vertically
    w, h = 43, 72
    img = np.zeros((h, w, 4), np.float32)
    cols = ['#C8B4CC', '#B4A0BA', '#D8C8DA', '#B4A0BA']
    for x in range(w):
        img[:, x, :3] = hex01(cols[(x // 6) % 4])
        img[:, x, 3] = 1.0
    img[:, :2, :3] = hex01('#14161D')
    img[:, w - 2:, :3] = hex01('#14161D')
    img[:, 2:5, :3] = img[:, 2:5, :3] * 0.8
    save_png(img, os.path.join(out, 'cake_lift_column.png'))

    def syrup(u, v):    # 11.0 x 0.3 u, 36 px rounded ends (9-slice), translucent brown with a glossy streak
        x = (u + 1) * 396           # px 0..792
        y = (v + 1) * 11            # px 0..22 (bottom-up)
        end = np.minimum(x, 792 - x)
        r = 11.0
        inside = (y < 15) & ((end > r) | ((end - r) ** 2 + (np.maximum(y - 4, 0)) ** 2 < r * r)) & (y > 0.5)
        o = _rgba(inside.astype(np.float32) * 0.78, hex01('#6A3A18'))
        gloss = inside & (y > 10) & (y < 13) & (end > 40)
        o[..., :3] = np.where(gloss[..., None], hex01('#C88A4A'), o[..., :3])
        top = inside & (y > 13.5)
        o[..., :3] = np.where(top[..., None], hex01('#9A5A2A'), o[..., :3])
        return o
    save_png(ss_image(792, 22, syrup), os.path.join(out, 'syrup.png'))

    def drip(u, v):     # stream from the top (pivot top centre) ending in a drop
        x, y = u * 0.25, (v - 1) * 0.5          # units; y 0 at top, -1 at bottom
        stream = (np.abs(x) < 0.035 + 0.01 * np.sin(y * 20)) & (y > -0.78)
        drop = (x ** 2 + ((y + 0.84) / 1.3) ** 2 < 0.075 ** 2)
        o = _rgba((stream | drop).astype(np.float32) * 0.85, hex01('#7A4420'))
        o[..., :3] = np.where(((x < -0.01) & (stream | drop))[..., None], hex01('#B8783C'), o[..., :3])
        return o
    save_png(ss_image(64, 128, drip), os.path.join(out, 'syrup_drip.png'))


# ================================================================ per map: render

MAPS2 = {
    'kitchen': dict(
        folder='map', build=kitchen_bg, mid=kitchen_mid, front=bd.front_kitchen, pieces=kitchen_pieces,
        fxsprites=kitchen_fx_sprites, attach=('Rail', 'Utensils', 'Duct', 'Backsplash'), fog=('#1A1E28', 0.10),
        list=[('kitchen_floor.png', 'FLOOR', FLOOR_RECT, {}),
              ('spice_rack.png', 'TOWER', (0.75, 0.0, 4.25, 7.325), {}),
              ('ladle_rail.png', 'LADLE', (4.75, 9.1, 9.25, 9.725), {}),
              ('wall_board.png', 'WALLBOARD', (9.25, 4.3, 13.75, 4.925), {}),
              ('tray_shelf.png', 'TRAY', (12.25, 1.9, 15.75, 2.525), {}),
              ('range_hood.png', 'HOOD', (15.75, 5.6, 20.75, 7.325), {})],
        sprites=[('butter.png', 'BUTTER', (154, 128), (77, 0), 'pivot = bottom centre, 1.2 x 1.0')],
        extra={'burner_flame.png': dict(ppu=128, size=[1536, 128], frames=4, frame_size=[384, 128],
                                        pivot_px_from_bottom_left=[192, 0], note='3.0 x 1.0, loop the 4 frames'),
               'burner_glow.png': dict(ppu=128, size=[384, 48], pivot_px_from_bottom_left=[192, 0],
                                       note='warning: burner ring glowing red, 3.0 x 0.375')},
        play=[('kitchen_floor.png', None), ('spice_rack.png', None), ('ladle_rail.png', None), ('wall_board.png', None),
              ('tray_shelf.png', None), ('range_hood.png', None)],
        fx=lambda: [
            dict(kind='steam', layer='mid', at=[17.6, 7.4], size=0.34, count=6, rise=2.2, period=4.6,
                 color='#9AA3B5', alpha=[0.0, 0.14]),
            dict(kind='steam', layer='mid', at=[19.0, 7.4], size=0.3, count=5, rise=1.9, period=5.3,
                 color='#9AA3B5', alpha=[0.0, 0.12]),
            dict(kind='glow', layer='far', at=[18.25, 5.3], size=2.4, color='#F0C27A', alpha=[0.03, 0.07], period=3.4)]),
    'winter': dict(
        folder='map_winter', build=winter_build, mid=winter_mid, front=bd.front_winter, pieces=winter_pieces,
        fxsprites=winter_fx_sprites, attach=('Window0', 'Window1', 'BeamBrackets', 'Chimney'), fog=('#1A2030', 0.10),
        list=[('winter_floor.png', 'FLOOR', FLOOR_RECT, dict(note='ice puddle x 7..13')),
              ('winter_ice_sill.png', 'SILL', (0.75, 1.525, 5.25, 2.525),
               dict(instances=[[0.75, 1.525, 5.25, 2.525], [14.25, 1.525, 18.75, 2.525]])),
              ('winter_beam.png', 'BEAM', (6.25, 4.3, 10.75, 4.925), {}),
              ('stone_ledge.png', 'LEDGE', (14.25, 8.975, 18.25, 9.725), {}),
              ('icy_gutter.png', 'GUTTER', (18.75, 6.6, 23.75, 7.325), {})],
        moving=[('snow_awning.png', 'AWNING', (252, 65), (126, 56), [[10.75, 6.425, 14.25, 7.325], [18.25, 4.025, 21.75, 4.925]]),
                ('snow_awning_crack.png', 'AWNINGCRACK', (252, 65), (126, 56), [])],
        extra={'snow_dump.png': dict(ppu=128, size=[256, 256], pivot_px_from_bottom_left=[128, 256],
                                     note='falling snow when an awning collapses, decoration'),
               'wind_streak.png': dict(ppu=128, size=[256, 13], pivot_px_from_bottom_left=[128, 6], note='2.0 x 0.1'),
               'snowflake.png': dict(ppu=128, size=[16, 16], pivot_px_from_bottom_left=[8, 8])},
        play=[('winter_floor.png', None), ('winter_ice_sill.png', None), ('winter_beam.png', None),
              ('stone_ledge.png', None), ('icy_gutter.png', None), ('snow_awning.png', None)],
        fx=lambda: [
            dict(kind='snow', layer='far', area=[-1.5, -0.6, 25.5, 13.2], count=70, size=0.07, speed=[0.35, 0.6],
                 color='#8FA3C8', alpha=[0.28, 0.42]),
            dict(kind='snow', layer='mid', area=[-1.5, -0.6, 25.5, 13.2], count=26, size=0.11, speed=[0.7, 1.1],
                 color='#9DB0D2', alpha=[0.38, 0.52]),
            dict(kind='sway', layer='mid', sprite='map_winter/lantern', pivot=[48, 160], at=[5.95, 10.86], scale=0.72,
                 color='#B4B4BC', glow='#E2AE68', glowAt=[0.0, -0.72], amp=5.0, period=3.6),
            dict(kind='sway', layer='mid', sprite='map_winter/lantern', pivot=[48, 160], at=[22.65, 10.86], scale=0.72,
                 color='#B4B4BC', glow='#E2AE68', glowAt=[0.0, -0.72], amp=5.0, period=4.1),
            dict(kind='glow', layer='attach', at=[3.0, 3.9], size=1.9, color='#E0904A', alpha=[0.04, 0.10], period=1.7,
                 flicker=0.35),
            dict(kind='glow', layer='attach', at=[16.5, 3.9], size=1.9, color='#E0904A', alpha=[0.04, 0.10], period=1.9,
                 flicker=0.35)]),
    'warehouse': dict(
        folder='map_warehouse', build=warehouse_build, mid=warehouse_mid, front=bd.front_warehouse,
        pieces=warehouse_pieces, fxsprites=warehouse_fx_sprites, attach=('Rail', 'Columns'), fog=('#171B24', 0.10),
        list=[('warehouse_floor.png', 'FLOOR', FLOOR_RECT, dict(note='conveyor frame x 6..19; belt = conveyor_belt tile')),
              ('workbench.png', 'BENCH', (-0.25, 0.0, 6.25, 1.325), {}),
              ('wall_shelf.png', 'WSHELF', (0.25, 3.1, 3.75, 3.725), {}),
              ('catwalk.png', 'CATWALK', (2.25, 5.5, 6.25, 6.125), {}),
              ('warehouse_rack3.png', 'RACK3', (18.75, 0.0, 23.75, 7.325), {})],
        moving=[('crane_pallet.png', 'CRANE', (324, 410), (162, 36), [[6.25, 7.9, 10.75, 13.6]])],
        extra={'conveyor_belt.png': dict(ppu=72, size=[72, 18], note='tile 1.0 x 0.25, repeat along x over 6..19, '
                                                                     'y -0.25..0; scroll during the event')},
        play=[('warehouse_floor.png', None), ('workbench.png', None), ('wall_shelf.png', None), ('catwalk.png', None),
              ('warehouse_rack3.png', None), ('crane_pallet.png', None)],
        fx=lambda: [
            dict(kind='dust', layer='far', points=[2.1, 9.0, 4.5, 9.0, 8.4, 0.0, 5.2, 0.0], count=26, size=0.06,
                 speed=[0.05, 0.16], color='#9AAACC', alpha=[0.18, 0.4]),
            dict(kind='dust', layer='far', points=[19.5, 9.0, 21.9, 9.0, 18.8, 0.0, 15.6, 0.0], count=26, size=0.06,
                 speed=[0.05, 0.16], color='#9AAACC', alpha=[0.18, 0.4]),
            dict(kind='tube', layer='mid', area=[7.52, 11.6, 9.08, 11.7], color='#C9D6E4', alpha=[0.42, 0.5],
                 size=1.7, period=5.0, flicker=0.0),
            dict(kind='tube', layer='mid', area=[14.92, 11.6, 16.48, 11.7], color='#C9D6E4', alpha=[0.42, 0.5],
                 size=1.7, period=7.0, flicker=1.0),
            dict(kind='spin', layer='mid', sprite='map_warehouse/vent_fan', pivot=[80, 80], at=[12.0, 5.0], scale=1.0,
                 color='#FFFFFF', amp=40.0),
            dict(kind='glow', layer='far', at=[12.0, 10.45], size=2.4, color='#D9AE6E', alpha=[0.05, 0.09], period=2.8)]),
    'dessert': dict(
        folder='map_dessert', build=dessert_build, mid=dessert_mid, front=bd.front_dessert, pieces=dessert_pieces,
        fxsprites=dessert_fx_sprites, attach=('Molding', 'LiftSlot'), fog=('#221C26', 0.12),
        list=[('cherry_cake.png', 'CAKE', (13.25, 8.85, 16.25, 9.725), {}),
              ('wafer_shelf.png', 'WAFER', (18.25, 6.7, 22.75, 7.325), {})],
        moving=[('cake_lift_plate.png', 'LIFT', (216, 45), (108, 36), [[15.75, 0.7, 18.75, 1.325]])],
        extra={'cake_lift_column.png': dict(ppu=72, size=[43, 72], pivot='top centre',
                                            note='0.6 x 1.0 tile, repeat downward from under the plate to the floor'),
               'syrup.png': dict(ppu=72, size=[792, 22], nine_slice=dict(left_px=36, right_px=36),
                                 instances=[[0.5, -0.05, 11.5, 0.25], [12.5, -0.05, 23.5, 0.25]],
                                 note='even events left half, odd right half'),
               'syrup_drip.png': dict(ppu=128, size=[64, 128], pivot_px_from_bottom_left=[32, 128])},
        play=[('dessert_floor.png', '..'), ('cherry_cake.png', None), ('wafer_shelf.png', None),
              ('cake_lift_plate.png', None)],
        fx=lambda: [
            dict(kind='bulbs', layer='mid', points=[c for p in bd.garland_points() for c in p], size=0.14,
                 colors=['#F2B8C6', '#BDEBD8', '#F6E7B8'], alpha=[0.02, 0.26], period=6.0),
            dict(kind='sparkle', layer='mid', points=[4.85, 10.9, 9.25, 10.72, 6.25, 9.78, 8.7, 9.15, 7.0, 11.58],
                 size=0.32, color='#F4EEF8', alpha=[0.0, 0.55], period=2.6),
            dict(kind='glow', layer='far', at=[2.4, 9.75], size=1.3, color='#C8955A', alpha=[0.06, 0.1], period=4.5)]),
}


def render_sprite(scene, kit, coll, size, piv, path):
    for n, c in kit.cols.items():
        c.hide_render = n != coll
    x0, z0 = -piv[0] / CHAR_PPU, -piv[1] / CHAR_PPU
    frame_camera(scene, x0, z0, x0 + size[0] / CHAR_PPU, z0 + size[1] / CHAR_PPU, CHAR_PPU)
    render_to(scene, path)


def target_map(name):
    cfg = MAPS2[name]
    scene = reset_scene()
    add_camera(scene, 0, 0, 1.0)
    add_sun(scene, (-0.5, -0.8, 0.6), energy=3.0)
    kit = MapKit(scene)
    cfg['build'](kit)
    # the old builders also add first-layout platform pieces / sprites; keep only background collections + ours
    move_to(kit, cfg['attach'], 'BGA')
    cfg['mid'](kit)
    cfg['front'](kit)
    cfg['pieces'](kit)
    out = os.path.join(bs.SPRITES, cfg['folder'], 'layout2')
    os.makedirs(out, exist_ok=True)
    tmp = os.path.join(out, '_tmp.png')

    def shot(colls, r=BG_RECT, ppu=MAP_PPU):
        for n, c in kit.cols.items():
            c.hide_render = n not in colls
        frame_camera(scene, *r, ppu)
        render_to(scene, tmp)
        return load_png(tmp)

    meta = dict(ppu=MAP_PPU, layout='map-layout.md b3c88ca', note='pieces: world_rect [x0, y0, x1, y1], pivot '
                'bottom-left; moving pieces: 72 px per unit, pivot px from bottom-left = platform top centre, '
                'world_rect = start position. Rules: docs/work/map-visual.md section 6.', pieces={}, moving={},
                sprites={})
    report = dict(map=name, sizes={}, clipped=[])
    far, attach = shot(('BG',)), shot(('BGA',))
    bg = far.copy()
    over(bg, attach, 0, 0)
    save_png(bg, os.path.join(out, '%s_bg.png' % name))
    meta['pieces']['%s_bg.png' % name] = dict(world_rect=list(BG_RECT), note='far + attach, for a client without layers')
    imgs = dict(far=fog(far, *cfg['fog']), attach=attach, mid=shot(('MID',)), front=blur(shot(('FRONT',)), 5))
    imgs['front'][..., 3] *= 0.92
    for fname, coll, r, extra in cfg['list']:
        img = shot((coll,), r)
        save_png(img, os.path.join(out, fname))
        meta['pieces'][fname] = dict(world_rect=list(r), **extra)
        report['sizes'][fname] = [img.shape[1], img.shape[0]]
        if coll != 'FLOOR' and bs._touches_edge(img):
            report['clipped'].append(fname)
    for fname, coll, size, piv, starts in cfg.get('moving', []):
        # 72 px per unit around the platform top centre
        x0, z0 = -piv[0] / MAP_PPU, -piv[1] / MAP_PPU
        img = shot((coll,), (x0, z0, x0 + size[0] / MAP_PPU, z0 + size[1] / MAP_PPU))
        save_png(img, os.path.join(out, fname))
        meta['moving'][fname] = dict(size=list(size), pivot_px_from_bottom_left=list(piv),
                                     pivot_normalized=[round(piv[0] / size[0], 5), round(piv[1] / size[1], 5)],
                                     start_world_rects=starts)
        report['sizes'][fname] = [img.shape[1], img.shape[0]]
    for fname, coll, size, piv, note in cfg.get('sprites', []):
        render_sprite(scene, kit, coll, size, piv, os.path.join(out, fname))
        meta['sprites'][fname] = dict(ppu=CHAR_PPU, size=list(size), pivot_px_from_bottom_left=list(piv), note=note)
        report['sizes'][fname] = list(load_png(os.path.join(out, fname)).shape[1::-1])
    os.remove(tmp)
    cfg['fxsprites'](out)
    meta['sprites'].update(cfg['extra'])
    for fname in cfg['extra']:
        report['sizes'][fname] = list(load_png(os.path.join(out, fname)).shape[1::-1])

    layers = []
    for key, order, depth in bd.LAYERS:
        fname = '%s_%s.png' % (name, key)
        img, r = (imgs[key], list(BG_RECT)) if key == 'far' else crop(imgs[key], BG_RECT)
        save_png(img, os.path.join(out, fname))
        layers.append(dict(name=key, file=fname[:-4], rect=r, depth=depth, order=order, size=[img.shape[1], img.shape[0]]))
    with open(os.path.join(out, 'layers.json'), 'w', encoding='utf-8') as fh:
        json.dump(dict(ppu=MAP_PPU, map=name, layout='map-layout.md b3c88ca', layers=layers, fx=cfg['fx']()), fh, indent=1)
    with open(os.path.join(out, 'map.json'), 'w', encoding='utf-8') as fh:
        json.dump(meta, fh, indent=2)
    print('REPORT', json.dumps(report))
    preview(name, cfg, out, imgs, meta)


# ---------------------------------------------------------------- check image

def preview(name, cfg, out, imgs, meta):
    base = np.zeros_like(imgs['far'])
    over(base, imgs['far'], 0, 0)
    for key in ('mid', 'attach'):
        L = json.load(open(os.path.join(out, 'layers.json'), encoding='utf-8'))
        r = [l for l in L['layers'] if l['name'] == key][0]['rect']
        over(base, load_png(os.path.join(out, '%s_%s.png' % (name, key))),
             round((r[0] - BG_RECT[0]) * MAP_PPU), round((r[1] - BG_RECT[1]) * MAP_PPU))

    def put(img, x0, y0):
        over(base, img, round((x0 - BG_RECT[0]) * MAP_PPU), round((y0 - BG_RECT[1]) * MAP_PPU))
    s = MAP_PPU / CHAR_PPU

    def put_sprite(fname, x, y, piv, frames=1, frame=0):
        img = load_png(os.path.join(out, fname))
        if frames > 1:
            fw = img.shape[1] // frames
            img = img[:, fw * frame:fw * (frame + 1)].copy()
        img = bs.resize(img, round(img.shape[1] * s), round(img.shape[0] * s))
        put(img, x - piv[0] * s / MAP_PPU, y - piv[1] * s / MAP_PPU)

    if name == 'dessert':      # first-layout ice creams stretched to the new widths (9-slice) as the client will
        put(load_png(os.path.join(bs.SPRITES, 'map_dessert', 'dessert_floor.png')), -1.5, -1.5)
        for fname, cx, w, top in (('icecream_strawberry.png', 4.0, 5.0, 2.4), ('icecream_vanilla.png', 8.5, 6.0, 4.8),
                                  ('icecream_chocolate.png', 12.0, 3.0, 7.2), ('icecream_mint.png', 21.0, 4.0, 2.4)):
            src = load_png(os.path.join(bs.SPRITES, 'map_dessert', fname))
            W = round((w + 0.5) * MAP_PPU)
            mid = bs.resize(src[:, 54:-54].copy(), W - 108, src.shape[0])
            img = np.concatenate([src[:, :54], mid, src[:, -54:]], axis=1)
            put(img, cx - (w + 0.5) / 2, top + 0.25 - src.shape[0] / MAP_PPU)
        col = load_png(os.path.join(out, 'cake_lift_column.png'))
        for k in range(1):
            put(col, 16.95, 0.0)
        put(load_png(os.path.join(out, 'syrup.png')), 0.5, -0.05)
    for fname, p in meta['pieces'].items():
        if fname.endswith('_bg.png'):
            continue
        for r in p.get('instances', [p['world_rect']]):
            put(load_png(os.path.join(out, fname)), r[0], r[1])
    for fname, p in meta['moving'].items():
        for r in p['start_world_rects']:
            put(load_png(os.path.join(out, fname)), r[0], r[1])
    if name == 'kitchen':
        for a in (10.0, 17.0):
            put_sprite('burner_flame.png', a + 1.5, 0.0, (192, 0), 4, 1)
        put_sprite('burner_glow.png', 15.0, 0.0, (192, 0))
        put_sprite('butter.png', 7.0, 0.0, (77, 0))
    if name == 'warehouse':
        belt = load_png(os.path.join(out, 'conveyor_belt.png'))
        for k in range(13):
            put(belt, 6.0 + k, -0.25)
    if name == 'winter':
        put_sprite('snow_dump.png', 20.0, 4.5, (128, 256))
        for k, (x, y) in enumerate(((4.0, 6.0), (9.0, 8.5), (13.0, 5.0), (17.0, 11.0))):
            put_sprite('wind_streak.png', x, y, (128, 6))
    # a few characters for scale on the new platforms
    cdir = os.path.join(bs.SPRITES, 'characters')
    cmeta = json.load(open(os.path.join(cdir, 'jelly_sheet.json'), encoding='utf-8'))
    rows = len(cmeta['animations'])
    sheets = {int(k): load_png(os.path.join(cdir, v)) for k, v in cmeta['sheets'].items()}
    cast = dict(kitchen=[(1, 2.5, 7.2), (2, 7.0, 9.6), (3, 11.5, 4.8), (4, 18.0, 7.2), (5, 14.0, 2.4)],
                winter=[(1, 3.0, 2.4), (2, 8.5, 4.8), (3, 16.2, 9.6), (4, 21.0, 7.2), (5, 12.5, 7.2)],
                warehouse=[(1, 3.0, 1.2), (2, 4.0, 6.0), (3, 8.5, 8.4), (4, 21.0, 7.2), (5, 12.0, 0.0)],
                dessert=[(1, 4.0, 2.4), (2, 8.5, 4.8), (3, 14.8, 9.6), (4, 20.5, 7.2), (5, 17.2, 1.2)])[name]
    for pid, x, y in cast:
        fr = bs._frame(sheets[pid], 0, 0, rows)
        img = bs.resize(fr, round(fr.shape[1] * s), round(fr.shape[0] * s))
        put(img, x - bs.CHAR_PIVOT[0] * s / MAP_PPU, y - bs.CHAR_PIVOT[1] * s / MAP_PPU)
    front = imgs['front']
    over(base, front, 0, 0)
    save_png(base, os.path.join(bs.PREVIEW, name + '_layout2.png'))


TARGETS = {k: (lambda k=k: target_map(k)) for k in MAPS2}


def main():
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    target = args[0] if args else 'layout2'
    if target == 'layout2':
        for t in MAPS2:
            subprocess.run([bpy.app.binary_path, '-b', '--factory-startup', '--python', os.path.abspath(__file__),
                            '--', t], check=True)
        return
    TARGETS[target]()


if __name__ == '__main__':
    main()
