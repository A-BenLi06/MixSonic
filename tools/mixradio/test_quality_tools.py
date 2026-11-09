"""Tool self-tests use synthetic pixels and a separate, minimal specification.

Simulated review metadata exercises validation rules; it is never an actual
MixRadio capture, client acceptance result, or reference screenshot.
"""

from copy import deepcopy
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest
import zlib

import compare
import quality


def synthetic_png(path, width=2, height=2, rgba=(40, 80, 120, 255), raw=None):
    def chunk(kind, content):
        return struct.pack(">I", len(content)) + kind + content + struct.pack(">I", zlib.crc32(kind + content) & 0xffffffff)
    header = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    scanlines = raw if raw is not None else (b"\0" + bytes(rgba) * width) * height
    Path(path).write_bytes(b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(scanlines)) + chunk(b"IEND", b""))


class QualityToolTests(unittest.TestCase):
    def setUp(self):
        scratch = quality.ROOT / ".tmp/mixradio-tools-tests"
        scratch.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(prefix="self-test-", dir=scratch)
        self.directory = Path(self.temporary.name).resolve()
        self.assertTrue(self.directory.is_relative_to(scratch.resolve()))
        self.addCleanup(self.temporary.cleanup)
        self.spec_path = self.directory / "synthetic-tool-spec.json"
        self.spec = {
            "schemaVersion": 1, "title": "Tool self-test specification", "scope": "Synthetic tool fixtures only.",
            "dimensions": {"correctness": {"label": "Tool fixture", "weight": 100, "minimum": 3}},
            "thresholds": {"overall": 85, "requireAllRequiredCases": True, "requireCleanTrackedFiles": True,
                           "requireNoUntrackedFiles": True, "blockingPriorities": ["P0", "P1"]},
            "evidenceKinds": ["test_log", "source_note", "client_screenshot", "reference_screenshot", "comparison_report", "manual_record", "runtime_log", "video"],
            "cases": [{"id": "TOOL-FIXTURE", "title": "Synthetic fixture", "dimension": "correctness", "priority": "P0", "required": True,
                       "weight": 1, "evidence": [["test_log"]], "steps": ["Exercise the tool."], "expected": ["Validation follows the specification."]}]
        }
        quality.write_json(self.spec_path, self.spec)
        reference = self.directory / "synthetic-tool-index.json"
        quality.write_json(reference, {"schemaVersion": 1, "sources": [{"kind": "synthetic-tool-fixture", "sha256": "0" * 64}]})
        self.run_path = self.directory / "run/run.json"
        self.run = quality.initial_run(self.spec, self.spec_path, self.run_path, reference)
        self.clean_code = {"commit": "tool-self-test-revision", "trackedClean": True, "untrackedFiles": []}
        self.run["code"] = deepcopy(self.clean_code)
        self.log = self.directory / "synthetic-tool-test.log"
        self.log.write_text("Synthetic evidence for tool self-tests; no app behavior is certified.\n", encoding="utf-8")

    def passed_result(self, score=4):
        return {"id": "TOOL-FIXTURE", "status": "pass", "score": score, "notes": "Tool fixture observation.", "reviewer": "unit-test",
                "measuredAt": quality.timestamp(), "evidence": [quality.copy_evidence(self.run_path.parent, "test_log", self.log)]}

    def evaluate(self):
        return quality.evaluate_run(self.run, self.spec, self.run_path.parent, quality.digest(self.spec_path), self.clean_code)

    def visual_fixture(self):
        case = {**self.spec["cases"][0], "evidence": [["reference_screenshot"], ["client_screenshot"], ["comparison_report"]],
                "viewport": {"width": 2, "height": 2, "scale": 1}, "comparisonScope": "full_frame"}
        result = self.passed_result()
        result["evidence"] = []
        images = {}
        for role, kind in (("reference", "reference_screenshot"), ("candidate", "client_screenshot")):
            path = self.directory / ("synthetic-" + role + ".png")
            synthetic_png(path)
            result["evidence"].append(quality.copy_evidence(self.run_path.parent, kind, path, (2, 2), 1))
            images[role] = compare.describe_image(path)
        # These are simulated fields for unit testing the validator, not captures.
        review = {"schemaVersion": 1, "title": "Simulated review validator input: synthetic pixels", "referenceKind": "runtime", "candidateKind": "runtime",
                  "images": images, "reviewed": True, "reviewedAt": quality.timestamp(), "reviewer": "unit-test",
                  "decision": "pass", "notes": "Unit-test metadata fixture only.",
                  "crops": {role: {"x": 0, "y": 0, "width": 2, "height": 2} for role in images}}
        review_path = self.directory / "simulated-review.json"
        quality.write_json(review_path, review)
        result["evidence"].append(quality.copy_evidence(self.run_path.parent, "comparison_report", review_path))
        return case, result, review, review_path

    def replace_review(self, result, path, review):
        quality.write_json(path, review)
        result["evidence"] = [item for item in result["evidence"] if item["kind"] != "comparison_report"]
        result["evidence"].append(quality.copy_evidence(self.run_path.parent, "comparison_report", path))

    def test_functional_reference_cannot_be_replaced_by_design_inventory(self):
        spec = {**self.spec, "requiredReferenceRoles": ["functionality", "design"],
                "functionalityReference": {"repository": "fixture-repository", "commit": "fixture-revision"}}
        design = {"schemaVersion": 1, "sources": [{"role": "design"}]}
        with self.assertRaisesRegex(ValueError, "authorities"):
            quality.validate_reference(spec, design)
        wrong = {"schemaVersion": 1, "sources": [{"role": "design"},
                 {"role": "functionality", "repository": "fixture-repository", "commit": "other-revision"}]}
        with self.assertRaisesRegex(ValueError, "locked"):
            quality.validate_reference(spec, wrong)
        wrong["sources"][1]["commit"] = "fixture-revision"
        quality.validate_reference(spec, wrong)

    def test_navidrome_specification_promotes_complete_client_functions(self):
        spec = quality.load_spec(quality.ROOT / "tools/navidrome/acceptance-spec.json")
        self.assertEqual({"functionality"}, set(spec["requiredReferenceRoles"]))
        self.assertEqual({"protocol", "service", "server", "native"}, set(spec["dimensions"]))
        self.assertTrue(all(case["required"] for case in spec["cases"]))
        for feature in ("TRANSPORT", "DOWNLOADS", "RELATED", "LYRICS", "SERVER-QUEUE", "PLAYLISTS",
                        "API-KEY", "TRANSCODING", "AVATAR", "JUKEBOX", "SONIC", "SONIC-PATH"):
            case_id = "ND-" + feature + "-NATIVE"
            self.assertIn(case_id, {case["id"] for case in spec["cases"]})
        for case in spec["cases"]:
            if case["verificationLevel"] == "native":
                self.assertTrue(case["prerequisites"])
                self.assertNotIn("automatedProofs", case)
                self.assertIn(["manual_record"], case["evidence"])
                self.assertIn(["runtime_log"], case["evidence"])

    def test_cli_check_only_preserves_acceptance_artifacts(self):
        report_path = self.run_path.parent / "report.html"
        evaluation_path = self.run_path.parent / "evaluation.json"
        report_path.write_text("Preserved functional report.", encoding="utf-8")
        quality.write_json(evaluation_path, {"preserved": "existing evaluation"})
        before = {path: path.read_bytes() for path in (report_path, evaluation_path, self.run_path)}
        result = subprocess.run([sys.executable, str(quality.ROOT / "tools/mixradio/quality.py"),
                                 "--spec", str(self.spec_path), "evaluate", "--run", str(self.run_path),
                                 "--check-only"], capture_output=True, text=True)
        self.assertEqual(2, result.returncode, result.stderr)
        self.assertIn('"accepted": false', result.stdout)
        for path, content in before.items():
            self.assertEqual(content, path.read_bytes())

    def test_default_specification_has_required_and_roadmap_cases(self):
        spec = quality.load_spec(quality.DEFAULT_SPEC)
        self.assertEqual(30, sum(case["required"] for case in spec["cases"]))
        self.assertEqual(3, sum(not case["required"] for case in spec["cases"]))

    def test_unrun_and_blocked_never_reduce_denominator(self):
        for status in ("not_run", "blocked"):
            result = self.passed_result()
            result.update(status=status, score=None)
            self.run["results"] = [result]
            evaluation = self.evaluate()
            self.assertFalse(evaluation["accepted"])
            self.assertEqual(0, evaluation["overall"])
            self.assertEqual(1, evaluation["required"])
            self.assertEqual(0, evaluation["passed"])

    def test_valid_static_tool_run_and_score_threshold(self):
        for score, accepted in ((4, True), (3, False), (3.4, True), (3.399999, False)):
            self.run["results"] = [self.passed_result(score)]
            self.assertEqual(accepted, self.evaluate()["accepted"])

    def test_invalid_scores_and_failure_anchors(self):
        for score in (True, -1, 4.5, float("nan"), float("inf"), None, 2):
            with self.subTest(score=score):
                result = self.passed_result(score)
                self.assertTrue(quality.validate_result(self.spec["cases"][0], result, self.run_path.parent, self.spec))
        result = self.passed_result(3)
        result["status"] = "fail"
        self.assertTrue(quality.validate_result(self.spec["cases"][0], result, self.run_path.parent, self.spec))
        result.update(status="blocked", score=0)
        self.assertTrue(quality.validate_result(self.spec["cases"][0], result, self.run_path.parent, self.spec))

    def test_missing_or_changed_evidence_is_rejected(self):
        result = self.passed_result()
        self.run["results"] = [result]
        bundled = quality.safe_bundle_path(self.run_path.parent, result["evidence"][0]["path"])
        bundled.write_text("Tampered tool fixture", encoding="utf-8")
        self.assertFalse(self.evaluate()["accepted"])
        bundled.unlink()
        self.assertFalse(self.evaluate()["accepted"])
        result["evidence"] = []
        self.assertFalse(self.evaluate()["accepted"])

    def test_reviewers_and_offset_timestamps_are_required(self):
        for key, value in (("reviewer", ""), ("notes", ""), ("measuredAt", "2026-11-09T01:00:00")):
            result = self.passed_result()
            result[key] = value
            self.assertTrue(quality.validate_result(self.spec["cases"][0], result, self.run_path.parent, self.spec))
        result = self.passed_result()
        result["evidence"][0].pop("capturedAt")
        self.assertTrue(quality.validate_result(self.spec["cases"][0], result, self.run_path.parent, self.spec))

    def test_evidence_paths_cannot_escape_bundle(self):
        for path in ("../outside.txt", "../../outside.txt", str(self.log.resolve())):
            with self.subTest(path=path), self.assertRaises(ValueError):
                quality.safe_bundle_path(self.run_path.parent, path)

    def test_png_crc_truncation_deflate_filters_and_bounds(self):
        path = self.directory / "synthetic.png"
        synthetic_png(path)
        self.assertEqual((2, 2), quality.png_size(path))
        data = path.read_bytes()
        for invalid in (data[:-1], data[:40], data[:-1] + bytes([data[-1] ^ 1])):
            path.write_bytes(invalid)
            with self.assertRaises(ValueError):
                quality.png_size(path)
        synthetic_png(path, raw=b"\0")
        with self.assertRaises(ValueError):
            quality.png_size(path)
        synthetic_png(path, raw=(b"\xff" + bytes(8)) * 2)
        with self.assertRaises(ValueError):
            quality.png_size(path)
        synthetic_png(path, 100000, 100000, raw=b"\0")
        with self.assertRaises(ValueError):
            quality.png_size(path)

    def test_screenshot_viewport_and_scale_are_verified(self):
        case, result, _, _ = self.visual_fixture()
        self.assertEqual([], quality.validate_result(case, result, self.run_path.parent, self.spec))
        for viewport in (None, {"width": 2, "height": 2, "scale": True}, {"width": 2, "height": 2, "scale": 1.5},
                         {"width": -2, "height": 2, "scale": 1}, {"width": 2, "height": 2, "scale": float("nan")}):
            changed = deepcopy(result)
            changed["evidence"][0]["viewport"] = viewport
            self.assertTrue(quality.validate_result(case, changed, self.run_path.parent, self.spec))

    def test_synthetic_mockup_unreviewed_and_missing_review_fields_fail_visual_gate(self):
        case, result, review, path = self.visual_fixture()
        for key, value in (("referenceKind", "synthetic"), ("candidateKind", "mockup"), ("reviewed", False), ("reviewed", "true"),
                           ("decision", "blocked"), ("reviewer", ""), ("notes", ""), ("reviewedAt", "2026-11-09"), ("schemaVersion", 2)):
            changed = deepcopy(review)
            changed[key] = value
            self.replace_review(result, path, changed)
            self.assertTrue(quality.validate_result(case, result, self.run_path.parent, self.spec))

    def test_comparison_hash_dimensions_and_full_frame_crops_are_verified(self):
        case, result, review, path = self.visual_fixture()
        for mutation in ("hash", "dimensions", "outside", "partial"):
            changed = deepcopy(review)
            if mutation == "hash": changed["images"]["reference"]["sha256"] = "0" * 64
            if mutation == "dimensions": changed["images"]["reference"]["width"] = 3
            if mutation == "outside": changed["crops"]["reference"]["x"] = 2
            if mutation == "partial": changed["crops"]["reference"]["width"] = 1
            self.replace_review(result, path, changed)
            self.assertTrue(quality.validate_result(case, result, self.run_path.parent, self.spec))

    def test_code_commit_dirty_tracked_and_untracked_files_block_acceptance(self):
        self.run["results"] = [self.passed_result()]
        for key, value in (("commit", "different-revision"), ("trackedClean", False), ("untrackedFiles", ["Client/new.cs"])):
            original = deepcopy(self.clean_code)
            self.clean_code[key] = value
            self.assertFalse(self.evaluate()["accepted"])
            self.clean_code = original

    def test_reference_inventory_hash_is_verified(self):
        self.run["results"] = [self.passed_result()]
        path = quality.safe_bundle_path(self.run_path.parent, self.run["referenceIndex"]["path"])
        path.write_text("Changed reference fixture", encoding="utf-8")
        self.assertFalse(self.evaluate()["accepted"])

    def test_duplicate_unknown_missing_and_invalid_optional_case_results(self):
        result = self.passed_result()
        for values in ([result, result], [{**result, "id": "UNKNOWN"}], []):
            self.run["results"] = values
            self.assertFalse(self.evaluate()["accepted"])
        self.spec["cases"].append({**self.spec["cases"][0], "id": "OPTIONAL", "required": False})
        self.run["results"] = [result, {**result, "id": "OPTIONAL", "score": 99}]
        self.assertFalse(self.evaluate()["accepted"])

    def test_blocking_defects_and_resolution_evidence(self):
        self.run["results"] = [self.passed_result()]
        defect = {"id": "TOOL-DEFECT", "caseId": "TOOL-FIXTURE", "priority": "P1", "status": "open", "notes": "Tool fixture defect.",
                  "reviewer": "unit-test", "updatedAt": quality.timestamp(), "evidence": []}
        self.run["defects"] = [defect]
        self.assertFalse(self.evaluate()["accepted"])
        defect["priority"] = "P2"
        self.assertTrue(self.evaluate()["accepted"])
        defect.update(priority="P1", status="resolved")
        self.assertFalse(self.evaluate()["accepted"])
        defect["evidence"] = [quality.copy_evidence(self.run_path.parent, "test_log", self.log)]
        self.assertTrue(self.evaluate()["accepted"])
        self.run["defects"].append(deepcopy(defect))
        self.assertFalse(self.evaluate()["accepted"])

    def test_specification_rejects_duplicates_and_invalid_weights(self):
        for mutation in ("duplicates", "weight", "sum", "minimum", "required", "evidence", "threshold"):
            invalid = deepcopy(self.spec)
            if mutation == "duplicates": invalid["cases"].append(deepcopy(invalid["cases"][0]))
            if mutation == "weight": invalid["cases"][0]["weight"] = -1
            if mutation == "sum": invalid["dimensions"]["correctness"]["weight"] = 99
            if mutation == "minimum": invalid["dimensions"]["correctness"]["minimum"] = float("nan")
            if mutation == "required": invalid["cases"][0]["required"] = "true"
            if mutation == "evidence": invalid["cases"][0]["evidence"] = []
            if mutation == "threshold": invalid["thresholds"]["overall"] = 101
            quality.write_json(self.spec_path, invalid)
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                quality.load_spec(self.spec_path)

    def test_report_escapes_observations_and_includes_procedures(self):
        result = self.passed_result()
        result["notes"] = '<script>alert("tool-test")</script>'
        self.run["results"] = [result]
        report = quality.render_report(self.run, self.spec, self.evaluate())
        self.assertNotIn('<script>alert("tool-test")', report)
        self.assertIn("&lt;script&gt;", report)
        self.assertIn("Procedure and expected result", report)
        self.assertIn("unit-test", report)

    def test_comparator_requires_provenance_preserves_review_and_escapes_script_payload(self):
        image = self.directory / "synthetic-input.png"
        synthetic_png(image)
        output = self.directory / "synthetic-comparator.html"
        metadata = compare.create_comparison(image, output, "synthetic", title="Tool test </script><script>alert(1)</script>")
        self.assertEqual("missing", metadata["referenceKind"])
        self.assertFalse(metadata["reviewed"])
        self.assertNotIn("__PAYLOAD__", output.read_text(encoding="utf-8"))
        self.assertIn("\\u003c/script", output.read_text(encoding="utf-8"))
        with self.assertRaises(ValueError):
            compare.create_comparison(image, output, "synthetic")
        with self.assertRaises(ValueError):
            compare.create_comparison(image, self.directory / "invalid.html", "unknown")
        with self.assertRaises(ValueError):
            compare.create_comparison(image, self.directory / "invalid.json", "synthetic")

    def test_cli_records_atomically_reports_gaps_and_requires_defect_resolution(self):
        spec = deepcopy(self.spec)
        spec["thresholds"].update(requireCleanTrackedFiles=False, requireNoUntrackedFiles=False)
        quality.write_json(self.spec_path, spec)
        index = self.directory / "synthetic-tool-index.json"
        output = self.directory / "cli/run.json"
        base = [sys.executable, str(Path(quality.__file__)), "--spec", str(self.spec_path)]
        def invoke(*args):
            return subprocess.run(base + list(args), capture_output=True, text=True, check=False)
        self.assertEqual(0, invoke("init", "--output", str(output), "--reference-index", str(index)).returncode)
        self.assertEqual(2, invoke("evaluate", "--run", str(output)).returncode)
        before = output.read_bytes()
        common = ["--run", str(output), "--case", "TOOL-FIXTURE", "--notes", "Tool CLI self-test.", "--reviewer", "unit-test"]
        self.assertEqual(1, invoke("record", *common, "--status", "pass", "--score", "4").returncode)
        self.assertEqual(before, output.read_bytes())
        self.assertEqual(0, invoke("record", *common, "--status", "pass", "--score", "4", "--evidence", "test_log=" + str(self.log)).returncode)
        self.assertEqual(0, invoke("evaluate", "--run", str(output)).returncode)

        defect = ["--id", "TOOL-DEFECT", "--priority", "P1"]
        self.assertEqual(0, invoke("defect", *common, *defect, "--status", "open").returncode)
        self.assertEqual(2, invoke("evaluate", "--run", str(output)).returncode)
        self.assertEqual(1, invoke("defect", *common, *defect, "--status", "resolved").returncode)
        self.assertEqual(0, invoke("defect", *common, *defect, "--status", "resolved", "--evidence", "test_log=" + str(self.log)).returncode)
        self.assertEqual(0, invoke("evaluate", "--run", str(output)).returncode)

    def test_invalid_command_line_usage_returns_invalid_input(self):
        for script in (quality.__file__, compare.__file__):
            result = subprocess.run([sys.executable, str(script), "--unknown-option"], capture_output=True, text=True, check=False)
            self.assertEqual(1, result.returncode)


if __name__ == "__main__":
    unittest.main(verbosity=2)
