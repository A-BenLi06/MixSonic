"""Build function-based acceptance cases from the reviewed Navidrome inventory."""

import argparse
import hashlib
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FEATURES = Path(__file__).with_name('features.json')
SPEC = Path(__file__).with_name('acceptance-spec.json')
LEVELS = {'protocol', 'service', 'server', 'native'}


def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def load_features(path=FEATURES):
    data = read_json(path)
    if data.get('schemaVersion') != 1 or not data.get('features'):
        raise ValueError('A versioned functional inventory is required')
    ids = [feature['id'] for feature in data['features']]
    if len(set(ids)) != len(ids):
        raise ValueError('Duplicate feature IDs')
    for feature in data['features']:
        if not feature.get('sources') or not feature.get('client'):
            raise ValueError('Source and client mapping missing: ' + feature['id'])
        for key in ('steps', 'expected', 'prerequisites'):
            values = feature.get('native', {}).get(key)
            if not values or any(not isinstance(value, str) or not value.strip() for value in values):
                raise ValueError('Native criteria missing: ' + feature['id'] + '/' + key)
        levels = [check['level'] for check in feature['checks']]
        if len(levels) != len(set(levels)) or any(level not in LEVELS - {'native'} for level in levels):
            raise ValueError('Invalid or duplicate automated level: ' + feature['id'])
        for check in feature['checks']:
            if not check.get('proofs') or not check.get('expected') or len(set(check['proofs'])) != len(check['proofs']):
                raise ValueError('A scoped check needs unique proofs and explicit claims')
            for proof in check['proofs']:
                suite, group = proof.split(':')
                if suite not in data['proofSources'] or not group:
                    raise ValueError('Unknown proof suite: ' + proof)
                if check['level'] == 'server' and suite != 'integration':
                    raise ValueError('Real-server evidence must come from integration: ' + proof)
    return data


def make_spec(data, feature_digest, lock):
    cases = []

    def case(identity, title, level, expected, proofs=None, feature=None, steps=None):
        item = {'id': identity, 'title': title, 'dimension': 'protocol' if level == 'source' else level,
                'priority': 'P0' if level == 'source' else 'P1', 'required': True, 'weight': 1,
                'verificationLevel': level, 'referenceRole': 'functionality',
                'steps': steps or ['Run the named production checks from a clean commit and inspect the scoped proof groups.'],
                'expected': expected, 'evidence': [['source_note'], ['manual_record'], ['runtime_log']] if level == 'native'
                else [['source_note'], ['test_log']]}
        if proofs:
            item['automatedProofs'] = proofs
        if feature:
            item['featureId'] = feature['id']
            item['referenceIds'] = ['ND-' + feature['id']]
            if level == 'native':
                item['prerequisites'] = feature['native']['prerequisites']
        cases.append(item)

    case('ND-SOURCE', 'Locked source and complete endpoint accounting', 'source',
         ['The pinned clean Navidrome commit/remote/source bytes match the lock.',
          'Every registered endpoint is mapped to a function, a documented legacy alternative, a fixed compatibility response or an explicit upstream unavailable status.'],
         ['reference:source', 'coverage:functions'])
    case('BUILD-PC', 'Windows 8.1 desktop build and registration', 'source',
         ['MSBuild 14 Debug x86 build and workspace package registration succeed for the current commit. Native interaction is separate.'], ['build:desktop'])
    case('XML-MODELS', 'Production XML contracts', 'source',
         ['Production model serializers and response regressions pass.'], ['serializer:models'])
    case('RESOURCES-PC', 'Resource-backed native command source', 'source',
         ['Desktop strings, namescopes, project-owned resources and source inclusion validate. Rendering remains a native task.'], ['resources:desktop'])
    for feature in data['features']:
        for check in feature['checks']:
            level = check['level']
            case('ND-' + feature['id'] + '-' + level.upper(), feature['title'] + ' — ' + level,
                 level, check['expected'], check['proofs'], feature)
        case('ND-' + feature['id'] + '-NATIVE', feature['title'] + ' — native task', 'native',
             feature['native']['expected'], feature=feature, steps=feature['native']['steps'])
    return {'schemaVersion': 1, 'title': 'Navidrome desktop functional acceptance',
            'target': {'platform': 'Windows 8.1 Store', 'architecture': 'x86', 'configuration': 'Debug'},
            'scope': data['scope'], 'featureInventorySha256': feature_digest,
            'functionalityReference': {key: lock[key] for key in ('repository', 'commit')},
            'requiredReferenceRoles': ['functionality'],
            'thresholds': {'overall': 75, 'requireAllRequiredCases': True, 'requireCleanTrackedFiles': True,
                           'requireNoUntrackedFiles': True, 'blockingPriorities': ['P0', 'P1']},
            'dimensions': {level: {'weight': 25, 'minimum': 3, 'label': label} for level, label in
                           [('protocol', 'Source and protocol contracts'), ('service', 'Production service/control logic'),
                            ('server', 'Real Navidrome behavior'), ('native', 'Native functional tasks')]},
            'scoreAnchors': {'0': 'Missing, broken or unverified.', '1': 'Only a fragment works.',
                             '2': 'Material stated criteria fail.', '3': 'All stated criteria hold at the declared evidence layer.',
                             '4': 'The criteria also hold in independently observed alternate states and repeated runs.'},
            'evidenceKinds': ['test_log', 'source_note', 'reference_inventory', 'client_screenshot',
                              'manual_record', 'runtime_log', 'video'], 'cases': cases}


def build(feature_path=FEATURES):
    return make_spec(load_features(feature_path), digest(feature_path), read_json(Path(__file__).with_name('source-lock.json')))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--write', action='store_true', help='Update the tracked generated specification')
    args = parser.parse_args()
    spec = build()
    if args.write:
        SPEC.write_text(json.dumps(spec, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    elif read_json(SPEC) != spec:
        raise ValueError('The specification is stale; run build_spec.py --write and review the result')
    print(f"PASS: {len(spec['cases'])} function-based acceptance cases; only Navidrome functional authority is required.")


if __name__ == '__main__':
    main()
