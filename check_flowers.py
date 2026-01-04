import json
import base64
import struct

with open("Mods/pokemon-emerald/Definitions/Entities/Maps/Hoenn/LittlerootTown.json", "r") as f:
    mapdata = json.load(f)

for layer in mapdata.get("layers", []):
    if layer.get("layerId") == "Ground":
        data_b64 = layer["data"]
        data_bytes = base64.b64decode(data_b64)
        width = layer["width"]
        height = layer["height"]

        gids = []
        for i in range(0, len(data_bytes), 4):
            gid = struct.unpack("<I", data_bytes[i:i+4])[0]
            gids.append(gid)

        flowers = []
        for idx, gid in enumerate(gids):
            raw_gid = gid & 0x1FFFFFFF
            if raw_gid == 80:
                x = idx % width
                y = idx // width
                flowers.append((x, y, raw_gid))

        print("Ground layer:", width, "x", height, "=", len(gids), "tiles")
        print("Found", len(flowers), "flower tiles (GID 80):")
        for x, y, gid in flowers:
            print("  Position", x, y)
        break
