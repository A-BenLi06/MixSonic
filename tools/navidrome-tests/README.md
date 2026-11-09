# Navidrome client verification

`run.ps1` compiles production shared API/model sources with MSBuild 14's C#
compiler. `check-client.py` checks desktop resources and project inclusion. Neither
runner claims native interaction or visual acceptance.

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
scrobbles and byte-identical streaming downloads.

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
