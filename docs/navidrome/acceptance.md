# Navidrome desktop functional acceptance

The functional authority is the clean Navidrome source pinned by
[source-lock.json](../../tools/navidrome/source-lock.json). MixRadio supplies a
separate component, layout, UI and design reference. Its package files and
screenshots are not prerequisites for functional verification.

The target is the Windows 8.1 Store desktop client, Debug x86. Windows Phone UI
and Navidrome's native web administration APIs are separate scopes.

## Reviewed inventory

[features.json](../../tools/navidrome/features.json) defines 38 functions, their
source/client mappings, scoped automated claims and native prerequisites, steps
and expected results. The source audit accounts for all 91 route registrations
and 86 distinct endpoints: 68 implemented endpoints comprise 58 directly
requested endpoints, nine documented legacy alternatives and one fixed valid
license response. The other 18 endpoints use upstream `h501` or `h410` handlers.

The inventory covers authentication, capabilities, libraries, album lists,
artists, search, genres, metadata, covers, avatars, favorites, ratings,
information, related songs, random mixes, transport, quality, transcoding,
ReplayGain, local/server queues, listening/activity, lyrics, playlists,
downloads, bookmarks, shares, radio/administration, scanning, accounts,
jukebox, acoustic tracks/paths, native recovery and functional access.

Legacy alternatives have explicit reasons and implemented replacements.
Navidrome's `getMusicDirectory` builds virtual artist/album directories; ID3
detail navigation covers that function. Its locked `getUsers` handler returns
only the logged-in user; `getUser` covers that account function. A fixed valid
license response does not introduce a license-management task. Upstream
unavailable endpoints do not become working client requirements.

Conditional sharing, jukebox and acoustic routes remain in the inventory.
Disabled-device/provider errors prove the stated denial behavior only. Positive
server-device playback and acoustic results require the enabled configuration
and actual observations specified for those native tasks.

## Evidence layers and acceptance

[acceptance-spec.json](../../tools/navidrome/acceptance-spec.json) is generated
from the reviewed inventory. It contains 115 required cases: 77 automated cases
and 38 native functional tasks. Four dimensions retain separate evidence:

| Layer | What it establishes | What remains separate |
| --- | --- | --- |
| Source/protocol | Pinned bytes, complete endpoint accounting, request/response contracts, build and source resources | Actual page rendering and native execution |
| Production service/control | Production logic under controlled HTTP, storage and media-event adapters | Native storage, decoding and lifecycle |
| Real server | Actual API behavior and returned media from the disposable Navidrome fixture | Native decoding and successful optional providers/devices |
| Native task | Observed registered-app interactions, playback, recovery, keyboard and narrow views | MixRadio visual fidelity |

Source/request presence is structural coverage, not runtime proof. Named
`ND-CHECK:<suite>:<group>` log markers certify only the claims assigned to that
group. The report snapshots source mappings and client bytes and copies hashed
logs into the run. Sanitized integration facts record the actual server version
and advertised extensions; the server release is separate from the source lock.

A score of 3 means all stated criteria hold at that case's declared layer.
The overall threshold is 75, with a minimum score of 3 in every dimension and
every required case passing. This replaces the older combined functional/design
scoring; results from those older scopes cannot be compared as the same metric.
A passing build or automated case cannot promote a native task into a pass.

## Reproduce verification

Use PowerShell 7 from the repository root. Prepare and start the disposable
fixture as described in [the test guide](../../tools/navidrome-tests/README.md).
Keep its generated account, binary, music and private manifest under ignored
`.tmp/navidrome-fixture`; mutation suites must run sequentially.

```powershell
./tools/navidrome/source.ps1
python tools/navidrome/build_spec.py
python tools/navidrome/inspect_source.py
python tools/navidrome/audit_functions.py
```

After intentional inventory edits, use `build_spec.py --write`, review the
generated specification, update `walkthrough.md` and commit the completed batch.
Generate final evidence only from that clean commit:

```powershell
./tools/navidrome/verify.ps1
python tools/navidrome/report.py
python tools/mixradio/quality.py --spec tools/navidrome/acceptance-spec.json evaluate --run '<new report directory>/run.json' --check-only
```

Verification performs 14 checks, builds/registers the desktop app without
launching it, and records clean code state at both ends. It validates the owned
fixture process and binary and writes hashed logs and
`.tmp/navidrome-audit/verification.json`. Report generation verifies these bytes,
the reviewed inventory, locked reference and current commit before bundling a
new preserved run. Exit 2 means a valid report with outstanding acceptance;
exit 1 indicates an error.

The independent evaluator's `--check-only` option verifies the run without
replacing the generated function matrix or stored evaluation. Its normal
`evaluate` command writes a generic case report, which remains useful after
recording later native results.

## Observe native tasks

Without current observations, native tasks default to `not_run`. Historical
capture failures do not establish the current app's runtime state. An optional
`report.py --native-record <path>` may describe current-commit `not_run` or
`blocked` conditions using schema 2 and `nativeVerified: false`; it cannot certify
success or reuse a record from another commit.

Follow each task's prerequisites and steps in the generated specification.
Record actual observations in the preserved run using the generic evaluator's
evidence and result commands. Native cases require the source note, a manual
record and a native runtime log; screenshots/video can supplement those records.
Keep credentials and authenticated request URLs out of evidence. Re-evaluate
after recording results. A run with 77/77 automated cases passing and 0/38 native
tasks accepted remains **not accepted**.

Use [the MixRadio design workflow](../mixradio/acceptance-workflow.md) for separate
visual comparison. Functional navigation and accessible task completion remain
required by this Navidrome checklist.
