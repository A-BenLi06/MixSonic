# Native MixRadio component port

The Windows 8.1 Store desktop client uses components recovered from the installed
`NokiaCorporation.NokiaMusic` PC package, version `2.2.0.7933`. Navidrome remains the
functional authority for library, account, playback and server operations.
This port replaces the home page's obsolete `BrowseView` inheritance with a
native Hub and connects its visible entries to the current services.

The original XBF object trees are kept locally under
`.tmp/mixradio-audit/pc/xbf-metadata`. The selected generated dictionaries and PNGs
are included in the project so a clean checkout can build without the original
package or the extraction tools. The complete recovered source, packages and
reference reports remain local.

## Component map

Source paths in this table are relative to the recovered PC metadata directory.

| Recovered component | Client implementation | Functional adaptation |
| --- | --- | --- |
| `Common/NokiaMusicClient.json`, `Common/StandardStyles.json` | `Client/Common/MixRadioOriginalStyles.xaml` | Original brushes, typography, back buttons, press/disabled states, search and transport styles; native buttons replace proprietary button types. |
| `Views/MainPage.json`, `Views/MyMusic/MyMusicPanel.json` | `Client/Main/MainView.*`, `Client/Navidrome/MixRadioHomePanel.*` | Hub layout, vector wordmark and 162 × 162 tiles display recent/new albums, playlists, artists, favourites and downloads. Explicit handlers dispatch section headers and item clicks. |
| Station templates in `Common/NokiaMusicClient.json` | `Client/Navidrome/NavidromeResources.xaml`, `LibraryPresentation.cs` | The 332 × 332 featured tile and smaller station tiles present Navidrome playlists. Empty sections retain a reachable “see all” entry. |
| `Views/MyMusic/MyMusicView.json`, `Views/Search/SearchResultsView.json` | `NavidromeBrowserView.*`, generated library templates | Recovered album/artist rows use 260 × 130 layouts; song rows use 270 × 65 layouts. The library menu retains every supported destination. |
| `Views/Player/Player.json`, `TrackDetails.json`, `PlayerTransportControls.json` | `MixRadioPlayerSurface.*`, `NavidromePlayerView.*` | Recovered 172 px grid units, artwork region and circular vector transport controls use the existing local playback, seeking, metadata and lyrics services. |
| `Views/Player/PlayerQueue.json` | `MixRadioQueuePanel.*` | The 300 px drawer, header, toolbar and 58 px entries bind to the actual local queue. The player page retains its queue editing and server save/restore commands. |
| `Views/Search/SearchControl.json` | `Client/Common/LibrarySearch.*` | Original search presentation connects to Navidrome search; keyboard activation is forwarded explicitly by the home and library pages. |
| `Views/MoreFromArtist/ArtistDetails.json` and recovered music/offline templates | `NavidromeDetailView.*`, `NavidromeDownloadsView.*` | Hub sections and offline tiles show server metadata, related media, real download state, progress and errors. |
| Recovered image-fader behavior and package artwork | `Client/Common/MixRadioArtwork.*`, `Client/Assets/MixRadio/` | Native image loading/fading uses current artwork URLs and the original fallback assets. Optional artist backgrounds come from `getArtistInfo2` through the existing API and use opacity 0.6. |

`NavidromeAccountView`, `NavidromeJukeboxView` and the Settings flyout also use the
recovered header, back-button, font and colour resources. Their existing
Navidrome-specific operations remain in their view models.

## Behavior and lifecycle

- Home exposes all 13 destinations: albums, artists, songs, genres, playlists,
  random songs, favourites, radio, activity, bookmarks, shares, downloads and
  account. Section headers and “see all” tiles share the same route definition.
- Album, playlist and song previews resolve through the existing queue service;
  artist previews open artist details. The embedded player shares the existing
  playback state and forwards its frame-state save/load contract.
- Completed downloads for the current account load before the connection attempt.
  One failed online section leaves other successful sections available.
- Pending tile playback is rejected when its account, library, page activation
  or reload generation becomes stale. A missing download cannot silently redirect
  the requested selection to a different song.
- Narrow windows change Hub orientation, tile flow and player placement. Featured
  playlists use the compact tile template there, and search remains available.
- Labels use the client's English resource file and existing localization lookup.
  Ordinary buttons have resource IDs for their supported native properties;
  AppBar `Label` resources are not applied to ordinary buttons.

## Reproduction and verification

Run the exporters from the repository root after preparing the local reference:

```powershell
python tools/mixradio/port_styles.py
python tools/mixradio/port_templates.py
```

`port_styles.py` selects recovered resource keys and their dependencies.
`port_templates.py` retains recovered template structure while substituting
Navidrome bindings and the native artwork control. Their outputs are checked in
as `MixRadioOriginalStyles.xaml` and `NavidromeResources.xaml`; edit the exporter
when changing a generated resource.

The [asset manifest](../../tools/mixradio/ported-assets.json) lists all eight copied
PNGs with original package-relative paths, destination paths and SHA-256 hashes.
Both exporter outputs were compared with the checked-in dictionaries, and each
PNG matched its original installed package file.

Use the following focused checks after changing the port:

```powershell
python tools/navidrome-tests/check-client.py
./tools/navidrome-tests/run-home.ps1
./tools/playback-tests/run.ps1
./tools/navidrome-tests/run-audio.ps1
./tools/desktop/run.ps1 -NoLaunch
```

The home harness verifies the production controller and presentation decisions
with controlled adapters and a loopback HTTP peer. The resource audit checks
localization properties, project inclusion, XAML names and resource references.
These checks and a successful desktop build do not establish native click
behavior, visual parity, media decoding or suspend/resume correctness.

## Startup repair and native captures

The indefinitely spinning home screen came from failed XAML construction. The
shell initialized the legacy `SearchPane` before creating a page with `SearchBox`;
Windows does not allow those two search integrations in the same view. The shell
now leaves search integration to the inline control, awaits initialization and
reports navigation failures through the existing error view. Its progress ring
follows initialization state and stops on both success and failure.

The first successful navigation also exposed an inherited, sealed transition
collection. Home item transitions now explicitly create a `TransitionCollection`,
and the style exporter preserves recovered transition collections. Zero grid
padding restores the full three rows of 170 px cells, including the last two
tiles. The exporter also resolves the original lyrics button's vector resources
from `Common/DesignAssets.json`. The resource audit checks these dependencies and
the incompatible search registration.

The native screenshot driver is opt-in and compiled into an explicit Debug build:

```powershell
./tools/desktop/run.ps1 -NativeUiProbe
```

It waits for page loading and artwork decoding, then exports the running XAML
tree through Windows 8.1 `RenderTargetBitmap`. Results are written to the app's
`LocalState/native-ui-probe/<UTC timestamp>/` directory, with `latest.json` in
the parent directory. The report records page state, decoded artwork, section
loading, item bounds and capture errors. The driver visits home, albums, player,
home again and a scrolled home view, then lays out the shell at 500 × 900. It uses
the configured account and production navigation/services. A saved startup page
other than home is captured without running the home navigation sequence.

Run a normal build after capture to remove the driver:

```powershell
./tools/desktop/run.ps1
```

The runner rebuilds when switching capture modes because MSBuild 14 can reuse an
executable after a conditional source item disappears. `-NoBuild` rejects a last
build marked as a probe. Normal application binaries exclude `NativeUiProbe`.

The 2026-11-10 18:32:58 +08:00 run produced six actual XAML PNGs. Every captured
page finished initialization/loading without an error, all six home sections
finished loading, and no observed remote image was awaiting decoding. All 88
home item bounds across four captures fit their grid; the six-entry groups retain
three complete rows. The screenshots and JSON are preserved locally under
`.tmp/ui-acceptance/2026-11-10/`. A subsequent normal-build launch at
20:13:31 +08:00 opened `MainView` and completed shell initialization successfully.

The recovered style dictionaries reproduce exactly and all eight copied PNGs
match the original package hashes. The captures were visually reviewed for the
wordmark, typography, accent, tile layout, library rows and transport/lyrics
controls. They establish the rendered client layout; they do not establish
pixel-for-pixel parity with a running original. Original-window capture remains
unavailable due to `window capture timed out: timed out waiting on channel`.
The 500 × 900 capture uses an in-app root size override rather than an OS window
resize, and navigation is driven programmatically rather than through clicks.
Native input, search/queue interactions, OS resizing and media/lifecycle behavior
retain their separate acceptance requirements. Existing functional and visual
acceptance reports are unchanged.

## Library refinement (2026-11-11)

The supplied original screenshot provides a 1744 × 1090 app-content reference
after cropping at (102, 33). Its lower-right notification is excluded from the
header/card comparison crops. Compare geometry and typography, not whole-frame
pixel differences: the original uses Chinese UI labels and a different library
ordering, while this client retains its English resources and server results.

The library now uses the recovered `MyMusicItemContainerZoomedInAlbumStyle` and
song counterpart, selected by media type. Album cards retain their 260 × 130
content and 130 px artwork; the original container establishes 307 × 147 cell
spacing. Artist entries use the pink, 3 px `MyMusicIndexZoomedInTemplate` border
and bottom-aligned `ExtraLargeLightSubHeaderText`. They open the existing artist
detail page. The original screenshot interleaves artist headings and albums;
the later index implementation below replaces those standalone artist entries
with grouped album cards.

Three rendered refinement rounds established the retained layout:

| Parameter group | Before | Retained change / observation |
| --- | --- | --- |
| Category heading | Inherited button weight, 12 px gap | Explicit Light weight, original 54 px spacer and 60 px filter grid. Search has its own reserved column. |
| Card spacing | General home item presenter; 293 × 138 cells | Recovered library containers; 307 × 147 cells and 47 / 17 px visible horizontal / vertical gaps. |
| Card text | Pixel-height caps could push the count below the card | At most three title lines, one artist line and the count; full title/artist tooltips. |
| Content anchor | First refinement at (127, 146) | Calibrated to the supplied screenshot's (130, 149). |
| Bottom spacing | Adding 3 px at the top left only 881 px for six 147 px cells | Reduced bottom padding from 20 to 16, restoring six rows at 1744 × 1090 with Load more visible. The intermediate five-row candidate was rejected. |
| Focus | Recovered Focused states were empty | White/black theme focus outlines are connected to keyboard focus; hover and selected frames keep the original geometry. |
| Category menu | Generic unmarked menu items | Recovered list-item style, current-category highlight and all 13 localized destinations. |

The first native attempt caught a XAML duration conversion failure in the newly
imported transition. The exporter now serializes `GeneratedDuration` as a time
value, like `Duration` and `KeyTime`. Both generated dictionaries still reproduce
from the local XBF metadata. Focus behavior is an explicit native adaptation of
the recovered templates.

The Debug-only probe now also captures the reference viewport, 900/500 px album
layouts, artists, songs, selection, keyboard focus and the category popup. It
records header/card/text bounds and focus state. It invokes the actual filter
button through its automation peer and selects another destination, verifying
the production menu handler. Hover and missing-artwork previews are controlled
states in real XAML; the artwork binding is restored after the preview. These
checks do not constitute physical mouse/touch input or OS window-resize tests.
Local round captures, the supplied-reference crops and the final verification
record are stored under `.tmp/ui-acceptance/2026-11-11/`. No global visual or
functional acceptance score is inferred from this refinement.

## Library indices (2026-11-11)

The eight supplied MixRadio screenshots establish the album/song letter grids,
artist/genre named indices, and pink outlined headers flowing through the same
columns as the music cards. The browser now builds those headers from actual
metadata. Album-title and song indices include `#`, `a`–`z`, and discovered
non-Latin initials. Numbers and symbols use `#`; accented Latin initials share
their base letter, while other scripts retain their original text element.
Empty groups appear only in the index, with a disabled gray tile.

Artist and genre browsing pages group albums using their album-artist and genre
metadata, including multiple values. Artist identifiers remain opaque and case
sensitive. Missing values use localized resources; server-provided names remain
unchanged. Genre cards use the recovered title/count/year template. Other album
sort modes keep their requested ordering without adding title-group headings.

Click an inline header or use the Index app-bar command to open the index.
Ctrl+minus and Ctrl+mouse-wheel-down also open it; selecting an enabled tile
scrolls to that exact header. Escape, the page Back button, or Ctrl+wheel-up return
to the music grid. Two bounded GridViews deliberately map index entries to their
inline header objects because the reference headers occupy ordinary card cells.
This is an explicit jump view; native SemanticZoom pinch gestures are not wired.
The index preserves keyboard focus and media selection, excludes headers from
media commands, and reflows into vertical scrolling at compact widths.

Normal browsing still requests 100 records per page. The first explicit index
request finishes the current metadata listing before showing disabled letters:
Subsonic has no complete title-initial endpoint, so the first page cannot prove
that a letter is empty. Album metadata is reused for artist and genre groupings,
avoiding per-artist detail requests. Metadata remains in memory for subsequent
jumps; artwork and item containers remain virtualized. Account, folder and page
version checks reject stale completions. Refresh discards the old index and
filtering rebuilds its targets. Large libraries can take longer on the first
index request, with the existing progress/error and navigation paths available.

The recovered templates remain reproducible through `port_templates.py`.
Character tiles retain 126 × 106 content; named tiles use a 158 px width, an
88 px label area and a 236 px pink panel. The default semantic artist asset is
reused unchanged. The index container supplies the observed 8 px cell gaps.
The recovered content panels cap song columns at seven rows and album columns
at ten, filling fewer rows where the viewport height requires it.
The Debug-only native probe now captures all four grouped/index/jump views,
compact indices, and selection while paging. Actual runtime evidence is kept
under `.tmp/ui-acceptance/2026-11-11/library-index/`; it establishes rendered XAML
and programmatic navigation, not physical mouse/touch gestures or OS resizing.
