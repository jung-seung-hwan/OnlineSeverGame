"""
Jelly kitchen sprites: builds the models in Blender and renders the sprite sheets.

Run from the repository root (Blender 5.2):
  blender -b --factory-startup --python Art/blender/build_sprites.py -- all
Targets: all | characters | knife | map | preview
  all runs characters, knife, map, preview in that order, each in its own Blender process.

Coordinates: world X = game x, world Z = game y (1 unit = 1 game unit), the orthographic camera looks
along +Y (side view, no tilt). Characters are built facing +X and turned 30 degrees toward the camera;
Unity flips the sprite for facing left. Outputs go to Art/sprites/ (sheets + json) and Art/preview/.
"""
import bpy
import bmesh
import json
import math
import os
import subprocess
import sys
import tempfile

import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ART = os.path.dirname(HERE)
SPRITES = os.path.join(ART, 'sprites')
PREVIEW = os.path.join(ART, 'preview')

CHAR_PPU = 128
CHAR_FRAME = 256            # px, square frame: 2 x 2 units
CHAR_PIVOT = (128, 8)       # px from the frame's bottom-left = body bottom centre (game position)
KNIFE_SIZE = (64, 144)      # px: 0.5 x 1.125 units
KNIFE_PIVOT = (32, 8)       # px from bottom-left = knife tip (game position)
MAP_PPU = 72
VIEW_TURN = -30.0           # degrees: from facing right toward the camera
CHAR_SUN = (-0.5, -0.6, 1.0)  # direction toward the key light: upper left, in front

# id, name, body colour (docs/work/art.md table 1), decoration
PLAYERS = [
    (1, 'strawberry', '#E2475A', 'leaves'),
    (2, 'blueberry', '#3F6FE6', 'bow'),
    (3, 'lime', '#4DC35A', 'crown'),
    (4, 'lemon', '#F0C93A', 'flag'),
    (5, 'grape', '#9A5DDB', 'cherry'),
    (6, 'tangerine', '#F38B2E', 'chef_hat'),
    (7, 'soda', '#37C4D2', 'umbrella'),
    (8, 'peach', '#EE70B0', 'cream'),
]


# ---------------------------------------------------------------- colours

def _lin(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def _hex3(h):
    h = h.lstrip('#')
    return [int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]


def rgb(h):
    """'#RRGGBB' (sRGB) -> linear RGBA for node sockets."""
    return tuple(_lin(c) for c in _hex3(h)) + (1.0,)


def mix_hex(a, b, t):
    ca, cb = _hex3(a), _hex3(b)
    return '#' + ''.join('%02X' % round(255 * (x * (1 - t) + y * t)) for x, y in zip(ca, cb))


def scale_hex(a, k):
    return '#' + ''.join('%02X' % round(255 * min(1.0, x * k)) for x in _hex3(a))


# ---------------------------------------------------------------- scene / render

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.preferences.edit.keyframe_new_interpolation_type = 'CONSTANT'
    bpy.context.preferences.filepaths.save_version = 0      # no .blend1 backups next to the files
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE'
    scene.eevee.taa_render_samples = 16
    scene.render.film_transparent = True
    scene.render.resolution_percentage = 100
    scene.render.filter_size = 1.0
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.image_settings.color_depth = '8'
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    world = bpy.data.worlds.new('World')
    scene.world = world
    bg = world.node_tree.nodes.get('Background')
    if bg:
        bg.inputs['Color'].default_value = (0, 0, 0, 1)
        bg.inputs['Strength'].default_value = 0.0
    return scene


def add_camera(scene, cx, cz, ortho_scale):
    cd = bpy.data.cameras.new('Camera')
    cd.type = 'ORTHO'
    cd.ortho_scale = ortho_scale
    cd.clip_start = 0.1
    cd.clip_end = 200
    cam = bpy.data.objects.new('Camera', cd)
    scene.collection.objects.link(cam)
    cam.location = (cx, -50.0, cz)
    cam.rotation_euler = (math.pi / 2, 0, 0)
    scene.camera = cam
    return cam


def frame_camera(scene, x0, z0, x1, z1, ppu):
    """Point the camera at world rect [x0,x1] x [z0,z1] rendered at ppu pixels per unit."""
    w, h = round((x1 - x0) * ppu), round((z1 - z0) * ppu)
    scene.render.resolution_x, scene.render.resolution_y = w, h
    cam = scene.camera
    cam.location.x, cam.location.z = (x0 + x1) / 2, (z0 + z1) / 2
    cam.data.ortho_scale = max(x1 - x0, z1 - z0)
    return w, h


def add_sun(scene, direction, energy=3.0, shadow=False):
    ld = bpy.data.lights.new('Sun', 'SUN')
    ld.energy = energy
    ld.use_shadow = shadow
    ld.angle = math.radians(2)
    sun = bpy.data.objects.new('Sun', ld)
    scene.collection.objects.link(sun)
    sun.rotation_euler = (-Vector(direction)).normalized().to_track_quat('-Z', 'Y').to_euler()
    return sun


def render_to(scene, path):
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def load_png(path):
    img = bpy.data.images.load(path, check_existing=False)
    w, h = img.size
    arr = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(arr)
    bpy.data.images.remove(img)
    return arr.reshape(h, w, 4)      # rows bottom-up (Blender order)


def save_png(arr, path):
    h, w = arr.shape[:2]
    img = bpy.data.images.new('out', w, h, alpha=True)
    img.pixels.foreach_set(np.ascontiguousarray(arr, dtype=np.float32).ravel())
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def resize(arr, w, h):
    img = bpy.data.images.new('rs', arr.shape[1], arr.shape[0], alpha=True)
    img.pixels.foreach_set(np.ascontiguousarray(arr, dtype=np.float32).ravel())
    img.scale(w, h)
    out = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(out)
    bpy.data.images.remove(img)
    return out.reshape(h, w, 4)


def over(dst, src, x, y):
    """Alpha-composite src (straight alpha) onto dst with src's bottom-left at pixel (x, y)."""
    h, w = src.shape[:2]
    x0, y0, x1, y1 = max(0, x), max(0, y), min(dst.shape[1], x + w), min(dst.shape[0], y + h)
    if x0 >= x1 or y0 >= y1:
        return
    s = src[y0 - y:y1 - y, x0 - x:x1 - x]
    d = dst[y0:y1, x0:x1]
    sa = s[..., 3:4]
    da = d[..., 3:4]
    oa = sa + da * (1 - sa)
    rgb_ = (s[..., :3] * sa + d[..., :3] * da * (1 - sa)) / np.maximum(oa, 1e-6)
    dst[y0:y1, x0:x1, :3] = rgb_
    dst[y0:y1, x0:x1, 3:4] = oa


# ---------------------------------------------------------------- materials

def _tree(mat):
    nt = mat.node_tree
    nt.nodes.clear()
    return nt


def _sock(node, ident, out=False):
    for s in (node.outputs if out else node.inputs):
        if s.identifier == ident:
            return s
    raise KeyError(ident)


def _mix(nt, fac, a, b):
    m = nt.nodes.new('ShaderNodeMix')
    m.data_type = 'RGBA'
    for sock, src in ((_sock(m, 'Factor_Float'), fac), (_sock(m, 'A_Color'), a), (_sock(m, 'B_Color'), b)):
        if isinstance(src, (int, float)):
            sock.default_value = src
        elif isinstance(src, tuple):
            sock.default_value = src
        else:
            nt.links.new(src, sock)
    return _sock(m, 'Result_Color', True)


def _ramp(nt, src, stops, interp='CONSTANT'):
    """Colour ramp on a scalar (stepped by default): stops = [(position, value)], first position 0."""
    n = nt.nodes.new('ShaderNodeValToRGB')
    cr = n.color_ramp
    cr.interpolation = interp
    els = cr.elements
    els[1].position = 1.0
    for i, (pos, v) in enumerate(stops):
        e = els[i] if i < 2 else els.new(pos)
        e.position = pos
        e.color = (v, v, v, 1)
    if len(stops) == 1:
        els.remove(els[1])
    nt.links.new(src, n.inputs['Fac'])
    return n.outputs['Color']


def _math(nt, op, a, b):
    n = nt.nodes.new('ShaderNodeMath')
    n.operation = op
    for sock, src in ((n.inputs[0], a), (n.inputs[1], b)):
        if isinstance(src, (int, float)):
            sock.default_value = src
        else:
            nt.links.new(src, sock)
    return n.outputs[0]


def _rgb_node(nt, name, col):
    n = nt.nodes.new('ShaderNodeRGB')
    n.name = name
    n.outputs[0].default_value = rgb(col)
    return n.outputs[0]


def _emit(nt, color_socket):
    em = nt.nodes.new('ShaderNodeEmission')
    em.inputs['Strength'].default_value = 1.0
    nt.links.new(color_socket, em.inputs['Color'])
    out = nt.nodes.new('ShaderNodeOutputMaterial')
    nt.links.new(em.outputs[0], out.inputs['Surface'])


def flat_mat(name, col):
    mat = bpy.data.materials.new(name)
    nt = _tree(mat)
    _emit(nt, _rgb_node(nt, 'BASE', col))
    return mat


def line_mat(name, col):
    mat = flat_mat(name, col)
    mat.use_backface_culling = True
    return mat


def toon_mat(name, base, shadow, spec=0.0, rim=0.0, rim_col=None, mid=0.55, lit_at=0.35, spec_at=0.6):
    """Cel shading: diffuse light quantised to shadow / mid / lit, plus a hard glossy spot and a rim band."""
    mat = bpy.data.materials.new(name)
    nt = _tree(mat)
    b = _rgb_node(nt, 'BASE', base)
    s = _rgb_node(nt, 'SHADOW', shadow)
    dif = nt.nodes.new('ShaderNodeBsdfDiffuse')
    dif.inputs['Color'].default_value = (1, 1, 1, 1)
    s2r = nt.nodes.new('ShaderNodeShaderToRGB')
    nt.links.new(dif.outputs[0], s2r.inputs[0])
    band = _ramp(nt, s2r.outputs['Color'], [(0.0, 0.0), (0.04, mid), (lit_at, 1.0)])
    col = _mix(nt, band, s, b)
    if spec > 0:
        g = nt.nodes.new('ShaderNodeBsdfGlossy')
        g.inputs['Roughness'].default_value = 0.2
        g.inputs['Color'].default_value = (1, 1, 1, 1)
        s2r2 = nt.nodes.new('ShaderNodeShaderToRGB')
        nt.links.new(g.outputs[0], s2r2.inputs[0])
        spot = _ramp(nt, s2r2.outputs['Color'], [(0.0, 0.0), (spec_at, 1.0)])
        col = _mix(nt, _math(nt, 'MULTIPLY', spot, spec), col, (1, 1, 1, 1))
    if rim > 0:
        lw = nt.nodes.new('ShaderNodeLayerWeight')
        lw.inputs['Blend'].default_value = 0.5
        edge = _ramp(nt, lw.outputs['Facing'], [(0.0, 0.0), (0.82, 1.0)])
        rc = _rgb_node(nt, 'RIM', rim_col or base)
        col = _mix(nt, _math(nt, 'MULTIPLY', edge, rim), col, rc)
    _emit(nt, col)
    return mat


def set_rgb(mat, name, col):
    mat.node_tree.nodes[name].outputs[0].default_value = rgb(col)


# ---------------------------------------------------------------- mesh helpers

def new_obj(name, bm, mats, parent=None, coll=None, smooth=True):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(m)
    me.polygons.foreach_set('use_smooth', [smooth] * len(me.polygons))
    ob = bpy.data.objects.new(name, me)
    (coll or bpy.context.scene.collection).objects.link(ob)
    ob.parent = parent
    return ob


def add_outline(ob, mat, thickness):
    """Inverted hull: a flipped, back-face-culled shell one outline thick, drawn in the line colour."""
    n = len(ob.data.materials)
    for _ in range(max(1, n)):
        ob.data.materials.append(mat)
    md = ob.modifiers.new('Outline', 'SOLIDIFY')
    md.thickness = thickness
    md.offset = 1.0
    md.use_flip_normals = True
    md.use_rim = False
    md.material_offset = max(1, n)
    return md


def _faces_of(verts):
    return {f for v in verts for f in v.link_faces}


def add_ellipsoid(bm, center, radii, matrix=Matrix(), mat=0, seg=24, rings=12):
    verts = bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0)['verts']
    m = matrix @ Matrix.Translation(center) @ Matrix.Diagonal((*radii, 1.0))
    bmesh.ops.transform(bm, matrix=m, verts=verts)
    for f in _faces_of(verts):
        f.material_index = mat


def add_tube(bm, pts, radius, matrix=Matrix(), mat=0, sides=8, closed=False):
    """Sweep a circle along a polyline. radius may be a number or one value per point."""
    pts = [matrix @ Vector(p) for p in pts]
    n = len(pts)
    radii = radius if isinstance(radius, (list, tuple)) else [radius] * n
    rings = []
    prev = None
    for i, p in enumerate(pts):
        if closed:
            t = (pts[(i + 1) % n] - pts[i - 1]).normalized()
        else:
            t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]).normalized()
        if prev is None:
            a = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
            nrm = t.cross(a).normalized()
        else:
            nrm = (prev - t * prev.dot(t)).normalized()
        prev = nrm
        bi = t.cross(nrm)
        rings.append([bm.verts.new(p + (nrm * math.cos(k * 2 * math.pi / sides) +
                                        bi * math.sin(k * 2 * math.pi / sides)) * radii[i])
                      for k in range(sides)])
    faces = []
    for i in range(n if closed else n - 1):
        a, b = rings[i], rings[(i + 1) % n]
        for k in range(sides):
            faces.append(bm.faces.new((a[k], a[(k + 1) % sides], b[(k + 1) % sides], b[k])))
    if not closed:
        faces.append(bm.faces.new(rings[0]))
        faces.append(bm.faces.new(list(reversed(rings[-1]))))
    for f in faces:
        f.material_index = mat


def add_prism(bm, pts2d, h0, h1, matrix=Matrix(), mat=0):
    """Polygon (u, v) extruded along the matrix's third axis from h0 to h1."""
    front = [bm.verts.new(matrix @ Vector((u, v, h0))) for u, v in pts2d]
    back = [bm.verts.new(matrix @ Vector((u, v, h1))) for u, v in pts2d]
    faces = [bm.faces.new(front), bm.faces.new(list(reversed(back)))]
    n = len(pts2d)
    for i in range(n):
        j = (i + 1) % n
        faces.append(bm.faces.new((front[i], back[i], back[j], front[j])))
    for f in faces:
        f.material_index = mat


def add_revolve(bm, profile, seg=32, matrix=Matrix(), mat=0):
    """Revolve [(r, z)] around Z; r == 0 points become poles."""
    rings = []
    for r, z in profile:
        if r < 1e-6:
            rings.append([bm.verts.new(matrix @ Vector((0, 0, z)))])
        else:
            rings.append([bm.verts.new(matrix @ Vector((r * math.cos(a), r * math.sin(a), z)))
                          for a in (2 * math.pi * k / seg for k in range(seg))])
    faces = []
    for a, b in zip(rings, rings[1:]):
        for k in range(seg):
            k1 = (k + 1) % seg
            if len(a) == 1:
                faces.append(bm.faces.new((a[0], b[k], b[k1])))
            elif len(b) == 1:
                faces.append(bm.faces.new((a[k], a[k1], b[0])))
            else:
                faces.append(bm.faces.new((a[k], a[k1], b[k1], b[k])))
    for f in faces:
        f.material_index = mat


def ellipse_pts(cu, cv, ru, rv, n=24, a0=0.0, a1=2 * math.pi):
    return [(cu + ru * math.cos(a0 + (a1 - a0) * i / n), cv + rv * math.sin(a0 + (a1 - a0) * i / n))
            for i in range(n)]


XZ = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))   # (u, v, h) -> (x=u, y=h, z=v)


def at_xz(x, z, y=0.0):
    return Matrix.Translation((x, y, z)) @ XZ


# ================================================================ characters

BODY_R = 0.39
BODY_Z0 = 0.07
BODY_Z1 = 1.19
EYE_Z = 0.68
EYE_YAWS = (-47.0, -13.0)      # local yaw (0 = forward, -60 = toward the camera)
MOUTH_AT = (-29.0, 0.52)
FEET = ((0.19, 0.03, 0.066), (-0.19, -0.03, 0.066))
LAT_LOC = Vector((0.0, 0.0, 0.95))
LAT_SIZE = Vector((1.7, 1.3, 2.0))

SHAPES = {
    'tall': lambda p: Vector((p.x * 0.82, p.y * 0.82, p.z * 1.28)),
    'flat': lambda p: Vector((p.x * 1.22, p.y * 1.22, p.z * 0.74)),
    'lean': lambda p: Vector((p.x + 0.30 * p.z, p.y, p.z)),
    'long': lambda p: Vector((p.x * 1.8, p.y * 0.95, p.z * 0.6)),
    'taper': lambda p: Vector((p.x, p.y, p.z * (1 - 0.45 * max(0.0, -p.x) / 0.85))),
    'puddle': lambda p: Vector((p.x * 2.0, p.y * 1.7, p.z * 0.28)),
}


def body_radius(z):
    t = (z - BODY_Z0) / (BODY_Z1 - BODY_Z0)
    if t <= 0 or t >= 1:
        return 0.0
    k = 0.45
    if t < k:
        u = (k - t) / k
        return BODY_R * (1 - u ** 4) ** 0.25
    u = (t - k) / (1 - k)
    return BODY_R * (1 - 0.07 * u) * math.sqrt(max(0.0, 1 - u * u))


def surface_frame(yaw_deg, z):
    """Matrix whose x = viewer's right, y = up along the surface, z = outward normal, at the body surface."""
    a = math.radians(yaw_deg)
    r = body_radius(z)
    dr = (body_radius(z + 1e-3) - body_radius(z - 1e-3)) / 2e-3
    radial = Vector((math.cos(a), math.sin(a), 0))
    p = radial * r + Vector((0, 0, z))
    n = (radial - Vector((0, 0, dr))).normalized()
    up = (Vector((0, 0, 1)) - n * n.z).normalized()
    side = up.cross(n)
    m = Matrix((side, up, n)).transposed().to_4x4()
    m.translation = p
    return m


def body_bmesh():
    bm = bmesh.new()
    seg, n = 48, 40
    zs = [BODY_Z0 + (BODY_Z1 - BODY_Z0) * (0.5 - 0.5 * math.cos(math.pi * i / n)) for i in range(1, n)]
    rings = []
    for z in zs:
        r = body_radius(z)
        rings.append([bm.verts.new((r * math.cos(2 * math.pi * k / seg), r * math.sin(2 * math.pi * k / seg), z))
                      for k in range(seg)])
    bottom = bm.verts.new((0, 0, BODY_Z0))
    top = bm.verts.new((0, 0, BODY_Z1))
    for a, b in zip(rings, rings[1:]):
        for k in range(seg):
            bm.faces.new((a[k], a[(k + 1) % seg], b[(k + 1) % seg], b[k]))
    for k in range(seg):
        bm.faces.new((bottom, rings[0][(k + 1) % seg], rings[0][k]))
        bm.faces.new((top, rings[-1][k], rings[-1][(k + 1) % seg]))
    return bm


def add_lattice_mod(ob, lat):
    md = ob.modifiers.new('Lattice', 'LATTICE')
    md.object = lat
    ob.modifiers.move(len(ob.modifiers) - 1, 0)      # deform first, then grow the outline shell
    return md


def make_lattice(parent):
    ld = bpy.data.lattices.new('JellyLattice')
    ld.points_u, ld.points_v, ld.points_w = 3, 2, 4
    ld.interpolation_type_u = ld.interpolation_type_v = ld.interpolation_type_w = 'KEY_LINEAR'
    lo = bpy.data.objects.new('JellyLattice', ld)
    bpy.context.scene.collection.objects.link(lo)
    lo.parent = parent
    lo.location = LAT_LOC
    lo.scale = LAT_SIZE
    lo.shape_key_add(name='Basis', from_mix=False)
    keys = {}
    for name, fn in SHAPES.items():
        kb = lo.shape_key_add(name=name, from_mix=False)
        kb.slider_min, kb.slider_max = -1.0, 1.5
        for i, pt in enumerate(ld.points):
            c = pt.co
            p = Vector((LAT_LOC.x + c[0] * LAT_SIZE.x, LAT_LOC.y + c[1] * LAT_SIZE.y, LAT_LOC.z + c[2] * LAT_SIZE.z))
            q = fn(p)
            kb.data[i].co = ((q.x - LAT_LOC.x) / LAT_SIZE.x, (q.y - LAT_LOC.y) / LAT_SIZE.y,
                             (q.z - LAT_LOC.z) / LAT_SIZE.z)
        keys[name] = kb
    return lo, keys


# ---- faces: each expression is one object (both eyes, or the mouth), shown per frame

def build_faces(parent, lat, mats):
    ink, white, mouth, tongue = mats
    eyes = [surface_frame(y, EYE_Z) for y in EYE_YAWS]
    mf = surface_frame(*MOUTH_AT)
    out = {}

    def part(name, build):
        bm = bmesh.new()
        build(bm)
        ob = new_obj(name, bm, [ink, white, mouth, tongue], parent)
        add_lattice_mod(ob, lat)
        out[name] = ob

    def open_eyes(scale):
        def b(bm):
            for m in eyes:
                add_ellipsoid(bm, (0, 0, 0.004), (0.06 * scale, 0.108 * scale, 0.022), m, 0)
                add_ellipsoid(bm, (-0.018 * scale, 0.048 * scale, 0.02), (0.022 * scale, 0.027 * scale, 0.01), m, 1)
                add_ellipsoid(bm, (0.02 * scale, -0.042 * scale, 0.018), (0.01, 0.011, 0.008), m, 1)
        return b

    part('Eyes_open', open_eyes(1.0))
    part('Eyes_wide', open_eyes(1.14))

    def determined(bm):
        for i, m in enumerate(eyes):
            add_ellipsoid(bm, (0, -0.016, 0.004), (0.06, 0.08, 0.022), m, 0)
            add_ellipsoid(bm, (-0.016, 0.016, 0.02), (0.02, 0.021, 0.01), m, 1)
            brow = [(-0.065, 0.112), (0.065, 0.066)] if i == 0 else [(-0.065, 0.066), (0.065, 0.112)]
            add_tube(bm, [(u, v, 0.014) for u, v in brow], 0.014, m, 0)
    part('Eyes_determined', determined)

    def happy(bm):
        for m in eyes:
            us = [-0.062 + 0.124 * i / 10 for i in range(11)]
            add_tube(bm, [(u, 0.026 - 8 * u * u, 0.012) for u in us], 0.016, m, 0)
    part('Eyes_happy', happy)

    def cross(bm):
        for m in eyes:
            add_tube(bm, [(-0.058, -0.058, 0.012), (0.058, 0.058, 0.012)], 0.017, m, 0)
            add_tube(bm, [(-0.058, 0.058, 0.012), (0.058, -0.058, 0.012)], 0.017, m, 0)
    part('Eyes_x', cross)

    def spiral(bm):
        for m in eyes:
            turns, steps = 2.25, 40
            pts = []
            for i in range(steps + 1):
                t = i / steps
                a = t * turns * 2 * math.pi
                r = 0.01 + 0.06 * t
                pts.append((r * math.cos(a), r * math.sin(a), 0.012))
            add_tube(bm, pts, 0.01, m, 0, sides=6)
    part('Eyes_spiral', spiral)

    us = [-0.045 + 0.09 * i / 10 for i in range(11)]
    part('Mouth_smile', lambda bm: add_tube(bm, [(u, -0.014 + 8 * u * u, 0.01) for u in us], 0.009, mf, 0))
    part('Mouth_frown', lambda bm: add_tube(bm, [(u, 0.006 - 8 * u * u, 0.01) for u in us], 0.009, mf, 0))
    part('Mouth_o', lambda bm: add_prism(bm, ellipse_pts(0, -0.004, 0.025, 0.032, 20), -0.012, 0.008, mf, 2))

    def open_mouth(bm):
        pts = [(0.052 * math.cos(math.pi * i / 16), 0.014 - 0.058 * math.sin(math.pi * i / 16)) for i in range(17)]
        add_prism(bm, pts, -0.012, 0.008, mf, 2)
        add_ellipsoid(bm, (0, -0.03, 0.004), (0.028, 0.015, 0.008), mf, 3)
    part('Mouth_open', open_mouth)
    return out


# ---- decorations: built around their anchor (origin = top of the head, +Z up, +X forward)

DECOR_LINE = '#1E1A28'


def build_decor(kind, anchor, coll):
    root = bpy.data.objects.new('Decor_' + kind, None)
    coll.objects.link(root)
    root.parent = anchor
    line = line_mat('DecorLine_' + kind, DECOR_LINE)
    parts = []

    def part(name, mats, build, outline=0.016):
        bm = bmesh.new()
        build(bm)
        ob = new_obj(name, bm, mats, root, coll)
        if outline:
            add_outline(ob, line, outline)
        parts.append(ob)

    def toon(name, base, shadow, spec=0.0):
        return toon_mat(name, base, shadow, spec=spec, rim=0.0)

    rot = lambda deg: Matrix.Rotation(math.radians(deg), 4, 'Y')
    tr = Matrix.Translation

    if kind == 'leaves':
        leaf = toon('Leaf', '#5CB848', '#2E7A3A', 0.3)
        part('Leaves', [leaf], lambda bm: (
            add_ellipsoid(bm, (0, 0, 0), (0.15, 0.032, 0.065), tr((0.1, 0, 0.12)) @ rot(-38)),
            add_ellipsoid(bm, (0, 0, 0), (0.15, 0.032, 0.065), tr((-0.1, 0, 0.12)) @ rot(38)),
            add_tube(bm, [(0, 0, -0.04), (0, 0, 0.08)], 0.022)))
    elif kind == 'bow':
        cloth = toon('Bow', '#F2F0EA', '#AEB6C8', 0.2)
        part('Bow', [cloth], lambda bm: (
            add_ellipsoid(bm, (0, 0, 0), (0.15, 0.055, 0.095), tr((0.135, 0, 0.08)) @ rot(-16)),
            add_ellipsoid(bm, (0, 0, 0), (0.15, 0.055, 0.095), tr((-0.135, 0, 0.08)) @ rot(16)),
            add_ellipsoid(bm, (0, 0, 0.075), (0.055, 0.06, 0.06))))
        root.location = (-0.1, 0, -0.03)
        root.rotation_euler = (0, math.radians(-22), 0)
    elif kind == 'crown':
        gold = toon('Gold', '#F5C232', '#B47E1E', 0.7)

        def crown(bm):
            n, ro, ri = 10, 0.155, 0.13
            ob_, ot, it, ib = [], [], [], []
            for i in range(n):
                a = 2 * math.pi * i / n
                h = 0.21 if i % 2 == 0 else 0.09
                c, s = math.cos(a), math.sin(a)
                ob_.append(bm.verts.new((ro * c, ro * s, -0.04)))
                ot.append(bm.verts.new((ro * c, ro * s, h)))
                it.append(bm.verts.new((ri * c, ri * s, h - 0.01)))
                ib.append(bm.verts.new((ri * c, ri * s, -0.04)))
            for i in range(n):
                j = (i + 1) % n
                bm.faces.new((ob_[i], ob_[j], ot[j], ot[i]))
                bm.faces.new((ot[i], ot[j], it[j], it[i]))
                bm.faces.new((it[i], it[j], ib[j], ib[i]))
                bm.faces.new((ib[i], ib[j], ob_[j], ob_[i]))
            for i in range(0, n, 2):
                a = 2 * math.pi * i / n
                add_ellipsoid(bm, (0.143 * math.cos(a), 0.143 * math.sin(a), 0.22), (0.025, 0.025, 0.025), seg=12, rings=6)
        part('Crown', [gold], crown)
    elif kind == 'flag':
        wood = toon('Toothpick', '#E9CF9A', '#B08D58')
        cloth = toon('Flag', '#3448A8', '#222E78')
        part('Toothpick', [wood], lambda bm: add_tube(bm, [(0, 0, -0.06), (0, 0, 0.42)], 0.014))
        part('Flag', [cloth], lambda bm: add_prism(bm, [(0.012, 0.415), (0.012, 0.25), (0.21, 0.33)],
                                                   -0.012, 0.012, XZ))
    elif kind == 'cherry':
        red = toon('Cherry', '#D9243C', '#8A1228', 0.9)
        stem = toon('Stem', '#6B4024', '#3E2414')
        part('Cherry', [red], lambda bm: add_ellipsoid(bm, (0, 0, 0.08), (0.1, 0.1, 0.096)))
        part('Stem', [stem], lambda bm: add_tube(
            bm, [(0, 0, 0.16), (0.008, 0, 0.23), (0.04, 0, 0.3), (0.09, 0, 0.35)], 0.013))
    elif kind == 'chef_hat':
        cloth = toon('ChefHat', '#F4F2EC', '#AFB7C8', 0.15)
        part('ChefHat', [cloth], lambda bm: (
            add_revolve(bm, [(0, -0.03), (0.125, -0.03), (0.13, 0.1), (0, 0.1)], 32),
            add_ellipsoid(bm, (-0.075, 0, 0.17), (0.1, 0.1, 0.09)),
            add_ellipsoid(bm, (0.075, 0, 0.17), (0.1, 0.1, 0.09)),
            add_ellipsoid(bm, (0, 0.06, 0.2), (0.1, 0.09, 0.09)),
            add_ellipsoid(bm, (0, -0.06, 0.19), (0.1, 0.09, 0.09)),
            add_ellipsoid(bm, (0, 0, 0.245), (0.09, 0.09, 0.07))))
        root.location = (-0.03, 0, 0)
        root.rotation_euler = (0, math.radians(-8), 0)
    elif kind == 'umbrella':
        pink = toon('Umbrella', '#F58DBA', '#C25683')
        stripe = toon('UmbrellaStripe', '#FFD3E4', '#D99BB6')
        wood = toon('UmbrellaStick', '#E9CF9A', '#B08D58')

        def canopy(bm):
            n, r, zc = 10, 0.25, 0.25
            rim = [bm.verts.new((r * math.cos(2 * math.pi * i / n), r * math.sin(2 * math.pi * i / n), zc))
                   for i in range(n)]
            top = bm.verts.new((0, 0, zc + 0.11))
            under = bm.verts.new((0, 0, zc + 0.05))
            for i in range(n):
                j = (i + 1) % n
                bm.faces.new((rim[i], rim[j], top)).material_index = i % 2
                bm.faces.new((rim[j], rim[i], under)).material_index = 0
            add_ellipsoid(bm, (0, 0, zc + 0.12), (0.018, 0.018, 0.018), seg=10, rings=6)
        part('Canopy', [pink, stripe], canopy)
        part('Stick', [wood], lambda bm: add_tube(bm, [(0, 0, -0.07), (0, 0, 0.3)], 0.011))
        root.location = (-0.03, 0, -0.02)
        root.rotation_euler = (0, math.radians(-24), 0)
    elif kind == 'cream':
        cream = toon('Cream', '#FBF5EA', '#CDBFAE', 0.25)

        def swirl(bm):
            steps, turns = 70, 2.6
            pts, rads = [], []
            for i in range(steps + 1):
                t = i / steps
                a = t * turns * 2 * math.pi
                rr = 0.16 * (1 - t)
                pts.append((rr * math.cos(a), rr * math.sin(a), 0.0 + 0.25 * t))
                rads.append(0.072 * (1 - t) + 0.02)
            pts.append((0.0, 0.0, 0.32))
            rads.append(0.008)
            add_tube(bm, pts, rads, sides=12)
        part('Cream', [cream], swirl)
    return root, parts


# ---- animation table

def F(eyes='open', mouth='smile', **k):
    k.update(eyes=eyes, mouth=mouth)
    return k


def _run(i):
    p = i / 6
    s = math.sin(2 * math.pi * p)
    c = math.cos(2 * math.pi * p)
    a = abs(s)
    return F('open', 'open', lean=0.32, flat=0.1 * (1 - a), tall=0.08 * a, dz=0.05 * a,
             feet=((0.12 * c, 0.07 * max(0.0, s)), (-0.12 * c, 0.07 * max(0.0, -s))),
             decor_tilt=-12 + 6 * a)


ANIMS = [
    ('idle', 6, True, [F(flat=0.0), F(flat=0.04), F(flat=0.08), F(flat=0.04)]),
    ('run', 12, True, [_run(i) for i in range(6)]),
    ('jump', 10, True, [F('open', 'o', tall=0.35), F('open', 'o', tall=0.28, decor_dz=-0.01)]),
    ('fall', 10, True, [F('wide', 'o', flat=0.28, feet=((0.02, 0.03), (-0.02, 0.03)), decor_dz=0.04),
                        F('wide', 'o', flat=0.34, feet=((0.02, 0.04), (-0.02, 0.04)), decor_dz=0.06)]),
    ('dash', 15, True, [F('determined', 'smile', long=1.0, taper=1.0, lean=0.1, decor_tilt=-25),
                        F('determined', 'smile', long=0.9, taper=0.9, lean=0.12, decor_tilt=-28),
                        F('determined', 'smile', long=1.05, taper=1.0, lean=0.08, decor_tilt=-22)]),
    ('knockback', 12, False, [
        F('spiral', 'o', tilt=-16, dz=0.06, lean=-0.25, flat=0.1, feet=((0.05, 0.03), (-0.05, 0.06)), decor_tilt=-12),
        F('spiral', 'o', tilt=-28, dz=0.14, lean=-0.3, flat=0.12, feet=((0.06, 0.05), (-0.05, 0.08)), decor_tilt=-18),
        F('spiral', 'o', tilt=-22, dz=0.10, lean=-0.22, flat=0.08, feet=((0.05, 0.04), (-0.05, 0.06)), decor_tilt=-14)]),
    ('death', 10, False, [
        F('x', 'frown', tall=0.2),
        F('x', 'frown', flat=0.5, decor_dz=0.18, decor_tilt=-25),
        F('x', 'frown', puddle=0.6, decor_at=(-0.25, 0.0, 0.62, -35)),
        F('x', 'frown', puddle=1.0, decor_at=(-0.38, -0.1, 0.27, -35))]),
    ('victory', 8, True, [
        F('happy', 'open', flat=0.25, decor_tilt=6),
        F('happy', 'open', tall=0.3, dz=0.12, decor_tilt=-6),
        F('happy', 'open', tall=0.15, dz=0.16, decor_tilt=-8),
        F('happy', 'open', flat=0.05, dz=0.04, decor_tilt=4)]),
]


def jelly_colours(base):
    return dict(
        base=base,
        shadow=mix_hex(scale_hex(base, 0.72), '#3A2A6A', 0.32),
        rim=mix_hex(base, '#FFFFFF', 0.45),
        line=mix_hex(scale_hex(base, 0.25), '#140F22', 0.6),
    )


def build_characters(scene):
    view = bpy.data.objects.new('View', None)
    scene.collection.objects.link(view)
    view.rotation_euler = (0, 0, math.radians(VIEW_TURN))
    pose = bpy.data.objects.new('Pose', None)
    scene.collection.objects.link(pose)
    pose.parent = view
    lat, keys = make_lattice(pose)

    c = jelly_colours(PLAYERS[0][2])
    body_mat = toon_mat('Jelly', c['base'], c['shadow'], spec=0.9, rim=0.35, rim_col=c['rim'], spec_at=1.5)
    body_line = line_mat('JellyLine', c['line'])

    body = new_obj('Body', body_bmesh(), [body_mat], pose)
    add_outline(body, body_line, 0.02)
    add_lattice_mod(body, lat)
    feet = []
    for i, (x, y, z) in enumerate(FEET):
        bm = bmesh.new()
        add_ellipsoid(bm, (x, y, z), (0.115, 0.095, 0.066))
        f = new_obj('Foot%d' % i, bm, [body_mat], pose)
        add_outline(f, body_line, 0.02)
        add_lattice_mod(f, lat)
        feet.append(f)

    face_mats = [flat_mat('Ink', '#17131F'), flat_mat('EyeShine', '#FFFFFF'),
                 flat_mat('MouthIn', '#5A1A2C'), flat_mat('Tongue', '#F07C8C')]
    faces = build_faces(pose, lat, face_mats)

    anchor = bpy.data.objects.new('DecorAnchor', None)
    scene.collection.objects.link(anchor)
    anchor.parent = pose
    decor = {}
    for pid, _, _, kind in PLAYERS:
        coll = bpy.data.collections.new('Decor_%d_%s' % (pid, kind))
        scene.collection.children.link(coll)
        build_decor(kind, anchor, coll)
        decor[pid] = coll

    rig = dict(view=view, pose=pose, lat=lat, keys=keys, body=body, feet=feet, faces=faces, anchor=anchor,
               decor=decor, body_mat=body_mat, body_line=body_line)
    keyframe_animations(scene, rig)
    return rig


def _apply(rig, fr):
    for k, kb in rig['keys'].items():
        kb.value = fr.get(k, 0.0)
    rig['pose'].location = (fr.get('dx', 0.0), 0.0, fr.get('dz', 0.0))
    rig['pose'].rotation_euler = (0.0, math.radians(fr.get('tilt', 0.0)), 0.0)
    for foot, (dx, dz) in zip(rig['feet'], fr.get('feet', ((0, 0), (0, 0)))):
        foot.location = (dx, 0.0, dz)
    for name, ob in rig['faces'].items():
        hidden = name not in ('Eyes_' + fr['eyes'], 'Mouth_' + fr['mouth'])
        ob.hide_render = hidden
        ob.hide_viewport = hidden


def _head_top(rig):
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    ev = rig['body'].evaluated_get(dg)
    me = ev.to_mesh()
    top = max((v.co.copy() for v in me.vertices), key=lambda co: co.z)
    ev.to_mesh_clear()
    return top


def keyframe_animations(scene, rig):
    """Pass 1 poses each frame and measures the head top for the decoration; pass 2 writes the keyframes."""
    frames = []
    outline = rig['body'].modifiers['Outline']
    outline.show_viewport = False
    for name, fps, loop, frs in ANIMS:
        for i, fr in enumerate(frs):
            _apply(rig, fr)
            if 'decor_at' in fr:
                x, y, z, tilt = fr['decor_at']       # slid off the head onto the puddle
                loc = Vector((x, y, z))
            else:
                top = _head_top(rig)
                loc = Vector((top.x, top.y, top.z - 0.035 + fr.get('decor_dz', 0.0)))
                tilt = fr.get('decor_tilt', 0.0)
            frames.append((name, i, fr, loc, tilt))
    outline.show_viewport = True

    for f, (name, i, fr, loc, tilt) in enumerate(frames, start=1):
        _apply(rig, fr)
        rig['anchor'].location = loc
        rig['anchor'].rotation_euler = (0.0, math.radians(tilt), 0.0)
        for kb in rig['keys'].values():
            kb.keyframe_insert('value', frame=f)
        for ob in (rig['pose'], rig['anchor']):
            ob.keyframe_insert('location', frame=f)
            ob.keyframe_insert('rotation_euler', frame=f)
        for foot in rig['feet']:
            foot.keyframe_insert('location', frame=f)
        for ob in rig['faces'].values():
            ob.keyframe_insert('hide_render', frame=f)
            ob.keyframe_insert('hide_viewport', frame=f)
        scene.timeline_markers.new('%s_%d' % (name, i), frame=f)
    scene.frame_start, scene.frame_end = 1, len(frames)
    rig['frames'] = frames


def set_player(rig, pid):
    _, _, base, _ = PLAYERS[pid - 1]
    c = jelly_colours(base)
    set_rgb(rig['body_mat'], 'BASE', c['base'])
    set_rgb(rig['body_mat'], 'SHADOW', c['shadow'])
    set_rgb(rig['body_mat'], 'RIM', c['rim'])
    set_rgb(rig['body_line'], 'BASE', c['line'])
    for k, coll in rig['decor'].items():
        coll.hide_render = k != pid
        coll.hide_viewport = k != pid


def alpha_bbox(arr, thr=0.5):
    ys, xs = np.nonzero(arr[..., 3] > thr)
    if len(xs) == 0:
        return None
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def target_characters():
    scene = reset_scene()
    add_camera(scene, 0, 0, 2.0)
    add_sun(scene, CHAR_SUN, energy=3.0)
    half = CHAR_FRAME / CHAR_PPU / 2
    z0 = -CHAR_PIVOT[1] / CHAR_PPU
    frame_camera(scene, -half, z0, half, z0 + CHAR_FRAME / CHAR_PPU, CHAR_PPU)
    rig = build_characters(scene)
    set_player(rig, 1)
    scene.frame_set(1)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'jelly.blend'), compress=True)

    out_dir = os.path.join(SPRITES, 'characters')
    os.makedirs(out_dir, exist_ok=True)
    tmp = tempfile.mkdtemp(prefix='jelly_')
    cols = max(len(frs) for _, _, _, frs in ANIMS)
    rows = len(ANIMS)
    fs = CHAR_FRAME
    report = {}

    # Hit-box check: body only (no decoration), idle frame 0.
    for coll in rig['decor'].values():
        coll.hide_render = True
    scene.frame_set(1)
    render_to(scene, os.path.join(tmp, 'bodyonly.png'))
    bb = alpha_bbox(load_png(os.path.join(tmp, 'bodyonly.png')))
    report['idle_body_bbox_units'] = dict(
        left=(bb[0] - CHAR_PIVOT[0]) / CHAR_PPU, right=(bb[2] - CHAR_PIVOT[0]) / CHAR_PPU,
        bottom=(bb[1] - CHAR_PIVOT[1]) / CHAR_PPU, top=(bb[3] - CHAR_PIVOT[1]) / CHAR_PPU)

    clipped = []
    for pid, pname, _, kind in PLAYERS:
        set_player(rig, pid)
        sheet = np.zeros((rows * fs, cols * fs, 4), np.float32)
        f = 0
        for r, (aname, _, _, frs) in enumerate(ANIMS):
            for i in range(len(frs)):
                f += 1
                scene.frame_set(f)
                path = os.path.join(tmp, '%d_%d.png' % (pid, f))
                render_to(scene, path)
                img = load_png(path)
                a = img[..., 3]
                if a[-1, :].max() > 0.02 or a[0, :].max() > 0.02 or a[:, 0].max() > 0.02 or a[:, -1].max() > 0.02:
                    clipped.append('%d %s %d' % (pid, aname, i))
                y0 = (rows - 1 - r) * fs
                sheet[y0:y0 + fs, i * fs:(i + 1) * fs] = img
        save_png(sheet, os.path.join(out_dir, 'jelly_%d_%s.png' % (pid, pname)))
        print('sheet', pid, pname)
    report['clipped_frames'] = clipped

    meta = dict(
        ppu=CHAR_PPU, frame_size=[fs, fs], pivot_px_from_bottom_left=list(CHAR_PIVOT),
        pivot_normalized=[CHAR_PIVOT[0] / fs, CHAR_PIVOT[1] / fs],
        layout='one row per animation, top row first; frames left to right; empty cells are transparent',
        facing='right (flip X for left)',
        sheets={str(pid): 'jelly_%d_%s.png' % (pid, pname) for pid, pname, _, _ in PLAYERS},
        decorations={str(pid): kind for pid, _, _, kind in PLAYERS},
        animations=[dict(name=n, row=r, frames=len(frs), fps=fps, loop=loop)
                    for r, (n, fps, loop, frs) in enumerate(ANIMS)],
    )
    with open(os.path.join(out_dir, 'jelly_sheet.json'), 'w', encoding='utf-8') as fh:
        json.dump(meta, fh, indent=2)
    print('REPORT', json.dumps(report))


# ================================================================ knife

def target_knife():
    scene = reset_scene()
    add_camera(scene, 0, 0, 1.0)
    w, h = KNIFE_SIZE
    x0 = -KNIFE_PIVOT[0] / CHAR_PPU
    z0 = -KNIFE_PIVOT[1] / CHAR_PPU
    frame_camera(scene, x0, z0, x0 + w / CHAR_PPU, z0 + h / CHAR_PPU, CHAR_PPU)
    line = line_mat('KnifeLine', '#14161D')

    def piece(name, col, build, outline=True):
        bm = bmesh.new()
        build(bm)
        ob = new_obj(name, bm, [flat_mat(name, col)], smooth=False)
        if outline:
            add_outline(ob, line, 0.018)
        return ob

    blade = [(0, 0), (0.045, 0.05), (0.085, 0.14), (0.105, 0.26), (0.11, 0.40), (0.11, 0.60),
             (-0.10, 0.60), (-0.10, 0.30), (-0.085, 0.16), (-0.05, 0.07)]
    edge = [(0, 0), (0.045, 0.05), (0.085, 0.14), (0.105, 0.26), (0.11, 0.40), (0.11, 0.60),
            (0.076, 0.60), (0.076, 0.40), (0.07, 0.27), (0.052, 0.16), (0.022, 0.075)]
    shine = [(-0.075, 0.22), (-0.06, 0.22), (-0.06, 0.55), (-0.075, 0.55)]
    handle = [(-0.075, 0.65), (0.075, 0.65), (0.075, 0.95)] + \
        [(0.025 + 0.05 * math.cos(a), 0.95 + 0.05 * math.sin(a)) for a in (math.pi * i / 12 for i in range(1, 6))] + \
        [(-0.025 + 0.05 * math.cos(a), 0.95 + 0.05 * math.sin(a)) for a in (math.pi * i / 12 for i in range(7, 12))] + \
        [(-0.075, 0.95)]
    piece('Blade', '#E9EDF3', lambda bm: add_prism(bm, blade, -0.012, 0.012, XZ))
    piece('BladeEdge', '#B9C2D2', lambda bm: add_prism(bm, edge, -0.016, -0.013, XZ), outline=False)
    piece('BladeShine', '#FFFFFF', lambda bm: add_prism(bm, shine, -0.016, -0.013, XZ), outline=False)
    piece('Bolster', '#7B8494', lambda bm: add_prism(bm, [(-0.105, 0.595), (0.115, 0.595), (0.115, 0.655),
                                                          (-0.105, 0.655)], -0.018, 0.018, XZ))
    piece('Handle', '#4A2A1A', lambda bm: add_prism(bm, handle, -0.02, 0.02, XZ))
    piece('HandleLight', '#6E4329', lambda bm: add_prism(bm, [(-0.06, 0.67), (-0.035, 0.67), (-0.035, 0.96),
                                                              (-0.06, 0.95)], -0.024, -0.021, XZ), outline=False)
    piece('Rivets', '#D9DEE6', lambda bm: [add_prism(bm, ellipse_pts(0.01, v, 0.018, 0.018, 12), -0.025, -0.021, XZ)
                                           for v in (0.72, 0.81, 0.90)], outline=False)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'knife.blend'), compress=True)
    out_dir = os.path.join(SPRITES, 'knife')
    os.makedirs(out_dir, exist_ok=True)
    render_to(scene, os.path.join(out_dir, 'knife.png'))
    with open(os.path.join(out_dir, 'knife.json'), 'w', encoding='utf-8') as fh:
        json.dump(dict(ppu=CHAR_PPU, size=list(KNIFE_SIZE), pivot_px_from_bottom_left=list(KNIFE_PIVOT),
                       pivot_normalized=[KNIFE_PIVOT[0] / w, KNIFE_PIVOT[1] / h],
                       note='pivot = knife tip (game position). Tip down. Hit box 0.4 x 1.0 above the tip.'),
                  fh, indent=2)


# ================================================================ map

STAGE_W = 24.0
SHELF_TOP = 2.4
LID_TOP = 4.8
PLATFORMS = [(3.0, 9.0, SHELF_TOP), (15.0, 21.0, SHELF_TOP), (9.0, 15.0, LID_TOP)]   # Stage.Default
BG_RECT = (-1.5, -1.5, 25.5, 14.0)
FLOOR_RECT = (-1.5, -1.5, 25.5, 0.25)
SHELF_RECT = (-3.25, -1.0, 3.25, 0.125)     # relative to the platform's top centre
LID_RECT = (-3.25, -1.25, 3.25, 0.25)
CHAIN_X = (9.2, 14.8)
RAIL_Z = 12.9
PLATFORM_LINE = '#121318'


def _xz_coords(nt):
    tc = nt.nodes.new('ShaderNodeTexCoord')
    sep = nt.nodes.new('ShaderNodeSeparateXYZ')
    nt.links.new(tc.outputs['Object'], sep.inputs[0])
    comb = nt.nodes.new('ShaderNodeCombineXYZ')
    nt.links.new(sep.outputs['X'], comb.inputs['X'])
    nt.links.new(sep.outputs['Z'], comb.inputs['Y'])
    return comb.outputs[0], sep.outputs['X'], sep.outputs['Z']


def _glow(nt, xz, cx, cz, radius, col_socket, base, strength):
    """Stepped radial glow (3 bands) mixed over base."""
    vm = nt.nodes.new('ShaderNodeVectorMath')
    vm.operation = 'DISTANCE'
    nt.links.new(xz, vm.inputs[0])
    vm.inputs[1].default_value = (cx, cz, 0)
    mr = nt.nodes.new('ShaderNodeMapRange')
    nt.links.new(vm.outputs['Value'], mr.inputs['Value'])
    mr.inputs['From Min'].default_value = 0.0
    mr.inputs['From Max'].default_value = radius
    mr.inputs['To Min'].default_value = 1.0
    mr.inputs['To Max'].default_value = 0.0
    steps = _ramp(nt, mr.outputs['Result'], [(0.0, 0.0), (0.2, 0.34), (0.5, 0.67), (0.8, 1.0)])
    return _mix(nt, _math(nt, 'MULTIPLY', steps, strength), base, col_socket)


def wall_mat():
    mat = bpy.data.materials.new('Wall')
    nt = _tree(mat)
    xz, _, z = _xz_coords(nt)
    br = nt.nodes.new('ShaderNodeTexBrick')
    br.offset = 0.0
    nt.links.new(xz, br.inputs['Vector'])
    br.inputs['Color1'].default_value = rgb('#2B3142')
    br.inputs['Color2'].default_value = rgb('#272D3D')
    br.inputs['Mortar'].default_value = rgb('#1C202A')
    br.inputs['Scale'].default_value = 1.0
    br.inputs['Mortar Size'].default_value = 0.035
    br.inputs['Brick Width'].default_value = 1.0
    br.inputs['Row Height'].default_value = 1.0
    mr = nt.nodes.new('ShaderNodeMapRange')
    nt.links.new(z, mr.inputs['Value'])
    mr.inputs['From Min'].default_value = 6.0
    mr.inputs['From Max'].default_value = 14.0
    col = _mix(nt, _ramp(nt, mr.outputs['Result'], [(0.0, 0.0), (1.0, 0.55)], 'LINEAR'),
               br.outputs['Color'], rgb('#14161D'))
    col = _glow(nt, xz, 20.0, 8.6, 5.0, _rgb_node(nt, 'WARM', '#5A4836'), col, 0.45)
    col = _glow(nt, xz, 3.6, 9.4, 4.0, _rgb_node(nt, 'COOL', '#2E3D5E'), col, 0.45)
    _emit(nt, col)
    return mat


def wood_mat(name, light, dark, seam, plank=1.7, fade_from=None, fade_to=None):
    mat = bpy.data.materials.new(name)
    nt = _tree(mat)
    xz, _, z = _xz_coords(nt)
    br = nt.nodes.new('ShaderNodeTexBrick')
    br.offset = 0.5
    nt.links.new(xz, br.inputs['Vector'])
    br.inputs['Color1'].default_value = rgb(light)
    br.inputs['Color2'].default_value = rgb(mix_hex(light, dark, 0.35))
    br.inputs['Mortar'].default_value = rgb(seam)
    br.inputs['Scale'].default_value = 1.0
    br.inputs['Mortar Size'].default_value = 0.025
    br.inputs['Brick Width'].default_value = plank
    br.inputs['Row Height'].default_value = 40.0
    wave = nt.nodes.new('ShaderNodeTexWave')
    wave.wave_type = 'BANDS'
    wave.bands_direction = 'X'
    nt.links.new(xz, wave.inputs['Vector'])
    wave.inputs['Scale'].default_value = 1.6
    wave.inputs['Distortion'].default_value = 6.0
    wave.inputs['Detail'].default_value = 1.0
    grain = _ramp(nt, wave.outputs['Fac'], [(0.0, 0.0), (0.72, 1.0)])
    col = _mix(nt, _math(nt, 'MULTIPLY', grain, 0.35), br.outputs['Color'], rgb(dark))
    if fade_from is not None:
        mr = nt.nodes.new('ShaderNodeMapRange')
        nt.links.new(z, mr.inputs['Value'])
        mr.inputs['From Min'].default_value = fade_from
        mr.inputs['From Max'].default_value = fade_to
        col = _mix(nt, _ramp(nt, mr.outputs['Result'], [(0.0, 0.0), (0.35, 0.3), (0.7, 0.55)]),
                   col, rgb('#2A1A10'))
    _emit(nt, col)
    return mat


def _box(bm, x0, z0, x1, z1, y0, y1, mat=0):
    add_prism(bm, [(x0, z0), (x1, z0), (x1, z1), (x0, z1)], y0, y1, XZ, mat)


def build_map(scene):
    cols = {}
    for name in ('BG', 'FLOOR', 'SHELF', 'LID'):
        cols[name] = bpy.data.collections.new(name)
        scene.collection.children.link(cols[name])
    line = line_mat('PlatformLine', PLATFORM_LINE)

    def obj(name, coll, mats, build, outline=0.0, smooth=False):
        bm = bmesh.new()
        build(bm)
        ob = new_obj(name, bm, mats, coll=cols[coll], smooth=smooth)
        if outline:
            add_outline(ob, line, outline)
        return ob

    # ---- background (y >= 1)
    obj('Wall', 'BG', [wall_mat()], lambda bm: _box(bm, -4, -4, 28, 16, 6.0, 6.2))
    frame = flat_mat('WindowFrame', '#1E1C24')
    glass = flat_mat('Glass', '#1C2847')
    moon = flat_mat('Moon', '#A8B5D2')
    halo = flat_mat('MoonHalo', '#25355A')
    plant = flat_mat('Plant', '#18241F')
    pot = flat_mat('Pot', '#1A1B21')
    obj('Window', 'BG', [frame, glass, halo, moon, plant, pot], lambda bm: (
        _box(bm, 1.5, 7.3, 5.7, 11.7, 5.0, 5.2, 0),
        _box(bm, 1.7, 7.5, 5.5, 11.5, 4.8, 5.0, 1),
        add_prism(bm, ellipse_pts(4.55, 10.65, 0.66, 0.66, 32), 4.6, 4.8, XZ, 2),
        add_prism(bm, ellipse_pts(4.55, 10.65, 0.42, 0.42, 32), 4.4, 4.6, XZ, 3),
        _box(bm, 3.54, 7.5, 3.66, 11.5, 4.2, 4.4, 0),
        _box(bm, 1.7, 9.44, 5.5, 9.56, 4.2, 4.4, 0),
        _box(bm, 1.3, 7.12, 5.9, 7.34, 4.2, 4.4, 0),
        add_prism(bm, [(2.0, 7.34), (2.7, 7.34), (2.8, 7.9), (1.9, 7.9)], 4.0, 4.2, XZ, 5),
        add_prism(bm, ellipse_pts(2.15, 8.25, 0.32, 0.18, 16), 4.0, 4.2, XZ, 4),
        add_prism(bm, ellipse_pts(2.6, 8.4, 0.34, 0.2, 16), 4.0, 4.2, XZ, 4),
        add_prism(bm, ellipse_pts(2.35, 8.65, 0.22, 0.32, 16), 4.0, 4.2, XZ, 4)))
    hood = flat_mat('Hood', '#22252D')
    hood_lip = flat_mat('HoodLip', '#383B45')
    lamp = flat_mat('Lamp', '#F0C27A')
    obj('Hood', 'BG', [hood, hood_lip, lamp], lambda bm: (
        add_prism(bm, [(16.6, 9.5), (23.4, 9.5), (22.4, 11.2), (17.6, 11.2)], 4.0, 4.2, XZ, 0),
        _box(bm, 19.1, 11.2, 20.9, 14.5, 4.2, 4.4, 0),
        _box(bm, 16.5, 9.32, 23.5, 9.52, 3.8, 4.0, 1),
        _box(bm, 17.4, 9.24, 22.6, 9.32, 3.8, 4.0, 2)))
    utensil = flat_mat('Utensil', '#3A3F4A')
    slot = flat_mat('UtensilSlot', '#252932')
    obj('Utensils', 'BG', [utensil, slot], lambda bm: (
        add_tube(bm, [(0.9, 3.0, RAIL_Z), (0.9, 3.0, 10.8)], 0.06, sides=8),
        add_prism(bm, [(0.9 + 0.44 * math.cos(a), 10.8 + 0.44 * math.sin(a)) for a in
                       (math.pi + math.pi * i / 16 for i in range(17))], 2.9, 3.1, XZ, 0),
        add_tube(bm, [(15.70, 3.0, RAIL_Z), (15.70, 3.0, 11.45)], 0.055, sides=8),
        _box(bm, 15.32, 10.25, 16.08, 11.5, 2.9, 3.1, 0),
        _box(bm, 15.48, 10.45, 15.56, 11.3, 2.7, 2.9, 1),
        _box(bm, 15.66, 10.45, 15.74, 11.3, 2.7, 2.9, 1),
        _box(bm, 15.84, 10.45, 15.92, 11.3, 2.7, 2.9, 1)))
    rail = flat_mat('Rail', '#2C2F37')
    rail_top = flat_mat('RailTop', '#4A4E58')
    obj('Rail', 'BG', [rail, rail_top], lambda bm: (
        _box(bm, -2.0, RAIL_Z - 0.08, 26.0, RAIL_Z + 0.08, 1.0, 1.2, 0),
        _box(bm, -2.0, RAIL_Z + 0.05, 26.0, RAIL_Z + 0.08, 0.9, 1.0, 1),
        _box(bm, 0.3, RAIL_Z, 0.5, 14.5, 1.2, 1.4, 0),
        _box(bm, 11.9, RAIL_Z, 12.1, 14.5, 1.2, 1.4, 0),
        _box(bm, 23.5, RAIL_Z, 23.7, 14.5, 1.2, 1.4, 0)))
    chain = flat_mat('Chain', '#59606C')
    chain_dark = flat_mat('ChainDark', '#3E434D')

    def chains(bm):
        pitch = 0.19
        for cx in CHAIN_X:
            z, i = LID_TOP + 0.16, 0
            while z < RAIL_Z:
                if i % 2 == 0:
                    pts = [(cx + 0.065 * math.cos(a), 1.5, z + 0.12 * math.sin(a))
                           for a in (2 * math.pi * k / 16 for k in range(16))]
                    add_tube(bm, pts, 0.022, mat=0, sides=6, closed=True)
                else:
                    add_tube(bm, [(cx, 1.45, z - 0.12), (cx, 1.45, z + 0.12)], 0.024, mat=1, sides=6)
                z += pitch
                i += 1
    obj('Chains', 'BG', [chain, chain_dark], chains)

    # ---- floor: cutting board, top at y = 0
    board = wood_mat('Board', '#7E5030', '#5E3A22', '#43281A', plank=1.7, fade_from=-0.3, fade_to=-1.5)
    board_top = flat_mat('BoardTop', '#E2A866')
    board_lip = flat_mat('BoardLip', '#5A3820')
    obj('Board', 'FLOOR', [board, board_top, board_lip], lambda bm: (
        _box(bm, -2.0, -2.5, 26.0, 0.0, -1.0, 1.0, 0),
        _box(bm, -2.0, -0.07, 26.0, 0.0, -1.02, -1.0, 1),
        _box(bm, -2.0, -0.1, 26.0, -0.07, -1.02, -1.0, 2)))

    # ---- left shelf (the right one reuses the same sprite)
    x0, x1, top = PLATFORMS[0]
    plank = wood_mat('Shelf', '#9A653D', '#704626', '#55341E', plank=2.0)
    plank_top = flat_mat('ShelfTop', '#F0BE78')
    iron = flat_mat('Bracket', '#30333C')
    screw = flat_mat('Screw', '#6A707C')
    obj('ShelfPlank', 'SHELF', [plank, plank_top], lambda bm: (
        _box(bm, x0, top - 0.26, x1, top, -0.3, 0.3, 0),
        _box(bm, x0 + 0.02, top - 0.06, x1 - 0.02, top - 0.0, -0.31, -0.3, 1)), outline=0.03)
    obj('ShelfBrackets', 'SHELF', [iron, screw], lambda bm: [
        (add_prism(bm, [(bx - 0.09, top - 0.26), (bx + 0.09, top - 0.26), (bx + 0.09, top - 0.72),
                        (bx, top - 0.82), (bx - 0.09, top - 0.72)], -0.2, 0.2, XZ, 0),
         add_prism(bm, ellipse_pts(bx, top - 0.45, 0.03, 0.03, 10), -0.24, -0.2, XZ, 1),
         add_prism(bm, ellipse_pts(bx, top - 0.65, 0.03, 0.03, 10), -0.24, -0.2, XZ, 1))
        for bx in (x0 + 0.75, x1 - 0.75)], outline=0.03)

    # ---- pot lid hanging upside down, rim top at LID_TOP
    x0, x1, top = PLATFORMS[2]
    cx = (x0 + x1) / 2
    steel = toon_mat('Steel', '#8890A0', '#4A505E', spec=0.8, mid=0.6)
    rim_col = flat_mat('LidRim', '#E9D7B0')
    knob = toon_mat('Knob', '#3A3C44', '#22232A', spec=0.6)
    lid_m = Matrix.Translation((cx, 0, 0))
    r = (x1 - x0) / 2
    obj('LidDome', 'LID', [steel], lambda bm: add_revolve(
        bm, [(0, top - 0.55), (0.9, top - 0.5), (1.8, top - 0.38), (2.5, top - 0.22), (r - 0.06, top - 0.1),
             (r, top - 0.06), (r, top - 0.0), (0, top - 0.0)], 64, lid_m), outline=0.03, smooth=True)
    obj('LidRim', 'LID', [rim_col], lambda bm: _box(bm, x0 + 0.02, top - 0.05, x1 - 0.02, top, -3.2, -3.1))
    obj('LidKnob', 'LID', [knob], lambda bm: (
        add_revolve(bm, [(0, top - 0.5), (0.16, top - 0.5), (0.13, top - 0.72), (0, top - 0.72)], 24, lid_m),
        add_ellipsoid(bm, (0, 0, top - 0.86), (0.26, 0.26, 0.18), lid_m)), outline=0.03, smooth=True)
    obj('LidEyelets', 'LID', [steel], lambda bm: [
        add_tube(bm, [(ex + 0.07 * math.cos(a), -0.2, top + 0.02 + 0.09 * math.sin(a))
                      for a in (2 * math.pi * k / 16 for k in range(16))], 0.022, sides=6, closed=True)
        for ex in CHAIN_X], outline=0.02)
    return cols


def target_map():
    scene = reset_scene()
    add_camera(scene, 0, 0, 1.0)
    add_sun(scene, (-0.5, -0.8, 0.6), energy=3.0)
    cols = build_map(scene)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, 'kitchen.blend'), compress=True)
    out_dir = os.path.join(SPRITES, 'map')
    os.makedirs(out_dir, exist_ok=True)

    def only(*names):
        for n, c in cols.items():
            c.hide_render = n not in names

    lid_c = (PLATFORMS[2][0] + PLATFORMS[2][1]) / 2
    shelf_c = (PLATFORMS[0][0] + PLATFORMS[0][1]) / 2

    def rel(rect, cx, cz):
        return (cx + rect[0], cz + rect[1], cx + rect[2], cz + rect[3])

    pieces = [
        ('kitchen_bg.png', ('BG',), BG_RECT),
        ('floor_board.png', ('FLOOR',), FLOOR_RECT),
        ('shelf.png', ('SHELF',), rel(SHELF_RECT, shelf_c, SHELF_TOP)),
        ('pot_lid.png', ('LID',), rel(LID_RECT, lid_c, LID_TOP)),
        ('kitchen_full.png', ('BG', 'FLOOR', 'SHELF', 'LID'), BG_RECT),
    ]
    meta = dict(ppu=MAP_PPU, note='world_rect = [x0, y0, x1, y1] in game units covered by the image; '
                                  'pivot bottom-left at (x0, y0). kitchen_full has only the left shelf: '
                                  'draw shelf.png again at the right shelf rect.', pieces={})
    for fname, names, rect in pieces:
        only(*names)
        frame_camera(scene, *rect, MAP_PPU)
        render_to(scene, os.path.join(out_dir, fname))
        meta['pieces'][fname] = dict(world_rect=list(rect))
    meta['pieces']['shelf.png']['instances'] = [list(rel(SHELF_RECT, (a + b) / 2, t)) for a, b, t in PLATFORMS[:2]]

    # kitchen_full: add the right shelf by copying the left one
    full = load_png(os.path.join(out_dir, 'kitchen_full.png'))
    shelf = load_png(os.path.join(out_dir, 'shelf.png'))
    rx0, ry0 = meta['pieces']['shelf.png']['instances'][1][:2]
    over(full, shelf, round((rx0 - BG_RECT[0]) * MAP_PPU), round((ry0 - BG_RECT[1]) * MAP_PPU))
    save_png(full, os.path.join(out_dir, 'kitchen_full.png'))
    with open(os.path.join(out_dir, 'map.json'), 'w', encoding='utf-8') as fh:
        json.dump(meta, fh, indent=2)


# ================================================================ preview (composite check images)

WARN_RGB = (1.0, 0.25, 0.2)    # Client SwordView.WarnColor


def _frame(sheet, row, col, rows):
    fs = CHAR_FRAME
    y0 = (rows - 1 - row) * fs
    return sheet[y0:y0 + fs, col * fs:(col + 1) * fs].copy()


def target_preview():
    reset_scene()
    os.makedirs(PREVIEW, exist_ok=True)
    cdir = os.path.join(SPRITES, 'characters')
    meta = json.load(open(os.path.join(cdir, 'jelly_sheet.json'), encoding='utf-8'))
    rows = len(meta['animations'])
    row_of = {a['name']: a['row'] for a in meta['animations']}
    sheets = {int(k): load_png(os.path.join(cdir, v)) for k, v in meta['sheets'].items()}

    # 1) line-up on the concept sheet background with the 0.8 x 1.2 hit box, colour and greyscale
    fs = CHAR_FRAME
    pad = 24
    W, H = 8 * fs + 9 * pad, fs + 2 * pad
    bg = np.zeros((H, W, 4), np.float32)
    bg[...] = [43 / 255, 46 / 255, 54 / 255, 1.0]
    for i in range(8):
        x = pad + i * (fs + pad)
        over(bg, _frame(sheets[i + 1], row_of['idle'], 0, rows), x, pad)
        px, py = x + CHAR_PIVOT[0], pad + CHAR_PIVOT[1]
        hw, hh = round(0.4 * CHAR_PPU), round(1.2 * CHAR_PPU)
        box = (1.0, 0.9, 0.3, 1.0)
        bg[py:py + hh, px - hw] = box
        bg[py:py + hh, px + hw] = box
        bg[py, px - hw:px + hw + 1] = box
        bg[py + hh, px - hw:px + hw + 1] = box
    save_png(bg, os.path.join(PREVIEW, 'characters_lineup.png'))
    gray = bg.copy()
    lum = 0.2126 * bg[..., 0] + 0.7152 * bg[..., 1] + 0.0722 * bg[..., 2]
    gray[..., 0] = gray[..., 1] = gray[..., 2] = lum
    save_png(gray, os.path.join(PREVIEW, 'characters_lineup_gray.png'))

    # 2) stage with characters, knives and warning columns at game scale (72 px per unit)
    mdir = os.path.join(SPRITES, 'map')
    full = load_png(os.path.join(mdir, 'kitchen_full.png'))
    s = MAP_PPU / CHAR_PPU
    fsz = round(fs * s)

    def to_px(x, y):
        return round((x - BG_RECT[0]) * MAP_PPU), round((y - BG_RECT[1]) * MAP_PPU)

    def put(img, piv, x, y, scale_w, scale_h, flip=False):
        im = resize(img, scale_w, scale_h)
        if flip:
            im = im[:, ::-1].copy()
        px, py = to_px(x, y)
        ox = round(piv[0] * scale_w / img.shape[1])
        if flip:
            ox = scale_w - ox
        over(full, im, px - ox, py - round(piv[1] * scale_h / img.shape[0]))

    def warn(x, land_y, strong):
        x0, _ = to_px(x - 0.2, 0)
        x1, _ = to_px(x + 0.2, 0)
        _, y0 = to_px(0, land_y)
        _, y1 = to_px(0, 14.0)
        col = np.zeros((y1 - y0, x1 - x0, 4), np.float32)
        col[...] = (*WARN_RGB, 0.22 if strong else 0.12)
        over(full, col, x0, y0)
        mk = np.zeros((round(0.12 * MAP_PPU), round(0.7 * MAP_PPU), 4), np.float32)
        mk[...] = (*WARN_RGB, 1.0)
        over(full, mk, to_px(x - 0.35, 0)[0], y0 - mk.shape[0] // 2)

    knife = load_png(os.path.join(SPRITES, 'knife', 'knife.png'))
    kw, kh = round(KNIFE_SIZE[0] * s), round(KNIFE_SIZE[1] * s)
    warn(5.6, SHELF_TOP, True)
    warn(13.3, LID_TOP, False)
    put(knife, KNIFE_PIVOT, 13.3, 8.2, kw, kh)
    put(knife, KNIFE_PIVOT, 18.9, SHELF_TOP - 0.3, kw, kh)
    put(knife, KNIFE_PIVOT, 2.9, -0.3, kw, kh)
    cast = [  # id, anim, frame, x, y, flip
        (1, 'idle', 0, 6.6, SHELF_TOP, True), (2, 'run', 1, 1.6, 0.0, False), (3, 'idle', 0, 12.2, LID_TOP, False),
        (4, 'jump', 0, 10.4, 1.6, False), (5, 'dash', 0, 15.8, 0.0, True), (6, 'idle', 0, 19.8, SHELF_TOP, True),
        (7, 'fall', 0, 22.6, 3.4, True), (8, 'knockback', 1, 14.1, LID_TOP + 0.3, False)]
    for pid, anim, fi, x, y, flip in cast:
        put(_frame(sheets[pid], row_of[anim], fi, rows), CHAR_PIVOT, x, y, fsz, fsz, flip)
    save_png(full, os.path.join(PREVIEW, 'kitchen_preview.png'))

    # 3) one sheet per animation for every colour: frame 0 of each animation, ids top to bottom
    small = 128
    grid = np.zeros((8 * small, rows * small, 4), np.float32)
    grid[...] = [43 / 255, 46 / 255, 54 / 255, 1.0]
    for pid in range(1, 9):
        for r in range(rows):
            n = meta['animations'][r]['frames']
            fr = _frame(sheets[pid], r, min(1, n - 1), rows)
            over(grid, resize(fr, small, small), r * small, (8 - pid) * small)
    save_png(grid, os.path.join(PREVIEW, 'characters_poses.png'))
    print('preview done')


# ================================================================ entry

def target_probe(out_dir):
    """Quick look while tuning: frame 0 of every animation for one player, and idle for all players."""
    scene = reset_scene()
    add_camera(scene, 0, 0, 2.0)
    add_sun(scene, CHAR_SUN, energy=3.0)
    half = CHAR_FRAME / CHAR_PPU / 2
    z0 = -CHAR_PIVOT[1] / CHAR_PPU
    frame_camera(scene, -half, z0, half, z0 + CHAR_FRAME / CHAR_PPU, CHAR_PPU)
    rig = build_characters(scene)
    os.makedirs(out_dir, exist_ok=True)
    pid = int(os.environ.get('PROBE_ID', '2'))
    set_player(rig, pid)
    starts, f = [], 1
    for _, _, _, frs in ANIMS:
        starts.append(f)
        f += len(frs)
    strip = np.zeros((CHAR_FRAME, CHAR_FRAME * len(starts), 4), np.float32)
    strip[...] = [43 / 255, 46 / 255, 54 / 255, 1.0]
    for i, f in enumerate(starts):
        scene.frame_set(f + (1 if f + 1 < (starts + [999])[i + 1] else 0))
        render_to(scene, os.path.join(out_dir, 'p.png'))
        over(strip, load_png(os.path.join(out_dir, 'p.png')), i * CHAR_FRAME, 0)
    save_png(strip, os.path.join(out_dir, 'poses_%d.png' % pid))
    strip = np.zeros((CHAR_FRAME, CHAR_FRAME * 8, 4), np.float32)
    strip[...] = [43 / 255, 46 / 255, 54 / 255, 1.0]
    scene.frame_set(1)
    for p in range(1, 9):
        set_player(rig, p)
        render_to(scene, os.path.join(out_dir, 'p.png'))
        over(strip, load_png(os.path.join(out_dir, 'p.png')), (p - 1) * CHAR_FRAME, 0)
    save_png(strip, os.path.join(out_dir, 'idle_all.png'))


TARGETS = dict(characters=target_characters, knife=target_knife, map=target_map, preview=target_preview,
               probe=lambda: target_probe(os.environ['PROBE_DIR']))


def main():
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    target = args[0] if args else 'all'
    if target == 'all':
        for t in ('characters', 'knife', 'map', 'preview'):
            subprocess.run([bpy.app.binary_path, '-b', '--factory-startup', '--python', os.path.abspath(__file__),
                            '--', t], check=True)
        return
    TARGETS[target]()


main()
