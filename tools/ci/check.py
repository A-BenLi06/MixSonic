"""Hosted checks; no app deployment or original MixRadio packages required."""
import argparse
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]


def run(*args):
    subprocess.run([str(arg) for arg in args], cwd=ROOT, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path)
    args = parser.parse_args()
    output = ROOT / '.tmp/ci'
    output.mkdir(parents=True, exist_ok=True)
    source = (args.source or output / 'navidrome-reference').resolve()
    lock = json.loads((ROOT / 'tools/navidrome/source-lock.json').read_text(encoding='utf-8-sig'))
    if not source.exists():
        run('git', 'clone', '--filter=blob:none', '--no-checkout', lock['repository'], source)
        run('git', '-C', source, 'fetch', '--depth=1', 'origin', lock['commit'])
        run('git', '-C', source, 'checkout', '--detach', lock['commit'])
    for script in ('tools/navidrome-tests/check-client.py', 'tools/phone/check.py', 'tools/navidrome/build_spec.py',
                   'tools/navidrome/test_acceptance.py', 'tools/mixradio/test_quality_tools.py',
                   'tools/release/test_release.py'):
        run(sys.executable, script)
    run(sys.executable, 'tools/navidrome/inspect_source.py', '--source', source,
        '--output', output / 'reference-inventory.json')
    run(sys.executable, 'tools/navidrome/audit_functions.py', '--source', source,
        '--inventory', output / 'reference-inventory.json', '--output', output / 'function-inventory.json')
    run('git', 'diff', '--exit-code')
    print('PASS: resource, source and tooling checks; native execution requires the legacy SDK.')


if __name__ == '__main__':
    main()
