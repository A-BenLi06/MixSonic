"""Bounded, read-only decoder for Windows 8/8.1 XBF version 1.

Format reference: Microsoft's MIT-licensed DumpXbf/FileFormat.cs and XBFReader.cs
in microsoft/microsoft-ui-xaml. This independent decoder retains a diagnostic
object/property tree, including flags and namespaces; it does not claim to
recreate buildable source XAML or execute markup extensions.
"""

import math
from pathlib import Path
import struct


class Reader:
    def __init__(self, data, start=0, end=None):
        self.data, self.pos = data, start
        self.end = len(data) if end is None else end

    def read(self, fmt):
        size = struct.calcsize("<" + fmt)
        if self.pos + size > self.end:
            raise ValueError(f"Truncated XBF at byte {self.pos}")
        value = struct.unpack_from("<" + fmt, self.data, self.pos)
        self.pos += size
        return value[0] if len(value) == 1 else value

    def text(self):
        length = self.read("I")
        size = length * 2
        if self.pos + size > self.end:
            raise ValueError(f"Truncated XBF string at byte {self.pos}")
        result = self.data[self.pos:self.pos + size].decode("utf-16-le")
        self.pos += size
        return result


def value(reader):
    kind = reader.read("B")
    if kind == 0:
        result = None
    elif kind in (1, 2):
        result = kind == 2
    elif kind in (3, 6, 8, 11):
        result = reader.read("f")
        if not math.isfinite(result):
            result = str(result)
    elif kind == 4:
        result = reader.read("i")
    elif kind == 5:
        result = reader.text()
    elif kind == 7:
        result = list(reader.read("ffff"))
    elif kind == 9:
        unit, length = reader.read("If")
        result = {"unit": unit, "length": length}
    elif kind == 10:
        result = f"#{reader.read('I'):08X}"
    else:
        raise ValueError(f"Unsupported XBF value type {kind} at byte {reader.pos - 1}")
    return {"kind": kind, "value": result}


def decode(path):
    data = Path(path).read_bytes()
    if len(data) < 132 or data[:4] != b"XBF\0":
        raise ValueError("Invalid XBF header")
    metadata_size, node_size, major, minor = struct.unpack_from("<IIII", data, 4)
    if 12 + metadata_size + node_size != len(data):
        raise ValueError("Invalid XBF section bounds")
    result = {"version": f"{major}.{minor}", "metadataBytes": metadata_size,
              "nodeBytes": node_size}
    if (major, minor) != (1, 0):
        return {**result, "unsupported": True, "coverage": "header only"}
    offsets = list(struct.unpack_from("<QQQQQQ", data, 20)) + [metadata_size]
    if offsets[0] < 120 or offsets != sorted(offsets) or offsets[-1] > metadata_size:
        raise ValueError("Invalid XBF metadata offsets")
    tables = []
    for index, (start, end) in enumerate(zip(offsets, offsets[1:])):
        table_reader = Reader(data, 12 + start, 12 + end)
        count = table_reader.read("I")
        if count > (end - start) // 4:
            raise ValueError("Invalid XBF table count")
        if index == 0:
            table = [table_reader.text() for _ in range(count)]
        else:
            fmt = ("II", "II", "III", "III", "I")[index - 1]
            table = [table_reader.read(fmt) for _ in range(count)]
        if table_reader.pos != table_reader.end:
            raise ValueError(f"Unexpected remainder in XBF metadata table {index}")
        tables.append(table)
    strings, assemblies, type_namespaces, types, properties, xml_namespaces = tables

    def type_info(index):
        flags, namespace_id, string_id = types[index]
        return {"type": strings[string_id], "flags": flags,
                "namespaceId": namespace_id, "typeId": index}

    def property_info(index):
        flags, type_id, string_id = properties[index]
        return {"name": strings[string_id], "flags": flags,
                "ownerTypeId": type_id, "propertyId": index, "values": []}

    reader = Reader(data, 12 + metadata_size)
    objects, members, roots, namespaces = [], [], [], []
    node_count = 0
    while reader.pos < reader.end:
        node_offset = reader.pos
        node = reader.read("B")
        node_count += 1
        if node == 1:
            index, flags = reader.read("II")
            current = {**type_info(index), "nodeFlags": flags,
                       "offset": node_offset, "properties": []}
            objects.append(current)
        elif node == 2:
            if not objects:
                raise ValueError("Unbalanced XBF end object")
            current = objects.pop()
            if members:
                members[-1]["values"].append({"object": current})
            else:
                roots.append(current)
        elif node == 3:
            index, flags = reader.read("II")
            members.append({**property_info(index), "nodeFlags": flags, "offset": node_offset})
        elif node == 4:
            if not objects or not members:
                raise ValueError("Unbalanced XBF end property")
            objects[-1]["properties"].append(members.pop())
        elif node in (5, 6):
            if not members:
                raise ValueError("XBF value without property")
            if node == 5:
                index, flags = reader.read("II")
                item = {"kind": "text", "value": strings[index], "nodeFlags": flags}
            else:
                item = value(reader)
            members[-1]["values"].append(item)
        elif node == 7:
            index, flags = reader.read("II")
            namespaces.append({"uri": strings[xml_namespaces[index]],
                               "prefix": reader.text(), "nodeFlags": flags})
        elif node in (8, 9):
            pass
        elif node == 10:
            reader.read("hh")
        elif node == 11:
            reader.read("II")
        else:
            raise ValueError(f"Unsupported XBF node {node} at byte {node_offset}")
    if objects or members:
        raise ValueError("Unbalanced XBF node stream")
    return {**result, "coverage": "decoded diagnostic object/property tree",
            "nodeCount": node_count, "strings": strings, "namespaces": namespaces,
            "assemblies": assemblies, "typeNamespaces": type_namespaces,
            "types": types, "properties": properties, "objects": roots}
