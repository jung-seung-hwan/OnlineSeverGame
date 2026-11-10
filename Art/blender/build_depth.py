"""
Map depth layers and moving background (docs/work/map-visual.md).

Run from the repository root (Blender 5.2):
  blender -b --factory-startup --python Art/blender/build_depth.py -- depth
Targets: depth (all four maps + fx sprites + check images) | kitchen | winter | warehouse | dessert | fx

Each map keeps its existing pieces (build_sprites.py, map.json) untouched and gets, in the same folder:
  <map>_far.png     far background: the old <map>_bg minus everything tied to a platform, slightly fogged
  <map>_attach.png  things fixed to the play plane (platform chains, rails, brackets, sill windows); no parallax
  <map>_mid.png     new middle layer: props that fill the empty upper half (shelves, racks, garlands ...)
  <map>_front.png   blurred dark silhouettes standing in the margins outside the walls (x < 0, x > 24)
  layers.json       rect / parallax depth / sort order of each layer + the moving-background (fx) list
Shared fx sprites (128 px per unit, centre pivot): Art/sprites/map_fx/fx_*.png.
<map>_far + <map>_attach together equal the old <map>_bg, so a client without layers.json still works.

The script builds the map scene with build_sprites.build_* (same models), moves the platform-tied objects to
their own collection, adds the middle / front props, and renders each collection at 72 px per unit.
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
import build_sprites as bs  # noqa: E402  (module guards its main())
from build_sprites import (XZ, BG_RECT, MAP_PPU, CHAR_PPU, _box, add_prism, add_tube, ellipse_pts,  # noqa: E402
                           flat_mat, load_png, save_png, over, frame_camera, render_to, reset_scene, add_camera,
                           add_sun, MapKit)

FX_DIR = os.path.join(bs.SPRITES, 'map_fx')
MAPS = {   # name -> sprite folder, build, platform-tied objects, play collections for the check image, fog
    'kitchen': dict(folder='map', attach=('Rail', 'Chains', 'Utensils'),
                    play=('FLOOR', 'SHELF', 'LID'), fog=('#1A1E28', 0.10)),
    'winter': dict(folder='map_winter', attach=('Window0', 'Window1', 'BeamBrackets'),
                   play=('FLOOR', 'SILL', 'BENCH', 'BEAM'), fog=('#1A2030', 0.10)),
    'warehouse': dict(folder='map_warehouse', attach=('Rail', 'Chains', 'Columns'),
                      play=('FLOOR', 'RACK', 'PALLET'), fog=('#171B24', 0.10)),
    'dessert': dict(folder='map_dessert', attach=('Molding',),
                    play=('FLOOR', 'SCOOP0', 'SCOOP1', 'SCOOP2', 'SCOOP3'), fog=('#221C26', 0.12)),
}
# parallax: layer offset = virtual camera offset * depth (1 = stays with the camera, 0 = play plane, < 0 nearer)
FOCUS_RANGE = 0.8   # client BackdropView.FocusRange: virtual camera offset when my jelly is at a screen edge
LAYERS = [('far', -100, 0.5), ('mid', -95, 0.25), ('attach', -90, 0.0), ('front', 35, -0.6)]


# ---------------------------------------------------------------- small geometry helpers (flat, like the bg)

def rect(bm, x0, z0, x1, z1, y, mat=0, d=0.04):
    _box(bm, x0, z0, x1, z1, y, y + d, mat)


def poly(bm, pts, y, mat=0, d=0.04):
    add_prism(bm, pts, y, y + d, XZ, mat)


def disc(bm, cx, cz, r, y, mat=0, rz=None, n=32, a0=0.0, a1=2 * math.pi):
    poly(bm, ellipse_pts(cx, cz, r, rz or r, n, a0, a1), y, mat)


def ring(bm, cx, cz, r0, r1, y, mat=0, n=40):
    for k in range(n):
        a, b = 2 * math.pi * k / n, 2 * math.pi * (k + 1) / n
        poly(bm, [(cx + r0 * math.cos(a), cz + r0 * math.sin(a)), (cx + r1 * math.cos(a), cz + r1 * math.sin(a)),
                  (cx + r1 * math.cos(b), cz + r1 * math.sin(b)), (cx + r0 * math.cos(b), cz + r0 * math.sin(b))],
             y, mat)


def rrect_pts(x0, z0, x1, z1, r, n=5):
    pts = []
    for cx, cz, a in ((x1 - r, z0 + r, -90), (x1 - r, z1 - r, 0), (x0 + r, z1 - r, 90), (x0 + r, z0 + r, 180)):
        pts += [(cx + r * math.cos(math.radians(a + 90 * i / n)), cz + r * math.sin(math.radians(a + 90 * i / n)))
                for i in range(n + 1)]
    return pts


def lit(bm, build, y, base=0, edge=1, off=0.028):
    """Silhouette with a thin lighter rim on the upper left: the shape in the edge colour slightly up-left and
    behind, then the shape itself in the base colour."""
    build(bm, y + 0.02, edge, (-off, off))
    build(bm, y, base, (0.0, 0.0))


def move_to(kit, names, coll):
    target = kit.coll(coll)
    for n in names:
        ob = bpy.data.objects.get(n)
        if ob is None:
            raise KeyError('object %s not found' % n)
        for c in list(ob.users_collection):
            c.objects.unlink(ob)
        target.objects.link(ob)


# ---------------------------------------------------------------- kitchen

def mid_kitchen(kit):
    m = [flat_mat('KMid', '#1B1E26'), flat_mat('KEdge', '#363B48'), flat_mat('KJar', '#232B33'),
         flat_mat('KJarFill', '#3A3330'), flat_mat('KLid', '#3A3F4A'), flat_mat('KFace', '#272B35'),
         flat_mat('KHand', '#4A505C'), flat_mat('KGreen', '#1F2A26')]
    # spice shelf with jars, a bottle and a small pot (steam source at 8.35, 10.1); no bright top line
    def shelf(bm, y, mat, o):
        dx, dz = o
        rect(bm, 6.3 + dx, 9.48 + dz, 9.0 + dx, 9.6 + dz, y, mat)
        for bx in (6.7, 8.6):
            poly(bm, [(bx - 0.05 + dx, 9.48 + dz), (bx + 0.05 + dx, 9.48 + dz), (bx + 0.05 + dx, 9.2 + dz)], y, mat)
        for x0, x1, h in ((6.45, 6.85, 0.5), (6.98, 7.34, 0.38)):
            poly(bm, rrect_pts(x0 + dx, 9.6 + dz, x1 + dx, 9.6 + h + dz, 0.06), y, mat)
        poly(bm, [(7.5 + dx, 9.6 + dz), (7.76 + dx, 9.6 + dz), (7.76 + dx, 10.05 + dz), (7.68 + dx, 10.15 + dz),
                  (7.68 + dx, 10.32 + dz), (7.58 + dx, 10.32 + dz), (7.58 + dx, 10.15 + dz), (7.5 + dx, 10.05 + dz)],
             y, mat)
        poly(bm, rrect_pts(7.95 + dx, 9.6 + dz, 8.75 + dx, 9.98 + dz, 0.05), y, mat)
        rect(bm, 7.86 + dx, 9.86 + dz, 8.84 + dx, 9.92 + dz, y, mat)
        disc(bm, 8.35 + dx, 10.02 + dz, 0.05, y, mat, n=12)
    kit.obj('SpiceShelf', 'MID', m, lambda bm: lit(bm, shelf, 2.4))
    kit.obj('SpiceFill', 'MID', m, lambda bm: (
        rect(bm, 6.5, 9.65, 6.8, 9.95, 2.38, 3), rect(bm, 7.03, 9.65, 7.29, 9.85, 2.38, 3),
        rect(bm, 6.45, 10.04, 6.85, 10.1, 2.38, 4), rect(bm, 6.98, 9.92, 7.34, 9.98, 2.38, 4)))
    # pot rack: its own bar at 12.25 (not the play-plane rail), pan, whisk, ladle, saucepan
    def rack(bm, y, mat, o):
        dx, dz = o
        rect(bm, 9.9 + dx, 12.2 + dz, 14.1 + dx, 12.3 + dz, y, mat)
        for sx in (10.1, 13.9):
            rect(bm, sx - 0.04 + dx, 12.3 + dz, sx + 0.04 + dx, 13.2 + dz, y, mat)
        rect(bm, 10.58 + dx, 11.62 + dz, 10.66 + dx, 12.2 + dz, y, mat)
        disc(bm, 10.62 + dx, 11.22 + dz, 0.42, y, mat)
        rect(bm, 11.51 + dx, 11.72 + dz, 11.59 + dx, 12.2 + dz, y, mat)
        poly(bm, [(11.55 + dx + 0.24 * math.sin(a) * (1 - 0.5 * (math.cos(a) + 1) / 2) * 1.4,
                   11.36 + dz + 0.38 * math.cos(a)) for a in (2 * math.pi * k / 28 for k in range(28))], y, mat)
        rect(bm, 12.41 + dx, 11.32 + dz, 12.49 + dx, 12.2 + dz, y, mat)
        disc(bm, 12.45 + dx, 11.16 + dz, 0.21, y, mat, a0=math.pi, a1=2 * math.pi + 0.001, n=20)
        rect(bm, 12.24 + dx, 11.14 + dz, 12.66 + dx, 11.2 + dz, y, mat)
        poly(bm, [(13.36 + dx, 12.2 + dz), (13.44 + dx, 12.2 + dz), (13.62 + dx, 11.5 + dz), (13.54 + dx, 11.5 + dz)],
             y, mat)
        poly(bm, rrect_pts(13.0 + dx, 10.95 + dz, 13.85 + dx, 11.52 + dz, 0.06), y, mat)
    kit.obj('PotRack', 'MID', m, lambda bm: lit(bm, rack, 2.2))
    kit.obj('WhiskHole', 'MID', m, lambda bm: poly(bm, [
        (11.55 + 0.15 * math.sin(a) * (1 - 0.5 * (math.cos(a) + 1) / 2) * 1.4, 11.38 + 0.28 * math.cos(a))
        for a in (2 * math.pi * k / 24 for k in range(24))], 2.18, 5))
    # wall clock above the shelf
    kit.obj('Clock', 'MID', m, lambda bm: (
        disc(bm, 7.65, 11.75, 0.44, 2.4, 1), disc(bm, 7.65, 11.75, 0.4, 2.38, 0), disc(bm, 7.65, 11.75, 0.33, 2.36, 5),
        poly(bm, [(7.63, 11.75), (7.67, 11.75), (7.67, 12.0), (7.63, 12.0)], 2.34, 6),
        poly(bm, [(7.65, 11.73), (7.85, 11.66), (7.86, 11.7), (7.66, 11.77)], 2.34, 6)))
    # dish rack with standing plates (round, no level line) under the spice shelf
    def dishes(bm, y, mat, o):
        dx, dz = o
        rect(bm, 6.35 + dx, 7.05 + dz, 8.95 + dx, 7.17 + dz, y, mat)
        for k in range(6):
            disc(bm, 6.75 + 0.36 * k + dx, 7.62 + dz, 0.5 - 0.03 * (k % 2), y - 0.004 * k, mat, rz=0.5, n=28)
        rect(bm, 6.35 + dx, 7.05 + dz, 6.43 + dx, 7.75 + dz, y - 0.05, mat)
        rect(bm, 8.87 + dx, 7.05 + dz, 8.95 + dx, 7.75 + dz, y - 0.05, mat)
    kit.obj('Dishes', 'MID', m, lambda bm: (lit(bm, dishes, 2.4),
                                            [ring(bm, 6.75 + 0.36 * k, 7.62, 0.28, 0.31, 2.3 - 0.004 * k, 5, 28)
                                             for k in range(6)]))
    # round cutting board and rolling pin hanging on the right wall
    def board(bm, y, mat, o):
        dx, dz = o
        disc(bm, 23.2 + dx, 7.1 + dz, 0.62, y, mat, n=32)
        poly(bm, rrect_pts(23.05 + dx, 7.6 + dz, 23.35 + dx, 8.25 + dz, 0.1), y, mat)
        poly(bm, rrect_pts(21.9 + dx, 6.2 + dz, 22.12 + dx, 7.9 + dz, 0.1), y, mat)
        rect(bm, 21.97 + dx, 7.9 + dz, 22.05 + dx, 8.2 + dz, y, mat)
    kit.obj('Board', 'MID', m, lambda bm: (lit(bm, board, 2.4), disc(bm, 23.2, 8.02, 0.07, 2.38, 5, n=12),
                                           ring(bm, 23.2, 7.1, 0.5, 0.53, 2.38, 5)))
    # wall cabinet right of the hood chimney
    def cab(bm, y, mat, o):
        dx, dz = o
        rect(bm, 21.5 + dx, 11.55 + dz, 25.6 + dx, 12.85 + dz, y, mat)
    kit.obj('Cabinet', 'MID', m, lambda bm: (
        lit(bm, cab, 2.4), rect(bm, 23.52, 11.6, 23.58, 12.8, 2.38, 1),
        rect(bm, 23.3, 11.9, 23.36, 12.25, 2.36, 6), rect(bm, 23.74, 11.9, 23.8, 12.25, 2.36, 6)))


def front_kitchen(kit):
    m = [flat_mat('KFront', '#0B0C10'), flat_mat('KFront2', '#121318')]
    kit.obj('Front', 'FRONT', m, lambda bm: (
        poly(bm, rrect_pts(-1.6, -1.6, -0.35, 1.25, 0.18), -2.0),                    # pepper mill
        poly(bm, [(-1.35, 1.25), (-0.6, 1.25), (-0.8, 1.6), (-1.15, 1.6)], -2.0),
        disc(bm, -0.98, 1.7, 0.12, -2.0),
        poly(bm, rrect_pts(-0.3, -1.6, 0.6, -0.2, 0.1), -2.1, 1),                     # jar, below the floor top
        disc(bm, 24.6, -0.4, 1.25, -2.0, rz=0.7, a0=0.0, a1=math.pi, n=24),            # mixing bowl rim
        poly(bm, [(23.35, -0.4), (25.85, -0.4), (25.85, -1.6), (23.35, -1.6)], -2.0),
        poly(bm, [(24.4, 0.2), (24.52, 0.2), (24.9, 1.5), (24.78, 1.52)], -2.1, 1)))  # whisk handle


def fx_kitchen():
    return [
        dict(kind='steam', layer='mid', at=[8.35, 10.1], size=0.34, count=7, rise=1.7, period=4.2,
             color='#9AA3B5', alpha=[0.0, 0.15]),
        dict(kind='glow', layer='far', at=[20.0, 9.0], size=2.6, color='#F0C27A', alpha=[0.03, 0.07], period=3.4),
        dict(kind='glow', layer='far', at=[20.0, 9.28], size=0.9, color='#F6D49A', alpha=[0.05, 0.1], period=3.4),
    ]


# ---------------------------------------------------------------- winter eaves

def mid_winter(kit):
    m = [flat_mat('WMid', '#1A1F2A'), flat_mat('WEdge', '#344056'), flat_mat('WFruit', '#432E25'),
         flat_mat('WFruitLit', '#56392C'), flat_mat('WCalyx', '#22261F'), flat_mat('WLeaf', '#1C2A27'),
         flat_mat('WBerry', '#4A2A30'), flat_mat('WRope', '#2A2B30')]
    # drying pole with persimmon strings (ropes run up behind the eave)
    def pole(bm, y, mat, o):
        dx, dz = o
        rect(bm, 9.3 + dx, 12.1 + dz, 14.7 + dx, 12.22 + dz, y, mat)
        for rx in (9.6, 14.4):
            poly(bm, [(rx - 0.02 + dx, 12.22 + dz), (rx + 0.02 + dx, 12.22 + dz), (rx + 0.32 + dx, 13.3 + dz),
                      (rx + 0.28 + dx, 13.3 + dz)] if rx < 12 else
                 [(rx - 0.02 + dx, 12.22 + dz), (rx + 0.02 + dx, 12.22 + dz), (rx - 0.28 + dx, 13.3 + dz),
                  (rx - 0.32 + dx, 13.3 + dz)], y, mat)
    kit.obj('DryPole', 'MID', m, lambda bm: (lit(bm, pole, 2.4),
                                             [rect(bm, nx, 12.1, nx + 0.04, 12.22, 2.38, 1) for nx in (10.5, 11.9, 13.3)]))
    rnd = random.Random(21)
    strings = [(10.2, 5), (11.4, 4), (12.6, 5), (13.8, 4)]
    kit.obj('Persimmons', 'MID', m, lambda bm: [
        (rect(bm, sx - 0.012, 12.1 - 0.3 * n - 0.05, sx + 0.012, 12.1, 2.3, 7),
         [(disc(bm, sx + rnd.uniform(-0.03, 0.03), 11.92 - 0.3 * k, 0.14, 2.28, 2, rz=0.13, n=20),
           disc(bm, sx - 0.04, 11.96 - 0.3 * k, 0.06, 2.26, 3, n=10),
           poly(bm, [(sx - 0.07, 12.03 - 0.3 * k), (sx + 0.07, 12.03 - 0.3 * k), (sx, 12.09 - 0.3 * k)], 2.25, 4))
          for k in range(n)]) for sx, n in strings])
    # wreath
    kit.obj('Wreath', 'MID', m, lambda bm: (
        ring(bm, 12.0, 8.6, 0.36, 0.6, 2.4, 5),
        [disc(bm, 12.0 + 0.48 * math.cos(a), 8.6 + 0.48 * math.sin(a), 0.07, 2.38, 6, n=10)
         for a in (2 * math.pi * k / 9 + 0.3 for k in range(9))],
        poly(bm, [(11.78, 8.04), (12.0, 8.16), (12.22, 8.04), (12.22, 8.26), (12.0, 8.16), (11.78, 8.26)], 2.36, 6)))
    # straw hat on a nail (left) and a small wooden sled hanging on the right wall
    def hat(bm, y, mat, o):
        dx, dz = o
        disc(bm, 3.9 + dx, 8.15 + dz, 0.85, y, mat, rz=0.2, n=28)
        disc(bm, 3.9 + dx, 8.25 + dz, 0.42, y, mat, rz=0.42, a0=0.0, a1=math.pi, n=20)
        poly(bm, [(3.82 + dx, 8.62 + dz), (3.98 + dx, 8.62 + dz), (3.9 + dx, 8.8 + dz)], y, mat)

    def sled(bm, y, mat, o):
        dx, dz = o
        poly(bm, rrect_pts(19.6 + dx, 7.55 + dz, 21.4 + dx, 8.0 + dz, 0.12), y, mat)
        poly(bm, [(19.35 + dx, 7.2 + dz), (21.45 + dx, 7.2 + dz), (21.6 + dx, 7.42 + dz), (21.5 + dx, 7.3 + dz),
                  (19.4 + dx, 7.3 + dz)], y, mat)
        for lx in (19.8, 21.1):
            rect(bm, lx + dx, 7.3 + dz, lx + 0.08 + dx, 7.55 + dz, y, mat)
        rect(bm, 20.47 + dx, 8.0 + dz, 20.53 + dx, 8.55 + dz, y, mat)
    kit.obj('HatSled', 'MID', m, lambda bm: (
        lit(bm, hat, 2.4), lit(bm, sled, 2.4),
        [rect(bm, sx, 7.6, sx + 0.04, 7.95, 2.38, 1) for sx in (20.0, 20.4, 20.8)]))
    # wall arms for the two swaying lanterns (fx, hang points 7.75 / 16.25, 10.86)
    def arms(bm, y, mat, o):
        dx, dz = o
        for x0, s in ((7.3, 1), (16.7, -1)):
            rect(bm, min(x0, x0 + s * 0.5) + dx, 10.86 + dz, max(x0, x0 + s * 0.5) + dx, 10.94 + dz, y, mat)
            poly(bm, [(x0 + dx, 10.94 + dz), (x0 + dx, 10.5 + dz), (x0 + s * 0.3 + dx, 10.86 + dz)], y, mat)
            rect(bm, x0 - 0.06 + dx, 10.4 + dz, x0 + 0.06 + dx, 11.05 + dz, y, mat)
    kit.obj('LanternArms', 'MID', m, lambda bm: lit(bm, arms, 2.4))


def front_winter(kit):
    m = [flat_mat('WFront', '#0B0D12'), flat_mat('WFrontSnow', '#1A2130')]
    kit.obj('Front', 'FRONT', m, lambda bm: (
        [poly(bm, [(-1.7, z - 0.9), (-0.2 - 0.15 * k, z - 0.65), (-1.7, z)], -2.0) for k, z in
         enumerate((2.6, 1.8, 1.0, 0.2))],                                            # pine bough tips
        [poly(bm, [(-1.7, z), (-0.2 - 0.15 * k, z - 0.65), (-0.45 - 0.15 * k, z - 0.62), (-1.7, z - 0.14)],
              -2.05, 1) for k, z in enumerate((2.6, 1.8, 1.0, 0.2))],
        poly(bm, rrect_pts(24.25, -1.6, 25.05, 1.7, 0.05), -2.0),                    # fence post + snow cap
        disc(bm, 24.65, 1.72, 0.48, -2.05, 1, rz=0.2, a0=0.0, a1=math.pi, n=16),
        disc(bm, 24.4, -0.6, 1.6, -2.1, 1, rz=0.55, a0=0.0, a1=math.pi, n=24)))       # snow mound under the floor top


def fx_winter():
    return [
        dict(kind='snow', layer='far', area=[-1.5, -0.6, 25.5, 13.2], count=70, size=0.07, speed=[0.35, 0.6],
             color='#8FA3C8', alpha=[0.28, 0.42]),
        dict(kind='snow', layer='mid', area=[-1.5, -0.6, 25.5, 13.2], count=26, size=0.11, speed=[0.7, 1.1],
             color='#9DB0D2', alpha=[0.38, 0.52]),
        dict(kind='sway', layer='mid', sprite='map_winter/lantern', pivot=[48, 160], at=[7.75, 10.86], scale=0.72,
             color='#B4B4BC', glow='#E2AE68', glowAt=[0.0, -0.72], amp=5.0, period=3.6),
        dict(kind='sway', layer='mid', sprite='map_winter/lantern', pivot=[48, 160], at=[16.25, 10.86], scale=0.72,
             color='#B4B4BC', glow='#E2AE68', glowAt=[0.0, -0.72], amp=5.0, period=4.1),
        dict(kind='glow', layer='attach', at=[4.0, 3.9], size=1.9, color='#E0904A', alpha=[0.04, 0.10], period=1.7,
             flicker=0.35),
        dict(kind='glow', layer='attach', at=[20.0, 3.9], size=1.9, color='#E0904A', alpha=[0.04, 0.10], period=1.9,
             flicker=0.35),
    ]


# ---------------------------------------------------------------- warehouse

def mid_warehouse(kit):
    m = [flat_mat('HMid', '#1B2029'), flat_mat('HEdge', '#353D4C'), flat_mat('HTube', '#39404D'),
         flat_mat('HGrill', '#14181E'), flat_mat('HSign', '#3A3426'), flat_mat('HSignDark', '#24221E'),
         flat_mat('HGauge', '#2E3440')]
    # two hanging fluorescent fixtures, cables go up behind the rail
    def tubes(bm, y, mat, o):
        dx, dz = o
        for x0 in (7.4, 14.8):
            for cx in (x0 + 0.25, x0 + 1.55):
                rect(bm, cx - 0.015 + dx, 11.95 + dz, cx + 0.015 + dx, 14.5 + dz, y, mat)
            poly(bm, [(x0 + dx, 11.95 + dz), (x0 + 1.8 + dx, 11.95 + dz), (x0 + 1.7 + dx, 11.7 + dz),
                      (x0 + 0.1 + dx, 11.7 + dz)], y, mat)
    kit.obj('Tubes', 'MID', m, lambda bm: (lit(bm, tubes, 2.4),
                                           [rect(bm, x0 + 0.12, 11.6, x0 + 1.68, 11.7, 2.38, 2) for x0 in (7.4, 14.8)]))
    # wall vent fan housing (the blades are an fx sprite turning at 12.0, 7.6)
    def vent(bm, y, mat, o):
        dx, dz = o
        poly(bm, rrect_pts(11.15 + dx, 6.75 + dz, 12.85 + dx, 8.45 + dz, 0.12), y, mat)
    kit.obj('Vent', 'MID', m, lambda bm: (lit(bm, vent, 2.4), disc(bm, 12.0, 7.6, 0.72, 2.38, 3),
                                          [disc(bm, 12.0 + 0.66 * sx, 7.6 + 0.66 * sz, 0.05, 2.36, 1, n=10)
                                           for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]))
    # pipe run under the rail with a drop to a pressure gauge
    def pipe(bm, y, mat, o):
        dx, dz = o
        rect(bm, -2.0 + dx, 12.5 + dz, 26.0 + dx, 12.64 + dz, y, mat)
        rect(bm, 23.83 + dx, 9.5 + dz, 23.97 + dx, 12.5 + dz, y, mat)
        for bx in [0.5 + 3.0 * k for k in range(9)]:
            rect(bm, bx + dx, 12.46 + dz, bx + 0.12 + dx, 12.68 + dz, y, mat)
    kit.obj('Pipes', 'MID', m, lambda bm: (lit(bm, pipe, 2.5), disc(bm, 23.9, 9.32, 0.24, 2.48, 1),
                                           disc(bm, 23.9, 9.32, 0.2, 2.46, 6),
                                           poly(bm, [(23.89, 9.32), (23.91, 9.32), (24.02, 9.46), (24.0, 9.47)],
                                                2.44, 3)))
    # dim hazard-stripe plate under the wall lamp
    kit.obj('Plate', 'MID', m, lambda bm: (
        rect(bm, 11.1, 9.35, 12.9, 9.8, 2.4, 4),
        [poly(bm, [(sx, 9.35), (sx + 0.18, 9.35), (sx + 0.36, 9.8), (sx + 0.18, 9.8)], 2.38, 5)
         for sx in (11.15, 11.55, 11.95, 12.35)],
        rect(bm, 11.1, 9.35, 12.9, 9.8, 2.42, 1)))


def front_warehouse(kit):
    m = [flat_mat('HFront', '#0B0C10'), flat_mat('HFront2', '#14161C')]
    kit.obj('Front', 'FRONT', m, lambda bm: (
        poly(bm, rrect_pts(-1.75, -1.6, -0.15, 1.45, 0.12), -2.0),                   # oil drum
        [rect(bm, -1.75, z, -0.15, z + 0.08, -2.05, 1) for z in (-0.4, 0.55, 1.3)],
        poly(bm, [(24.15, -1.6), (25.8, -1.6), (25.8, 1.25), (24.15, 1.25)], -2.0),   # crate corner
        poly(bm, [(24.15, 1.1), (25.8, -0.6), (25.8, -0.4), (24.35, 1.1)], -2.05, 1),
        rect(bm, 22.6, -1.6, 25.8, -0.25, -2.1, 1)))                                    # pallet edge under the floor top


def fx_warehouse():
    return [
        dict(kind='dust', layer='far', points=[2.1, 9.0, 4.5, 9.0, 8.4, 0.0, 5.2, 0.0], count=26, size=0.06,
             speed=[0.05, 0.16], color='#9AAACC', alpha=[0.18, 0.4]),
        dict(kind='dust', layer='far', points=[19.5, 9.0, 21.9, 9.0, 18.8, 0.0, 15.6, 0.0], count=26, size=0.06,
             speed=[0.05, 0.16], color='#9AAACC', alpha=[0.18, 0.4]),
        dict(kind='tube', layer='mid', area=[7.52, 11.6, 9.08, 11.7], color='#C9D6E4', alpha=[0.42, 0.5],
             size=1.7, period=5.0, flicker=0.0),
        dict(kind='tube', layer='mid', area=[14.92, 11.6, 16.48, 11.7], color='#C9D6E4', alpha=[0.42, 0.5],
             size=1.7, period=7.0, flicker=1.0),
        dict(kind='spin', layer='mid', sprite='map_warehouse/vent_fan', pivot=[80, 80], at=[12.0, 7.6], scale=1.0,
             color='#FFFFFF', amp=40.0),
        dict(kind='glow', layer='far', at=[12.0, 10.45], size=2.4, color='#D9AE6E', alpha=[0.05, 0.09], period=2.8),
    ]


# ---------------------------------------------------------------- dessert shop

GARLAND = [(-1.5 + 3.0 * k, -1.5 + 3.0 * (k + 1)) for k in range(9)]
GARLAND_TOP, GARLAND_SAG = 13.28, 0.34


def garland_points():
    pts = []
    for a, b in GARLAND:
        for i in range(1, 4):
            t = i / 4
            pts.append((a + (b - a) * t, GARLAND_TOP - 4 * GARLAND_SAG * t * (1 - t) - 0.1))
    return pts


def mid_dessert(kit):
    m = [flat_mat('DMid', '#211B24'), flat_mat('DEdge', '#3C3242'), flat_mat('DGlass', '#2C2A39'),
         flat_mat('DCake', '#3E3236'), flat_mat('DCream', '#4C4249'), flat_mat('DMac', '#3E4A44'),
         flat_mat('DMac2', '#4A3C48'), flat_mat('DBulb', '#3A3440'), flat_mat('DWire', '#1A161C')]
    # wall display cabinet with cakes, a domed cake on top
    def cab(bm, y, mat, o):
        dx, dz = o
        rect(bm, 5.3 + dx, 8.85 + dz, 10.3 + dx, 11.15 + dz, y, mat)
        poly(bm, [(7.15 + dx, 11.15 + dz), (8.45 + dx, 11.15 + dz), (8.3 + dx, 11.22 + dz), (7.3 + dx, 11.22 + dz)],
             y, mat)
        disc(bm, 7.8 + dx, 11.22 + dz, 0.5, y, mat, rz=0.55, a0=0.0, a1=math.pi, n=20)
    kit.obj('Cabinet', 'MID', m, lambda bm: (
        lit(bm, cab, 2.5),
        rect(bm, 5.45, 9.0, 10.15, 11.0, 2.48, 2),
        rect(bm, 5.45, 9.92, 10.15, 9.98, 2.46, 1),
        poly(bm, rrect_pts(5.7, 9.0, 6.7, 9.5, 0.08), 2.44, 3), rect(bm, 5.7, 9.42, 6.7, 9.52, 2.42, 4),
        [poly(bm, [(cx - 0.2, 9.0), (cx + 0.2, 9.0), (cx + 0.24, 9.22), (cx - 0.24, 9.22)], 2.44, 3)
         for cx in (7.3, 7.95)], [disc(bm, cx, 9.3, 0.17, 2.42, 4, rz=0.12, n=16) for cx in (7.3, 7.95)],
        poly(bm, rrect_pts(8.6, 9.0, 9.9, 9.32, 0.14), 2.44, 3), ring(bm, 9.25, 9.16, 0.04, 0.12, 2.42, 4, 16),
        [disc(bm, 5.85 + 0.42 * k, 10.13, 0.15, 2.44, 5 + k % 2, rz=0.11, n=16) for k in range(5)],
        poly(bm, [(8.25, 9.98), (9.95, 9.98), (9.75, 10.3), (8.45, 10.3)], 2.44, 3),
        disc(bm, 7.8, 11.22, 0.38, 2.46, 2, rz=0.42, a0=0.0, a1=math.pi, n=20),
        poly(bm, rrect_pts(7.5, 11.22, 8.1, 11.48, 0.05), 2.44, 3)))
    # candy jar shelf right of the menu board
    def shelf(bm, y, mat, o):
        dx, dz = o
        rect(bm, 20.55 + dx, 8.78 + dz, 23.45 + dx, 8.9 + dz, y, mat)
        for x0, x1, h in ((20.75, 21.35, 0.62), (21.6, 22.3, 0.78), (22.55, 23.2, 0.55)):
            poly(bm, rrect_pts(x0 + dx, 8.9 + dz, x1 + dx, 8.9 + h + dz, 0.12), y, mat)
            rect(bm, x0 + 0.08 + dx, 8.9 + h + dz, x1 - 0.08 + dx, 9.0 + h + dz, y, mat)
    rnd = random.Random(44)
    kit.obj('Jars', 'MID', m, lambda bm: (
        lit(bm, shelf, 2.5),
        [rect(bm, x0 + 0.06, 8.95, x1 - 0.06, 8.9 + h - 0.06, 2.48, 2) for x0, x1, h in
         ((20.75, 21.35, 0.62), (21.6, 22.3, 0.78), (22.55, 23.2, 0.55))],
        [disc(bm, rnd.uniform(x0 + 0.14, x1 - 0.14), rnd.uniform(9.02, 8.9 + h * 0.6), 0.07, 2.46,
              rnd.choice((3, 5, 6)), n=10)
         for x0, x1, h in ((20.75, 21.35, 0.62), (21.6, 22.3, 0.78), (22.55, 23.2, 0.55)) for _ in range(9)]))
    # fairy-light garland under the molding; bulbs dark here, lit by the fx
    def wire(bm):
        for a, b in GARLAND:
            pts = [(a + (b - a) * i / 16, GARLAND_TOP - 4 * GARLAND_SAG * (i / 16) * (1 - i / 16)) for i in range(17)]
            for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
                poly(bm, [(x0, z0 - 0.012), (x1, z1 - 0.012), (x1, z1 + 0.012), (x0, z0 + 0.012)], 2.3, 8)
            disc(bm, a, GARLAND_TOP, 0.05, 2.28, 1, n=10)
        for x, z in garland_points():
            rect(bm, x - 0.025, z + 0.06, x + 0.025, z + 0.1, 2.27, 8)
            disc(bm, x, z, 0.06, 2.26, 7, rz=0.085, n=12)
    kit.obj('Garland', 'MID', m, wire)


def front_dessert(kit):
    m = [flat_mat('DFront', '#0C0B0F'), flat_mat('DFront2', '#16131A')]
    kit.obj('Front', 'FRONT', m, lambda bm: (
        poly(bm, rrect_pts(-1.6, -1.6, -0.2, 1.4, 0.25), -2.0),                      # candy jar
        rect(bm, -1.45, 1.4, -0.35, 1.62, -2.0),
        [poly(bm, [(sx, 1.4), (sx + 0.08, 1.4), (sx + 0.08, 2.0), (sx + 0.0, 2.05)], -2.05, 1) for sx in (-1.2, -0.75)],
        poly(bm, [(24.1, -1.6), (25.7, -1.6), (25.9, 0.4), (23.9, 0.4)], -2.0),        # cupcake
        disc(bm, 24.9, 0.4, 1.05, -2.0, rz=0.75, a0=0.0, a1=math.pi, n=24),
        disc(bm, 24.9, 1.12, 0.14, -2.05, 1, n=12)))


def fx_dessert():
    cols = ['#F2B8C6', '#BDEBD8', '#F6E7B8']
    return [
        dict(kind='bulbs', layer='mid', points=[c for p in garland_points() for c in p], size=0.14, colors=cols,
             alpha=[0.02, 0.26], period=6.0),
        dict(kind='sparkle', layer='mid', points=[5.55, 10.9, 9.95, 10.72, 6.95, 9.78, 9.4, 9.15, 8.05, 11.58,
                                                  21.05, 9.35, 22.0, 9.55, 22.95, 9.25],
             size=0.32, color='#F4EEF8', alpha=[0.0, 0.55], period=2.6),
        dict(kind='glow', layer='far', at=[2.4, 9.75], size=1.3, color='#C8955A', alpha=[0.06, 0.1], period=4.5),
    ]


BUILD = {
    'kitchen': (None, mid_kitchen, front_kitchen, fx_kitchen),
    'winter': (bs.build_winter, mid_winter, front_winter, fx_winter),
    'warehouse': (bs.build_warehouse, mid_warehouse, front_warehouse, fx_warehouse),
    'dessert': (bs.build_dessert, mid_dessert, front_dessert, fx_dessert),
}


# ---------------------------------------------------------------- image post-processing

def hex01(h):
    h = h.lstrip('#')
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], np.float32)


def fog(arr, col, t):
    out = arr.copy()
    out[..., :3] = arr[..., :3] * (1 - t) + hex01(col) * t
    return out


def blur(arr, r):
    """Gaussian-like blur (three box passes) on premultiplied alpha."""
    pm = arr.copy()
    pm[..., :3] *= pm[..., 3:4]
    for axis in (0, 1):
        for _ in range(3):
            pad = [(0, 0)] * 3
            pad[axis] = (r + 1, r)
            c = np.cumsum(np.pad(pm, pad, mode='edge'), axis=axis)
            n = pm.shape[axis]
            hi = np.take(c, np.arange(2 * r + 1, 2 * r + 1 + n), axis=axis)
            lo = np.take(c, np.arange(0, n), axis=axis)
            pm = (hi - lo) / (2 * r + 1)
    a = pm[..., 3:4]
    pm[..., :3] = pm[..., :3] / np.maximum(a, 1e-6)
    return pm


def crop(arr, rect, thr=1.0 / 255):
    """Trim transparent borders; returns (image, world rect). Rows are bottom-up."""
    ys, xs = np.nonzero(arr[..., 3] > thr)
    if len(xs) == 0:
        return arr[:2, :2], list(rect)
    x0, x1, y0, y1 = max(0, xs.min() - 2), min(arr.shape[1], xs.max() + 3), max(0, ys.min() - 2), \
        min(arr.shape[0], ys.max() + 3)
    wx0, wy0 = rect[0] + x0 / MAP_PPU, rect[1] + y0 / MAP_PPU
    return arr[y0:y1, x0:x1].copy(), [round(wx0, 4), round(wy0, 4), round(rect[0] + x1 / MAP_PPU, 4),
                                      round(rect[1] + y1 / MAP_PPU, 4)]


# ---------------------------------------------------------------- fx sprites (numpy, supersampled, flat bands)

def ss_image(w, h, shade, k=4):
    ys, xs = np.mgrid[0:h * k, 0:w * k].astype(np.float32)
    u = ((xs + 0.5) / k - w / 2) / (w / 2)       # -1..1, centre pivot
    v = ((ys + 0.5) / k - h / 2) / (h / 2)
    rgba = shade(u, v)
    return rgba.reshape(h, k, w, k, 4).mean(axis=(1, 3))


def _rgba(cov, col):
    out = np.zeros(cov.shape + (4,), np.float32)
    out[..., :3] = col
    out[..., 3] = cov
    return out


def target_fx():
    os.makedirs(FX_DIR, exist_ok=True)

    def glow(u, v):     # three stepped bands like the wall glows
        r = np.sqrt(u * u + v * v)
        a = np.where(r < 0.3, 1.0, np.where(r < 0.6, 0.6, np.where(r < 0.95, 0.28, 0.0)))
        return _rgba(a.astype(np.float32), (1, 1, 1))

    def puff(u, v):     # cartoon steam puff: overlapping circles, lighter top
        cov = np.zeros_like(u)
        for cx, cy, r in ((0.0, -0.1, 0.55), (-0.42, -0.25, 0.4), (0.42, -0.28, 0.38), (0.12, 0.32, 0.42)):
            cov = np.maximum(cov, ((u - cx) ** 2 + (v - cy) ** 2 < r * r).astype(np.float32))
        out = _rgba(cov, (1, 1, 1))
        out[..., :3] = np.where((v < -0.3)[..., None], 0.82, 1.0)
        return out

    def flake(u, v):
        return _rgba((u * u + v * v < 0.8).astype(np.float32), (1, 1, 1))

    def sparkle(u, v):  # four-point star (astroid) with a round core
        star = np.sqrt(np.abs(u)) + np.sqrt(np.abs(v)) < 0.95
        return _rgba((star | (u * u + v * v < 0.03)).astype(np.float32), (1, 1, 1))

    def bar(u, v):      # lit tube: rounded bar
        return _rgba(((np.abs(u) < 0.94) & (np.abs(v) < 0.6) |
                      ((np.abs(u) - 0.94) ** 2 * 64 + v * v < 0.36)).astype(np.float32), (1, 1, 1))

    sprites = {'fx_glow.png': (256, 256, glow), 'fx_puff.png': (128, 128, puff), 'fx_flake.png': (32, 32, flake),
               'fx_sparkle.png': (64, 64, sparkle), 'fx_bar.png': (128, 16, bar)}
    meta = dict(ppu=CHAR_PPU, note='white sprites tinted in the client; pivot = centre', sprites={})
    for fname, (w, h, f) in sprites.items():
        save_png(ss_image(w, h, f), os.path.join(FX_DIR, fname))
        meta['sprites'][fname] = dict(size=[w, h], pivot_px_from_bottom_left=[w // 2, h // 2])
    with open(os.path.join(FX_DIR, 'fx.json'), 'w', encoding='utf-8') as fh:
        json.dump(meta, fh, indent=2)

    def fan(u, v):      # warehouse vent fan blades, dark (middle layer)
        r = np.sqrt(u * u + v * v)
        ang = np.arctan2(v, u)
        local = (ang - r * 0.9) % (math.pi / 2) - math.pi / 4
        blade = (np.abs(local) < 0.15 + 0.38 * r) & (r < 0.93) & (r > 0.12)
        hub = r < 0.22
        out = _rgba((blade | hub).astype(np.float32), hex01('#2C323C'))
        out[..., :3] = np.where(hub[..., None], hex01('#3E4552'), out[..., :3])
        edge = blade & ~hub & (local > 0.15 + 0.38 * r - 0.06)
        out[..., :3] = np.where(edge[..., None], hex01('#3A414D'), out[..., :3])
        return out
    save_png(ss_image(160, 160, fan), os.path.join(bs.SPRITES, 'map_warehouse', 'vent_fan.png'))
    print('fx sprites done')


# ---------------------------------------------------------------- per map

def target_map_depth(name):
    cfg = MAPS[name]
    build, mid, front, fxs = BUILD[name]
    scene = reset_scene()
    add_camera(scene, 0, 0, 1.0)
    add_sun(scene, (-0.5, -0.8, 0.6), energy=3.0)
    kit = MapKit(scene)
    if build is None:
        for n, c in bs.build_map(scene).items():
            kit.cols[n] = c
    else:
        build(kit)
    move_to(kit, cfg['attach'], 'BGA')
    mid(kit)
    front(kit)
    out_dir = os.path.join(bs.SPRITES, cfg['folder'])
    tmp = os.path.join(out_dir, '_tmp_layer.png')

    def shot(*colls):
        for n, c in kit.cols.items():
            c.hide_render = n not in colls
        frame_camera(scene, *BG_RECT, MAP_PPU)
        render_to(scene, tmp)
        return load_png(tmp)

    imgs = {'far': fog(shot('BG'), *cfg['fog']), 'attach': shot('BGA'), 'mid': shot('MID'),
            'front': blur(shot('FRONT'), 5)}
    imgs['front'][..., 3] *= 0.92
    play = shot(*cfg['play'])
    os.remove(tmp)
    meta = json.load(open(os.path.join(out_dir, 'map.json'), encoding='utf-8'))
    for fname, p in meta['pieces'].items():   # second instances (right shelf / sill / beam / rack)
        for inst in p.get('instances', [])[1:]:
            over(play, load_png(os.path.join(out_dir, fname)),
                 round((inst[0] - BG_RECT[0]) * MAP_PPU), round((inst[1] - BG_RECT[1]) * MAP_PPU))

    layers = []
    for key, order, depth in LAYERS:
        fname = '%s_%s.png' % (name, key)
        img, r = (imgs[key], list(BG_RECT)) if key == 'far' else crop(imgs[key], BG_RECT)
        save_png(img, os.path.join(out_dir, fname))
        layers.append(dict(name=key, file=fname[:-4], rect=r, depth=depth, order=order, size=[img.shape[1],
                                                                                                img.shape[0]]))
    out = dict(ppu=MAP_PPU, map=name, note='Depth layers and moving background, docs/work/map-visual.md. rect = '
                                           'world rect of the image (pivot bottom-left). depth = parallax factor. '
                                           'fx are drawn by the client (BackdropView).',
               layers=layers, fx=fxs())
    with open(os.path.join(out_dir, 'layers.json'), 'w', encoding='utf-8') as fh:
        json.dump(out, fh, indent=1)

    # check image: far + mid + attach + play pieces + front, and the same with every layer shifted as the
    # client does with the virtual camera at the far right
    def compose(shift):
        base = np.zeros_like(imgs['far'])
        for key, img in (('far', imgs['far']), ('mid', imgs['mid']), ('attach', imgs['attach']), ('play', play),
                         ('front', imgs['front'])):
            d = dict(far=0.5, mid=0.25, attach=0.0, play=0.0, front=-0.6)[key]
            over(base, img, round(shift * d * MAP_PPU), 0)
        return base
    save_png(compose(0.0), os.path.join(bs.PREVIEW, name + '_depth.png'))
    save_png(compose(FOCUS_RANGE), os.path.join(bs.PREVIEW, name + '_depth_shift.png'))
    print('REPORT', json.dumps(dict(map=name, layers=[(l['file'], l['size'], l['rect']) for l in layers])))


TARGETS = dict(fx=target_fx, kitchen=lambda: target_map_depth('kitchen'), winter=lambda: target_map_depth('winter'),
               warehouse=lambda: target_map_depth('warehouse'), dessert=lambda: target_map_depth('dessert'))


def main():
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    target = args[0] if args else 'depth'
    if target == 'depth':
        for t in ('fx', 'kitchen', 'winter', 'warehouse', 'dessert'):
            subprocess.run([bpy.app.binary_path, '-b', '--factory-startup', '--python', os.path.abspath(__file__),
                            '--', t], check=True)
        return
    TARGETS[target]()


if __name__ == '__main__':   # build_layout2.py imports this module
    main()
