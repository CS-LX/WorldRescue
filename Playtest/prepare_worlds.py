"""Create fresh vanilla-content repair fixtures in an EXISTING game's Worlds directory."""
import argparse
import array
import json
import pathlib
import struct
import sys
import xml.etree.ElementTree as ET
import zipfile
import zlib

PROJECT_GUID = "9e9a67f8-79df-4d05-8cfa-61bd8095661e"
CHEST_GUID = "08550017-af17-4955-81fa-aafaf97b92bd"


def value(parent, name, kind, content):
    return ET.SubElement(parent, "Value", Name=name, Type=kind, Value=str(content))


def group(parent, name):
    return ET.SubElement(parent, "Values", Name=name)


def project(name, entities):
    root = ET.Element("Project", Guid=PROJECT_GUID, Name="GameProject", Version="2.4", APIVersion="1.9.3.1")
    systems = ET.SubElement(root, "Subsystems")
    info = group(systems, "GameInfo")
    settings = [
        ("WorldName", "string", name), ("WorldSeed", "int", 20261107),
        ("OriginalSerializationVersion", "string", "2.4"), ("WorldSeedString", "string", "20261107"),
        ("GameMode", "Game.GameMode", "Creative"),
        ("EnvironmentBehaviorMode", "Game.EnvironmentBehaviorMode", "Static"),
        ("TimeOfDayMode", "Game.TimeOfDayMode", "Day"),
        ("TerrainGenerationMode", "Game.TerrainGenerationMode", "FlatContinent"),
        ("TerrainLevel", "int", 64), ("TerrainBlockIndex", "int", 2),
        ("TerrainOceanBlockIndex", "int", 18), ("AreWeatherEffectsEnabled", "bool", "False"),
        ("AreSupernaturalCreaturesEnabled", "bool", "False"),
        ("AreSeasonsChanging", "bool", "False"), ("BlockTextureName", "string", ""),
        ("StartingPositionMode", "Game.StartingPositionMode", "Easy"),
    ]
    for key, kind, data in settings:
        value(info, key, kind, data)
    players = group(systems, "Players")
    value(players, "NextPlayerIndex", "int", 1)
    value(players, "GlobalSpawnPosition", "Vector3", "8.5,65,13.5")
    player = group(group(players, "Players"), "0")
    value(player, "Name", "string", "修复测试员")
    value(player, "SpawnPosition", "Vector3", "8.5,65,13.5")
    value(player, "InputDevice", "Game.WidgetInputDevice", "Keyboard, Mouse")
    value(player, "PlayerClass", "Game.PlayerClass", "Male")
    group(systems, "BlocksManager")
    root.append(entities)
    ET.indent(root)
    return ET.tostring(root, encoding="utf-8", xml_declaration=True)


def chest(entity_id, position, variant, full=False):
    node = ET.Element("Entity", Id=str(entity_id), Name="Chest", Guid=CHEST_GUID)
    value(group(node, "BlockEntity"), "Coordinates", "Point3", ",".join(map(str, position)))
    slots = group(group(node, "Chest"), "Slots")
    if variant:
        for index in range(16 if full else 3):
            slot = group(slots, f"Slot{index}")
            value(slot, "Contents", "int", [5, 2, 21, 45][index % 4])
            value(slot, "Count", "int", min(40, variant + index))
    return node


def terrain(directory, sites):
    # RGN1 region table, CHK1 chunk bodies, raw-DEFLATE with one-cell runs.
    # Light bits are zero: each serialized int represents one cell.
    region_chunks = {}
    for cz in range(-2, 7):
        for cx in range(-2, 7):
            cells = array.array("i", [0]) * 65536
            cells[:256] = array.array("i", [1]) * 256
            cells[256:64 * 256] = array.array("i", [2]) * (63 * 256)
            cells[64 * 256:65 * 256] = array.array("i", [5]) * 256
            for x, y, z in sites:
                if x >> 4 == cx and z >> 4 == cz:
                    cells[(x & 15) + (z & 15) * 16 + y * 256] = 45
                    # Low stone marker around the chest gives its preview readable context.
                    for dx, dz in [(-2, -2), (2, -2)]:
                        nx, nz = x + dx, z + dz
                        if nx >> 4 == cx and nz >> 4 == cz:
                            cells[(nx & 15) + (nz & 15) * 16 + y * 256] = 21
            if sys.byteorder != "little":
                cells.byteswap()
            compressor = zlib.compressobj(6, zlib.DEFLATED, -15)
            payload = compressor.compress(bytes([0x88]) * 256 + cells.tobytes()) + compressor.flush()
            region_chunks.setdefault((cx >> 4, cz >> 4), []).append(((cx & 15) + (cz & 15) * 16, payload))
    regions = directory / "Regions"
    regions.mkdir()
    for (rx, rz), chunks in region_chunks.items():
        data = bytearray(struct.pack("<I", 0x314E4752) + bytes(2048))
        for index, payload in chunks:
            struct.pack_into("<ii", data, 4 + index * 8, len(data), len(payload))
            data += struct.pack("<I", 0x314B4843) + payload
        (regions / f"Region {rx},{rz}.dat").write_bytes(data)


def prepare(game, artifacts, suffix):
    worlds = game / "doc" / "Worlds"
    if not (game / "Survivalcraft.exe").is_file():
        raise ValueError("Choose an existing Survivalcraft game directory.")
    artifacts.mkdir(parents=True, exist_ok=True)
    result = []
    cases = [
        ("WRTest01", "修复测试01-单处双记录", 1),
        ("WRTest02", "修复测试02-32处混合", 32),
        ("WRTest03", "修复测试03-256处压力", 256),
    ]
    for slug, title, count in cases:
        destination = worlds / (slug + suffix)
        if destination.exists():
            raise FileExistsError(f"Refusing to overwrite: {destination}")
        archive = artifacts / (slug + suffix + ".scworld")
        if archive.exists():
            raise FileExistsError(f"Refusing to overwrite: {archive}")
    for slug, title, count in cases:
        destination = worlds / (slug + suffix)
        destination.mkdir(parents=True)
        entities = ET.Element("Entities")
        sites = []
        identical = 0
        manual = 0
        entity_id = 1000000
        for i in range(count):
            position = (8 + (i % 16) * 4, 65, 8 + (i // 16) * 4)
            sites.append(position)
            same = (count == 32 and i % 2 == 0) or (count == 256 and i not in (0, 255))
            copies = 2 if count != 256 else (32 if i in (0, 255) else 4)
            identical += same
            manual += not same
            for j in range(copies):
                entity_id += 1
                variant = 20 if same else (0 if j == 0 else j + 8)
                entities.append(chest(entity_id, position, variant, full=count == 256))
        data = project(title + suffix, entities)
        (destination / "Project.xml").write_bytes(data)
        (destination / "Project.bak").write_bytes(data)
        terrain(destination, sites)
        archive = artifacts / (slug + suffix + ".scworld")
        with zipfile.ZipFile(archive, "x", zipfile.ZIP_DEFLATED) as packed:
            for file in destination.rglob("*"):
                if file.is_file():
                    packed.write(file, file.relative_to(destination).as_posix())
        result.append(dict(name=title + suffix, directory=str(destination), archive=str(archive),
                           conflicts=count, records=len(entities), identical=identical, manual=manual,
                           first_position=sites[0], last_position=sites[-1]))
    manifest = artifacts / ("manifest" + suffix + ".json")
    manifest.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    args = argparse.ArgumentParser()
    args.add_argument("--game", type=pathlib.Path, required=True)
    args.add_argument("--artifacts", type=pathlib.Path, required=True)
    args.add_argument("--suffix", default="")
    options = args.parse_args()
    prepare(options.game.resolve(), options.artifacts.resolve(), options.suffix)
