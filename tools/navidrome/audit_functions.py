"""Account for all locked Navidrome endpoints without claiming native execution."""

import argparse
from datetime import datetime
import json
from pathlib import Path
import re

import build_spec
import inspect_source

ROOT = Path(__file__).resolve().parents[2]
REQUEST = re.compile(r'(?:\bRequest|\bnew\s+SubsonicRequest)\s*\(')
TOKEN = re.compile(r'''//[^\r\n]*|/\*[\s\S]*?\*/|@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*' ''', re.VERBOSE)


def request_literals(content):
    values = set()
    tokens = list(TOKEN.finditer(content))
    masked = list(content)
    for token in tokens:
        masked[token.start():token.end()] = [' '] * (token.end() - token.start())
    masked = ''.join(masked)
    for match in REQUEST.finditer(masked):
        depth, index = 1, match.end()
        while index < len(masked) and depth:
            char = masked[index]
            if char == '(':
                depth += 1
            elif char == ')':
                depth -= 1
            index += 1
        if depth:
            raise ValueError('An unterminated production request was found')
        for token in tokens:
            if match.end() <= token.start() and token.end() < index:
                literal = re.fullmatch(r'"([A-Za-z][A-Za-z0-9]+)"', token.group())
                if literal:
                    values.add(literal[1])
    return values


def local_path(directory, relative):
    if not isinstance(relative, str) or Path(relative).is_absolute():
        raise ValueError('A source reference must be relative')
    target = (directory / relative).resolve()
    if not target.is_relative_to(directory.resolve()) or not target.is_file():
        raise ValueError('Missing or escaping source reference: ' + str(relative))
    return target


def reference(directory, item):
    path = local_path(directory, item['path'])
    content = path.read_text(encoding='utf-8-sig')
    symbol = item.get('symbol')
    match = re.search(r'\b' + re.escape(symbol) + r'\b', content) if symbol else None
    if symbol and match is None:
        raise ValueError('Mapped symbol is absent: ' + item['path'] + '/' + symbol)
    line = content[:match.start()].count('\n') + 1 if symbol else 1
    return {**item, 'line': line, 'sha256': build_spec.digest(path)}


def proof_exists(content, suite, group):
    marker = 'ND-CHECK:' + suite + ':' + group
    return marker in content or ('ND-CHECK:' + suite + ':' in content and
        re.search(r'\b(?:Verify|Evidence)\("' + re.escape(group) + r'"', content) is not None)


def analyze(data, functional, source, root):
    registrations = functional['registrations']
    by_endpoint = {}
    for route in registrations:
        by_endpoint.setdefault(route['endpoint'], []).append(route)
    implemented = {name for name, routes in by_endpoint.items() if any(r['registration'] in {'h', 'hr'} for r in routes)}
    allocated = {}

    def allocate(name, disposition, feature_id=None, reason=None):
        if name in allocated:
            raise ValueError('Endpoint accounted for twice: ' + name)
        if name not in by_endpoint:
            raise ValueError('Unknown source endpoint: ' + name)
        allocated[name] = {'endpoint': name, 'disposition': disposition, 'featureId': feature_id,
                           'reason': reason, 'registrations': by_endpoint[name]}

    requests = set()
    for relative in ('Client.Common/Services/Navidrome/NavidromeApi.cs', 'Client.Common/Services/SubsonicService.cs'):
        content = local_path(root, relative).read_text(encoding='utf-8-sig')
        requests.update(request_literals(content))
    functions = []
    handlers = {}
    for path in (source / 'server/subsonic').glob('*.go'):
        if 'test' in path.name.lower():
            continue
        content = path.read_text(encoding='utf-8-sig')
        for match in re.finditer(r'func\s+\(api \*Router\)\s+(\w+)\s*\(', content):
            handlers[match[1]] = {'path': path.relative_to(source).as_posix(), 'symbol': match[1],
                                 'line': content[:match.start()].count('\n') + 1, 'sha256': build_spec.digest(path)}
    feature_ids = {feature['id'] for feature in data['features']}
    for feature in data['features']:
        for name in feature['endpoints']:
            if name not in implemented or name not in requests:
                raise ValueError('A required implemented endpoint has no production request: ' + name)
            allocate(name, 'client_function', feature['id'])
        sources = [reference(source, item) for item in feature['sources']]
        for name in feature['endpoints']:
            route = next(route for route in by_endpoint[name] if route['registration'] in {'h', 'hr'})
            handler = route.get('handler')
            if handler:
                if handler not in handlers:
                    raise ValueError('An implemented route has no mapped handler: ' + name)
                if not any(item.get('symbol') == handler for item in sources):
                    sources.append(handlers[handler])
        clients = [reference(root, item) for item in feature['client']]
        for item in sources:
            if not any(file['path'] == item['path'] and file['sha256'] == item['sha256'] for file in functional['files']):
                raise ValueError('A functional source reference was not inventoried: ' + item['path'])
        for check in feature['checks']:
            for proof in check['proofs']:
                suite, group = proof.split(':')
                content = local_path(root, data['proofSources'][suite]).read_text(encoding='utf-8-sig')
                if not proof_exists(content, suite, group):
                    raise ValueError('A named proof has no test-source declaration: ' + proof)
        functions.append({'id': feature['id'], 'title': feature['title'], 'sources': sources, 'client': clients,
                          'endpoints': feature['endpoints'], 'checks': feature['checks'], 'native': feature['native']})
    for alias in data['aliases']:
        if alias['featureId'] not in feature_ids or alias['endpoint'] not in implemented or not alias.get('reason'):
            raise ValueError('Invalid legacy alternative')
        if not alias.get('alternatives') or any(name not in implemented or name not in allocated for name in alias['alternatives']):
            raise ValueError('Legacy alternative has no implemented replacement: ' + alias['endpoint'])
        allocate(alias['endpoint'], 'legacy_alternative', alias['featureId'], alias['reason'])
    for item in data['fixedCompatibility']:
        if item['endpoint'] not in implemented or not item.get('reason'):
            raise ValueError('Invalid fixed compatibility boundary')
        reference(source, {'path': item['source']})
        allocate(item['endpoint'], 'fixed_compatibility', reason=item['reason'])
    for kind, endpoints in data['upstreamUnavailable'].items():
        if kind not in {'h501', 'h410'}:
            raise ValueError('Unknown upstream unavailable status')
        for name in endpoints:
            if name in implemented or not any(route['registration'] == kind for route in by_endpoint.get(name, [])):
                raise ValueError('An unavailable boundary changed: ' + name)
            allocate(name, 'upstream_unavailable', reason=kind)
    missing = set(by_endpoint) - set(allocated)
    if missing:
        raise ValueError('Unaccounted source endpoints: ' + ', '.join(sorted(missing)))
    return {'schemaVersion': 1, 'recordedAt': datetime.now().astimezone().isoformat(timespec='seconds'),
            'featureInventorySha256': build_spec.digest(build_spec.FEATURES), 'sourceCommit': functional['commit'],
            'limits': 'Request/symbol presence is structural coverage. Named checks establish only their declared layers; native task execution is separate.',
            'counts': {'functions': len(functions), 'routeRegistrations': len(registrations), 'distinctEndpoints': len(by_endpoint),
                       'implementedEndpoints': len(implemented), 'directClientEndpoints': sum(item['disposition'] == 'client_function' for item in allocated.values()),
                       'legacyAlternatives': len(data['aliases']), 'fixedCompatibility': len(data['fixedCompatibility']),
                       'upstreamUnavailable': sum(len(values) for values in data['upstreamUnavailable'].values())},
            'functions': functions, 'endpoints': [allocated[key] for key in sorted(allocated)]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=ROOT / '.tmp/navidrome')
    parser.add_argument('--inventory', type=Path, default=ROOT / '.tmp/navidrome-audit/reference-index.json')
    parser.add_argument('--output', type=Path, default=ROOT / '.tmp/navidrome-audit/function-inventory.json')
    args = parser.parse_args()
    data = build_spec.load_features()
    if build_spec.read_json(build_spec.SPEC) != build_spec.build():
        raise ValueError('Generated acceptance specification is stale')
    inventory = build_spec.read_json(args.inventory)
    functional = next(item for item in inventory['sources'] if item.get('role') == 'functionality')
    lock = build_spec.read_json(Path(__file__).with_name('source-lock.json'))
    if (functional['commit'] != lock['commit'] or functional['repository'] != lock['repository'] or
            inspect_source.git(args.source, 'rev-parse', 'HEAD') != lock['commit'] or inspect_source.git(args.source, 'status', '--porcelain')):
        raise ValueError('The functional source differs from its clean lock')
    result = analyze(data, functional, args.source, ROOT)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    print('PASS: ' + json.dumps(result['counts']) + '. Structural coverage only; native functional acceptance is separate.')
    print(args.output.resolve())


if __name__ == '__main__':
    main()
