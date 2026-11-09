"""Index the locked Navidrome source, keeping reference authority explicit."""

import argparse
from datetime import datetime, timezone, timedelta
import hashlib
import json
from pathlib import Path
import re
import subprocess


ROOT = Path(__file__).resolve().parents[2]
LOCK = Path(__file__).with_name("source-lock.json")
REGISTRATION = re.compile(
    r'\b(h|hr|h501|h410)\(\s*r(?:\.With\(adminOnly\))?\s*,(.*?)\)',
    re.DOTALL,
)


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def git(source, *arguments):
    return subprocess.check_output(
        ["git", "-C", str(source), *arguments], text=True, encoding="utf-8"
    ).strip()


def inspect(source, design_index, output):
    lock = json.loads(LOCK.read_text(encoding="utf-8-sig"))
    source = source.resolve()
    if git(source, "rev-parse", "HEAD") != lock["commit"]:
        raise ValueError("Reference revision differs from source-lock.json")
    if git(source, "status", "--porcelain"):
        raise ValueError("Reference checkout has local changes")
    if git(source, "remote", "get-url", "origin") != lock["repository"]:
        raise ValueError("Reference repository differs from source-lock.json")

    api = source / "server/subsonic/api.go"
    api_text = api.read_text(encoding="utf-8")
    registrations = []
    for match in REGISTRATION.finditer(api_text):
        kind, body = match.groups()
        for name in re.findall(r'"([A-Za-z][A-Za-z0-9]+)"', body):
            registrations.append({
                "endpoint": name,
                "registration": kind,
                "line": api_text.count("\n", 0, match.start()) + 1,
                "status": {"h501": "not_implemented_or_disabled", "h410": "deprecated"}.get(kind, "implemented"),
                "handler": next(iter(re.findall(r'api\.(\w+)', body)), None),
            })
    if not registrations or "getLyricsBySongId" not in {r["endpoint"] for r in registrations}:
        raise ValueError("Subsonic registration inventory is incomplete")

    selected = [source / "LICENSE", source / "server/subsonic/api.go"]
    for directory, pattern in (("server/subsonic", "*.go"), ("server/subsonic/responses", "*.go"),
                               ("ui/src/album", "*.js"), ("ui/src/audioplayer", "*.jsx"),
                               ("ui/src/song", "*.jsx"), ("ui/src/playlist", "*.jsx"),
                               ("ui/src/common", "*Lyrics*"), ("ui/src/hooks", "*Scrobbl*")):
        selected.extend(p for p in (source / directory).glob(pattern) if "test" not in p.name.lower())
    selected.extend(source / path for path in ("ui/src/App.jsx", "ui/src/actions/player.js", "ui/src/utils/calculateReplayGain.js"))
    features_path = Path(__file__).with_name("features.json")
    if features_path.is_file():
        features = json.loads(features_path.read_text(encoding="utf-8-sig"))
        for feature in features["features"]:
            for item in feature["sources"]:
                path = (source / item["path"]).resolve()
                if not path.is_relative_to(source) or not path.is_file():
                    raise ValueError("Missing or escaping functional source: " + item["path"])
                selected.append(path)
    files = [{"path": p.relative_to(source).as_posix(), "sha256": digest(p)}
             for p in sorted(set(selected)) if p.is_file()]
    functionality = {
        "type": "git-source", "role": "functionality", "name": "Navidrome",
        "repository": lock["repository"], "commit": lock["commit"],
        "committedAt": lock["committedAt"], "license": lock["license"],
        "files": files, "registrations": registrations,
    }
    sources = [functionality]
    if design_index is not None:
        design = json.loads(design_index.read_text(encoding="utf-8-sig"))
        if design.get("schemaVersion") != 1 or not design.get("sources"):
            raise ValueError("Invalid MixRadio design inventory")
        for original in design["sources"]:
            sources.append({**original, "role": "design"})
    result = {
        "schemaVersion": 1,
        "generatedAt": datetime.now(timezone(timedelta(hours=8))).isoformat(timespec="seconds"),
        "sourceLockSha256": digest(LOCK), "sources": sources,
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Navidrome {lock['commit']}: {len(files)} source files, {len(registrations)} route registrations")
    print(f"Reference roles: {', '.join(sorted({s['role'] for s in sources}))}")
    print(output.resolve())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ROOT / ".tmp/navidrome")
    parser.add_argument("--design-index", type=Path)
    parser.add_argument("--output", type=Path, default=ROOT / ".tmp/navidrome-audit/reference-index.json")
    args = parser.parse_args()
    inspect(args.source, args.design_index, args.output)


if __name__ == "__main__":
    main()
