# Navidrome client verification

`run.ps1` compiles production shared API/model sources with MSBuild 14's C#
compiler. `check-client.py` checks desktop resources and project inclusion. Neither
runner claims native interaction or visual acceptance.

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

The start script binds to loopback, creates a fresh data directory and initializes
a disposable account only after verifying that its own process owns the listener.
The generated password stays in the ignored fixture manifest. The stop script
verifies the process, config path and binary hash and preserves diagnostic files.

Integration traverses 125 tagged songs beyond the first page, album/artist/genre
queries, all three favorites and ratings, complete playlist entries and duplicate
ordering, metadata/add/remove/reorder/delete, bookmark milliseconds, negotiated
server queue indices, lyric variants, original WAV download, shares, scan state
and playback report negotiation, including suppression of position-based false
scrobbles and byte-identical streaming downloads. The 51 assertions also cover
administrator station create/edit/delete, clearing a homepage, an actual rescan,
disabled jukebox access and the absence of an acoustic provider. Successful
acoustic response parsing and all jukebox action/parameter shapes use controlled
protocol responses; an enabled provider/server audio device needs native testing.

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
`report.py` takes this proof, a current native observation and explicit original
MixRadio package paths; it retains native cases as blocked and generates a report
for the Navidrome specification. Exit 2 is a valid report that is not accepted.

```powershell
./tools/navidrome/verify.ps1
python tools/navidrome/report.py --phone-package '<original XAP path>' --desktop-package '<installed PC package directory>'
python tools/mixradio/quality.py --spec tools/navidrome/acceptance-spec.json evaluate --run '<new run directory>/run.json'
```

`check-client.py` now checks XAML namescopes and project-owned StaticResource
references as well as string lookups/project entries. SDK theme resources and
actual XAML loading remain the responsibility of the build and native acceptance.
