"""Acceptance-tool self-tests use isolated synthetic source/logs, never app evidence."""

from copy import deepcopy
from pathlib import Path
import tempfile
import unittest

import audit_functions
import build_spec
import report

quality = report.quality
TIME = '2026-11-09T14:00:00+08:00'


class FunctionalAcceptanceTests(unittest.TestCase):
    def setUp(self):
        scratch = report.ROOT / '.tmp/navidrome-tool-tests'
        scratch.mkdir(parents=True, exist_ok=True)
        temporary = tempfile.TemporaryDirectory(prefix='synthetic-', dir=scratch)
        self.directory = Path(temporary.name).resolve()
        self.assertTrue(self.directory.is_relative_to(scratch.resolve()))
        self.addCleanup(temporary.cleanup)
        self.source = self.directory / 'source'
        self.root = self.directory / 'client'
        self.write(self.source / 'server/subsonic/tiny.go', 'func (api *Router) GetThing() {}')
        self.write(self.root / 'Client.Common/Services/Navidrome/NavidromeApi.cs', 'public void GetThing() { Request("getThing"); }')
        self.write(self.root / 'Client.Common/Services/SubsonicService.cs', '// Synthetic empty client support file.')
        self.write(self.root / 'checks.cs', 'Console.WriteLine("ND-CHECK:protocol:thing");')
        self.data = {'schemaVersion': 1, 'scope': 'Isolated synthetic acceptance-tool fixture.', 'features': [
            {'id': 'THING', 'title': 'Synthetic thing', 'endpoints': ['getThing'],
             'sources': [{'path': 'server/subsonic/tiny.go'}],
             'client': [{'path': 'Client.Common/Services/Navidrome/NavidromeApi.cs', 'symbol': 'GetThing'}],
             'checks': [{'level': 'protocol', 'proofs': ['protocol:thing'], 'expected': ['Synthetic check holds.']}],
             'native': {'steps': ['Observe the actual native task.'], 'expected': ['Native evidence exists.'], 'prerequisites': ['Synthetic tool prerequisite.']}}
        ], 'aliases': [], 'fixedCompatibility': [], 'upstreamUnavailable': {'h501': [], 'h410': []}, 'proofSources': {'protocol': 'checks.cs'}}
        self.functional = {'commit': 'synthetic-source-only', 'files': [
            {'path': 'server/subsonic/tiny.go', 'sha256': build_spec.digest(self.source / 'server/subsonic/tiny.go')}],
            'registrations': [{'endpoint': 'getThing', 'registration': 'h', 'handler': 'GetThing', 'line': 1}]}
        self.code = {'commit': 'synthetic-code-only', 'trackedClean': True, 'untrackedFiles': []}
        self.verification = {'schemaVersion': 2, 'passed': True, 'code': self.code, 'codeAfter': deepcopy(self.code),
                             'startedAt': TIME, 'completedAt': TIME, 'checks': []}
        for suite in sorted(report.REQUIRED_SUITES):
            log = self.directory / (suite + '.log')
            self.write(log, 'ND-CHECK:' + suite + ':synthetic\n')
            self.verification['checks'].append({'name': suite, 'log': str(log), 'sha256': quality.digest(log), 'exitCode': 0,
                'startedAt': TIME, 'completedAt': TIME, 'proofs': [suite + ':synthetic']})

    @staticmethod
    def write(path, text):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding='utf-8')

    def analyze(self):
        return audit_functions.analyze(self.data, self.functional, self.source, self.root)

    def test_complete_endpoint_accounting_is_structural(self):
        value = self.analyze()
        self.assertEqual(value['counts']['directClientEndpoints'], 1)
        self.assertIn('native task execution is separate', value['limits'])
        self.assertNotIn('nativeAccepted', value)

    def test_new_source_endpoint_is_not_silently_excluded(self):
        self.functional['registrations'].append({'endpoint': 'newFunction', 'registration': 'h', 'line': 2})
        with self.assertRaisesRegex(ValueError, 'Unaccounted'):
            self.analyze()

    def test_conditional_disabled_route_remains_a_function(self):
        self.functional['registrations'].append({'endpoint': 'getThing', 'registration': 'h501', 'line': 2})
        self.assertEqual(self.analyze()['counts']['implementedEndpoints'], 1)

    def test_disabled_boundary_cannot_hide_an_implemented_function(self):
        self.data['upstreamUnavailable']['h501'].append('getThing')
        with self.assertRaisesRegex(ValueError, 'boundary changed'):
            self.analyze()

    def test_missing_production_request_fails(self):
        self.write(self.root / 'Client.Common/Services/Navidrome/NavidromeApi.cs', 'public void GetThing() {}')
        with self.assertRaisesRegex(ValueError, 'no production request'):
            self.analyze()

    def test_missing_named_test_declaration_fails(self):
        self.write(self.root / 'checks.cs', '// The actual scoped proof was removed.')
        with self.assertRaisesRegex(ValueError, 'no test-source declaration'):
            self.analyze()

    def test_changed_source_bytes_fail(self):
        self.write(self.source / 'server/subsonic/tiny.go', 'func (api *Router) GetThing() { changed() }')
        with self.assertRaisesRegex(ValueError, 'not inventoried'):
            self.analyze()

    def test_escaping_source_reference_fails(self):
        with self.assertRaisesRegex(ValueError, 'escaping'):
            audit_functions.local_path(self.source, '../client/checks.cs')

    def test_legacy_alternative_needs_a_real_replacement(self):
        self.functional['registrations'].append({'endpoint': 'oldThing', 'registration': 'h', 'line': 2})
        self.data['aliases'].append({'endpoint': 'oldThing', 'featureId': 'THING', 'alternatives': ['missingThing'], 'reason': 'Synthetic alternative.'})
        with self.assertRaisesRegex(ValueError, 'no implemented replacement'):
            self.analyze()

    def test_nested_request_arguments_preserve_both_endpoints(self):
        values = audit_functions.request_literals('Request(string.IsNullOrEmpty(id) ? "createThing" : "updateThing")')
        self.assertEqual(values, {'createThing', 'updateThing'})

    def test_comments_and_quoted_decoys_are_not_requests(self):
        values = audit_functions.request_literals('// Request("commentThing")\n/* Request("blockThing") */\nvar x = "Request(\\"quotedThing\\")"; Request("getThing");')
        self.assertEqual(values, {'getThing'})

    def test_parenthesis_in_a_literal_cannot_end_a_request(self):
        values = audit_functions.request_literals('new SubsonicRequest(config, condition(")") ? "oneThing" : "twoThing")')
        self.assertEqual(values, {'oneThing', 'twoThing'})

    def test_generated_spec_requires_functionality_only(self):
        value = build_spec.make_spec(self.data, 'synthetic-digest', {'repository': 'synthetic', 'commit': 'synthetic'})
        self.assertEqual(value['requiredReferenceRoles'], ['functionality'])
        self.assertNotIn('visual', value['dimensions'])
        native = [case for case in value['cases'] if case['verificationLevel'] == 'native']
        self.assertEqual(len(native), 1)
        self.assertNotIn('automatedProofs', native[0])

    def test_scoped_verification_accepts_unchanged_records(self):
        self.assertEqual(set(report.validate_verification(self.verification, self.code)), report.REQUIRED_SUITES)

    def test_an_old_or_dirty_commit_is_rejected(self):
        old = deepcopy(self.code); old['commit'] = 'other-synthetic-code'
        with self.assertRaises(ValueError):
            report.validate_verification(self.verification, old)
        dirty = deepcopy(self.code); dirty['trackedClean'] = False
        with self.assertRaises(ValueError):
            report.validate_verification(self.verification, dirty)

    def test_missing_or_duplicate_suite_is_rejected(self):
        value = deepcopy(self.verification); value['checks'].pop()
        with self.assertRaises(ValueError):
            report.validate_verification(value, self.code)
        value = deepcopy(self.verification); value['checks'].append(value['checks'][0])
        with self.assertRaises(ValueError):
            report.validate_verification(value, self.code)

    def test_changed_log_is_rejected(self):
        self.write(Path(self.verification['checks'][0]['log']), 'Changed synthetic log.')
        with self.assertRaisesRegex(ValueError, 'log failed or changed'):
            report.validate_verification(self.verification, self.code)

    def test_proof_list_cannot_disagree_with_its_log(self):
        self.verification['checks'][0]['proofs'] = ['fabricated:synthetic']
        with self.assertRaisesRegex(ValueError, 'declarations differ'):
            report.validate_verification(self.verification, self.code)

    def test_a_suite_cannot_certify_another_suite(self):
        item = self.verification['checks'][0]
        self.write(Path(item['log']), 'ND-CHECK:foreign:synthetic\n')
        item['sha256'] = quality.digest(item['log']); item['proofs'] = ['foreign:synthetic']
        with self.assertRaisesRegex(ValueError, 'another suite'):
            report.validate_verification(self.verification, self.code)

    def test_a_passed_suite_does_not_imply_a_missing_group_passed(self):
        case = {'verificationLevel': 'server', 'automatedProofs': ['integration:positive']}
        self.assertEqual(report.missing_proofs(case, {'integration': {'proofs': ['integration:disabled']}}), ['integration:positive'])

    def test_automated_evidence_never_certifies_native_tasks(self):
        case = {'verificationLevel': 'native', 'automatedProofs': ['audio:synthetic']}
        self.assertEqual(report.missing_proofs(case, {'audio': {'proofs': ['audio:synthetic']}}), ['native task observation'])
        gap = report.native_gap(self.code)
        self.assertFalse(gap['nativeVerified'])
        self.assertEqual(gap['status'], 'not_run')

    def test_old_or_positive_native_gap_records_are_rejected(self):
        value = report.native_gap(self.code); value['code'] = {**self.code, 'commit': 'old-source'}
        path = self.directory / 'native.json'; quality.write_json(path, value)
        with self.assertRaises(ValueError):
            report.native_gap(self.code, path)
        value['code'] = self.code; value['nativeVerified'] = True; quality.write_json(path, value)
        with self.assertRaises(ValueError):
            report.native_gap(self.code, path)

    def test_native_pass_requires_native_evidence_kinds(self):
        spec = build_spec.make_spec(self.data, 'synthetic-digest', {'repository': 'synthetic', 'commit': 'synthetic'})
        case = next(case for case in spec['cases'] if case['verificationLevel'] == 'native')
        evidence = [quality.copy_evidence(self.directory, kind, self.root / 'checks.cs') for kind in ('source_note', 'test_log')]
        result = {'status': 'pass', 'score': 3, 'notes': 'Synthetic protocol evidence only.', 'reviewer': 'Tool self-test', 'measuredAt': TIME, 'evidence': evidence}
        errors = quality.validate_result(case, result, self.directory, spec)
        self.assertTrue(any('manual_record' in error for error in errors))
        self.assertTrue(any('runtime_log' in error for error in errors))


if __name__ == '__main__':
    unittest.main(verbosity=1)
