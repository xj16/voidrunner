"""
generate_sprites.py — procedural sprite art for VoidRunner, built and rendered in Blender.

VoidRunner ships source-only and is fully playable with runtime-generated procedural shapes (see
Assets/Scripts/Gameplay/SpriteFactory.cs), so NO binary art is required. This script is the
optional "nicer art" path: it builds a set of low-poly emissive meshes in Blender and renders them
to transparent PNGs sized for the game's 64 px sprite grid. Drop the PNGs next to a content pack and
reference them by name (e.g. "sprite": "drifter") to replace the procedural shapes.

Run headless:
    blender --background --python art/blender/generate_sprites.py -- --out Assets/StreamingAssets/ContentPacks/base

Run inside Blender:
    open the Scripting tab, load this file, press Run.

Requires Blender 3.x or 4.x (ships its own Python; no pip installs needed). Tested with Blender 4.2.
"""

import sys
import os
import math

try:
    import bpy
    import bmesh
    from mathutils import Vector
    _IN_BLENDER = True
except ImportError:  # allow importing this file outside Blender (e.g. for linting) without crashing
    _IN_BLENDER = False


# Each sprite: name -> (shape, hex color). Colors match the base content pack tints.
SPRITES = {
    "drifter":  ("circle",   (0.50, 0.71, 1.00)),
    "swarmer":  ("circle",   (0.62, 1.00, 0.54)),
    "lancer":   ("triangle", (1.00, 0.54, 0.36)),
    "wisp":     ("diamond",  (0.90, 0.54, 1.00)),
    "mote":     ("square",   (1.00, 0.89, 0.48)),
    "warden":   ("ring",     (1.00, 0.36, 0.48)),
    "player":   ("triangle", (0.40, 0.90, 1.00)),
    "bolt":     ("capsule",  (1.00, 0.91, 0.65)),
}

RESOLUTION = 128  # rendered at 2x the in-game 64px grid for crisp downscaling


def parse_out_dir(default="."):
    argv = sys.argv
    if "--" in argv:
        extra = argv[argv.index("--") + 1:]
        if "--out" in extra:
            return extra[extra.index("--out") + 1]
    return default


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for block in (bpy.data.meshes, bpy.data.materials):
        for item in list(block):
            block.remove(item)


def emissive_material(name, rgb):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links
    nodes.clear()

    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = (rgb[0], rgb[1], rgb[2], 1.0)
    emission.inputs["Strength"].default_value = 1.5

    output = nodes.new("ShaderNodeOutputMaterial")
    links.new(emission.outputs["Emission"], output.inputs["Surface"])
    return mat


def make_shape(shape):
    """Creates a flat mesh in the XY plane for the given shape name; returns the object."""
    mesh = bpy.data.meshes.new(shape)
    obj = bpy.data.objects.new(shape, mesh)
    bpy.context.collection.objects.link(obj)

    bm = bmesh.new()
    r = 0.9

    if shape == "circle":
        bmesh.ops.create_circle(bm, cap_ends=True, radius=r, segments=48)
    elif shape == "ring":
        # Outer minus inner: build a circle and inset it to leave a ring.
        bmesh.ops.create_circle(bm, cap_ends=True, radius=r, segments=48)
        bmesh.ops.inset_individual(bm, faces=bm.faces, thickness=r * 0.45)
        # Delete the inner face(s) to leave a hollow ring.
        inner = [f for f in bm.faces if f.calc_area() < math.pi * (r * 0.55) ** 2 * 0.5]
        bmesh.ops.delete(bm, geom=inner, context="FACES")
    elif shape == "square":
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=r * 0.8)
    elif shape == "triangle":
        verts = [bm.verts.new((0, r, 0)),
                 bm.verts.new((-r * 0.87, -r * 0.5, 0)),
                 bm.verts.new((r * 0.87, -r * 0.5, 0))]
        bm.faces.new(verts)
    elif shape == "diamond":
        verts = [bm.verts.new((0, r, 0)), bm.verts.new((r, 0, 0)),
                 bm.verts.new((0, -r, 0)), bm.verts.new((-r, 0, 0))]
        bm.faces.new(verts)
    elif shape == "capsule":
        # A stadium/capsule pointing +X, used for projectiles.
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=1.0)
        bmesh.ops.scale(bm, vec=Vector((0.9, 0.28, 1.0)), verts=bm.verts)
    else:
        bmesh.ops.create_circle(bm, cap_ends=True, radius=r, segments=32)

    bm.to_mesh(mesh)
    bm.free()
    return obj


def setup_render(out_path):
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in _engines() else scene.render.engine
    scene.render.resolution_x = RESOLUTION
    scene.render.resolution_y = RESOLUTION
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.filepath = out_path

    # Orthographic top-down camera framing a ~2-unit area.
    cam_data = bpy.data.cameras.new("Cam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 2.2
    cam = bpy.data.objects.new("Cam", cam_data)
    cam.location = (0, 0, 5)
    bpy.context.collection.objects.link(cam)
    scene.camera = cam


def _engines():
    try:
        return {item.identifier for item in
                bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items}
    except Exception:
        return set()


def render_all(out_dir):
    os.makedirs(out_dir, exist_ok=True)
    for name, (shape, rgb) in SPRITES.items():
        clear_scene()
        obj = make_shape(shape)
        obj.data.materials.append(emissive_material(name, rgb))
        out_file = os.path.join(out_dir, f"{name}.png")
        setup_render(out_file)
        bpy.ops.render.render(write_still=True)
        print(f"[VoidRunner] rendered {out_file}")


def main():
    if not _IN_BLENDER:
        print("This script must be run inside Blender (blender --background --python ...).")
        return
    out_dir = parse_out_dir(".")
    render_all(out_dir)
    print(f"[VoidRunner] done. {len(SPRITES)} sprites written to {out_dir}")


if __name__ == "__main__":
    main()
