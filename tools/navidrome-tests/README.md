# Navidrome client verification

`run.ps1` compiles production shared API/model sources with MSBuild 14's C#
compiler. `check-client.py` checks desktop resources and project inclusion. Neither
runner claims native interaction or visual acceptance.

`./tools/navidrome-tests/run-home.ps1` compiles the production home controller,
section models and template selectors with controlled navigation, queue and
storage boundaries. A loopback HTTP peer supplies real protocol responses for
home previews. It checks all 13 destinations, section and tile dispatch,
independent section failures, disconnected/current-account downloads, stale
playback rejection, embedded player state forwarding and compact/featured
template selection. The library-index checks cover Unicode initials, disabled
letters, exact header targets, media ordering, multiple album artists/genres,
opaque artist IDs, localized fallbacks and rebuilding after filtering. Its 68
assertions do not exercise native pointer input,
XAML layout, audio decoding or the actual suspension lifecycle.

`./tools/navidrome-tests/run-connection.ps1` compiles the production route, session
and stream services against a controlled HTTP peer. It checks authenticated XML
responses, API-key account ownership, separate authentication mechanisms, JSON
transcode profiles, actual request cancellation and stale-account decisions.
This peer does not establish real API-key server support or native decoding.

`./tools/navidrome-tests/run-audio.ps1` compiles the production audio view model
and control contracts with simulated native events. It verifies persisted controls,
media-open/start/stop/seek ordering, queue completion, current stream preferences
and bounded MP3 compatibility retries. It neither decodes media nor verifies XAML
or background playback. Malformed saved controls must recover to valid defaults.
It also verifies position restoration before/after native opening and duration
clamping; event adapters do not establish actual suspend/resume behavior.

`./tools/navidrome-tests/run-jukebox.ps1` compiles the production server-player
page, operation base, API and local queue service. A controlled loopback HTTP
fixture verifies stale response suppression, remote order/current-index changes,
duplicate removals, delayed/cancelled forms, account isolation, volume drafts,
invalid indices, queue commands and explicit errors. Rendering, timer scheduling,
native lifecycle and audio on a real server device remain unverified.

The real-server fixture is separate from the functional source lock. This work
uses the official Navidrome 0.64.2 Windows amd64 archive, verified against the
release checksum file. Its extracted executable SHA-256 is
`F1E5C86DE256EE36EB74B4692EAB70055030DD37D53EC24C4F47E0EF7A0F914D`.
Keep the binary and all generated test data under ignored
`.tmp/navidrome-fixture`; do not vendor it or point mutation tests at a personal server.

```powershell
python -m pip install -r tools/navidrome-tests/requirements.txt --target .tmp/navidrome-fixture/python-libs
$env:PYTHONPATH = (Resolve-Path .tmp/navidrome-fixture/python-libs).Path
python tools/navidrome-tests/fixture.py .tmp/navidrome-fixture/test-music
./tools/navidrome-tests/start-fixture.ps1
./tools/navidrome-tests/run-integration.ps1
./tools/navidrome-tests/run-services.ps1
./tools/navidrome-tests/stop-fixture.ps1
```

For successful negotiated-stream integration, supply an explicit FFmpeg binary
under the ignored fixture folder with `start-fixture.ps1 -FFmpegPath <path>`.
The test setup used the Gyan FFmpeg 9.0.2 essentials release, checked against its
published archive checksum `60F467265B1E312373DBCD92200C2618A74850F98D3D078E94296BB3FA2047BA`.
The executable SHA-256 is `3256173F3F8BFFD7DF12227C68ADF68025EDB1832273A9530688A7BB1ED8EDEC`;
keep adjacent ffprobe available. The start script records the explicit FFmpeg
hash in the private fixture manifest. A server advertising `transcoding` without
the required FFmpeg setup fails the positive integration cases.

The start script binds to loopback, creates a fresh data directory and initializes
a disposable account only after verifying that its own process owns the listener.
The generated password stays in the ignored fixture manifest. The stop script
verifies the process, config path and binary hash and preserves diagnostic files.

Integration traverses 125 tagged songs beyond the first page, album/artist/genre
queries, all three favorites and ratings, complete playlist entries and duplicate
ordering, metadata/add/remove/reorder/delete, bookmark milliseconds, negotiated
server queue indices, lyric variants, original WAV download, shares, scan state
and playback report negotiation, including suppression of position-based false
scrobbles and byte-identical streaming downloads. The 65 assertions also cover
administrator station create/edit/delete, clearing a homepage, an actual rescan,
disabled jukebox access and the absence of an acoustic provider. Successful
acoustic response parsing and all jukebox action/parameter shapes use controlled
protocol responses; an enabled provider/server audio device needs native testing.
Negotiated direct WAV bytes must match the original download, and a 64 kbps MP3
decision must produce actual media bytes. This verifies server output, not native
MediaElement decoding.
Account avatars use the authenticated current-user endpoint; the fixture confirms
the real PNG placeholder returned when Gravatar is disabled.

`run-services.ps1` compiles the actual desktop session, offline library and playback
activity services with small CLR storage/event adapters. It uses the disposable
Navidrome fixture to verify completed catalogs, local queues, disconnected access,
cancellation, missing/corrupt files, retry, account isolation and pending-listen
synchronization. Synthetic binary responses cover the incompatible-original/MP3
fallback and failed-transcoding branches. These synthetic headers are not decoded
audio and cannot establish native playback or codec compatibility. WinRT storage,
the native MediaElement and the actual XAML/pickers still require native acceptance.

Navidrome's share behavior is explicit in `core/share.go`: a new share without
a custom date uses `DefaultShareExpiration`, and an update without a date preserves
the current expiration. The client must display the returned date, rather than
claiming that it created or cleared an unlimited share.

From a clean committed checkout, `./tools/navidrome/verify.ps1` runs the fresh
desktop build/deployment and all production checks, writes per-step SHA-256 logs
and binds `.tmp/navidrome-audit/verification.json` to the same commit at both ends.
The fixture must already be running. It does not launch or automate the client.
`report.py` takes this proof and generates the 38-function, 115-case Navidrome
report without MixRadio package paths. Native tasks default to `not_run`; an
optional schema-2 record may describe a current-commit native gap. Earlier capture
failures cannot certify the current source. Exit 2 is a valid report that is not
accepted. See [the functional acceptance guide](../../docs/navidrome/acceptance.md).

```powershell
./tools/navidrome/verify.ps1
python tools/navidrome/report.py
python tools/mixradio/quality.py --spec tools/navidrome/acceptance-spec.json evaluate --run '<new run directory>/run.json' --check-only
```

`check-client.py` now checks XAML namescopes and project-owned StaticResource
references as well as string lookups/project entries. SDK theme resources and
actual XAML loading remain the responsibility of the build and native acceptance.

Each production suite emits scoped `ND-CHECK:<suite>:<group>` markers only after
its named assertions finish. Integration also emits sanitized server-version and
extension facts. These markers map the 77 automated acceptance cases to specific
claims; disabled-provider/device markers do not certify successful optional
functions. Model serialization includes the actual production XML helper and
Navidrome contracts, including the concrete transcoding-reasons collection.

`python tools/navidrome/test_acceptance.py` runs 23 isolated acceptance-tool
tests for endpoint accounting, generated requirements, balanced request parsing,
missing/duplicate proofs, changed logs/code and historical native rejection.
The generic evaluator's own tests run separately. Native task completion and
MixRadio design comparison are independent of these tool self-tests.

The focused native playback/layout probe is available through
`./tools/desktop/run.ps1 -Architecture AnyCPU -NativePlaybackProbe` (use the
architecture of the registered development package). It creates an isolated
local PCM file and queue, exercises the production player view model and native
MediaElement, and renders the player at 1744, 900 and 500 logical pixels. Check
the fresh `LocalState/native-playback-probe/result.json` for `passed: true`;
successful build/deployment alone does not establish the probe result. The
checks cover held scrub previews, a single release commit, cancellation,
removed/stopped items, invalid positions, slider visual states and clipping.
It does not inject pointer input or certify seeking in server-provided codecs.
After verification, run the same command without `-NativePlaybackProbe` to
rebuild and launch the ordinary app without diagnostic code.
