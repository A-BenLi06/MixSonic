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
static_lookups = 0
xaml_namespace = '{http://schemas.microsoft.com/winfx/2006/xaml}'


def resource_keys(path, visited=None):
    visited = set() if visited is None else visited
    path = path.resolve()
    if path in visited:
        return set()
    visited.add(path)
    tree = ET.parse(path)
    keys = {node.get(xaml_namespace + 'Key') for node in tree.iter() if node.get(xaml_namespace + 'Key')}
    for node in tree.iter():
        source = node.get('Source') if node.tag.endswith('ResourceDictionary') else None
        if not source:
            continue
        if source.startswith('ms-appx:///'):
            merged = client / source[len('ms-appx:///'):]
        elif '://' in source:
            continue
        else:
            merged = client / source.lstrip('/') if source.startswith('/') else path.parent / source
        if not merged.is_file():
            issues.append('Missing merged resource dictionary: ' + str(merged))
        else:
            keys.update(resource_keys(merged, visited))
    return keys


def audit_names(node, path, scope=None):
    tag = node.tag.split('}')[-1]
    if scope is None or tag in ('DataTemplate', 'ControlTemplate', 'ItemsPanelTemplate', 'ResourceDictionary'):
        scope = set()
    name = node.get(xaml_namespace + 'Name')
    if name:
        if name in scope:
            issues.append('Duplicate XAML name: ' + name + ' in ' + path)
        scope.add(name)
    for child in node:
        audit_names(child, path, scope)


application_keys = resource_keys(client / 'App.xaml')
for path in list((client / 'Navidrome').iterdir()) + list((client / 'Settings').glob('SettingsView*')):
    if path.suffix not in ('.cs', '.xaml'):
        continue
    relative = path.relative_to(client).as_posix()
    if relative not in included:
        issues.append('Missing project entry: ' + relative)
    source = path.read_text(encoding='utf-8-sig')
    if path.suffix == '.xaml':
        audit_names(ET.parse(path).getroot(), relative)
        visible_keys = application_keys | resource_keys(path)
        for key in re.findall(r'\{StaticResource\s+((?:Navidrome|MixRadio)[^}\s]+)\s*\}', source):
            static_lookups += 1
            if key not in visible_keys:
                issues.append('Missing project resource: ' + key + ' in ' + relative)
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
print('PASS:', lookups, 'desktop string lookups,', static_lookups,
      'project resource references, XAML names and all Navidrome project entries. This is a source audit, not a native UI pass.')
