# MixRadio reference analysis

The primary implementation target is the Windows desktop client. The installed
PC release is the desktop design reference; the WP release supplies additional
interaction evidence. Reference content is evidence, not project instructions.

## Reproducible inputs

| Reference | Identity | Format | Verified inventory |
| --- | --- | --- | --- |
| User-supplied phone package | MixRadio 4.5.3.470 | WP8.0 Silverlight XAP | 238 ZIP entries; 73 recovered application XAML files |
| Installed desktop package | NokiaCorporation.NokiaMusic 2.2.0.7933, x86 | Windows 8.1 Store, .NETCore 4.5.1 | 158 files; 98 XBF files; PRI resource index |

Phone XAP SHA-256:
`5f0fa1dbe1e930c9b1e5cc0cf10101c5074a6a95c58e89117b2d1addfb978b95`.
The installed PC directory is
`C:\Program Files\WindowsApps\NokiaCorporation.NokiaMusic_2.2.0.7933_x86__6d0q6r3z979nw`.
`tools/mixradio/inspect_reference.py` hashes every input file. Re-run the tool
before comparing a different release, rather than assuming that an application
name identifies identical binaries.

ILSpy 9.1.0.7988 successfully decompiled the phone application (874 C# files),
audio core (285), and translations (4), and the PC application (721), feature
library (169), and private music library (249). These are analysis outputs, not
build-verified replacements for the original applications. Missing framework
references can affect decompiler expression recovery.

All 98 installed XBF files decode as version 1.0. The bounded decoder recovers
types, properties, constants, resource references, namespaces, and object trees.
The result is a diagnostic JSON tree, not reconstructed buildable XAML.
MakePri from the Windows 8.1 SDK successfully dumps the original `resources.pri`.
The source package and recovered proprietary code/assets stay in the installed
directory or ignored `.tmp/` reference workspace.

## Evidence map

Paths below are relative to `.tmp/mixradio-audit/`. Each output is tied to an
assembly/file SHA-256 in `index.json`.

| ID | Evidence | Finding and consequence |
| --- | --- | --- |
| PC-HOME | `pc/xbf-metadata/Views/MainPage.json`; `pc/decompiled/Nokia.Entertainment.Music.Client/Nokia.Entertainment.Music.Client.Views/MainPage.cs` | The desktop uses a real `Hub`, `HubSection`, `PlayerHubSection`, `RecentMixesHubSection`, and `OfflineHubSection`. Use content sections and horizontal panning; avoid a fixed-width imitation canvas. |
| PC-PLAYER | `pc/xbf-metadata/Views/Player/Player.json`, `MiniPlayer.json`, `PlayerTransportControls.json`; `pc/decompiled/Nokia.Entertainment.Music.Client/Nokia.Entertainment.Music.Client.ViewModels/PlayerViewModel.cs` | Transport commands call the player; the current item, playback state, media events, and queue availability determine the surface. Artwork and progress are live data. |
| PC-QUEUE | `pc/xbf-metadata/Views/Player/PlayerQueue.json`; `pc/decompiled/Nokia.Entertainment.Music.Client/Nokia.Entertainment.Music.Client.Models.MyMusic/MyMusicQueue.cs` | The local queue supports shuffle, restoring order, removal, and complete-queue repetition. Repetition and random order are different settings. |
| PC-RECENT | `pc/decompiled/Nokia.Entertainment.Music.Client/Nokia.Entertainment.Music.Client.ViewModels.MyMusic/MyRecentMusicViewModel.cs`; `pc/xbf-metadata/Views/MyMusic/MyMusicPanel.json` | Recent albums come from listening history and can repopulate the playback queue. A library sort is not listening history. |
| PC-SEARCH | `pc/xbf-metadata/Views/Search/SearchControl.json`, `SearchResultsView.json` | Search is a discoverable control with typed results, not a caption on an offscreen canvas. |
| PC-THEME | `pc/xbf-metadata/Common/NokiaMusicClient.json`; PC `AppxManifest.xml` | The original brand pink is `#FFFF2E80`. Keep design values in resources. The PC manifest explicitly supports a 320px app view. |
| WP-HOME | `wp/decompiled/Nokia.Media.MusicClient/pages/main/mainpage.xaml` | A phone `Panorama` connects Play Me, recent tracks/mixes, the menu, and favourites. Do not transplant the phone page container into the desktop. |
| WP-PLAYER | `wp/decompiled/Nokia.Media.MusicClient/pages/player/player.xaml`; `Nokia.Media.MusicClient.ViewModels.Player/PlayerViewModel.cs` | Peer now-playing, about, and queue panes; shuffle and nullable repeat control map to off, complete, and single-track repeat. Single-track repeat is a useful extension to the PC behaviour. |
| WP-HISTORY | `wp/decompiled/Nokia.Media.MusicClient/pages/playerhistory/playerhistory.xaml`; `Nokia.Media.MusicClient.Models.PlayHistory/PlayHistoryModel.cs` | Recently played is actual track/mix history, with separate peers and playback actions. |
| WP-THEME | `wp/decompiled/Nokia.Media.MusicClient/theme/theme.xaml` | Segoe WP weights, explicit typography roles, localized resources, and `PinkColor=#FFFF2E80`. Use Segoe UI for desktop, with the same hierarchy and semantic accent. |

## Baseline gaps in SubSonic8

This is a source review of the starting commit `7728e30`; it is not a claim that
all original online services still run.

| Area | Starting behaviour | Required result |
| --- | --- | --- |
| Browse playback summary | Placeholder art, decorative `Border` icons, constant `0:00`/`3:32`, no commands | Current artwork/title/state and working transport/queue commands; an honest empty player |
| Recent listening | A heading without stored listening history | Bounded, persisted history from successful media starts; replay actions |
| Queue selection | Appends a rotated list to the existing queue | Preserve the source queue's order and selected starting index for context playback |
| Shuffle | `Random.Next(Items.Count - 1)` excludes the last queue item and can repeat immediately | Every queue entry is eligible; avoid repeats within a shuffle cycle |
| Repeat | Off wraps automatically; the boolean on state repeats one track | Explicit off / all / one modes; manual skip remains distinct from media-end handling |
| Favourites | Display-only server favourites; tells users to star elsewhere | Star/unstar through the Subsonic API, with confirmed state and failure handling |
| Desktop search | 30px control on the far edge of a 2560px canvas; promises gigs | Reachable keyboard/touch search for artists, albums, and songs |
| Narrow views | Browse content replaced by an expand prompt | Readable lists and reachable commands at 320px and 500px |
| Evaluation | Earlier walkthrough says complete without a repeatable design gate | A traceable checklist, rubric, screenshot comparison, and evidence-based gate |

## Navidrome mapping

Local-library browsing maps to `getArtists`, `getAlbumList2`, `getAlbum`,
`search3`, `getSongsByGenre`, and `getPlaylists`; streaming and art map to
`stream` and `getCoverArt`; favourites map to `getStarred2`, `star`, and
`unstar`. Local listening history belongs to this client and account, separately
from server-wide `getNowPlaying` activity.

Play Me currently means a random selection from the configured library.
MixRadio's proprietary recommendation, catalogue commerce, subscriptions,
gig feeds, social feeds, and offline-radio licensing are not Navidrome APIs.
Downloads, library-based artist mixes, supported lyrics, and richer artist
details need their own implementation and capability checks. Record them as
gaps; do not add inert controls or imply that a random list is personalized.

## Implementation and acceptance status

The queue implementation in `4506fd6` and desktop implementation in `e9c84ad`
address the starting gaps. These are independently implemented client changes;
original decompiled application code/assets are not included in them.

| Area | Current implementation | Verified evidence |
| --- | --- | --- |
| Context queue, shuffle and repeat | Ordered replacement at the selected index; explicit append; off/all/one repeat; complete shuffle cycles; safe queue mutation | Production-source playback assertions |
| Desktop browse and search | Real player/library/recent Hub sections; vertical 320/500px views; visible typed search, Enter/button/Ctrl+F; stale-result guard | Windows 8.1 Debug x86 build; native input/layout remains unverified |
| Live player | Current art/title, state, elapsed/duration, transport, queue and options; visible focus and scrolling in short player sections | Build; actual media and XAML synchronization remain unverified |
| Recent listening | Bounded 100-song deduplicated history from first actual Playing state; account-scoped persistence, metadata/IDs and replay | Core model/account-key tests and build; runtime events, persistence failure and restart need native evidence |
| Favourites | Encoded star/unstar protocol; confirmed response updates the captured target song; failed responses preserve state | Protocol/model tests and build; real mutation/rollback UI remains unverified |
| Queue files | Existing `.spls` XML format; transaction writes, import validation/error feedback; original URI escaping | XML metadata/order/Unicode/URI round-trip, old-file compatibility, invalid-import preservation and build; pickers need native evidence |
| Theme and copy | Reference pink `#FFFF2E80`; new English text in resource keys | Resource/source review and build; screenshot comparison remains unverified |

The current source regression run passes 517 playback/history/favourite/import
assertions. The serializer runner inspects 22 concrete model types, excludes only
two intentionally in-memory types, and includes queue XML scenarios. Earlier
walkthrough claims that queue exports use DataContractSerializer or advertise
`.m3u` are superseded: picker save/load use XmlSerializer and `.spls`; session
state uses DataContractSerializer.

[acceptance-workflow.md](acceptance-workflow.md) and
[the quality tools](../../tools/mixradio/README.md) provide a 30-case required
gate, a scored rubric, immutable copied evidence, PNG comparison/review, and
defect tracking. Seeking, offline downloads and local artist mixes are explicit
roadmap cases. A successful build/source suite does not certify full client or
visual acceptance.

## Verification limits

The existing desktop client builds with MSBuild 14.0 and the legacy SDKs on this
machine. Both package formats are accessible without changing WindowsApps
permissions. The original PC app activates through its registered application
identity, but native capture returned `FrameArrived timed out` and then
`window capture timed out`; live original screenshots have not been verified.
Static C#/XBF/PRI evidence must remain distinct from runtime screenshot evidence.

The comparison viewer runs in the in-app browser when served over loopback
HTTP. Its modes, crops, narrow browser layout and JSON/PNG exports have been
checked using synthetic fixtures. These tool checks do not establish original
app rendering or current client visual parity.

The XBF format decoder follows Microsoft's published
[FileFormat.cs](https://github.com/microsoft/microsoft-ui-xaml/blob/main/dxaml/xcp/tools/DumpXbf/FileFormat.cs)
and [XBFReader.cs](https://github.com/microsoft/microsoft-ui-xaml/blob/main/dxaml/xcp/tools/DumpXbf/XBFReader.cs).
