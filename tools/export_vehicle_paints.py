"""Export factory paint swatches from an installed TDU2 catalogue and material banks.

Swatches use the material's RAMPCLR1 base colour where available. They do not
reproduce lighting, metallic flakes, or textures. Unresolved finishes remain null.
"""
import argparse
import json
import math
from pathlib import Path
import re
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "Decoder"))
from tdu2_save_tool import decode_xmbf_to_dict


def path_hash(path):
    high, low = 0x0CD5E44A, 0xFAC432B1
    polynomial = (0x00F0D50B << 32) | 0x00600340
    for char in path.lower().encode("ascii"):
        carry = low >> 24
        remainder = 0
        for bit in range(8):
            if carry & (1 << bit):
                remainder ^= polynomial << bit
        low, high = ((low << 8) ^ (high >> 24) ^ remainder) & 0xFFFFFFFF, (
            (high << 8) ^ char ^ (remainder >> 32)
        ) & 0xFFFFFFFF
    return (high << 32) | low


def pack_priority(name):
    name = name.casefold()
    if re.fullmatch(r"bigfile_eu_[1-5]", name):
        return 0, int(name[-1])
    for order, prefix in enumerate(("tdu2v", "tdu2dlcexplov", "tdu2dlc2v"), 1):
        match = re.fullmatch(prefix + r"(\d+)", name)
        if match:
            return order, int(match[1])
    if name in ("tduwbasepack", "tduwcarpack"):
        return 4, int(name == "tduwcarpack")
    return None


def decode_database(raw):
    key = struct.unpack("<4I", bytes.fromhex("4a3ce24f11c2ba803abd1769bd8e52f0"))
    end = len(raw) // 8 * 8
    previous = struct.unpack_from("<2I", raw)
    decoded = bytearray()
    for block in struct.iter_unpack("<2I", raw[8:end]):
        y, z = block
        total = 0xC6EF3720
        for _ in range(32):
            z = (z - ((y + ((y >> 5) ^ (y << 4))) ^ (total + key[(total >> 11) & 3]))) & 0xFFFFFFFF
            total = (total + 0x61C88647) & 0xFFFFFFFF
            y = (y - ((z + ((z >> 5) ^ (z << 4))) ^ (total + key[total & 3]))) & 0xFFFFFFFF
        decoded.extend(struct.pack("<2I", previous[0] ^ y, previous[1] ^ z))
        previous = block
    decoded.extend(raw[end:])
    if decoded[:4] != b"XMBF":
        raise ValueError("Unsupported installed database.")
    return decode_xmbf_to_dict(decoded)[1]


class GameAssets:
    def __init__(self, installation):
        self.game = Path(installation).resolve()
        self.entries = {}
        for index in sorted(self.game.glob("*.map")):
            archive = index.with_suffix(".big")
            if not archive.is_file():
                continue
            data = index.read_bytes()
            if data[:4] != b"XMBF":
                raise ValueError(f"Unsupported archive index {index.name}.")
            if data[8:12] == b"\0\0\0\x1c":
                section = struct.unpack_from(">I", data, 16)[0]
                count, hashes, records, offsets = struct.unpack_from(">4I", data, section + 4)
                if count != records:
                    raise ValueError(f"Invalid archive index {index.name}.")
                keys = struct.unpack_from(f">{count}Q", data, section + hashes)
                rows = [(size, offset, False) for size, offset in struct.iter_unpack(
                    ">IQ", data[section + offsets:section + offsets + count * 12])]
            else:
                _, directory = decode_xmbf_to_dict(data)
                keys = directory["Hashcodes"]
                rows = [(r["CompressedSize"] or r["UncompressedSize"], r["BigfileOffset"],
                         bool(r["CompressedSize"])) for r in directory["Files"]]
            if len(keys) != len(rows):
                raise ValueError(f"Invalid archive entry count in {index.name}.")
            archive_size = archive.stat().st_size
            for key, (size, offset, compressed) in zip(keys, rows):
                if offset < 0 or size <= 0 or offset + size > archive_size:
                    raise ValueError(f"Invalid archive entry in {index.name}.")
                self.entries.setdefault(key, []).append(
                    (pack_priority(index.stem), archive, offset, size, compressed))

    def locate(self, path):
        loose = self.game / path
        if loose.is_file():
            return loose, 0, loose.stat().st_size, False
        choices = self.entries.get(path_hash(path))
        if not choices:
            return None
        if any(choice[0] is None for choice in choices):
            raise ValueError(f"Unknown archive load priority for {path}.")
        _, archive, offset, size, compressed = max(choices, key=lambda choice: choice[0])
        if compressed:
            raise ValueError(f"Unsupported compressed asset {path}.")
        return archive, offset, size, True

    @staticmethod
    def read(location, start=0, count=None):
        archive, offset, size, encrypted = location
        count = size - start if count is None else count
        if start < 0 or count < 0 or start + count > size:
            raise ValueError("Asset read exceeds its archive entry.")
        with archive.open("rb") as stream:
            stream.seek(offset + start)
            data = bytearray(stream.read(count))
        if len(data) != count:
            raise ValueError("Truncated archive entry.")
        if encrypted:
            key = b"\xd7\xa8\xe2\xd4"
            for index in range(4):
                table = bytes(value ^ key[(start + index) % 4] for value in range(256))
                data[index::4] = data[index::4].translate(table)
        return bytes(data)

    def materials(self, path):
        location = self.locate(path)
        if location is None:
            return {}
        header = self.read(location, 0, 64)
        if header[8:12] != b"KNAB":
            raise ValueError(f"Unsupported vehicle bank {path}.")
        count, _, directory = struct.unpack_from("<3I", header, 40)
        if count > 4096:
            raise ValueError("Unexpected bank file count.")
        records = self.read(location, directory + 8, count * 20)
        result = {}
        for index in range(count):
            start, size = struct.unpack_from("<2I", records, index * 20)
            if size < 16:
                continue
            signature = self.read(location, start, 16)
            if signature[12:16] == b".2DM":
                if size > 4 * 1024 * 1024:
                    raise ValueError("Unexpected material library size.")
                result.update(material_colours(self.read(location, start, size)))
        return result


def material_key(name):
    """Eden's case-insensitive eight-byte material name key."""
    key = bytearray(8)
    for i, char in enumerate(name.upper().encode("ascii")):
        key[i % 8] = (key[i % 8] + char) & 255
    return bytes(key)


def rgb_hex(values):
    if any(not math.isfinite(v) or not 0 <= v <= 1.001 for v in values[:3]):
        return None
    return "#" + "".join(f"{min(255, round(v * 255)):02X}" for v in values[:3])


def material_colour(material):
    # MAT has sixteen reserved texture slots, followed by the shader header.
    # Two counts describe runtime and stored parameters. Each has a 16-byte
    # descriptor and a parallel 16-byte value, including the runtime slots.
    if len(material) >= 0x350:
        runtime, stored = struct.unpack_from("<2H", material, 0x31A)
        count = runtime + stored
        if count <= 256 and 0x350 + count * 32 <= len(material):
            values = 0x350 + count * 16
            for index in range(count):
                at = 0x350 + index * 16
                key = material[at:at + 8]
                kind, dynamic, length = struct.unpack_from("<HHI", material, at + 8)
                if key == b"RAMPCLR1" and kind == 2 and not dynamic and length == 16:
                    return rgb_hex(struct.unpack_from("<4f", material, values + index * 16))
    # Older non-ramp materials use the diffuse colour directly.
    if len(material) >= 224:
        diffuse = struct.unpack_from("<4f", material, 208)
        return rgb_hex(diffuse)
    return None


def material_colours(data):
    if len(data) < 16 or data[12:16] != b".2DM" or struct.unpack_from("<I", data, 8)[0] != len(data):
        raise ValueError("Unsupported material library.")
    result = {}

    def visit(start, end, depth=0):
        if depth > 16:
            raise ValueError("Material segment nesting is too deep.")
        while start < end:
            if start + 16 > end:
                raise ValueError("Truncated material segment.")
            tag, _, size, child = struct.unpack_from("<4s3I", data, start)
            if size < 16 or start + size > end:
                raise ValueError("Invalid material segment size.")
            if tag == b"MAT.":
                material = data[start:start + size]
                colour = material_colour(material)
                name = material[32:64].split(b"\0", 1)[0].decode("ascii")
                if colour is not None and name:
                    result[name.casefold()] = colour
                    result[material[16:24]] = colour
            if 16 <= child < size:
                visit(start + child, start + size, depth + 1)
            start += size

    visit(16, len(data))
    return result


def resolve_material(materials, name):
    if not name or name == "##":
        return None
    return materials.get(name.casefold()) or materials.get(material_key(name))


def build_catalogue(assets, database):
    cars = {row["_id_car"]: row for row in database["DBCarPhysicsData"]}
    rows = {}
    for paint in database["DBCarsAndColors"]:
        rows.setdefault(paint["_car"], []).append(paint)
    result = []
    for car_id, paints in rows.items():
        car = cars.get(car_id)
        materials = {}
        if car and car["_car_file_name"] not in ("", "##"):
            path = f'euro/bnk/vehicules/high/{car["_car_file_name"].lower()}.bnk'
            materials = assets.materials(path)
        for index, paint in enumerate(paints):
            primary = paint["_color_id_1"]
            secondary = paint["_color_id_2"]
            name = paint["_color_name"].strip()
            result.append({
                "vehicle_id": car_id,
                "paint_index": index,
                "name": name if name not in ("", "--", "##") else "Factory finish",
                "price": paint["_price"],
                "primary": resolve_material(materials, primary),
                "secondary": resolve_material(materials, secondary),
                "has_secondary": secondary not in ("", "##"),
            })
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("installation", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    assets = GameAssets(args.installation)
    source = assets.locate("euro/bnk/database/db_data.cpr")
    if source is None:
        raise ValueError("The installed game database was not found.")
    data = build_catalogue(assets, decode_database(assets.read(source)))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Exported {len(data)} factory paints; {sum(r['primary'] is not None for r in data)} have base-colour swatches.")


if __name__ == "__main__":
    main()

