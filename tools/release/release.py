"""Prepare and validate portable release assets. Signatures are checked by verify-signatures.ps1."""
import argparse
from datetime import datetime, timezone
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import re
import subprocess
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[2]
METADATA = 'release-manifest.json'
CERTIFICATE = 'SigningCertificate.cer'
PACKAGE = 'SubSonic8.appx'
MAX_SIZE = 2 * 1024**3


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def identity(xml):
    node = ET.fromstring(xml).find('{*}Identity')
    if node is None:
        raise ValueError('AppX identity is missing')
    return dict(node.attrib)


def version_tag(version):
    if not re.fullmatch(r'(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)', version):
        raise ValueError('AppX versions require four canonical integer components')
    if any(int(part) > 65535 for part in version.split('.')):
        raise ValueError('AppX version component exceeds 65535')
    return 'v' + version


def safe_archive(archive):
    names = set()
    total = 0
    for entry in archive.infolist():
        # ZipInfo.filename is normalized on Windows; inspect the raw archive name.
        name = entry.orig_filename
        parts = PurePosixPath(name).parts
        if (not name or '\x00' in name or '\\' in name or ':' in name or name.startswith('/') or
                any(part in ('..', '.') for part in parts) or
                PurePosixPath(name).as_posix() != name.rstrip('/') or name.casefold() in names):
            raise ValueError('Unsafe or duplicate archive path: ' + name)
        if (entry.external_attr >> 16) & 0o170000 == 0o120000:
            raise ValueError('Archive contains a symbolic link')
        names.add(name.casefold())
        total += entry.file_size
        if entry.file_size > MAX_SIZE or total > MAX_SIZE:
            raise ValueError('Archive is too large')


def appx_info(data):
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        safe_archive(archive)
        if 'AppxSignature.p7x' not in archive.namelist():
            raise ValueError('Unsigned AppX package')
        xml = ET.fromstring(archive.read('AppxManifest.xml'))
        return identity(ET.tostring(xml)), [dict(node.attrib) for node in xml.findall('{*}Dependencies/{*}PackageDependency')]


def validate_contents(files, expected):
    if PACKAGE not in files or CERTIFICATE not in files:
        raise ValueError('Release requires the main AppX and its public signing certificate')
    main, requirements = appx_info(files[PACKAGE])
    for key in ('Name', 'Publisher', 'Version'):
        if main.get(key) != expected.get(key):
            raise ValueError('Package/source identity mismatch: ' + key)
    dependencies = []
    for name, data in files.items():
        if name in (PACKAGE, CERTIFICATE):
            continue
        path = PurePosixPath(name)
        if len(path.parts) != 3 or path.parts[0] != 'Dependencies' or path.suffix.lower() != '.appx':
            raise ValueError('Unexpected release file: ' + name)
        item, _ = appx_info(data)
        if path.parts[1].lower() != item.get('ProcessorArchitecture', 'neutral'):
            raise ValueError('Dependency directory differs from its architecture: ' + name)
        dependencies.append(item)
        if not any(req['Name'] == item['Name'] and req.get('Publisher') == item.get('Publisher') for req in requirements):
            raise ValueError('Unrelated dependency: ' + name)
    architecture = main.get('ProcessorArchitecture', 'neutral')
    architectures = ('x86', 'x64', 'arm') if architecture == 'neutral' else (architecture,)
    for required in requirements:
        for architecture in architectures:
            if not any(item['Name'] == required['Name'] and item.get('Publisher') == required.get('Publisher') and
                       item.get('ProcessorArchitecture', 'neutral') in ('neutral', architecture) and
                       tuple(map(int, item['Version'].split('.'))) >= tuple(map(int, required['MinVersion'].split('.')))
                       for item in dependencies):
                raise ValueError('Missing compatible dependency: ' + required['Name'] + '/' + architecture)


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, text=True, encoding='utf-8').strip()


def prepare(package, certificate, dependencies, output):
    if git('status', '--porcelain'):
        raise ValueError('Commit all tracked and untracked source changes before building a release')
    expected = identity((ROOT / 'Client/Package.appxmanifest').read_bytes())
    tag = version_tag(expected['Version'])
    files = {PACKAGE: package.read_bytes(), CERTIFICATE: certificate.read_bytes()}
    if dependencies:
        for path in sorted(dependencies.rglob('*.appx')):
            relative = path.relative_to(dependencies)
            if len(relative.parts) != 2:
                raise ValueError('Expected Dependencies/<architecture>/<package>.appx')
            files['Dependencies/' + relative.as_posix()] = path.read_bytes()
    validate_contents(files, expected)
    output.mkdir(parents=True, exist_ok=False)
    asset = 'SubSonic8-' + tag + '.zip'
    with zipfile.ZipFile(output / asset, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        for name, data in files.items():
            archive.writestr(name, data)
    metadata = {
        'schemaVersion': 1, 'tag': tag, 'version': expected['Version'], 'commit': git('rev-parse', 'HEAD'),
        'createdAt': datetime.now(timezone.utc).isoformat(timespec='seconds'), 'asset': asset,
        'sha256': sha256((output / asset).read_bytes()),
        'certificateSha256': sha256(files[CERTIFICATE]),
        'files': {name: sha256(data) for name, data in files.items()},
    }
    (output / METADATA).write_text(json.dumps(metadata, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(metadata, indent=2))


def verify(directory, tag, commit, certificate_hash, extract=None):
    metadata = json.loads((directory / METADATA).read_text(encoding='utf-8'))
    if metadata.get('schemaVersion') != 1 or not re.fullmatch('[0-9a-f]{40}', commit):
        raise ValueError('Invalid release metadata or source revision')
    expected = identity(git('show', commit + ':Client/Package.appxmanifest'))
    if (tag != version_tag(expected['Version']) or metadata.get('tag') != tag or
            metadata.get('version') != expected['Version'] or metadata.get('commit') != commit):
        raise ValueError('Release tag, version and source commit must match')
    asset = 'SubSonic8-' + tag + '.zip'
    if metadata.get('asset') != asset or {p.name for p in directory.iterdir()} != {asset, METADATA}:
        raise ValueError('Unexpected or missing release assets')
    data = (directory / asset).read_bytes()
    if sha256(data) != metadata.get('sha256'):
        raise ValueError('Release archive checksum mismatch')
    if not re.fullmatch('[0-9a-fA-F]{64}', certificate_hash):
        raise ValueError('Configure RELEASE_CERTIFICATE_SHA256 before publication')
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        safe_archive(archive)
        files = {entry.filename: archive.read(entry) for entry in archive.infolist()}
    if {name: sha256(value) for name, value in files.items()} != metadata.get('files'):
        raise ValueError('Release file inventory/checksum mismatch')
    if sha256(files.get(CERTIFICATE, b'')) != certificate_hash.lower() or metadata.get('certificateSha256') != certificate_hash.lower():
        raise ValueError('Release certificate does not match the repository pin')
    validate_contents(files, expected)
    if extract:
        extract.mkdir(parents=True, exist_ok=False)
        for name, value in files.items():
            path = extract / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(value)
    print('PASS: source identity, version, certificate pin, archive inventory and checksums')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    stage = commands.add_parser('prepare')
    stage.add_argument('--package', type=Path, required=True)
    stage.add_argument('--certificate', type=Path, required=True)
    stage.add_argument('--dependencies', type=Path)
    stage.add_argument('--output', type=Path, required=True)
    check = commands.add_parser('verify')
    check.add_argument('--directory', type=Path, required=True)
    check.add_argument('--tag', required=True)
    check.add_argument('--commit', required=True)
    check.add_argument('--certificate-sha256', required=True)
    check.add_argument('--extract', type=Path)
    args = vars(parser.parse_args())
    command = args.pop('command')
    if command == 'prepare':
        prepare(**args)
    else:
        verify(directory=args['directory'], tag=args['tag'], commit=args['commit'],
               certificate_hash=args['certificate_sha256'], extract=args['extract'])


if __name__ == '__main__':
    main()
