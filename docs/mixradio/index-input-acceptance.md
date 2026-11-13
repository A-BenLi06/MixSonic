# Library index input acceptance — 2026-11-11

**Follow-up, 2026-11-14:** the two reported defects have been repaired and
targeted keyboard retests completed. See the fix verification below. The
2026-11-11 observations remain preserved as the original acceptance record.

**Result: partial acceptance; not an overall pass.** Actual keyboard input
completed the album, artist and song index paths. The final genre jump did not
produce a completed observation in two fresh-process attempts; Windows recorded
both processes as hung and terminated. Startup also exposes invisible error
report buttons to keyboard focus. These findings remain open.

## Method and source

- Product source: `e4859ccd0671cfb05c35b3298ae06840424a5246`.
- Windows 8.1 Store client, Debug x86, running on Windows 10 22H2. Target app:
  `11722CristiBadila-AgileFr.Subsonic8_nd4kycddfv9rg!App`; the separately running
  original Nokia MixRadio was not the input target.
- All navigation, selection and activation below used OS keyboard input through
  the computer-use `sky.press_key` API. Each action was followed by observation
  before another action was chosen. No production handler or automation peer
  was invoked to perform these interactions.
- A fresh `-NativeUiObserve` build included passive diagnostics. Requests name
  snapshots only. The observer records focus, page state, realized controls and
  actual XAML `RenderTargetBitmap` images. It does not navigate, select, focus,
  resize or invoke commands. Product browser code was unchanged during the run.
- Windows compositor capture failed with `FrameArrived timed out` / `window
  capture timed out`; the accessibility result was null. Thus the pictures are
  app-rendered XAML snapshots, not OS screenshots. No pointer coordinate mapping
  was inferred from them.
- Recorded viewport: 1920 × 1138. No root-size override was used. The configured
  library supplied 425 albums and 2,749 songs.

## Completed operations

Snapshot names below identify JSON and PNG files in the local evidence bundle.
They refer to observations after real input, not the earlier scripted probe.

| Case | Actual operation and observed result | Evidence |
| --- | --- | --- |
| Library entry | Tab to the visible Choose music command, Enter; random-song browser opened with 100 items. | `06-home-tab`, `07-open-library` |
| Category menu | Tab/Shift+Tab to category, Enter, then Home or Down; the 13-option popup opened and navigated to albums, artists, songs and genres. | `10-category-menu`–`11-albums`, `22-albums-category-menu`–`23-artists-open`, `30-artists-category-menu`–`31-songs-open`, `45-songs-category-menu`–`46-genres-open` |
| Album index | Ctrl+minus completed metadata loading from 100 to 425 albums; End focused `首`; Enter closed the index and focused the matching visible header. Selection count stayed zero. | `13-albums-focus`–`17-album-jump` |
| Header and return | Enter on the focused `首` header reopened the index at `首`; Escape closed it and restored focus to that header. | `18-album-header-reopen`, `19-album-escape` |
| Artist index | Ctrl+minus loaded 425 albums; End focused `黒澤ルビィ(CV.降幡愛) from Aqours`; Enter returned to that exact artist header and its albums. Selection count stayed zero. | `24-artists-page-focus`–`27-artists-jump` |
| Song index | Ctrl+minus loaded 2,749 songs; End focused `黑`; Enter returned to that header, with `黑暗力量` visible below it. Selection count stayed zero. | `32-songs-page-focus`–`36-songs-jump` |
| Media selection | Down focused `黑暗力量`; Ctrl+Space selected it. Ctrl+minus opened the index with one media item selected; Enter jumped to `陽`, retaining the visible check mark on the same song. | `37-song-media-focus`–`41-song-selection-after-jump` |
| Header exclusion | Ctrl+Space on the focused `陽` header left the media selection count at one. | `42-song-header-selection-rejected` |
| Genre index opening | Ctrl+minus loaded 425 albums and opened named genre tiles; End focused `Unknown genre`. This succeeded again after relaunch. | `47-genres-page-focus`–`49-genres-last`, `65-genres-retest-page-focus`–`67-genres-retest-last` |

Completed page observations report neither page loading nor page errors. That
does not establish success for actions whose observations never completed.

## Open findings

### Genre jump did not complete acceptance

After `49-genres-last`, Enter was sent to the focused `Unknown genre` index
entry. No completed `50-genres-jump` observation was produced. Refreshing state
without repeating Enter did not recover a post-jump observation. Windows
Application Hang event 1002, record 65801, at **12:20:12 +08:00** reports the
workspace client process terminated with hang type `Quiesce`.

The client was relaunched and the category was reached through real keyboard
navigation. After `67-genres-retest-last`, Enter again produced no completed
`68-genres-retest-jump` observation. Event 1002, record 65803, at
**12:32:19 +08:00** reports another `Quiesce` termination.

This repeated sequence fails the acceptance criterion of an observed successful
return to the genre group. It does not establish whether the root cause is
browser navigation, application suspension, capture instrumentation or their
interaction. A normal-build reproduction with usable external observation and
a hang stack is still needed before attributing a production defect. The
earlier scripted genre jump is not substituted for this failed real-input case.

### Invisible startup controls receive keyboard focus

On both launches, focus initially reported the error view's Settings button
while the home page was rendered. Tab then reached Send anonymously and Send
by email before reaching the player and visible Choose music command
(`01-home`–`06-home-tab`, `52-relaunch-home`–`56-relaunch-choose-focus`). No report
button was activated and no diagnostic data was sent. Keyboard navigation can
continue, but the invisible focus stops fail predictable startup navigation.

The shell embeds `ErrorDialogView` using a model binding plus an `IsHidden`
visibility binding. This observation alone does not prove which binding or
initial-focus lifecycle is responsible; no speculative product fix was applied.

## Evidence and limits

Local evidence is kept outside Git at
`.tmp/ui-acceptance/2026-11-11/library-index-input/`:

- `observer/20261111T033303Z/`: first process, including completed cases and the
  first genre attempt's last successful state.
- `observer/20261111T042150Z/`: relaunch and isolated retest.
  Its initial `51-genres-jump-state` file contains **home**, because the pending
  request name survived relaunch. It is not evidence of a genre jump.
- `application-hang-events.json`, `startup-crash.log`, observer build log,
  observer binary SHA-256, source patch and `manifest.json`.
- `normal-build.log` and `normal-build-verification.json`: ordinary-build
  restoration and diagnostic-code exclusion checks.

Some input and observation calls timed out. Later state was inspected before
retrying input; timed-out calls were never counted as successful by themselves.
The observer also logged two file-access failures writing its latest record;
named captures were retained, and later observations recovered those states.

Mouse/touch, wheel gestures, the Index app-bar command, disabled-letter keyboard
skipping, OS resizing, refresh/filter changes, selection during initial paging,
playback and lifecycle correctness were not accepted by this run. Pinch is not
implemented by the explicit index view. Existing global functional and visual
acceptance scores remain unchanged.

The ordinary Debug x86 build, deployment and launch completed successfully.
At **12:36:34 +08:00**, its mode marker was `normal`, the executable contained
neither the `NativeUiProbe` type nor `ObserveAsync` method name, and the workspace
process was responding. This restores the standard client; it does not resolve
or override the open input findings.

## Fix verification — 2026-11-14 (+08:00)

The saved follow-up dump at
`.tmp/ui-acceptance/2026-11-11/index-fix/genre-hang.dmp` and its symbolized
`native-hang-stack.txt` place the UI thread in
`ItemsControlAutomationPeer::GetItemPeerFromChildrenCache` during UI Automation
sibling traversal. The grouped projection reused the same album objects in
multiple artist/genre groups. Each named-group occurrence now has an independent
presentation identity and stable group key, with selection and commands mapped
back to the original media item. This keeps multi-genre albums in every group
without ambiguous item-peer identity or duplicate command targets.

The error overlay's visibility and enabled bindings now explicitly reference
`ShellRoot.DataContext.ErrorDialogViewModel.IsHidden`, with collapsed/disabled
defaults during attachment. Selection restoration also maps the original media
item back to all its displayed occurrences after a projection rebuild.

Real OS keyboard input used the computer-use `sky.press_key` API. The passive
observer performed no navigation, selection, focus or resizing. Completed JSON
and app-rendered PNG evidence is saved outside Git under
`.tmp/ui-acceptance/2026-11-14/index-fix/`.

| Retest | Observed result | Evidence |
| --- | --- | --- |
| Startup | Initial focus belongs to the shell; the first Tab reaches the visible Choose music control. No invisible report control precedes it. | `fix-01-home`, `fix-02-home-tab`, `fix-03-library` |
| Genre metadata | Ctrl+minus completes the 425-album listing and opens the index; End focuses Unknown genre. | `fix-17-genres-index`, `fix-18-genres-last` |
| Final genre jump | Enter closes the index and focuses the visible Unknown genre header. Reopen/Enter repeats the jump successfully. | `fix-19-genres-jump`, `fix-20-genres-reopen`, `fix-21-genres-repeat-jump` |
| Selection and return | Space selects the focused album. Ctrl+minus and Escape retain one selected album and restore header focus. A third Enter jump also retains the visible selection check mark. | `fix-24-genre-album-selection-state`–`fix-28-selected-genre-jump` |

The genre keyboard retests used the actual **670 × 770** app viewport, with no
root-size override. Three Enter jumps completed without a process hang. This is
targeted keyboard verification; mouse, touch and full application acceptance are
still unverified. Windows compositor capture still reports `FrameArrived timed
out`, and external accessibility capture returns null. The images are actual
XAML renders, not OS screenshots.

Snapshot requests `fix-07`, `fix-11` and `fix-14` did not complete after transient
observer file-access failures. Later uniquely named observations established
the current state before any further input. These failed observations are not
counted as successful actions. Pending/latest publication is now atomic, and an
unsuccessful snapshot remains retryable until the completed record is published.

The final scripted native probe completed **37 actual XAML captures**. All four
indices pass complete paging, final-group visibility, unique entry identities,
header selection exclusion, selection retention, and shared occurrence
selection/deselection. Named-group paging starts by selecting a displayed
occurrence and retains its canonical media selection across rebuilding. The
overlay is collapsed/disabled at startup, visible/enabled when shown, and
collapsed/disabled again after hiding.

Wide checks use **1744 × 1090** root dimensions; the extra genre jump uses
**1920 × 1138**, matching the original acceptance viewport. These scripted
root-size and navigation checks supplement the real keyboard retest; they do
not certify OS resizing or pointer/touch input. The corrected probe, PNGs,
initial probe results, final observer-branch compile log, and verification JSON
are preserved in the same local evidence folder. The initial probe's failed
named-group paging results are retained: it incorrectly attempted to select a
canonical source item outside the new displayed collection. The corrected
fresh-process run selects an actual occurrence and passes both paging cases.

The production home/navigation/index harness passes **76 assertions**. These
checks and the focused native results do not promote the global application
acceptance score or establish mouse/touch acceptance.

The ordinary Windows 8.1 Store Debug x86 package was rebuilt, deployed and
launched after verification. Its mode marker is `normal`, the executable
contains neither `NativeUiProbe` nor `ObserveAsync`, and the workspace process
responds. `normal-verification.json` records the timestamp, binary hashes and
process check. Source changes and verification are also recorded with a +08:00
timestamp in the root walkthrough.
