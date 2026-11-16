# Phone runtime refinement acceptance

This round uses the Windows Phone 8.1 **WinRT** x86 Debug application on the
WVGA 4 inch 512 MB emulator, with a 480 × 800 guest display. The design reference
is the recovered MixRadio 4.5.3.470 Silverlight XAML described in
[phone-port.md](phone-port.md). No matching original application runtime frame
was captured, so this is not a claim of complete visual parity.

Evidence is private/local under
`.tmp/ui-acceptance/2026-11-16/phone-refinement`; the run continued into
2026-11-17 (+08:00). PNGs are original SDK guest-display captures and WebPs are
compressed review copies. Each input is followed by an inspected capture.
Captures 15 and 36 contain navigation animations and are excluded from layout
acceptance. Capture 46 shows the Start screen after a delayed baseline run and
does not establish why the application left the foreground.

## Established before the player correction

| Case | Evidence | Result |
| --- | --- | --- |
| Responsive staggered home tiles, large label and empty recent slots | 22-final-home | Observed |
| Populated recent artwork without a title strip | 60-observer-connected, 61-observer-home-connected | Observed |
| Unfocused address/username remain readable; hardware Back closes keyboard | 26-final-url-blur, 29-final-user-blur | Observed |
| Artist title opens that artist's three actual albums; Back returns | 38-artist-title-navigation, 39-library-back | Observed |
| Album artwork play affordance in the upper-right corner | 40-library-albums | Observed |
| Album/artist direct play creates the actual queue | Private queue snapshots: 25 tracks, then two acceptance tracks | Observed; initial player UI was incorrect |

Settings Tab navigation from address to secondary address and username was
observed. The subsequent authentication-to-password transition did not focus
the password field; that segment is not accepted. Configuration used an
explicitly observed touch focus instead.

## Player failure investigation

The initial player showed “Nothing is playing” and a generic playback error
while native progress/transport and server listening records were active. A
temporary, opt-in diagnostic build recorded only exception types, HRESULTs and
native cancellation/error enums. It did not log messages, URLs or credentials,
or issue playback/navigation commands. The five instrumented source/project
files were restored using saved SHA-256 values before the ordinary rebuild.

The captured errors were in foreground `ApplyStateAsync`: one `COMException`
(`0x80004005`) and seven `ArgumentException`s (`0x80070057`). There were no
recorded background media failures, command exceptions, cancellations or
unhandled exceptions. The queue snapshot contained the expected two tracks.
These observations do not support attributing the error to a media codec.

The investigation identified five production defects:

- A higher-sequence Failure previously discarded an in-flight initial snapshot
  and returned without an authoritative queue state. Failure now carries the
  complete state, and the foreground commits the matching snapshot before
  presenting the failure.
- The foreground set a selected index on the deferred queue Pivot before its
  native Items collection was ready, blocking the subsequent metadata update.
  Metadata/transport are now independent of selection; Loaded and public
  `Items.VectorChanged` notifications synchronize only a matching displayed
  occurrence. Unloaded/departure detach the observer.
- Cover-art IDs are opaque Subsonic values. Navidrome IDs containing a colon
  were accepted as arbitrary URI schemes and passed to `BitmapImage` instead
  of the cover-art endpoint. Only supported HTTP/application image URIs are
  retained directly; all other values use the existing encoded cover-art API.
- Progress was read while the native player was not ready, and an initial
  failed state update could prevent the timer from starting. Polling now starts
  immediately after the ready handshake. Closed/opening/buffering states do not
  read unavailable progress properties; only the observed `E_FAIL` concurrent
  transition into such a state is treated as not-ready, without hiding other
  errors or actual media failures.
- A successful cover-art HTTP response can still contain an image the legacy
  native decoder cannot display. The fixture's no-art response is exactly the
  locked server's embedded `album-placeholder.webp`; `format` is not a supported
  endpoint parameter and PNG/JPEG Accept headers do not negotiate it. Decoder
  failure therefore uses the existing packaged PNG fallback for the current
  bitmap only, without changing playback or allowing an old image failure to
  overwrite newer artwork. This does not add a general WebP decoder.

The CLR tests compile the actual production methods. The earlier Failure method
fails the newer-failure snapshot case; the pre-selection-fix method throws in a
strict uninitialized ListView fixture. The corrected methods pass both sets.
Additional tests execute the production artwork resolver and initialization/
progress methods, including opaque colon IDs and strict not-ready native-property
getters. Managed stubs do not substitute for the ordinary native retest.

The private fixture contains the original 125 six-second PCM tracks plus two
same-source 90-second MP3/PCM tracks. Read-only server checks found qualified
`submission=true` playback counts for all 25 tracks in the first album and for
both long acceptance tracks. These are not browse records or injected
scrobbles. They establish native observed-listening submissions, not audible
speaker output or lock-screen/interruption acceptance.

## Ordinary-package interaction retest

The normal x86/ARM rebuilds exclude the diagnostic methods and log filename in
both foreground and background binaries. The following cases used ordinary
packages and SDK touch/Back input, not product event-handler invocation:

| Case | Evidence | Result |
| --- | --- | --- |
| Metadata before first queue-Pivot visit; no false playback error | 98-final-playing, 99-final-playing-progress | MP3 title/artist displayed; progress advanced to 0:19 |
| Pause retains position | 100-final-paused, 101-final-paused-stable | 0:52 remained unchanged |
| Seek while paused | 102-final-seek-paused | Native position changed to 0:30 and remained paused |
| Resume and natural next track | 103-final-resumed, 104-final-about | MP3 resumed, then PCM metadata and 90-second duration appeared |
| First queue visit and row activation | 105-final-queue-first-open, 108-final-queue-select-first, 109-final-queue-first-paused, 110-final-queue-metadata-match | Two real rows; selecting MP3 restarted it; paused at 0:40 with matching title and next enabled |
| Complete-library shuffle | 113-shuffle-songs, 115-shuffle-playing-settled | Shuffle accent active; real PNG cover/background and current song rendered |
| Unsupported default cover on the final ordinary package | 130-accepted-fallback-playing, 131-accepted-paused, 132-accepted-paused-stable | Packaged PNG and metadata visible; progress advanced, then remained paused at 1:05 without a playback error |

Read-only inspection of the native Shuffle all snapshot found **127 tracks and
127 unique IDs**, exactly matching the actual six-album library. Independent
100+27 protocol requests were stable and had no overlap, missing or extra IDs.
The server uses natural row order for this query, so unsorted titles are not
evidence of random pagination. Comparisons are saved under private
`.tmp/phone-emulator-fixture/*-comparison.json` and `song-paging-stability.json`.

The final managed regressions comprise 221 assertions: connection 33, artwork
13, library 39, queue 38, player state/lifecycle/artwork failure 47, listening 36
and search 15. Initial connection/library/listening failures are retained
alongside unchanged successful reruns. A new envelope assertion's own race was
also corrected by observing the message under the same command gate used by
production, without changing product synchronization or timeouts. Native build
and CLR results do not extend the accepted runtime cases beyond the table.

## Remaining scope

Original/client matching runtime frames, additional display sizes, grouped
letter jumps, the home mini-player transformation, real-user server integration,
lock-screen/system transport and interruption recovery remain separate checks.
No global acceptance score is changed by this round.
