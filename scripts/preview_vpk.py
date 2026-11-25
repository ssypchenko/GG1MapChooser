"""Read the directory records in a Source 2 VPK without loading its assets."""

from pathlib import PurePosixPath
import struct


MAX_INDEX_BYTES = 8 * 1024 * 1024


def parse_index(raw):
    try:
        signature, version, length = struct.unpack_from("<III", raw)
        if signature != 0x55AA1234 or version not in (1, 2):
            return []
        offset = 12 if version == 1 else 28
        if length > MAX_INDEX_BYTES or offset + length > len(raw):
            return []
        tree = raw[offset:offset + length]
        cursor = 0

        def read_name():
            nonlocal cursor
            end = tree.index(0, cursor)
            name = tree[cursor:end].decode("utf-8")
            cursor = end + 1
            return name

        entries = []
        while extension := read_name():
            while directory := read_name():
                while name := read_name():
                    preload = struct.unpack_from("<H", tree, cursor + 4)[0]
                    cursor += 18 + preload
                    entries.append(PurePosixPath(directory) / f"{name}.{extension}")
        return entries
    except (ValueError, struct.error):
        return []
