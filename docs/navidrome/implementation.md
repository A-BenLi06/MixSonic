# Navidrome client implementation baseline

Navidrome defines the client's music, library, playback and account behavior.
MixRadio defines component composition, layout, typography, color and visual
interaction treatment. A MixRadio feature list cannot establish feature completeness.
The priority, retained from the preceding chat, is the Windows 8.1 Store C#/XAML
desktop client running on Windows 10. Shared protocol work also remains usable by
the Windows Phone 8.1 client; phone UI acceptance is a separate target.

The locked reference is [Navidrome f5b6805](https://github.com/navidrome/navidrome/tree/f5b6805b3150ed6e5b92d8f0fcf5174823ae9a2e).
Run [source.ps1](../../tools/navidrome/source.ps1) to obtain the same source under
ignored `.tmp/navidrome`. The source is GPL-3.0. The implementation uses the documented
protocol and independently written client code; upstream application code is not vendored.

## Source evidence and required behavior

Paths in this table are relative to the locked Navidrome checkout. The generated
reference inventory hashes their actual bytes and records each route registration.

| Area | Navidrome evidence | Desktop client requirement |
| --- | --- | --- |
| Connection | `server/subsonic/middlewares.go`, `system.go`, `opensubsonic.go` | Encoded credentials/IDs, actual app identity, successful ping, reported server/extension capabilities, recoverable authentication errors |
| Libraries | `browsing.go`, `helpers.go`; `ui/src/common/LibrarySelector.jsx` | Use accessible server music-folder IDs; retain selection across browsing and search |
| Albums | `album_lists.go`; `ui/src/album/albumLists.js` | Complete pagination; alphabetical, artist, newest, recent, frequent, random, starred, rating, year and genre lists |
| Artists, songs and genres | `browsing.go`, `searching.go`, `album_lists.go` | Full song browsing, typed paged search, artist albums, genre songs; no silent 20/500-item truncation |
| Media details | `responses/responses.go`, `browsing.go` | IDs, track/disc, year/genre, album/artist links, quality metadata, multi-artist values when supplied |
| Favorites and ratings | `media_annotation.go`, `album_lists.go` | Star/unstar songs, albums and artists; retrieve all three; ratings 0–5; update only after confirmed success |
| Artist and album information | `browsing.go` | Server biography/notes, safe external links, similar artists and top/similar songs; metadata absence is a valid empty state |
| Playback | `stream.go`, `transcode.go`; `ui/src/audioplayer/Player.jsx` | Stream, seek, pause/resume, previous/next, volume/mute, supported rate/format/bitrate settings; preserve actual playback state |
| Queue | `bookmarks.go`; `ui/src/actions/player.js` | Replace/play at index, append, play next, reorder/remove, shuffle/repeat, save/restore server queue and position; use index queue extension to preserve duplicates |
| Listening activity | `media_annotation.go`; `ui/src/hooks` | Report actual playback to Navidrome; no false listen on failed media open or seek; local recent history remains account-scoped |
| Lyrics | `media_retrieval.go`, `lyrics.go`; `responses/responses.go` | Retrieve supplied lyric variants, synchronized timestamps and offsets; useful empty/unsupported/error states |
| Playlists | `playlists.go`; `ui/src/playlist` | Create, view, rename, comment/public metadata, add/remove/reorder songs and delete; honor ownership and server smart-playlist constraints |
| Downloads | `media_retrieval.go`, `stream.go` | Actual original-file download/export, visible errors/cancellation; offline listening is a client capability and must use completed local files |
| Bookmarks | `bookmarks.go` | Save, list, resume and delete positions in milliseconds |
| Sharing | `sharing.go`, `api.go`; `ui/src/share` | Create/list/update/delete share links where server policy allows; preserve expiration and server-generated URL |
| Radio | `radio.go`, `api.go` | Play returned station URLs; an empty response is valid; station administration is role-restricted |
| Server player | `jukebox.go`, `users.go` | Use the returned jukebox role, remote queue/status and source-defined commands; queue offsets are seconds and gain is 0–1 |
| Scan/account | `library_scanning.go`, `users.go` | Report current user roles and scan status; only an administrator may start a scan |
| Optional extensions | `opensubsonic.go`, `sonic_similarity.go`, `transcode.go` | Negotiate extensions and handle unsupported operations; no server-name/version guessing |

## Explicit upstream boundaries

`api.go` registers podcasts and Subsonic user creation/edit/password changes with
`h501`, and chat/video/HLS/old search with `h410`. They cannot be required as working
Navidrome client functions. Jukebox, shares and sonic similarity depend on server
configuration/providers. User/library/transcoding/plugin administration in the web
application uses native admin APIs and is outside the music client's standard API scope.
Existing generic Subsonic video code is not evidence of Navidrome video support.

## Verification

The desktop home now routes to a Navidrome section page for albums, artists,
songs, search, genres, favorites, playlists, radio, listening activity, bookmarks
and shares. Album order/year/genre filters and the selected accessible music folder
are shared by browsing and search. Each search category keeps its own offset;
explicit play-all resolves the remaining pages instead of truncating at 100/500.

Detail pages show actual song/album/artist metadata, server information and related
music. Playlist ownership gates metadata edits; `readonly` independently gates
track add/remove/reorder. Favorite and rating actions await confirmed mutations.
Bookmark resume positions and duplicate server queue indices reach the native
player. Shares use server default expiration on creation and preserve it when an
update omits the date, matching `core/share.go`.

The real 0.64.2 fixture verifies 53 API integration assertions against 125 synthetic
tagged WAV files. Its version is recorded separately from the locked source.
Playback activity now reports actual native states and counts real playing intervals
independently of seek positions. Enhanced presence requests set `ignoreScrobble`;
qualifying submissions use persisted account-scoped occurrence IDs and UTC times.
The real fixture verifies that seeking to the end does not increase play counts.
Pending records retry after network failures. The standard scrobble API has no
idempotency key, so an accepted request whose response is lost can be resubmitted;
local occurrence deduplication does not claim exactly-once server delivery.
The Downloads section persists full song metadata, original files, optional compatible
MP3 copies, artwork and lyric variants under an account-specific app-local folder.
It validates completed file sizes, excludes partial files from playback and never
stores stream or artwork request URLs in catalogs. Browsing, playback and original
export work without `ConnectAsync`. Cancellation, retry, missing-file and corrupt
catalog paths are covered by the production service harness. Unsupported Windows 8.1
codecs request an MP3 stream; a transcoding failure retains the original for export
and disables offline playback with an explicit error. Download work is foreground
work and is interrupted if the process closes; incomplete files stay outside the
catalog.
The native music player now presents artwork/transport, queue entries and lyric
variants as separate media sections, recomposed vertically at narrow widths.
All music playback entry points navigate to it. It supports actual-position
scrubbing, volume/mute/rate controls, queue selection/removal/reordering, server
queue save/restore, bookmarks, favorites/ratings, playlist creation and local
queue import/export. Synchronized lyric lines follow media milliseconds and the
returned offset; manual lyric scrolling can disable following. Stale track/account
loads cannot replace current lyrics, and local playback reads cached album/artist
metadata and variants without connecting. The Settings flyout persists server
addresses, stream format and bitrate, migrates the older MP3 compatibility switch,
validates input and preserves saved settings with an explicit connection error
when offline. Queued songs use current quality preferences when playback starts.
An unopened HTTP song can retry once as MP3, with a visible compatibility state;
local/radio/other-account sources and already-playing failures do not start that
retry. Failed compatible opens retain both errors and do not create listening history.
The Account page shows actual server roles, version/capabilities and scan state;
only the returned administrator role enables starting a scan. Active scans refresh
while the page is open. Explicit reconnect refreshes roles and restores library
selection; optional extension/user errors remain visible without inventing support.
The radio page now exposes administrator-gated station creation, editing and
deletion. It validates HTTP(S) stream/homepage URLs, preserves full query strings,
and permits clearing an optional homepage. Acoustic discovery uses Navidrome's
`getSonicSimilarTracks` and `findSonicPath`, with a destination chosen from actual
current-account queue songs. An absent provider returns a real HTTP 404; the
client retains that failure instead of inventing recommendations. Account/page
changes cancel late forms and discovery results before they can alter playback.
The Account page also opens a separate server-player page when the returned
jukebox role permits it. Its queue, status, transport, seek, gain and queue
mutations use the server's audio device. Indexed commands recheck remote order;
relative skips and seeking also recheck the current index. Replace/clear recheck
after confirmation. Request generations prevent delayed polls from overwriting
newer operations, and remote gain refreshes preserve an unapplied volume draft.
The standard API supplies no queue revision or atomic conditional mutation, so
another client can still change the server between the final read and write.
Player frame state delegates queue serialization to the existing service and
retains account, playback milliseconds and lyric following. Position restoration
waits for media opening, clamps to duration, and applies only to the saved current
queue item; changing the selected item prevents a later unintended seek.
Protocol/model tests, simulated native audio events and controlled loopback
jukebox responses remain separate from actual native acceptance. The fixture
verifies station administration, an administrator-triggered scan, and explicit
disabled-jukebox/provider-absent failures; it does not supply an enabled server
audio device or acoustic provider. Native capture still times out through the
installed helper; no native interaction, decoding, background or visual pass is
inferred from successful source/build checks.

Random mixes expose the source-defined size (1–500), genre, optional lower/upper
year and accessible-library filters. Blank filters are omitted. Server activity
retains the reported player, age, playback state, millisecond position and rate;
its entries use full song models and can be played in their displayed context.
The page refreshes every 30 seconds while open and without a selection, stopping
on navigation so a background response cannot replace another page/account.

ReplayGain metadata reaches online and offline queue entries. Track/album modes
and a persisted −15 to +15 dB pre-amplifier follow Navidrome's decibel calculation
and reciprocal-peak clipping limit. Missing gain/peak stays neutral; explicit zero
remains distinct from missing data. MediaElement volume has a 0–1 range: the
effective volume combines user volume with the requested gain, and exceeding that
range produces a visible limitation without changing the saved user volume.
Actual native audio normalization remains unverified. Track details also retain
server display artist/album-artist/composer credits, BPM, comments, moods, ISRC
and content advisory values.

The [Navidrome acceptance specification](../../tools/navidrome/acceptance-spec.json)
adds these functional requirements to the preceding design/accessibility/resilience
checks. Its reference inventory must contain both `functionality` and `design`
authorities. Source tests, server integration, actual native interaction and visual
comparison remain distinct evidence; a passing compile cannot pass a native UI case.
The preceding `91e82a0` report (10/30 cases, 22.5%) describes its old scope only.
Seeking, downloads and artist discovery are required here, rather than optional
MixRadio roadmap credit.

The current specification contains 53 required cases, including separate server
player and acoustic-discovery cases. Run [verify.ps1](../../tools/navidrome/verify.ps1)
from a clean commit to build/deploy and bind fresh logs to that commit; service
mutation suites run sequentially against the disposable fixture.
[report.py](../../tools/navidrome/report.py) revalidates the original functional
and design references, bundles those logs and the current native observation, and
generates a Navidrome report using the existing evidence evaluator. It can pass
source/server checks while retaining native cases as blocked. Record actual native
interaction and screenshots through the normal evaluator workflow when available.
