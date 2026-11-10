"""Port native XAML styles from the locally recovered MixRadio XBF object trees.

The application checks in the result; it never needs the reference package at runtime.
Only native styles and their resources are selected here. Navidrome data bindings live
in the application's templates, separately from this mechanical XBF reconstruction.
"""

import argparse
import copy
import json
from pathlib import Path
from xml.sax.saxutils import escape, quoteattr


ROOT = Path(__file__).resolve().parents[2]
REFERENCE = ROOT / ".tmp/mixradio-audit/pc/xbf-metadata"
SELECTION = {
    "Common/NokiaMusicClient.json": {
        "LayoutRootStyle", "GridViewItemStyle", "VerticalGridViewStyle", "SearchBoxStyle",
        "NullButtonStyle", "NullButtonStyleWithPress", "NullButtonStyleWithPressAndDisable",
        "BasicTextStyle", "SmallRegularText", "SmallGreyText", "BasicGreyText", "NormalLightText",
        "NormalBoldText", "NormalSemiLightText", "NormalText", "MediumLightText", "Medium2LightText",
        "LargeLightGreyText", "ExtraLargeLightText", "ExtraLargeLightHeaderText",
        "ExtraLargeLightSubHeaderText", "SubtitleText", "NormalSemiLightHyperlinkText",
        "ButtonBase", "TransportButton", "PlayButton", "NowPlaying", "StandardButtonStyle",
        "SeeAllButtonStyle", "SeeAllMyMusicButtonStyle", "AddFavouriteArtistButtonStyle",
        "MainProgressBarStyle", "SeekSliderStyle", "LyricToggleButtonStyle",
    },
    "Common/StandardStyles.json": {"BackButtonStyle", "PageHeaderTextStyle", "SubheaderTextStyle"},
    "Common/DesignAssets.json": set(),
    "Views/MyMusic/MyMusicView.json": {
        "FilterListItemStyle", "MyMusicFilterButtonStyle",
        "MyMusicItemContainerZoomedInAlbumStyle", "MyMusicItemContainerZoomedInSongStyle",
    },
    "Views/Search/SearchResultsView.json": {"TextRadioButtonStyle"},
}
PRIMITIVES = {"Color", "SolidColorBrush", "Double", "Thickness", "FontFamily"}
ATTACHED = {
    "Grid": {"Row", "Column", "RowSpan", "ColumnSpan"},
    "Canvas": {"Left", "Top", "ZIndex"},
    "Storyboard": {"TargetName", "TargetProperty"},
    "ScrollViewer": {
        "HorizontalScrollBarVisibility", "VerticalScrollBarVisibility", "HorizontalScrollMode",
        "VerticalScrollMode", "ZoomMode", "IsHorizontalScrollChainingEnabled",
        "IsVerticalScrollChainingEnabled",
    },
    "AutomationProperties": {"Name", "HelpText", "LabeledBy", "AutomationId", "ItemType", "AccessibilityView"},
    "ToolTipService": {"ToolTip"},
    "VisualStateManager": {"VisualStateGroups"},
}


def property_value(obj, name):
    prop = next((p for p in obj["properties"] if p["name"] == name), None)
    return prop["values"][0].get("value") if prop and prop["values"] else None


def resource_key(obj):
    return property_value(obj, "Key") or property_value(obj, "Name")


def objects(obj):
    yield obj
    for prop in obj["properties"]:
        for value in prop["values"]:
            if "object" in value:
                yield from objects(value["object"])


def scalar(value):
    if value is None:
        return "{x:Null}"
    if isinstance(value, bool):
        return str(value)
    if isinstance(value, float):
        return format(value, ".8g")
    if isinstance(value, (list, tuple)):
        return ",".join(scalar(item) for item in value)
    if isinstance(value, dict) and "unit" in value:
        return {0: "Auto", 1: scalar(value["length"]),
                2: "*" if value["length"] == 1 else scalar(value["length"]) + "*"}[value["unit"]]
    if isinstance(value, dict):
        raise ValueError("Unsupported scalar: " + repr(value))
    return str(value)


def markup(obj):
    kind = obj["type"].lstrip("!")
    if kind == "Null":
        return "{x:Null}"
    if kind in {"StaticResource", "ThemeResource"}:
        return "{" + kind + " " + str(property_value(obj, "ResourceKey")) + "}"
    if kind == "TemplateBinding":
        return "{TemplateBinding " + str(property_value(obj, "Property")) + "}"
    if kind == "RelativeSource":
        return "{RelativeSource " + str(property_value(obj, "Mode")) + "}"
    if kind == "Binding":
        parts = []
        for prop in obj["properties"]:
            if not prop["values"]:
                continue
            value = prop["values"][0]
            rendered = markup(value["object"]) if "object" in value else scalar(value.get("value"))
            if rendered is None:
                raise ValueError("Unsupported binding value: " + repr(value))
            parts.append(prop["name"] + "=" + rendered)
        return "{Binding " + ", ".join(parts) + "}"
    return None


def render(obj, metadata, level=1, resource=False):
    kind = obj["type"].lstrip("!")
    tag = "x:" + kind if kind in {"Double", "String", "Int32", "Boolean"} else kind
    attributes, properties, children = [], [], []
    for prop in obj["properties"]:
        name, values = prop["name"], prop["values"]
        if name in {"ConnectionId", "Class", "FieldModifier"}:
            continue
        if name == "__DeferredStoryboard":
            name = "Storyboard"
        if name.startswith("__implicit") or (name == "Template" and kind in {"ControlTemplate", "DataTemplate", "ItemsPanelTemplate"}):
            children.extend(values)
            continue
        if name in {"Key", "Name", "Uid"} and prop.get("flags", 0) & 2:
            name = "x:" + ("Key" if resource and name == "Name" else name)
        owner = metadata["strings"][metadata["types"][prop["ownerTypeId"]][2]]
        if prop["name"] in ATTACHED.get(owner, set()) or owner == "Typography":
            name = owner + "." + prop["name"]
        if len(values) == 1 and "value" in values[0]:
            raw = values[0]["value"]
            text = scalar(raw)
            if name in {"Duration", "KeyTime", "GeneratedDuration"} and isinstance(raw, (float, int)):
                text = "0:0:" + format(raw, ".7g")
            if name == "x:Uid":
                text = text.strip()
            attributes.append(name + "=" + quoteattr(text))
        elif len(values) == 1 and "object" in values[0] and markup(values[0]["object"]) is not None:
            attributes.append(name + "=" + quoteattr(markup(values[0]["object"])))
        else:
            properties.append((name, values))
    pad = "    " * level
    result = pad + "<" + tag + (" " + " ".join(attributes) if attributes else "")
    if not properties and not children:
        return result + " />\n"
    result += ">\n"

    def render_values(values, depth, flatten=True):
        output = ""
        for value in values:
            if "object" not in value:
                output += "    " * depth + escape(scalar(value.get("value"))) + "\n"
            # Transition properties can inherit a sealed default collection.
            # Assign a new collection instead of appending to that shared value.
            elif flatten and value["object"]["type"].endswith("Collection") and value["object"]["type"] != "TransitionCollection":
                for prop in value["object"]["properties"]:
                    output += render_values(prop["values"], depth)
            else:
                output += render(value["object"], metadata, depth)
        return output

    for name, values in properties:
        property_tag = name if "." in name else tag + "." + name
        result += pad + "    <" + property_tag + ">\n"
        result += render_values(values, level + 2, flatten=name != "Value")
        result += pad + "    </" + property_tag + ">\n"
    result += render_values(children, level + 1)
    return result + pad + "</" + tag + ">\n"


def export(reference):
    resources, by_key, sources = [], {}, {}
    for filename in SELECTION:
        metadata = json.loads((reference / filename).read_text(encoding="utf-8-sig"))
        for obj in objects(metadata["objects"][0]):
            key = resource_key(obj)
            if key and key not in by_key:
                by_key[key] = (obj, metadata, filename)
        sources[filename] = metadata
    selected = set().union(*SELECTION.values())
    for value in sources["Common/NokiaMusicClient.json"]["objects"][0]["properties"][0]["values"]:
        if value["object"]["type"] in PRIMITIVES:
            selected.add(resource_key(value["object"]))
    pending = list(selected)
    while pending:
        key = pending.pop()
        if key not in by_key:
            raise ValueError("Missing recovered resource: " + key)
        for child in objects(by_key[key][0]):
            dependency = property_value(child, "ResourceKey") if child["type"] == "StaticResource" else None
            if dependency in by_key and dependency not in selected:
                selected.add(dependency)
                pending.append(dependency)
    emitted = set()

    def emit(key):
        if key in emitted:
            return
        emitted.add(key)
        obj, metadata, filename = by_key[key]
        for child in objects(obj):
            dependency = property_value(child, "ResourceKey") if child["type"] == "StaticResource" else None
            if dependency in selected:
                emit(dependency)
        obj = copy.deepcopy(obj)
        if obj["type"] == "TextBlock":
            # Original strings were localized by these UIDs. Keep that contract;
            # the client's English resources provide the retained action labels.
            obj["properties"] = [p for p in obj["properties"] if p["name"] != "Inlines"]
        xml = render(obj, metadata, resource=True)
        if key in {"MyMusicItemContainerZoomedInAlbumStyle", "MyMusicItemContainerZoomedInSongStyle", "MyMusicFilterButtonStyle"}:
            # The recovered templates declare focus rectangles but leave Focused
            # empty. Connect them to keyboard focus in the native desktop port.
            focused = '''<VisualState x:Name="Focused">
                                            <Storyboard>
                                                <DoubleAnimation Storyboard.TargetName="FocusVisualWhite" Storyboard.TargetProperty="Opacity" To="1" Duration="0" />
                                                <DoubleAnimation Storyboard.TargetName="FocusVisualBlack" Storyboard.TargetProperty="Opacity" To="1" Duration="0" />
                                            </Storyboard>
                                        </VisualState>'''
            if '<VisualState x:Name="Focused" />' in xml:
                xml = xml.replace('<VisualState x:Name="Focused" />', focused)
                height = '145' if key.endswith('AlbumStyle') else '80'
                width = '275' if key.endswith('AlbumStyle') else '280'
                for rectangle in ['FocusVisualWhite', 'FocusVisualBlack']:
                    xml = xml.replace('<Rectangle x:Name="' + rectangle + '"',
                                      '<Rectangle x:Name="' + rectangle + '" Width="' + width + '" Height="' + height + '" Margin="0,0,24,0"')
            else:
                xml = xml.replace('</VisualStateManager.VisualStateGroups>',
                                  '<VisualStateGroup x:Name="FocusStates">' + focused +
                                  '<VisualState x:Name="Unfocused" /><VisualState x:Name="PointerFocused" /></VisualStateGroup>\n'
                                  '                            </VisualStateManager.VisualStateGroups>')
        resources.append("    <!-- " + filename + ": " + key + " -->\n" + xml)
    for key in sorted(selected):
        emit(key)
    # Original transport vectors are ContentTemplates on SmartButton. Move each
    # vector into the recovered native ButtonBase template so its pressed and
    # disabled foreground states also reach the vector after the data-model port.
    vectors = {
        "Previous": ("M3.493,0 L0,0 L0,18.759001 L3.493,18.759001 L3.493,0 z M5.8280029,9.7940025 L21.069004,0.067001343 L21.069004,19.415001 L5.8280029,9.7940025 z", "21.069", "19.415", "0"),
        "Play": ("M0,0 L0,24 L18,12 Z", "18", "24", "4,0,0,0"),
        "Pause": ("M10.787007,23.449001 L15.568007,23.449001 L15.568007,0 L10.787007,0 L10.787007,23.449001 z M0,0 L4.7810001,0 L4.7810001,23.448999 L0,23.448999 L0,0 z", "15.568", "23.449", "0"),
        "Next": ("M17.576019,18.757999 L21.069019,18.757999 L21.069019,0 L17.576019,0 L17.576019,18.757999 z M0,19.414001 L0,0.067001343 L15.241001,9.7930031 L0,19.414001 z", "21.069", "19.414", "0"),
    }
    base, metadata, _ = by_key["ButtonBase"]
    template = next(o for o in objects(base) if o["type"] == "ControlTemplate")
    for name, (data, width, height, margin) in vectors.items():
        xml = render(template, metadata, level=4)
        import re
        xml = re.sub(r'<ContentPresenter x:Name="Content"[^>]*/>',
                     '<Path x:Name="Content" Data=' + quoteattr(data) + ' Fill="{TemplateBinding Foreground}" '
                     'Width="' + width + '" Height="' + height + '" Margin="' + margin + '" '
                     'UseLayoutRounding="False" Stretch="Fill" HorizontalAlignment="Center" VerticalAlignment="Center" />', xml)
        xml = xml.replace('Storyboard.TargetName="Content" Storyboard.TargetProperty="Foreground"',
                          'Storyboard.TargetName="Content" Storyboard.TargetProperty="Fill"')
        resources.append('    <!-- Native vector adaptation of PlayerTransportControls.json / ButtonBase. -->\n'
                         '    <Style x:Key="MixRadio' + name + 'TransportButtonStyle" TargetType="Button" BasedOn="{StaticResource TransportButton}">\n'
                         '        <Setter Property="Template"><Setter.Value>\n' + xml +
                         '        </Setter.Value></Setter>\n    </Style>\n')
    return ('<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"\n'
            '    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n'
            '    <!-- Reconstructed by tools/mixradio/port_styles.py from MixRadio 2.2.0.7933. -->\n\n'
            + "\n".join(resources) + '</ResourceDictionary>\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference", type=Path, default=REFERENCE)
    parser.add_argument("--output", type=Path, default=ROOT / "Client/Common/MixRadioOriginalStyles.xaml")
    args = parser.parse_args()
    source = export(args.reference)
    args.output.write_text(source, encoding="utf-8")
    print("Ported recovered styles to " + str(args.output))


if __name__ == "__main__":
    main()
