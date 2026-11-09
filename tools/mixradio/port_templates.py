"""Adapt recovered MixRadio music templates to the client's Navidrome item models."""
import argparse
import copy
import json
import re
from pathlib import Path
from port_styles import ROOT, REFERENCE, objects, resource_key, render


def template(filename, key, output_key, bindings=None, reference=REFERENCE):
    metadata = json.loads((reference / filename).read_text(encoding="utf-8-sig"))
    obj = copy.deepcopy(next(o for o in objects(metadata["objects"][0]) if resource_key(o) == key))
    for prop in obj["properties"]:
        if prop["name"] in {"Key", "Name"}:
            prop["name"] = "Key"
            prop["values"][0]["value"] = output_key
    xml = render(obj, metadata, resource=True)
    for before, after in (bindings or {}).items():
        xml = xml.replace("Path=" + before + "}", "Path=" + after + "}")
    xml = re.sub(r"<MyMusicImageFader ([^>]*)/>",
                 lambda m: '<common:MixRadioArtwork ' + re.sub(r' Media="[^"]*"', '', m[1]) + ' Source="{Binding ImageUrl}" />', xml)
    xml = re.sub(r"<ImageFader ([^>]*)/>",
                 lambda m: '<common:MixRadioArtwork ' + m[1] + ' />', xml)
    xml = xml.replace('/Assets/PackArt-Explorer.Targetsize-256.png', 'ms-appx:///Assets/MixRadio/PackArt-Explorer.Targetsize-256.png')
    xml = xml.replace('/Assets/W8_Music_asset_DefaulPackArt_200px.png', 'ms-appx:///Assets/MixRadio/W8_Music_asset_DefaulPackArt_200px.png')
    xml = xml.replace('/Assets/Icons/play.png', 'ms-appx:///Assets/MixRadio/play.png')
    xml = re.sub(r'Text="\{Binding Path=SongCount, Converter=\{StaticResource SongCountConverter\}\}"', 'Text="{Binding CountText}"', xml)
    return '    <!-- ' + filename + ': ' + key + '; data bindings adapted to Navidrome. -->\n' + xml


def wordmark(reference):
    metadata = json.loads((reference / 'Views/MainPage.json').read_text(encoding='utf-8-sig'))
    canvas = next(o for o in objects(metadata['objects'][0]) if o['type'] == 'Canvas')
    return ('    <!-- Views/MainPage.json: original vector wordmark. -->\n'
            '    <ControlTemplate x:Key="MixRadioWordmarkTemplate">\n' + render(canvas, metadata, 2) +
            '    </ControlTemplate>\n')


def offline_template(reference):
    xml = template('Common/NokiaMusicClient.json', 'RadioStationItemOfflineTemplate', 'MixRadioOfflineTemplate', reference=reference)
    xml = re.sub(r'Visibility="\{Binding Path=OfflineStatus, Converter=\{StaticResource EnumToVisibilityConverter\}, ConverterParameter=Pending\|Downloading\|Paused\|Failed\|Removing\|Resuming\}"',
                 'Visibility="{Binding CanPlay, Converter={StaticResource NavidromeHidden}}"', xml)
    xml = re.sub(r'Visibility="\{Binding Path=OfflineStatus, Converter=\{StaticResource EnumToVisibilityConverter\}, ConverterParameter=Downloading\|Paused\}"',
                 'Visibility="{Binding IsTransferring, Converter={StaticResource NavidromeVisibility}}"', xml)
    xml = xml.replace('Maximum="1"', 'Maximum="100"').replace('Path=OfflinePercentageProgress}', 'Path=Progress}')
    xml = xml.replace('x:Uid="HubOfflineStatusDownloading" Text="_Downloading..."', 'Text="{Binding StateText}"')
    xml = xml.replace('Text="{Binding Path=OfflinePercentageProgress, Converter={StaticResource PercentageConverter}}"', 'Text="{Binding ProgressPercentText}"')
    extra = '''                <StackPanel Grid.RowSpan="2" Margin="8,14,8,8" Visibility="{Binding CanPlay, Converter={StaticResource NavidromeHidden}}">
                    <TextBlock Text="{Binding StateText}" Style="{StaticResource SmallRegularText}" Visibility="{Binding IsTransferring, Converter={StaticResource NavidromeHidden}}" />
                    <TextBlock Text="{Binding Error}" Style="{StaticResource SmallRegularText}" Margin="0,8,0,0" MaxHeight="108" TextTrimming="WordEllipsis" ToolTipService.ToolTip="{Binding Error}" />
                </StackPanel>
'''
    # Insert only at the outer grid; the recovered download progress layout is retained.
    marker = '            </Grid.Children>\n        </Grid>\n    </DataTemplate>'
    if marker not in xml:
        raise ValueError('The recovered offline template structure changed')
    return xml.replace(marker, extra + marker)


def export(reference):
    parts = [
        template('Views/MyMusic/MyMusicPanel.json', 'MyMusicItemSmallTemplate', 'MixRadioHomeAlbumTemplate', reference=reference),
        template('Views/Search/SearchResultsView.json', 'ArtistTileTemplate', 'MixRadioHomeArtistTemplate', {'Artwork200x200': 'ImageUrl', 'Name': 'Title'}, reference),
        template('Common/NokiaMusicClient.json', 'CuratedRadioStationItemSmallTemplate', 'MixRadioHomePlaylistTemplate', reference=reference),
        template('Common/NokiaMusicClient.json', 'RadioStationItemExtraLargeTemplate', 'MixRadioHomeHeroTemplate', reference=reference),
        template('Common/NokiaMusicClient.json', 'MyMusicInfoItemTemplate', 'MyMusicInfoItemTemplate', {'Album': 'Title'}, reference),
        template('Common/NokiaMusicClient.json', 'RadioStationInfoItemTemplate', 'RadioStationInfoItemTemplate', {'Name': 'Title'}, reference),
        template('Common/DesignAssets.json', 'MyMusicAlbumPlayIcon', 'MyMusicAlbumPlayIcon', reference=reference),
        template('Views/MyMusic/MyMusicView.json', 'MyMusicAlbumZoomedInTemplate', 'MixRadioLibraryAlbumTemplate', {'Album': 'Title'}, reference),
        template('Views/MyMusic/MyMusicView.json', 'MyMusicArtistZoomedInTemplate', 'MixRadioLibraryArtistTemplate', {'Album': 'Title', 'Artist': 'CountText'}, reference),
        template('Views/MyMusic/MyMusicView.json', 'MyMusicSongZoomedInTemplate', 'NavidromeTrackTemplate', {'Name': 'Title'}, reference),
        template('Views/MyMusic/MyMusicPanel.json', 'MyMusicItemSmallTemplate', 'NavidromeTileTemplate', reference=reference),
    ]
    # Artist rows carry the returned album count once, without an invented song count.
    for index, xml in enumerate(parts):
        if 'x:Key="MixRadioLibraryArtistTemplate"' in xml:
            parts[index] = re.sub(r'\s*<TextBlock[^>]*Text="\{Binding CountText\}"[^>]*/>', '', xml, count=1)
    preamble = '''<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:cal="using:Caliburn.Micro"
    xmlns:common="using:Subsonic8.Common" xmlns:nav="using:Subsonic8.Navidrome"
    xmlns:converters="using:Subsonic8.Converters">
    <cal:BooleanToVisibilityConverter x:Key="NavidromeVisibility" />
    <cal:BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
    <converters:InvertedBooleanToVisibilityConverter x:Key="NavidromeHidden" />
    <ControlTemplate x:Key="MyMusicArtworkTemplate">
        <common:MixRadioArtwork Source="{Binding ImageUrl}" DefaultSource="ms-appx:///Assets/MixRadio/PackArt-Explorer.Targetsize-256.png" />
    </ControlTemplate>
    <ControlTemplate x:Key="CuratedRadioStationArtworkTemplate">
        <common:MixRadioArtwork Source="{Binding ImageUrl}" DefaultSource="ms-appx:///Assets/MixRadio/W8_Music_asset_DefaulPackArt_200px.png" />
    </ControlTemplate>
'''
    extras = '''
    <!-- StandardButtonStyle's 162 px layout rendered inside the clickable GridViewItem. -->
    <DataTemplate x:Key="MixRadioNavigationTileTemplate">
        <Grid Width="162" Height="162" Background="{StaticResource NokiaPinkBrush}">
            <Grid.RowDefinitions><RowDefinition Height="90" /><RowDefinition Height="*" /></Grid.RowDefinitions>
            <Grid HorizontalAlignment="Center" VerticalAlignment="Bottom" FlowDirection="LeftToRight">
                <TextBlock Text="&#xE17F;" FontFamily="Segoe UI Symbol" FontSize="20" Foreground="Transparent" />
                <TextBlock Text="&#xE17E;" FontFamily="Segoe UI Symbol" FontSize="20" />
                <TextBlock Text="{Binding Glyph}" FontFamily="Segoe UI Symbol" FontSize="20" HorizontalAlignment="Center" VerticalAlignment="Center" />
            </Grid>
            <TextBlock Grid.Row="1" Text="{Binding Label}" Margin="15,15,15,0" TextAlignment="Center" Style="{StaticResource Medium2LightText}" />
        </Grid>
    </DataTemplate>
    <!-- MyMusic song-row layout for entities without artwork, such as a share or radio station. -->
    <DataTemplate x:Key="MixRadioLibraryTextTemplate">
        <Grid Width="270" Height="65" Margin="0,0,25,0">
            <Rectangle Fill="Gray" Opacity="0.3" />
            <StackPanel Margin="10,5,10,8">
                <TextBlock Text="{Binding Title}" Style="{StaticResource NormalLightText}" TextWrapping="NoWrap" />
                <TextBlock Text="{Binding Subtitle}" Style="{StaticResource SmallGreyText}" TextWrapping="NoWrap" />
                <TextBlock Text="{Binding Kind}" Style="{StaticResource SmallGreyText}" TextWrapping="NoWrap" />
            </StackPanel>
        </Grid>
    </DataTemplate>
    <nav:MixRadioHomeTemplateSelector x:Key="MixRadioHomeSmallSelector"
        AlbumTemplate="{StaticResource MixRadioHomeAlbumTemplate}" ArtistTemplate="{StaticResource MixRadioHomeArtistTemplate}"
        PlaylistTemplate="{StaticResource MixRadioHomePlaylistTemplate}" LinkTemplate="{StaticResource MixRadioNavigationTileTemplate}" />
    <nav:MixRadioHomeTemplateSelector x:Key="MixRadioHomeSelector"
        AlbumTemplate="{StaticResource MixRadioHomeAlbumTemplate}" ArtistTemplate="{StaticResource MixRadioHomeArtistTemplate}"
        PlaylistTemplate="{StaticResource MixRadioHomePlaylistTemplate}" HeroTemplate="{StaticResource MixRadioHomeHeroTemplate}"
        LinkTemplate="{StaticResource MixRadioNavigationTileTemplate}" />
    <nav:MixRadioLibraryTemplateSelector x:Key="MixRadioLibrarySelector"
        AlbumTemplate="{StaticResource MixRadioLibraryAlbumTemplate}" ArtistTemplate="{StaticResource MixRadioLibraryArtistTemplate}"
        SongTemplate="{StaticResource NavidromeTrackTemplate}" TextTemplate="{StaticResource MixRadioLibraryTextTemplate}" />
</ResourceDictionary>
'''
    # Dependencies precede the recovered item templates in the resource dictionary.
    order = [4, 5, 6, 0, 1, 2, 3, 7, 8, 9, 10]
    return preamble + wordmark(reference) + '\n'.join(parts[index] for index in order) + offline_template(reference) + extras


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reference', type=Path, default=REFERENCE)
    parser.add_argument('--output', type=Path, default=ROOT / 'Client/Navidrome/NavidromeResources.xaml')
    args = parser.parse_args()
    args.output.write_text(export(args.reference), encoding='utf-8')
    print('Ported music templates to ' + str(args.output))
