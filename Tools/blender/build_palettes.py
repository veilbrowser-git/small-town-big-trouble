"""Writes the recoloured palette atlases used for non-player characters.

Every character shares the red panda meshes. A different atlas swaps the fur
colours, which costs one tiny texture and no extra geometry.

    /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P Tools/blender/build_palettes.py
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import lowpoly as lp  # noqa: E402

VARIANTS = {
    # name: replacements for the fur colours (orange, maroon, darkbrown) and markings (cream, tan)
    "Officer": {"orange": "3C5FBF", "maroon": "24387A", "darkbrown": "17234F"},
    "Cashier": {"orange": "9A9DA6", "maroon": "5F636E", "darkbrown": "3A3D46", "cream": "F2F2EE"},
    "Chef": {"orange": "F1EEE6", "maroon": "CFC9BC", "darkbrown": "8E8A80"},
    "Broker": {"orange": "8E6BBF", "maroon": "5B3F86", "darkbrown": "38265A"},
    "CitizenA": {"orange": "C08A4E", "maroon": "83582E", "darkbrown": "4E3319"},
    "CitizenB": {"orange": "F09AB0", "maroon": "B4627E", "darkbrown": "743C52"},
    "CitizenC": {"orange": "5FB58A", "maroon": "357A58", "darkbrown": "1F4D37"},
}

lp.write_palette_png()
for name, overrides in VARIANTS.items():
    path = os.path.join(lp.MODEL_DIR, "Palette_%s.png" % name)
    lp.write_palette_png(path, overrides)
    print("PALETTE", path)
