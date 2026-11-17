# Phone reference composition and acceptance

Recorded: **2026-11-17T14:37:38+08:00**.

This records the second composition pass against the eight screenshots supplied
on 17 November. It supplements [the original phone-port investigation](phone-port.md)
and supersedes that document's initial create-mix, favourites and offline feature
mapping. The reference is MixRadio; the implemented product remains Mixdrome,
using the configured Navidrome account and the existing WP8.1 WinRT project.

The supplied Store reference is [Microsoft Store product 9wzdncrfhwt4](https://apps.microsoft.com/detail/9wzdncrfhwt4?hl=en-US&gl=US).
Measurements below come from the supplied screenshots and recovered original
XAML. The Store link alone does not establish runtime behavior.

## Measurement method

All eight reference images are **768 × 1280**. Divide their pixel coordinates by
**1.6** to compare them on the original **480 × 800** design surface. Values below
are rounded design coordinates, not assumptions about WinRT device-independent
pixels. For example, a 207-unit card becomes 207 physical pixels on a 480-pixel
wide emulator screenshot even when that emulator's XAML content width is 400 DIPs.

Page geometry derives its scale from actual page width / 480. Native item-panel
cell sizing also uses the rendered content width; source arithmetic alone cannot
establish that two columns fit after native measurement and layout rounding.
Lists retain native scrolling and virtualization rather than placing the entire
app inside a scaled fixed canvas.

## Reference measurements

| Image | Surface | Key geometry at 480 × 800 |
| --- | --- | --- |
| 1 | Home with recently played content | Wordmark starts about `(24, 31)`. Large current-album tile starts `(24, 168)` and is 228 square. The 168-square Play Me tile starts about `(84, 408)`. Second column starts near `x264`; recent covers are 96 square, with staggered vertical positions. Normal gaps are 12. Recently played link sits near `(24, 750)`. |
| 2 | Home menu | First row starts `(24, 168)`, second column `x204`. Normal tiles are 168 square with 12 gaps. Favourites spans 348 × 168 at `(24, 348)`. My music/offline row starts near `y528`. Full-page artist photograph remains behind the tiles. |
| 3 | Mixes / featured | System status area is about 32 high, then the application title bar is 60 high. Large Pivot text starts near `y115`. Photo cards start `(24, 216)` and `(249, 216)`, are about 207 square, and have an 18 gap. Caption band is 60 high at 0.6 accent opacity; label text is about 20–21. Second card row starts near `y441`. |
| 4 | Play Me player | Same 32 + 60 top chrome. Artist photograph fills the page. Cover starts about `(24, 454)` and is 194 square; track metadata starts near `x230`. Feedback circles are about 44 diameter, centered near `x252/342/432`, `y618`. Progress starts near `(24, 654)` and spans 432. Pause/next circles are about 58 diameter, centered around `(180, 725)` and `(300, 725)`. |
| 5 | Offline mixes | Large title starts near `y115`; explanatory text near `(24, 223)`. First cards start `(24, 282)` / `(249, 282)` and use the same 207-square / 60-caption geometry. A downloading card adds a thin progress line around `y493` and status text below it; that added height affects the next row. |
| 6 | Like feedback | Pink panel is about 328 high. Bold title starts near `(24, 23)`; body starts near `(24, 93)`. Circular thumbs graphic is about 80 diameter at `(376, 68)`. Outlined OK button is about 204 × 48 at `(26, 256)`. The player beneath is dimmed. |
| 7 | Favourites / artists | Large Pivot heading starts near `y115`. First letter block starts `(24, 230)` and is 68 square. First artist photo starts `(24, 315)` and is 90 square. Name starts near `x126`, uses about 32 bold text and wraps. Photo rows retain about 12 spacing. Bottom bar has add/select commands and overflow. |
| 8 | Favourites / what's new | First image starts `(24, 221)` and is 120 square. Title/subtitle start near `x156`, using about 32 bold / 20 text. Normal image-row gap is about 12; a long title increases the row height rather than clipping its text. Original calendar/gig artwork describes an original service capability, not data that can be invented for Navidrome. |

Recovered source corroborates the 228/168/96 home tiles, 207-square mix cards,
60-high caption band, 68-square letter headers, 90-square artist pictures and
20/32 metadata typography. The reference has both a special Play Me presentation
and ordinary library/queue navigation; the special presentation is selected by
actual playback context rather than replacing all library behavior.

## Visual and data rules

- Ordinary menu tiles, favourite letter blocks, selected feedback circles and mix
  caption bands use the **system phone accent**. Yellow in the supplied reference
  is the reference device's theme. The acceptance emulator's blue accent is valid.
- Play Me and the feedback panel use the separate MixRadio pink reference color
  (`#FFFF2E80`). Pink is not the application's substitute system accent.
- Backgrounds use actual artist images returned by the configured server. With
  no usable artist photograph, the backdrop remains black. An album cover or a
  packaged music-note placeholder does not become a full-screen artist portrait.
- Cover/artist image failures use packaged foreground fallbacks. Collection image
  fallbacks update the bound item so later real artist metadata can still replace
  them. A failed initial request must not remove the image binding.
- Original names, concerts, notification counts and recommendation claims are not
  copied into the product. Labels use resources; metadata and identities come from
  real protocol responses.

The live fixture exposed a useful protocol distinction: `getArtistInfo2` returned
empty image values while `getArtist` supplied a usable OpenSubsonic
`artistImageUrl`. `ExpandedArtist` now retains that attribute. The phone adapter
uses it when artist information succeeds with an empty image URL. Unsupported
endpoints, account failures and network failures remain explicit errors; the
client does not infer support from English exception messages.

## Functional mapping

| Original interaction | Current Navidrome behavior |
| --- | --- |
| Home / recently played | Show actual current playback metadata, recent albums and server playlists. The menu keeps the reference's mixes/create/favourites/my music/offline arrangement. |
| Play Me | Request real random songs from the current server and start an actual mix queue. This does not imply OEM recommendation learning. |
| Mixes / featured | Show actual server playlists. A server without playlists can show actual recent albums. Opening a playlist preserves its complete entries, order and duplicate occurrences. A home playlist tile can pass its stable ID directly. Returning from the player does not automatically start it again. |
| Mixes / categories | Show real server genres with a cover from an actual genre song. Playback loads all genre-song pages; a later-page error does not start a partial mix. |
| Create a mix | Select a real genre, obtain up to 30 server-random library songs, save a named server playlist with `createPlaylist`, then play it. It does not reproduce the original commercial seed-artist service. |
| Favourites / artists | Show actual starred artists, grouped from their Unicode names and culture-aware ordering. Add performs a real artist `star`; multi-select removal performs real `unstar` requests. |
| Favourites / what's new | Filter server `newest` album pages by stable favourite artist IDs, including multiple-artist metadata. Entries retain server order. They represent additions to this library, not fabricated release dates, gigs or ticket notifications. |
| Favourites / albums and songs | Keep access to real starred albums and songs. Song play-all/shuffle uses the actual songs and the queue's shuffle state. |
| Like / dislike / favourite | Like writes rating 5, dislike writes rating 1 and can advance the actual queue, and favourite toggles the server song's starred state. Feedback copy describes that server operation and makes no personalization-learning claim. |
| Offline mixes | Long-press a real mix, resolve its actual songs, then enter the offline page. The service verifies account identity and download/stream roles, re-fetches song metadata, downloads a compatible MP3 copy and artwork, and atomically publishes a completed manifest. Queue occurrence order is retained while repeated files are stored once. |

Offline downloads are currently **foreground-page operations**. Leaving the
offline page or cancelling stops the current request and cleans incomplete
storage. This is not a background download service. Only complete manifests with
available local playback files appear as playable downloads. Storage is account
isolated, local queue files are validated before playback, and removing a saved
mix deletes its own local files. The PC offline service is not presented as a
phone capability merely because both clients share download primitives.

## Native evidence before the final layout pass

Local evidence directory:

```text
.tmp/ui-acceptance/2026-11-17/reference-composition/
```

These are emulator captures from the native WP8.1 XAML build. They establish the
observed flows below and also record defects that triggered further edits. They
are **not acceptance of the later layout/background/feedback changes**.

| Captures | What was observed | Remaining/revised interpretation |
| --- | --- | --- |
| `023-mixes-featured`, `024-mixes-categories` | Real playlist/genre metadata and photo cards render in the native page. | `023` exposed one column with unused right-hand space, heading text near `y125`, and first card near `y231`. The native panel now receives an explicit cell width from actual content width / 2; title spacing was tightened toward text `y115` and first card `y216`. Those edits require a new capture. |
| `025-mix-player`, `026-mix-player-ready` | A real mix opens the dedicated player presentation and resolves playable metadata/artwork. | Subsequent artist-image transition and feedback-composition refinements require the final package's own evidence. |
| `027-like-feedback`, `028-feedback-dismissed`, `029-paused` | Like feedback opens, OK dismisses it, and the pause interaction produces the corresponding native presentation. | A separate backend read confirms the rating mutation; a screenshot alone does not establish a server write. Final feedback styling is still to be recaptured. |
| `030-returned-menu`, `033-home-menu-paused`, `034-home-mini-player` | Navigation returns to the menu and current playback appears in the home/mini-player composition. | Final artwork transition and status-bar changes are subject to the last capture pass. |
| `031-favourites-artists` | Real artist JPEGs, a letter group, large names and elapsed shortcut (`0:05`) render successfully. | First group was at `y255` and first photo near `y341`, against targets `y230`/`y315`. Common heading spacing was reduced about 15, then group top spacing another 10. Photo-row spacing was retained. New positions are not yet verified here. |
| `032-favourites-updates` | Actual newest-library albums for the starred artists appear, with real covers and wrapped titles. | First photo was near `y231` against target `y221`. After the common heading reduction, this list receives 5 additional top units. New geometry is pending capture. |
| `035-favourites-selection`, `036-favourite-selected`, `037-favourite-removed` | Enter selection, select Fixture artist 1 and remove it from the visible favourites list. | The selection icon was subsequently replaced by a neutral three-line checklist path to match the reference. No backend count was sampled during the intermediate removed state. |
| `038-add-artist-dialog`, `039-add-artist-selected`, `040-artist-added-back` | The real candidate list opens, Fixture artist 1 is selected, and it is added back. | The read-only backend check below confirms the restored server state. |

### Backend and photo checks

All fixture records and media below are local acceptance data, not shipped
application assets or hardcoded product metadata. Reference photo/artwork regions
were extracted into the disposable media library, followed by a real full scan.
The server supplied the resulting IDs and image URLs.

Safe local summaries:

```text
.tmp/phone-emulator-fixture/reference-photo-fixture.json
.tmp/phone-emulator-fixture/reference-photo-artists.json
.tmp/phone-emulator-fixture/reference-collection-fixture.json
.tmp/phone-emulator-fixture/native-feedback-collection-backend-040.json
```

- Photo verification fetched each of the three actual artist image URLs and
  decoded JPEG responses: two at 600 × 519 and one at 331 × 235. Artist cover-art
  requests also returned JPEGs. The summary records format/dimensions and stable
  metadata without authenticated URLs or credentials.
- Fixture preparation created six real server playlists from actual album song
  entries: five with 25 songs and one with the two 90-second acceptance tracks.
  They all received server cover-art IDs. Three artists, two albums and two songs
  were starred for the native collection flows.
- The **read-only check at `2026-11-17T14:33:42.564080+08:00`**, after capture 040,
  found three starred artists including Fixture artist 1. Scanning all 127 songs
  found Fixture track 040 with `userRating = 5`, supporting the real like operation.
  This verifies the restored state, not an unobserved intermediate count of two.

Production-source checks passed for collection grouping/identity/full queues
(19 assertions), the phone connection/adapter path (38), artwork URI handling
(13), and serialization (33 model types plus explicit artist-image metadata).
The phone source/resource/package audit also passed. These checks supplement
native evidence and do not prove screenshot geometry, native image decoding or
offline playback on the emulator.

## Final native acceptance — root to complete after capture 041

**Pending.** The root agent will fill this section from the final installed
package. Do not promote corrected source geometry or earlier screenshots into a
passed native result.

| Check | Final screenshot / runtime evidence | Result |
| --- | --- | --- |
| Mixes has two columns; first cards near `(24,216)` / `(249,216)` | To fill from 041 onward | Pending |
| Favourites first letter near `(24,230)` and first photo near `(24,315)` | To fill | Pending |
| What's new first photo near `(24,221)`; wrapped rows remain readable | To fill | Pending |
| Checklist icon, native selection/removal/addition and preserved server state | To fill | Pending |
| Home/mini-player, artist backdrop transition, black fallback, player and feedback panel | To fill | Pending |
| Status bar hidden on home and shown on other destinations, including settings/library | To fill | Pending |
| Create-mix server playlist save and resulting playback | To fill | Pending |
| Offline progress, cancellation/cleanup, completed local playback and removal | To fill | Pending |

