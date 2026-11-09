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

The real 0.64.2 fixture verifies 40 API integration assertions against 125 synthetic
tagged WAV files. Its version is recorded separately from the locked source.
Playback activity, lyric presentation, download storage/offline UI and preferences
are the next desktop integration stage. Native capture still times out and the
client accessibility tree is empty through the installed helper; no native
interaction or visual pass is inferred from successful source/build checks.

The [Navidrome acceptance specification](../../tools/navidrome/acceptance-spec.json)
adds these functional requirements to the preceding design/accessibility/resilience
checks. Its reference inventory must contain both `functionality` and `design`
authorities. Source tests, server integration, actual native interaction and visual
comparison remain distinct evidence; a passing compile cannot pass a native UI case.
The preceding `91e82a0` report (10/30 cases, 22.5%) describes its old scope only.
Seeking, downloads and artist discovery are required here, rather than optional
MixRadio roadmap credit.
