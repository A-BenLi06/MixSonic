# MixRadio reference and quality tools

Run commands from the repository root in PowerShell 7. Reference, screenshot,
log and report outputs belong under ignored `.tmp/`. Python 3.11+ is sufficient
for these standard-library tools; Pillow is not required. The desktop build
uses the existing Windows 8.1 SDK/MSBuild 14 toolchain. Node.js 18+ is needed only
for the isolated viewer script tests.

Read [the workflow](../../docs/mixradio/acceptance-workflow.md) for the fixture,
review and release process, and [the analysis](../../docs/mixradio/reference-analysis.md)
for source evidence IDs and capability mapping.

## Reference inspection and decompilation

```powershell
python tools/mixradio/inspect_reference.py `
  --xap '65.MixRadio_4.5.3.470.xap' `
  --installed-dir 'C:/Program Files/WindowsApps/NokiaCorporation.NokiaMusic_2.2.0.7933_x86__6d0q6r3z979nw' `
  --fetch-decompiler `
  --output '.tmp/mixradio-audit'
```

Alternatively, pass `--decompiler` with an existing `ilspycmd.dll` and `--makepri`
with the legacy SDK's `makepri.exe`. The downloader pins ILSpy 9.1.0.7988.
Unchanged assemblies reuse their SHA-256 cache; package inventories and XBF
diagnostic trees are refreshed. The tool reads original binaries without
loading their application code or changing WindowsApps permissions.
Use a new output directory when changing reference releases.

| Output | Purpose |
| --- | --- |
| `index.json`, `inventory.md` | Identities, per-file hashes, assembly/decompilation coverage |
| `wp/decompiled/` | Recovered phone C# and application XAML |
| `pc/decompiled/` | PC application, feature library and private music-library C# |
| `pc/xbf-metadata/` | XBF 1.0 diagnostic object/property trees; not buildable XAML |
| `pc/resources.xml` | MakePri resource dump |

The complete original packages and recovered source stay under ignored local
reference directories. The desktop component port intentionally checks in the
selected reconstructed XAML resources and eight original PNG assets requested
for the MixRadio UI. The [port map](../../docs/mixradio/native-port.md) identifies
their sources and the Navidrome adaptations; [ported-assets.json](ported-assets.json)
records the original package paths and SHA-256 hashes. Application secrets and
obsolete service endpoints are not part of this port.

## Reproduce the native component resources

With the PC XBF metadata from the inspection above available locally:

```powershell
python tools/mixradio/port_styles.py
python tools/mixradio/port_templates.py
```

These scripts regenerate `Client/Common/MixRadioOriginalStyles.xaml` and
`Client/Navidrome/NavidromeResources.xaml`. Both accept `--reference` to select
another metadata directory and `--output` to write a comparison file. The checked-in
resources and PNGs are sufficient for an ordinary client build; the reference
package and Python exporters are not runtime or build dependencies.

The generators retain recovered geometry, native control states and template
structure, while adapting bindings to the current Navidrome models. Native page
and control code implements navigation, playback and responsive layout. Resource
reproduction and matching asset hashes establish provenance, not rendered parity.

## Build and source regressions

```powershell
./tools/desktop/run.ps1 -NoLaunch
./tools/playback-tests/run.ps1
./tools/serializer-tests/run.ps1
```

The desktop runner writes `.tmp/desktop/build.log`, builds the requested
configuration/architecture, checks the appx identity, deploys a workspace
development layout and verifies the active registration. Omit `-NoLaunch` to
launch; use `-NoBuild` to deploy an already built matching package. It stops only
client processes with executable paths inside this workspace. Unchanged files
are skipped, and same-version development registrations are reused.

Supply `-MSBuildPath`/`MIXDROME_MSBUILD` for a different legacy MSBuild location;
the test runners accept `-CompilerPath`/`MIXDROME_CSC`. Both test runners compile
current production sources into `.tmp/` before testing; they do not inspect a
stale prebuilt common DLL. Queue exports use `.spls` XML, while session state
uses DataContractSerializer.

## Create an acceptance run

Complete and commit the source changes first. Each run gets its own directory:

```powershell
$acceptanceStamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$acceptanceRoot = Join-Path '.tmp/mixradio-acceptance' $acceptanceStamp
$runPath = Join-Path $acceptanceRoot 'run.json'
python tools/mixradio/quality.py init --output $runPath `
  --reference-index '.tmp/mixradio-audit/index.json'
```

The run starts with every case `not_run`. Its evidence bundle stores the
reference inventory, specification hash, Git commit, workspace state and
environment. Existing run files are not overwritten. Use `--spec PATH` **before**
the subcommand to select an intentional alternate specification.

Record a verified source check with its actual observation and log:

```powershell
python tools/mixradio/quality.py record --run $runPath --case QUEUE-SHUFFLE `
  --status pass --score 4 --reviewer 'Automated production-source runner' `
  --notes 'Complete cycles, final-item eligibility and queue boundaries passed.' `
  --evidence 'test_log=.tmp/mixradio-audit/playback-tests.log' `
  --evidence 'source_note=docs/mixradio/reference-analysis.md'
```

Use score 3 only when every criterion is met; score 4 also needs the specified
alternate-state/repeated-run evidence. Use `--status fail --score 0`, `1` or `2`
for an observed failure. A blocked observation has no score:

```powershell
python tools/mixradio/quality.py record --run $runPath --case HOME-WIDE `
  --status blocked --reviewer 'Capture observer' `
  --notes 'Native capture timed out; matching runtime frames were not obtained.' `
  --evidence 'runtime_log=.tmp/mixradio-audit/native-capture.log'
```

Evidence paths must exist. `record` copies content into the bundle and hashes
it; invalid records leave the saved run unchanged. Re-recording a case retains
previous bundled evidence and appends distinct new inputs. Inspect the listed
inputs when changing the observation; create a new run for a different commit,
specification or reference inventory.

## Produce and review a visual comparison

After obtaining actual app-content PNGs at the specified viewport and scale:

```powershell
python tools/mixradio/compare.py `
  --reference '.tmp/captures/pc-home-1366x768.png' --reference-kind runtime `
  --candidate '.tmp/captures/client-home-1366x768.png' --candidate-kind runtime `
  --output '.tmp/comparisons/home-wide.html' --title 'Home at 1366x768'
```

The HTML embeds both PNGs and has side-by-side, overlay, wipe, difference,
crop and Actual pixels controls. It needs no server or remote scripts. Its
adjacent `.json` starts unreviewed. A reviewer records observations and exports
`comparison-review.json` from the viewer. Input dimensions are preserved;
unequal crops disable composite comparisons and metrics.

Open the HTML directly in an ordinary browser. For browser automation that
does not allow `file://`, serve only the comparison directory over loopback:

```powershell
python -m http.server 8765 --bind 127.0.0.1 --directory '.tmp/comparisons'
```

Open `http://127.0.0.1:8765/home-wide.html`. Choose another unused port if needed;
Ctrl+C stops the server. The generated viewer remains a standalone HTML file.

Declare `mockup` or `synthetic` for those image types. They are useful for design
or tool tests, but the evaluator rejects them as runtime visual evidence. An
omitted reference creates a candidate-only inspection with missing-reference
provenance. Outputs cannot overwrite an existing comparison/review.

Bundle the reviewed decision and both exact screenshots:

```powershell
python tools/mixradio/quality.py record --run $runPath --case HOME-WIDE `
  --status pass --score 3 --reviewer 'Actual reviewer name' `
  --notes 'All home criteria observed at the recorded viewport; substitutions documented.' `
  --viewport 1366x768 --scale 1 `
  --evidence 'reference_screenshot=.tmp/captures/pc-home-1366x768.png' `
  --evidence 'client_screenshot=.tmp/captures/client-home-1366x768.png' `
  --evidence 'comparison_report=.tmp/comparisons/comparison-review.json' `
  --evidence 'manual_record=.tmp/captures/home-observation.json'
```

Viewport describes logical app content, excluding window chrome. PNG pixels must
match viewport × DPI scale; the wide cases require full-frame comparisons.
Images are checked for PNG CRC/compressed-data integrity and bounded to 128 MiB
of compressed and decoded data. The review must name its reviewer, include an
offset timestamp and match the bundled screenshot hashes.

The examples above require real files and an actual observation. They are usage
patterns, not pre-recorded passing results.

## Record defects and evaluate

```powershell
python tools/mixradio/quality.py defect --run $runPath --id HOME-001 `
  --case HOME-WIDE --priority P1 --status open --reviewer 'Actual reviewer name' `
  --notes 'Describe the reproduced discrepancy and expected behavior.'

python tools/mixradio/quality.py evaluate --run $runPath
```

Resolving a defect uses the same command with `--status resolved`, a resolution
observation and execution evidence (`test_log`, `manual_record`, `runtime_log`
or `video`). A bare resolved status cannot clear the gate. Fix the code and start
a new committed run when implementation changes are needed.

Evaluation writes `evaluation.json` and a local `report.html` with dimensions,
case procedures, observations, reviewer/time, evidence links and status filter.
Add `--check-only` to validate and print the current results without replacing
either artifact. This preserves an existing specialized functional report during
independent verification.
The score retains missing/blocked required cases in its denominator. All 30
required cases, every dimension minimum, the 85% overall threshold and absence
of unresolved P0/P1 defects are independent gates. Evidence tampering and
commit/workspace/specification/reference changes also prevent acceptance.

The report and its copied evidence can likewise be viewed through loopback:

```powershell
python -m http.server 8765 --bind 127.0.0.1 --directory $acceptanceRoot
```

Open `http://127.0.0.1:8765/report.html`. Keep the run directory intact so its
relative evidence links continue to work.

Exit codes for `quality.py`: **0** for a successful operation or accepted run,
**2** for a valid run that is not accepted, **1** for invalid input.
`compare.py` exits **0** when created and **1** on invalid input.

## Test the tools

```powershell
python -m unittest discover -s tools/mixradio -p 'test_*.py' -v
node tools/mixradio/test_compare_view.js
```

Python tests cover score anchors/rounding, evidence integrity, PNGs, viewports,
review provenance, path containment, clean commits, defects, HTML escaping and
CLI behavior. The JavaScript harness exercises the production viewer script
with known synthetic pixels, including exact difference statistics, crops,
overlays, wipe and exports. Neither certifies native app or browser rendering.

The viewer has also been exercised in the in-app browser over loopback HTTP:
all four modes, Actual pixels, crop bounds, unequal-size disabling, a 500px
browser viewport, review JSON download and difference PNG download. Those
checks used synthetic PNGs and verify the tool's browser behavior.

Earlier native app captures returned timeouts. Original/client runtime
comparison and native task acceptance remain unverified; neither synthetic
browser checks nor source regressions close those gates.
