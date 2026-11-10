"""High-resolution character and knife renders for the Store art (poster, box art, hero, icon).

Uses the same Blender models as the game sprites (Art/blender/build_sprites.py), only at a higher pixel density,
so the Store images match the game exactly without blurry upscaling.

Run from the repository root (Blender 5.2). Each kind in its own process:
  blender -b --factory-startup --python Tools/StorePackage/render_store_art.py -- chars jelly <out_dir> <ppu>
  blender -b Art/blender/knife.blend --python Tools/StorePackage/render_store_art.py -- knife <out_dir> <scale>

chars writes <kind>_<pid>_<anim><frame>.png for the poses listed in POSES (transparent, 2 x 2 units frame,
feet 8/128 of a unit above the bottom). knife writes knife.png (tip down, same framing as the game sprite).
Nothing in Art/blender or Art/sprites is written.
"""
import os
import sys

import bpy

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
sys.path.insert(0, os.path.join(ROOT, 'Art', 'blender'))

# pose list: (player id, animation, frame index in that animation)
POSES = [(p, 'idle', 0) for p in range(1, 9)] + [(p, 'jump', 1) for p in range(1, 9)] + \
        [(p, 'fall', 1) for p in range(1, 9)] + [(p, 'run', 2) for p in range(1, 9)] + \
        [(p, 'dash', 0) for p in (1, 2, 6)] + [(p, 'victory', 2) for p in range(1, 9)]


def chars(kind, out_dir, ppu):
    import build_sprites as bs
    scene = bs.reset_scene()
    bs.add_camera(scene, 0, 0, 2.0)
    bs.add_sun(scene, bs.CHAR_SUN, energy=3.0)
    half = bs.CHAR_FRAME / bs.CHAR_PPU / 2
    z0 = -bs.CHAR_PIVOT[1] / bs.CHAR_PPU
    bs.frame_camera(scene, -half, z0, half, z0 + bs.CHAR_FRAME / bs.CHAR_PPU, ppu)
    bs.set_kind(kind)
    rig = bs.build_characters(scene)
    starts, f = {}, 1
    for name, _, _, frs in bs.ANIMS:
        starts[name] = (f, len(frs))
        f += len(frs)
    os.makedirs(out_dir, exist_ok=True)
    for pid, anim, i in POSES:
        if anim not in starts:
            print('skip unknown animation', anim)
            continue
        first, n = starts[anim]
        bs.set_player(rig, pid)
        scene.frame_set(first + min(i, n - 1))
        bs.render_to(scene, os.path.join(out_dir, '%s_%d_%s%d.png' % (kind, pid, anim, i)))


def knife(out_dir, scale):
    scene = bpy.context.scene
    scene.render.resolution_x *= scale
    scene.render.resolution_y *= scale
    os.makedirs(out_dir, exist_ok=True)
    scene.render.filepath = os.path.join(out_dir, 'knife.png')
    bpy.ops.render.render(write_still=True)


def main():
    args = sys.argv[sys.argv.index('--') + 1:]
    if args[0] == 'chars':
        chars(args[1], args[2], int(args[3]))
    elif args[0] == 'knife':
        knife(args[1], int(args[2]))


main()
