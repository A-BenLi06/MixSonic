"""Bundle fresh source/server checks and native acceptance gaps for the current commit.

Run verify.ps1 first. This report deliberately cannot promote source or simulated
events into native interaction or visual passes. Use quality.py to record those
separately after observing them. Exit 2 means a valid report that is not accepted.
"""

import argparse
import hashlib
import json
from pathlib import Path
import sys
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools/mixradio"))
import quality
import inspect_source
from xbf_v1 import decode


def revalidate(inventory, phone_package, desktop_package):
    functional = next(source for source in inventory["sources"] if source.get("role") == "functionality")
    source_root = ROOT / ".tmp/navidrome"
    lock_path = ROOT / "tools/navidrome/source-lock.json"
    if (inspect_source.git(source_root, "rev-parse", "HEAD") != functional["commit"]
            or inspect_source.git(source_root, "remote", "get-url", "origin") != functional["repository"]
            or inspect_source.git(source_root, "status", "--porcelain")
            or quality.digest(lock_path) != inventory["sourceLockSha256"]):
        raise ValueError("The locked Navidrome checkout or inventory changed")
    for item in functional["files"]:
        path = quality.safe_bundle_path(source_root, item["path"])
        if quality.digest(path) != item["sha256"]:
            raise ValueError("Navidrome source bytes changed: " + item["path"])
    phone = next(source for source in inventory["sources"] if source.get("kind") == "windows-phone-xap")
    desktop = next(source for source in inventory["sources"] if source.get("kind") == "windows-store-installed")
    if quality.digest(phone_package) != phone["sha256"]:
        raise ValueError("The phone design package differs from its inventory")
    with zipfile.ZipFile(phone_package) as archive:
        members = {item.filename.replace("\\", "/"): item for item in archive.infolist() if not item.is_dir()}
        for item in phone["files"]:
            if hashlib.sha256(archive.read(members[item["path"]])).hexdigest() != item["sha256"]:
                raise ValueError("Phone design bytes changed: " + item["path"])
    for item in desktop["files"]:
        path = quality.safe_bundle_path(desktop_package, item["path"])
        if quality.digest(path) != item["sha256"]:
            raise ValueError("Desktop design bytes changed: " + item["path"])
    design_root = ROOT / ".tmp/mixradio-audit"
    assemblies = 0
    for source, platform in ((phone, "wp"), (desktop, "pc")):
        for assembly in source["assemblies"]:
            recovered = design_root / platform / "decompiled" / Path(assembly["assembly"]).stem
            if (not assembly["decompiled"] or (recovered / "assembly.sha256").read_text().strip() != assembly["sha256"]
                    or len(list(recovered.rglob("*.cs"))) != assembly["sourceFiles"]
                    or len(list(recovered.rglob("*.xaml"))) != assembly["xamlFiles"]):
                raise ValueError("Design decompilation provenance changed: " + assembly["assembly"])
            assemblies += 1
    for item in desktop["xbf"]:
        decoded = decode(quality.safe_bundle_path(desktop_package, item["path"]))
        if decoded["coverage"] != "decoded diagnostic object/property tree" or decoded["version"] != "1.0" or decoded["nodeCount"] <= 0:
            raise ValueError("The original design XBF could not be decoded: " + item["path"])
    if not desktop["priDumped"] or not (design_root / "pc/resources.xml").is_file():
        raise ValueError("The original desktop design resource inventory is incomplete")
    return {"navidromeSourceFiles": len(functional["files"]), "registeredEndpoints": len(functional["registrations"]),
            "phoneDesignFiles": len(phone["files"]), "desktopDesignFiles": len(desktop["files"]),
            "decompiledDesignAssemblies": assemblies, "decodedDesktopXbfTrees": len(desktop["xbf"])}


def main():
    parser = quality.InputParser(description=__doc__)
    parser.add_argument("--verification", type=Path, default=ROOT / ".tmp/navidrome-audit/verification.json")
    parser.add_argument("--native-record", type=Path, default=ROOT / ".tmp/navidrome-audit/native-capture.json")
    parser.add_argument("--phone-package", type=Path, required=True)
    parser.add_argument("--desktop-package", type=Path, required=True)
    parser.add_argument("--output", type=Path, help="New run directory; existing runs are preserved")
    args = parser.parse_args()
    code = quality.git_state()
    if not code["trackedClean"] or code["untrackedFiles"]:
        raise ValueError("The final acceptance run requires committed, clean sources")
    verification = quality.read_json(args.verification)
    if (verification.get("schemaVersion") != 1 or verification.get("passed") is not True
            or verification.get("code") != code or verification.get("codeAfter") != code):
        raise ValueError("Verification is not bound to this clean commit")
    checks = {check["name"]: check for check in verification["checks"]}
    required_checks = {"build", "protocol", "audio", "jukebox", "services", "integration", "playback", "serializer", "resources", "reference"}
    if not required_checks.issubset(checks) or len(checks) != len(verification["checks"]):
        raise ValueError("Required verification steps are missing or duplicated")
    for check in checks.values():
        if check["exitCode"] != 0 or quality.digest(check["log"]) != check["sha256"]:
            raise ValueError("A verification log failed or changed: " + check["name"])
        quality.offset_time(check["startedAt"])
        quality.offset_time(check["completedAt"])
    native = quality.read_json(args.native_record)
    if (native.get("schemaVersion") != 1 or native.get("code", {}).get("commit") != code["commit"]
            or native.get("nativeVerified") is not False or not native.get("limitation")):
        raise ValueError("Supply the current native observation and its explicit verification limit")
    quality.offset_time(native["recordedAt"])
    spec_path = ROOT / "tools/navidrome/acceptance-spec.json"
    spec = quality.load_spec(spec_path)
    inventory_path = ROOT / ".tmp/navidrome-audit/reference-index.json"
    inventory = quality.read_json(inventory_path)
    quality.validate_reference(spec, inventory)
    provenance = revalidate(inventory, args.phone_package.resolve(), args.desktop_package.resolve())
    directory = args.output or ROOT / ".tmp/navidrome-acceptance" / (quality.timestamp()[:10] + "-" + code["commit"][:7] + "-desktop")
    directory = directory.resolve()
    run_path = directory / "run.json"
    run = quality.initial_run(spec, spec_path, run_path, inventory_path)
    observations = {
        "REF-PC": "The original installed desktop package file hashes match the MixRadio design inventory.",
        "REF-WP": "The supplied phone archive and every original member hash match the separate MixRadio design inventory.",
        "BUILD-PC": "A fresh MSBuild 14 Windows 8.1 Store Debug x86 build and workspace deployment completed successfully.",
        "XML-MODELS": "Production XML model and serializer regressions passed.",
        "QUEUE-CONTEXT": "Production queue regressions verify selected indices, full context replacement and explicit append.",
        "QUEUE-REMOVE": "Production queue regressions verify removal, reordering, clear and current-item transitions.",
        "QUEUE-SHUFFLE": "Production queue regressions verify complete shuffle cycles and previous-track history.",
        "QUEUE-REPEAT": "Production queue regressions verify Off/All/One, natural completion and manual skip.",
        "HISTORY-MODEL": "Production model/service regressions verify bounded, persisted and account-scoped listening records.",
        "FAV-PROTOCOL": "Protocol and real-server integration verify confirmed song/album/artist star and unstar mutations.",
        "ND-SOURCE": "The clean Navidrome reference commit, remote and source bytes match the lock; MixRadio retains design authority only.",
        "ND-PROTOCOL": "Protocol/model, simulated audio, controlled jukebox, production service and real-server integration checks passed. Native interaction/decoding remains unverified.",
    }
    note_path = directory / "source-observations.json"
    quality.write_json(note_path, {"schemaVersion": 1, "recordedAt": quality.timestamp(), "code": code,
                                 "scope": spec["scope"], "referenceRevalidation": provenance,
                                 "observations": observations, "verification": verification,
                                 "nativeLimit": native["limitation"]})
    source_note = quality.copy_evidence(directory, "source_note", note_path)
    native_evidence = quality.copy_evidence(directory, "runtime_log", args.native_record)
    test_evidence = {name: quality.copy_evidence(directory, "test_log", check["log"]) for name, check in checks.items()}
    reference_evidence = quality.copy_evidence(directory, "reference_inventory", inventory_path)
    for case, result in zip(spec["cases"], run["results"]):
        case_id = case["id"]
        if case_id in observations:
            result.update(status="pass", score=4, notes=observations[case_id], reviewer="Automated source/server verification", measuredAt=quality.timestamp())
            result["evidence"] = [source_note]
            if case_id in {"REF-PC", "REF-WP"}:
                result["evidence"].append(reference_evidence)
            elif case_id == "ND-PROTOCOL":
                result["evidence"].extend(test_evidence[name] for name in ("protocol", "audio", "jukebox", "services", "integration", "resources"))
            else:
                check = "build" if case_id == "BUILD-PC" else "serializer" if case_id == "XML-MODELS" else "reference" if case_id == "ND-SOURCE" else "playback"
                result["evidence"].append(test_evidence[check])
                if case_id == "FAV-PROTOCOL":
                    result["evidence"].extend(test_evidence[name] for name in ("protocol", "integration"))
        elif case["required"]:
            result.update(status="blocked", score=None, notes=native["limitation"] + " Source/server checks do not establish this native case.",
                          reviewer="Automated capture observer", measuredAt=quality.timestamp())
            result["evidence"] = [native_evidence, source_note]
        errors = quality.validate_result(case, result, directory, spec)
        if errors:
            raise ValueError(case_id + ": " + "; ".join(errors))
    run["defects"] = [{"id": "ENV-NATIVE-CAPTURE", "caseId": "PLAYER-LIVE", "priority": "P1", "status": "open",
                       "notes": native["limitation"], "reviewer": "Automated capture observer", "updatedAt": quality.timestamp(), "evidence": [native_evidence]}]
    run["verificationRecord"] = quality.copy_evidence(directory, "source_note", args.verification)
    quality.write_json(run_path, run)
    evaluation = quality.evaluate_run(run, spec, directory, quality.digest(spec_path))
    if any(check["errors"] for check in evaluation["checks"]):
        raise ValueError("The acceptance run contains invalid evidence")
    quality.write_json(directory / "evaluation.json", {key: value for key, value in evaluation.items() if key != "checks"})
    (directory / "report.html").write_text(quality.render_report(run, spec, evaluation), encoding="utf-8")
    print(json.dumps({key: evaluation[key] for key in ("accepted", "overall", "verified", "passed", "required")}, indent=2))
    print("Report: " + str(directory / "report.html"))
    return 0 if evaluation["accepted"] else 2


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (ValueError, KeyError, OSError, StopIteration, zipfile.BadZipFile) as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
