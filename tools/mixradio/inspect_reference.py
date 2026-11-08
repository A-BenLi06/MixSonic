"""Read MixRadio packages without loading their application code.

Python 3 standard library only. Decompiled source and extracted assets are local
reference material; the default output is inside the ignored .tmp directory.
"""

import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

from xbf_v1 import decode as decode_xbf


ILSPY_VERSION = "9.1.0.7988"  # Runs on the .NET 8 runtime; an SDK is not needed.
ROOT = Path(__file__).resolve().parents[2]


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def local_name(name):
    return name.rsplit("}", 1)[-1]


def inventory(root):
    return [
        {"path": p.relative_to(root).as_posix(), "bytes": p.stat().st_size,
         "sha256": digest(p)}
        for p in sorted(root.rglob("*")) if p.is_file()
    ]


def safe_extract(archive, destination, prefix=""):
    destination = destination.resolve()
    names = set()
    for entry in archive.infolist():
        name = entry.filename.replace("\\", "/")
        if prefix:
            if not name.startswith(prefix):
                continue
            name = name[len(prefix):]
        if not name or entry.is_dir():
            continue
        target = (destination / name).resolve()
        if not target.is_relative_to(destination) or target == destination:
            raise ValueError(f"Archive entry escapes destination: {name}")
        folded = str(target).casefold()
        if folded in names:
            raise ValueError(f"Duplicate archive entry: {name}")
        names.add(folded)
        target.parent.mkdir(parents=True, exist_ok=True)
        with archive.open(entry) as source, target.open("wb") as output:
            shutil.copyfileobj(source, output)


def managed_pe(path):
    """Check the PE CLI directory; do not execute or reflect over the assembly."""
    data = path.read_bytes()
    if len(data) < 64 or data[:2] != b"MZ":
        return False
    pe = struct.unpack_from("<I", data, 60)[0]
    if pe + 24 > len(data) or data[pe:pe + 4] != b"PE\0\0":
        return False
    optional = pe + 24
    magic = struct.unpack_from("<H", data, optional)[0]
    directory = optional + (96 if magic == 0x10b else 112 if magic == 0x20b else 0)
    cli = directory + 14 * 8
    return directory > optional and cli + 8 <= len(data) and any(
        struct.unpack_from("<II", data, cli))


def manifest(path, phone=False):
    xml = ET.parse(path).getroot()
    if phone:
        app = next(node for node in xml if local_name(node.tag) == "App")
        identity = dict(app.attrib)
        identity["AppPlatformVersion"] = xml.attrib.get("AppPlatformVersion")
    else:
        identity = next(node.attrib for node in xml if local_name(node.tag) == "Identity")
    return {"identity": identity, "declarations": [
        {"kind": local_name(node.tag), **node.attrib}
        for node in xml.iter() if local_name(node.tag) in
        {"Capability", "DeviceCapability", "Application", "DefaultTask",
         "BackgroundServiceAgent", "Resource", "Language", "Extension"}
    ]}


def obtain_decompiler(path, fetch, output):
    if path:
        path = path.resolve()
        if not path.is_file():
            raise FileNotFoundError(path)
        return path
    if not fetch:
        return None
    tool_dir = output / "tooling" / f"ilspycmd-{ILSPY_VERSION}"
    tool_dir.mkdir(parents=True, exist_ok=True)
    package = tool_dir / "ilspycmd.nupkg"
    if not package.exists():
        url = ("https://api.nuget.org/v3-flatcontainer/ilspycmd/"
               f"{ILSPY_VERSION}/ilspycmd.{ILSPY_VERSION}.nupkg")
        with urllib.request.urlopen(url, timeout=60) as response, package.open("wb") as stream:
            shutil.copyfileobj(response, stream)
    with zipfile.ZipFile(package) as archive:
        safe_extract(archive, tool_dir / "runtime", "tools/net8.0/any/")
    (tool_dir / "package.sha256").write_text(digest(package) + "\n", encoding="utf-8")
    return tool_dir / "runtime" / "ilspycmd.dll"


def decompile(assembly, references, output, decompiler):
    result = {"assembly": assembly.name, "sha256": digest(assembly),
              "managed": managed_pe(assembly), "decompiled": False}
    if not decompiler or not result["managed"]:
        return result
    output.mkdir(parents=True, exist_ok=True)
    signature = output / "assembly.sha256"
    if signature.exists() and signature.read_text(encoding="utf-8").strip() == result["sha256"]:
        result.update(decompiled=True, sourceFiles=len(list(output.rglob("*.cs"))),
                      xamlFiles=len(list(output.rglob("*.xaml"))))
        return result
    command = [shutil.which("dotnet") or "dotnet", str(decompiler),
               "--disable-updatecheck", "-p", "-o", str(output),
               "-r", str(references), str(assembly)]
    with (output / "decompiler.log").open("w", encoding="utf-8") as log:
        completed = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, check=False)
    if completed.returncode:
        raise RuntimeError(f"ILSpy failed for {assembly.name}; see {output / 'decompiler.log'}")
    result["decompiled"] = True
    result["sourceFiles"] = len(list(output.rglob("*.cs")))
    result["xamlFiles"] = len(list(output.rglob("*.xaml")))
    signature.write_text(result["sha256"] + "\n", encoding="utf-8")
    return result


def xaml_summary(root):
    documents, tokens = [], []
    for path in sorted(root.rglob("*.xaml")):
        try:
            xml = ET.parse(path).getroot()
        except ET.ParseError:
            continue
        controls = Counter(local_name(node.tag) for node in xml.iter())
        documents.append({"path": path.relative_to(root).as_posix(),
                          "root": local_name(xml.tag), "controls": dict(controls)})
        for node in xml.iter():
            if local_name(node.tag) in {"Color", "FontFamily", "Double", "Thickness"}:
                key = node.attrib.get("{http://schemas.microsoft.com/winfx/2006/xaml}Key")
                if key and node.text:
                    tokens.append({"path": path.relative_to(root).as_posix(),
                                   "key": key, "value": node.text.strip()})
    return {"documents": documents, "designTokens": tokens}


def inspect_xap(package, output, decompiler):
    extracted = output / "package"
    with zipfile.ZipFile(package) as archive:
        safe_extract(archive, extracted)
    result = {"kind": "windows-phone-xap", "sha256": digest(package),
              "manifest": manifest(extracted / "WMAppManifest.xml", phone=True),
              "files": inventory(extracted), "assemblies": []}
    for name in ("Nokia.Media.MusicClient.dll", "Nokia.Media.AudioAgent.Core.dll",
                 "Nokia.Media.MusicClient.Translations.dll"):
        source = extracted / name
        if source.exists():
            result["assemblies"].append(decompile(
                source, extracted, output / "decompiled" / source.stem, decompiler))
    result.update(xaml_summary(output / "decompiled"))
    return result


def find_makepri(explicit):
    if explicit:
        return explicit.resolve()
    kits = Path(os.environ.get("ProgramFiles(x86)", "C:/Program Files (x86)")) / "Windows Kits"
    # Prefer the reference app's Windows 8.1 resource format.
    for version in ("8.1", "10", "8.0"):
        candidate = kits / version / "bin" / "x86" / "makepri.exe"
        if candidate.exists():
            return candidate
    return None


def inspect_installed(package, output, decompiler, makepri):
    xml = ET.parse(package / "AppxManifest.xml").getroot()
    app = next(node for node in xml.iter() if local_name(node.tag) == "Application")
    result = {"kind": "windows-store-installed", "manifest": manifest(package / "AppxManifest.xml"),
              "files": inventory(package), "assemblies": [], "xbf": []}
    names = [app.attrib["Executable"], "Nokia.Music.Features.dll", "Nokia.Music.Private.dll"]
    for name in names:
        source = (package / name).resolve()
        if not source.is_relative_to(package):
            raise ValueError("Manifest executable escapes package directory")
        if source.exists():
            result["assemblies"].append(decompile(
                source, package, output / "decompiled" / source.stem, decompiler))
    xbf_dir = output / "xbf-metadata"
    for source in sorted(package.rglob("*.xbf")):
        metadata = decode_xbf(source)
        relative = source.relative_to(package)
        destination = (xbf_dir / relative).with_suffix(".json")
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(json.dumps(metadata, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        result["xbf"].append({"path": relative.as_posix(), "version": metadata["version"],
                              "strings": len(metadata.get("strings", [])),
                              "coverage": metadata["coverage"]})
    pri = package / "resources.pri"
    result["priDumped"] = False
    if pri.exists() and makepri:
        output.mkdir(parents=True, exist_ok=True)
        with (output / "makepri.log").open("wb") as log:
            completed = subprocess.run([str(makepri), "dump", "/if", str(pri), "/of",
                                        str(output / "resources.xml"), "/dt", "detailed", "/o"],
                                       stdout=log, stderr=subprocess.STDOUT, check=False)
        if completed.returncode:
            raise RuntimeError(f"MakePri failed; see {output / 'makepri.log'}")
        result["priDumped"] = True
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--xap", type=Path)
    parser.add_argument("--installed-dir", type=Path)
    parser.add_argument("--output", type=Path, default=ROOT / ".tmp" / "mixradio-audit")
    parser.add_argument("--decompiler", type=Path, help="Path to ilspycmd.dll")
    parser.add_argument("--fetch-decompiler", action="store_true")
    parser.add_argument("--makepri", type=Path)
    args = parser.parse_args()
    if not args.xap and not args.installed_dir:
        parser.error("Provide --xap, --installed-dir, or both")
    output = args.output.resolve()
    source_paths = [p.resolve() for p in (args.xap, args.installed_dir) if p]
    if any(output == p or (p.is_dir() and output.is_relative_to(p)) for p in source_paths):
        parser.error("Output must be separate from the reference package")
    output.mkdir(parents=True, exist_ok=True)
    decompiler = obtain_decompiler(args.decompiler, args.fetch_decompiler, output)
    results = []
    if args.xap:
        results.append(inspect_xap(args.xap.resolve(), output / "wp", decompiler))
    if args.installed_dir:
        results.append(inspect_installed(args.installed_dir.resolve(), output / "pc",
                                         decompiler, find_makepri(args.makepri)))
    index = {"schemaVersion": 1, "generatedAt": datetime.now(timezone.utc).isoformat(),
             "decompiler": ILSPY_VERSION if args.fetch_decompiler else str(decompiler or "not used"),
             "sources": results}
    (output / "index.json").write_text(json.dumps(index, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    lines = ["# MixRadio reference inventory", "", f"Generated: {index['generatedAt']}", ""]
    for result in results:
        lines += [f"## {result['kind']}", "", f"Identity: `{json.dumps(result['manifest']['identity'])}`",
                  "", f"Files: {len(result['files'])}; managed application assemblies: "
                  f"{sum(a['managed'] for a in result['assemblies'])}", "",
                  "| Assembly | Decompiled | C# files | XAML files |", "| --- | --- | ---: | ---: |"]
        lines += [f"| {a['assembly']} | {a['decompiled']} | {a.get('sourceFiles', 0)} | {a.get('xamlFiles', 0)} |"
                  for a in result["assemblies"]]
        if "xbf" in result:
            lines += ["", f"XBF files: {len(result['xbf'])}. Version 1 object/property trees "
                      "are decoded; these are diagnostic trees, not buildable XAML or runtime evidence.",
                      f"PRI dumped: {result['priDumped']}"]
        lines.append("")
    (output / "inventory.md").write_text("\n".join(lines), encoding="utf-8")
    print(f"Reference inventory: {output / 'index.json'}")
    print(f"Readable summary: {output / 'inventory.md'}")


if __name__ == "__main__":
    main()
