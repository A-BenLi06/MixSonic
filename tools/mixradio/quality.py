"""Evidence-backed acceptance runs. Standard-library Python; no app instrumentation.

Exit codes: 0 accepted/successfully recorded, 2 not accepted, 1 invalid input.
Missing or blocked checks never count as passing or disappear from scoring.
"""

import argparse
from datetime import datetime
import hashlib
import html
import json
import math
from pathlib import Path
import platform
import shutil
import struct
import subprocess
import sys
import zlib
from urllib.parse import quote

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SPEC = Path(__file__).with_name("acceptance-spec.json")
STATUSES = {"not_run", "blocked", "fail", "pass"}
SCREENSHOTS = {"client_screenshot", "reference_screenshot"}


class InputParser(argparse.ArgumentParser):
    def error(self, message):
        raise ValueError(message)


def timestamp():
    return datetime.now().astimezone().isoformat(timespec="seconds")


def digest(path):
    with Path(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    pending = path.with_name(path.name + ".pending")
    pending.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    pending.replace(path)


def git_state():
    def git(*args):
        return subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, check=False)
    commit = git("rev-parse", "HEAD")
    if commit.returncode:
        return {"commit": None, "trackedClean": False, "untrackedFiles": []}
    untracked = git("ls-files", "--others", "--exclude-standard", "-z")
    return {"commit": commit.stdout.strip(),
            "trackedClean": git("diff", "--quiet").returncode == 0 and git("diff", "--cached", "--quiet").returncode == 0,
            "untrackedFiles": untracked.stdout.rstrip("\0").split("\0") if untracked.stdout else []}


def safe_bundle_path(directory, relative):
    relative = Path(relative)
    path = (Path(directory) / relative).resolve()
    if relative.is_absolute() or not path.is_relative_to(Path(directory).resolve()):
        raise ValueError("Evidence path escapes the run bundle")
    return path


def png_size(path):
    maximum = 128 * 1024 * 1024
    if Path(path).stat().st_size > maximum:
        raise ValueError("Screenshot PNG exceeds the 128 MiB limit")
    data = Path(path).read_bytes()
    if len(data) < 33 or data[:8] != b"\x89PNG\r\n\x1a\n" or data[12:16] != b"IHDR" or struct.unpack_from(">I", data, 8)[0] != 13:
        raise ValueError("Screenshot evidence must be PNG")
    width, height, depth, color, compression, filtering, interlace = struct.unpack(">IIBBBBB", data[16:29])
    if not width or not height:
        raise ValueError("Screenshot dimensions are empty")
    depths = {0: (1, 2, 4, 8, 16), 2: (8, 16), 3: (1, 2, 4, 8), 4: (8, 16), 6: (8, 16)}
    if color not in depths or depth not in depths[color] or compression or filtering or interlace not in (0, 1):
        raise ValueError("Screenshot PNG has an invalid image header")
    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[color]
    passes = [(0, 0, 1, 1)] if not interlace else [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)]
    scanlines = []
    for x, y, dx, dy in passes:
        columns, rows = max(0, (width - x + dx - 1) // dx), max(0, (height - y + dy - 1) // dy)
        if columns and rows:
            row_bytes = (columns * channels * depth + 7) // 8
            scanlines.append((rows, row_bytes + 1))
    expected = sum(rows * size for rows, size in scanlines)
    if expected > maximum:
        raise ValueError("Decoded screenshot exceeds the 128 MiB limit")
    offset, compressed, ended, palette = 8, bytearray(), False, False
    while offset + 12 <= len(data):
        length = struct.unpack_from(">I", data, offset)[0]
        end = offset + 12 + length
        if end > len(data):
            raise ValueError("Screenshot PNG is truncated")
        chunk = data[offset + 4:offset + 8]
        payload = data[offset + 4:end - 4]
        if zlib.crc32(payload) & 0xffffffff != struct.unpack_from(">I", data, end - 4)[0]:
            raise ValueError("Screenshot PNG chunk checksum is invalid")
        if chunk == b"IDAT":
            compressed.extend(data[offset + 8:end - 4])
        if chunk == b"PLTE":
            palette = 0 < length <= 768 and length % 3 == 0
        offset = end
        if chunk == b"IEND":
            ended = length == 0 and offset == len(data)
            break
    if not compressed or not ended or (color == 3 and not palette):
        raise ValueError("Screenshot PNG is incomplete")
    try:
        decoder = zlib.decompressobj()
        decoded = decoder.decompress(compressed, expected + 1)
        if len(decoded) != expected or not decoder.eof or decoder.unused_data or decoder.unconsumed_tail:
            raise ValueError("Screenshot PNG image data is truncated or oversized")
        position = 0
        for rows, size in scanlines:
            for _ in range(rows):
                if decoded[position] > 4:
                    raise ValueError("Screenshot PNG has an invalid scanline filter")
                position += size
    except zlib.error as error:
        raise ValueError("Screenshot PNG image data cannot be decoded") from error
    return width, height


def numeric(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value)


def offset_time(value):
    if not isinstance(value, str) or datetime.fromisoformat(value).utcoffset() is None:
        raise ValueError("Time must include a UTC offset")


def load_spec(path):
    spec = read_json(path)
    if spec.get("schemaVersion") != 1:
        raise ValueError("Unsupported acceptance specification")
    if not isinstance(spec.get("cases"), list) or not spec["cases"] or not isinstance(spec.get("dimensions"), dict) or not spec["dimensions"]:
        raise ValueError("Specification needs cases and scoring dimensions")
    ids = [case["id"] for case in spec["cases"]]
    if len(ids) != len(set(ids)):
        raise ValueError("Duplicate case IDs in specification")
    for dimension in spec["dimensions"].values():
        if not numeric(dimension["weight"]) or dimension["weight"] <= 0 or not numeric(dimension["minimum"]) or not 0 <= dimension["minimum"] <= 4:
            raise ValueError("Invalid dimension weight or minimum")
    if not math.isclose(sum(value["weight"] for value in spec["dimensions"].values()), 100):
        raise ValueError("Dimension weights must sum to 100")
    for case in spec["cases"]:
        if not isinstance(case["id"], str) or not case["id"].strip() or type(case["required"]) is not bool or case["priority"] not in {"P0", "P1", "P2", "P3"}:
            raise ValueError("Invalid case identity, requirement, or priority")
        if case["dimension"] not in spec["dimensions"] or not numeric(case["weight"]) or case["weight"] <= 0:
            raise ValueError("Invalid case dimension or weight")
        if not isinstance(case["evidence"], list) or not case["evidence"] or any(not choices or any(kind not in spec["evidenceKinds"] for kind in choices) for choices in case["evidence"]):
            raise ValueError("Invalid evidence requirement")
    if not numeric(spec["thresholds"]["overall"]) or not 0 <= spec["thresholds"]["overall"] <= 100:
        raise ValueError("Invalid overall threshold")
    for key in ("requireAllRequiredCases", "requireCleanTrackedFiles", "requireNoUntrackedFiles"):
        if key in spec["thresholds"] and type(spec["thresholds"][key]) is not bool:
            raise ValueError("Acceptance gate switches must be booleans")
    return spec


def validate_reference(spec, inventory):
    if inventory.get("schemaVersion") != 1 or not inventory.get("sources"):
        raise ValueError("A generated reference inventory is required")
    required_roles = set(spec.get("requiredReferenceRoles", []))
    actual_roles = {source.get("role") for source in inventory["sources"]}
    if not required_roles.issubset(actual_roles):
        raise ValueError("Reference inventory is missing required authorities: " + ", ".join(sorted(required_roles - actual_roles)))
    expected = spec.get("functionalityReference")
    if expected and not any(source.get("role") == "functionality" and all(source.get(key) == value for key, value in expected.items()) for source in inventory["sources"]):
        raise ValueError("Navidrome functional reference differs from the locked specification")


def initial_run(spec, spec_path, output, reference_index):
    output = Path(output).resolve()
    if output.exists():
        raise ValueError("Run already exists; use a new output path to preserve its evidence")
    inventory = read_json(reference_index)
    validate_reference(spec, inventory)
    output.parent.mkdir(parents=True, exist_ok=True)
    bundle = output.parent / "evidence" / "reference-index.json"
    bundle.parent.mkdir(parents=True, exist_ok=True)
    if bundle.exists() and digest(bundle) != digest(reference_index):
        raise ValueError("Use a new run directory for a different reference inventory")
    if Path(reference_index).resolve() != bundle.resolve():
        shutil.copyfile(reference_index, bundle)
    run = {"schemaVersion": 1, "createdAt": timestamp(), "specSha256": digest(spec_path),
           "code": git_state(), "environment": {"system": platform.system(), "version": platform.version(),
                                                    "python": platform.python_version(), "clientTarget": spec.get("target", {})},
           "referenceIndex": {"path": bundle.relative_to(output.parent).as_posix(), "sha256": digest(bundle)},
           "results": [{"id": case["id"], "status": "not_run", "score": None, "notes": "", "reviewer": "",
                        "measuredAt": None, "evidence": []} for case in spec["cases"]], "defects": []}
    write_json(output, run)
    return run


def copy_evidence(directory, kind, source, viewport=None, scale=None):
    source = Path(source).resolve()
    if not source.is_file():
        raise ValueError(f"Evidence file is missing: {source.name}")
    checksum = digest(source)
    target = Path(directory) / "evidence" / (checksum[:16] + "-" + source.name)
    target.parent.mkdir(parents=True, exist_ok=True)
    if source != target:
        shutil.copyfile(source, target)
    evidence = {"kind": kind, "path": target.relative_to(directory).as_posix(), "sha256": checksum,
                "capturedAt": timestamp()}
    if kind in SCREENSHOTS:
        evidence["pixels"] = list(png_size(target))
        if viewport:
            evidence["viewport"] = {"width": viewport[0], "height": viewport[1], "scale": scale}
    return evidence


def validate_result(case, result, directory, spec):
    errors = []
    if not isinstance(result, dict) or not isinstance(result.get("evidence", []), list):
        return ["Result and evidence must be structured records"]
    status, score = result.get("status"), result.get("score")
    if status not in STATUSES:
        return ["Unknown result status"]
    scored = numeric(score) and 0 <= score <= 4
    if status == "pass" and (not scored or score < 3):
        errors.append("A pass needs a score of 3 to 4")
    if status == "fail" and (not scored or score > 2):
        errors.append("A failure needs a score of 0 to 2")
    if status in {"not_run", "blocked"} and score is not None:
        errors.append("Unverified results must have no score")
    if status != "not_run":
        if any(not isinstance(result.get(key), str) or not result[key].strip() for key in ("notes", "reviewer")):
            errors.append("An observation and reviewer are required")
        try:
            offset_time(result.get("measuredAt"))
        except ValueError:
            errors.append("Observation time must include a UTC offset")
    valid_evidence = []
    for item in result.get("evidence", []):
        try:
            if not isinstance(item, dict):
                raise ValueError("evidence must be a structured record")
            offset_time(item.get("capturedAt"))
            path = safe_bundle_path(directory, item["path"])
            if item["kind"] not in spec["evidenceKinds"] or not path.is_file() or digest(path) != item["sha256"]:
                raise ValueError("missing, changed, or unknown evidence")
            if item["kind"] in SCREENSHOTS:
                if list(png_size(path)) != item.get("pixels"):
                    raise ValueError("screenshot dimensions changed")
                capture = item.get("viewport")
                if not capture or any(not numeric(capture.get(key)) or capture[key] <= 0 for key in ("width", "height", "scale")):
                    raise ValueError("screenshot needs logical viewport and DPI scale")
                pixels = [round(capture[key] * capture["scale"]) for key in ("width", "height")]
                if pixels != item["pixels"]:
                    raise ValueError("screenshot pixels do not match the declared content viewport")
                if case.get("viewport") and capture != case["viewport"]:
                    raise ValueError("screenshot does not match this case's viewport")
            valid_evidence.append(item)
        except (KeyError, TypeError, ValueError, OSError) as error:
            errors.append(f"Invalid evidence: {error}")
    if status == "pass":
        kinds = {item["kind"] for item in valid_evidence}
        for choices in case["evidence"]:
            if not kinds.intersection(choices):
                errors.append("Missing evidence: " + " or ".join(choices))
        for item in valid_evidence:
            if item["kind"] != "comparison_report":
                continue
            try:
                comparison = read_json(safe_bundle_path(directory, item["path"]))
                if comparison.get("schemaVersion") != 1 or comparison.get("referenceKind") != "runtime" or comparison.get("candidateKind") != "runtime" or comparison.get("reviewed") is not True or comparison.get("decision") != "pass":
                    raise ValueError("comparison needs a reviewed runtime reference and a passing decision")
                offset_time(comparison.get("reviewedAt"))
                if any(not isinstance(comparison.get(key), str) or not comparison[key].strip() for key in ("notes", "reviewer")):
                    raise ValueError("comparison needs an observation and named reviewer")
                for role, kind in (("reference", "reference_screenshot"), ("candidate", "client_screenshot")):
                    description = comparison["images"][role]
                    matched = next((entry for entry in valid_evidence if entry["kind"] == kind and entry["sha256"] == description["sha256"]), None)
                    if not matched:
                        raise ValueError("comparison input hash does not match bundled screenshots")
                    if [description["width"], description["height"]] != matched["pixels"]:
                        raise ValueError("comparison input dimensions differ from its screenshot")
                    crop = comparison["crops"][role]
                    if any(type(crop.get(key)) is not int for key in ("x", "y", "width", "height")) or crop["x"] < 0 or crop["y"] < 0 or crop["width"] <= 0 or crop["height"] <= 0 or crop["x"] + crop["width"] > description["width"] or crop["y"] + crop["height"] > description["height"]:
                        raise ValueError("comparison crop is outside its screenshot")
                    if case.get("comparisonScope") == "full_frame" and crop != {"x": 0, "y": 0, "width": description["width"], "height": description["height"]}:
                        raise ValueError("this case requires a full-frame comparison")
            except (KeyError, ValueError, TypeError, OSError) as error:
                errors.append(f"Invalid comparison report: {error}")
    return errors


def validate_defect(defect, directory, spec):
    if not isinstance(defect, dict) or not isinstance(defect.get("id"), str) or not defect["id"].strip():
        return ["Defect needs an ID"]
    if defect.get("priority") not in {"P0", "P1", "P2", "P3"} or defect.get("status") not in {"open", "resolved"}:
        return ["Defect has an unknown priority or status"]
    if defect.get("caseId") not in {case["id"] for case in spec["cases"]}:
        return ["Defect must identify an acceptance case"]
    # Reuse evidence integrity checks; a resolution also needs execution evidence.
    observed = {"status": "pass" if defect["status"] == "resolved" else "blocked",
                "score": 4 if defect["status"] == "resolved" else None,
                "notes": defect.get("notes"), "reviewer": defect.get("reviewer"),
                "measuredAt": defect.get("updatedAt"), "evidence": defect.get("evidence", [])}
    return validate_result({"evidence": [["test_log", "manual_record", "runtime_log", "video"]]}, observed, directory, spec)


def evaluate_run(run, spec, directory, spec_sha256, current_code=None):
    reasons = []
    if not isinstance(run, dict) or not isinstance(run.get("results"), list) or any(not isinstance(item, dict) or "id" not in item for item in run["results"]):
        raise ValueError("Run needs structured case results")
    if run.get("schemaVersion") != 1 or run.get("specSha256") != spec_sha256:
        reasons.append("Run specification is missing or has changed")
    current_code = git_state() if current_code is None else current_code
    if not run.get("code", {}).get("commit") or run["code"]["commit"] != current_code["commit"]:
        reasons.append("Code commit differs from the recorded run")
    if spec["thresholds"]["requireCleanTrackedFiles"] and (not run.get("code", {}).get("trackedClean") or not current_code["trackedClean"]):
        reasons.append("Tracked code was modified during the run")
    if spec["thresholds"].get("requireNoUntrackedFiles") and (run.get("code", {}).get("untrackedFiles") or current_code.get("untrackedFiles")):
        reasons.append("Uncommitted files are present in the acceptance workspace")
    try:
        reference = run["referenceIndex"]
        if digest(safe_bundle_path(directory, reference["path"])) != reference["sha256"]:
            raise ValueError("reference inventory changed")
        validate_reference(spec, read_json(safe_bundle_path(directory, reference["path"])))
    except (KeyError, OSError, ValueError):
        reasons.append("Reference inventory is missing or changed")
    result_list = run.get("results", [])
    result_ids = [item["id"] for item in result_list]
    known_ids = {case["id"] for case in spec["cases"]}
    if len(result_ids) != len(set(result_ids)) or any(item not in known_ids for item in result_ids):
        reasons.append("Duplicate or unknown case results")
    results = {item["id"]: item for item in result_list}
    checked, dimensions = [], {}
    for case in spec["cases"]:
        result = results.get(case["id"], {"status": "not_run", "score": None, "evidence": []})
        errors = validate_result(case, result, directory, spec)
        verified = result.get("status") in {"pass", "fail"} and not errors
        checked.append({"case": case, "result": result, "errors": errors, "verified": verified})
        if case["required"] and spec["thresholds"]["requireAllRequiredCases"] and (result.get("status") != "pass" or errors):
            reasons.append(case["id"] + ": " + ("; ".join(errors) if errors else str(result.get("status"))))
        elif errors:
            reasons.append(case["id"] + ": " + "; ".join(errors))
    raw_ratings = {}
    for key, dimension in spec["dimensions"].items():
        cases = [item for item in checked if item["case"]["required"] and item["case"]["dimension"] == key]
        denominator = sum(item["case"]["weight"] for item in cases)
        total = sum((item["result"]["score"] if item["verified"] else 0) * item["case"]["weight"] for item in cases)
        rating = total / denominator if denominator else 0
        raw_ratings[key] = rating
        dimensions[key] = {"rating": round(rating, 3), "percent": round(rating * 25, 2), "weight": dimension["weight"]}
        if rating < dimension["minimum"]:
            reasons.append(dimension["label"] + " is below its minimum rating")
    overall_raw = sum(raw_ratings[key] * 25 * item["weight"] / 100 for key, item in dimensions.items())
    overall = round(overall_raw, 2)
    if overall_raw < spec["thresholds"]["overall"]:
        reasons.append("Overall score is below the acceptance threshold")
    defects = run.get("defects", [])
    if not isinstance(defects, list):
        raise ValueError("Defects must be a list")
    defect_ids = [defect.get("id") for defect in defects if isinstance(defect, dict)]
    if len(defect_ids) != len(set(defect_ids)):
        reasons.append("Duplicate defect IDs")
    for defect in defects:
        errors = validate_defect(defect, directory, spec)
        if errors:
            reasons.append("Invalid defect: " + "; ".join(errors))
        if not isinstance(defect, dict):
            continue
        if defect.get("priority") in spec["thresholds"]["blockingPriorities"] and defect.get("status") != "resolved":
            reasons.append("Unresolved blocking defect: " + str(defect.get("id", "unnamed")))
    return {"accepted": not reasons, "evaluatedAt": timestamp(), "overall": overall,
            "dimensions": dimensions, "verified": sum(item["verified"] for item in checked if item["case"]["required"]),
            "passed": sum(item["verified"] and item["result"].get("status") == "pass" for item in checked if item["case"]["required"]),
            "required": sum(case["required"] for case in spec["cases"]), "reasons": reasons, "checks": checked}


def render_report(run, spec, evaluation):
    escape = lambda value: html.escape(str(value))
    rows = []
    for item in evaluation["checks"]:
        case, result = item["case"], item["result"]
        links = " ".join(f'<a href="{quote(evidence["path"])}">{escape(evidence["kind"])}</a>' for evidence in result.get("evidence", [])
                         if isinstance(evidence, dict) and isinstance(evidence.get("path"), str) and "kind" in evidence)
        notes = str(result.get("notes", "")) + (" Evidence errors: " + "; ".join(item["errors"]) if item["errors"] else "")
        criteria = "".join(f"<li>{escape(step)}</li>" for step in case["steps"])
        expected = "".join(f"<li>{escape(step)}</li>" for step in case["expected"])
        rows.append(f'<tr data-status="{escape(result.get("status"))}"><td>{escape(case["id"])}<br>{escape(case["title"])}'
                    f'<details><summary>Procedure and expected result</summary><ol>{criteria}</ol><ul>{expected}</ul></details></td>'
                    f'<td>{escape(case["priority"])} {"required" if case["required"] else "roadmap"}</td>'
                    f'<td>{escape(result.get("status"))}<br>{escape(result.get("score") if item["verified"] else "unverified")}</td>'
                    f'<td>{escape(notes)}<br><small>{escape(result.get("reviewer", ""))} · {escape(result.get("measuredAt", ""))}</small><br>{links}</td></tr>')
    ratings = "".join(f'<li>{escape(spec["dimensions"][key]["label"])}: {value["percent"]}% (weight {value["weight"]}%)</li>'
                      for key, value in evaluation["dimensions"].items())
    reasons = "".join(f"<li>{escape(reason)}</li>" for reason in evaluation["reasons"])
    defects = "".join(f'<li>{escape(defect.get("id"))} ({escape(defect.get("priority"))}, {escape(defect.get("status"))}): {escape(defect.get("notes", ""))}</li>'
                      for defect in run.get("defects", []) if isinstance(defect, dict)) or "<li>No recorded defects.</li>"
    reference_path = quote(run.get("referenceIndex", {}).get("path", ""))
    return f'''<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
<title>{escape(spec["title"])}</title><style>body{{font:16px system-ui;background:#141414;color:#eee;margin:32px;line-height:1.5}}a{{color:#ff75ac}}table{{border-collapse:collapse;width:100%}}th,td{{padding:14px;text-align:left;border-bottom:1px solid #444;vertical-align:top}}td:first-child{{width:25%}}select{{font:inherit;padding:8px}}.gate{{font-size:28px;color:{"#91dca5" if evaluation["accepted"] else "#ffb66e"}}}details{{margin:24px 0}}</style>
<h1>{escape(spec["title"])}</h1><p class="gate">{"Accepted" if evaluation["accepted"] else "Not accepted"}: {evaluation["overall"]}%</p>
<p>{evaluation["passed"]}/{evaluation["required"]} required cases passed; {evaluation["verified"]} verified. Missing and blocked evidence remains unverified.</p>
<p>Commit: {escape(run.get("code", {}).get("commit"))}<br>Evaluated: {escape(evaluation["evaluatedAt"])}</p><ul>{ratings}</ul>
<p>{escape(spec.get("scope", ""))}<br>Threshold: {spec["thresholds"]["overall"]}%; each dimension needs its stated minimum; every required case must pass.</p>
<p><a href="{reference_path}">Reference inventory</a></p><details><summary>Recorded defects</summary><ul>{defects}</ul></details>
<details open><summary>Acceptance gaps</summary><ul>{reasons}</ul></details>
<label>Cases <select id="filter"><option value="all">All</option><option>not_run</option><option>blocked</option><option>fail</option><option>pass</option></select></label>
<table><thead><tr><th>Case</th><th>Priority</th><th>Result / 4</th><th>Observation and evidence</th></tr></thead><tbody>{"".join(rows)}</tbody></table>
<script>document.querySelector('#filter').addEventListener('change',e=>document.querySelectorAll('tr[data-status]').forEach(row=>row.hidden=e.target.value!=='all'&&row.dataset.status!==e.target.value));</script></html>'''


def main():
    parser = InputParser(description=__doc__)
    parser.add_argument("--spec", type=Path, default=DEFAULT_SPEC)
    commands = parser.add_subparsers(dest="command", required=True)
    init = commands.add_parser("init")
    init.add_argument("--output", type=Path, required=True)
    init.add_argument("--reference-index", type=Path, default=ROOT / ".tmp/mixradio-audit/index.json")
    record = commands.add_parser("record")
    record.add_argument("--run", type=Path, required=True)
    record.add_argument("--case", required=True)
    record.add_argument("--status", choices=sorted(STATUSES), required=True)
    record.add_argument("--score", type=float)
    record.add_argument("--notes", required=True)
    record.add_argument("--reviewer", required=True)
    record.add_argument("--evidence", action="append", default=[], metavar="KIND=PATH")
    record.add_argument("--viewport", help="Logical app-content WIDTHxHEIGHT, excluding window chrome")
    record.add_argument("--scale", type=float)
    defect = commands.add_parser("defect")
    defect.add_argument("--run", type=Path, required=True)
    defect.add_argument("--id", required=True)
    defect.add_argument("--case", dest="case_id", required=True)
    defect.add_argument("--priority", choices=["P0", "P1", "P2", "P3"], required=True)
    defect.add_argument("--status", choices=["open", "resolved"], required=True)
    defect.add_argument("--notes", required=True)
    defect.add_argument("--reviewer", required=True)
    defect.add_argument("--evidence", action="append", default=[], metavar="KIND=PATH")
    evaluate = commands.add_parser("evaluate")
    evaluate.add_argument("--run", type=Path, required=True)
    args = parser.parse_args()
    spec = load_spec(args.spec)
    if args.command == "init":
        initial_run(spec, args.spec, args.output, args.reference_index)
        print("Acceptance run: " + str(args.output.resolve()))
        return 0
    run = read_json(args.run)
    directory = args.run.resolve().parent
    if run.get("specSha256") != digest(args.spec):
        raise ValueError("Specification changed; start a new run")
    if args.command == "defect":
        item = {"id": args.id, "caseId": args.case_id, "priority": args.priority, "status": args.status,
                "notes": args.notes, "reviewer": args.reviewer, "updatedAt": timestamp(), "evidence": []}
        for value in args.evidence:
            kind, separator, source = value.partition("=")
            if not separator or kind not in spec["evidenceKinds"]:
                raise ValueError("Evidence must be KIND=PATH with a known kind")
            item["evidence"].append(copy_evidence(directory, kind, source))
        errors = validate_defect(item, directory, spec)
        if errors:
            raise ValueError("; ".join(errors))
        run["defects"] = [existing for existing in run.get("defects", []) if existing["id"] != args.id] + [item]
        write_json(args.run, run)
        print(args.id + ": " + args.status)
        return 0
    if args.command == "record":
        case = next((case for case in spec["cases"] if case["id"] == args.case), None)
        if case is None:
            raise ValueError("Case is not in this specification")
        result = next(item for item in run["results"] if item["id"] == args.case)
        result.update(status=args.status, score=args.score, notes=args.notes, reviewer=args.reviewer, measuredAt=timestamp())
        viewport = tuple(map(int, args.viewport.lower().split("x"))) if args.viewport else None
        if viewport and (len(viewport) != 2 or min(viewport) <= 0 or args.scale is None or not math.isfinite(args.scale) or args.scale <= 0):
            raise ValueError("Supply a positive logical viewport and DPI scale")
        for value in args.evidence:
            kind, separator, source = value.partition("=")
            if not separator or kind not in spec["evidenceKinds"]:
                raise ValueError("Evidence must be KIND=PATH with a known kind")
            item = copy_evidence(directory, kind, source, viewport, args.scale)
            if not any(existing["kind"] == kind and existing["sha256"] == item["sha256"] for existing in result["evidence"]):
                result["evidence"].append(item)
        errors = validate_result(case, result, directory, spec)
        if errors:
            raise ValueError("; ".join(errors))
        write_json(args.run, run)
        print(args.case + ": " + args.status)
        return 0
    evaluation = evaluate_run(run, spec, directory, digest(args.spec))
    write_json(directory / "evaluation.json", {key: value for key, value in evaluation.items() if key != "checks"})
    (directory / "report.html").write_text(render_report(run, spec, evaluation), encoding="utf-8")
    print(json.dumps({key: value for key, value in evaluation.items() if key not in {"checks", "reasons"}}, indent=2))
    print("Report: " + str(directory / "report.html"))
    return 0 if evaluation["accepted"] else 2


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ValueError, KeyError, TypeError, OSError, StopIteration) as error:
        print("Invalid acceptance input: " + str(error), file=sys.stderr)
        sys.exit(1)
