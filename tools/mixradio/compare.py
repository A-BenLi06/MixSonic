"""Portable PNG comparison viewer with explicit capture provenance.

Side-by-side, overlay, wipe, crop and pixel difference use browser Canvas.
No image library, external script, server, resampling, or automatic UI verdict.
"""

import base64
from pathlib import Path
import sys

from quality import InputParser, digest, png_size, timestamp, write_json
import json


def describe_image(path):
    width, height = png_size(path)
    return {"name": Path(path).name, "sha256": digest(path), "width": width, "height": height}


def create_comparison(candidate, output, candidate_kind, reference=None, reference_kind="missing", title="Image comparison"):
    output = Path(output)
    if output.suffix.lower() != ".html":
        raise ValueError("Comparison output must be an .html file")
    kinds = {"runtime", "mockup", "synthetic"}
    if candidate_kind not in kinds or (reference and reference_kind not in kinds):
        raise ValueError("Declare runtime, mockup, or synthetic image provenance")
    metadata_path = output.with_suffix(".json")
    if output.exists() or metadata_path.exists():
        raise ValueError("Comparison output already exists; choose a new filename to preserve the review")
    images = {"candidate": describe_image(candidate), "reference": describe_image(reference) if reference else None}
    metadata = {"schemaVersion": 1, "title": title, "createdAt": timestamp(), "candidateKind": candidate_kind,
                "referenceKind": reference_kind if reference else "missing", "images": images,
                "reviewed": False, "decision": None, "notes": "", "metrics": None,
                "crops": {role: {"x": 0, "y": 0, "width": item["width"], "height": item["height"]} if item else None
                          for role, item in images.items()}}
    payload = {**metadata, "images": {role: dict(item) if item else None for role, item in images.items()}}
    for role, path in (("candidate", candidate), ("reference", reference)):
        if path:
            payload["images"][role]["dataUrl"] = "data:image/png;base64," + base64.b64encode(Path(path).read_bytes()).decode("ascii")
    template = Path(__file__).with_name("compare-view.html").read_text(encoding="utf-8")
    encoded = json.dumps(payload, ensure_ascii=True).replace("<", "\\u003c")
    document = template.replace("__PAYLOAD__", encoded)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(document, encoding="utf-8")
    write_json(metadata_path, metadata)
    return metadata


def main():
    parser = InputParser(description=__doc__)
    parser.add_argument("--candidate", type=Path, required=True)
    parser.add_argument("--candidate-kind", choices=["runtime", "mockup", "synthetic"], required=True)
    parser.add_argument("--reference", type=Path)
    parser.add_argument("--reference-kind", choices=["runtime", "mockup", "synthetic"])
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--title", default="MixRadio reference comparison")
    args = parser.parse_args()
    if args.reference and not args.reference_kind:
        parser.error("Declare --reference-kind; package assets and reconstructed mockups are not runtime captures")
    metadata = create_comparison(args.candidate, args.output, args.candidate_kind,
                                 args.reference, args.reference_kind, args.title)
    print("Comparison viewer: " + str(args.output.resolve()))
    print("Unreviewed metadata: " + str(args.output.with_suffix(".json").resolve()))
    if not metadata["images"]["reference"]:
        print("Reference capture missing: visual parity remains unverified")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ValueError, OSError) as error:
        print("Invalid comparison input: " + str(error), file=sys.stderr)
        sys.exit(1)
