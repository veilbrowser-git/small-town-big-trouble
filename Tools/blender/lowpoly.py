"""Shared helpers for generating Small Town, Big Trouble art in Blender.

Everything is authored in UNITY coordinates (x right, y up, z forward, metres)
and converted to Blender space on build, so numbers in these scripts match
numbers in the Unity scene one to one.

Colour comes from one small palette atlas. Every face's UVs sit on the centre
of a swatch, so the whole game renders with a single material and texture.

Run scripts with:
    /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P Tools/blender/<script>.py
"""
import math
import os
import struct
import zlib

import bmesh
import bpy
from mathutils import Matrix, Vector

PROJECT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
MODEL_DIR = os.path.join(PROJECT, "Assets", "SmallTown", "Art", "Models")
PALETTE_PNG = os.path.join(MODEL_DIR, "Palette.png")

ATLAS_COLUMNS = 8
ATLAS_ROWS = 8
SWATCH_PIXELS = 8

# Order matters: the index is the atlas slot. Append new colours, never reorder.
PALETTE = [
    ("cream", "F6DFC0"), ("tan", "C9A27A"), ("orange", "D2562B"), ("maroon", "6E2F2A"),
    ("darkbrown", "4A2420"), ("white", "FFFFFF"), ("black", "1E1E24"), ("eye", "3A1F14"),
    ("asphalt", "4B4F5A"), ("sidewalk", "C9C4B8"), ("sand", "E8D9A8"), ("grass", "7EC8A0"),
    ("water", "3FA7D6"), ("water_deep", "2B7FB8"), ("wood", "B07A45"), ("wood_dark", "7A5230"),
    ("wall_cream", "F2E3C6"), ("wall_pink", "F2A6A0"), ("wall_teal", "6CC5B0"), ("wall_blue", "6FA8DC"),
    ("wall_yellow", "F6D06F"), ("brick", "C8553D"), ("roof_red", "B5443A"), ("roof_slate", "5C6B7A"),
    ("red", "E0453A"), ("green", "3FB56F"), ("yellow", "F4C430"), ("navy", "27456F"),
    ("metal", "9AA5B1"), ("metal_dark", "5A6470"), ("glass", "A8DDF0"), ("leaf", "4FA75A"),
    ("police_blue", "2F4FA8"), ("purple", "8E6BBF"), ("concrete", "AFAFA8"), ("dough", "E9C46A"),
    ("cheese", "FFD86B"), ("pepperoni", "B8322A"), ("grey_fur", "8C8F98"), ("dark_grey", "3A3D46"),
    ("line_white", "EEEEE8"), ("leaf_dark", "3B8548"), ("olive", "8A9A5B"), ("olive_dark", "5B6B3A"),
    ("pink", "F4A7B9"), ("sky", "8FD3F4"), ("grass_dark", "5FAE84"), ("sand_dark", "D2BE86"),
]
_INDEX = {name: i for i, (name, _) in enumerate(PALETTE)}


def colour_uv(name):
    """UV at the centre of a palette swatch."""
    index = _INDEX[name]
    column = index % ATLAS_COLUMNS
    row = index // ATLAS_COLUMNS
    return ((column + 0.5) / ATLAS_COLUMNS, 1.0 - (row + 0.5) / ATLAS_ROWS)


def write_palette_png(path=PALETTE_PNG, overrides=None):
    """Writes the atlas with a tiny pure-Python PNG encoder (no image library needed).

    overrides maps colour names to replacement hex values. It is how recoloured
    characters are made: same mesh, same UVs, different atlas."""
    overrides = overrides or {}
    width = ATLAS_COLUMNS * SWATCH_PIXELS
    height = ATLAS_ROWS * SWATCH_PIXELS
    rows = []
    for y in range(height):
        row = bytearray([0])
        for x in range(width):
            index = (y // SWATCH_PIXELS) * ATLAS_COLUMNS + (x // SWATCH_PIXELS)
            if index < len(PALETTE):
                rgb = bytes.fromhex(overrides.get(PALETTE[index][0], PALETTE[index][1]))
            else:
                rgb = b"\xff\x00\xff"
            row += rgb
        rows.append(bytes(row))

    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(b"".join(rows), 9))
    png += chunk(b"IEND", b"")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as handle:
        handle.write(png)
    return path


def unity_to_blender(v):
    """Unity (x, y, z) -> Blender (x, z, y).

    Verified against a real import: with the FBX export settings below and Unity's
    "Bake Axis Conversion" import option, a Blender point (bx, by, bz) arrives in
    Unity at (bx, bz, by). TownSceneBuilder checks this on every build.
    """
    return Vector((v[0], v[2], v[1]))


def rotation(yaw=0.0, pitch=0.0, roll=0.0):
    """Unity-style rotation in degrees: yaw about Y, pitch about X, roll about Z (applied Z, X, then Y)."""
    ry = Matrix.Rotation(math.radians(yaw), 3, "Y")
    rx = Matrix.Rotation(math.radians(pitch), 3, "X")
    rz = Matrix.Rotation(math.radians(roll), 3, "Z")
    # These matrices act on Unity-space numbers. The standard rotation formulas give
    # the same numeric result Unity does (yaw 90 sends +Z to +X), so no inversion.
    return ry @ rx @ rz


class MeshBuilder:
    """Collects coloured polygons in Unity space and turns them into one Blender mesh."""

    def __init__(self):
        self.vertices = []
        self.faces = []
        self.face_colours = []
        # Each primitive is a convex shell. Faces remember which shell they belong to so
        # build() can point every face away from its shell's centre. (Recalculating
        # normals does not work here: faces do not share vertices, so there is no
        # connected surface for Blender to reason about.)
        self.face_shells = []
        self.shell_centres = []
        self._shell_depth = 0
        self._shell_start = 0

    # ---------------------------------------------------------------- primitives

    def _begin_shell(self):
        if self._shell_depth == 0:
            self._shell_start = len(self.vertices)
            self.shell_centres.append(None)
        self._shell_depth += 1

    def _end_shell(self):
        self._shell_depth -= 1
        if self._shell_depth == 0:
            added = self.vertices[self._shell_start:]
            centre = Vector((0.0, 0.0, 0.0))
            for v in added:
                centre += v
            self.shell_centres[-1] = centre / max(1, len(added))

    def polygon(self, points, colour):
        start = len(self.vertices)
        self.vertices.extend(Vector(p) for p in points)
        self.faces.append(list(range(start, start + len(points))))
        self.face_colours.append(colour)
        self.face_shells.append(len(self.shell_centres) - 1 if self._shell_depth > 0 else -1)

    def hull(self, bottom, top, colour, cap_colour=None, top_colour=None):
        """A closed solid between two matching rings of points (same count, same order)."""
        self._begin_shell()
        try:
            count = len(bottom)
            for i in range(count):
                j = (i + 1) % count
                self.polygon([bottom[i], bottom[j], top[j], top[i]], colour)
            self.polygon(list(reversed(bottom)), cap_colour or colour)
            self.polygon(list(top), top_colour or cap_colour or colour)
        finally:
            self._end_shell()

    def box(self, centre, size, colour, yaw=0.0, pitch=0.0, roll=0.0, top_colour=None):
        """Axis box of full `size`, optionally rotated about its centre."""
        self._begin_shell()
        try:
            hx, hy, hz = size[0] / 2.0, size[1] / 2.0, size[2] / 2.0
            rot = rotation(yaw, pitch, roll)
            c = Vector(centre)

            def p(x, y, z):
                return c + rot @ Vector((x, y, z))

            bottom = [p(-hx, -hy, -hz), p(hx, -hy, -hz), p(hx, -hy, hz), p(-hx, -hy, hz)]
            top = [p(-hx, hy, -hz), p(hx, hy, -hz), p(hx, hy, hz), p(-hx, hy, hz)]
            self.hull(bottom, top, colour, top_colour=top_colour)
        finally:
            self._end_shell()

    def tapered(self, centre, bottom_size, top_size, height, colour, yaw=0.0, pitch=0.0, roll=0.0,
                top_offset=(0.0, 0.0), sides=4, top_colour=None):
        """A frustum standing on Y: an n-gon of bottom_size (x, z) narrowing to top_size, `height` tall,
        centred on `centre`. sides=4 gives a tapered box; more sides give a faceted cylinder or cone."""
        self._begin_shell()
        try:
            rot = rotation(yaw, pitch, roll)
            c = Vector(centre)

            def ring(size, y, offset):
                pts = []
                for i in range(sides):
                    angle = 2.0 * math.pi * (i + 0.5) / sides
                    scale = 1.0 / math.cos(math.pi / sides) if sides == 4 else 1.0
                    x = math.cos(angle) * size[0] / 2.0 * scale + offset[0]
                    z = math.sin(angle) * size[1] / 2.0 * scale + offset[1]
                    pts.append(c + rot @ Vector((x, y, z)))
                return pts

            self.hull(ring(bottom_size, -height / 2.0, (0.0, 0.0)), ring(top_size, height / 2.0, top_offset),
                      colour, top_colour=top_colour)
        finally:
            self._end_shell()

    def wedge(self, centre, size, colour, yaw=0.0):
        """A ramp: full height at +Z falling to nothing at -Z. `size` is the bounding box."""
        self._begin_shell()
        try:
            hx, hy, hz = size[0] / 2.0, size[1] / 2.0, size[2] / 2.0
            rot = rotation(yaw)
            c = Vector(centre)

            def p(x, y, z):
                return c + rot @ Vector((x, y, z))

            a, b = p(-hx, -hy, -hz), p(hx, -hy, -hz)
            d, e = p(hx, -hy, hz), p(-hx, -hy, hz)
            f, g = p(hx, hy, hz), p(-hx, hy, hz)
            self.polygon([a, e, d, b], colour)      # bottom
            self.polygon([a, b, f, g], colour)      # slope
            self.polygon([e, g, f, d], colour)      # back
            self.polygon([a, g, e], colour)
            self.polygon([b, d, f], colour)
        finally:
            self._end_shell()

    def gable_roof(self, centre, size, colour, yaw=0.0, end_colour=None):
        """Ridge runs along local X. `size` is the bounding box; centre is the middle of that box."""
        self._begin_shell()
        try:
            hx, hy, hz = size[0] / 2.0, size[1] / 2.0, size[2] / 2.0
            rot = rotation(yaw)
            c = Vector(centre)

            def p(x, y, z):
                return c + rot @ Vector((x, y, z))

            a, b, d, e = p(-hx, -hy, -hz), p(hx, -hy, -hz), p(hx, -hy, hz), p(-hx, -hy, hz)
            r0, r1 = p(-hx, hy, 0.0), p(hx, hy, 0.0)
            self.polygon([a, e, d, b], colour)
            self.polygon([a, b, r1, r0], colour)
            self.polygon([d, e, r0, r1], colour)
            self.polygon([a, r0, e], end_colour or colour)
            self.polygon([b, d, r1], end_colour or colour)
        finally:
            self._end_shell()

    def blob(self, centre, size, colour, yaw=0.0, pitch=0.0, roll=0.0, rings=2, sides=6, squash_top=1.0):
        """A chunky faceted ellipsoid: low-poly rocks, bushes, heads, bellies."""
        self._begin_shell()
        try:
            rot = rotation(yaw, pitch, roll)
            c = Vector(centre)
            levels = rings + 2
            loops = []
            for level in range(1, levels):
                t = level / levels
                y = -math.cos(math.pi * t) * size[1] / 2.0
                radius = math.sin(math.pi * t)
                if y > 0:
                    y *= squash_top
                loop = []
                for i in range(sides):
                    angle = 2.0 * math.pi * (i + (0.5 if level % 2 else 0.0)) / sides
                    loop.append(c + rot @ Vector((math.cos(angle) * radius * size[0] / 2.0, y,
                                                  math.sin(angle) * radius * size[2] / 2.0)))
                loops.append(loop)
            bottom_tip = c + rot @ Vector((0.0, -size[1] / 2.0, 0.0))
            top_tip = c + rot @ Vector((0.0, size[1] / 2.0 * squash_top, 0.0))
            for i in range(sides):
                j = (i + 1) % sides
                self.polygon([bottom_tip, loops[0][j], loops[0][i]], colour)
                self.polygon([top_tip, loops[-1][i], loops[-1][j]], colour)
            for a, b in zip(loops, loops[1:]):
                for i in range(sides):
                    j = (i + 1) % sides
                    self.polygon([a[i], a[j], b[j], b[i]], colour)
        finally:
            self._end_shell()

    # ---------------------------------------------------------------- output

    def bounds(self):
        xs = [v.x for v in self.vertices]
        ys = [v.y for v in self.vertices]
        zs = [v.z for v in self.vertices]
        return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))

    def build(self, name, collection=None):
        """Creates the Blender object. Its origin is the Unity-space origin the points were given in."""
        mesh = bpy.data.meshes.new(name)
        bm = bmesh.new()
        verts = [bm.verts.new(unity_to_blender(v)) for v in self.vertices]
        uv_layer = bm.loops.layers.uv.new("UVMap")
        shelled = []
        loose = []
        # Builders filled by copying faces across directly have no shell records; treat those as loose.
        shells = list(self.face_shells) + [-1] * (len(self.faces) - len(self.face_shells))
        if len(self.shell_centres) == 0:
            shells = [-1] * len(self.faces)
        for indices, colour, shell in zip(self.faces, self.face_colours, shells):
            try:
                face = bm.faces.new([verts[i] for i in indices])
            except ValueError:
                continue  # degenerate or duplicate face
            uv = colour_uv(colour)
            for loop in face.loops:
                loop[uv_layer].uv = uv
            face.smooth = False
            if 0 <= shell < len(self.shell_centres) and self.shell_centres[shell] is not None:
                shelled.append((face, shell))
            else:
                loose.append(face)
        bm.normal_update()
        # Faces made by a primitive point away from that primitive's centre.
        for face, shell in shelled:
            outward = face.calc_center_median() - unity_to_blender(self.shell_centres[shell])
            if face.normal.dot(outward) < 0.0:
                face.normal_flip()
        # Hand-placed polygons have no shell; fall back to Blender's best guess.
        if loose:
            bmesh.ops.recalc_face_normals(bm, faces=loose)
        bm.normal_update()
        bm.to_mesh(mesh)
        bm.free()
        mesh.materials.append(palette_material())
        obj = bpy.data.objects.new(name, mesh)
        (collection or bpy.context.scene.collection).objects.link(obj)
        return obj


def palette_material():
    material = bpy.data.materials.get("Palette")
    if material is not None:
        return material
    if not os.path.exists(PALETTE_PNG):
        write_palette_png()
    material = bpy.data.materials.new("Palette")
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    bsdf = nodes.get("Principled BSDF")
    texture = nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(PALETTE_PNG, check_existing=True)
    texture.interpolation = "Closest"
    links.new(texture.outputs["Color"], bsdf.inputs["Base Color"])
    if "Roughness" in bsdf.inputs:
        bsdf.inputs["Roughness"].default_value = 0.9
    return material


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def export_fbx(path, objects=None):
    """Exports with the settings Unity expects. Pass objects to export only those."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    if objects is not None:
        for obj in objects:
            obj.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=objects is not None,
        object_types={"MESH", "EMPTY"},
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=False,
        mesh_smooth_type="OFF",
        use_mesh_modifiers=True,
        add_leaf_bones=False,
        bake_anim=False,
        path_mode="STRIP",
    )
    return path


def render_preview(path, eye, target, width=1600, height=900, lens=35.0, sun=(50.0, 0.0, 35.0), ortho_scale=None):
    """Renders the scene with EEVEE. `eye` and `target` are Unity-space points."""
    scene = bpy.context.scene
    camera_data = bpy.data.cameras.new("PreviewCamera")
    camera_data.lens = lens
    camera_data.clip_end = 500.0
    if ortho_scale:
        camera_data.type = "ORTHO"
        camera_data.ortho_scale = ortho_scale
    camera = bpy.data.objects.new("PreviewCamera", camera_data)
    scene.collection.objects.link(camera)
    camera.location = unity_to_blender(eye)
    direction = unity_to_blender(target) - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    scene.camera = camera

    light_data = bpy.data.lights.new("PreviewSun", "SUN")
    light_data.energy = 3.5
    light = bpy.data.objects.new("PreviewSun", light_data)
    scene.collection.objects.link(light)
    light.rotation_euler = (math.radians(sun[0]), math.radians(sun[1]), math.radians(sun[2]))

    world = bpy.data.worlds.new("PreviewWorld")
    world.use_nodes = True
    background = world.node_tree.nodes.get("Background")
    if background is not None:
        background.inputs[0].default_value = (0.56, 0.83, 0.96, 1.0)
        background.inputs[1].default_value = 1.0
    scene.world = world

    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    scene.render.filepath = path
    scene.view_settings.view_transform = "Standard"
    bpy.ops.render.render(write_still=True)

    bpy.data.objects.remove(camera)
    bpy.data.objects.remove(light)
    return path
