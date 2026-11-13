"""Generates the coastal town for Small Town, Big Trouble.

Outputs
  Assets/SmallTown/Art/Models/Town.fbx          visual meshes, one per chunk
  Assets/SmallTown/Art/Models/Town.layout.json  colliders, climbable surfaces, zones, markers and sign text
  Docs/town-*.png                               preview renders

Geometry is authored in Unity coordinates (x east, y up, z north). The visual
mesh goes through FBX; everything gameplay depends on (colliders, markers) goes
through the JSON file, so no gameplay position depends on FBX axis conventions.

Scale note: the player's eyes are about 0.8 m above the ground and the body is
0.95 m tall, so doors are 1.45 m high and single-storey roofs are 2.1 m up.

Run from the project root:
    /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P Tools/blender/build_town.py
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lowpoly as lp  # noqa: E402

FLOOR = 0.08          # height of sidewalks and interior floors above the road
DOOR_H = 1.45
DOOR_W = 1.0
WALL_T = 0.2
STOREY = 2.1          # roof top of a single-storey building
PARAPET = 0.25


class Town:
    def __init__(self):
        self.chunks = {}
        self.chunk = None
        self.colliders = []
        self.climbables = []
        self.zones = []
        self.markers = []
        self.signs = []

    # ------------------------------------------------------------ recording

    def begin(self, name, record=True):
        """Starts a mesh chunk. record=False is for movable props authored at the origin:
        their boxes are visual only and Unity adds its own collider."""
        self.chunk = self.chunks.setdefault(name, lp.MeshBuilder())
        self.chunk_name = name
        self.record = record

    def collider(self, centre, size, yaw=0.0, pitch=0.0, roll=0.0):
        if not self.record:
            return
        self.colliders.append({"n": self.chunk_name, "c": [round(v, 4) for v in centre],
                               "s": [round(v, 4) for v in size], "r": [pitch, yaw, roll]})

    def solid(self, centre, size, colour, yaw=0.0, pitch=0.0, roll=0.0, top_colour=None):
        """Visible box that also blocks the player."""
        self.chunk.box(centre, size, colour, yaw, pitch, roll, top_colour)
        self.collider(centre, size, yaw, pitch, roll)

    def deco(self, centre, size, colour, yaw=0.0, pitch=0.0, roll=0.0, top_colour=None):
        """Visible box with no collision."""
        self.chunk.box(centre, size, colour, yaw, pitch, roll, top_colour)

    def climbable(self, centre, size, yaw=0.0):
        """A yellow striped grip surface. Unity builds the visual so it can use the striped material."""
        self.climbables.append({"c": [round(v, 4) for v in centre], "s": [round(v, 4) for v in size], "r": [0.0, yaw, 0.0]})

    def zone(self, name, centre, size):
        self.zones.append({"n": name, "c": [round(v, 4) for v in centre], "s": [round(v, 4) for v in size]})

    def marker(self, name, position, yaw=0.0):
        self.markers.append({"n": name, "p": [round(v, 4) for v in position], "yaw": yaw})

    def sign(self, text, position, yaw, width, height, board="navy", text_colour="FFFFFF", font=1.0, thickness=0.06):
        """A sign board. The lettering is added in Unity from the JSON entry. yaw 0 reads from the south."""
        self.chunk.box(position, (width, height, thickness), board, yaw)
        self.signs.append({"t": text, "p": [round(v, 4) for v in position], "yaw": yaw, "w": width, "h": height,
                           "col": text_colour, "font": font, "d": thickness})

    # ------------------------------------------------------------ construction helpers

    def wall(self, axis, start, end, fixed, y0, y1, colour, openings=(), thickness=WALL_T):
        """Axis-aligned wall with door and window openings.

        axis 'x': runs from x=start to x=end at z=fixed. axis 'z': runs along z at x=fixed.
        openings: (centre_along_axis, width, sill_y, head_y) in absolute coordinates.
        """
        def piece(a, b, low, high):
            if b - a < 1e-3 or high - low < 1e-3:
                return
            mid, length, height = (a + b) / 2.0, b - a, high - low
            if axis == "x":
                self.solid((mid, (low + high) / 2.0, fixed), (length, height, thickness), colour)
            else:
                self.solid((fixed, (low + high) / 2.0, mid), (thickness, height, length), colour)

        cursor = start
        for centre, width, sill, head in sorted(openings):
            left, right = centre - width / 2.0, centre + width / 2.0
            piece(cursor, left, y0, y1)
            piece(left, right, y0, sill)
            piece(left, right, head, y1)
            cursor = right
        piece(cursor, end, y0, y1)

    def frame(self, axis, centre, fixed, width, sill, head, colour, depth=0.26):
        """Trim around an opening (visual only)."""
        t = 0.07
        for side in (-1, 1):
            along = centre + side * (width / 2.0 + t / 2.0)
            pos = (along, (sill + head) / 2.0, fixed) if axis == "x" else (fixed, (sill + head) / 2.0, along)
            size = (t, head - sill, depth) if axis == "x" else (depth, head - sill, t)
            self.deco(pos, size, colour)
        pos = (centre, head + t / 2.0, fixed) if axis == "x" else (fixed, head + t / 2.0, centre)
        size = (width + 2 * t, t, depth) if axis == "x" else (depth, t, width + 2 * t)
        self.deco(pos, size, colour)
        if sill > FLOOR + 0.05:
            pos = (centre, sill - t / 2.0, fixed) if axis == "x" else (fixed, sill - t / 2.0, centre)
            self.deco(pos, size, colour)

    def shell(self, x0, z0, x1, z1, height, colour, trim, floor_colour, roof_colour,
              south=(), north=(), west=(), east=(), parapet=PARAPET, base_colour=None):
        """Hollow one-room building: floor, four walls with openings, flat roof with a parapet."""
        cx, cz = (x0 + x1) / 2.0, (z0 + z1) / 2.0
        w, d = x1 - x0, z1 - z0
        self.solid((cx, FLOOR / 2.0, cz), (w, FLOOR, d), floor_colour)
        ceiling = height - 0.15
        t = WALL_T
        self.wall("x", x0, x1, z0 + t / 2.0, FLOOR, ceiling, colour, south)
        self.wall("x", x0, x1, z1 - t / 2.0, FLOOR, ceiling, colour, north)
        self.wall("z", z0 + t, z1 - t, x0 + t / 2.0, FLOOR, ceiling, colour, west)
        self.wall("z", z0 + t, z1 - t, x1 - t / 2.0, FLOOR, ceiling, colour, east)
        for axis, fixed, group in (("x", z0 + t / 2.0, south), ("x", z1 - t / 2.0, north),
                                   ("z", x0 + t / 2.0, west), ("z", x1 - t / 2.0, east)):
            for centre, width, sill, head in group:
                self.frame(axis, centre, fixed, width, sill, head, trim)
        if base_colour:
            for z in (z0 - 0.01, z1 + 0.01):
                self.deco((cx, FLOOR + 0.12, z), (w + 0.04, 0.24, 0.03), base_colour)
        # Roof slab, cornice and parapet.
        self.solid((cx, height - 0.075, cz), (w, 0.15, d), roof_colour)
        self.deco((cx, height - 0.02, cz), (w + 0.12, 0.1, d + 0.12), trim)
        if parapet > 0:
            py = height + parapet / 2.0
            self.solid((cx, py, z0 + 0.075), (w, parapet, 0.15), colour, top_colour=trim)
            self.solid((cx, py, z1 - 0.075), (w, parapet, 0.15), colour, top_colour=trim)
            self.solid((x0 + 0.075, py, cz), (0.15, parapet, d - 0.3), colour, top_colour=trim)
            self.solid((x1 - 0.075, py, cz), (0.15, parapet, d - 0.3), colour, top_colour=trim)

    def awning(self, x0, x1, wall_z, y, depth, colours, facing):
        """Striped awning. facing -1: the shop front faces south (-Z); +1: it faces north."""
        tilt = 14.0 * facing
        centre_z = wall_z + facing * depth / 2.0
        stripes = max(2, int(round((x1 - x0) / 0.5)))
        width = (x1 - x0) / stripes
        for i in range(stripes):
            x = x0 + width * (i + 0.5)
            self.deco((x, y, centre_z), (width, 0.05, depth), colours[i % len(colours)], pitch=tilt)
        self.collider(((x0 + x1) / 2.0, y, centre_z), (x1 - x0, 0.05, depth), pitch=tilt)

    def lamp(self, x, z):
        self.solid((x, 1.1, z), (0.1, 2.2, 0.1), "metal_dark")
        self.deco((x, 2.25, z), (0.34, 0.1, 0.34), "metal_dark")
        self.deco((x, 2.12, z), (0.24, 0.16, 0.24), "yellow")

    def palm(self, x, z, height=3.0, lean=0.25):
        segments = 5
        for i in range(segments):
            t = i / segments
            self.chunk.tapered((x + lean * t * t * height * 0.4, FLOOR + height * (t + 0.5 / segments), z),
                               (0.26 - 0.1 * t, 0.26 - 0.1 * t), (0.24 - 0.1 * t, 0.24 - 0.1 * t),
                               height / segments + 0.02, "wood" if i % 2 else "wood_dark", sides=6)
        top_x = x + lean * height * 0.4
        for k in range(6):
            yaw = k * 60.0 + 15.0
            rad = math.radians(yaw)
            self.deco((top_x + math.sin(rad) * 0.55, FLOOR + height + 0.02, z + math.cos(rad) * 0.55),
                      (0.3, 0.05, 1.1), "leaf" if k % 2 else "leaf_dark", yaw=yaw, pitch=22.0)
        self.collider((x + lean * height * 0.2, FLOOR + height / 2.0, z), (0.3, height, 0.3))

    def bench(self, x, z, yaw=0.0):
        self.solid((x, FLOOR + 0.2, z), (1.1, 0.06, 0.34), "wood", yaw)
        rad = math.radians(yaw)
        back = (x - math.sin(rad) * -0.16, FLOOR + 0.38, z - math.cos(rad) * -0.16)
        self.deco(back, (1.1, 0.26, 0.05), "wood", yaw)
        for side in (-0.45, 0.45):
            self.deco((x + math.cos(rad) * side, FLOOR + 0.1, z - math.sin(rad) * side), (0.06, 0.2, 0.3), "metal_dark", yaw)

    def crate(self, x, y, z, size=0.5, yaw=0.0):
        self.solid((x, y + size / 2.0, z), (size, size, size), "wood", yaw)
        self.deco((x, y + size / 2.0, z), (size + 0.02, size * 0.18, size + 0.02), "wood_dark", yaw)

    def dumpster(self, x, z, yaw=0.0):
        self.solid((x, 0.35, z), (1.5, 0.7, 0.8), "green", yaw)
        self.deco((x, 0.72, z), (1.56, 0.06, 0.86), "metal_dark", yaw)

    def car(self, x, z, yaw, body, roof):
        """Static cartoon car, used for parked cars and as the look of the patrol car."""
        rad = math.radians(yaw)

        def at(lx, ly, lz):
            return (x + math.cos(rad) * lx + math.sin(rad) * lz, ly, z - math.sin(rad) * lx + math.cos(rad) * lz)

        self.solid(at(0, 0.33, 0), (0.95, 0.36, 1.9), body, yaw)
        self.solid(at(0, 0.66, -0.1), (0.85, 0.32, 1.0), roof, yaw)
        self.deco(at(0, 0.66, 0.42), (0.78, 0.24, 0.04), "glass", yaw)
        self.deco(at(0, 0.66, -0.62), (0.78, 0.24, 0.04), "glass", yaw)
        for lx in (-0.48, 0.48):
            for lz in (-0.6, 0.6):
                self.deco(at(lx, 0.17, lz), (0.12, 0.34, 0.34), "black", yaw)
        for lx in (-0.3, 0.3):
            self.deco(at(lx, 0.36, 0.96), (0.16, 0.1, 0.04), "yellow", yaw)
            self.deco(at(lx, 0.36, -0.96), (0.16, 0.1, 0.04), "red", yaw)


def build():
    t = Town()

    # ================================================================== GROUND
    t.begin("Ground")
    t.solid((0, -0.5, 6), (64, 1.0, 54), "grass")                       # land: z -21 .. 33
    t.deco((0, 0.005, 0), (60, 0.01, 5), "asphalt")                     # main street
    t.deco((-22.5, 0.005, 8), (4, 0.01, 25), "asphalt")                 # west side street
    t.deco((22.5, 0.005, 8), (4, 0.01, 25), "asphalt")                  # east side street
    t.deco((0, 0.005, 19), (49, 0.01, 3), "asphalt")                    # residential lane
    t.deco((0, 0.004, 15), (41, 0.008, 3), "concrete")                  # back alley
    for x in range(-28, 29, 3):                                         # centre dashes
        if abs(abs(x) - 22.5) > 2.2:
            t.deco((x, 0.012, 0), (1.2, 0.006, 0.12), "line_white")
    for cx in (-20.2, 20.2):                                            # crosswalks
        for i in range(6):
            t.deco((cx, 0.012, -2.0 + i * 0.8), (1.2, 0.006, 0.4), "line_white")
    # Sidewalks (raised kerbs the player steps over).
    t.solid((0, FLOOR / 2, 3.5), (41, FLOOR, 2.0), "sidewalk")
    t.solid((0, FLOOR / 2, -3.5), (41, FLOOR, 2.0), "sidewalk")
    # Plaza paving between the apartments and the police station.
    t.solid((0, FLOOR / 2, -9), (18.5, FLOOR, 9), "sand_dark")
    # Beach and boardwalk.
    t.solid((0, FLOOR / 2, -15.5), (52, FLOOR, 4), "wood", top_colour="wood")
    for x in range(-26, 27, 2):
        t.deco((x, FLOOR + 0.002, -15.5), (0.05, 0.004, 4), "wood_dark")
    t.deco((0, 0.006, -19.25), (64, 0.012, 3.5), "sand")
    # Sea bed and water surface (the surface is visual; a zone handles falling in).
    t.solid((0, -1.7, -34), (64, 1.0, 26), "sand_dark")
    t.deco((0, -0.38, -34), (64, 0.04, 26), "water")
    t.deco((0, -0.36, -41), (64, 0.04, 12), "water_deep")
    t.zone("Water", (0, -0.9, -34), (64, 1.0, 26))
    # World edge: hedges on land, buoy line at sea, plus tall invisible walls.
    for x in (-31.5, 31.5):
        t.solid((x, 0.5, 6), (1.0, 1.0, 54), "leaf_dark", top_colour="leaf")
    t.solid((0, 0.5, 32.5), (62, 1.0, 1.0), "leaf_dark", top_colour="leaf")
    for x in range(-28, 29, 4):
        t.deco((x, -0.3, -46.5), (0.3, 0.3, 0.3), "red")
    t.collider((-32.5, 6, -7), (1, 14, 82))
    t.collider((32.5, 6, -7), (1, 14, 82))
    t.collider((0, 6, 33.5), (66, 14, 1))
    t.collider((0, 6, -47.5), (66, 14, 1))

    # ================================================================== PIZZA RESTAURANT  x -19 .. -10.5
    t.begin("Building_Pizza")
    px0, px1, z0, z1 = -19.0, -10.5, 4.5, 13.5
    door_x = -14.75
    t.shell(px0, z0, px1, z1, STOREY, "wall_yellow", "roof_red", "wood", "roof_slate",
            south=[(-17.4, 1.7, 0.6, 1.4), (door_x, DOOR_W, FLOOR, DOOR_H), (-12.1, 1.7, 0.6, 1.4)],
            north=[(-11.6, DOOR_W, FLOOR, DOOR_H)], base_colour="roof_red")
    t.awning(px0 + 0.3, px1 - 0.3, z0, 1.72, 1.2, ("red", "white"), -1)
    t.sign("SLICE OF TROUBLE", (door_x, STOREY + 0.55, z0 - 0.04), 0, 5.2, 0.7, "roof_red", "FFF4DC", 3.2)
    t.sign("PIZZA", (px0 - 0.04, 1.5, 6.2), 90, 1.4, 0.5, "red", "FFFFFF", 2.6)
    # Kitchen counter along the back, stations left to right.
    counter_z = 11.0
    t.solid((-14.75, FLOOR + 0.26, counter_z), (5.6, 0.52, 0.7), "wall_cream", top_colour="metal")
    counter_top = FLOOR + 0.52
    t.marker("PizzaBase", (-16.9, counter_top, counter_z))
    for i, name in enumerate(("Cheese", "Pepperoni", "Mushroom")):
        bx = -16.2 + i * 0.5
        t.deco((bx, counter_top + 0.05, counter_z + 0.2), (0.36, 0.1, 0.26), "metal_dark")
        t.marker("Bin_" + name, (bx, counter_top + 0.12, counter_z + 0.2))
    t.deco((-16.9, counter_top + 0.01, counter_z), (0.5, 0.02, 0.5), "wood")       # prep board
    # Oven: an open-fronted box on the counter.
    ox = -14.2
    t.solid((ox, counter_top + 0.02, counter_z + 0.05), (0.8, 0.04, 0.6), "brick")
    t.solid((ox - 0.36, counter_top + 0.24, counter_z + 0.05), (0.08, 0.44, 0.6), "brick")
    t.solid((ox + 0.36, counter_top + 0.24, counter_z + 0.05), (0.08, 0.44, 0.6), "brick")
    t.solid((ox, counter_top + 0.5, counter_z + 0.05), (0.8, 0.08, 0.6), "brick")
    t.solid((ox, counter_top + 0.26, counter_z + 0.31), (0.8, 0.48, 0.08), "brick")
    t.deco((ox, counter_top + 0.05, counter_z + 0.1), (0.6, 0.02, 0.4), "orange")
    t.deco((ox, 1.45, counter_z + 0.2), (0.3, 1.0, 0.3), "metal_dark")              # chimney
    t.marker("PizzaOven", (ox, counter_top + 0.06, counter_z + 0.02))
    t.deco((-13.0, counter_top + 0.01, counter_z), (0.6, 0.02, 0.55), "tan")       # boxing mat
    t.marker("PizzaBoxing", (-13.0, counter_top + 0.02, counter_z))
    t.marker("PizzaTerminal", (px0 + WALL_T + 0.05, 0.8, 9.2), 90)
    t.marker("PizzaChef", (-15.2, FLOOR, 12.3), 180)
    for tx, tz in ((-17.2, 6.6), (-12.3, 6.8)):                                     # dining tables
        t.solid((tx, FLOOR + 0.2, tz), (0.12, 0.4, 0.12), "metal_dark")
        t.solid((tx, FLOOR + 0.43, tz), (0.9, 0.06, 0.9), "red", top_colour="white")
        for sx in (-0.7, 0.7):
            t.solid((tx + sx, FLOOR + 0.13, tz), (0.3, 0.26, 0.3), "wood")

    # ================================================================== CONVENIENCE STORE  x -9.5 .. -1
    t.begin("Building_Store")
    sx0, sx1 = -9.5, -1.0
    sdoor = -5.25
    t.shell(sx0, z0, sx1, z1, STOREY, "wall_teal", "white", "sidewalk", "roof_slate",
            south=[(-7.9, 1.8, 0.6, 1.4), (sdoor, DOOR_W, FLOOR, DOOR_H), (-2.6, 1.8, 0.6, 1.4)],
            north=[(-2.3, DOOR_W, FLOOR, DOOR_H)], base_colour="navy")
    t.awning(sx0 + 0.3, sx1 - 0.3, z0, 1.72, 1.2, ("wall_teal", "white"), -1)
    t.sign("QUICKPAW MART", (sdoor, STOREY + 0.55, z0 - 0.04), 0, 5.0, 0.7, "navy", "FFFFFF", 3.2)
    t.sign("OPEN 24 HRS", (-7.9, 1.55, z0 - 0.02), 0, 1.2, 0.2, "red", "FFFFFF", 1.1, 0.03)
    # Counter with the register; the cashier stands on the east side of it.
    t.solid((-3.2, FLOOR + 0.27, 7.0), (0.6, 0.54, 2.8), "navy", top_colour="wall_cream")
    register_top = FLOOR + 0.54
    t.solid((-3.2, register_top + 0.12, 6.2), (0.36, 0.24, 0.4), "metal_dark")
    t.deco((-3.4, register_top + 0.27, 6.2), (0.04, 0.16, 0.3), "glass")
    t.marker("Register", (-3.2, register_top + 0.24, 6.2), 270)
    t.marker("RegisterCash", (-3.25, register_top + 0.06, 7.2), 270)
    t.marker("Cashier", (-2.0, FLOOR, 7.0), 270)
    t.marker("StoreStatusSign", (-1.0 - WALL_T - 0.03, 1.35, 7.0), 270)
    # Freestanding shelves with two boards each, plus a wall shelf.
    merch = 0
    for cz in (8.3, 10.6):
        t.solid((-7.0, FLOOR + 0.03, cz), (2.6, 0.06, 0.5), "metal_dark")
        for sxp in (-8.3, -5.7):
            t.solid((sxp, FLOOR + 0.5, cz), (0.06, 1.0, 0.5), "metal_dark")
        for board in (0.36, 0.72):
            t.solid((-7.0, FLOOR + board, cz), (2.6, 0.04, 0.5), "white")
            for k in range(3):
                t.marker("Merch_%d" % merch, (-7.9 + k * 0.9, FLOOR + board + 0.03, cz))
                merch += 1
    t.solid((-5.0, FLOOR + 0.45, z1 - WALL_T - 0.2), (2.4, 0.04, 0.4), "white")
    for k in range(3):
        t.marker("Merch_%d" % merch, (-5.8 + k * 0.8, FLOOR + 0.48, z1 - WALL_T - 0.2))
        merch += 1
    t.solid((-8.9, FLOOR + 0.55, 12.3), (0.7, 1.1, 0.8), "white", top_colour="glass")      # fridge
    t.deco((-8.53, FLOOR + 0.6, 12.3), (0.03, 0.9, 0.6), "glass")
    t.zone("Store", ((sx0 + sx1) / 2, 1.0, (z0 + z1) / 2), (sx1 - sx0 - 0.2, 2.0, z1 - z0 - 0.2))
    t.deco((sdoor + 1.2, 1.75, z0 - 0.08), (0.22, 0.22, 0.1), "red")                       # alarm bell
    t.marker("StoreAlarm", (sdoor + 1.2, 1.75, z0 - 0.15))
    t.marker("StoreFront", (sdoor, FLOOR, z0 - 1.0), 0)

    # ================================================================== PAWN SHOP  x 1 .. 8.5
    t.begin("Building_Pawn")
    wx0, wx1 = 1.0, 8.5
    wdoor = 4.75
    t.shell(wx0, z0, wx1, z1, STOREY, "wall_pink", "purple", "wood_dark", "roof_slate",
            south=[(2.5, 1.5, 0.6, 1.4), (wdoor, DOOR_W, FLOOR, DOOR_H), (7.0, 1.5, 0.6, 1.4)],
            north=[(7.4, DOOR_W, FLOOR, DOOR_H)], base_colour="purple")
    t.awning(wx0 + 0.3, wx1 - 0.3, z0, 1.72, 1.2, ("purple", "yellow"), -1)
    t.sign("SECOND CHANCE PAWN", (wdoor, STOREY + 0.55, z0 - 0.04), 0, 5.6, 0.7, "purple", "FFE08A", 3.0)
    t.solid((4.75, FLOOR + 0.27, 10.4), (5.2, 0.54, 0.6), "wood_dark", top_colour="wood")
    pawn_top = FLOOR + 0.54
    t.deco((3.0, pawn_top + 0.02, 10.4), (0.9, 0.04, 0.5), "green")                         # sell tray
    t.marker("PawnSell", (3.0, pawn_top + 0.04, 10.4))
    t.marker("PawnPayoutSign", (3.0, 1.35, 10.75), 180)
    t.marker("ShopPistol", (5.3, pawn_top + 0.02, 10.4))
    t.marker("ShopSatchel", (6.6, pawn_top + 0.02, 10.4))
    t.marker("PawnBroker", (4.4, FLOOR, 11.7), 180)
    for k, col in enumerate(("yellow", "wall_blue", "red", "green")):                       # shelf clutter
        t.solid((1.6, FLOOR + 0.25 + (k % 2) * 0.5, 6.0 + k * 0.9), (0.5, 0.5, 0.5), "wood")
        t.deco((1.6, FLOOR + 0.55 + (k % 2) * 0.5, 6.0 + k * 0.9), (0.24, 0.1, 0.24), col)

    # ================================================================== GARAGE  x 10 .. 19 (open front)
    t.begin("Building_Garage")
    gx0, gx1 = 10.0, 19.0
    t.shell(gx0, z0, gx1, z1, STOREY, "wall_blue", "metal_dark", "concrete", "roof_slate",
            south=[(14.5, 5.6, FLOOR, 1.7)], base_colour="metal_dark")
    t.sign("WRENCH & ROLL", (14.5, STOREY + 0.55, z0 - 0.04), 0, 4.6, 0.7, "metal_dark", "F4C430", 3.2)
    t.sign("GARAGE - CARS SOON", (14.5, 1.86, z0 - 0.03), 0, 3.0, 0.24, "yellow", "1E1E24", 1.2, 0.03)
    t.solid((14.5, FLOOR + 0.1, 9.5), (2.0, 0.2, 3.4), "metal")                             # car lift
    for lx in (13.4, 15.6):
        t.solid((lx, FLOOR + 0.6, 9.5), (0.14, 1.2, 0.14), "yellow")
    t.car(14.5, 9.5, 0, "red", "white")
    t.solid((11.0, FLOOR + 0.3, 12.6), (1.4, 0.6, 0.7), "red", top_colour="metal")          # tool chest
    for k in range(3):
        t.solid((18.3, FLOOR + 0.17 + k * 0.34, 12.4), (0.34, 0.34, 0.9), "black", roll=90)  # tyre stack
    t.marker("VehicleSpawn", (14.5, 0.0, 1.3), 90)

    # ================================================================== ROOFTOP ROUTE AND BACK ALLEY
    t.begin("Props_Alley")
    t.dumpster(-6.0, 14.05)
    t.climbable((-6.0, 1.45, z1 + 0.05), (0.7, 1.6, 0.1))                                    # store back wall
    t.climbable((-6.0, STOREY + PARAPET / 2, z1 - 0.075), (0.7, PARAPET + 0.02, 0.17))
    t.solid((0.0, STOREY + PARAPET + 0.04, 9.0), (2.6, 0.08, 0.7), "wood")                    # plank over the alley gap
    t.solid((9.25, STOREY + PARAPET + 0.04, 8.0), (2.1, 0.08, 0.7), "wood")                   # pawn to garage
    t.solid((-10.0, STOREY + PARAPET + 0.04, 10.0), (1.6, 0.08, 0.7), "wood")                 # pizza to store
    t.climbable((19.05, 1.2, 9.0), (0.1, 2.3, 0.6), 0)                                        # garage east wall ladder
    t.crate(3.2, 0.0, 14.2, 0.6)
    t.crate(3.9, 0.0, 14.3, 0.5, 20)
    t.crate(3.4, 0.6, 14.25, 0.45, 10)
    t.dumpster(12.5, 14.05)
    t.crate(-15.5, 0.0, 14.2, 0.55)
    for x in (-18, -12, -4, 6, 16):                                                           # rooftop clutter
        t.solid((x, STOREY + 0.2, 11.5), (0.7, 0.4, 0.7), "metal", top_colour="metal_dark")
    t.solid((-13.0, STOREY + 0.5, 8.0), (0.5, 1.0, 0.5), "brick")                             # chimney on pizza roof
    # Fence between the alley and the residential lane, with three gaps.
    for a, b in ((-20.5, -19.0), (-17.0, -1.2), (1.2, 17.0), (19.0, 20.5)):
        t.solid(((a + b) / 2, 0.55, 16.9), (b - a, 1.1, 0.1), "wood", top_colour="wood_dark")
    t.marker("AlleyHide", (0.0, 0.0, 15.0))

    # ================================================================== APARTMENTS  x -19 .. -10, z -13.5 .. -4.5
    t.begin("Building_Apartments")
    ax0, ax1, az0, az1 = -19.0, -10.0, -13.5, -4.5
    adoor = -14.5
    t.shell(ax0, az0, ax1, az1, STOREY, "wall_cream", "wood_dark", "wood", "wall_cream",
            north=[(-17.3, 1.5, 0.6, 1.4), (adoor, DOOR_W, FLOOR, DOOR_H), (-11.7, 1.5, 0.6, 1.4)],
            south=[(-14.5, 1.8, 0.6, 1.4)], parapet=0, base_colour="brick")
    # Upper floor: a solid block for now (the upgraded apartment arrives in Milestone D), with a balcony.
    t.solid((-14.5, STOREY + 0.95, -9.0), (9.0, 1.9, 9.0), "wall_cream")
    for wx in (-17.3, -14.5, -11.7):
        t.deco((wx, STOREY + 1.0, az1 + 0.01), (1.3, 0.8, 0.04), "glass")
        t.deco((wx, STOREY + 1.0, az1 + 0.02), (1.42, 0.92, 0.02), "wood_dark")
        t.deco((wx, STOREY + 1.0, az0 - 0.01), (1.3, 0.8, 0.04), "glass")
    t.solid((-14.5, STOREY + 0.04, az1 + 0.55), (6.0, 0.08, 1.1), "wood_dark")               # balcony floor
    t.solid((-14.5, STOREY + 0.4, az1 + 1.07), (6.0, 0.06, 0.06), "wood_dark")               # balcony rail
    for bx in (-17.4, -15.95, -14.5, -13.05, -11.6):
        t.solid((bx, STOREY + 0.22, az1 + 1.07), (0.06, 0.36, 0.06), "wood_dark")
    roof_top = STOREY + 1.9
    t.solid((-14.5, roof_top + 0.075, -9.0), (9.3, 0.15, 9.3), "roof_red")
    for px, pz, sx_, sz_ in ((-14.5, az0 + 0.075, 9.0, 0.15), (-14.5, az1 - 0.075, 9.0, 0.15),
                             (ax0 + 0.075, -9.0, 0.15, 8.7), (ax1 - 0.075, -9.0, 0.15, 8.7)):
        t.solid((px, roof_top + 0.15 + PARAPET / 2, pz), (sx_, PARAPET, sz_), "wall_cream", top_colour="roof_red")
    t.climbable((ax1 + 0.05, 2.25, -6.0), (0.1, 4.4, 0.5))                                     # drainpipe to the roof
    t.sign("HARBOR ARMS", (adoor, 1.75, az1 + 0.04), 180, 2.6, 0.34, "wood_dark", "FFF4DC", 1.8, 0.04)
    # Inside: the player's first home.
    t.solid((-17.6, FLOOR + 0.14, -12.2), (1.9, 0.28, 1.1), "wood_dark")                      # bed
    t.deco((-17.6, FLOOR + 0.31, -12.2), (1.8, 0.08, 1.0), "wall_blue")
    t.deco((-18.3, FLOOR + 0.38, -12.2), (0.4, 0.1, 0.7), "white")
    t.solid((-11.6, FLOOR + 0.2, -11.6), (0.1, 0.4, 0.1), "wood_dark")                        # table
    t.solid((-11.6, FLOOR + 0.42, -11.6), (1.2, 0.05, 0.8), "wood")
    t.marker("EquipmentTable", (-11.6, FLOOR + 0.46, -11.6))
    t.solid((-11.0, FLOOR + 0.3, -6.2), (0.9, 0.6, 0.5), "wood")                              # dresser
    t.deco((-15.0, FLOOR + 0.006, -9.0), (2.4, 0.012, 1.6), "roof_red")                       # rug
    t.marker("PlayerSpawn", (-14.5, FLOOR, -9.5), 0)
    t.marker("ApartmentBoard", (ax0 + WALL_T + 0.04, 1.0, -8.5), 90)
    t.marker("GymButton", (ax0 + WALL_T + 0.1, 0.45, -6.4), 90)
    t.marker("ApartmentFront", (adoor, FLOOR, az1 + 1.0), 0)

    # ================================================================== POLICE STATION  x 10 .. 19
    t.begin("Building_Police")
    qx0, qx1 = 10.0, 19.0
    qdoor = 14.5
    t.shell(qx0, az0, qx1, az1, STOREY + 0.3, "wall_blue", "navy", "sidewalk", "roof_slate",
            north=[(11.8, 1.5, 0.6, 1.4), (qdoor, 1.2, FLOOR, DOOR_H), (17.2, 1.5, 0.6, 1.4)], base_colour="navy")
    t.sign("POLICE", (qdoor, STOREY + 0.85, az1 + 0.04), 180, 3.2, 0.7, "navy", "FFFFFF", 3.6)
    for sx_ in (-0.8, 0.8):                                                                    # entrance lamps
        t.deco((qdoor + sx_, 1.7, az1 + 0.12), (0.18, 0.26, 0.18), "police_blue")
    t.solid((14.5, FLOOR + 0.27, -9.5), (4.0, 0.54, 0.6), "navy", top_colour="wall_cream")     # front desk
    t.solid((17.9, FLOOR + 0.6, -12.6), (1.4, 1.2, 0.1), "metal_dark")                         # holding cell bars
    t.solid((17.2, FLOOR + 0.6, -13.0), (0.1, 1.2, 0.8), "metal_dark")
    t.marker("PoliceRelease", (qdoor, FLOOR, az1 + 1.0), 0)
    t.marker("PoliceDesk", (14.5, FLOOR, -10.4), 0)
    for i, (sx_, sz_) in enumerate(((12.0, -3.4), (17.0, -3.4), (13.2, -7.0), (15.8, -7.0), (14.5, -3.2))):
        t.marker("PoliceSpawn_%d" % i, (sx_, FLOOR, sz_), 0)
    t.marker("PatrolCar", (17.5, 0.0, -1.5), 270)

    # ================================================================== PLAZA
    t.begin("Props_Plaza")
    t.chunk.tapered((0, FLOOR + 0.18, -9), (3.0, 3.0), (3.0, 3.0), 0.36, "sidewalk", sides=8)   # fountain basin
    t.chunk.tapered((0, FLOOR + 0.33, -9), (2.6, 2.6), (2.6, 2.6), 0.08, "water", sides=8)
    t.chunk.tapered((0, FLOOR + 0.6, -9), (0.5, 0.5), (0.3, 0.3), 0.9, "sidewalk", sides=6)
    t.chunk.blob((0, FLOOR + 1.15, -9), (0.6, 0.5, 0.6), "orange")
    t.collider((0, FLOOR + 0.18, -9), (2.9, 0.36, 2.9))
    t.collider((0, FLOOR + 0.7, -9), (0.5, 1.1, 0.5))
    t.bench(-4.5, -6.5, 270)
    t.bench(4.5, -6.5, 90)
    t.bench(-4.5, -11.5, 270)
    t.bench(4.5, -11.5, 90)
    for px_, pz_ in ((-7.5, -6.0), (7.5, -6.0), (-7.5, -12.0), (7.5, -12.0)):
        t.palm(px_, pz_, 3.0, 0.2 if px_ < 0 else -0.2)
    for bx in (-6, -3, 0, 3, 6):                                                               # bollards
        t.solid((bx, FLOOR + 0.2, -4.9), (0.14, 0.4, 0.14), "metal_dark", top_colour="yellow")
    for i, (wx, wz) in enumerate(((-6, -8), (6, -8), (-3, -12), (3, -12), (0, -5.6), (-8, 3.4), (8, 3.4), (-12, -3.4), (12, -15.4), (-10, -15.6))):
        t.marker("Wander_%d" % i, (wx, FLOOR, wz))

    # ================================================================== STREET FURNITURE
    t.begin("Props_Street")
    for x in (-18, -9, 0, 9, 18):
        t.lamp(x, 2.8)
        t.lamp(x + 4.5, -2.8)
    t.car(-6.5, -1.55, 90, "wall_teal", "white")
    t.car(6.0, 1.55, 270, "yellow", "white")
    for x, z in ((-10.0, 2.9), (8.9, 2.9), (-9.6, -2.9)):                                      # hydrants and bins
        t.solid((x, FLOOR + 0.18, z), (0.16, 0.36, 0.16), "red")
    for x, z in ((-0.6, 2.9), (9.6, -2.9)):
        t.solid((x, FLOOR + 0.22, z), (0.3, 0.44, 0.3), "metal_dark", top_colour="black")
    for i, (x, z) in enumerate(((-18, 3.5), (-1, 3.5), (18, 3.5), (18, -3.5), (0, -3.5), (-18, -3.5))):
        t.marker("Patrol_%d" % i, (x, FLOOR, z))
    t.marker("Roadblock_W", (-18.0, 0.0, 0.0), 90)
    t.marker("Roadblock_E", (18.0, 0.0, 0.0), 270)
    t.sign("WELCOME TO PAWPORT", (-27.0, 1.3, 3.2), 90, 3.0, 0.8, "wood_dark", "FFF4DC", 2.4)
    t.solid((-27.0, 0.45, 3.2), (0.1, 0.9, 0.1), "wood_dark")

    # ================================================================== DOCK
    t.begin("Dock")
    deck = 0.3
    t.solid((0, 0.04, -18.4), (3.0, deck, 2.05), "wood", pitch=6.3)                            # ramp up from the boardwalk
    t.solid((0, deck - 0.05, -27.5), (3.0, 0.1, 16.5), "wood")
    for z in range(-35, -19, 1):
        t.deco((0, deck + 0.002, z), (3.0, 0.004, 0.04), "wood_dark")
    for z in (-20, -24, -28, -32, -35.5):
        for x in (-1.5, 1.5):
            t.solid((x, -0.5, z), (0.2, 2.0, 0.2), "wood_dark")
            t.deco((x, 0.62, z), (0.24, 0.16, 0.24), "white")
    t.solid((0, deck - 0.05, -37.0), (7.0, 0.1, 3.0), "wood")                                  # T-head
    t.solid((-2.6, deck + 0.55, -37.6), (1.6, 1.1, 1.6), "wall_teal")                          # bait hut
    t.chunk.gable_roof((-2.6, deck + 1.35, -37.6), (2.0, 0.5, 2.0), "roof_red", end_colour="wall_cream")
    t.sign("DOCKSIDE BAIT", (-2.6, deck + 0.85, -36.77), 180, 1.3, 0.26, "wood_dark", "FFF4DC", 1.3, 0.04)
    for cx in (1.2, 2.2):
        t.crate(cx, deck, -37.4, 0.5, 15 * cx)
    # A small boat moored on the east side.
    t.solid((4.2, -0.2, -29.0), (1.4, 0.5, 3.6), "red", top_colour="wood")
    t.deco((4.2, -0.02, -29.0), (1.1, 0.16, 3.2), "wood_dark")
    t.solid((4.2, 0.25, -28.0), (0.9, 0.6, 1.0), "white", top_colour="navy")
    t.deco((4.2, 0.9, -28.0), (0.06, 0.8, 0.06), "wood_dark")
    t.marker("DockRespawn", (0, deck, -22.0), 0)
    t.marker("Customer_3", (1.0, deck, -36.6), 0)
    t.marker("Fishing", (2.8, deck, -37.0), 180)
    for px_ in (-20, -10, 10, 20):
        t.palm(px_, -19.0, 2.6, 0.25 if px_ < 0 else -0.25)
    for bx in (-14, 14):
        t.bench(bx, -14.4, 0)

    # ================================================================== HOUSES on the lane (fronts face south)
    house_colours = (("wall_pink", "roof_slate"), ("wall_yellow", "roof_red"), ("wall_teal", "roof_slate"))
    for i, hx in enumerate((-14.0, 0.0, 14.0)):
        t.begin("House_%d" % i)
        wall_c, roof_c = house_colours[i]
        hz = 25.0
        t.solid((hx, 1.0, hz), (6.0, 2.0, 5.0), wall_c)
        t.chunk.gable_roof((hx, 2.6, hz), (6.6, 1.2, 5.6), roof_c, end_colour=wall_c)
        pitch = math.degrees(math.atan2(1.2, 2.8))
        slope = math.hypot(1.2, 2.8)
        t.collider((hx, 2.6, hz - 1.4), (6.6, 0.1, slope), pitch=-pitch)
        t.collider((hx, 2.6, hz + 1.4), (6.6, 0.1, slope), pitch=pitch)
        front = hz - 2.5
        t.deco((hx, FLOOR + 0.72, front - 0.02), (0.9, 1.4, 0.05), "wood_dark")              # door
        t.deco((hx + 0.3, FLOOR + 0.7, front - 0.05), (0.08, 0.08, 0.04), "yellow")
        for wx in (-1.9, 1.9):
            t.deco((hx + wx, 1.15, front - 0.02), (1.1, 0.8, 0.05), "glass")
            t.deco((hx + wx, 1.15, front - 0.01), (1.24, 0.94, 0.03), "white")
        t.solid((hx, 0.1, front - 0.8), (3.0, 0.2, 1.6), "wood")                              # porch
        for sx_ in (-1.4, 1.4):
            t.solid((hx + sx_, 0.95, front - 1.5), (0.1, 1.5, 0.1), "white")
        t.solid((hx, 1.75, front - 0.8), (3.3, 0.1, 1.8), roof_c)
        t.solid((hx - 2.4, 0.3, front - 2.4), (0.08, 0.6, 0.08), "wood_dark")                 # mailbox
        t.solid((hx - 2.4, 0.66, front - 2.4), (0.2, 0.16, 0.34), "red")
        for sx_ in (-2.4, 2.4):
            t.chunk.blob((hx + sx_, 0.3, front - 0.5), (0.9, 0.7, 0.7), "leaf")
        t.solid((hx, 1.9, hz + 1.2), (0.5, 1.4, 0.5), "brick") if i != 1 else None            # chimney
        t.marker("Customer_%d" % i, (hx, 0.2, front - 0.9), 180)
        t.marker("HouseFront_%d" % i, (hx, 0.0, front - 3.0), 0)
    t.begin("Props_Lane")
    for x in (-21, -7, 7, 21):
        t.lamp(x, 21.2)
    for x in (-24, -8, 8, 24):
        t.chunk.blob((x, 1.3, 29.0), (2.6, 2.2, 2.6), "leaf", sides=7)
        t.solid((x, 0.4, 29.0), (0.3, 0.8, 0.3), "wood_dark")
    for x in range(-26, 27, 4):
        t.solid((x, 0.3, 30.8), (3.6, 0.08, 0.08), "white")
        t.solid((x - 1.8, 0.25, 30.8), (0.1, 0.5, 0.1), "white")

    # ================================================================== MOVABLE PROPS (authored at the origin, facing +Z)
    t.begin("Prop_PatrolCar", record=False)
    t.car(0.0, 0.0, 0, "white", "police_blue")
    t.deco((-0.18, 0.87, -0.1), (0.3, 0.1, 0.16), "red")
    t.deco((0.18, 0.87, -0.1), (0.3, 0.1, 0.16), "police_blue")
    t.deco((0.49, 0.36, 0.0), (0.02, 0.2, 0.9), "police_blue")
    t.deco((-0.49, 0.36, 0.0), (0.02, 0.2, 0.9), "police_blue")
    t.begin("Prop_Roadblock", record=False)
    for i in range(6):
        t.deco((-1.0 + i * 0.4, 0.5, 0.0), (0.4, 0.24, 0.08), "red" if i % 2 else "white")
    for x in (-1.0, 1.0):
        t.deco((x, 0.25, 0.0), (0.08, 0.5, 0.5), "metal_dark")
        t.deco((x, 0.68, 0.0), (0.14, 0.14, 0.14), "yellow")

    return t


def main():
    lp.reset_scene()
    lp.write_palette_png()
    town = build()

    objects = []
    for name, builder in town.chunks.items():
        if builder.faces:
            objects.append(builder.build(name))
    # Keep the movable props out of the preview renders (they sit at the origin).
    props = [o for o in objects if o.name.startswith("Prop_")]

    fbx = os.path.join(lp.MODEL_DIR, "Town.fbx")
    lp.export_fbx(fbx, objects)

    bounds = {}
    for name, builder in town.chunks.items():
        if builder.faces:
            low, high = builder.bounds()
            bounds[name] = {"min": [round(v, 4) for v in low], "max": [round(v, 4) for v in high]}
    layout = {
        "version": 1,
        "colliders": town.colliders,
        "climbables": town.climbables,
        "zones": town.zones,
        "markers": town.markers,
        "signs": town.signs,
        "bounds": bounds,
    }
    with open(os.path.join(lp.MODEL_DIR, "Town.layout.json"), "w") as handle:
        json.dump(layout, handle, indent=1)

    triangles = sum(len(f) - 2 for b in town.chunks.values() for f in b.faces)
    print("TOWN chunks=%d colliders=%d climbables=%d markers=%d signs=%d triangles=%d" % (
        len(objects), len(town.colliders), len(town.climbables), len(town.markers), len(town.signs), triangles))

    for prop in props:
        prop.hide_render = True
    docs = os.path.join(lp.PROJECT, "Docs")
    lp.render_preview(os.path.join(docs, "town-overview.png"), (6, 44, -58), (0, 0, -2), lens=32)
    lp.render_preview(os.path.join(docs, "town-street.png"), (-25, 1.6, -1.2), (0, 1.2, 1.0), lens=22)
    lp.render_preview(os.path.join(docs, "town-alley.png"), (-12, 6.5, 20), (0, 1.0, 8), lens=26)
    print("DONE")


if __name__ == "__main__":
    main()
