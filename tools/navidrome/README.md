# Navidrome reference and verification

Run from the repository root with PowerShell 7. Obtain the pinned public source:

```powershell
./tools/navidrome/source.ps1
python tools/navidrome/inspect_source.py --design-index .tmp/mixradio-audit/index.json
```

The clone and generated inventory stay under ignored `.tmp/`. To intentionally
change the functional baseline, update `source-lock.json`, inspect the new source,
revise the requirements and create a new acceptance run. Never reuse an old run
for different source, client code or reference bytes.

The protocol/source mapping is in [implementation.md](../../docs/navidrome/implementation.md).
MixRadio analysis and image comparison remain available in [the design tools](../mixradio/README.md).

Use the extended functional and design specification with the existing evaluator:

```powershell
python tools/mixradio/quality.py --spec tools/navidrome/acceptance-spec.json init `
  --output .tmp/navidrome-acceptance/run.json `
  --reference-index .tmp/navidrome-audit/reference-index.json
python tools/mixradio/quality.py --spec tools/navidrome/acceptance-spec.json evaluate `
  --run .tmp/navidrome-acceptance/run.json
```

Use a disposable server/library/account for mutating integration cases. Obtain
IDs from its responses. Do not store user credentials in source, logs or reports.
Functional observations refer to Navidrome source/API behavior; original MixRadio
screenshots establish only design criteria. Required but unobserved cases remain
unverified in the report.
