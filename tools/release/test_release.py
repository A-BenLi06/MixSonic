"""Synthetic archive tests establish release validation, not native build/signature acceptance."""
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile

import release

XML = b'<Package xmlns="http://schemas.microsoft.com/appx/2010/manifest"><Identity Name="Test.App" Publisher="CN=Fixture" Version="1.2.3.4" ProcessorArchitecture="x86"/><Dependencies><PackageDependency Name="Test.Dependency" Publisher="CN=Vendor" MinVersion="1.0.0.0"/></Dependencies></Package>'
COMMIT = 'a' * 40
CERTIFICATE = b'synthetic public certificate fixture'


def appx(xml):
    output = io.BytesIO()
    with zipfile.ZipFile(output, 'w') as archive:
        archive.writestr('AppxManifest.xml', xml)
        archive.writestr('AppxSignature.p7x', b'PKCX-synthetic-not-a-real-signature')
    return output.getvalue()


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.directory = Path(self.temporary.name)
        self.asset = 'SubSonic8-v1.2.3.4.zip'
        dependency = b'<Package><Identity Name="Test.Dependency" Publisher="CN=Vendor" Version="1.0.0.0" ProcessorArchitecture="x86"/></Package>'
        self.files = {release.PACKAGE: appx(XML), release.CERTIFICATE: CERTIFICATE,
                      'Dependencies/x86/Test.Dependency.appx': appx(dependency)}
        self.metadata = {'schemaVersion': 1, 'tag': 'v1.2.3.4', 'version': '1.2.3.4', 'commit': COMMIT,
                         'asset': self.asset, 'certificateSha256': release.sha256(CERTIFICATE)}
        self.save()
        self.mock = patch.object(release, 'git', return_value=XML.decode())
        self.mock.start()
        self.addCleanup(self.mock.stop)

    def save(self):
        with zipfile.ZipFile(self.directory / self.asset, 'w') as archive:
            for name, value in self.files.items():
                archive.writestr(name, value)
        self.metadata['sha256'] = release.sha256((self.directory / self.asset).read_bytes())
        self.metadata['files'] = {name: release.sha256(value) for name, value in self.files.items()}
        self.save_metadata()

    def save_metadata(self):
        (self.directory / release.METADATA).write_text(json.dumps(self.metadata), encoding='utf-8')

    def verify(self, **kwargs):
        release.verify(self.directory, 'v1.2.3.4', COMMIT, release.sha256(CERTIFICATE), **kwargs)

    def test_valid_inventory_extracts_only_installation_assets(self):
        with tempfile.TemporaryDirectory() as scratch:
            output = Path(scratch) / 'verified'
            self.verify(extract=output)
            self.assertEqual((output / release.PACKAGE).read_bytes(), self.files[release.PACKAGE])

    def test_missing_dependency(self):
        del self.files['Dependencies/x86/Test.Dependency.appx']
        self.save()
        with self.assertRaisesRegex(ValueError, 'Missing compatible dependency'):
            self.verify()

    def test_unsigned_package(self):
        output = io.BytesIO()
        with zipfile.ZipFile(output, 'w') as archive:
            archive.writestr('AppxManifest.xml', XML)
        self.files[release.PACKAGE] = output.getvalue()
        self.save()
        with self.assertRaisesRegex(ValueError, 'Unsigned'):
            self.verify()

    def test_identity_version_mismatch(self):
        self.files[release.PACKAGE] = appx(XML.replace(b'1.2.3.4', b'1.2.3.5'))
        self.save()
        with self.assertRaisesRegex(ValueError, 'identity mismatch'):
            self.verify()

    def test_wrong_source_commit(self):
        self.metadata['commit'] = 'b' * 40
        self.save_metadata()
        with self.assertRaisesRegex(ValueError, 'source commit'):
            self.verify()

    def test_unpinned_certificate(self):
        self.files[release.CERTIFICATE] = b'other certificate'
        self.save()
        with self.assertRaisesRegex(ValueError, 'certificate does not match'):
            self.verify()

    def test_changed_archive(self):
        with (self.directory / self.asset).open('ab') as stream:
            stream.write(b'changed')
        with self.assertRaisesRegex(ValueError, 'checksum mismatch'):
            self.verify()

    def test_changed_inventory(self):
        self.metadata['files'][release.PACKAGE] = '0' * 64
        self.save_metadata()
        with self.assertRaisesRegex(ValueError, 'inventory/checksum'):
            self.verify()

    def test_extra_remote_asset(self):
        (self.directory / 'extra.exe').write_bytes(b'unknown')
        with self.assertRaisesRegex(ValueError, 'release assets'):
            self.verify()

    def test_no_scripts_in_release_archive(self):
        self.files['install.ps1'] = b'unknown commands'
        self.save()
        with self.assertRaisesRegex(ValueError, 'Unexpected release file'):
            self.verify()

    def test_reject_unsafe_and_case_duplicate_paths(self):
        for names in (('../escape.appx',), ('/escape.appx',), ('C:/escape.appx',), ('a\\escape.appx',), ('Same.appx', 'same.appx')):
            with self.subTest(names=names):
                output = io.BytesIO()
                with zipfile.ZipFile(output, 'w') as archive:
                    for name in names:
                        entry = zipfile.ZipInfo(name)
                        # ZipInfo normalizes separators on Windows at construction.
                        # Preserve the raw hostile name to exercise received archives.
                        entry.filename = name
                        archive.writestr(entry, b'fixture')
                with zipfile.ZipFile(io.BytesIO(output.getvalue())) as archive:
                    with self.assertRaisesRegex(ValueError, 'Unsafe or duplicate'):
                        release.safe_archive(archive)

    def test_canonical_four_part_tag(self):
        self.assertEqual(release.version_tag('1.2.3.4'), 'v1.2.3.4')
        for version in ('1.2.3', '01.2.3.4', '1.2.3.65536', '1.2.3.4;command'):
            with self.assertRaises(ValueError):
                release.version_tag(version)


if __name__ == '__main__':
    unittest.main()
