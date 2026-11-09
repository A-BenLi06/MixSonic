"""Audit resource lookup and project inclusion without claiming native acceptance."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[2]
client = root / 'Client'
resources = {data.get('name') for data in ET.parse(client / 'Strings/en-US/Resources.resw').findall('data')}
project = ET.parse(client / 'Client.csproj')
included = {node.get('Include', '').replace('\\', '/') for node in project.iter()}
issues = []
lookups = 0
for path in list((client / 'Navidrome').iterdir()) + list((client / 'Settings').glob('SettingsView*')):
    if path.suffix not in ('.cs', '.xaml'):
        continue
    relative = path.relative_to(client).as_posix()
    if relative not in included:
        issues.append('Missing project entry: ' + relative)
    source = path.read_text(encoding='utf-8-sig')
    for key in re.findall(r'NavidromeText\.Get\("([^"]+)"\)', source):
        lookups += 1
        if 'Navidrome_Text_' + key not in resources:
            issues.append('Missing string: ' + key + ' in ' + relative)
    for key in re.findall(r'x:Uid="(Navidrome_[^"]+)"', source):
        lookups += 1
        if not any(value.startswith(key + '.') for value in resources):
            issues.append('Missing property resource: ' + key + ' in ' + relative)
for key in ('albums', 'artists', 'songs', 'genres', 'playlists', 'radio', 'activity', 'random', 'favourites', 'search', 'bookmarks', 'shares'):
    if 'Navidrome_Text_Section' + key not in resources:
        issues.append('Missing section title: ' + key)
if issues:
    raise SystemExit('\n'.join(issues))
print('PASS:', lookups, 'desktop resource lookups and all Navidrome project entries. This is a source audit, not a native UI pass.')
