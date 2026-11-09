"""Report scoped Navidrome functionality evidence; native tasks remain independent.

Run verify.ps1 from a clean commit first. No MixRadio installation/package is
required. Exit 2 means valid, outstanding functional acceptance.
"""

import argparse
from collections import Counter
import html
import json
from pathlib import Path
import re
import sys
from urllib.parse import quote

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/mixradio'))
import quality
import audit_functions
import build_spec
import inspect_source

REQUIRED_SUITES = {'build', 'protocol', 'audio', 'connection', 'jukebox', 'services', 'integration',
                   'playback', 'serializer', 'resources', 'reference', 'coverage', 'acceptance-tools', 'quality-tools'}
MARKER = re.compile(r'^ND-CHECK:([a-z-]+:[a-z-]+)$', re.MULTILINE)


def validate_verification(verification, code):
    if (verification.get('schemaVersion') != 2 or verification.get('passed') is not True or
            verification.get('code') != code or verification.get('codeAfter') != code or
            not code.get('trackedClean') or code.get('untrackedFiles')):
        raise ValueError('Verification must belong to this clean committed checkout')
    quality.offset_time(verification['startedAt'])
    quality.offset_time(verification['completedAt'])
    checks = {item['name']: item for item in verification['checks']}
    if set(checks) != REQUIRED_SUITES or len(checks) != len(verification['checks']):
        raise ValueError('Verification suites are missing, unexpected or duplicated')
    for name, check in checks.items():
        if check['exitCode'] != 0 or quality.digest(check['log']) != check['sha256']:
            raise ValueError('A verification log failed or changed: ' + name)
        quality.offset_time(check['startedAt'])
        quality.offset_time(check['completedAt'])
        markers = MARKER.findall(Path(check['log']).read_text(encoding='utf-8-sig'))
        if any(count != 1 for count in Counter(markers).values()) or markers != check.get('proofs'):
            raise ValueError('Scoped proof declarations differ from the log: ' + name)
        if any(proof.split(':')[0] != name for proof in markers):
            raise ValueError('A suite cannot certify another suite: ' + name)
    return checks


def missing_proofs(case, checks):
    if case['verificationLevel'] == 'native':
        return ['native task observation']
    missing = []
    for proof in case['automatedProofs']:
        suite = proof.split(':')[0]
        if proof not in checks.get(suite, {}).get('proofs', []):
            missing.append(proof)
    return missing


def revalidate_functional(inventory, data):
    sources = [item for item in inventory['sources'] if item.get('role') == 'functionality']
    if len(sources) != 1:
        raise ValueError('Exactly one locked functional source is required')
    source = sources[0]
    source_root = ROOT / '.tmp/navidrome'
    lock_path = ROOT / 'tools/navidrome/source-lock.json'
    lock = quality.read_json(lock_path)
    if (source['commit'] != lock['commit'] or source['repository'] != lock['repository'] or
            inspect_source.git(source_root, 'rev-parse', 'HEAD') != source['commit'] or
            inspect_source.git(source_root, 'remote', 'get-url', 'origin') != source['repository'] or
            inspect_source.git(source_root, 'status', '--porcelain') or
            quality.digest(lock_path) != inventory['sourceLockSha256']):
        raise ValueError('The clean Navidrome source/remote/lock differs from the inventory')
    for item in source['files']:
        if quality.digest(quality.safe_bundle_path(source_root, item['path'])) != item['sha256']:
            raise ValueError('Functional source bytes changed: ' + item['path'])
    return audit_functions.analyze(data, source, source_root, ROOT)


def native_gap(code, path=None):
    if path is None:
        return {'schemaVersion': 2, 'recordedAt': quality.timestamp(), 'code': code,
                'nativeVerified': False, 'status': 'not_run',
                'limitation': 'No native task was observed for this commit. Earlier capture failures describe historical source and are not current native evidence.'}
    value = quality.read_json(path)
    quality.offset_time(value['recordedAt'])
    if (value.get('schemaVersion') != 2 or value.get('code') != code or value.get('nativeVerified') is not False or
            value.get('status') not in {'not_run', 'blocked'} or not value.get('limitation')):
        raise ValueError('Native gap observations must belong to this commit and cannot certify native success')
    return value


def facts(checks):
    result = {}
    text = Path(checks['integration']['log']).read_text(encoding='utf-8-sig')
    for name, value in re.findall(r'^ND-FACT:([a-z-]+):([^\r\n]+)$', text, re.MULTILINE):
        if name in result or name not in {'server-version', 'extensions'}:
            raise ValueError('Unexpected or duplicate fixture fact')
        result[name] = value
    if set(result) != {'server-version', 'extensions'}:
        raise ValueError('Actual server version/capabilities are missing from integration evidence')
    return result


def feature_summary(data, spec, run):
    results = {item['id']: item for item in run['results']}
    case_by_id = {case['id']: case for case in spec['cases']}
    summary = []
    for feature in data['features']:
        cases = [case for case in spec['cases'] if case.get('featureId') == feature['id']]
        levels = {case['verificationLevel']: results[case['id']]['status'] for case in cases}
        summary.append({'id': feature['id'], 'title': feature['title'], 'levels': levels,
                        'nativeAccepted': levels.get('native') == 'pass'})
    return {'functions': summary, 'mapped': len(summary), 'nativeAccepted': sum(item['nativeAccepted'] for item in summary),
            'automatedPassed': sum(item['status'] == 'pass' for item in run['results'] if case_by_id[item['id']]['verificationLevel'] != 'native'),
            'automatedRequired': sum(case['verificationLevel'] != 'native' for case in spec['cases'])}


def render_matrix(report, inventory, summary, code_evidence, server_facts):
    escape = lambda value: html.escape(str(value))
    by_id = {item['id']: item for item in summary['functions']}
    lock = quality.read_json(ROOT / 'tools/navidrome/source-lock.json')
    upstream = lock['repository'].removesuffix('.git') + '/blob/' + lock['commit'] + '/'
    rows = []
    for function in inventory['functions']:
        item = by_id[function['id']]
        source_links = ' '.join('<a href="' + escape(upstream + quote(source['path'], safe='/') + '#L' + str(source['line'])) + '">' + escape(source.get('symbol', source['path'])) + '</a>' for source in function['sources'])
        client_links = ' '.join('<a href="' + quote(code_evidence[client['path']]['path']) + '">' + escape(Path(client['path']).name + ':' + str(client['line'])) + '</a>' for client in function['client'])
        cells = ''.join('<td>' + escape(item['levels'].get(level, 'no separate check')) + '</td>' for level in ('protocol', 'service', 'server', 'native'))
        endpoints = ', '.join(function['endpoints']) or 'Client playback/state capability'
        rows.append('<tr><td>' + escape(function['id']) + '<br>' + escape(function['title']) + '<br><small>' + escape(endpoints) + '</small></td>' + cells + '<td>' + source_links + '<br>' + client_links + '</td></tr>')
    section = ('<section><h2>Navidrome function evidence</h2><p>' + str(summary['mapped']) + ' mapped functions; '
               + str(summary['automatedPassed']) + '/' + str(summary['automatedRequired']) + ' source/protocol/service/server cases passed; '
               + str(summary['nativeAccepted']) + '/' + str(summary['mapped']) + ' native functional tasks accepted.</p>'
               + '<p>Real fixture: ' + escape(server_facts['server-version']) + '. Its release is separate from the locked source revision. '
               + 'Disabled jukebox/provider errors do not establish successful device audio or acoustic results. '
               + 'MixRadio design acceptance is independent.</p><details open><summary>Function-by-function evidence layers and source mapping</summary>'
               + '<table id="feature-table"><thead><tr><th>Navidrome function</th><th>Protocol</th><th>Service/control</th><th>Real server</th><th>Native task</th><th>Source / client</th></tr></thead><tbody>'
               + ''.join(rows) + '</tbody></table></details></section>')
    report = report.replace('<table><thead>', '<table id="case-table"><thead>')
    report = report.replace("document.querySelectorAll('tr[data-status]')", "document.querySelectorAll('#case-table tr[data-status]')")
    return report.replace('<details open><summary>Acceptance gaps', section + '<details open><summary>Acceptance gaps', 1)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--verification', type=Path, default=ROOT / '.tmp/navidrome-audit/verification.json')
    parser.add_argument('--native-record', type=Path, help='Optional current-commit blocked/unperformed native observation')
    parser.add_argument('--output', type=Path, help='A new run directory; existing evidence is preserved')
    args = parser.parse_args()
    code = quality.git_state()
    verification = quality.read_json(args.verification)
    checks = validate_verification(verification, code)
    data = build_spec.load_features()
    spec = quality.load_spec(build_spec.SPEC)
    if spec != build_spec.build() or verification.get('featureInventorySha256') != quality.digest(build_spec.FEATURES):
        raise ValueError('The reviewed functional inventory/specification changed after verification')
    reference_path = ROOT / '.tmp/navidrome-audit/reference-index.json'
    reference = quality.read_json(reference_path)
    quality.validate_reference(spec, reference)
    inventory = revalidate_functional(reference, data)
    gap = native_gap(code, args.native_record)
    server_facts = facts(checks)
    directory = (args.output or ROOT / '.tmp/navidrome-acceptance' / (quality.timestamp()[:10] + '-' + code['commit'][:7] + '-functions')).resolve()
    run_path = directory / 'run.json'
    run = quality.initial_run(spec, build_spec.SPEC, run_path, reference_path)
    inventory_path = directory / 'function-inventory.json'
    quality.write_json(inventory_path, inventory)
    note = quality.copy_evidence(directory, 'source_note', inventory_path)
    native_path = directory / 'native-observation.json'
    quality.write_json(native_path, gap)
    native = quality.copy_evidence(directory, 'runtime_log', native_path)
    test_evidence = {name: quality.copy_evidence(directory, 'test_log', check['log']) for name, check in checks.items()}
    code_evidence = {client['path']: quality.copy_evidence(directory, 'source_note', ROOT / client['path'])
                     for function in inventory['functions'] for client in function['client']}
    for case, result in zip(spec['cases'], run['results']):
        missing = missing_proofs(case, checks)
        if case['verificationLevel'] == 'native':
            result.update(status=gap['status'], notes=gap['limitation'] + ' Prerequisites: ' + ' '.join(case['prerequisites']),
                          reviewer='Current-source evidence recorder', measuredAt=quality.timestamp(), evidence=[note, native])
        elif missing:
            result.update(status='blocked', notes='The declared evidence layer was not observed: ' + ', '.join(missing),
                          reviewer='Scoped automated verification', measuredAt=quality.timestamp(), evidence=[note])
        else:
            suites = sorted({proof.split(':')[0] for proof in case['automatedProofs']})
            result.update(status='pass', score=3, notes='Declared ' + case['verificationLevel'] + ' criteria verified by ' + ', '.join(case['automatedProofs']) + '. Native execution is independent.',
                          reviewer='Scoped automated verification', measuredAt=quality.timestamp(),
                          evidence=[note] + [test_evidence[suite] for suite in suites])
        errors = quality.validate_result(case, result, directory, spec)
        if errors:
            raise ValueError(case['id'] + ': ' + '; '.join(errors))
    run['defects'] = [{'id': 'NATIVE-FUNCTIONS-OUTSTANDING', 'caseId': 'ND-TRANSPORT-NATIVE', 'priority': 'P1', 'status': 'open',
                       'notes': gap['limitation'], 'reviewer': 'Current-source evidence recorder', 'updatedAt': quality.timestamp(), 'evidence': [native]}]
    run['verificationRecord'] = quality.copy_evidence(directory, 'source_note', args.verification)
    run['featureInventory'] = quality.copy_evidence(directory, 'source_note', build_spec.FEATURES)
    run['fixture'] = {**verification['fixture'], **server_facts}
    quality.write_json(run_path, run)
    evaluation = quality.evaluate_run(run, spec, directory, quality.digest(build_spec.SPEC))
    if any(item['errors'] for item in evaluation['checks']):
        raise ValueError('The generated acceptance run has invalid evidence')
    summary = feature_summary(data, spec, run)
    quality.write_json(directory / 'function-summary.json', summary)
    quality.write_json(directory / 'evaluation.json', {key: value for key, value in evaluation.items() if key != 'checks'})
    rendered = render_matrix(quality.render_report(run, spec, evaluation), inventory, summary, code_evidence, server_facts)
    (directory / 'report.html').write_text(rendered, encoding='utf-8')
    print(json.dumps({'accepted': evaluation['accepted'], 'mappedFunctions': summary['mapped'],
                      'automatedPassed': summary['automatedPassed'], 'automatedRequired': summary['automatedRequired'],
                      'nativeAccepted': summary['nativeAccepted'], 'nativeRequired': summary['mapped'],
                      'passed': evaluation['passed'], 'required': evaluation['required']}, indent=2))
    print('Report: ' + str(directory / 'report.html'))
    return 0 if evaluation['accepted'] else 2


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (ValueError, KeyError, OSError, StopIteration) as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
