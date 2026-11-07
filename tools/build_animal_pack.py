"""Build a deterministic in-process pet resource pack after exact RGBA validation.
Requires only Python's standard library. Optimize copies first with oxipng --nx --strip safe.
"""
import hashlib
import json
import pathlib
import struct
import zlib
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCE = ROOT.parent / "DesktopPet" / "assets"
ASSETS = ROOT / "desktop" / "pets-assets"
STEMS = ["meowser", "meowser-pomeranian", "meowser-red-panda", "meowser-capybara", "meowser-squirrel", "meowser-alpaca", "meowser-elephant"]


def rgba(path):
    data = path.read_bytes()
    assert data[:8] == b"\x89PNG\r\n\x1a\n"
    offset = 8
    payload = []
    dimensions = None
    while offset < len(data):
        length = struct.unpack_from(">I", data, offset)[0]
        kind = data[offset + 4:offset + 8]
        block = data[offset + 8:offset + 8 + length]
        assert zlib.crc32(kind + block) & 0xffffffff == struct.unpack_from(">I", data, offset + 8 + length)[0]
        if kind == b"IHDR":
            width, height, depth, color, compression, filtering, interlace = struct.unpack(">IIBBBBB", block)
            assert (depth, color, compression, filtering, interlace) == (8, 6, 0, 0, 0)
            dimensions = (width, height)
        if kind == b"IDAT":
            payload.append(block)
        offset += length + 12
    width, height = dimensions
    stride = width * 4
    raw = zlib.decompress(b"".join(payload))
    assert len(raw) == height * (stride + 1)
    previous = bytearray(stride)
    output = bytearray()
    for y in range(height):
        start = y * (stride + 1)
        mode = raw[start]
        row = bytearray(raw[start + 1:start + stride + 1])
        if mode == 1:
            for x in range(4, stride):
                row[x] = (row[x] + row[x - 4]) & 255
        elif mode == 2:
            for x in range(stride):
                row[x] = (row[x] + previous[x]) & 255
        elif mode == 3:
            for x in range(stride):
                row[x] = (row[x] + ((row[x - 4] if x >= 4 else 0) + previous[x]) // 2) & 255
        elif mode == 4:
            for x in range(stride):
                a = row[x - 4] if x >= 4 else 0
                b = previous[x]
                c = previous[x - 4] if x >= 4 else 0
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                row[x] = (row[x] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        else:
            assert mode == 0
        output.extend(row)
        previous = row
    return dimensions, bytes(output)


records = []
for stem in STEMS:
    original = SOURCE / (stem + "-atlas.png")
    optimized = ASSETS / original.name
    old_dims, old_rgba = rgba(original)
    new_dims, new_rgba = rgba(optimized)
    assert old_dims == new_dims and old_rgba == new_rgba, f"Changed RGBA pixels: {stem}"
    frames = ASSETS / (stem + "-frames.json")
    assert frames.read_bytes() == (SOURCE / frames.name).read_bytes()
    assert len(json.loads(frames.read_text())["frames"]) == 64
    records.append({"asset": stem, "width": new_dims[0], "height": new_dims[1], "rgba_sha256": hashlib.sha256(new_rgba).hexdigest(), "source_bytes": original.stat().st_size, "optimized_bytes": optimized.stat().st_size})

pack = ROOT / "desktop" / "pets.zip"
with zipfile.ZipFile(pack, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
    for stem in STEMS:
        for suffix in ["-atlas.png", "-frames.json"]:
            path = ASSETS / (stem + suffix)
            entry = zipfile.ZipInfo(path.name, (2020, 1, 1, 0, 0, 0))
            entry.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(entry, path.read_bytes(), compresslevel=9)
    entry = zipfile.ZipInfo("manifest.json", (2020, 1, 1, 0, 0, 0))
    entry.compress_type = zipfile.ZIP_DEFLATED
    archive.writestr(entry, json.dumps({"version": 1, "pixel_validation": "exact RGBA including transparent pixels", "images": records}, ensure_ascii=False, indent=2).encode(), compresslevel=9)
report = {"images": records, "pack_bytes": pack.stat().st_size, "pack_sha256": hashlib.sha256(pack.read_bytes()).hexdigest(), "exact_rgba": True}
(ROOT / "artifacts" / "animal-compression.json").write_text(json.dumps(report, indent=2))
print(json.dumps({"exact_rgba": True, "images": len(records), "pack_MiB": round(pack.stat().st_size / 1048576, 3)}))
