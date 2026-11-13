"""Builds the red panda player character as separate low-poly parts.

Run from the project root:
    /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P Tools/blender/build_character.py

Writes:
    Assets/SmallTown/Art/Models/Panda.fbx   Head, Torso, UpperArm, Forearm, HandL, HandR, PoliceCap, Visor
    Docs/panda-model.png                    assembled turnaround (preview only, not in the FBX)
    Docs/panda-hands.png                    hands from the back and from the palm side

Everything is authored in UNITY coordinates (x right, y up, z forward, metres).

Part conventions (Unity code depends on these):
    Head        origin = centre of the skull at eye height, faces +Z
    Torso       origin = base of the neck (top centre), hangs down to y = -0.44, faces +Z,
                tail on -Z. A short neck stub rises above the origin (to y = +0.09) so no gap shows
                under the head, and orange shoulder caps cover the arm pivots at (+-0.21, -0.07, 0).
    UpperArm    unit segment on +Z: z -0.5 (shoulder) .. +0.5 (elbow), about 1 x 1 across
    Forearm     unit segment on +Z: z -0.5 (elbow) .. +0.5 (wrist), 0.9 across growing to 1.25
    HandL/R     origin = palm centre, fingers +Z, palm faces -Y, thumb on +X (L) / -X (R)
    PoliceCap   same origin as Head
    Visor       same origin as Head

Set PANDA_DEBUG_DIR to a folder to also render close-ups there while tuning the look.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lowpoly as lp  # noqa: E402

import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

V = Vector
X_AXIS, Y_AXIS, Z_AXIS = V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))
MIRROR_X = Matrix.Diagonal((-1.0, 1.0, 1.0))

FBX_PATH = os.path.join(lp.MODEL_DIR, "Panda.fbx")
DOCS_DIR = os.path.join(lp.PROJECT, "Docs")

# Ears follow the reference sheet: cream shell (front rim and back) around a rust-coloured hollow.
# For "orange outside, cream inside" instead, set EAR_BACK = "orange" and EAR_INNER = EAR_FLOOR = "cream".
EAR_BACK = "cream"
EAR_RIM = "cream"
EAR_INNER = "orange"
EAR_FLOOR = "maroon"
SHOULDER_CAPS = True
HAND_COLOUR = "darkbrown"
FINGER_COLOUR = "darkbrown"


# ============================================================================ helpers

def poly(mb, points, colour):
    """mb.polygon, but drops repeated points so collapsed rings do not make zero-area faces."""
    out = []
    for p in points:
        p = V(p)
        if not out or (p - out[-1]).length > 1e-7:
            out.append(p)
    if len(out) > 1 and (out[0] - out[-1]).length <= 1e-7:
        out.pop()
    if len(out) >= 3:
        mb.polygon(out, colour)


def _pick(colour, band, side, centre):
    if callable(colour):
        return colour(band, side, centre)
    if isinstance(colour, (list, tuple)):
        return colour[band]
    return colour


def loft(mb, rings, colour, start_colour=None, end_colour=None, tip_start=None, tip_end=None):
    """A closed shell skinned over matching rings of points.

    colour is a palette name, a list with one name per band, or f(band, side, centre) -> name.
    Ends are capped flat, or closed to a point when tip_start / tip_end is given."""
    count = len(rings[0])
    last = len(rings) - 2
    for band in range(len(rings) - 1):
        a, b = rings[band], rings[band + 1]
        for i in range(count):
            j = (i + 1) % count
            quad = [a[i], a[j], b[j], b[i]]
            centre = sum((V(p) for p in quad), V()) / 4.0
            poly(mb, quad, _pick(colour, band, i, centre))
    first_colour = start_colour or _pick(colour, 0, -1, V(rings[0][0]))
    last_colour = end_colour or _pick(colour, last, -1, V(rings[-1][0]))
    if tip_start is None:
        poly(mb, list(reversed(rings[0])), first_colour)
    else:
        for i in range(count):
            poly(mb, [rings[0][(i + 1) % count], rings[0][i], tip_start], first_colour)
    if tip_end is None:
        poly(mb, list(rings[-1]), last_colour)
    else:
        for i in range(count):
            poly(mb, [rings[-1][i], rings[-1][(i + 1) % count], tip_end], last_colour)


def oct_ring(y, rx, zf, zb, fx=0.6, sz=0.45, cx=0.0, cz=0.0):
    """Horizontal 8-point ring with a broad flat front (+Z) and back.

    Side order: 0 front, 1 front-left bevel, 2 left (-X), 3 back-left, 4 back, 5 back-right,
    6 right (+X), 7 front-right bevel."""
    return [
        V((cx + fx * rx, y, cz + zf)), V((cx - fx * rx, y, cz + zf)),
        V((cx - rx, y, cz + sz * zf)), V((cx - rx, y, cz - sz * zb)),
        V((cx - fx * rx, y, cz - zb)), V((cx + fx * rx, y, cz - zb)),
        V((cx + rx, y, cz - sz * zb)), V((cx + rx, y, cz + sz * zf)),
    ]


def ring_around(centre, axis_u, axis_v, half_u, half_v, sides=8, phase=0.5, flats=True):
    """A ring in the plane of axis_u / axis_v. With flats=True the half sizes are measured to the
    flat sides (so bounds come out as given); otherwise to the corners."""
    scale = 1.0 / math.cos(math.pi / sides) if flats else 1.0
    centre = V(centre)
    points = []
    for i in range(sides):
        angle = 2.0 * math.pi * (i + phase) / sides
        points.append(centre + axis_u * (math.cos(angle) * half_u * scale)
                      + axis_v * (math.sin(angle) * half_v * scale))
    return points


def slab_ring(z, cx, cy, hw, hh, chamfer):
    """A chamfered rectangle in the XY plane at depth z (palm cross-section)."""
    c = chamfer
    return [V((cx + hw - c, cy + hh, z)), V((cx - hw + c, cy + hh, z)), V((cx - hw, cy + hh - c, z)),
            V((cx - hw, cy - hh + c, z)), V((cx - hw + c, cy - hh, z)), V((cx + hw - c, cy - hh, z)),
            V((cx + hw, cy - hh + c, z)), V((cx + hw, cy + hh - c, z))]


def interp(table, y, column):
    """Piecewise-linear lookup of `column` against the first column of a table of rows."""
    rows = sorted(table, key=lambda r: r[0])
    if y <= rows[0][0]:
        return rows[0][column]
    for a, b in zip(rows, rows[1:]):
        if y <= b[0]:
            t = (y - a[0]) / (b[0] - a[0])
            return a[column] + (b[column] - a[column]) * t
    return rows[-1][column]


def plate(mb, rows, zfn, colour, lift=0.006, depth=0.02, levels=()):
    """A thin solid lying on a front (+Z) surface.

    rows are (y, x_left, x_right) from top to bottom; zfn(y) is the surface z at that height.
    Extra rows are inserted at `levels` (the surface's ring heights) so every strip stays flat on
    one facet of the surface instead of cutting through a crease."""
    full = []
    for a, b in zip(rows, rows[1:]):
        full.append(a)
        inside = sorted((lv for lv in levels if min(a[0], b[0]) + 1e-6 < lv < max(a[0], b[0]) - 1e-6),
                        reverse=a[0] > b[0])
        for lv in inside:
            t = (lv - a[0]) / (b[0] - a[0])
            full.append((lv, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t))
    full.append(rows[-1])
    rings = []
    for y, xl, xr in full:
        z = zfn(y)
        rings.append([V((xl, y, z + lift)), V((xr, y, z + lift)), V((xr, y, z - depth)), V((xl, y, z - depth))])
    loft(mb, rings, colour)


def spike(mb, base, tip, half_w, half_t, colour):
    """A four-sided fur spike: diamond base, pointed tip."""
    base, tip = V(base), V(tip)
    d = (tip - base).normalized()
    t = (Z_AXIS - d * Z_AXIS.dot(d)).normalized()
    w = d.cross(t)
    ring = [base + w * half_w, base + t * half_t, base - w * half_w, base - t * half_t]
    loft(mb, [ring], colour, tip_end=tip)


def add(dst, src, m=None, t=(0.0, 0.0, 0.0)):
    """Appends src into dst after the transform v -> m @ v + t (Unity space)."""
    m = m or Matrix.Identity(3)
    t = V(t)
    base = len(dst.vertices)
    dst.vertices.extend(m @ v + t for v in src.vertices)
    dst.faces.extend([[i + base for i in face] for face in src.faces])
    dst.face_colours.extend(src.face_colours)
    return dst


def moved(src, m=None, t=(0.0, 0.0, 0.0)):
    return add(lp.MeshBuilder(), src, m, t)


def basis(x_axis, y_axis, z_axis, scale=(1.0, 1.0, 1.0)):
    """Matrix sending local X/Y/Z to the given world axes, with a local scale applied first."""
    m = Matrix.Identity(3)
    for column, (axis, s) in enumerate(zip((x_axis, y_axis, z_axis), scale)):
        for row in range(3):
            m[row][column] = axis[row] * s
    return m


def catmull(points, samples):
    """Evenly samples a Catmull-Rom curve through the points. Returns (point, parameter 0..n-1)."""
    pts = [points[0] * 2 - points[1]] + list(points) + [points[-1] * 2 - points[-2]]
    segments = len(points) - 1
    out = []
    for s in range(samples):
        t = s / (samples - 1) * segments
        k = min(int(t), segments - 1)
        u = t - k
        p0, p1, p2, p3 = pts[k:k + 4]
        out.append((0.5 * ((2 * p1) + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u
                           + (-p0 + 3 * p1 - 3 * p2 + p3) * u ** 3), t))
    return out


def finalize(obj):
    """Welds each solid, makes every shell's normals point outward, and triangulates.

    MeshBuilder gives each polygon its own vertices, so Blender's normal recalculation has no
    connected shell to reason about until the mesh is welded. UVs are per face corner, so colours
    survive the weld. Returns (triangles, open_edges)."""
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
    bmesh.ops.dissolve_degenerate(bm, edges=bm.edges[:], dist=1e-6)
    bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="BEAUTY")
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.faces.ensure_lookup_table()
    bm.faces.index_update()
    seen = set()
    for face in bm.faces:
        if face.index in seen:
            continue
        seen.add(face.index)
        stack, island = [face], []
        while stack:
            g = stack.pop()
            island.append(g)
            for edge in g.edges:
                for h in edge.link_faces:
                    if h.index not in seen:
                        seen.add(h.index)
                        stack.append(h)
        volume = 0.0
        for g in island:
            co = [loop.vert.co for loop in g.loops]
            for i in range(1, len(co) - 1):
                volume += co[0].dot(co[i].cross(co[i + 1]))
        if volume < 0.0:
            for g in island:
                g.normal_flip()
    bm.normal_update()
    for face in bm.faces:
        face.smooth = False
    open_edges = sum(1 for edge in bm.edges if len(edge.link_faces) != 2)
    triangles = len(bm.faces)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    return triangles, open_edges


# ============================================================================ head

# y, half width, front z, back z, front-strip fraction. Eye height is y = 0.
HEAD = [
    (-0.145, 0.070, 0.092, 0.060, 0.58),
    (-0.110, 0.122, 0.130, 0.100, 0.58),
    (-0.040, 0.166, 0.150, 0.138, 0.58),
    (-0.004, 0.170, 0.150, 0.144, 0.58),
    (0.040, 0.172, 0.150, 0.150, 0.58),
    (0.100, 0.150, 0.126, 0.142, 0.70),
    (0.142, 0.106, 0.086, 0.106, 0.70),
    (0.160, 0.060, 0.042, 0.060, 0.70),
]
HEAD_LEVELS = [row[0] for row in HEAD]


def head_ring(y, rx, zf, zb, a1, margin=0.0):
    """Ten-point skull ring: a flat face, two bevels a side (tear stripe, then cheek), sides, back.

    Side order: 0 face, 1 inner bevel L, 2 outer bevel L, 3 left (-X), 4 back-left, 5 back,
    6 back-right, 7 right (+X), 8 outer bevel R, 9 inner bevel R."""
    rx, zf, zb = rx + margin, zf + margin, zb + margin
    a2, b2, b3, b4, a5 = 0.86, 0.74, 0.28, 0.45, 0.6
    return [
        V((a1 * rx, y, zf)), V((-a1 * rx, y, zf)), V((-a2 * rx, y, b2 * zf)), V((-rx, y, b3 * zf)),
        V((-rx, y, -b4 * zb)), V((-a5 * rx, y, -zb)), V((a5 * rx, y, -zb)), V((rx, y, -b4 * zb)),
        V((rx, y, b3 * zf)), V((a2 * rx, y, b2 * zf)),
    ]


def head_front(y):
    return interp(HEAD, y, 2)


def round_rows(cx, cy, radius):
    """Rows for an octagonal disc, for plate()."""
    k = 0.42
    return [(cy + radius, cx - radius * k, cx + radius * k), (cy + radius * k, cx - radius, cx + radius),
            (cy - radius * k, cx - radius, cx + radius), (cy - radius, cx - radius * k, cx + radius * k)]


def build_ear():
    """Right (+X) ear, authored upright around its base centre; front is +Z."""
    ear = lp.MeshBuilder()
    outline = [(-0.050, 0.0), (0.050, 0.0), (0.060, 0.075), (0.050, 0.140), (0.014, 0.172),
               (-0.030, 0.166), (-0.058, 0.110)]

    def ring(scale, w, about=(0.0, 0.085)):
        return [V((about[0] + (u - about[0]) * scale, about[1] + (v - about[1]) * scale, w)) for u, v in outline]

    rings = [ring(0.74, -0.030), ring(1.0, -0.008), ring(0.95, 0.018),
             ring(0.64, 0.018, (0.0, 0.080)), ring(0.50, 0.004, (0.0, 0.080))]
    loft(ear, rings, [EAR_BACK, EAR_RIM, EAR_RIM, EAR_INNER], start_colour=EAR_BACK, end_colour=EAR_FLOOR)
    return ear


def build_head():
    mb = lp.MeshBuilder()

    def skull_colour(band, side, centre):
        if band == 0:
            return "maroon" if side in (0, 1, 2, 8, 9) else "orange"      # jaw under the muzzle
        if band == 1:
            if side in (0, 1, 9):
                return "maroon"       # tear stripes either side of the muzzle
            if side in (2, 8):
                return "cream"        # cheeks
        if band == 2 and side in (2, 8):
            return "cream"            # cheeks reach up beside the eyes
        return "orange"

    loft(mb, [head_ring(*row) for row in HEAD], skull_colour)

    # Muzzle: a broad cream snout that also forms the chin.
    def face_ring(z, cy, hw, hh):
        return ring_around((0.0, cy, z), X_AXIS, Y_AXIS, hw, hh, sides=8)

    loft(mb, [face_ring(0.100, -0.086, 0.084, 0.056), face_ring(0.186, -0.086, 0.074, 0.052),
              face_ring(0.216, -0.082, 0.050, 0.036)], "cream")

    # Nose: dark, wide at the top, on the top front edge of the muzzle.
    def nose_ring(z, s, drop=0.0):
        outline = [(-0.021, 0.013), (0.021, 0.013), (0.028, 0.003), (0.010, -0.017), (-0.010, -0.017), (-0.028, 0.003)]
        return [V((u * s, -0.056 - drop + v * s, z)) for u, v in outline]

    loft(mb, [nose_ring(0.180, 1.0), nose_ring(0.222, 1.0), nose_ring(0.229, 0.66, 0.002)], "eye")

    # Mouth: a short line down from the nose and a wide smile.
    mb.box((0.0, -0.084, 0.216), (0.005, 0.022, 0.007), "eye")
    for side in (1.0, -1.0):
        mb.box((side * 0.019, -0.092, 0.216), (0.040, 0.005, 0.007), "eye", roll=side * 16.0)

    for side in (1.0, -1.0):
        cx = side * 0.068
        # Eye: dark rim, brown iris, black pupil, white glint.
        plate(mb, round_rows(cx, 0.0, 0.028), head_front, "eye", lift=0.005, levels=HEAD_LEVELS)
        plate(mb, round_rows(cx, 0.0, 0.0235), head_front, "maroon", lift=0.007, depth=0.004)
        plate(mb, round_rows(cx, 0.0, 0.0150), head_front, "black", lift=0.009, depth=0.004)
        plate(mb, round_rows(cx - 0.010, 0.011, 0.0075), head_front, "white", lift=0.011, depth=0.004)

        # Brow patch: a thick cream bar, low at the nose bridge and rising outward.
        def brow(points):
            return [(y, min(side * a, side * b), max(side * a, side * b)) for y, a, b in points]

        plate(mb, brow([(0.092, 0.046, 0.086), (0.080, 0.024, 0.102), (0.060, 0.016, 0.104),
                        (0.049, 0.016, 0.072), (0.041, 0.019, 0.036)]),
              head_front, "cream", lift=0.007, levels=HEAD_LEVELS)

    # Fur tufts and the ears. Built for +X and mirrored.
    half = lp.MeshBuilder()
    spike(half, (0.150, 0.004, 0.030), (0.226, -0.018, -0.004), 0.040, 0.050, "orange")
    spike(half, (0.122, -0.034, 0.068), (0.216, -0.046, 0.030), 0.046, 0.056, "cream")
    spike(half, (0.116, -0.070, 0.072), (0.204, -0.100, 0.040), 0.046, 0.056, "cream")
    spike(half, (0.102, -0.094, 0.078), (0.160, -0.142, 0.056), 0.044, 0.050, "cream")
    add(half, build_ear(), lp.rotation(yaw=10.0, roll=-18.0), (0.146, 0.035, -0.032))
    add(mb, half)
    add(mb, half, MIRROR_X)
    return mb


# ============================================================================ torso

# y, half width, front z, back z, centre z. The origin is the base of the neck.
TORSO = [
    (0.090, 0.100, 0.085, 0.090, 0.000),    # neck stub, hidden inside the head
    (0.020, 0.150, 0.112, 0.120, 0.000),
    (-0.030, 0.205, 0.132, 0.134, -0.004),
    (-0.090, 0.220, 0.152, 0.150, -0.004),
    (-0.190, 0.212, 0.162, 0.148, 0.004),   # orange ends here
    (-0.270, 0.184, 0.152, 0.140, 0.006),
    (-0.345, 0.156, 0.135, 0.128, 0.006),   # maroon ends here
    (-0.405, 0.122, 0.105, 0.102, 0.004),
    (-0.440, 0.064, 0.055, 0.055, 0.002),
]
TORSO_LEVELS = [row[0] for row in TORSO]


def torso_colour(band, side, centre):
    if band == 0:
        return "maroon"                                   # shadowed neck under the chin
    if band >= 6:
        return "darkbrown"
    if band >= 4:
        return "maroon"
    if band == 3 and side == 0:
        return "maroon"                                   # chest plate under the bib
    return "orange"


def torso_front(y):
    return interp(TORSO, y, 2) + interp(TORSO, y, 4)


def build_tail(mb):
    """Thick striped tail leaving the lower back and curling up behind the shoulders."""
    control = [V((0.0, -0.300, -0.050)), V((0.0, -0.360, -0.200)), V((0.0, -0.340, -0.350)),
               V((0.0, -0.230, -0.460)), V((0.0, -0.090, -0.490)), V((0.0, 0.020, -0.450)),
               V((0.0, 0.080, -0.370))]
    radii = [0.060, 0.086, 0.106, 0.116, 0.112, 0.096, 0.066]
    samples = catmull(control, 9)
    rings = []
    for index, (point, t) in enumerate(samples):
        before = samples[max(index - 1, 0)][0]
        after = samples[min(index + 1, len(samples) - 1)][0]
        tangent = (after - before).normalized()
        up = tangent.cross(X_AXIS).normalized()
        k = min(int(t), len(radii) - 2)
        radius = radii[k] + (radii[k + 1] - radii[k]) * (t - k)
        rings.append(ring_around(point, X_AXIS, up, radius, radius * 0.94, sides=8))
    end_tangent = (samples[-1][0] - samples[-2][0]).normalized()
    tip = samples[-1][0] + end_tangent * 0.045
    bands = ["orange" if band % 2 == 0 else "maroon" for band in range(len(rings) - 1)]
    loft(mb, rings, bands, tip_end=tip, end_colour="maroon")


def build_torso():
    mb = lp.MeshBuilder()
    loft(mb, [oct_ring(y, rx, zf, zb, cz=cz) for y, rx, zf, zb, cz in TORSO], torso_colour)

    # Cream bib: wide under the chin, closing to a V on the chest.
    bib = [(0.020, -0.076, 0.076), (-0.030, -0.112, 0.112), (-0.090, -0.094, 0.094), (-0.172, 0.0, 0.0)]
    plate(mb, bib, torso_front, "cream", lift=0.007, depth=0.03, levels=TORSO_LEVELS)
    # The neck shadow dips into the top of the bib as a small V.
    plate(mb, [(0.020, -0.060, 0.060), (-0.040, 0.0, 0.0)], torso_front, "maroon", lift=0.011, depth=0.03,
          levels=TORSO_LEVELS)

    # Rounded orange shoulder caps over the arm pivots at (+-0.21, -0.07, 0), so the upper arms
    # always emerge from a deltoid however they are rotated.
    if SHOULDER_CAPS:
        for side in (1.0, -1.0):
            mb.blob((side * 0.202, -0.088, 0.0), (0.146, 0.150, 0.190), "orange", rings=2, sides=6)

    build_tail(mb)
    return mb


# ============================================================================ arms

def arm_ring(z, half):
    return ring_around((0.0, 0.0, z), X_AXIS, Y_AXIS, half, half, sides=8)


def build_upper_arm():
    mb = lp.MeshBuilder()
    rings = [arm_ring(-0.50, 0.32), arm_ring(-0.43, 0.46), arm_ring(-0.15, 0.52),
             arm_ring(0.22, 0.50), arm_ring(0.50, 0.44)]
    loft(mb, rings, ["orange", "orange", "maroon", "maroon"])
    return mb


def build_forearm():
    mb = lp.MeshBuilder()
    rings = [arm_ring(-0.50, 0.40), arm_ring(-0.42, 0.45), arm_ring(0.00, 0.54),
             arm_ring(0.36, 0.625), arm_ring(0.50, 0.54)]
    loft(mb, rings, "maroon")
    return mb


# ============================================================================ hands

def build_finger(length, half_w, colour):
    """One finger along +Z from its knuckle, with a cream pad under the tip."""
    f = lp.MeshBuilder()

    def ring(z, hw, cy):
        return ring_around((0.0, cy, z), X_AXIS, Y_AXIS, hw, hw * 0.9, sides=6, phase=0.0, flats=False)

    rings = [ring(-0.012, half_w * 1.0, 0.0), ring(length * 0.5, half_w, -0.001),
             ring(length * 0.88, half_w * 0.86, -0.003), ring(length, half_w * 0.5, -0.004)]
    loft(f, rings, colour)
    under = -0.003 - half_w * 0.86 * 0.9 * math.sin(math.radians(60.0))
    f.tapered((0.0, under - 0.0005, length * 0.72), (half_w * 0.95, half_w * 1.25), (half_w * 1.5, half_w * 1.9),
              0.007, "cream", sides=6)
    return f


def build_hand_left():
    """Left hand: fingers +Z, palm down (-Y), thumb on +X."""
    mb = lp.MeshBuilder()
    rings = [slab_ring(-0.040, 0.0, 0.0, 0.037, 0.018, 0.008), slab_ring(-0.012, 0.0, 0.0, 0.049, 0.024, 0.010),
             slab_ring(0.030, 0.0, 0.0, 0.052, 0.023, 0.010), slab_ring(0.046, 0.0, 0.0, 0.050, 0.018, 0.008)]
    loft(mb, rings, HAND_COLOUR)

    # Big palm pad.
    pad = [(-0.030, 0.030), (0.030, 0.030), (0.037, 0.008), (0.024, -0.024), (-0.024, -0.024), (-0.037, 0.008)]

    def pad_ring(y, s):
        return [V((-0.002 + x * s, y, 0.002 + z * s)) for x, z in pad]

    loft(mb, [pad_ring(-0.020, 1.0), pad_ring(-0.0265, 1.0), pad_ring(-0.030, 0.78)], "cream")

    # x at the knuckle, length, splay (yaw), from the index (thumb side) to the little finger.
    fingers = [(0.0375, 0.060, 7.0), (0.0125, 0.068, 2.0), (-0.0125, 0.062, -3.0), (-0.0375, 0.050, -9.0)]
    for x, length, yaw in fingers:
        add(mb, build_finger(length, 0.0135, FINGER_COLOUR), lp.rotation(yaw=yaw, pitch=7.0), (x, 0.002, 0.040))

    add(mb, build_finger(0.052, 0.0155, FINGER_COLOUR), lp.rotation(yaw=42.0, pitch=4.0), (0.034, -0.001, -0.018))
    return mb


# ============================================================================ hats

def build_police_cap():
    mb = lp.MeshBuilder()
    cap = [(0.132, 0.098, 0.098, 0.106), (0.172, 0.098, 0.098, 0.106), (0.212, 0.116, 0.118, 0.116),
           (0.226, 0.092, 0.092, 0.092)]
    tilt = 0.10   # the crown rises toward the front like a peaked cap

    def ring(row, tilted):
        points = oct_ring(row[0], row[1], row[2], row[3], fx=0.55, cz=-0.004)
        if tilted:
            points = [V((p.x, p.y + p.z * tilt, p.z)) for p in points]
        return points

    loft(mb, [ring(cap[0], False), ring(cap[1], False), ring(cap[2], True), ring(cap[3], True)],
         ["police_blue", "navy", "navy"], start_colour="navy", end_colour="navy")

    # Short brim.
    bottom = [V((-0.085, 0.130, 0.080)), V((0.085, 0.130, 0.080)), V((0.062, 0.116, 0.158)), V((-0.062, 0.116, 0.158))]
    top = [p + V((0.0, 0.010, 0.0)) for p in bottom]
    mb.hull(bottom, top, "black")

    # Badge on the front of the crown.
    def crown_front(y):
        front = interp([(cap[1][0], cap[1][2]), (cap[2][0] + cap[2][2] * tilt, cap[2][2])], y, 1)
        return front - 0.004

    plate(mb, [(0.214, -0.014, 0.014), (0.200, -0.019, 0.019), (0.178, 0.0, 0.0)], crown_front, "yellow",
          lift=0.007, depth=0.01)
    return mb


def build_visor():
    mb = lp.MeshBuilder()
    margin = 0.009

    def ring(y):
        return head_ring(y, interp(HEAD, y, 1), interp(HEAD, y, 2), interp(HEAD, y, 3), interp(HEAD, y, 4), margin)

    loft(mb, [ring(0.095), ring(0.130)], "green")

    front = head_front(0.100) + margin
    bottom = [V((-0.100, 0.100, front - 0.02)), V((0.100, 0.100, front - 0.02)),
              V((0.078, 0.080, front + 0.095)), V((-0.078, 0.080, front + 0.095))]
    top = [p + V((0.0, 0.009, 0.0)) for p in bottom]
    mb.hull(bottom, top, "green", cap_colour="leaf_dark", top_colour="green")
    return mb


# ============================================================================ previews

def set_exposure():
    bpy.context.scene.view_settings.exposure = -0.55   # keeps cream from clipping to white


def clear_scene_objects():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)


def assemble(parts, hat=None, arm_thickness=0.11):
    """Poses the parts as a standing character. Torso origin is at (0, 0, 0)."""
    full = lp.MeshBuilder()
    add(full, parts["Torso"])
    add(full, parts["Head"], t=(0.0, 0.19, 0.0))
    if hat:
        add(full, parts[hat], t=(0.0, 0.19, 0.0))

    scale = (arm_thickness, arm_thickness, 0.42)
    shoulder = V((-0.21, -0.07, 0.0))
    upper_dir = V((-0.17, -1.0, 0.0)).normalized()
    fore_dir = V((0.05, -1.0, 0.10)).normalized()
    elbow = shoulder + upper_dir * 0.42
    wrist = elbow + fore_dir * 0.42

    def frame(direction, side_hint):
        y_axis = (side_hint - direction * side_hint.dot(direction)).normalized()
        return y_axis.cross(direction), y_axis, direction

    left = lp.MeshBuilder()
    ux, uy, uz = frame(upper_dir, V((-1.0, 0.0, 0.0)))
    add(left, parts["UpperArm"], basis(ux, uy, uz, scale), shoulder + upper_dir * 0.21)
    fx, fy, fz = frame(fore_dir, V((-1.0, 0.0, 0.0)))
    add(left, parts["Forearm"], basis(fx, fy, fz, scale), elbow + fore_dir * 0.21)
    hx, hy, hz = frame(fore_dir, V((0.0, 0.0, 1.0)))
    hand_matrix = basis(hx, hy, hz)          # knuckle-walker: back of the hand forward, thumb inward
    hand_at = wrist + fore_dir * 0.035
    add(full, left)
    add(full, left, MIRROR_X)
    add(full, parts["HandL"], hand_matrix, hand_at)
    add(full, parts["HandR"], MIRROR_X @ hand_matrix @ MIRROR_X, MIRROR_X @ hand_at)
    return full


def render_turnaround(parts, path):
    clear_scene_objects()
    body = assemble(parts)
    views = [("PreviewFront", 0.0, 1.50), ("PreviewSide", 90.0, 0.42), ("PreviewBack", 180.0, -0.62)]
    for name, yaw, x in views:
        finalize(moved(body, lp.rotation(yaw=yaw), (x, 0.0, 0.0)).build(name))
    hats = [("PreviewCap", "PoliceCap", 0.20), ("PreviewVisor", "Visor", -0.42)]
    for name, hat, y in hats:
        head = lp.MeshBuilder()
        add(head, parts["Head"])
        add(head, parts[hat])
        finalize(moved(head, lp.rotation(yaw=32.0), (-1.55, y, 0.0)).build(name))
    set_exposure()
    lp.render_preview(path, (0.0, -0.31, 8.0), (0.0, -0.31, 0.0), width=2400, height=1100, ortho_scale=4.0)
    clear_scene_objects()


def render_hands(parts, path):
    clear_scene_objects()
    back = basis(-X_AXIS, Z_AXIS, Y_AXIS)      # fingers up, back of the hand toward the camera
    palm = basis(X_AXIS, -Z_AXIS, Y_AXIS)      # fingers up, palm toward the camera
    layout = [("HandsBackL", "HandL", back, 0.27), ("HandsBackR", "HandR", back, 0.09),
              ("HandsPalmL", "HandL", palm, -0.09), ("HandsPalmR", "HandR", palm, -0.27)]
    for name, part, matrix, x in layout:
        finalize(moved(parts[part], matrix, (x, 0.0, 0.0)).build(name))
    set_exposure()
    lp.render_preview(path, (0.0, 0.035, 3.0), (0.0, 0.035, 0.0), width=1800, height=620, ortho_scale=0.78)
    clear_scene_objects()


def render_debug(parts, folder):
    """Extra close-ups for tuning the look. Only runs when PANDA_DEBUG_DIR is set."""
    clear_scene_objects()
    shots = [("a", None, 0.0, 0.55), ("b", None, 40.0, 0.0), ("c", None, 90.0, -0.55),
             ("d", "PoliceCap", 0.0, -1.10), ("e", "PoliceCap", 60.0, -1.65), ("f", "Visor", 30.0, -2.20)]
    for name, hat, yaw, x in shots:
        head = lp.MeshBuilder()
        add(head, parts["Head"])
        if hat:
            add(head, parts[hat])
        finalize(moved(head, lp.rotation(yaw=yaw), (x, 0.0, 0.0)).build("Debug" + name))
    set_exposure()
    lp.render_preview(os.path.join(folder, "debug-heads.png"), (-0.825, 0.03, 6.0), (-0.825, 0.03, 0.0),
                      width=2700, height=500, ortho_scale=3.4)
    clear_scene_objects()
    body = assemble(parts)
    finalize(moved(body, lp.rotation(yaw=35.0), (0.45, 0.0, 0.0)).build("DebugBodyA"))
    finalize(moved(assemble(parts, arm_thickness=0.17), lp.rotation(yaw=20.0), (-0.65, 0.0, 0.0)).build("DebugBodyB"))
    lp.render_preview(os.path.join(folder, "debug-body.png"), (0.0, 1.0, 5.0), (0.0, -0.3, 0.0),
                      width=1600, height=1200, lens=70.0)
    clear_scene_objects()


# ============================================================================ main

def main():
    lp.reset_scene()
    lp.write_palette_png()

    hand_left = build_hand_left()
    parts = {
        "Head": build_head(),
        "Torso": build_torso(),
        "UpperArm": build_upper_arm(),
        "Forearm": build_forearm(),
        "HandL": hand_left,
        "HandR": moved(hand_left, MIRROR_X),
        "PoliceCap": build_police_cap(),
        "Visor": build_visor(),
    }

    report = []
    objects = []
    for name, builder in parts.items():
        obj = builder.build(name)
        triangles, open_edges = finalize(obj)
        objects.append(obj)
        report.append((name, triangles, open_edges, builder.bounds()))

    lp.export_fbx(FBX_PATH, objects)

    # Previews are built only after the export, from fresh copies, so they never reach the FBX.
    os.makedirs(DOCS_DIR, exist_ok=True)
    render_turnaround(parts, os.path.join(DOCS_DIR, "panda-model.png"))
    render_hands(parts, os.path.join(DOCS_DIR, "panda-hands.png"))
    if os.environ.get("PANDA_DEBUG_DIR"):
        render_debug(parts, os.environ["PANDA_DEBUG_DIR"])

    print("\n==== Panda parts (Unity space, metres) ====")
    total = 0
    for name, triangles, open_edges, (low, high) in report:
        total += triangles
        size = tuple(h - l for l, h in zip(low, high))
        note = "" if open_edges == 0 else "  WARNING %d non-manifold edges" % open_edges
        print("%-10s tris %4d  min (%+.3f, %+.3f, %+.3f)  max (%+.3f, %+.3f, %+.3f)  size (%.3f, %.3f, %.3f)%s"
              % ((name, triangles) + tuple(low) + tuple(high) + size + (note,)))
    print("Total triangles in FBX: %d (character without hats: %d)" % (
        total, total - sum(r[1] for r in report if r[0] in ("PoliceCap", "Visor"))))
    print("Wrote", FBX_PATH)


main()
