# Navidrome reference and verification

Run from the repository root with PowerShell 7. Obtain and audit the pinned public source:

```powershell
./tools/navidrome/source.ps1
python tools/navidrome/build_spec.py
python tools/navidrome/inspect_source.py
python tools/navidrome/audit_functions.py
```

The clone and generated inventory stay under ignored `.tmp/`. To intentionally
change the functional baseline, update `source-lock.json`, inspect the new source,
revise the requirements and create a new acceptance run. Never reuse an old run
for different source, client code or reference bytes.

The protocol/source mapping is in [implementation.md](../../docs/navidrome/implementation.md).
The [functional acceptance guide](../../docs/navidrome/acceptance.md) explains the
38-function inventory and its 115 required cases. All 86 distinct endpoints
(91 route registrations) have explicit dispositions: 58 direct requests, nine
legacy alternatives, one fixed compatibility response and 18 upstream unavailable
endpoints. Source presence is structural coverage, not runtime proof.

Edit `features.json` when changing reviewed requirements, then regenerate with
`build_spec.py --write` and review `acceptance-spec.json`. `inspect_source.py`
hashes the locked source and handler registrations; `audit_functions.py` checks
endpoint accounting, client/source references and named test declarations.

Start the [disposable fixture](../navidrome-tests/README.md), commit completed
changes, and generate evidence from that clean commit:

```powershell
./tools/navidrome/verify.ps1
python tools/navidrome/report.py
python tools/mixradio/quality.py --spec tools/navidrome/acceptance-spec.json evaluate --run '<new report directory>/run.json' --check-only
```

Verification runs 14 checks and binds hashed logs to the same clean commit at
both ends. Mutating suites run sequentially. The report separates 77 automated
cases from 38 native tasks; current native observations are absent by default.
Exit 2 is a valid report with outstanding acceptance. No original MixRadio
packages or historical native records are required for this functional report.
The independent evaluator's `--check-only` mode preserves the function matrix;
ordinary `evaluate` writes a generic case report.

Do not store credentials in source, logs or reports. Disabled jukebox/provider
responses prove denial behavior only; enabled device/provider tasks remain
required. [MixRadio design tools](../mixradio/README.md) and optional
`inspect_source.py --design-index <path>` remain available for a separate design
inventory. Visual reference checks cannot establish Navidrome completeness.
