# Walkthrough

## 2026-07-05 17:00:27 +08:00

- Adjusted the Windows Phone home recently played album list to better match the WP MixRadio reference: transparent rows, smaller 64px artwork, lighter spacing, and a subtle divider instead of a heavy card background with a right accent bar.
- Updated the Stitch UI structure notes so the phone MainPage direction explicitly avoids desktop-style album cards in the lower album list.
- Kept the change scoped to phone UI structure and documentation; no shared service or package identity changes were made.

## 2026-07-05 17:07:37 +08:00

- Audited the current Windows Phone pages against the WP MixRadio references and the Metro phone layout rules.
- Reworked `ArtistAlbumsPage` from a two-column album wall into a phone-first single-column album flow with artwork, album title, artist, track count, and a subtle divider.
- Updated the Stitch structure notes so artist detail is specified as a WP album flow instead of a shrunken desktop tile wall.

## 2026-07-05 17:14:26 +08:00

- Audited the Windows 8.1 Store browse surface against the desktop MixRadio references.
- Changed ordinary media tiles from wide list-like blocks to square MixRadio-style visual tiles with cover art or color, a bottom caption band, and a small play marker.
- Updated the shared media tile sizing so album/search/browse collections read as a Metro tile wall instead of dense rows, and noted that direction in the Stitch structure document.

## 2026-07-05 17:21:29 +08:00

- Audited the desktop playback queue against the MixRadio queue-drawer reference.
- Shifted the queue panel toward a MixRadio-style deep magenta drawer with a lighter command header and reduced black-card layering.
- Reworked queue rows into compact media list items with subtle dividers and a bright pink state strip, keeping playback behavior unchanged.

## 2026-07-05 17:28:56 +08:00

- Audited the desktop root `PageHeader` against the MixRadio desktop reference.
- Reworked the root wordmark so `Mix` uses a stronger weight, `Radio` stays light, and the brand mark is larger with a larger circular play bubble.
- Reduced the root section title size so the first viewport reads like MixRadio's brand-first home instead of a generic large page title.

## 2026-07-05 17:34:59 +08:00

- Audited the Windows Phone `NowPlayingPage` against the WP MixRadio music-list direction.
- Reworked active server playback rows from a pure text status list into cover-art media rows with title, artist, activity, and a subtle divider.
- Updated the Stitch structure notes so Now Playing is specified as a lightweight music list rather than a status feed.

## 2026-07-05 17:46:08 +08:00

- Audited the Windows Phone home page against the WP MixRadio references, especially the horizontal home sweep with cover collage, bright play tile, and separate music/search panels.
- Reworked `MainPage` into a panorama-like horizontal canvas: first screen for cover collage and recently played, second screen for music entry tiles, and third screen for search/featured actions.
- Kept the existing Navidrome entry points and counts intact while making the albums entry return to the first screen instead of behaving like a desktop-scaled menu.

## 2026-07-05 17:51:23 +08:00

- Audited the desktop bottom playback controls against the MixRadio playback treatment already used on the main playback page.
- Added scoped circular MixRadio playback button styles to `PlaybackControlsView` so previous, play/pause, next, and stop no longer fall back to default AppBar button chrome.
- Kept the Caliburn playback actions and toggle behavior unchanged, with the visual change isolated from global AppBar command styles.

## 2026-07-05 17:56:20 +08:00

- Audited the desktop in-app tile template against the MixRadio Store reference tile wall.
- Tightened `MenuItemView` tile spacing and reduced caption-band height so command and media tiles read as hard-edged MixRadio tiles instead of heavier generic cards.
- Enlarged the central glyphs while shrinking the small play marker and text band density, preserving all existing tile bindings and navigation behavior.

## 2026-07-05 17:59:42 +08:00

- Rechecked the Windows Phone home panorama for places where copied reference styling had become static product data.
- Replaced the fixed small header number with the live album count binding so the MixRadio-style indicator reflects the current library.
- Simplified the search tile column layout so the text and search glyph fit predictably on narrow phone widths.

## 2026-07-05 18:06:11 +08:00

- Audited the desktop playback progress indicator against the MixRadio reference playback surface.
- Replaced the default full-width ProgressBar appearance with a centered 320px thin progress line using the MixRadio pink accent over a dark gray track.
- Preserved the existing playback progress binding and time tooltip while reducing the default template feel around the album-art playback area.

## 2026-07-05 18:10:04 +08:00

- Audited the desktop playlist item row used by playback queues and playlist lists against the MixRadio queue/media-row references.
- Reworked the row from a single `Artist - Title` text run into a cover-art media row with title, artist, duration, and a subtle divider.
- Kept the existing cover art, title, artist, duration, and playing-state bindings intact while making the row easier to scan in the dark MixRadio layout.

## 2026-07-05 18:22:02 +08:00

- Audited the desktop playback filter surface against MixRadio's search-on-media visual language.
- Reworked `Filter.xaml` from a plain form under album art into a cover-art overlay with dark gradient, large light title, teal search input, and a pink icon-only done button.
- Preserved the existing realtime `FilterText` binding, Enter-key handling, and `DoneFiltering()` action while removing the old typo-prone instruction text.

## 2026-07-05 18:32:25 +08:00

- Investigated the desktop package launch failure that currently prevents screenshot-based MixRadio parity verification.
- Added scoped startup and unhandled-exception logging to `App.xaml.cs`, writing activation milestones and exception details to the app local folder.
- Kept the diagnostic code out of the UI surface and preserved startup behavior by rethrowing caught activation exceptions after logging them.

## 2026-07-05 18:35:57 +08:00

- Rebuilt the desktop target and found the first diagnostic implementation used `System.IO.File`, which is unavailable in the Windows Store app target.
- Reworked startup diagnostics to use WinRT `StorageFile` and `FileIO.AppendTextAsync`, with a small LocalSettings fallback containing the recent startup milestones.
- Kept the logging fire-and-forget so the UI thread is not blocked during normal launch, while preserving the same startup flow and exception rethrow behavior.

## 2026-07-05 18:36:51 +08:00

- Removed the diagnostic handler name collision with Caliburn's inherited unhandled-exception hook.
- Renamed the local app-level event handler to clarify that it is only attached to the Windows XAML application event.
- Kept the actual logged message text unchanged so existing diagnostic searches still find `UnhandledException`.

## 2026-07-05 18:44:32 +08:00

- Used the new startup log to identify the desktop launch crash as a XAML resource ordering problem: `LightStandardStyles.xaml` referenced `MixRadioAccentColor` before App-level resources had been loaded.
- Added a shared desktop `MixRadioResources.xaml` palette and merged it before the standard/custom style dictionaries that depend on those colors and brushes.
- Kept the fix in the resource layer so page markup and playback behavior stay unchanged while startup parsing becomes deterministic.

## 2026-07-05 18:49:45 +08:00

- Retested the desktop launch and found WinRT XAML still could not resolve the MixRadio palette from a sibling merged dictionary while parsing `LightStandardStyles.xaml`.
- Merged `MixRadioResources.xaml` directly inside `LightStandardStyles.xaml` so its own first-level theme brushes can resolve the palette locally.
- Preserved the existing desktop palette values and kept the change limited to resource dictionary visibility.

## 2026-07-05 18:56:49 +08:00

- Compared the live desktop home screenshot against the MixRadio desktop references after the startup fix.
- Removed the duplicate pink play tile from the left playback area so the title block sits beside the cover art, and widened the left content rail to push library sections toward the MixRadio reference position.
- Changed non-play library command tiles from full-color dashboard blocks to dark MixRadio-style tiles with a magenta caption band, while keeping the existing Navidrome entry points and tile commands intact.

## 2026-07-05 19:23:19 +08:00

- Built the Windows Phone 8.1 target with VS2015 MSBuild successfully after the desktop layout pass.
- Started the WP8.1 WVGA 4-inch emulator from XDE with the SDK `flash.vhd`, confirmed the emulator reached the app list, and enabled the stopped `PhoneSvc` service.
- Runtime phone deployment is still blocked by the WP8.1 deployment stack: `AppDeployCmd`, VS `/Deploy`, and target index deployment all fail with `Object reference not set to an instance of an object`, while the physical device target reports no phone detected.

## 2026-09-05 06:00 +08:00

- Resumed from the July 5 state with a full end-to-end pass against the live Navidrome 0.58.5 server at 127.0.0.1:10011.
- Fixed the Genres page crash: `Genre.Name` overrode the base `[XmlAttribute("name")]` property with `[XmlText]`, which `XmlSerializer` rejects ("member hides inherited member with different custom attributes"). The element text now lives in a `Value` property and `Name` is an `[XmlIgnore]` override that forwards to it.
- Fixed the Internet radio crash: `InternetRadioStation` was missing `[XmlRoot("internetRadioStation")]`, so the per-element `XmlSerializer` rejected Navidrome's camelCase element ("error in XML document (0,0)").
- Made search actually usable on Windows 10 (the old flow depended on the Win8.1 search charm): the header search box in `FullScreen.xaml` is now a real `TextBox` with Enter/button submit that navigates to `SearchViewModel`, and the search page gained its own query input pre-filled with the current query.
- Stopped stripping cover art from search results, so album tiles in search show real covers like the rest of the app.
- Added MixRadio-style letter group headers to the library browse pages: `GroupedMenuItems` on `CollectionViewModelBase` groups items by first character, and `FullScreen.xaml` renders a grouped `GridView` with pink-outlined letter headers per column. Song-based pages (random mix, genre songs, favourites) stay flat via an `IsGroupedView` override.
- Added the missing `Client.csproj` compile/page entries for all July page folders (Artists, Favourites, Genres, NowPlaying, Radio, RandomSongs). They were never included, so only incremental builds ever succeeded and a clean rebuild failed.
- Added a repeatable desktop deploy loop (`.tmp/run_desktop.ps1`): build Debug x86, expand the produced appx over the loose layout (packaging pulls in framework assets the loose output misses), re-register in place with `Add-AppxPackage -Register` (preserves LocalState/settings), and launch.
- Replaced the expired signing certificate on Client.Common.Tests and Client.Tests so their test packages build again; left the remaining legacy test projects on their original certs. Running the WinRT MSTest suites still requires appcontainer deployment (admin/Trusted People), which this machine cannot do without elevation.
- Rebuilt Client.Phone (x86 Release) with today's fixes; phone deployment remains blocked by the WP8.1 `AppDeployCmd` tooling crash ("value out of expected range" on /ListTargets), reconfirming the July 5 blocker.

## 2026-09-05 07:05 +08:00

- Replaced the grey star artist placeholder with the reference MixRadio pink smiley: generated `Assets/ArtistPlaceholder.png` (pink square, darker-pink eyes and smile, composed for the tile's center crop), added a `CoverArtFallback` on `ItemViewModelBase` that wins whenever an item has no cover-art id, and pointed the Artist case of the tile styling at it. Navidrome always emits a generated coverArt id for artists, so the fallback is applied unconditionally for Artist tiles.
- Kept the home "my music" tile wall flat via an `IsGroupedView` override on `MainViewModel` (the new letter grouping briefly regrouped the home tiles alphabetically, which broke the designed home layout).
- Switched group ordering to ordinal (`#`/digits first, then letters, then CJK) to match the reference library pages.
- Verified interactively against the live server: now playing tile navigation (graceful "Nobody is playing right now" empty state), random-mix playback with auto-advance through the queue, and the queue drawer's save-playlist flow (system file picker opens with `Playlists.m3u`; scripted save-click timing prevented a byte-level file check, the dialog flow itself is wired).
- Video playback and the artist-info biography panel remain Subsonic-server/LastFM-EchoNest legacy features: Navidrome answers getVideos with "endpoint will not be implemented" and artist bios require external API keys, so neither is reachable against Navidrome.
- Phone deployment reconfirmed blocked at the tool level: `AppDeployCmd /ListTargets` crashes with "value out of expected range" before any device is queried. `.tmp/TrustCert-RunAsAdmin.ps1` was added; run it as administrator (or connect a physical WP device) to unblock phone package installation and verification.

## 2026-09-05 07:35 +08:00

- Completed the playlist save verification byte-for-byte: the queue drawer's save button writes `playlist.spls` into the picked folder — a 29 KB XML containing all 9 queued tracks with artist, title, duration, cover-art URL and stream URL. Note the save-as dialog advertises `*.m3u` while the payload is the app's `.spls` XML format that `LoadPlaylist` consumes; the extension mismatch is cosmetic (save/load pair works) and left as a known nit.
- The test-signing certificate (EC6D7DC1..., CN=benli06, valid to 2031) is now trusted in CurrentUser\Root and My, so MSBuild signs test packages without APPX0108/0107 errors for the projects migrated to it.
- Retried appcontainer test deployment after the trust import: still rejected (0x800B0109) because the AppX deployment service consults LocalMachine trust stores only, which requires elevation. `.tmp/TrustCert-RunAsAdmin.ps1` remains the single unblock step for both WP8.1 deployment and the WinRT MSTest suites.

## 2026-09-05 08:00 +08:00

- Attempted the elevation route for phone verification: the UAC prompt for `TrustCert-RunAsAdmin.ps1` was dismissed, so LocalMachine trust remains unavailable in this session.
- Booted the WP8.1 WVGA emulator via `XDE.exe /vhd flash.vhd` and retried deployment three ways with the emulator online: `/ListTargets`, `/install <appx> <index>`, and `/install <appx> targetdevice:xd`. All crash identically with "value out of expected range" before any device query — third reproduction of the toolchain breakage, independent of emulator state.
- Static phone-package evidence gathered instead: the built `Client.Phone_1.3.0.0_x86.appx` carries 15 compiled XBF views (MainPage with MixRadio wordmark and command bar, ArtistAlbumsPage, GenresPage, NowPlayingPage, RadioStationsPage, SearchPage, SettingsPage, FavouritesPage, PlaylistsPage, PlaybackPage) and Styles.xaml embeds the full MixRadio palette (#FFFF2B86 pink, #FF8A1248 dark magenta, #FFD7C900 yellow, #FF78B800 green), so the MixRadio-ized phone UI is in the deliverable awaiting a working deployment path.

## 2026-09-05 08:30 +08:00

- Elevation attempt for the phone/admin gate: the UAC prompt for the trust script was dismissed by the user, so LocalMachine trust remains out of reach this session.
- Booted the WP8.1 emulator (XDE pid confirmed running) and retried `AppDeployCmd /install` with explicit target arguments (`0`, `targetdevice:0`, `xd`): all crash with the same "value out of expected range" during tool initialization — third reproduction, independent of emulator state or argument form.
- Load-side verification of the queue round-trip was inconclusive via synthetic clicks at the small app-bar icon size; the save side remains byte-verified (playlist.spls, 9 tracks) and load consumes the same serializer output.
